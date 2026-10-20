using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Astra.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Astra.Tests;

public sealed class OidcFactory(Guid objectId, string failure = "") : IdentityFactory
{
    public const string Tenant = "e21b63c0-8418-4414-8796-5b1adf57a836";
    public const string Client = "8ee6a2a4-2963-45e0-b330-81c42fdc1b62";
    public const string Issuer = "https://login.microsoftonline.com/" + Tenant + "/v2.0";
    public string Nonce { get; set; } = "";
    public string Challenge { get; set; } = "";
    private readonly RSA rsa = RSA.Create(2048);
    private bool redeemed;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ENTRA_CLIENT_ID", Client);
        builder.UseSetting("ENTRA_CLIENT_SECRET", "test-only-client-secret-no-live-credentials");
        builder.UseSetting("ENTRA_TENANT_ID", Tenant);
        builder.ConfigureServices(services => services.PostConfigure<OpenIdConnectOptions>("entra", options =>
        {
            var config = new OpenIdConnectConfiguration { Issuer = Issuer, AuthorizationEndpoint = Issuer + "/authorize", TokenEndpoint = Issuer + "/token" };
            config.SigningKeys.Add(new RsaSecurityKey(rsa) { KeyId = "test-key" });
            options.Configuration = config;
            options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<OpenIdConnectConfiguration>(config);
            options.Backchannel = new HttpClient(new Provider(this, objectId, failure));
            options.PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable;
        }));
    }

    private sealed class Provider(OidcFactory factory, Guid objectId, string failure) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(Issuer + "/token", request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.Equal(Client, body["client_id"].ToString());
            Assert.Equal("https://astra.test/api/auth/callback", body["redirect_uri"].ToString());
            var verifierHash = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(body["code_verifier"].ToString()))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            Assert.Equal(factory.Challenge, verifierHash);
            if (factory.redeemed) return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid_grant" }) };
            factory.redeemed = true;
            using var wrongRsa = RSA.Create(2048);
            var key = new RsaSecurityKey(failure == "signature" ? wrongRsa : factory.rsa) { KeyId = "test-key" };
            var claims = new[] { new Claim("sub", objectId.ToString()), new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64), new Claim("tid", failure == "tenant" ? Guid.NewGuid().ToString() : Tenant), new Claim("oid", objectId.ToString()), new Claim("name", "Isolated sign-in test"), new Claim("preferred_username", "oidc-test@invalid.test"), new Claim("nonce", failure == "nonce" ? "wrong-nonce" : factory.Nonce) };
            var expires = failure == "expired" ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(10);
            var jwt = new JwtSecurityToken(Issuer, failure == "audience" ? Guid.NewGuid().ToString() : Client, claims, DateTime.UtcNow.AddMinutes(-20), expires, new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { token_type = "Bearer", expires_in = 600, access_token = "test-only-access-token", id_token = new JwtSecurityTokenHandler().WriteToken(jwt) }) };
        }
    }
}

public class OidcTests
{
    private static async Task<(string Callback, string Cookies)> ChallengeAsync(OidcFactory factory, HttpClient client)
    {
        var response = await client.GetAsync("/auth/login?returnTo=/operations");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("code", query["response_type"].ToString());
        Assert.Equal("S256", query["code_challenge_method"].ToString());
        Assert.Equal("https://astra.test/api/auth/callback", query["redirect_uri"].ToString());
        factory.Nonce = query["nonce"].ToString();
        factory.Challenge = query["code_challenge"].ToString();
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray();
        Assert.Equal(2, cookies.Length);
        Assert.All(cookies, cookie => { Assert.Contains("path=/api/auth/callback", cookie, StringComparison.OrdinalIgnoreCase); Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase); Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase); });
        return ("/auth/callback?code=test-code&state=" + Uri.EscapeDataString(query["state"].ToString()), string.Join("; ", cookies.Select(c => c.Split(';')[0])));
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("audience")]
    [InlineData("expired")]
    [InlineData("nonce")]
    [InlineData("tenant")]
    public async Task InvalidIdentityTokensNeverCreateApplicationSessions(string failure)
    {
        await using var factory = new OidcFactory(Guid.NewGuid(), failure);
        using var client = factory.Browser();
        var login = await ChallengeAsync(factory, client);
        client.DefaultRequestHeaders.Add("Cookie", login.Cookies);
        var response = await client.GetAsync(login.Callback);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], c => c.StartsWith("amnikon_session=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CallbackRequiresBrowserCorrelationCookiesAndAuthenticState()
    {
        await using var factory = new OidcFactory(Guid.NewGuid());
        using var client = factory.Browser();
        var login = await ChallengeAsync(factory, client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(login.Callback)).StatusCode);
        client.DefaultRequestHeaders.Add("Cookie", login.Cookies);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/callback?code=test-code&state=forged")).StatusCode);
    }

    [SqlServerFact]
    public async Task ValidCodeFlowCreatesRevocableSessionAndCannotReplayAuthorizationCode()
    {
        var user = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var connectionString = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING")) { InitialCatalog = "AstraMigration" };
        await using var connection = new SqlConnection(connectionString.ConnectionString);
        await connection.OpenAsync();
        var parameters = new Dictionary<string, object?> { ["id"] = user, ["tenant"] = Guid.Parse(OidcFactory.Tenant), ["object"] = objectId };
        await Database.ExecuteAsync(connection, "INSERT dbo.users(id,entra_tenant_id,entra_object_id,email,display_name) VALUES(@id,@tenant,@object,'oidc-test@invalid.test','Isolated sign-in test'); INSERT dbo.user_roles(user_id,role_code) VALUES(@id,'TECHNICIAN');", parameters);
        try
        {
            await using var factory = new OidcFactory(objectId);
            using var client = factory.Browser();
            var login = await ChallengeAsync(factory, client);
            client.DefaultRequestHeaders.Add("Cookie", login.Cookies);
            var response = await client.GetAsync(login.Callback);
            Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
            Assert.Equal("/operations", response.Headers.Location!.OriginalString);
            var sessionCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("amnikon_session=", StringComparison.Ordinal)).Split(';')[0];
            var session = await factory.Sessions.ReadAsync(sessionCookie.Split('=')[1]);
            Assert.NotNull(session);
            Assert.Equal(user, session.UserId);
            Assert.True(session.CsrfToken.Length >= 40);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(login.Callback)).StatusCode);
        }
        finally { await Database.ExecuteAsync(connection, "DELETE dbo.user_roles WHERE user_id=@id; DELETE dbo.users WHERE id=@id;", parameters); }
    }
}
