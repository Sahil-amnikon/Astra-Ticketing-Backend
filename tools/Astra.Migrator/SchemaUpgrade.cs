using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astra.Api.Infrastructure;
using Microsoft.Data.SqlClient;

internal static class SchemaUpgrade
{
    public static async Task<int> ApplyAsync(string database, string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        if (!Regex.IsMatch(database, "^AstraMigration(?:_[A-Za-z0-9]+)?$") || !Regex.IsMatch(name, "^[0-9]{3}_[a-z_]+$")) return 2;
        var value = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(value)) return 2;
        var script = await File.ReadAllTextAsync(file);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        var batch = 0;
        try
        {
            await using var connection = new SqlConnection(new SqlConnectionStringBuilder(value) { InitialCatalog = database }.ConnectionString);
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await Database.ExecuteAsync(connection, "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'ASTRA schema upgrade',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @result<0 THROW 51000,'Upgrade unavailable',1;", transaction: transaction);
            var parameters = new Dictionary<string, object?> { ["name"] = name, ["checksum"] = checksum };
            var existing = await Database.QueryAsync(connection, "SELECT checksum FROM dbo.astra_migration_history WHERE name=@name", parameters, transaction);
            if (existing.Count > 0)
            {
                if ((string)existing[0]["checksum"]! != checksum) throw new InvalidOperationException("Applied schema checksum differs.");
                await transaction.RollbackAsync();
                Console.WriteLine(JsonSerializer.Serialize(new { migration = name, alreadyApplied = true }));
                return 0;
            }
            foreach (var sql in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(s => !string.IsNullOrWhiteSpace(s)))
            { batch++; await Database.ExecuteAsync(connection, sql, transaction: transaction); }
            await Database.ExecuteAsync(connection, "INSERT dbo.astra_migration_history(name,checksum) VALUES(@name,@checksum)", parameters, transaction);
            await transaction.CommitAsync();
            Console.WriteLine(JsonSerializer.Serialize(new { migration = name, checksum, passed = true, liveApplicationSwitched = false }));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { error = "Isolated schema upgrade was not committed", batch, type = error.GetType().Name, number = error is SqlException sql ? sql.Number : 0, line = error is SqlException detail ? detail.LineNumber : 0 }));
            return 1;
        }
    }
}
