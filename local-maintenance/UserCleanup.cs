using System.Text.Json;
using MySqlConnector;

// Applies only to the exact roster reviewed by the administrator. No FK checks are disabled.
var keep = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
    ["493631ad-fc93-493d-9e10-bb18eda281ed"] = "mohammed.sahil@amnikontechnologies.com",
    ["3d3ea7e7-2352-446a-a67b-150b895d2392"] = "hamza.rajmohammed@amnikontechnologies.com"
};
string[] approvedRemovalIds = [
    "0f258942-a4dc-4b59-ba98-aa3deda83a9b", "10000000-0000-4000-8000-000000000001",
    "2d1986aa-e763-41e5-9467-b66601b8db8e", "07602e95-f206-4c69-a462-15a2b8c62ae1",
    "6ef825ad-2869-40ac-b9d7-f0f0cd4734c8", "672a3dad-7f83-42e3-8d71-eedf81c99d72",
    "79ef2c82-df82-45c5-8c4a-67f9c364c0af", "cf5686df-bb21-4e2c-82d9-25b2fc4a0349",
    "dac8da07-8b4b-48cf-a9e6-971944c1890f", "6d355029-dde6-40be-945a-99d6b5a27b02",
    "0a10caa9-300a-4a17-8840-0ef73c25ce21", "2ebea7ef-9bd2-4990-9545-b3bd4c24f20f",
    "ab30803d-1bf1-49b2-9290-d92a1b582648", "43d108e7-a710-4580-bf72-a624240dc352",
    "3ef60385-1775-46b4-b6e3-a7ce4c143e7e", "abe255b5-4c6a-4673-9003-8dffaedae35d",
    "07164c75-de99-4f37-9838-be49a278d121", "e76d9bc1-8b1a-4cf2-b5c3-2b1194661b9b",
    "55238735-da46-4f2c-8f72-d51dfdd4d800", "2d0e3807-242b-4b88-86c4-988707bc5562",
    "b71e77e0-20c0-4c46-809a-ce649944e95a", "351dedda-dbdb-4c13-8788-286b68823367",
    "10000000-0000-4000-8000-000000000002", "560d36fa-8883-4931-8bf2-d3fba6ea13b3"
];
if (args.Contains("--self-test")) {
    await CleanupTests.Run();
    return 0;
}
if (!args.SequenceEqual(new[] { "--apply-reviewed-cleanup" })) {
    Console.Error.WriteLine("STOP: explicit reviewed cleanup flag is required."); return 2;
}
bool commitStarted = false;
try {
    var raw = Environment.GetEnvironmentVariable("ASTRA_INSPECTION_CONNECTION") ?? throw new InvalidOperationException("Missing private configuration");
    var target = new MySqlConnectionStringBuilder(raw);
    if (target.Server != "172.31.4.5" || target.Database != "astra_ticketing")
        throw new InvalidOperationException("Unexpected database target");
    await using var connection = new MySqlConnection(raw);
    await connection.OpenAsync();
    await using var tx = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    async Task<List<Dictionary<string, object?>>> Read(string sql, params (string Key, object? Value)[] parameters) {
        await using var cmd = new MySqlCommand(sql, connection, tx) { CommandTimeout = 180 };
        foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync()) {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }
    async Task<int> Execute(string sql, params (string Key, object? Value)[] parameters) {
        await using var cmd = new MySqlCommand(sql, connection, tx) { CommandTimeout = 180 };
        foreach (var p in parameters) cmd.Parameters.AddWithValue(p.Key, p.Value ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync();
    }
    var users = await Read("SELECT id,email,active FROM users ORDER BY id FOR UPDATE");
    CleanupGuards.CheckRoster(users, keep, approvedRemovalIds);
    var engines = await Read("SELECT TABLE_NAME,ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_TYPE='BASE TABLE'");
    if (engines.Any(r => Convert.ToString(r["ENGINE"]) != "InnoDB"))
        throw new InvalidOperationException("Nontransactional database tables require separate review");
    var pkRows = await Read("SELECT TABLE_NAME,COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE WHERE CONSTRAINT_SCHEMA=DATABASE() AND CONSTRAINT_NAME='PRIMARY' ORDER BY TABLE_NAME,ORDINAL_POSITION");
    var keys = pkRows.GroupBy(r => (string)r["TABLE_NAME"]!).ToDictionary(g => g.Key, g => g.Select(r => (string)r["COLUMN_NAME"]!).ToArray());
    var fkRows = await Read(@"SELECT k.CONSTRAINT_SCHEMA,k.CONSTRAINT_NAME,k.TABLE_NAME,k.COLUMN_NAME,k.REFERENCED_TABLE_NAME,k.REFERENCED_COLUMN_NAME,r.DELETE_RULE
      FROM information_schema.KEY_COLUMN_USAGE k JOIN information_schema.REFERENTIAL_CONSTRAINTS r
      ON r.CONSTRAINT_SCHEMA=k.CONSTRAINT_SCHEMA AND r.CONSTRAINT_NAME=k.CONSTRAINT_NAME AND r.TABLE_NAME=k.TABLE_NAME
      WHERE k.REFERENCED_TABLE_SCHEMA=DATABASE() ORDER BY k.CONSTRAINT_SCHEMA,k.TABLE_NAME,k.CONSTRAINT_NAME,k.ORDINAL_POSITION");
    if (fkRows.Any(r => (string)r["CONSTRAINT_SCHEMA"]! != target.Database))
        throw new InvalidOperationException("Cross-database relationships require separate review");
    var edges = fkRows.GroupBy(r => (Schema: (string)r["CONSTRAINT_SCHEMA"]!, Name: (string)r["CONSTRAINT_NAME"]!, Table: (string)r["TABLE_NAME"]!))
        .Select(g => new CleanupEdge((string)g.First()["REFERENCED_TABLE_NAME"]!, g.Key.Table,
            (string)g.First()["DELETE_RULE"]!, g.Select(r => ((string)r["COLUMN_NAME"]!, (string)r["REFERENCED_COLUMN_NAME"]!)).ToArray())).ToArray();
    var planner = new CleanupPlanner(keys, edges, keep.Keys.ToHashSet(), async (table, predicate) => {
        var columns = keys.TryGetValue(table, out var pk) ? pk.ToHashSet() : throw new InvalidOperationException("Missing primary key: " + table);
        foreach (var edge in edges.Where(e => e.Parent == table)) foreach (var pair in edge.Columns) columns.Add(pair.Parent);
        var where = string.Join(" AND ", predicate.Select((p, i) => CleanupSql.Quote(p.Key) + " <=> @p" + i));
        return await Read("SELECT " + string.Join(',', columns.Select(CleanupSql.Quote)) + " FROM " + CleanupSql.Quote(table) + " WHERE " + where + " FOR UPDATE",
            predicate.Select((p, i) => ("p" + i, p.Value)).ToArray());
    });
    foreach (string id in approvedRemovalIds) await planner.Include("users", new() { ["id"] = id });
    if (planner.Rows.Count(r => r.Table == "users") != 24)
        throw new InvalidOperationException("Unexpected account deletion plan");
    var counts = planner.Rows.GroupBy(r => r.Table).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
    Console.WriteLine("Applying approved removal, in one database transaction:");
    foreach (var item in counts) Console.WriteLine(item.Key + ": " + item.Value);
    foreach (var row in planner.Rows) {
        var where = string.Join(" AND ", row.Key.Select((p, i) => CleanupSql.Quote(p.Key) + " <=> @p" + i));
        int changed = await Execute("DELETE FROM " + CleanupSql.Quote(row.Table) + " WHERE " + where,
            row.Key.Select((p, i) => ("p" + i, p.Value)).ToArray());
        if (changed != 1) throw new InvalidOperationException("A planned record changed; transaction must roll back");
    }
    foreach (var id in keep.Keys) {
        await Execute("DELETE FROM user_roles WHERE user_id=@id AND role_code<>'ADMIN'", ("id", id));
        await Execute("INSERT IGNORE INTO user_roles(user_id,role_code) VALUES(@id,'ADMIN')", ("id", id));
        if (await Execute("UPDATE users SET active=1,access_expires_at=NULL,work_scope='ALL',change_access='CHANGE_MANAGER',session_version=session_version+1,updated_at=UTC_TIMESTAMP(6) WHERE id=@id", ("id", id)) != 1)
            throw new InvalidOperationException("Retained account verification failed");
    }
    var verified = await Read(@"SELECT u.id,u.email,u.active,u.work_scope,u.change_access,
      (SELECT GROUP_CONCAT(role_code ORDER BY role_code) FROM user_roles WHERE user_id=u.id) AS roles FROM users u ORDER BY email FOR UPDATE");
    CleanupGuards.CheckRetained(verified, keep);
    await Execute("INSERT INTO audit_logs(actor_user_id,action,entity_type,entity_id,details) VALUES(@actor,'USER.CLEANUP','USER',NULL,CAST(@details AS JSON))",
        ("actor", "493631ad-fc93-493d-9e10-bb18eda281ed"),
        ("details", JsonSerializer.Serialize(new { removedUsers = 24, keptEmails = keep.Values, deletedRecordCounts = counts, retainedRole = "ADMIN", retainedScope = "ALL", backupsCreated = false })));
    commitStarted = true;
    await tx.CommitAsync();
    Console.WriteLine("DATABASE COMMITTED: 24 accounts removed; exactly two active full administrators remain.");
    foreach (var row in verified) Console.WriteLine("KEEP VERIFIED: " + JsonSerializer.Serialize(row));
    Console.WriteLine("Both users must sign out and sign in again. No backup created.");
    return 0;
} catch (Exception e) {
    Console.Error.WriteLine(commitStarted
        ? "COMMIT RESULT UNCERTAIN. Inspect the two retained accounts before retrying. Error type: " + e.GetType().Name
        : "CLEANUP NOT COMMITTED. Database transaction was rolled back: " + e.GetType().Name);
    if (e is InvalidOperationException) Console.Error.WriteLine(e.Message);
    if (e is MySqlException sql) Console.Error.WriteLine("MySQL error number: " + sql.Number);
    for (Exception? cause = e.InnerException; cause != null; cause = cause.InnerException) {
        Console.Error.WriteLine("Cause type: " + cause.GetType().Name);
        if (cause is FileNotFoundException missing) Console.Error.WriteLine("Missing assembly: " + Path.GetFileName(missing.FileName));
    }
    return 1;
}

record CleanupEdge(string Parent, string Child, string Rule, (string Child, string Parent)[] Columns);
record CleanupRow(string Table, Dictionary<string, object?> Key);
static class CleanupSql {
    public static string Quote(string name) => "`" + name.Replace("`", "``") + "`";
}
static class CleanupGuards {
    public static void CheckRetained(List<Dictionary<string, object?>> users, Dictionary<string, string> keep) {
        // MySqlConnector can return CHAR(36) IDs as Guid values rather than strings.
        if (users.Count != 2 || users.Any(r => !keep.TryGetValue(Convert.ToString(r["id"]) ?? "", out var email) ||
            !email.Equals(Convert.ToString(r["email"]), StringComparison.OrdinalIgnoreCase) ||
            !Convert.ToBoolean(r["active"]) || Convert.ToString(r["work_scope"]) != "ALL" ||
            Convert.ToString(r["change_access"]) != "CHANGE_MANAGER" || Convert.ToString(r["roles"]) != "ADMIN"))
            throw new InvalidOperationException("Final verification failed; transaction must roll back");
    }
    public static void CheckRoster(List<Dictionary<string, object?>> users, Dictionary<string, string> keep, string[] remove) {
        if (keep.Count != 2 || remove.Length != 24 || remove.Distinct().Count() != 24 || keep.Keys.Intersect(remove).Any())
            throw new InvalidOperationException("Invalid approved roster");
        if (users.Count != 26 || !users.Select(r => Convert.ToString(r["id"])!).ToHashSet().SetEquals(keep.Keys.Concat(remove)))
            throw new InvalidOperationException("Roster has changed since review; no cleanup permitted");
        foreach (var item in keep) {
            var matches = users.Where(r => Convert.ToString(r["id"]) == item.Key).ToList();
            if (matches.Count != 1 || !item.Value.Equals(Convert.ToString(matches[0]["email"]), StringComparison.OrdinalIgnoreCase) || !Convert.ToBoolean(matches[0]["active"]))
                throw new InvalidOperationException("Retained account identity is different or inactive");
        }
    }
}
sealed class CleanupPlanner(Dictionary<string, string[]> keys, CleanupEdge[] edges, HashSet<string> protectedUsers,
    Func<string, Dictionary<string, object?>, Task<List<Dictionary<string, object?>>>> read) {
    readonly HashSet<string> visiting = [];
    readonly HashSet<string> visited = [];
    public readonly List<CleanupRow> Rows = [];
    public async Task Include(string table, Dictionary<string, object?> predicate) {
        if (!keys.TryGetValue(table, out var pk)) throw new InvalidOperationException("Missing primary key: " + table);
        foreach (var row in await read(table, predicate)) {
            var key = pk.ToDictionary(k => k, k => row[k]);
            var identity = table + ":" + JsonSerializer.Serialize(key);
            if (table == "users" && protectedUsers.Contains(Convert.ToString(row["id"])!))
                throw new InvalidOperationException("Cleanup would touch a protected account");
            if (visited.Contains(identity)) continue;
            if (!visiting.Add(identity)) throw new InvalidOperationException("Cyclic relationships require separate review");
            if (visited.Count + visiting.Count > 100000) throw new InvalidOperationException("Cleanup scope exceeds the safety limit");
            foreach (var edge in edges.Where(e => e.Parent == table && e.Rule != "SET NULL")) {
                if (edge.Rule is not ("CASCADE" or "RESTRICT" or "NO ACTION")) throw new InvalidOperationException("Unsupported deletion rule");
                if (edge.Columns.Any(pair => row[pair.Parent] is null)) continue;
                await Include(edge.Child, edge.Columns.ToDictionary(pair => pair.Child, pair => row[pair.Parent]));
            }
            visiting.Remove(identity); visited.Add(identity); Rows.Add(new(table, key));
        }
    }
}
static class CleanupTests {
    public static async Task Run() {
        var keys = new Dictionary<string, string[]> { ["users"] = ["id"], ["tickets"] = ["id"], ["comments"] = ["id"], ["audit"] = ["id"] };
        var data = new Dictionary<string, List<Dictionary<string, object?>>> {
            ["users"] = [new() { ["id"] = "drop" }, new() { ["id"] = "keep" }],
            ["tickets"] = [new() { ["id"] = "ticket", ["owner"] = "drop" }],
            ["comments"] = [new() { ["id"] = "comment", ["ticket"] = "ticket", ["author"] = "drop" }],
            ["audit"] = [new() { ["id"] = "log", ["actor"] = "drop" }]
        };
        Task<List<Dictionary<string, object?>>> Read(string table, Dictionary<string, object?> where) => Task.FromResult(data[table].Where(r => where.All(p => Equals(r[p.Key], p.Value))).ToList());
        CleanupEdge[] edges = [new("users", "tickets", "RESTRICT", [("owner", "id")]), new("tickets", "comments", "CASCADE", [("ticket", "id")]),
            new("users", "comments", "RESTRICT", [("author", "id")]), new("users", "audit", "SET NULL", [("actor", "id")])];
        var planner = new CleanupPlanner(keys, edges, ["keep"], Read);
        await planner.Include("users", new() { ["id"] = "drop" });
        if (!planner.Rows.Select(r => r.Table).SequenceEqual(new[] { "comments", "tickets", "users" })) throw new Exception("Child ordering, deduplication or SET NULL handling failed");
        bool protectedStopped = false;
        try { await new CleanupPlanner(keys, edges, ["keep"], Read).Include("users", new() { ["id"] = "keep" }); }
        catch (InvalidOperationException) { protectedStopped = true; }
        if (!protectedStopped) throw new Exception("Protected account guard failed");
        bool cycleStopped = false;
        var cycle = edges.Append(new CleanupEdge("comments", "users", "RESTRICT", [("id", "author")])).ToArray();
        try { await new CleanupPlanner(keys, cycle, ["keep"], Read).Include("users", new() { ["id"] = "drop" }); }
        catch (InvalidOperationException) { cycleStopped = true; }
        if (!cycleStopped) throw new Exception("Cycle guard failed");
        bool changedStopped = false;
        try { CleanupGuards.CheckRoster([], new() { ["a"] = "a", ["b"] = "b" }, Enumerable.Range(0,24).Select(x => x.ToString()).ToArray()); }
        catch (InvalidOperationException) { changedStopped = true; }
        if (!changedStopped) throw new Exception("Changed roster guard failed");
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var keepAccounts = new Dictionary<string, string> { [first.ToString()] = "first@example.invalid", [second.ToString()] = "second@example.invalid" };
        List<Dictionary<string, object?>> keptRows = [
            new() { ["id"] = first, ["email"] = "first@example.invalid", ["active"] = true, ["work_scope"] = "ALL", ["change_access"] = "CHANGE_MANAGER", ["roles"] = "ADMIN" },
            new() { ["id"] = second.ToString(), ["email"] = "second@example.invalid", ["active"] = (sbyte)1, ["work_scope"] = "ALL", ["change_access"] = "CHANGE_MANAGER", ["roles"] = "ADMIN" }
        ];
        CleanupGuards.CheckRetained(keptRows, keepAccounts);
        keptRows[0]["roles"] = "USER";
        bool invalidAdminStopped = false;
        try { CleanupGuards.CheckRetained(keptRows, keepAccounts); }
        catch (InvalidOperationException) { invalidAdminStopped = true; }
        if (!invalidAdminStopped) throw new Exception("Administrator verification guard failed");
        Console.WriteLine("PASS: dependent-first order, duplicate references, SET NULL retention, protected accounts, cycles, roster changes, GUID/string IDs and administrator verification.");
    }
}
