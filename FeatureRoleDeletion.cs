using MySqlConnector;

public static partial class FeatureApis
{
    private static void MapRoleDeletion(RouteGroupBuilder api)
    {
        api.MapDelete("/admin/access-policies/roles/{role}", async (string role, [Microsoft.AspNetCore.Mvc.FromBody] DeleteRoleInput body, HttpContext c, MySqlStore s, CancellationToken ct) =>
            Results.Ok(await s.DeleteCustomRole(Actor(c), role, body.Revision, ct)));
    }
}

public sealed class DeleteRoleInput
{
    public int Revision { get; init; }
}

public sealed partial class MySqlStore
{
    internal static string RoleDeletionId(string code, int revision)
    {
        if (revision < 1 || !code.StartsWith("custom-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(code[7..], "D", out var id))
            throw new ArgumentException("Only a saved custom role can be deleted. Built-in roles are protected.");
        return id.ToString("D");
    }

    public async Task<object> DeleteCustomRole(string actor, string code, int revision, CancellationToken ct)
    {
        await RequireAdmin(actor, ct);
        var id = RoleDeletionId(code, revision);
        await using var connection = CreateConnection(); await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        // Match assignment and permission-update lock order. The profile lock and
        // foreign key also prevent a new assignment racing with this deletion.
        await FRows(connection, tx, "SELECT id FROM users ORDER BY id FOR UPDATE", ct);
        var profile = (await FRows(connection, tx, "SELECT name,base_role,revision FROM custom_role_profiles WHERE id=@id FOR UPDATE", ct, ("id", id))).FirstOrDefault()
            ?? throw new KeyNotFoundException("The custom role no longer exists.");
        if (Convert.ToInt32(profile["revision"]) != revision)
            throw new InvalidOperationException("This role changed since you opened it. Refresh permissions before deleting.");
        await using var members = new MySqlCommand("SELECT COUNT(*) FROM user_custom_roles WHERE profile_id=@id", connection, tx);
        members.Parameters.AddWithValue("@id", id);
        if (Convert.ToInt64(await members.ExecuteScalarAsync(ct)) != 0)
            throw new InvalidOperationException("This role is assigned to users. Reassign those users in People before deleting it.");
        var removed = await FExecute(connection, tx, "DELETE FROM custom_role_profiles WHERE id=@id AND revision=@revision", ct, ("id", id), ("revision", revision));
        if (removed != 1) throw new InvalidOperationException("The role changed. Refresh before deleting.");
        await AddAudit(connection, tx, actor, "ROLE.DELETED", "ROLE", id, new { name = profile["name"], baseRole = profile["base_role"] }, ct);
        await tx.CommitAsync(ct);
        return new { deleted = true, code };
    }
}
