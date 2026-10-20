using System.Text.Json;
using Astra.Api.Infrastructure;
using Astra.Api.Security;
using Microsoft.Data.SqlClient;

namespace Astra.Api.Modules;

public sealed record NotificationPreferences
{
    public required bool TicketAssigned { get; init; }
    public required bool TicketTransferred { get; init; }
    public required bool PublicReply { get; init; }
    public required bool SlaAtRisk { get; init; }
    public required bool SlaBreached { get; init; }
    public required bool TicketUnattended { get; init; }
}

public sealed class Notifications(Database database)
{
    public static readonly IReadOnlyDictionary<string, string> Preferences = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["TICKET_ASSIGNED"] = "ticket_assigned",
        ["TICKET_TRANSFERRED"] = "ticket_transferred",
        ["PUBLIC_REPLY"] = "public_reply",
        ["SLA_AT_RISK"] = "sla_at_risk",
        ["SLA_BREACHED"] = "sla_breached",
        ["TICKET_UNATTENDED"] = "ticket_unattended"
    };
    public static Task EnqueueAsync(SqlConnection connection, SqlTransaction transaction, Guid user, Guid? ticket, string type, string title, string body, string eventKey, CancellationToken cancellationToken = default)
    {
        if (!Preferences.TryGetValue(type, out var column)) throw new ArgumentException("Invalid notification type.", nameof(type));
        var parameters = new Dictionary<string, object?> { ["user"] = user, ["ticket"] = ticket, ["type"] = type, ["title"] = title, ["body"] = body, ["key"] = eventKey };
        return Database.ExecuteAsync(connection, $"INSERT dbo.notifications(user_id,ticket_id,type,title,body,event_key) SELECT @user,@ticket,@type,@title,@body,@key WHERE COALESCE((SELECT {column} FROM dbo.notification_preferences WHERE user_id=@user),1)=1 AND NOT EXISTS(SELECT 1 FROM dbo.notifications WITH(UPDLOCK,HOLDLOCK) WHERE event_key=@key)", parameters, transaction, cancellationToken);
    }
    public async Task<object> ListAsync(Actor actor, string? rawLimit, CancellationToken cancellationToken)
    {
        var limit = double.TryParse(rawLimit, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) && parsed != 0 ? (int)Math.Clamp(parsed, 1, 50) : 20;
        var parameters = ScopeSql.Parameters(actor); parameters["user"] = actor.Id; parameters["limit"] = limit; parameters["wide"] = actor.Roles.Overlaps(["TEAM_LEAD", "MANAGER", "ADMIN"]); parameters["team"] = actor.Permissions.Contains("TICKETS_TEAM_VIEW"); parameters["departments"] = JsonSerializer.Serialize(actor.DepartmentIds ?? []);
        var access = """
            n.user_id=@user AND(n.ticket_id IS NULL OR @scopeAdmin=1 OR
             (t.client_id IN(SELECT CONVERT(uniqueidentifier,[value]) FROM OPENJSON(@scopeClients)) AND
              (@wide=1 OR t.assigned_user_id=@user OR EXISTS(SELECT 1 FROM dbo.ticket_collaborators tc WHERE tc.ticket_id=t.id AND tc.user_id=@user) OR
               EXISTS(SELECT 1 FROM dbo.help_requests hr WHERE hr.ticket_id=t.id AND hr.status='PENDING' AND(hr.requested_user_id=@user OR hr.department_id IN(SELECT CONVERT(uniqueidentifier,[value]) FROM OPENJSON(@departments)))) OR
               (@team=1 AND(@scopeSiteCount=0 OR t.site_id IN(SELECT CONVERT(uniqueidentifier,[value]) FROM OPENJSON(@scopeSites)))))))
            """;
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
        var items = await Database.QueryAsync(connection, $"SELECT TOP(@limit) n.id,n.ticket_id,n.change_id,n.type,n.title,n.body,n.read_at,n.created_at,t.ticket_number FROM dbo.notifications n LEFT JOIN dbo.tickets t ON t.id=n.ticket_id WHERE {access} ORDER BY n.created_at DESC", parameters, transaction, cancellationToken);
        var unread = (await Database.QueryAsync(connection, $"SELECT COUNT(*) AS n FROM dbo.notifications n LEFT JOIN dbo.tickets t ON t.id=n.ticket_id WHERE {access} AND n.read_at IS NULL", parameters, transaction, cancellationToken)).Single()["n"];
        await transaction.CommitAsync(cancellationToken); return new { unread, items };
    }
    public async Task<object> ReadPreferencesAsync(Guid user, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        var rows = await Database.QueryAsync(connection, "SELECT ticket_assigned AS ticketAssigned,ticket_transferred AS ticketTransferred,public_reply AS publicReply,sla_at_risk AS slaAtRisk,sla_breached AS slaBreached,ticket_unattended AS ticketUnattended FROM dbo.notification_preferences WHERE user_id=@user", new Dictionary<string, object?> { ["user"] = user }, cancellationToken: cancellationToken);
        return rows.Count > 0 ? rows[0] : new NotificationPreferences { TicketAssigned = true, TicketTransferred = true, PublicReply = true, SlaAtRisk = true, SlaBreached = true, TicketUnattended = true };
    }
    public async Task<object> SavePreferencesAsync(Guid user, NotificationPreferences input, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, object?> { ["user"] = user, ["assigned"] = input.TicketAssigned, ["transferred"] = input.TicketTransferred, ["reply"] = input.PublicReply, ["risk"] = input.SlaAtRisk, ["breach"] = input.SlaBreached, ["unattended"] = input.TicketUnattended };
        await using var connection = await database.OpenAsync(cancellationToken); await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await Database.ExecuteAsync(connection, """
            IF EXISTS(SELECT 1 FROM dbo.notification_preferences WITH(UPDLOCK,HOLDLOCK) WHERE user_id=@user)
             UPDATE dbo.notification_preferences SET ticket_assigned=@assigned,ticket_transferred=@transferred,public_reply=@reply,sla_at_risk=@risk,sla_breached=@breach,ticket_unattended=@unattended,updated_at=SYSUTCDATETIME() WHERE user_id=@user;
            ELSE INSERT dbo.notification_preferences(user_id,ticket_assigned,ticket_transferred,public_reply,sla_at_risk,sla_breached,ticket_unattended) VALUES(@user,@assigned,@transferred,@reply,@risk,@breach,@unattended);
            """, parameters, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken); return input;
    }
    public async Task MarkAsync(Guid user, Guid? id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await Database.ExecuteAsync(connection, "UPDATE dbo.notifications SET read_at=SYSUTCDATETIME() WHERE user_id=@user AND read_at IS NULL AND(@id IS NULL OR id=@id)", new Dictionary<string, object?> { ["user"] = user, ["id"] = id }, cancellationToken: cancellationToken);
    }
    public static void Map(WebApplication app)
    {
        app.MapGet("/notifications", async (HttpContext context, AuthService auth, Notifications notifications) => await notifications.ListAsync(await auth.CurrentAsync(context), context.Request.Query["limit"].ToString(), context.RequestAborted));
        app.MapGet("/notifications/preferences", async (HttpContext context, AuthService auth, Notifications notifications) => await notifications.ReadPreferencesAsync((await auth.CurrentAsync(context)).Id, context.RequestAborted));
        app.MapPatch("/notifications/preferences", async (HttpContext context, NotificationPreferences input, AuthService auth, Notifications notifications) => await notifications.SavePreferencesAsync((await auth.CurrentAsync(context)).Id, input, context.RequestAborted));
        app.MapPatch("/notifications/read-all", async (HttpContext context, AuthService auth, Notifications notifications) => { await notifications.MarkAsync((await auth.CurrentAsync(context)).Id, null, context.RequestAborted); return new { read = true }; });
        app.MapPatch("/notifications/{id:guid}/read", async (HttpContext context, Guid id, AuthService auth, Notifications notifications) => { await notifications.MarkAsync((await auth.CurrentAsync(context)).Id, id, context.RequestAborted); return new { id }; });
    }
}
