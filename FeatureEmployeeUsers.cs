using System.Net.Mail;
using MySqlConnector;

public sealed partial class MySqlStore
{
    internal const string EmployeeOrganizationId = "00000000-0000-4000-8000-000000000132";
    internal static bool IsEmployeeRequester(string role, string? email) => role == "USER" && IsAmnikonEmployeeEmail(email);
    internal static bool IsAmnikonEmployeeEmail(string? email)
    {
        var value = email?.Trim();
        return value is not null && MailAddress.TryCreate(value, out var address) &&
            string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(address.Host, "amnikontechnologies.com", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(address.Host, "amnikon.com", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task EnsureEmployeeRequester(MySqlConnection connection, MySqlTransaction tx, string user, string name, string email, CancellationToken ct)
    {
        if (!IsAmnikonEmployeeEmail(email)) throw new ArgumentException("An Amnikon employee requires a company email.");
        if ((await FRows(connection, tx, "SELECT id FROM clients WHERE id=@company AND status='ACTIVE' FOR SHARE", ct, ("company", EmployeeOrganizationId))).Count != 1)
            throw new InvalidOperationException("The Amnikon organization must be active.");
        // Keep historical contacts and tickets; remove only an obsolete sign-in link
        // before creating/reusing the employee's own Amnikon requester contact.
        await FExecute(connection, tx, "UPDATE contacts SET user_id=NULL,updated_at=UTC_TIMESTAMP(6) WHERE user_id=@user AND client_id<>@company", ct, ("user", user), ("company", EmployeeOrganizationId));
        var contacts = await FRows(connection, tx, "SELECT id,user_id FROM contacts WHERE client_id=@company AND (user_id=@user OR LOWER(email)=LOWER(@email)) ORDER BY id FOR UPDATE", ct,
            ("company", EmployeeOrganizationId), ("user", user), ("email", email));
        if (contacts.Any(row => row["user_id"] is not null && !string.Equals(Convert.ToString(row["user_id"]), user, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This employee email is linked to another Amnikon account. Review the contact before saving.");
        var existing = contacts.FirstOrDefault(row => string.Equals(Convert.ToString(row["user_id"]), user, StringComparison.OrdinalIgnoreCase)) ?? contacts.FirstOrDefault();
        if (existing is not null)
            await FExecute(connection, tx, "UPDATE contacts SET user_id=@user,name=@name,email=@email,site_id=NULL,active=1,updated_at=UTC_TIMESTAMP(6) WHERE id=@id", ct,
                ("user", user), ("name", name), ("email", email), ("id", existing["id"]));
        else
            await FExecute(connection, tx, "INSERT INTO contacts(id,client_id,user_id,name,email,active) VALUES(@id,@company,@user,@name,@email,1)", ct,
                ("id", Guid.NewGuid().ToString("D")), ("company", EmployeeOrganizationId), ("user", user), ("name", name), ("email", email));
    }

    public async Task<object> RepairAmnikonEmployeeUsers(string actor, CancellationToken ct)
    {
        await RequireAdmin(actor, ct);
        await using var connection = CreateConnection(); await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await FRows(connection, tx, "SELECT id FROM users ORDER BY id FOR UPDATE", ct);
        var users = await FRows(connection, tx, "SELECT u.id,u.display_name,u.email FROM users u WHERE u.active=1 AND EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code='USER') AND NOT EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code<>'USER') ORDER BY u.id", ct);
        var updated = 0;
        await FExecute(connection, tx, "UPDATE clients SET name='Amnikon' WHERE id=@company AND name='Amnikon Internal'", ct, ("company", EmployeeOrganizationId));
        foreach (var user in users)
        {
            var email = Convert.ToString(user["email"])!;
            if (!IsAmnikonEmployeeEmail(email)) continue;
            var id = Convert.ToString(user["id"])!;
            await FExecute(connection, tx, "DELETE FROM client_users WHERE user_id=@user AND client_id<>@company", ct, ("user", id), ("company", EmployeeOrganizationId));
            await FExecute(connection, tx, "INSERT IGNORE INTO client_users(user_id,client_id) VALUES(@user,@company)", ct, ("user", id), ("company", EmployeeOrganizationId));
            await FExecute(connection, tx, "DELETE FROM user_sites WHERE user_id=@user", ct, ("user", id));
            await FExecute(connection, tx, "DELETE FROM department_users WHERE user_id=@user", ct, ("user", id));
            await FExecute(connection, tx, "UPDATE users SET work_scope='INTERNAL',change_access='NONE',supervisor_user_id=NULL,session_version=session_version+1,updated_at=UTC_TIMESTAMP(6) WHERE id=@user", ct, ("user", id));
            await EnsureEmployeeRequester(connection, tx, id, Convert.ToString(user["display_name"])!, email, ct);
            await AddAudit(connection, tx, actor, "USER.EMPLOYEE_ORGANIZATION_UPDATED", "USER", id, new { organization = "Amnikon", clientId = EmployeeOrganizationId, workScope = "INTERNAL", role = "USER" }, ct);
            updated++;
        }
        var managersUpdated = 0;
        var managers = await FRows(connection, tx, "SELECT u.id FROM users u WHERE EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code='MANAGER') AND NOT EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code='ADMIN') ORDER BY u.id", ct);
        if (managers.Count > 0 && (await FRows(connection, tx, "SELECT id FROM clients WHERE id=@company AND status='ACTIVE' FOR SHARE", ct, ("company", EmployeeOrganizationId))).Count != 1)
            throw new InvalidOperationException("The Amnikon organization must be active.");
        foreach (var manager in managers)
        {
            var id = Convert.ToString(manager["id"])!;
            var memberships = await FRows(connection, tx, "SELECT client_id FROM client_users WHERE user_id=@user", ct, ("user", id));
            if (memberships.Count == 1 && string.Equals(Convert.ToString(memberships[0]["client_id"]), EmployeeOrganizationId, StringComparison.OrdinalIgnoreCase)) continue;
            await FExecute(connection, tx, "DELETE FROM client_users WHERE user_id=@user AND client_id<>@company", ct, ("user", id), ("company", EmployeeOrganizationId));
            await FExecute(connection, tx, "INSERT IGNORE INTO client_users(user_id,client_id) VALUES(@user,@company)", ct, ("user", id), ("company", EmployeeOrganizationId));
            await AddAudit(connection, tx, actor, "USER.MANAGER_ORGANIZATION_UPDATED", "USER", id, new { organization = "Amnikon", clientId = EmployeeOrganizationId, role = "MANAGER", fullManagementAccessRetained = true }, ct);
            managersUpdated++;
        }
        await tx.CommitAsync(ct);
        return new { updated, managersUpdated, organization = "Amnikon" };
    }
}
