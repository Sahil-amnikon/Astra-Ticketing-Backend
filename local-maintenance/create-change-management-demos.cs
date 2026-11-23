using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

const string Marker="DEMO-CM-REVIEW-V1";
var demos=new[]{
    new DemoChange("Geometry","GEOMETRY","Replace the shared printer queue driver","SERVER",
        "Repeated demo printing incidents need one compatible shared-printer driver and queue configuration.",
        "Sample change to address the recurring printer problem. Affected print queues may be unavailable briefly; no live outage is authorized by this demo.",
        "Record the current queue and driver settings; obtain the approved compatible driver; test it on a pilot queue; after approval replace the affected shared queue driver and reconnect the pilot device.",
        "Print a test page and a sample document from each affected demo device; confirm jobs clear without errors.",
        "Restore the previous driver package and captured queue settings if the pilot fails."),
    new DemoChange("Geometry","GEOMETRY","Pilot security updates on the file server","SERVER",
        "A controlled pilot is needed before a wider security-update rollout.",
        "Sample file-server maintenance plan. File access may be interrupted during the approved test window; this demo does not authorize patching production.",
        "Confirm the approved update list and recovery procedure; verify test access; install the approved updates on the pilot server only after approval; restart during the agreed window if required.",
        "Verify server health, file-share access and permissions from a test workstation; review the relevant service events.",
        "Follow the approved update rollback or recovery procedure and verify file-share access is restored."),
    new DemoChange("Ravid IT","RAVID","Renew the VPN gateway certificate","NETWORK",
        "The recurring demo VPN incidents share an expired gateway certificate as their sample root cause.",
        "Sample VPN-certificate renewal. Remote connections may reconnect during an approved maintenance window; no real certificate or service is changed by this record.",
        "Confirm the certificate subject and trusted chain; obtain the approved replacement through the certificate process; after approval install the chain on the pilot gateway and reload the affected VPN service.",
        "Confirm the presented certificate is valid and trusted; connect from each affected demo device and verify permitted application access.",
        "Restore the previously recorded gateway configuration and use the approved recovery certificate procedure if validation fails."),
    new DemoChange("Ravid IT","RAVID","Pilot the approved network switch firmware","NETWORK",
        "A staged switch-firmware pilot is required to validate stability before wider rollout.",
        "Sample network maintenance. Pilot-device connections may be interrupted during an approved test window; no live upgrade is authorized by this demo.",
        "Confirm supported hardware, image checksum and vendor guidance; record the current configuration and firmware; after approval install the approved image on the designated pilot switch and verify startup.",
        "Confirm uplinks, VLANs and required client connectivity; compare port health and error counters with the recorded baseline.",
        "Return to the previously approved image and recorded configuration using the documented switch recovery procedure.")
};
static string Id(object? value)=>Guid.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),out var id)?id.ToString("D"):throw new Exception("An invalid database record ID was returned.");
JsonElement Payload(DemoChange demo,string client,string? site,string actor,string approver)=>JsonSerializer.SerializeToElement(new{
    clientId=client,siteIds=site is null?Array.Empty<string>():new[]{site},departmentIds=Array.Empty<string>(),
    ownerUserId=actor,requesterRef="USER:"+actor,approverUserId=approver,
    title="["+Marker+"-"+demo.Code+"] "+demo.Title,
    description="DEMO ONLY — review-workflow training. "+demo.Impact+" Do not perform live work from this sample.",
    category=demo.Category,changeType="NORMAL",risk="MEDIUM",outageExpected=true,downtimeMinutes=15,
    impact=demo.Impact,reason=demo.Reason,
    businessJustification="Demonstrate a complete change plan, independent approval, implementation validation and final review before closure.",
    implementationPlan=demo.Implementation,testPlan=demo.Test,rollbackPlan=demo.Rollback,
    communicationPlan="DEMO ONLY. Use the existing in-app review queue. Client email notifications are disabled for this sample; agree a real maintenance window and communications separately before any live work.",
    clientNotificationEnabled=false
});
if(args is ["--check-plan"]){
    if(demos.Length!=4||demos.Count(d=>d.Code=="GEOMETRY")!=2||demos.Count(d=>d.Code=="RAVID")!=2||demos.Select(d=>d.Title).Distinct().Count()!=4)throw new Exception("Invalid two-per-organization plan.");
    var actor=Guid.NewGuid().ToString("D");var approver=Guid.NewGuid().ToString("D");
    foreach(var demo in demos){
        var payload=Payload(demo,Guid.NewGuid().ToString("D"),null,actor,approver);
        if(payload.GetProperty("changeType").GetString()!="NORMAL"||payload.GetProperty("clientNotificationEnabled").GetBoolean())throw new Exception("Demo approval or email settings are invalid.");
        foreach(var key in new[]{"description","impact","reason","businessJustification","implementationPlan","testPlan","rollbackPlan","communicationPlan"})if(payload.GetProperty(key).GetString()!.Length<3)throw new Exception("Incomplete plan.");
    }
    var rule=typeof(MySqlStore).GetMethod("ChangeApprovalRule",BindingFlags.Static|BindingFlags.NonPublic)!;
    if((bool)rule.Invoke(null,["NORMAL",actor,actor,actor,actor,actor])!||!(bool)rule.Invoke(null,["NORMAL",approver,actor,actor,actor,approver])!)throw new Exception("Independent Normal approval rule failed.");
    Console.WriteLine("PASS: four complete demo plans, two per organization, Normal changes with a separate approver and no client email. No database or network accessed.");return 0;
}
try{
    var settings=new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ASTRA_DEMO_CONNECTION")??throw new Exception("Service database settings missing.")){GuidFormat=MySqlGuidFormat.None};
    if(settings.Database!="astra_ticketing")throw new Exception("Unexpected application database.");
    var ct=CancellationToken.None;await using var db=new MySqlConnection(settings.ConnectionString);await db.OpenAsync(ct);
    await using var mutex=new MySqlCommand("SELECT GET_LOCK('astra-change-management-demos-v1',10)",db);
    if(Convert.ToInt32(await mutex.ExecuteScalarAsync(ct))!=1)throw new Exception("Another demo creator is running.");
    try{
        async Task<List<Dictionary<string,object?>>> Rows(string sql,params(string Name,object? Value)[] args){
            await using var cmd=new MySqlCommand(sql,db);foreach(var a in args)cmd.Parameters.AddWithValue("@"+a.Name,a.Value??DBNull.Value);
            await using var reader=await cmd.ExecuteReaderAsync(ct);var rows=new List<Dictionary<string,object?>>();
            while(await reader.ReadAsync(ct)){var row=new Dictionary<string,object?>();for(var i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);rows.Add(row);}return rows;
        }
        if(args is ["--diagnose"]){
            Console.WriteLine("READ-ONLY: Change demo database rules. No demo creation, ALTER, permission changes, backups or database copies.");
            var rules=await Rows("SELECT t.TABLE_NAME,t.CONSTRAINT_NAME,c.CHECK_CLAUSE FROM information_schema.TABLE_CONSTRAINTS t JOIN information_schema.CHECK_CONSTRAINTS c ON c.CONSTRAINT_SCHEMA=t.CONSTRAINT_SCHEMA AND c.CONSTRAINT_NAME=t.CONSTRAINT_NAME WHERE t.TABLE_SCHEMA=DATABASE() AND t.CONSTRAINT_TYPE='CHECK' AND t.TABLE_NAME IN ('changes','change_request_details','submission_reviews','notifications','change_history','audit_logs') ORDER BY t.TABLE_NAME,t.CONSTRAINT_NAME");
            foreach(var rule in rules)Console.WriteLine("RULE "+rule["TABLE_NAME"]+"."+rule["CONSTRAINT_NAME"]+": "+rule["CHECK_CLAUSE"]);
            if(rules.Count==0)Console.WriteLine("No visible check metadata. An administrator may need to inspect the named constraint from the database error.");
            var columns=await Rows("SELECT TABLE_NAME,COLUMN_NAME,COLUMN_TYPE,COLUMN_DEFAULT,IS_NULLABLE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND ((TABLE_NAME='changes' AND COLUMN_NAME IN ('status','category','change_type','risk','outage_expected','downtime_minutes','planned_start','planned_end')) OR (TABLE_NAME='notifications' AND COLUMN_NAME='type') OR (TABLE_NAME='submission_reviews' AND COLUMN_NAME IN ('kind','status'))) ORDER BY TABLE_NAME,ORDINAL_POSITION");
            foreach(var column in columns)Console.WriteLine("COLUMN "+column["TABLE_NAME"]+"."+column["COLUMN_NAME"]+": "+column["COLUMN_TYPE"]+"; default="+(column["COLUMN_DEFAULT"]??"NULL")+"; nullable="+column["IS_NULLABLE"]);
            Console.WriteLine("DEMO VALUES: changeType=NORMAL; risk=MEDIUM; category=SERVER or NETWORK; outageExpected=true; downtimeMinutes=15; planned dates unset. Workflow writes SUBMITTED/PENDING and SUBMISSION_REVIEW notifications.");
            return 0;
        }
        var actors=await Rows("SELECT u.id,u.display_name FROM users u WHERE active=1 AND (access_expires_at IS NULL OR access_expires_at>UTC_TIMESTAMP(6)) AND LOWER(TRIM(email))=@email AND EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code='ADMIN')",("email","mohammed.sahil@amnikontechnologies.com"));
        if(actors.Count!=1)throw new Exception("Expected one active Administrator account for mohammed.sahil@amnikontechnologies.com. No new demos created.");
        var actor=Id(actors[0]["id"]);
        var approver=actor; // The existing administrator reviews independently of each organization author.
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:MySql"]=settings.ConnectionString}).Build();
        var store=new MySqlStore(config);
        var plans=new List<(DemoChange Demo,string Client,string? Site,string Creator,string CreatorName,JsonElement Body,string? ExistingId,string? ExistingNumber)>();
        foreach(var demo in demos){
            var clients=await Rows("SELECT id FROM clients WHERE status='ACTIVE' AND LOWER(REPLACE(name,' ','')) IN (@name,@alias)",("name",demo.Code=="GEOMETRY"?"geometry":"ravidit"),("alias",demo.Code=="GEOMETRY"?"geometry":"ravid"));
            if(clients.Count!=1)throw new Exception("Cannot identify exactly one active "+demo.Organization+" organization. No new demos created.");
            var client=Id(clients[0]["id"]);
            var people=JsonSerializer.SerializeToElement(await store.ChangePeople(actor,client,ct)).GetProperty("users").EnumerateArray().ToArray();
            if(!people.Any(p=>p.GetProperty("id").GetString()==approver))throw new Exception("Your Administrator must be an eligible approver for "+demo.Organization+".");
            var sites=await Rows("SELECT s.id FROM sites s WHERE s.client_id=@client AND s.active=1 ORDER BY CASE WHEN EXISTS(SELECT 1 FROM problems p WHERE p.client_id=@client AND p.site_id=s.id AND p.title LIKE '[DEMO-PM-COMMON-FIX-V1%') THEN 0 ELSE 1 END,s.name,s.id",("client",client));
            string? site=null;
            var scopeMethod=typeof(MySqlStore).GetMethod("ChangePersonScopeSql",BindingFlags.Static|BindingFlags.NonPublic)??throw new Exception("Change selection scope is unavailable.");
            var siteMethod=typeof(MySqlStore).GetMethod("ChangePersonSiteScopeSql",BindingFlags.Static|BindingFlags.NonPublic)??throw new Exception("Change site scope is unavailable.");
            var personScope=(string)scopeMethod.Invoke(null,["u.id","@client"])!;
            var siteScope=(string)siteMethod.Invoke(null,["u.id","CAST(@sites AS JSON)"])!;
            var candidates=new List<Dictionary<string,object?>>();
            foreach(var candidateSite in sites.Count==0?new string?[]{null}:sites.Select(row=>(string?)Id(row["id"]))){
            candidates=await Rows("SELECT u.id,u.display_name FROM users u WHERE u.id<>@approver AND "+personScope+" AND "+siteScope+" AND EXISTS(SELECT 1 FROM user_roles support_role WHERE support_role.user_id=u.id AND support_role.role_code IN ('TECHNICIAN','TEAM_LEAD','MANAGER','ADMIN')) ORDER BY CASE WHEN LOWER(TRIM(u.email))=@preferred THEN 0 WHEN EXISTS(SELECT 1 FROM user_roles support_role WHERE support_role.user_id=u.id AND support_role.role_code='TECHNICIAN') THEN 1 ELSE 2 END,u.display_name,u.id LIMIT 1",("approver",approver),("client",client),("sites",JsonSerializer.Serialize(candidateSite is null?Array.Empty<string>():new[]{candidateSite})),("preferred",demo.Code=="GEOMETRY"?"alamin.shaikh@amnikontechnologies.com":""));
                if(candidates.Count==1){site=candidateSite;break;}
            }
            if(candidates.Count!=1)throw new Exception(demo.Organization+" needs a separate active staff account with organization/site access to request these Normal changes. No account permissions were changed.");
            var creator=Id(candidates[0]["id"]);var creatorName=Convert.ToString(candidates[0]["display_name"])!;
            var body=Payload(demo,client,site,creator,approver);
            // Preflight the actual application rules before creating any demo.
            await using(var preflight=await db.BeginTransactionAsync(ct)){
                var validateOwner=typeof(MySqlStore).GetMethod("ValidateChangeStaff",BindingFlags.Static|BindingFlags.NonPublic)!;
                await (Task)validateOwner.Invoke(null,[db,preflight,creator,client,ct,site])!;
                var validateSelection=typeof(MySqlStore).GetMethod("ValidateChangeRequestSelection",BindingFlags.Instance|BindingFlags.NonPublic)!;
                await (Task<ChangeRequestSelection>)validateSelection.Invoke(store,[db,preflight,creator,client,creator,body,ct,false])!;
                await preflight.RollbackAsync(ct);
            }
            var savedMatches=await Rows("SELECT id FROM changes WHERE client_id=@client AND title=@title",("client",client),("title","["+Marker+"-"+demo.Code+"] "+demo.Title));
            if(savedMatches.Count>1)throw new Exception("Duplicate matching demo titles exist. Existing changes were not modified.");
            var title="["+Marker+"-"+demo.Code+"] "+demo.Title;
            var existing=await Rows("SELECT id,change_number,change_type FROM changes WHERE client_id=@client AND requested_by_user_id=@actor AND title=@title",("client",client),("actor",creator),("title",title));
            if(savedMatches.Count!=existing.Count)throw new Exception("A demo with this title has a different requester; it was not changed or duplicated.");
            if(existing.Count>1||existing.Any(row=>Convert.ToString(row["change_type"])!="NORMAL"))throw new Exception("Matching demo records differ or are duplicated. Existing records were not changed.");
            plans.Add((demo,client,site,creator,creatorName,body,existing.Count==1?Id(existing[0]["id"]):null,existing.Count==1?Convert.ToString(existing[0]["change_number"]):null));
        }
        if((await Rows("SELECT next_value FROM astra_sequence_state WHERE name='change_number_seq'")).Count!=1)throw new Exception("Change numbering is not initialized. No new demos created.");
        await Rows("SELECT kind,entity_id,status,revision FROM submission_reviews LIMIT 0");
        await Rows("SELECT change_id,approver_user_id,site_ids FROM change_request_details LIMIT 0");
        Console.WriteLine("Selected independent approver: "+actors[0]["display_name"]+". Each requester/owner is an existing eligible staff account; no permissions changed.");
        var created=0;
        foreach(var plan in plans){
            if(plan.ExistingId is not null){Console.WriteLine(plan.Demo.Organization+" · "+plan.ExistingNumber+" already exists; unchanged.");continue;}
            Console.WriteLine("Creating "+plan.Demo.Organization+" · "+plan.Demo.Title+" · requested by "+plan.CreatorName);
            var result=JsonSerializer.SerializeToElement(await store.CreateChange(plan.Creator,plan.Body,ct));
            var id=result.GetProperty("id").GetString()!;var number=result.GetProperty("change_number").GetString()!;
            var check=await Rows("SELECT c.status,r.status AS review_status,detail.approver_user_id FROM changes c JOIN submission_reviews r ON r.kind='CHANGE' AND r.entity_id=c.id JOIN change_request_details detail ON detail.change_id=c.id WHERE c.id=@id AND c.client_id=@client",("id",id),("client",plan.Client));
            if(check.Count!=1||Convert.ToString(check[0]["status"])!="SUBMITTED"||Convert.ToString(check[0]["review_status"])!="PENDING"||Id(check[0]["approver_user_id"])!=approver)throw new Exception("Created "+number+" but its pending-approval state needs review. Do not approve it from this script.");
            created++;Console.WriteLine(plan.Demo.Organization+" · "+number+" · Requested by "+plan.CreatorName+" · Awaiting your review · "+plan.Demo.Title);
        }
        Console.WriteLine("COMPLETED: "+created+" new demo changes; existing matching demos skipped. Search "+Marker+". No approval, implementation, client email, account changes or backups were performed.");
    }finally{await using var release=new MySqlCommand("SELECT RELEASE_LOCK('astra-change-management-demos-v1')",db);await release.ExecuteScalarAsync(ct);}
    return 0;
}catch(MySqlException e){
    Console.Error.WriteLine("Demo setup stopped: database error "+e.Number+". Each change uses its own application transaction; completed demos remain and matching titles are skipped on rerun.");
    if(e.Number==3819){
        var constraint=System.Text.RegularExpressions.Regex.Match(e.Message,@"Check constraint ['\x60](?<name>[^'\x60]+)['\x60] is violated",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Console.Error.WriteLine(constraint.Success?"VIOLATED CONSTRAINT: "+constraint.Groups["name"].Value:"The server did not include a recognizable check-constraint name.");
        Console.Error.WriteLine("Use -CheckOnly to inspect the relevant validation rules before retrying. Database rules were not changed.");
        Console.Error.WriteLine("Failure location: "+string.Join(" > ",new System.Diagnostics.StackTrace(e).GetFrames().Select(f=>f.GetMethod()?.DeclaringType?.Name+"."+f.GetMethod()?.Name).Where(name=>name?.Contains("MySqlStore")==true).Distinct().Take(5)));
    }
    return 1;
}
catch(Exception e){Console.Error.WriteLine("Demo setup stopped: "+(e is TargetInvocationException t?t.InnerException?.Message:e.Message)+". Existing completed demos are retained; rerunning skips matches.");return 1;}
record DemoChange(string Organization,string Code,string Title,string Category,string Reason,string Impact,string Implementation,string Test,string Rollback);
