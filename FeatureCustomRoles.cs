using System.Text.Json;
using MySqlConnector;

public static partial class FeatureApis
{
    private static void MapCustomRoles(RouteGroupBuilder api)
    {
        MapRoleDeletion(api);
        api.MapPost("/admin/access-policies/roles", async (CustomRoleInput body, HttpContext c, MySqlStore s, CancellationToken ct) =>
            Results.Ok(await s.CreateCustomRole(Actor(c), body, ct)));
        api.MapGet("/admin/custom-roles/assignments/{id:guid}", async (string id, HttpContext c, MySqlStore s, CancellationToken ct) =>
            Results.Ok(await s.CustomRoleAssignment(Actor(c), id, ct)));
    }
}

public sealed class CustomRoleInput
{
    public string Name { get; init; } = "";
    public string BaseRole { get; init; } = "TECHNICIAN";
    public string[] Permissions { get; init; } = [];
}

public sealed partial class MySqlStore
{
    internal static readonly string[] CustomRoleBases = ["USER", "INTERN", "TECHNICIAN", "TEAM_LEAD", "MANAGER", "ADMIN"];
    internal static string ValidateCustomRoleName(string? name)
    {
        name = (name ?? "").Trim();
        if (name.Length is < 2 or > 80 || name.Any(char.IsControl))
            throw new ArgumentException("Enter a role name between 2 and 80 characters.");
        string[] reserved = [..CustomRoleBases, "Client user", "Intern", "Technician", "Team Lead", "Manager", "Administrator"];
        if (reserved.Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Use a name different from an existing built-in role.");
        return name;
    }
    private static string[] CustomRolePermissions(string baseRole, IEnumerable<string>? permissions)
    {
        if (!CustomRoleBases.Contains(baseRole)) throw new ArgumentException("Choose an existing base role.");
        var available = AvailableRolePolicy(baseRole);
        if (permissions is null) throw new ArgumentException("Choose valid role permissions.");
        var selected = permissions.Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Length > 12 || selected.Except(available, StringComparer.Ordinal).Any())
            throw new ArgumentException("Choose permissions supported by this role's base profile.");
        // Fixed base profiles keep their existing behavior, including full-access profiles.
        if (baseRole is "ADMIN" or "MANAGER" or "USER" or "INTERN") return available;
        return selected;
    }
    private async Task<List<object>> ReadCustomRoles(CancellationToken ct)
    {
        var result = new List<object>();
        foreach (var row in await FQuery("SELECT p.*,(SELECT COUNT(*) FROM user_custom_roles m JOIN users u ON u.id=m.user_id WHERE m.profile_id=p.id) AS users FROM custom_role_profiles p ORDER BY p.name", ct))
        {
            var baseRole = Convert.ToString(row["base_role"])!;
            result.Add(new { code = "custom-" + row["id"], name = row["name"], baseRole,
                permissions = CustomRolePermissions(baseRole, ParsePermissionArray(Convert.ToString(row["permissions"])!)),
                available = AvailableRolePolicy(baseRole), defaults = DefaultRolePolicy(baseRole),
                revision = Convert.ToInt32(row["revision"]), users = Convert.ToInt32(row["users"]),
                editable = baseRole is "TECHNICIAN" or "TEAM_LEAD", custom = true });
        }
        return result;
    }
    public async Task<object> CreateCustomRole(string actor, CustomRoleInput body, CancellationToken ct)
    {
        await RequireAdmin(actor, ct);
        var name = ValidateCustomRoleName(body.Name);
        var permissions = CustomRolePermissions(body.BaseRole, body.Permissions);
        var id = Guid.NewGuid().ToString("D");
        await using var connection = CreateConnection(); await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            await FExecute(connection, tx, "INSERT INTO custom_role_profiles(id,name,base_role,permissions,created_by) VALUES(@id,@name,@base,@permissions,@actor)", ct,
                ("id", id), ("name", name), ("base", body.BaseRole), ("permissions", JsonSerializer.Serialize(permissions)), ("actor", actor));
        }
        catch (MySqlException e) when (e.Number == 1062) { throw new InvalidOperationException("A role with this name already exists."); }
        await AddAudit(connection, tx, actor, "ROLE.CREATED", "ROLE", id, new { name, baseRole = body.BaseRole, permissions }, ct);
        await tx.CommitAsync(ct);
        return new { code = "custom-" + id, name, baseRole = body.BaseRole };
    }
    public async Task<object> CustomRoleAssignment(string actor, string id, CancellationToken ct)
    {
        await RequireAdmin(actor, ct);
        var rows = await FQuery("SELECT p.id,p.name,p.base_role FROM user_custom_roles m JOIN custom_role_profiles p ON p.id=m.profile_id WHERE m.user_id=@id", ct, ("id", id));
        return new { profile = rows.FirstOrDefault() };
    }
    private static async Task WriteCustomRolePolicy(MySqlConnection connection, MySqlTransaction tx, string actor, string user, string baseRole, string[] permissions, CancellationToken ct)
    {
        var allow = CustomRolePermissions(baseRole, permissions);
        var deny = AvailableRolePolicy(baseRole).Except(allow, StringComparer.Ordinal).ToArray();
        await FExecute(connection, tx, "INSERT INTO user_access_policies(user_id,allow_permissions,deny_permissions,revision,updated_by) VALUES(@user,@allow,@deny,1,@actor) ON DUPLICATE KEY UPDATE allow_permissions=@allow,deny_permissions=@deny,revision=revision+1,updated_by=@actor,updated_at=UTC_TIMESTAMP(6)", ct,
            ("user", user), ("allow", JsonSerializer.Serialize(allow)), ("deny", JsonSerializer.Serialize(deny)), ("actor", actor));
    }
    private static async Task ApplyCustomRoleAssignment(MySqlConnection connection, MySqlTransaction tx, string actor, string user, string role, string? profile, CancellationToken ct)
    {
        var previous = await FRows(connection, tx, "SELECT profile_id FROM user_custom_roles WHERE user_id=@id", ct, ("id", user));
        if (profile is null)
        {
            if (previous.Count != 0)
            {
                await FExecute(connection, tx, "DELETE FROM user_custom_roles WHERE user_id=@id", ct, ("id", user));
                await FExecute(connection, tx, "DELETE FROM user_access_policies WHERE user_id=@id", ct, ("id", user));
            }
            return;
        }
        var row = (await FRows(connection, tx, "SELECT base_role,permissions FROM custom_role_profiles WHERE id=@id FOR UPDATE", ct, ("id", profile))).FirstOrDefault();
        if (row is null || Convert.ToString(row["base_role"]) != role)
            throw new ArgumentException("The custom role does not match the selected base role. Refresh and select it again.");
        await FExecute(connection, tx, "INSERT INTO user_custom_roles(user_id,profile_id) VALUES(@user,@profile) ON DUPLICATE KEY UPDATE profile_id=@profile", ct, ("user", user), ("profile", profile));
        await WriteCustomRolePolicy(connection, tx, actor, user, role, ParsePermissionArray(Convert.ToString(row["permissions"])!), ct);
    }
    private async Task<object?> SaveCustomRolePolicy(string actor, string target, int revision, IReadOnlyList<string> permissions, CancellationToken ct)
    {
        if (!await IsAdministrator(actor, ct)) return null;
        if (!Guid.TryParse(target[7..], out var profileId) || revision < 0 || revision == int.MaxValue)
            throw new ArgumentException("Invalid custom role.");
        var id = profileId.ToString("D");
        await using var connection = CreateConnection(); await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        // Same lock order as employee access changes.
        await FRows(connection, tx, "SELECT id FROM users ORDER BY id FOR UPDATE", ct);
        var row = (await FRows(connection, tx, "SELECT * FROM custom_role_profiles WHERE id=@id FOR UPDATE", ct, ("id", id))).FirstOrDefault()
            ?? throw new ArgumentException("Unknown custom role.");
        var baseRole = Convert.ToString(row["base_role"])!;
        if (baseRole is not ("TECHNICIAN" or "TEAM_LEAD")) throw new ArgumentException("This role inherits a fixed base profile.");
        if (Convert.ToInt32(row["revision"]) != revision) throw new InvalidOperationException("The role changed. Refresh before saving.");
        var selected = CustomRolePermissions(baseRole, permissions);
        await FExecute(connection, tx, "UPDATE custom_role_profiles SET permissions=@permissions,revision=revision+1,updated_at=UTC_TIMESTAMP(6) WHERE id=@id", ct, ("permissions", JsonSerializer.Serialize(selected)), ("id", id));
        var members = await FRows(connection, tx, "SELECT m.user_id FROM user_custom_roles m JOIN users u ON u.id=m.user_id JOIN user_roles r ON r.user_id=u.id AND r.role_code=@base WHERE m.profile_id=@id", ct, ("id", id), ("base", baseRole));
        foreach (var member in members)
        {
            var user = Convert.ToString(member["user_id"])!;
            await WriteCustomRolePolicy(connection, tx, actor, user, baseRole, selected, ct);
            await FExecute(connection, tx, "UPDATE users SET session_version=session_version+1 WHERE id=@id", ct, ("id", user));
        }
        await AddAudit(connection, tx, actor, "ROLE.PERMISSIONS_UPDATED", "ROLE", id, new { name = row["name"], permissions = selected, affectedUsers = members.Count }, ct);
        await tx.CommitAsync(ct);
        return new { saved = true, revision = revision + 1 };
    }
    private async Task AddCustomRoleNames(List<Dictionary<string, object?>> users, CancellationToken ct)
    {
        var bindings = await FQuery("SELECT m.user_id,p.id,p.name FROM user_custom_roles m JOIN custom_role_profiles p ON p.id=m.profile_id", ct);
        var names = bindings.ToDictionary(row => Convert.ToString(row["user_id"])!, StringComparer.OrdinalIgnoreCase);
        foreach (var user in users)
            if (names.TryGetValue(Convert.ToString(user["id"])!, out var binding))
            { user["custom_role_id"] = binding["id"]; user["custom_role_name"] = binding["name"]; }
    }
}
