using Astra.Api.Modules;
using Microsoft.Data.SqlClient;

namespace Astra.Tests;

public class BusinessCalendarTests
{
    private static BusinessCalendar Calendar(params DateOnly[] holidays) => new(Guid.NewGuid(), TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"), new HashSet<int> { 1, 2, 3, 4, 5 }, new TimeOnly(9, 0), new TimeOnly(18, 0), holidays.ToHashSet());
    [Fact]
    public void BusinessTargetsSkipWeekendsAndHolidays()
    {
        var calendar = Calendar(new DateOnly(2026, 10, 12));
        var start = DateTimeOffset.Parse("2026-10-09T17:30:00+05:30", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(DateTimeOffset.Parse("2026-10-13T09:30:00+05:30", System.Globalization.CultureInfo.InvariantCulture), calendar.AddMinutes(start, 60));
        Assert.Equal(60, calendar.MinutesBetween(start, calendar.AddMinutes(start, 60)));
        Assert.Equal(start, calendar.AddMinutes(start, 0)); Assert.Equal(0, calendar.MinutesBetween(start, start.AddDays(-1)));
    }
    [Fact]
    public void PartialMinutesFollowSourceFlooringRules()
    {
        var calendar = Calendar(); var start = DateTimeOffset.Parse("2026-10-09T17:59:30+05:30", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(DateTimeOffset.Parse("2026-10-12T09:01:00+05:30", System.Globalization.CultureInfo.InvariantCulture), calendar.AddMinutes(start, 1));
        Assert.Equal(0, calendar.MinutesBetween(start, start.AddSeconds(30)));
    }
    [Theory]
    [InlineData("2026-03-08T01:00:00-05:00", 120, "2026-03-08T04:00:00-04:00")]
    [InlineData("2026-11-01T01:00:00-05:00", 120, "2026-11-01T03:00:00-05:00")]
    public void DaylightSavingUsesActualElapsedBusinessTime(string start, int minutes, string expected)
    {
        var calendar = new BusinessCalendar(Guid.NewGuid(), TimeZoneInfo.FindSystemTimeZoneById("America/New_York"), new HashSet<int> { 7 }, new TimeOnly(1, 0), new TimeOnly(5, 0), new HashSet<DateOnly>());
        var beginning = DateTimeOffset.Parse(start, System.Globalization.CultureInfo.InvariantCulture); var ending = DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(ending, calendar.AddMinutes(beginning, minutes)); Assert.Equal(minutes, calendar.MinutesBetween(beginning, ending));
    }
    [SqlServerFact]
    public async Task CopiedBusinessCalendarSupportsNativeDeadlineCalculation()
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING")) { InitialCatalog = "AstraMigration" }.ConnectionString); await connection.OpenAsync();
        var calendar = await BusinessCalendar.LoadAsync(connection, null, Guid.Parse("00000000-0000-4000-8000-000000000126"));
        var start = DateTimeOffset.Parse("2026-10-13T08:00:00+05:30", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(30, calendar.MinutesBetween(calendar.AddMinutes(start, 1), calendar.AddMinutes(start, 31)));
    }
}
