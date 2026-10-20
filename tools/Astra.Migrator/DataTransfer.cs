using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Astra.Api.Infrastructure;
using Microsoft.Data.SqlClient;
using Npgsql;

internal static class DataTransfer
{
    private static string Identifier(string name) => Regex.IsMatch(name, "^[a-z_][a-z_0-9]*$") ? name : throw new InvalidOperationException("Unsupported database identifier.");

    public static async Task<int> CopyAsync(string databaseName, string schemaFile, string? onlyTable = null, bool verifyOnly = false)
    {
        if (!Regex.IsMatch(databaseName, "^AstraMigration(?:_[A-Za-z0-9]+)?$")) return 2;
        var sourceString = Environment.GetEnvironmentVariable("POSTGRES_MIGRATION_CONNECTION_STRING");
        var sourceUrl = Environment.GetEnvironmentVariable("POSTGRES_MIGRATION_URL");
        if (string.IsNullOrWhiteSpace(sourceString) && !string.IsNullOrWhiteSpace(sourceUrl))
        {
            var uri = new Uri(sourceUrl);
            if (uri.Scheme is not ("postgres" or "postgresql")) return 2;
            var userInfo = uri.UserInfo.Split(':', 2);
            sourceString = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = 15432, Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')), Username = Uri.UnescapeDataString(userInfo[0]), Password = Uri.UnescapeDataString(userInfo[1]), ApplicationName = "ASTRA read-only migration", Timeout = 10, CommandTimeout = 120 }.ConnectionString;
        }
        var destinationString = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(sourceString) || string.IsNullOrWhiteSpace(destinationString)) { Console.Error.WriteLine("Migration connections are not configured."); return 2; }
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(schemaFile))!;
        var targetTypes = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(schemaFile)!, "sql-column-types.json")))!["types"]!;
        var tables = metadata["columns"]!.AsArray().Select(c => c!).GroupBy(c => c["table_name"]!.GetValue<string>()).ToDictionary(g => Identifier(g.Key), g => g.OrderBy(c => c["ordinal_position"]!.GetValue<int>()).ToList());
        if (onlyTable is not null)
        {
            Identifier(onlyTable);
            if (!tables.TryGetValue(onlyTable, out var selected)) return 2;
            tables = new Dictionary<string, List<JsonNode>> { [onlyTable] = selected };
        }
        var destination = new SqlConnectionStringBuilder(destinationString) { InitialCatalog = databaseName };
        string phase = "connect";
        try
        {
            await using var source = new NpgsqlConnection(sourceString);
            await source.OpenAsync();
            await using var sourceTransaction = await source.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", source, sourceTransaction)) await readOnly.ExecuteNonQueryAsync();
            await using var target = new SqlConnection(destination.ConnectionString);
            await target.OpenAsync();
            await using var transaction = (SqlTransaction)await target.BeginTransactionAsync(verifyOnly ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted);
            var report = new Dictionary<string, object?>();
            if (onlyTable is not null)
            {
                var previous = JsonNode.Parse(await File.ReadAllTextAsync("artifacts/data-transfer-result.json"))!;
                if (previous["destination"]!.GetValue<string>() != databaseName || previous["passed"]!.GetValue<bool>() != true || previous["tables"]!.AsObject().ContainsKey(onlyTable)) throw new InvalidOperationException("Supplemental copy requires a verified trial and a previously uncopied table.");
                foreach (var item in previous["tables"]!.AsObject()) report[item.Key] = item.Value!.DeepClone();
            }
            var widenings = new List<string>();
            foreach (var table in tables.Keys)
            {
                if (verifyOnly) continue;
                phase = "preflight:" + table;
                var count = await Database.QueryAsync(target, "SELECT COUNT_BIG(*) AS n FROM dbo.[" + table + "]", transaction: transaction);
                if (table != "app_schema_state" && Convert.ToInt64(count.Single()["n"]) != 0) throw new InvalidOperationException("The isolated target must contain no application records.");
                await Database.ExecuteAsync(target, "ALTER TABLE dbo.[" + table + "] NOCHECK CONSTRAINT ALL", transaction: transaction);
                foreach (var column in tables[table].Where(c => c["udt_name"]!.GetValue<string>() == "text" && c["is_generated"]!.GetValue<string>() != "ALWAYS"))
                {
                    var name = Identifier(column["column_name"]!.GetValue<string>());
                    if (targetTypes[table + "." + name]?.GetValue<string>() != "nvarchar(max)") continue;
                    var actual = await Database.QueryAsync(target, "SELECT max_length FROM sys.columns WHERE object_id=OBJECT_ID(@table) AND name=@column", new Dictionary<string, object?> { ["table"] = "dbo." + table, ["column"] = name }, transaction);
                    if (Convert.ToInt32(actual.Single()["max_length"]) == -1) continue;
                    var nullable = column["is_nullable"]!.GetValue<string>() == "YES" ? " NULL" : " NOT NULL";
                    await Database.ExecuteAsync(target, "ALTER TABLE dbo.[" + table + "] ALTER COLUMN [" + name + "] nvarchar(max)" + nullable, transaction: transaction);
                    widenings.Add(table + "." + name);
                }
            }
            foreach (var (table, columns) in tables)
            {
                phase = "copy:" + table;
                await using var command = new NpgsqlCommand("SELECT " + string.Join(',', columns.Select(c => '"' + Identifier(c["column_name"]!.GetValue<string>()) + '"')) + " FROM public.\"" + table + "\"", source, sourceTransaction);
                await using var reader = await command.ExecuteReaderAsync();
                var buffer = new DataTable(table);
                foreach (var c in columns.Where(c => c["is_generated"]!.GetValue<string>() != "ALWAYS")) buffer.Columns.Add(c["column_name"]!.GetValue<string>(), ClrType(c["udt_name"]!.GetValue<string>()));
                var sourceHashes = new List<string>();
                while (await reader.ReadAsync())
                {
                    var values = new Dictionary<string, object?>();
                    for (var index = 0; index < columns.Count; index++)
                    {
                        var c = columns[index];
                        var value = await reader.IsDBNullAsync(index) ? null : reader.GetValue(index);
                        if (value is Array array && value is not byte[]) value = JsonSerializer.Serialize(array);
                        if (c["udt_name"]!.GetValue<string>() == "timestamptz" && value is DateTime time) value = new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
                        if (value is TimeOnly timeOnly) value = timeOnly.ToTimeSpan();
                        if (value is DateOnly dateOnly) value = dateOnly.ToDateTime(TimeOnly.MinValue);
                        if (value is not null && ClrType(c["udt_name"]!.GetValue<string>()) == typeof(string)) value = Convert.ToString(value, CultureInfo.InvariantCulture);
                        values[c["column_name"]!.GetValue<string>()] = value;
                    }
                    sourceHashes.Add(RowHash(columns, values));
                    var row = buffer.NewRow();
                    foreach (DataColumn column in buffer.Columns) { phase = "copy:" + table + ":" + column.ColumnName; row[column] = values[column.ColumnName] ?? DBNull.Value; }
                    buffer.Rows.Add(row);
                }
                await reader.CloseAsync();
                phase = "copy:" + table;
                if (table == "app_schema_state" && !verifyOnly)
                {
                    if (buffer.Rows.Count != 1 || Convert.ToInt32(buffer.Rows[0]["version"]) != 24) throw new InvalidOperationException("Source schema changed.");
                    await Database.ExecuteAsync(target, "DELETE dbo.app_schema_state", transaction: transaction);
                }
                if (buffer.Rows.Count > 0 && !verifyOnly)
                {
                    phase = "bulk:" + table;
                    using var bulk = new SqlBulkCopy(target, SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.KeepNulls, transaction) { DestinationTableName = "dbo.[" + table + "]", BulkCopyTimeout = 120, BatchSize = 500 };
                    foreach (DataColumn column in buffer.Columns) bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
                    await bulk.WriteToServerAsync(buffer);
                }
                phase = "reconcile:" + table;
                var targetRows = await Database.QueryAsync(target, "SELECT " + string.Join(',', columns.Select(c => '[' + Identifier(c["column_name"]!.GetValue<string>()) + ']')) + " FROM dbo.[" + table + "]", transaction: transaction);
                var targetHashes = targetRows.Select(row => RowHash(columns, row)).Order(StringComparer.Ordinal).ToArray();
                var matched = sourceHashes.Order(StringComparer.Ordinal).SequenceEqual(targetHashes);
                if (!matched && !verifyOnly) throw new InvalidOperationException("Record reconciliation failed.");
                report[table] = new { rows = buffer.Rows.Count, targetRows = targetRows.Count, exactColumnHashesMatched = matched };
            }
            phase = "constraints";
            if (!verifyOnly) foreach (var table in tables.Keys) await Database.ExecuteAsync(target, "ALTER TABLE dbo.[" + table + "] WITH CHECK CHECK CONSTRAINT ALL", transaction: transaction);
            phase = "sequence-reset";
            foreach (var item in onlyTable is null && !verifyOnly ? metadata["sequences"]!.AsArray() : new JsonArray())
            {
                var sequence = item!["sequencename"]!.GetValue<string>();
                if (item["last_value"] is null) continue;
                var next = long.Parse(item["last_value"]!.ToString(), CultureInfo.InvariantCulture) + long.Parse(item["increment_by"]!.ToString(), CultureInfo.InvariantCulture);
                if (sequence is "problem_number_seq" or "change_number_seq") await Database.ExecuteAsync(target, "ALTER SEQUENCE dbo.[" + sequence + "] RESTART WITH " + next.ToString(CultureInfo.InvariantCulture), transaction: transaction);
            }
            if (verifyOnly) await transaction.RollbackAsync(); else await transaction.CommitAsync();
            await sourceTransaction.RollbackAsync();
            var allTables = metadata["columns"]!.AsArray().Select(c => c!["table_name"]!.GetValue<string>()).Distinct().ToArray();
            var changedTables = verifyOnly ? report.Where(p => JsonSerializer.SerializeToNode(p.Value)!["exactColumnHashesMatched"]!.GetValue<bool>() == false).Select(p => p.Key).ToArray() : [];
            var result = new { passed = changedTables.Length == 0, sourceReadOnly = true, destination = databaseName, tables = report, textColumnWidenings = widenings, liveApplicationSwitched = false, finalCutoverCopy = false, allApplicationTablesInventoried = metadata["allApplicationTablesReadable"]?.GetValue<bool>() == true, allInventoriedTablesCopied = allTables.All(report.ContainsKey), supplementalTable = onlyTable, targetReadOnly = verifyOnly, verifiedAt = DateTimeOffset.UtcNow, liveSourceChangedTables = changedTables };
            Directory.CreateDirectory("artifacts");
            await File.WriteAllTextAsync(verifyOnly ? "artifacts/data-verification-result.json" : "artifacts/data-transfer-result.json", JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(new { passed = changedTables.Length == 0, sourceReadOnly = true, targetReadOnly = verifyOnly, tablesExamined = report.Count, liveSourceChangedTables = changedTables, liveApplicationSwitched = false }));
            return changedTables.Length == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { error = "Isolated data transfer failed and was not committed", phase, type = error.GetType().Name, number = error is SqlException sql ? sql.Number : 0 }));
            return 1;
        }
    }

    private static Type ClrType(string type) => type switch
    {
        "uuid" => typeof(Guid),
        "bool" => typeof(bool),
        "int2" => typeof(short),
        "int4" => typeof(int),
        "int8" => typeof(long),
        "bytea" => typeof(byte[]),
        "timestamptz" => typeof(DateTimeOffset),
        "date" => typeof(DateTime),
        "time" => typeof(TimeSpan),
        _ => typeof(string)
    };

    private static string RowHash(List<JsonNode> columns, IReadOnlyDictionary<string, object?> row)
    {
        var parts = columns.Select(c => Canonical(c["udt_name"]!.GetValue<string>(), row[c["column_name"]!.GetValue<string>()]));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts))));
    }

    private static string Canonical(string type, object? value)
    {
        if (value is null || value == DBNull.Value) return "null";
        if (type is "jsonb" or "_text" or "_int2") return "json:" + (Sorted(JsonNode.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!))?.ToJsonString() ?? "null");
        if (value is byte[] bytes) return "bytes:" + Convert.ToHexString(bytes);
        if (value is DateTimeOffset offset) return "time:" + offset.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        if (value is DateTime date) return type == "date" ? "date:" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "time:" + DateTime.SpecifyKind(date, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
        if (value is TimeSpan clock) return "clock:" + clock.ToString("c", CultureInfo.InvariantCulture);
        return type + ":" + Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static JsonNode? Sorted(JsonNode? value) => value switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new KeyValuePair<string, JsonNode?>(p.Key, Sorted(p.Value))).ToArray()),
        JsonArray array => new JsonArray(array.Select(Sorted).ToArray()),
        _ => value?.DeepClone()
    };
}
