using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astra.Api.Infrastructure;
using Astra.Api.Security;
using Microsoft.Data.SqlClient;
using Astra.Api.Modules;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Astra.Tests;

public class AdministrationTests
{
    [SqlServerFact]
    public async Task OverviewAndAdministrativeListsPreserveReactContracts()
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING")) { InitialCatalog = "AstraMigration" }.ConnectionString);
        await connection.OpenAsync();
        var admin = (await Database.QueryAsync(connection, "SELECT TOP (1) u.id,u.session_version FROM dbo.users u JOIN dbo.user_roles r ON r.user_id=u.id WHERE u.active=1 AND r.role_code='ADMIN' ORDER BY u.id")).Single();
        await using var factory = new IdentityFactory(); using var client = factory.Browser();
        var token = AuthService.Token();
        await factory.Sessions.PutAsync(token, new SessionRecord((Guid)admin["id"]!, "test-token", Convert.ToInt32(admin["session_version"])), TimeSpan.FromMinutes(15));
        client.DefaultRequestHeaders.Add("Cookie", "amnikon_session=" + token); client.DefaultRequestHeaders.Add("x-amnikon-workspace", "admin");
        var overview = await client.GetAsync("/admin/overview"); Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        var metrics = await overview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(metrics.GetProperty("users").GetInt32() > 0); Assert.True(metrics.GetProperty("sites").GetInt32() > 0);
        foreach (var resource in new[] { "users", "clients", "contacts", "sites", "assets", "categories", "tags", "sources", "departments", "audit-logs" })
        {
            var response = await client.GetAsync("/admin/" + resource + "?pageSize=5");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var page = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Array, page.GetProperty("items").ValueKind); Assert.True(page.GetProperty("items").GetArrayLength() <= 5);
            Assert.True(page.GetProperty("total").GetInt32() >= page.GetProperty("items").GetArrayLength());
            Assert.Equal(1, page.GetProperty("page").GetInt32()); Assert.Equal(5, page.GetProperty("pageSize").GetInt32());
            foreach (var item in page.GetProperty("items").EnumerateArray()) Assert.DoesNotContain(item.EnumerateObject(), p => p.Name.StartsWith("astra_idx_", StringComparison.Ordinal));
            if (resource == "users") foreach (var item in page.GetProperty("items").EnumerateArray())
            {
                Assert.Equal(JsonValueKind.Array, item.GetProperty("roles").ValueKind); Assert.All(item.GetProperty("roles").EnumerateArray(), role => Assert.Equal(JsonValueKind.String, role.ValueKind));
                var expected = (await Database.QueryAsync(connection, "SELECT COUNT(*) AS n FROM dbo.user_roles WHERE user_id=@id", new Dictionary<string, object?> { ["id"] = item.GetProperty("id").GetGuid() })).Single();
                Assert.Equal(expected["n"], item.GetProperty("roles").GetArrayLength());
            }
            if (resource == "sites") foreach (var item in page.GetProperty("items").EnumerateArray()) Assert.Equal(JsonValueKind.Array, item.GetProperty("email_domains").ValueKind);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/admin/users?pageSize=500")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/admin/users?status=unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/mail_settings")).StatusCode);
        client.DefaultRequestHeaders.Remove("x-amnikon-workspace"); client.DefaultRequestHeaders.Add("x-amnikon-workspace", "operations");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/users")).StatusCode);
    }
}
