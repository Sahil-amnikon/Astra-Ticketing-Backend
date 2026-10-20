using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astra.Api.Infrastructure;
using Astra.Api.Modules;
using Astra.Api.Security;
using Microsoft.Data.SqlClient;

namespace Astra.Tests;

public class GroupTests
{
    [Theory]
    [InlineData("STAFF", "USERS_MANAGE")]
    [InlineData("STAFF", "*")]
    [InlineData("CUSTOMER", "TICKETS_ASSIGN")]
    [InlineData("CUSTOMER", "PORTAL_ORGANIZATION_REPLY")]
    public void GroupPermissionsCannotCrossWorkspaceOrEscalatePrivileges(string kind, string permission)
    {
        var dto = new GroupInput { Name = "Validation", Kind = kind, ClientId = kind == "CUSTOMER" ? Guid.NewGuid() : null, Permissions = [permission], MemberIds = [], Active = true };
        Assert.Equal(400, Assert.Throws<ApiFault>(() => Groups.Validate(dto)).Status);
    }

    [SqlServerFact]
    public async Task NativeGroupConfigurationEnforcesMembershipRevisionAndWorkspaceAccess()
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING")) { InitialCatalog = "AstraMigration" }.ConnectionString); await connection.OpenAsync();
        var admin = (await Database.QueryAsync(connection, "SELECT TOP (1) u.id,u.session_version FROM dbo.users u JOIN dbo.user_roles r ON r.user_id=u.id WHERE u.active=1 AND r.role_code='ADMIN' ORDER BY u.id")).Single();
        var contact = (await Database.QueryAsync(connection, "SELECT TOP (1) c.id,c.client_id FROM dbo.contacts c JOIN dbo.clients cl ON cl.id=c.client_id WHERE c.active=1 AND cl.status='ACTIVE' AND cl.organization_type='CLIENT' ORDER BY c.id")).Single();
        await using var factory = new IdentityFactory(); using var client = factory.Browser(); var token = AuthService.Token();
        await factory.Sessions.PutAsync(token, new SessionRecord((Guid)admin["id"]!, "test-csrf-token", Convert.ToInt32(admin["session_version"])), TimeSpan.FromMinutes(15));
        client.DefaultRequestHeaders.Add("Cookie", "amnikon_session=" + token); client.DefaultRequestHeaders.Add("x-amnikon-workspace", "admin"); client.DefaultRequestHeaders.Add("Origin", "https://astra.test"); client.DefaultRequestHeaders.Add("x-csrf-token", "test-csrf-token");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/groups/options")).StatusCode);
        var ids = new List<Guid>();
        var staffMember = Guid.NewGuid();
        await Database.ExecuteAsync(connection, "INSERT dbo.users(id,entra_tenant_id,entra_object_id,email,display_name) VALUES(@id,@tenant,@object,'group-fixture@invalid.test','Isolated group member'); INSERT dbo.user_roles(user_id,role_code) VALUES(@id,'TECHNICIAN')", new Dictionary<string, object?> { ["id"] = staffMember, ["tenant"] = Guid.NewGuid(), ["object"] = Guid.NewGuid() });
        try
        {
            var staff = new GroupInput { Name = "Isolated staff group " + Guid.NewGuid().ToString("N"), Kind = "STAFF", Permissions = ["REPORTS_VIEW"], MemberIds = [staffMember], Active = true };
            var create = await client.PostAsJsonAsync("/admin/groups", staff); Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var created = await create.Content.ReadFromJsonAsync<JsonElement>(); var staffId = created.GetProperty("id").GetGuid(); ids.Add(staffId);
            Assert.Equal(JsonValueKind.Array, created.GetProperty("permissions").ValueKind);
            var detail = await client.GetFromJsonAsync<JsonElement>("/admin/groups/" + staffId);
            Assert.Contains(detail.GetProperty("staff_members").EnumerateArray(), m => m.GetGuid() == staffMember);
            using (var staffBrowser = factory.Browser())
            {
                var staffToken = AuthService.Token(); await factory.Sessions.PutAsync(staffToken, new SessionRecord(staffMember, "fixture-csrf", 0), TimeSpan.FromMinutes(15));
                staffBrowser.DefaultRequestHeaders.Add("Cookie", "amnikon_session=" + staffToken); staffBrowser.DefaultRequestHeaders.Add("x-amnikon-workspace", "operations");
                Assert.Equal(HttpStatusCode.Forbidden, (await staffBrowser.GetAsync("/admin/groups")).StatusCode);
                var scopedReport = await staffBrowser.GetFromJsonAsync<JsonElement>("/service-reports?from=2026-01-01&to=2026-12-31");
                Assert.Equal(0, scopedReport.GetProperty("tickets").GetProperty("created").GetInt32());
                Assert.Empty(scopedReport.GetProperty("clients").EnumerateArray());
            }
            var update = await client.PatchAsJsonAsync("/admin/groups/" + staffId, staff with { Revision = created.GetProperty("revision").GetInt32(), Permissions = ["TICKETS_ASSIGN"] }); Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/admin/groups/" + staffId, staff with { Revision = created.GetProperty("revision").GetInt32() })).StatusCode);
            var customer = new GroupInput { Name = "Isolated customer group " + Guid.NewGuid().ToString("N"), Kind = "CUSTOMER", ClientId = (Guid)contact["client_id"]!, Permissions = ["PORTAL_ORGANIZATION_VIEW", "PORTAL_ORGANIZATION_REPLY"], MemberIds = [(Guid)contact["id"]!], Active = true };
            create = await client.PostAsJsonAsync("/admin/groups", customer); Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            created = await create.Content.ReadFromJsonAsync<JsonElement>(); var customerId = created.GetProperty("id").GetGuid(); ids.Add(customerId);
            detail = await client.GetFromJsonAsync<JsonElement>("/admin/groups/" + customerId);
            Assert.Equal((Guid)contact["id"]!, detail.GetProperty("customer_members")[0].GetGuid());
            var listing = await client.GetFromJsonAsync<JsonElement>("/admin/groups?q=Isolated&kind=CUSTOMER"); Assert.Equal(JsonValueKind.Array, listing.GetProperty("items").ValueKind);
            Assert.Contains(listing.GetProperty("items").EnumerateArray(), g => g.GetProperty("id").GetGuid() == customerId && g.GetProperty("member_count").GetInt32() == 1);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/groups", customer with { ClientId = Guid.NewGuid() })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/groups", staff with { Permissions = ["*"] })).StatusCode);
            client.DefaultRequestHeaders.Remove("x-amnikon-workspace"); client.DefaultRequestHeaders.Add("x-amnikon-workspace", "operations");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/groups")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/admin/groups", staff)).StatusCode);
        }
        finally
        {
            foreach (var id in ids) await Database.ExecuteAsync(connection, "DELETE dbo.staff_group_members WHERE group_id=@id; DELETE dbo.customer_group_members WHERE group_id=@id; DELETE dbo.access_groups WHERE id=@id; DELETE dbo.audit_logs WHERE entity_type='ACCESS_GROUP' AND entity_id=@id", new Dictionary<string, object?> { ["id"] = id });
            await Database.ExecuteAsync(connection, "DELETE dbo.user_roles WHERE user_id=@id; DELETE dbo.users WHERE id=@id", new Dictionary<string, object?> { ["id"] = staffMember });
        }
    }
}
