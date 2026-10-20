using System.Net;
using System.Net.Http.Json;
using Astra.Api.Security;
using Astra.Api.Modules;
using Astra.Api.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Astra.Tests;

public class ServiceReportTests
{
    [Theory]
    [InlineData("2026-02-30", "2026-03-01")]
    [InlineData("2026-10-13", "2026-10-12")]
    [InlineData("2025-01-01", "2026-10-13")]
    [InlineData("invalid", "2026-10-13")]
    public void InvalidReportingRangesAreRejected(string from, string to) => Assert.Equal(400, Assert.Throws<ApiFault>(() => ServiceReports.Range(from, to)).Status);

    [SqlServerFact]
    public async Task ServiceReportReadsTransferredDataAndTechnicianCannotRequestManagementReports()
    {
        var connectionString = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING")) { InitialCatalog = "AstraMigration" };
        await using var connection = new SqlConnection(connectionString.ConnectionString);
        await connection.OpenAsync();
        var admin = (await Database.QueryAsync(connection, "SELECT TOP (1) u.id,u.session_version FROM dbo.users u JOIN dbo.user_roles r ON r.user_id=u.id WHERE u.active=1 AND r.role_code='ADMIN' ORDER BY u.id")).Single();
        var expected = (await Database.QueryAsync(connection, "SELECT COUNT(*) AS n FROM dbo.tickets WHERE created_at>='2026-01-01' AND created_at<'2027-01-01'")).Single();
        await using var factory = new IdentityFactory();
        using var client = factory.Browser();
        var token = AuthService.Token();
        await factory.Sessions.PutAsync(token, new SessionRecord((Guid)admin["id"]!, "test-token", Convert.ToInt32(admin["session_version"])), TimeSpan.FromMinutes(15));
        client.DefaultRequestHeaders.Add("Cookie", "amnikon_session=" + token);
        client.DefaultRequestHeaders.Add("x-amnikon-workspace", "admin");
        var response = await client.GetAsync("/service-reports?from=2026-01-01&to=2026-12-31");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(expected["n"], report.GetProperty("tickets").GetProperty("created").GetInt32());
        Assert.Equal(report.GetProperty("tickets").GetProperty("created").GetInt32(), report.GetProperty("sources").EnumerateArray().Sum(s => s.GetProperty("count").GetInt32()));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/service-reports?from=2026-01-01&to=2026-12-31&sql=DROP")).StatusCode);
        client.DefaultRequestHeaders.Remove("x-amnikon-workspace");
        client.DefaultRequestHeaders.Add("x-amnikon-workspace", "operations");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/service-reports?from=2026-01-01&to=2026-12-31")).StatusCode);
    }
}
