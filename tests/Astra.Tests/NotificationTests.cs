using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astra.Api.Infrastructure;
using Astra.Api.Modules;
using Astra.Api.Security;
using Microsoft.Data.SqlClient;

namespace Astra.Tests;

public class NotificationTests
{
    [SqlServerFact]
    public async Task NotificationsAreIdempotentScopedToAccountAndRespectPreferences()
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING")) { InitialCatalog = "AstraMigration" }.ConnectionString); await connection.OpenAsync();
        var user = Guid.NewGuid(); var notice = Guid.NewGuid(); var hidden = Guid.NewGuid();
        var parameters = new Dictionary<string, object?> { ["id"] = user, ["tenant"] = Guid.NewGuid(), ["object"] = Guid.NewGuid(), ["notice"] = notice, ["hidden"] = hidden };
        await Database.ExecuteAsync(connection, "INSERT dbo.users(id,entra_tenant_id,entra_object_id,email,display_name) VALUES(@id,@tenant,@object,'notice-fixture@invalid.test','Isolated notification fixture'); INSERT dbo.user_roles(user_id,role_code) VALUES(@id,'TECHNICIAN'); INSERT dbo.notifications(id,user_id,type,title,body,event_key) VALUES(@notice,@id,'TICKET_ASSIGNED','Fixture notice','Fixture notice',CONVERT(nvarchar(36),@notice)); INSERT dbo.notifications(id,user_id,ticket_id,type,title,body,event_key) SELECT @hidden,@id,id,'TICKET_ASSIGNED','Out-of-scope fixture','Fixture notice',CONVERT(nvarchar(36),@hidden) FROM(SELECT TOP(1) id FROM dbo.tickets ORDER BY id)t;", parameters);
        try
        {
            await using var factory = new IdentityFactory(); using var client = factory.Browser(); var token = AuthService.Token();
            await factory.Sessions.PutAsync(token, new SessionRecord(user, "fixture-csrf-token", 0), TimeSpan.FromMinutes(15));
            client.DefaultRequestHeaders.Add("Cookie", "amnikon_session=" + token); client.DefaultRequestHeaders.Add("x-amnikon-workspace", "operations"); client.DefaultRequestHeaders.Add("Origin", "https://astra.test"); client.DefaultRequestHeaders.Add("x-csrf-token", "fixture-csrf-token");
            var preferences = await client.GetFromJsonAsync<JsonElement>("/notifications/preferences"); Assert.True(preferences.GetProperty("ticketAssigned").GetBoolean());
            var list = await client.GetFromJsonAsync<JsonElement>("/notifications"); Assert.Equal(1, list.GetProperty("unread").GetInt32()); Assert.Single(list.GetProperty("items").EnumerateArray()); Assert.Equal(notice, list.GetProperty("items")[0].GetProperty("id").GetGuid());
            Assert.Equal(HttpStatusCode.OK, (await client.PatchAsync("/notifications/" + notice + "/read", null)).StatusCode);
            list = await client.GetFromJsonAsync<JsonElement>("/notifications"); Assert.Equal(0, list.GetProperty("unread").GetInt32());
            var input = new NotificationPreferences { TicketAssigned = false, TicketTransferred = true, PublicReply = true, SlaAtRisk = true, SlaBreached = true, TicketUnattended = true };
            Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync("/notifications/preferences", input)).StatusCode);
            preferences = await client.GetFromJsonAsync<JsonElement>("/notifications/preferences"); Assert.False(preferences.GetProperty("ticketAssigned").GetBoolean());
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
            {
                var key = "isolated-notice-" + Guid.NewGuid().ToString("N");
                await Notifications.EnqueueAsync(connection, transaction, user, null, "TICKET_ASSIGNED", "Muted", "Fixture", key);
                var muted = await Database.QueryAsync(connection, "SELECT COUNT(*) AS n FROM dbo.notifications WHERE event_key=@key", new Dictionary<string, object?> { ["key"] = key }, transaction); Assert.Equal(0, muted.Single()["n"]);
                await Notifications.EnqueueAsync(connection, transaction, user, null, "PUBLIC_REPLY", "Enabled", "Fixture", key);
                await Notifications.EnqueueAsync(connection, transaction, user, null, "PUBLIC_REPLY", "Enabled", "Fixture", key);
                var count = await Database.QueryAsync(connection, "SELECT COUNT(*) AS n FROM dbo.notifications WHERE event_key=@key", new Dictionary<string, object?> { ["key"] = key }, transaction); Assert.Equal(1, count.Single()["n"]);
                await transaction.RollbackAsync();
            }
            Assert.Equal(HttpStatusCode.OK, (await client.PatchAsync("/notifications/read-all", null)).StatusCode);
        }
        finally { await Database.ExecuteAsync(connection, "DELETE dbo.notifications WHERE user_id=@id; DELETE dbo.notification_preferences WHERE user_id=@id; DELETE dbo.user_roles WHERE user_id=@id; DELETE dbo.users WHERE id=@id", parameters); }
    }
}
