using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Astra.Api.Infrastructure;
using Astra.Api.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Astra.Tests;

public sealed class MemorySessions : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionRecord> records = new();
    public Task<SessionRecord?> ReadAsync(string token) => Task.FromResult(records.GetValueOrDefault(token));
    public Task PutAsync(string token, SessionRecord session, TimeSpan lifetime) { records[token] = session; return Task.CompletedTask; }
    public Task DeleteAsync(string token) { records.TryRemove(token, out _); return Task.CompletedTask; }
    public Task CheckAsync() => Task.CompletedTask;
}

public class IdentityFactory : WebApplicationFactory<Program>
{
    public readonly MemorySessions Sessions = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("APP_BASE_URL", "https://astra.test");
        builder.UseSetting("ENTRA_CLIENT_ID", "");
        builder.UseSetting("ENTRA_CLIENT_SECRET", "");
        builder.UseSetting("ENTRA_TENANT_ID", "");
        if (Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING") is string connection)
            builder.UseSetting("SQLSERVER_CONNECTION_STRING", new SqlConnectionStringBuilder(connection) { InitialCatalog = "AstraMigration" }.ConnectionString);
        builder.ConfigureServices(services => { services.RemoveAll<ISessionStore>(); services.AddSingleton<ISessionStore>(Sessions); });
    }
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://astra.test"), AllowAutoRedirect = false, HandleCookies = false });
}

public class IdentityTests
{
    [Theory]
    [InlineData("https://evil.example", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/admin/../operations", "/")]
    [InlineData("/admin?redirect=https://evil.example", "/")]
    [InlineData("/admin", "/admin")]
    [InlineData("/operations", "/operations")]
    public void ReturnDestinationRejectsExternalAndUnapprovedPaths(string input, string expected) => Assert.Equal(expected, IdentityOptions.SafeReturnTo(input));

    [Fact]
    public async Task AnonymousRequestsCannotUseBusinessRoutesOrSpoofAWorkspace()
    {
        await using var factory = new IdentityFactory();
        using var client = factory.Browser();
        client.DefaultRequestHeaders.Add("x-amnikon-workspace", "admin");
        foreach (var route in new[] { "/auth/me", "/tickets", "/changes", "/admin/overview" })
        {
            var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        var status = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/auth/status");
        Assert.False(status.GetProperty("configured").GetBoolean());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/auth/login")).StatusCode);
    }

    private static async Task<(Guid Id, int Version)> AdministratorAsync()
    {
        var connectionString = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING")) { InitialCatalog = "AstraMigration" };
        await using var connection = new SqlConnection(connectionString.ConnectionString);
        await connection.OpenAsync();
        var rows = await Database.QueryAsync(connection, "SELECT TOP (1) u.id,u.session_version FROM dbo.users u JOIN dbo.user_roles r ON r.user_id=u.id WHERE u.active=1 AND r.role_code='ADMIN' ORDER BY u.id");
        Assert.Single(rows);
        return ((Guid)rows[0]["id"]!, Convert.ToInt32(rows[0]["session_version"]));
    }

    [SqlServerFact]
    public async Task SessionVersionRevocationAndWorkspaceCappingUseSqlServerAccounts()
    {
        await using var factory = new IdentityFactory();
        using var client = factory.Browser();
        var admin = await AdministratorAsync();
        var token = AuthService.Token();
        await factory.Sessions.PutAsync(token, new SessionRecord(admin.Id, "csrf", admin.Version), TimeSpan.FromMinutes(15));
        client.DefaultRequestHeaders.Add("Cookie", "amnikon_session=" + token);
        client.DefaultRequestHeaders.Add("x-amnikon-workspace", "operations");
        var response = await client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var user = body.GetProperty("user");
        Assert.Equal("TECHNICIAN", user.GetProperty("roles")[0].GetString());
        Assert.DoesNotContain(user.GetProperty("permissions").EnumerateArray(), p => p.GetString() == "*");
        client.DefaultRequestHeaders.Remove("x-amnikon-workspace");
        client.DefaultRequestHeaders.Add("x-amnikon-workspace", "chooser");
        var chooser = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/auth/me");
        Assert.Contains(chooser.GetProperty("user").GetProperty("roles").EnumerateArray(), p => p.GetString() == "ADMIN");
        // A chooser request cannot be reused to access domain APIs with base roles.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/tickets")).StatusCode);
        await factory.Sessions.PutAsync(token, new SessionRecord(admin.Id, "csrf", admin.Version - 1), TimeSpan.FromMinutes(15));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);
    }

    [SqlServerFact]
    public async Task AdministrationCannotCreateServiceRecordsAndLogoutRequiresOriginAndCsrf()
    {
        await using var factory = new IdentityFactory();
        using var client = factory.Browser();
        var admin = await AdministratorAsync();
        var token = AuthService.Token();
        await factory.Sessions.PutAsync(token, new SessionRecord(admin.Id, "test-csrf-token", admin.Version), TimeSpan.FromMinutes(15));
        client.DefaultRequestHeaders.Add("Cookie", "amnikon_session=" + token);
        client.DefaultRequestHeaders.Add("x-amnikon-workspace", "admin");
        client.DefaultRequestHeaders.Add("Origin", "https://astra.test");
        client.DefaultRequestHeaders.Add("x-csrf-token", "test-csrf-token");
        foreach (var route in new[] { "/tickets", "/changes", "/problems", "/tickets/00000000-0000-0000-0000-000000000001/comments" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, new { subject = "blocked" })).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://evil.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/auth/logout", null)).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://astra.test");
        client.DefaultRequestHeaders.Remove("x-csrf-token");
        client.DefaultRequestHeaders.Add("x-csrf-token", "wrong");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/auth/logout", null)).StatusCode);
        client.DefaultRequestHeaders.Remove("x-csrf-token");
        client.DefaultRequestHeaders.Add("x-csrf-token", "test-csrf-token");
        var logout = await client.PostAsync("/auth/logout", null);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("path=/api", StringComparison.OrdinalIgnoreCase) && cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
        Assert.Null(await factory.Sessions.ReadAsync(token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/auth/me")).StatusCode);
    }
}
