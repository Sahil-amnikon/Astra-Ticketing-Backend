using Astra.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Astra.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING"))) Skip = "SQL Server integration connection is not configured.";
    }
}

public class SqlServerTests
{
    private static SqlConnection Connection() => new(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING"));

    [SqlServerFact]
    public async Task ConnectsToMicrosoftSqlServer2025()
    {
        await using var connection = Connection();
        await connection.OpenAsync();
        var rows = await Database.QueryAsync(connection, "SELECT CONVERT(int, SERVERPROPERTY('ProductMajorVersion')) AS version, CONVERT(nvarchar(128), SERVERPROPERTY('Edition')) AS edition");
        Assert.True(Convert.ToInt32(rows.Single()["version"]) >= 17);
        Assert.Contains("Developer", Convert.ToString(rows.Single()["edition"]));
    }

    [SqlServerFact]
    public async Task ParametersPreserveUuidUnicodeAndDatesWithoutSqlInterpolation()
    {
        await using var connection = Connection();
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await Database.ExecuteAsync(connection, "CREATE TABLE #astra_parameter_test(id uniqueidentifier NOT NULL, body nvarchar(max), created_at datetimeoffset(7), active bit)", transaction: transaction);
        var id = Guid.NewGuid();
        var value = "Customer's request; DROP TABLE audit_logs; -- Ω";
        var timestamp = DateTimeOffset.UtcNow;
        await Database.ExecuteAsync(connection, "INSERT INTO #astra_parameter_test VALUES(@id,@body,@created,@active)", new Dictionary<string, object?> { ["id"] = id, ["body"] = value, ["created"] = timestamp, ["active"] = true }, transaction);
        var rows = await Database.QueryAsync(connection, "SELECT * FROM #astra_parameter_test WHERE id=@id", new Dictionary<string, object?> { ["id"] = id }, transaction);
        Assert.Equal(id, rows.Single()["id"]);
        Assert.Equal(value, rows.Single()["body"]);
        Assert.Equal(timestamp, rows.Single()["created_at"]);
        Assert.Equal(true, rows.Single()["active"]);
        await transaction.RollbackAsync();
    }
}
