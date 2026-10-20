using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astra.Api.Infrastructure;
using Astra.Api.Security;
using Microsoft.Data.SqlClient;

namespace Astra.Tests;

public class TicketReadTests
{
    [SqlServerFact]
    public async Task AdministrativeTicketOversightAndDashboardReadCopiedRecords()
    {
        await using var connection=new SqlConnection(new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING")){InitialCatalog="AstraMigration"}.ConnectionString);await connection.OpenAsync();
        var admin=(await Database.QueryAsync(connection,"SELECT TOP(1) u.id,u.session_version FROM dbo.users u JOIN dbo.user_roles r ON r.user_id=u.id WHERE u.active=1 AND r.role_code='ADMIN' ORDER BY u.id")).Single();
        var ticket=(await Database.QueryAsync(connection,"SELECT TOP(1) id,ticket_number FROM dbo.tickets ORDER BY created_at")).Single();
        var expected=(await Database.QueryAsync(connection,"SELECT COUNT(*) AS n FROM dbo.tickets WHERE status NOT IN('RESOLVED','CLOSED','CANCELLED')")).Single()["n"];
        await using var factory=new IdentityFactory();using var client=factory.Browser();var token=AuthService.Token();
        await factory.Sessions.PutAsync(token,new SessionRecord((Guid)admin["id"]!,"fixture-csrf-token",Convert.ToInt32(admin["session_version"])),TimeSpan.FromMinutes(15));
        client.DefaultRequestHeaders.Add("Cookie","amnikon_session="+token);client.DefaultRequestHeaders.Add("x-amnikon-workspace","admin");
        var options=await client.GetAsync("/tickets/options");Assert.Equal(HttpStatusCode.OK,options.StatusCode);
        var choices=await options.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(JsonValueKind.Array,choices.GetProperty("clients").ValueKind);Assert.Equal(9,choices.GetProperty("statuses").GetArrayLength());
        var dashboard=await client.GetAsync("/tickets/dashboard?scope=all");Assert.Equal(HttpStatusCode.OK,dashboard.StatusCode);
        var metrics=await dashboard.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(expected,metrics.GetProperty("counts").GetProperty("open").GetInt32());Assert.Equal(14,metrics.GetProperty("charts").GetProperty("trend").GetArrayLength());
        var response=await client.GetAsync("/tickets/"+ticket["id"]);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var detail=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(ticket["ticket_number"],detail.GetProperty("ticket").GetProperty("ticket_number").GetString());
        foreach(var section in new[]{"comments","history","attachments","approvals","collaborators","helpRequests","knowledge","problems"})Assert.Equal(JsonValueKind.Array,detail.GetProperty(section).ValueKind);
        Assert.Equal(JsonValueKind.Array,detail.GetProperty("ticket").GetProperty("tags").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/tickets/"+Guid.NewGuid())).StatusCode);
        client.DefaultRequestHeaders.Remove("x-amnikon-workspace");client.DefaultRequestHeaders.Add("x-amnikon-workspace","chooser");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/tickets/"+ticket["id"])).StatusCode);
    }
}
