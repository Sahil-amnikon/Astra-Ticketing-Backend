using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astra.Api.Infrastructure;
using Astra.Api.Modules;
using Astra.Api.Security;
using Microsoft.Data.SqlClient;

namespace Astra.Tests;

public class DomainTests
{
    [Theory]
    [InlineData(" AAHC.example ", "aahc.example")]
    [InlineData("bücher.example", "xn--bcher-kva.example")]
    public void WorkDomainsNormalizeConsistently(string input, string expected) => Assert.Equal(expected, ClientDomains.Normalize(input));
    [Theory]
    [InlineData("https://aahc.example")]
    [InlineData("*.aahc.example")]
    [InlineData("@aahc.example")]
    [InlineData("aahc.example.")]
    [InlineData("-aahc.example")]
    [InlineData("local")]
    public void DomainRoutingRejectsUrlsWildcardsAndInvalidLabels(string input) => Assert.Equal(400, Assert.Throws<ApiFault>(() => ClientDomains.Normalize(input)).Status);

    [SqlServerFact]
    public async Task DomainConfigurationPreservesSiteRoutingAndRejectsCrossCustomerChanges()
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING")) { InitialCatalog = "AstraMigration" }.ConnectionString); await connection.OpenAsync();
        var admin = (await Database.QueryAsync(connection, "SELECT TOP (1) u.id,u.session_version FROM dbo.users u JOIN dbo.user_roles r ON r.user_id=u.id WHERE u.active=1 AND r.role_code='ADMIN' ORDER BY u.id")).Single();
        var site = (await Database.QueryAsync(connection, "SELECT TOP(1) s.id,s.client_id,s.ticket_prefix FROM dbo.sites s JOIN dbo.clients c ON c.id=s.client_id WHERE s.active=1 AND c.status='ACTIVE' AND c.organization_type='CLIENT' ORDER BY s.id")).Single();
        await using var factory = new IdentityFactory(); using var client = factory.Browser(); var token = AuthService.Token();
        await factory.Sessions.PutAsync(token, new SessionRecord((Guid)admin["id"]!, "test-csrf-token", Convert.ToInt32(admin["session_version"])), TimeSpan.FromMinutes(15));
        client.DefaultRequestHeaders.Add("Cookie", "amnikon_session=" + token); client.DefaultRequestHeaders.Add("x-amnikon-workspace", "admin"); client.DefaultRequestHeaders.Add("Origin", "https://astra.test"); client.DefaultRequestHeaders.Add("x-csrf-token", "test-csrf-token");
        Guid? createdId = null;
        try
        {
            var input = new DomainInput { ClientId = (Guid)site["client_id"]!, SiteId = (Guid)site["id"]!, Domain = "isolated-" + Guid.NewGuid().ToString("N") + ".example", Active = true };
            var response = await client.PostAsJsonAsync("/admin/client-domains", input); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(); createdId = body.GetProperty("id").GetGuid();
            var page = await client.GetFromJsonAsync<JsonElement>("/admin/client-domains?siteId=" + site["id"]);
            Assert.Contains(page.GetProperty("domains").EnumerateArray(), domain => domain.GetProperty("id").GetGuid() == createdId && domain.GetProperty("ticket_prefix").GetString() == (string)site["ticket_prefix"]!);
            Assert.Equal("/support", page.GetProperty("portalPath").GetString());
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/admin/client-domains", input)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/client-domains", input with { Domain = "gmail.com" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/client-domains", input with { ClientId = Guid.NewGuid() })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/client-domains", input with { SiteId = Guid.NewGuid() })).StatusCode);
            var update = input with { Revision = body.GetProperty("revision").GetInt32(), Active = false };
            Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync("/admin/client-domains/" + createdId, update)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/admin/client-domains/" + createdId, update)).StatusCode);
            client.DefaultRequestHeaders.Remove("x-amnikon-workspace"); client.DefaultRequestHeaders.Add("x-amnikon-workspace", "operations");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/client-domains")).StatusCode);
        }
        finally { if (createdId.HasValue) await Database.ExecuteAsync(connection, "DELETE dbo.client_email_domains WHERE id=@id; DELETE dbo.audit_logs WHERE entity_type='CLIENT_EMAIL_DOMAIN' AND entity_id=@id", new Dictionary<string, object?> { ["id"] = createdId.Value }); }
    }
}
