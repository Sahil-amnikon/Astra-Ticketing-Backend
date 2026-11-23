using MySqlConnector;

public sealed partial class MySqlStore
{
    private static bool MayAssignNewTicket(bool canAssign, bool isSelf, bool eligibleSelf) => canAssign || (isSelf && eligibleSelf);

    private static string[] NormalizeAdditionalTicketUsers(string primary, string[]? requested)
    {
        if ((requested?.Length ?? 0) > 49) throw new ArgumentException("Select at most 50 users per ticket.");
        var primaryId = Guid.Parse(primary);
        var ids = new List<string>();
        foreach (var raw in requested ?? [])
        {
            if (!Guid.TryParse(raw, out var id)) throw new ArgumentException("Select valid ticket users.");
            if (id != primaryId && !ids.Contains(id.ToString("D"), StringComparer.OrdinalIgnoreCase)) ids.Add(id.ToString("D"));
        }
        return ids.ToArray();
    }

    private static async Task SaveAdditionalTicketUsers(MySqlConnection c, MySqlTransaction tx, string ticket, string client, string? site, string[] ids, CancellationToken ct)
    {
        foreach (var contact in ids)
        {
            var matches = await FRows(c, tx, "SELECT id FROM contacts WHERE id=@contact AND client_id=@client AND active=1 AND (@site IS NULL OR site_id=@site) FOR SHARE", ct, ("contact", contact), ("client", client), ("site", site));
            if (matches.Count != 1) throw new ArgumentException("Every selected user must be active and belong to the selected client.");
            await FExecute(c, tx, "INSERT INTO ticket_additional_users(ticket_id,contact_id) VALUES(@ticket,@contact)", ct, ("ticket", ticket), ("contact", contact));
        }
    }

    private static async Task<bool> CanCreateSelfAssignment(MySqlConnection c, MySqlTransaction tx, string actor, string client, string? site, CancellationToken ct)
    {
        var support = await FRows(c, tx, "SELECT user_id FROM user_roles WHERE user_id=@actor AND role_code IN ('TECHNICIAN','TEAM_LEAD','MANAGER','ADMIN') LIMIT 1", ct, ("actor", actor));
        return support.Count != 0 && await IsTicketAssigneeEligible(c, tx, actor, client, site, false, ct);
    }
}
