using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Astra.Api.Infrastructure;
using Astra.Api.Modules;
using Astra.Api.Security;
using Microsoft.Data.SqlClient;

namespace Astra.Tests;

public class CoreDataTests
{
    [Fact]
    public void CatalogValidationRejectsUnknownFieldsAndInvalidReferences()
    {
        Assert.Equal(400, Assert.Throws<ApiFault>(() => CoreData.Validate("sites", JsonNode.Parse("{\"name\":\"Site\",\"clientId\":\"invalid\"}")!.AsObject())).Status);
        Assert.Equal(400, Assert.Throws<ApiFault>(() => CoreData.Validate("clients", JsonNode.Parse("{\"name\":\"Customer\",\"active\":true}")!.AsObject())).Status);
        Assert.Equal(400, Assert.Throws<ApiFault>(() => CoreData.Validate("contacts", JsonNode.Parse("{\"name\":\"Contact\",\"clientId\":\"d841de35-0783-4c22-a667-753e05ab87f4\",\"email\":\"invalid\"}")!.AsObject())).Status);
    }
    [SqlServerFact]
    public async Task CoreCatalogListsAndCreationPreserveAdministrativeContracts()
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING")) { InitialCatalog = "AstraMigration" }.ConnectionString); await connection.OpenAsync();
        var admin = (await Database.QueryAsync(connection, "SELECT TOP (1) u.id,u.session_version FROM dbo.users u JOIN dbo.user_roles r ON r.user_id=u.id WHERE u.active=1 AND r.role_code='ADMIN' ORDER BY u.id")).Single();
        await using var factory = new IdentityFactory(); using var client = factory.Browser(); var token = AuthService.Token();
        await factory.Sessions.PutAsync(token, new SessionRecord((Guid)admin["id"]!, "test-csrf-token", Convert.ToInt32(admin["session_version"])), TimeSpan.FromMinutes(15));
        client.DefaultRequestHeaders.Add("Cookie", "amnikon_session=" + token); client.DefaultRequestHeaders.Add("x-amnikon-workspace", "admin"); client.DefaultRequestHeaders.Add("Origin", "https://astra.test"); client.DefaultRequestHeaders.Add("x-csrf-token", "test-csrf-token");
        foreach (var route in new[] { "roles", "permissions", "ticket-sources", "users", "clients", "contacts", "sites", "assets", "categories", "tags", "audit-logs" })
        {
            var response = await client.GetAsync("/" + route); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(JsonValueKind.Array, (await response.Content.ReadFromJsonAsync<JsonElement>()).ValueKind);
        }
        var records = new Dictionary<string, Guid>();
        async Task<Guid> Create(string resource, object input)
        {
            var response = await client.PostAsJsonAsync("/" + resource, input); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); records[resource] = id; return id;
        }
        try
        {
            var clientId = await Create("clients", new { name = "Isolated catalog customer " + Guid.NewGuid().ToString("N"), primaryEmail = "catalog@invalid.test" });
            var site = await Create("sites", new { clientId, name = "Isolated catalog site" });
            await Create("contacts", new { clientId, siteId = site, name = "Isolated catalog contact", email = "catalog-contact@invalid.test" });
            await Create("assets", new { clientId, siteId = site, name = "Isolated catalog asset", assetType = "TEST" });
            await Create("categories", new { name = "Isolated catalog category " + Guid.NewGuid().ToString("N") });
            await Create("tags", new { name = "Isolated catalog tag " + Guid.NewGuid().ToString("N") });
            var prefix = (await Database.QueryAsync(connection, "SELECT ticket_prefix FROM dbo.sites WHERE id=@id", new Dictionary<string, object?> { ["id"] = site })).Single()["ticket_prefix"];
            Assert.NotNull(prefix);
            var contacts = await client.GetFromJsonAsync<JsonElement>("/contacts?clientId=" + clientId); Assert.Single(contacts.EnumerateArray());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/contacts", new { clientId, siteId = Guid.NewGuid(), name = "Invalid site" })).StatusCode);
            client.DefaultRequestHeaders.Remove("x-amnikon-workspace"); client.DefaultRequestHeaders.Add("x-amnikon-workspace", "operations");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/sites", new { clientId, name = "Blocked" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/users")).StatusCode);
        }
        finally
        {
            foreach (var table in new[] { "contacts", "assets", "sites", "tags", "categories", "clients" })
            {
                if (!records.TryGetValue(table, out var id)) continue;
                var parameters = new Dictionary<string, object?> { ["id"] = id };
                if (table == "clients") await Database.ExecuteAsync(connection, "DELETE dbo.client_ticket_sequences WHERE client_id=@id", parameters);
                await Database.ExecuteAsync(connection, "DELETE dbo." + table + " WHERE id=@id; DELETE dbo.audit_logs WHERE entity_id=CONVERT(nvarchar(36),@id); DELETE dbo.ticket_prefix_registry WHERE owner_id=@id", parameters);
            }
        }
    }
}
