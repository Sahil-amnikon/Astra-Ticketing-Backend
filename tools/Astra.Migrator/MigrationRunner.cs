using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astra.Api.Infrastructure;
using Microsoft.Data.SqlClient;

if (args.Length == 3 && args[0] == "copy") return await DataTransfer.CopyAsync(args[1], args[2]);
if (args.Length == 4 && args[0] == "copy-table") return await DataTransfer.CopyAsync(args[1], args[2], args[3]);
if (args.Length == 3 && args[0] == "verify") return await DataTransfer.CopyAsync(args[1], args[2], verifyOnly: true);
if (args.Length == 2 && args[0] == "catalog") return await SourceCatalog.CaptureAsync(args[1]);
if (args.Length == 3 && args[0] == "apply") return await SchemaUpgrade.ApplyAsync(args[1], args[2]);

if (args.Length != 2 || !Regex.IsMatch(args[0], "^AstraMigration(?:_[A-Za-z0-9]+)?$"))
{
    Console.Error.WriteLine("Usage: Astra.Migrator AstraMigration[_testname] schema.sql");
    return 2;
}
var connectionString = Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString)) { Console.Error.WriteLine("SQL migration test connection is not configured."); return 2; }
var script = await File.ReadAllTextAsync(args[1]);
var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
var databaseName = args[0];
var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
try
{
    await using (var master = new SqlConnection(builder.ConnectionString))
    {
        await master.OpenAsync();
        var exists = await Database.QueryAsync(master, "SELECT name FROM sys.databases WHERE name=@name", new Dictionary<string, object?> { ["name"] = databaseName });
        if (exists.Count == 0) await Database.ExecuteAsync(master, "CREATE DATABASE [" + databaseName + "] COLLATE Latin1_General_100_CS_AS_SC; ALTER DATABASE [" + databaseName + "] SET COMPATIBILITY_LEVEL=170;");
    }
    builder.InitialCatalog = databaseName;
    await using var connection = new SqlConnection(builder.ConnectionString);
    await connection.OpenAsync();
    var existing = await Database.QueryAsync(connection, "SELECT COUNT(*) AS tables FROM sys.tables");
    if (Convert.ToInt32(existing.Single()["tables"]) != 0) throw new InvalidOperationException("Schema application requires an empty isolated migration database.");
    await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
    var batches = Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(s => !string.IsNullOrWhiteSpace(s));
    var batch = 0;
    try
    {
        foreach (var sql in batches) { batch++; await Database.ExecuteAsync(connection, sql, transaction: transaction); }
        await Database.ExecuteAsync(connection, "INSERT dbo.app_schema_state(id,version) VALUES(1,24); CREATE TABLE dbo.astra_migration_history(name nvarchar(100) NOT NULL PRIMARY KEY, checksum char(64) NOT NULL, applied_at datetimeoffset(7) NOT NULL DEFAULT SYSUTCDATETIME()); INSERT dbo.astra_migration_history(name,checksum) VALUES(N'001_schema',@checksum)", new Dictionary<string, object?> { ["checksum"] = checksum }, transaction);
        await transaction.CommitAsync();
    }
    catch (SqlException error)
    {
        try { await transaction.RollbackAsync(); } catch (SqlException) { }
        Console.Error.WriteLine(JsonSerializer.Serialize(new { error = "Schema batch failed", batch, number = error.Number, line = error.LineNumber, message = error.Message }));
        return 1;
    }
    var tables = await Database.QueryAsync(connection, "SELECT COUNT(*) AS tables FROM sys.tables");
    Console.WriteLine(JsonSerializer.Serialize(new { database = databaseName, schemaTables = tables.Single()["tables"], schemaChecksum = checksum, liveApplicationSwitched = false }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { error = "Migration setup failed", type = error.GetType().Name }));
    return 1;
}
