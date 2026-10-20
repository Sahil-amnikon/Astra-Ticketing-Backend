using System.Net;
using Astra.Api.Infrastructure;
using Astra.Api.Security;

namespace Astra.Tests;

public sealed class EntraDirectoryTests
{
    [Fact]
    public async Task SearchUsesClientCredentialsEscapesFilterAndCachesToken()
    {
        var tokenCalls = 0;
        var graphCalls = 0;
        var handler = new StubHandler(async request =>
        {
            if (request.RequestUri!.Host == "login.microsoftonline.com")
            {
                tokenCalls++;
                Assert.Equal(HttpMethod.Post, request.Method);
                var form = await request.Content!.ReadAsStringAsync();
                Assert.Contains("grant_type=client_credentials", form);
                Assert.Contains("scope=https%3A%2F%2Fgraph.microsoft.com%2F.default", form);
                return Json(HttpStatusCode.OK, """{"access_token":"test-token","expires_in":3600}""");
            }

            graphCalls++;
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization.Parameter);
            var query = Uri.UnescapeDataString(request.RequestUri.Query);
            Assert.Contains("startsWith(displayName,'O''Brien')", query);
            return Json(HttpStatusCode.OK, """{"value":[{"id":"149de61e-5b5b-4442-bcb3-ffed617ec0e5","displayName":"O'Brien","mail":null,"userPrincipalName":"obrien@example.test"}]}""");
        });
        using var client = new HttpClient(handler);
        var directory = new EntraDirectory(client, ConfiguredIdentity());

        var first = await directory.SearchAsync(" O'Brien ", CancellationToken.None);
        var second = await directory.SearchAsync("O'Brien", CancellationToken.None);

        Assert.Equal(1, tokenCalls);
        Assert.Equal(2, graphCalls);
        Assert.Equal("obrien@example.test", Assert.Single(first).Email);
        Assert.Equal("obrien@example.test", Assert.Single(second).Email);
    }

    [Fact]
    public async Task SearchDoesNotCallMicrosoftWhenIdentityIsUnconfigured()
    {
        using var client = new HttpClient(new StubHandler(_ => throw new InvalidOperationException("Unexpected HTTP request.")));
        var identity = new IdentityOptions
        {
            BaseUri = new Uri("http://localhost:3000"),
            InitialAdministrators = new HashSet<Guid>()
        };
        var directory = new EntraDirectory(client, identity);

        var fault = await Assert.ThrowsAsync<ApiFault>(() => directory.SearchAsync("admin", CancellationToken.None));

        Assert.Equal(503, fault.Status);
    }

    [Fact]
    public async Task FindByIdUsesGraphAndRejectsMismatchedUserIdentity()
    {
        var expectedId = Guid.Parse("149de61e-5b5b-4442-bcb3-ffed617ec0e5");
        var handler = new StubHandler(async request =>
        {
            if (request.RequestUri!.Host == "login.microsoftonline.com")
                return Json(HttpStatusCode.OK, """{"access_token":"test-token","expires_in":3600}""");
            Assert.Equal($"/v1.0/users/{expectedId:D}", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            return Json(HttpStatusCode.OK, """{"id":"149de61e-5b5b-4442-bcb3-ffed617ec0e5","displayName":"Astra Admin","mail":"admin@example.test","userPrincipalName":"admin@example.test"}""");
        });
        using var client = new HttpClient(handler);
        var directory = new EntraDirectory(client, ConfiguredIdentity());

        var user = await directory.FindByIdAsync(expectedId, CancellationToken.None);
        Assert.Equal(expectedId, user.Id);
        Assert.Equal("admin@example.test", user.Email);

        var mismatch = new StubHandler(request => Task.FromResult(request.RequestUri!.Host == "login.microsoftonline.com"
            ? Json(HttpStatusCode.OK, """{"access_token":"test-token","expires_in":3600}""")
            : Json(HttpStatusCode.OK, """{"id":"c63f0aca-5875-4c20-9761-853c05a95d8f","displayName":"Other","mail":null,"userPrincipalName":"other@example.test"}""")));
        using var mismatchClient = new HttpClient(mismatch);
        var mismatchDirectory = new EntraDirectory(mismatchClient, ConfiguredIdentity());
        Assert.Equal(503, (await Assert.ThrowsAsync<ApiFault>(() => mismatchDirectory.FindByIdAsync(expectedId, CancellationToken.None))).Status);
    }

    private static IdentityOptions ConfiguredIdentity() => new()
    {
        BaseUri = new Uri("https://astra.example.test"),
        TenantId = "e21b63c0-8418-4414-8796-5b1adf57a836",
        ClientId = "8ee6a2a4-2963-45e0-b330-81c42fdc1b62",
        ClientSecret = "test-only-client-secret-value",
        SessionSeconds = 28800,
        InitialAdministrators = new HashSet<Guid>()
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request);
    }
}
