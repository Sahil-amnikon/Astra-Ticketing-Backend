using Astra.Api.Infrastructure;
using Astra.Api.Modules;
using Microsoft.Data.SqlClient;

namespace Astra.Tests;

public class SqlBusinessCalendarTests
{
    [SqlServerFact]
    public async Task SqlAndDotnetCalculationsMatchForCopiedCalendar()
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING")) { InitialCatalog = "AstraMigration" }.ConnectionString); await connection.OpenAsync();
        var id = Guid.Parse("00000000-0000-4000-8000-000000000126"); var calendar = await BusinessCalendar.LoadAsync(connection, null, id);
        foreach (var date in new[] { "2026-10-09T17:59:30+05:30", "2026-10-10T08:00:00+05:30", "2026-10-13T10:15:30+05:30" }) foreach (var minutes in new[] { 0, 1, 30, 540, 600, 10000 })
        {
            var start = DateTimeOffset.Parse(date, System.Globalization.CultureInfo.InvariantCulture); var expected = calendar.AddMinutes(start, minutes);
            var row = (await Database.QueryAsync(connection, "SELECT dbo.sla_add_business_minutes(@calendar,@start,@minutes) AS ending,dbo.sla_business_minutes_between(@calendar,@start,@end) AS elapsed", new Dictionary<string, object?> { ["calendar"] = id, ["start"] = start, ["minutes"] = minutes, ["end"] = expected })).Single();
            Assert.Equal(expected, row["ending"]); Assert.Equal(calendar.MinutesBetween(start, expected), row["elapsed"]);
        }
    }
    [SqlServerFact]
    public async Task SqlTimezoneFoldResolutionMatchesPostgresqlStandardSide()
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING")) { InitialCatalog = "AstraMigration" }.ConnectionString); await connection.OpenAsync();
        var row = (await Database.QueryAsync(connection, "SELECT dbo.calendar_instant(CONVERT(date,'20261101'),CONVERT(time,'01:30:00'),N'Eastern Standard Time') AS fold,dbo.calendar_instant(CONVERT(date,'20260308'),CONVERT(time,'02:30:00'),N'Eastern Standard Time') AS gap")).Single();
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T06:30:00Z", System.Globalization.CultureInfo.InvariantCulture), row["fold"]);
        Assert.Equal(DateTimeOffset.Parse("2026-03-08T07:30:00Z", System.Globalization.CultureInfo.InvariantCulture), row["gap"]);
    }
}
