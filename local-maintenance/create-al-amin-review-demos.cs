using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

static string ReadDemoId(object? value) => value switch
{
    Guid id => id.ToString("D"),
    string text when Guid.TryParse(text, out var id) => id.ToString("D"),
    _ => throw new Exception("The database returned an invalid record ID. No account was changed.")
};

if (args is ["--check-id-handling"])
{
    var id = Guid.Parse("11111111-2222-4333-8444-555555555555");
    if (ReadDemoId(id) != id.ToString("D") || ReadDemoId(id.ToString("D")) != id.ToString("D"))
        throw new Exception("GUID/text conversion check failed.");
    var testSettings = new MySqlConnectionStringBuilder("Server=localhost;Database=astra_ticketing") { GuidFormat = MySqlGuidFormat.None };
    if (new MySqlConnectionStringBuilder(testSettings.ConnectionString).GuidFormat != MySqlGuidFormat.None)
        throw new Exception("Application-compatible GUID configuration check failed.");
    Console.WriteLine("PASS: GUID and text IDs are handled correctly; the database driver uses ASTRA's GUID setting. No database connection opened.");
    return 0;
}

try
{
var settings = Environment.GetEnvironmentVariable("ASTRA_DEMO_CONNECTION") ?? throw new Exception("Database settings missing.");
var connectionSettings = new MySqlConnectionStringBuilder(settings) { GuidFormat = MySqlGuidFormat.None };
if (connectionSettings.Database != "astra_ticketing") throw new Exception("Unexpected database. No demos created.");
settings = connectionSettings.ConnectionString;
if (typeof(MySqlStore).GetMethod("SubmissionReviews") is null) throw new Exception("Deploy the technician review update first. No demos created.");
var ct = CancellationToken.None;
await using var db = new MySqlConnection(settings); await db.OpenAsync(ct);
await using var mutex = new MySqlCommand("SELECT GET_LOCK('astra-al-amin-review-demos-v1',10)", db);
if (Convert.ToInt32(await mutex.ExecuteScalarAsync(ct)) != 1) throw new Exception("Another demo setup is running. Try again later.");
try
{
    await using (var schema = new MySqlCommand("SELECT kind,entity_id,status,revision FROM submission_reviews LIMIT 0", db))
    { await using var check = await schema.ExecuteReaderAsync(ct); }
    var candidates = new List<(string Id, string Name, string Email)>();
    await using (var query = new MySqlCommand(@"SELECT u.id,u.display_name,u.email FROM users u WHERE u.active=1
        AND (u.access_expires_at IS NULL OR u.access_expires_at>UTC_TIMESTAMP(6))
        AND LOWER(TRIM(u.email))=@email
        AND EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code='TECHNICIAN')
        AND NOT EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code IN ('TEAM_LEAD','MANAGER','ADMIN'))", db))
    {
        query.Parameters.AddWithValue("@email", "alamin.shaikh@amnikontechnologies.com");
        await using var reader = await query.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) candidates.Add((ReadDemoId(reader.GetValue(0)), reader.GetString(1), reader.GetString(2)));
    }
    if (candidates.Count != 1) throw new Exception("Expected one active Technician account for alamin.shaikh@amnikontechnologies.com; found " + candidates.Count + ". No demos created. Check that this account is active, has Technician access and is not assigned Team Lead, Manager or Administrator access.");
    var actor = candidates[0];
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:MySql"] = settings }).Build();
    var store = new MySqlStore(config);
    var options = JsonSerializer.SerializeToElement(await store.GetProblemOptions(actor.Id, ct) ?? throw new Exception("Al Amin cannot access Problem Management. No demos created."));
    var clients = options.GetProperty("clients").EnumerateArray().Where(x => string.Equals(x.GetProperty("name").GetString(), "Geometry", StringComparison.OrdinalIgnoreCase)).ToArray();
    if (clients.Length != 1) throw new Exception("Al Amin does not have one unambiguous Geometry assignment. No demos created.");
    var client = clients[0].GetProperty("id").GetString()!;
    var sites = options.GetProperty("sites").EnumerateArray().Where(x => x.GetProperty("client_id").GetString() == client).OrderBy(x => x.GetProperty("name").GetString()).ToArray();
    var site = sites.FirstOrDefault().ValueKind == JsonValueKind.Undefined ? null : sites[0].GetProperty("id").GetString();
    if (site is null)
    {
        await using var restricted = new MySqlCommand("SELECT COUNT(*) FROM user_sites WHERE user_id=@id", db);
        restricted.Parameters.AddWithValue("@id", actor.Id);
        if (Convert.ToInt32(await restricted.ExecuteScalarAsync(ct)) > 0) throw new Exception("Al Amin has site restrictions but no accessible Geometry site. No demos created.");
    }
    var knowledgeOptions = JsonSerializer.SerializeToElement(await store.GetKnowledgeOptions(actor.Id, ct));
    var categories = knowledgeOptions.GetProperty("categories").EnumerateArray().OrderByDescending(x => x.GetProperty("name").GetString()!.Contains("application",StringComparison.OrdinalIgnoreCase)).ThenBy(x => x.GetProperty("name").GetString()).ToArray();
    if (categories.Length == 0) throw new Exception("An active category is required. No demos created.");
    var category = categories[0].GetProperty("id").GetString()!;
    // Resolve all permissions before the first write. These application methods enforce the same scopes used by the UI.
    await store.ChangeOptions(actor.Id, ct);
    await using (var sequence = new MySqlCommand("SELECT next_value FROM astra_sequence_state WHERE name='change_number_seq'", db))
        if (await sequence.ExecuteScalarAsync(ct) is null) throw new Exception("Change numbering is not initialized. No demos created.");
    Console.WriteLine("Creating review demos for " + actor.Name + " (" + actor.Email + ") under Geometry.");
    Console.WriteLine("DEMO records only. No approval, password change, external notification or live implementation is requested.");

    async Task<string?> Existing(string kind, string table, string field, string authorColumn, string title)
    {
        await using var query = new MySqlCommand($"SELECT e.id,r.status FROM {table} e LEFT JOIN submission_reviews r ON r.kind=@kind AND r.entity_id=e.id WHERE e.{authorColumn}=@actor AND e.{field}=@title", db);
        query.Parameters.AddWithValue("@kind",kind); query.Parameters.AddWithValue("@actor",actor.Id); query.Parameters.AddWithValue("@title",title);
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var id = ReadDemoId(reader.GetValue(0));
        var status = reader.IsDBNull(1) ? "NO_REVIEW" : reader.GetString(1);
        if (await reader.ReadAsync(ct)) throw new Exception("Duplicate matching demo records exist. Nothing was removed or replaced.");
        if (status == "NO_REVIEW") throw new Exception("A matching demo has no review entry. It was not changed.");
        Console.WriteLine(kind + ": already exists · " + id + " · " + status);
        return id;
    }
    async Task Verify(string kind, string id)
    {
        await using var query = new MySqlCommand("SELECT status FROM submission_reviews WHERE kind=@kind AND entity_id=@id AND submitted_by_user_id=@actor",db);
        query.Parameters.AddWithValue("@kind",kind);query.Parameters.AddWithValue("@id",id);query.Parameters.AddWithValue("@actor",actor.Id);
        var state = Convert.ToString(await query.ExecuteScalarAsync(ct));
        if (state != "PENDING") throw new Exception("The new " + kind + " did not reach pending review. Stop and report this result.");
        Console.WriteLine(kind + ": created · " + id + " · Awaiting review");
    }
    const string marker = "[DEMO-AL-AMIN-REVIEW] ";
    var changeTitle = marker + "Pilot update to application support settings";
    if (await Existing("CHANGE","changes","title","requested_by_user_id",changeTitle) is null)
    {
        var result = JsonSerializer.SerializeToElement(await store.CreateChange(actor.Id, JsonSerializer.SerializeToElement(new {
            clientId=client,siteId=site,ownerUserId=actor.Id,title=changeTitle,
            description="Demonstration for the technician review workflow. Propose an application configuration pilot in a test environment; no real work should be performed from this demo.",
            category="APPLICATION",changeType="NORMAL",risk="LOW",outageExpected=false,downtimeMinutes=0,
            impact="Test environment only. No production or customer impact is authorized.",
            reason="Demonstrate review by the Geometry team lead, a manager or an administrator.",
            businessJustification="Validate that a technician submission remains hidden until an independent reviewer approves it.",
            implementationPlan="DEMO ONLY: capture the test configuration, apply one sample setting in a test environment, and record the result after the required approvals.",
            testPlan="Verify the test application opens and the sample setting behaves as expected. This record does not authorize testing on live systems.",
            rollbackPlan="Restore the captured test configuration if the test does not meet the expected result.",
            communicationPlan="Use the in-app review queue. Do not send customer communications for this demo.",clientNotificationEnabled=false,
        }),ct));
        await Verify("CHANGE",result.GetProperty("id").GetString()!);
    }
    var problemTitle = marker + "Recurring application sign-in prompts";
    if (await Existing("PROBLEM","problems","title","created_by_user_id",problemTitle) is null)
    {
        var result = await store.CreateProblem(actor.Id,new CreateProblemRequest {
            ClientId=client,SiteId=site,Title=problemTitle,Priority="P4_LOW",
            Description="DEMO ONLY: investigate repeated sign-in prompts reported in a test application. Collect the prompt time, application version and non-sensitive error text; compare test device settings. This sample is awaiting review and does not represent a confirmed customer incident."
        },ct) ?? throw new Exception("Problem creation was denied. Existing demo records are retained.");
        await Verify("PROBLEM",result.Id);
    }
    var articleTitle = marker + "Initial checks for repeated sign-in prompts";
    if (await Existing("KNOWLEDGE","knowledge_articles","title","author_user_id",articleTitle) is null)
    {
        var result = await store.SaveKnowledgeArticle(actor.Id,null,new KnowledgeArticleRequest {
            ClientId=client,CategoryId=category,Title=articleTitle,Visibility="STAFF",Status="DRAFT",
            Body="DEMO ONLY — staff guidance awaiting independent review.\n\n1. Record the affected application and the exact non-sensitive error message.\n2. Confirm the test device has the correct date and time and a stable connection.\n3. Check for an approved application update and test with a non-production account.\n4. Document the result and ask the team lead before making any change to a live environment.\n\nNever ask users to share passwords, tokens or verification codes. This article demonstrates the approval workflow."
        },ct) ?? throw new Exception("Article creation was denied. Existing demo records are retained.");
        await Verify("KNOWLEDGE",result.Id);
    }
    var serviceTitle = marker + "Application access assistance";
    if (await Existing("SERVICE","service_templates","name","created_by_user_id",serviceTitle) is null)
    {
        var result = await store.SaveServiceTemplate(actor.Id,null,new ServiceTemplateRequest {
            ClientId=client,CategoryId=category,Name=serviceTitle,Active=false,
            Description="DEMO ONLY: a service catalogue entry for requesting application access assistance. It remains inactive until reviewed.",
            Instructions="Provide the application name, a brief description of the access issue and a suitable contact time. Do not include passwords or security codes. A technician will assess the request after it is submitted. This sample template is for review-workflow demonstration only."
        },ct) ?? throw new Exception("Service creation was denied. Existing demo records are retained.");
        await Verify("SERVICE",result.Id);
    }
    Console.WriteLine("Done. Open Review submissions in each module. Al Amin is the author; no submission was approved by this command.");
}
finally
{
    await using var release = new MySqlCommand("SELECT RELEASE_LOCK('astra-al-amin-review-demos-v1')",db);await release.ExecuteScalarAsync(ct);
}
return 0;
}
catch (MySqlException e)
{
    Console.Error.WriteLine("Demo setup stopped: database error " + e.Number + ". Existing completed demos were retained; rerunning will skip them.");
    return 1;
}
catch (Exception e)
{
    Console.Error.WriteLine("Demo setup stopped: " + e.Message);
    return 1;
}
