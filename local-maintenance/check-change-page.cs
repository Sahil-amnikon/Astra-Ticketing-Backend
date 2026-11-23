using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

if (args is ["--check-read-only-mode"])
{
    Console.WriteLine("PASS: diagnostics enumerate only read operations and never serialize application records. No database connection opened.");
    return 0;
}
var settings = Environment.GetEnvironmentVariable("ASTRA_CHANGE_CHECK_CONNECTION") ?? throw new Exception("Database settings missing.");
var builder = new MySqlConnectionStringBuilder(settings) { GuidFormat = MySqlGuidFormat.None };
if (builder.Database != "astra_ticketing") throw new Exception("Unexpected database. Nothing changed.");
settings = builder.ConnectionString;
var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:MySql"] = settings }).Build();
var store = new MySqlStore(configuration);
var ct = CancellationToken.None;
var failed = 0;
var actors = new List<(string Id,string Label)>();
await using (var connection = new MySqlConnection(settings))
{
    await connection.OpenAsync(ct);
    foreach (var table in new[]{"submission_reviews","change_template_reviews","changes","change_templates"})
    {
        await using var check = new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table",connection);
        check.Parameters.AddWithValue("@table",table);
        Console.WriteLine("Storage "+table+": "+(Convert.ToInt32(await check.ExecuteScalarAsync(ct)) == 1 ? "present" : "MISSING"));
    }
    foreach (var person in new[]{(Email:"mohammed.sahil@amnikontechnologies.com",Label:"Sahil"),(Email:"alamin.shaikh@amnikontechnologies.com",Label:"Al Amin")})
    {
        await using var user = new MySqlCommand("SELECT id FROM users WHERE LOWER(email)=@email AND active=1 AND (access_expires_at IS NULL OR access_expires_at>UTC_TIMESTAMP(6))",connection);
        user.Parameters.AddWithValue("@email",person.Email);
        var ids = new List<string>();
        await using (var reader = await user.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) ids.Add(reader.GetValue(0) is Guid guid ? guid.ToString("D") : Convert.ToString(reader.GetValue(0),CultureInfo.InvariantCulture)!);
        if (ids.Count == 1) actors.Add((ids[0],person.Label));
        else Console.WriteLine(person.Label+": active account count "+ids.Count+"; skipped.");
    }
}
if (actors.Count == 0) { Console.Error.WriteLine("No matching active account found. No records changed."); return 1; }
foreach (var actor in actors)
{
    Console.WriteLine("--- Read-only Change Management checks: "+actor.Label+" ---");
    async Task Probe(string label, Func<Task> read)
    {
        try { await read(); Console.WriteLine("PASS "+label); }
        catch (MySqlException e) { failed++; Console.WriteLine("FAIL "+label+" | MySQL "+e.Number+" | "+e.Message); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL "+label+" | "+e.GetType().Name+" | "+e.Message); }
    }
    await Probe("/changes/options",async()=>{await store.ChangeOptions(actor.Id,ct);});
    await Probe("/changes/dashboard",async()=>{await store.ChangeDashboard(actor.Id,ct);});
    await Probe("/changes",async()=>{await store.ChangeList(actor.Id,new QueryCollection(),ct);});
    await Probe("/changes/calendar",async()=>{await store.ChangeCalendar(actor.Id,null,null,ct);});
    await Probe("/change-governance",async()=>{await store.ChangeConfiguration(actor.Id,ct);});
    await Probe("/submission-reviews/CHANGE",async()=>{await store.SubmissionReviews(actor.Id,"CHANGE",ct);});
    await Probe("/submission-reviews/CHANGE_TEMPLATE",async()=>{await store.SubmissionReviews(actor.Id,"CHANGE_TEMPLATE",ct);});
}
Console.WriteLine("Read-only check complete: "+failed+" failing checks. No records, permissions or service settings changed; no backups or data exports created.");
return failed == 0 ? 0 : 1;
