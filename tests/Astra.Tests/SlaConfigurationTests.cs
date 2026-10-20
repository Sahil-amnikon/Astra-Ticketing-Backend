using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Astra.Api.Infrastructure;
using Astra.Api.Modules;
using Astra.Api.Security;
using Microsoft.Data.SqlClient;

namespace Astra.Tests;

public class SlaConfigurationTests
{
    private static CalendarInput Input() => new() { Name = "Isolated SLA calendar " + Guid.NewGuid().ToString("N"), Timezone = "Europe/London", WorkingDays = [1, 2, 3, 4, 5], WorkStart = "09:00", WorkEnd = "18:00", Active = true, Holidays = [new CalendarHoliday { Date = new DateOnly(2026, 12, 25), Name = "Fixture holiday" }] };
    [Fact]
    public void CalendarAndPolicyValidationRejectsInvalidWorkingRules()
    {
        var input = Input(); Assert.Equal("GMT Standard Time", SlaConfiguration.Validate(input));
        Assert.Equal(400, Assert.Throws<ApiFault>(() => SlaConfiguration.Validate(input with { WorkingDays = [1, 1] })).Status);
        Assert.Equal(400, Assert.Throws<ApiFault>(() => SlaConfiguration.Validate(input with { WorkEnd = "08:00" })).Status);
        Assert.Equal(400, Assert.Throws<ApiFault>(() => SlaConfiguration.Validate(input with { Timezone = "Invalid/Timezone" })).Status);
        Assert.Equal(400, Assert.Throws<ApiFault>(() => SlaConfiguration.ValidatePolicy("P1_CRITICAL", new SlaPolicyInput { ResponseMinutes = 60, ResolutionMinutes = 30, WarningPercent = 80, Active = true, CalendarId = Guid.NewGuid() })).Status);
    }
    [SqlServerFact]
    public async Task CalendarConfigurationStoresTimezonesHolidaysAndAdministrativeAccess()
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("SQLSERVER_TEST_CONNECTION_STRING")) { InitialCatalog = "AstraMigration" }.ConnectionString); await connection.OpenAsync();
        var admin = (await Database.QueryAsync(connection, "SELECT TOP(1) u.id,u.session_version FROM dbo.users u JOIN dbo.user_roles r ON r.user_id=u.id WHERE u.active=1 AND r.role_code='ADMIN' ORDER BY u.id")).Single();
        await using var factory = new IdentityFactory(); using var client = factory.Browser(); var token = AuthService.Token();
        await factory.Sessions.PutAsync(token, new SessionRecord((Guid)admin["id"]!, "fixture-csrf-token", Convert.ToInt32(admin["session_version"])), TimeSpan.FromMinutes(15));
        client.DefaultRequestHeaders.Add("Cookie", "amnikon_session=" + token); client.DefaultRequestHeaders.Add("x-amnikon-workspace", "admin"); client.DefaultRequestHeaders.Add("Origin", "https://astra.test"); client.DefaultRequestHeaders.Add("x-csrf-token", "fixture-csrf-token");
        Guid? id = null;
        try
        {
            var policies = await client.GetFromJsonAsync<JsonElement>("/sla/policies"); Assert.Equal(4, policies.GetArrayLength());
            var input = Input(); var response = await client.PostAsJsonAsync("/sla/calendars", input); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
            var calendars = await client.GetFromJsonAsync<JsonElement>("/sla/calendars"); var created = calendars.EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == id);
            Assert.Equal(5, created.GetProperty("working_days").GetArrayLength()); Assert.Equal("2026-12-25", created.GetProperty("holidays")[0].GetProperty("date").GetString());
            Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync("/sla/calendars/" + id, input with { Timezone = "Asia/Kolkata", Holidays = [] })).StatusCode);
            var native = (await Database.QueryAsync(connection, "SELECT sql_timezone,dbo.sla_add_business_minutes(id,CONVERT(datetimeoffset,'2026-10-13T03:30:00Z'),30) AS deadline FROM dbo.sla_calendars WHERE id=@id", new Dictionary<string, object?> { ["id"] = id.Value })).Single();
            Assert.Equal("India Standard Time", native["sql_timezone"]); Assert.NotNull(native["deadline"]);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync("/sla/policies/P1_CRITICAL", new SlaPolicyInput { ResponseMinutes = 30, ResolutionMinutes = 60, WarningPercent = 80, Active = true, CalendarId = Guid.NewGuid() })).StatusCode);
            client.DefaultRequestHeaders.Remove("x-amnikon-workspace"); client.DefaultRequestHeaders.Add("x-amnikon-workspace", "operations");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/sla/calendars")).StatusCode);
        }
        finally { if (id.HasValue) await Database.ExecuteAsync(connection, "DELETE dbo.sla_holidays WHERE calendar_id=@id; DELETE dbo.sla_calendars WHERE id=@id; DELETE dbo.audit_logs WHERE entity_type='SLA_CALENDAR' AND entity_id=CONVERT(nvarchar(36),@id)", new Dictionary<string, object?> { ["id"] = id.Value }); }
    }
}
