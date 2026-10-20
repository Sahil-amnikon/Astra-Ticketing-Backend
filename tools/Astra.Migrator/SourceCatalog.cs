using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

internal static class SourceCatalog
{
    public static async Task<int> CaptureAsync(string file)
    {
        try
        {
            var configured = SourceConnection.Read(); if (configured is null) return 2;
            await using var connection = new NpgsqlConnection(configured); await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            await using (var mode = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction)) await mode.ExecuteNonQueryAsync();
            async Task<List<Dictionary<string, object?>>> Query(string sql)
            {
                await using var command = new NpgsqlCommand(sql, connection, transaction); await using var reader = await command.ExecuteReaderAsync();
                var result = new List<Dictionary<string, object?>>();
                while (await reader.ReadAsync())
                {
                    var row = new Dictionary<string, object?>(); for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i); result.Add(row);
                }
                return result;
            }
            var relations = await Query("SELECT c.relname AS table_name,has_table_privilege(current_user,c.oid,'SELECT') AS readable FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind IN('r','p') AND c.relname NOT IN('knex_migrations','knex_migrations_lock') ORDER BY c.relname");
            if (relations.Any(r => !(bool)r["readable"]!)) throw new InvalidOperationException("The source account cannot read all application tables.");
            var columns = await Query("SELECT table_name,column_name,ordinal_position,data_type,udt_name,is_nullable,column_default,character_maximum_length,is_generated,generation_expression FROM information_schema.columns WHERE table_schema='public' AND table_name NOT IN('knex_migrations','knex_migrations_lock') ORDER BY table_name,ordinal_position");
            var constraints = await Query("SELECT c.relname AS table_name,p.conname AS name,p.contype::text AS kind,pg_get_constraintdef(p.oid) AS definition FROM pg_constraint p JOIN pg_class c ON c.oid=p.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' ORDER BY c.relname,p.conname");
            var indexes = await Query("SELECT tablename AS table_name,indexname AS name,indexdef AS definition FROM pg_indexes WHERE schemaname='public' ORDER BY tablename,indexname");
            var sequences = await Query("SELECT * FROM pg_sequences WHERE schemaname='public' ORDER BY sequencename");
            var counts = new Dictionary<string, long>();
            foreach (var table in relations)
            {
                var name = (string)table["table_name"]!; if (!Regex.IsMatch(name, "^[a-z_][a-z_0-9]*$")) throw new InvalidOperationException("Unsupported source table.");
                var result = await Query("SELECT COUNT(*) AS n FROM public.\"" + name + "\""); counts[name] = (long)result[0]["n"]!;
                if (!columns.Any(c => (string)c["table_name"]! == name)) throw new InvalidOperationException("Column inventory is incomplete.");
            }
            var version = await Query("SELECT version FROM public.app_schema_state WHERE id=1");
            var resultMetadata = new { schemaVersion = version.Single()["version"], capturedAt = DateTimeOffset.UtcNow, sourceReadOnly = true, allApplicationTablesReadable = true, excludedInfrastructureTables = new[] { "knex_migrations", "knex_migrations_lock" }, counts, columns, constraints, indexes, sequences };
            await transaction.RollbackAsync();
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(resultMetadata, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(new { sourceReadOnly = true, applicationTables = relations.Count, columns = columns.Count, allTablesReadable = true }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(JsonSerializer.Serialize(new { error = "Source inventory failed", type = error.GetType().Name })); return 1; }
    }
}
