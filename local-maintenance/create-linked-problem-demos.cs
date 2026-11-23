using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MySqlConnector;

const string Marker="DEMO-PM-COMMON-FIX-V1";
var groups=new[]{
    new DemoGroup("Geometry","GEOMETRY",
        ["Print jobs stay in the queue","Shared printer appears offline","New print jobs fail after a driver update"],
        "Repeated failures on the shared printer queue",
        "Sample diagnosis: an incompatible shared-printer driver leaves the print spooler queue stuck.",
        "Use an alternate approved printer while the affected shared queue is repaired.",
        "Common sample solution: check the affected shared queue, clear only the failed demo jobs, install the approved compatible printer driver, restart the affected spooler through the approved procedure, reconnect the queue and confirm a test page prints from each affected device."),
    new DemoGroup("Ravid IT","RAVID",
        ["VPN connection fails during certificate validation","VPN repeatedly disconnects during sign-in","Remote access reports an expired certificate"],
        "Repeated VPN failures caused by an expired certificate",
        "Sample diagnosis: the certificate used by the VPN gateway has expired, so the affected devices reject the connection.",
        "Use the approved alternative access method while the gateway certificate is renewed.",
        "Common sample solution: renew the VPN gateway certificate through the approved certificate process, install its complete trusted chain, reload the VPN service during an approved window, then reconnect and verify access from each affected device.")
};
static string Id(object? value)=>Guid.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),out var id)?id.ToString("D"):throw new Exception("An invalid database record ID was returned.");
if(args is ["--check-plan"]){
    if(groups.Length!=2||groups.Sum(g=>g.Subjects.Length)!=6||groups.SelectMany(g=>g.Subjects).Distinct().Count()!=6)throw new Exception("Invalid six-ticket plan.");
    if(groups.Any(g=>g.Solution.Length<30||g.Root.Length<10||g.Workaround.Length<10))throw new Exception("Incomplete shared solution.");
    var id=Guid.NewGuid();if(Id(id)!=Id(id.ToString("D")))throw new Exception("GUID conversion failed.");
    Console.WriteLine("PASS: six distinct demo tickets, two organization-specific problems, shared root causes/workarounds/solutions and GUID-safe IDs. No database or network accessed.");return 0;
}
try{
    var settings=new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ASTRA_DEMO_CONNECTION")??throw new Exception("Service database settings missing.")){GuidFormat=MySqlGuidFormat.None};
    if(settings.Database!="astra_ticketing")throw new Exception("Unexpected application database.");
    var flags=BindingFlags.Static|BindingFlags.NonPublic;
    var allocateTicket=typeof(MySqlStore).GetMethod("AllocateTicketNumber",flags)??throw new Exception("Application ticket numbering is unavailable.");
    var allocateProblem=typeof(MySqlStore).GetMethod("AllocateProblemNumber",flags)??throw new Exception("Application problem numbering is unavailable.");
    var getCalendar=typeof(MySqlStore).GetMethod("GetSlaCalendar",flags)??throw new Exception("Application SLA calendar is unavailable.");
    var ct=CancellationToken.None;
    await using var db=new MySqlConnection(settings.ConnectionString);await db.OpenAsync(ct);
    await using var mutex=new MySqlCommand("SELECT GET_LOCK('astra-linked-problem-demos-v1',10)",db);
    if(Convert.ToInt32(await mutex.ExecuteScalarAsync(ct))!=1)throw new Exception("Another copy of this demo creator is running.");
    try{
        await using var tx=await db.BeginTransactionAsync(ct);
        async Task<List<Dictionary<string,object?>>> Rows(string sql,params(string Name,object? Value)[] args){
            await using var cmd=new MySqlCommand(sql,db,tx);foreach(var a in args)cmd.Parameters.AddWithValue("@"+a.Name,a.Value??DBNull.Value);
            await using var reader=await cmd.ExecuteReaderAsync(ct);var rows=new List<Dictionary<string,object?>>();
            while(await reader.ReadAsync(ct)){var row=new Dictionary<string,object?>();for(var i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);rows.Add(row);}return rows;
        }
        async Task Write(string sql,params(string Name,object? Value)[] args){
            await using var cmd=new MySqlCommand(sql,db,tx);foreach(var a in args)cmd.Parameters.AddWithValue("@"+a.Name,a.Value??DBNull.Value);await cmd.ExecuteNonQueryAsync(ct);
        }
        var actors=await Rows("SELECT u.id,u.display_name FROM users u WHERE u.active=1 AND (u.access_expires_at IS NULL OR u.access_expires_at>UTC_TIMESTAMP(6)) AND LOWER(TRIM(u.email))=@email AND EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code='ADMIN') FOR SHARE",("email","mohammed.sahil@amnikontechnologies.com"));
        if(actors.Count!=1)throw new Exception("Expected one active Administrator account for mohammed.sahil@amnikontechnologies.com. No demo records committed.");
        var actorId=Id(actors[0]["id"]);
        async Task Audit(string action,string kind,string id,object details)=>await Write("INSERT INTO audit_logs(actor_user_id,action,entity_type,entity_id,details) VALUES(@actor,@action,@kind,@id,CAST(@details AS JSON))",("actor",actorId),("action",action),("kind",kind),("id",id),("details",JsonSerializer.Serialize(details)));
        var sources=await Rows("SELECT code FROM ticket_sources WHERE active=1 AND code<>'EMAIL' ORDER BY CASE WHEN code='PORTAL' THEN 0 WHEN code='PHONE' THEN 1 ELSE 2 END,sort_order,code LIMIT 1 FOR SHARE");
        if(sources.Count!=1)throw new Exception("An active category and non-email ticket source are required. No demo records committed.");
        var source=Convert.ToString(sources[0]["code"])!;
        var plans=new List<(DemoGroup Group,string Client,string SiteName,string? Site,string Requester,string Category)>();
        foreach(var group in groups){
            var clients=await Rows("SELECT id,name FROM clients WHERE status='ACTIVE' AND LOWER(REPLACE(name,' ','')) IN (@name,@alias) FOR SHARE",("name",group.Code=="GEOMETRY"?"geometry":"ravidit"),("alias",group.Code=="GEOMETRY"?"geometry":"ravid"));
            if(clients.Count!=1)throw new Exception("Cannot identify exactly one active "+group.Organization+" organization. No demo records committed.");
            var client=Id(clients[0]["id"]);
            var contacts=await Rows("SELECT x.id,x.site_id,COALESCE(s.name,'Client-wide') AS site_name FROM contacts x LEFT JOIN sites s ON s.id=x.site_id AND s.client_id=x.client_id AND s.active=1 WHERE x.client_id=@client AND x.active=1 AND (x.site_id IS NULL OR s.id IS NOT NULL) ORDER BY (x.site_id IS NULL),s.name,x.name,x.id LIMIT 1 FOR SHARE",("client",client));
            if(contacts.Count!=1)throw new Exception(group.Organization+" needs an active existing contact and valid site. No demo records committed.");
            var categories=await Rows("SELECT id FROM categories WHERE active=1 ORDER BY CASE WHEN LOWER(name) LIKE @first THEN 0 WHEN LOWER(name) LIKE @second THEN 1 ELSE 2 END,name,id LIMIT 1 FOR SHARE",("first",group.Code=="GEOMETRY"?"%print%":"%vpn%"),("second",group.Code=="GEOMETRY"?"%hardware%":"%network%"));
            if(categories.Count!=1)throw new Exception("An active ticket category is required. No demo records committed.");
            plans.Add((group,client,Convert.ToString(contacts[0]["site_name"])!,contacts[0]["site_id"] is null?null:Id(contacts[0]["site_id"]),Id(contacts[0]["id"]),Id(categories[0]["id"])));
        }
        var output=new List<string>();var createdTickets=0;var createdProblems=0;
        foreach(var plan in plans){
            var ids=new List<string>();var ticketNumbers=new List<string>();
            var prefix="["+Marker+"-"+plan.Group.Code+"] ";
            for(var i=0;i<3;i++){
                var title=prefix+plan.Group.Subjects[i];
                var existing=await Rows("SELECT id,ticket_number,site_id,source_reference_id FROM tickets WHERE client_id=@client AND created_by_user_id=@actor AND subject=@title FOR UPDATE",("client",plan.Client),("actor",actorId),("title",title));
                if(existing.Count>1)throw new Exception("Duplicate matching demo tickets exist. Nothing committed.");
                string id,number;
                if(existing.Count==1){
                    if(Convert.ToString(existing[0]["source_reference_id"])!=Marker||!string.Equals(existing[0]["site_id"] is null?null:Id(existing[0]["site_id"]),plan.Site,StringComparison.OrdinalIgnoreCase))throw new Exception("A matching ticket differs from this demo plan. Nothing committed.");
                    id=Id(existing[0]["id"]);number=Convert.ToString(existing[0]["ticket_number"])!;
                }else{
                    id=Guid.NewGuid().ToString("D");
                    number=await (Task<string>)allocateTicket.Invoke(null,[db,tx,plan.Client,plan.Site,ct])!;
                    await Write("INSERT INTO tickets(id,ticket_number,client_id,requester_id,site_id,category_id,assigned_user_id,created_by_user_id,subject,description,source,priority,status,source_reference_id) VALUES(@id,@number,@client,@requester,@site,@category,@actor,@actor,@subject,@body,@source,'P4_LOW','NEW',@marker)",("id",id),("number",number),("client",plan.Client),("requester",plan.Requester),("site",plan.Site),("category",plan.Category),("actor",actorId),("subject",title),("body","DEMO ONLY. Sample incident "+(i+1)+" of three with a common underlying cause. "+plan.Group.Subjects[i]+". This is a training example, not a report of a real outage. Open the linked problem for the common diagnosis and fix."),("source",source),("marker",Marker));
                    var calendar=await (Task<SlaCalendar?>)getCalendar.Invoke(null,[db,tx,"P4_LOW",ct])!;
                    if(calendar is not null){
                        var nowRows=await Rows("SELECT UTC_TIMESTAMP(6) AS now_utc");var now=DateTime.SpecifyKind(Convert.ToDateTime(nowRows[0]["now_utc"],CultureInfo.InvariantCulture),DateTimeKind.Utc);
                        await Write("INSERT INTO ticket_sla(ticket_id,policy_id,calendar_id,target_priority,response_warning_at,response_due_at,resolution_warning_at,resolution_due_at) VALUES(@id,@policy,@calendar,'P4_LOW',@rw,@rd,@sw,@sd)",("id",id),("policy",calendar.PolicyId),("calendar",calendar.Id),("rw",SlaBusinessCalendar.Add(calendar,now,(int)Math.Ceiling(calendar.ResponseMinutes*calendar.WarningPercent/100.0))),("rd",SlaBusinessCalendar.Add(calendar,now,calendar.ResponseMinutes)),("sw",SlaBusinessCalendar.Add(calendar,now,(int)Math.Ceiling(calendar.ResolutionMinutes*calendar.WarningPercent/100.0))),("sd",SlaBusinessCalendar.Add(calendar,now,calendar.ResolutionMinutes)));
                    }
                    await Write("INSERT INTO ticket_history(ticket_id,actor_user_id,action,new_value) VALUES(@id,@actor,'TICKET.CREATED',CAST(@details AS JSON))",("id",id),("actor",actorId),("details",JsonSerializer.Serialize(new{ticketNumber=number,source,demo=Marker})));
                    var comment=Guid.NewGuid().ToString("D");
                    await Write("INSERT INTO ticket_comments(id,ticket_id,author_user_id,comment_type,body) VALUES(@comment,@id,@actor,'INTERNAL',@body)",("comment",comment),("id",id),("actor",actorId),("body","DEMO shared solution\n\n"+plan.Group.Root+"\n\nWorkaround: "+plan.Group.Workaround+"\n\n"+plan.Group.Solution+"\n\nThis sample does not authorize work on a live system."));
                    await Write("INSERT INTO ticket_history(ticket_id,actor_user_id,action,new_value) VALUES(@id,@actor,'TICKET.INTERNAL_NOTE_ADDED',CAST(@details AS JSON))",("id",id),("actor",actorId),("details",JsonSerializer.Serialize(new{commentId=comment,demo=Marker})));
                    await Audit("TICKET.CREATED","TICKET",id,new{ticketNumber=number,demo=Marker});createdTickets++;
                }
                ids.Add(id);ticketNumbers.Add(number);
            }
            var problemTitle=prefix+plan.Group.Problem;
            var existingProblems=await Rows("SELECT id,problem_number,site_id FROM problems WHERE client_id=@client AND created_by_user_id=@actor AND title=@title FOR UPDATE",("client",plan.Client),("actor",actorId),("title",problemTitle));
            if(existingProblems.Count>1)throw new Exception("Duplicate matching demo problems exist. Nothing committed.");
            string problemId,problemNumber;
            if(existingProblems.Count==1){
                problemId=Id(existingProblems[0]["id"]);problemNumber=Convert.ToString(existingProblems[0]["problem_number"])!;
                var links=await Rows("SELECT ticket_id FROM problem_tickets WHERE problem_id=@id",("id",problemId));
                if(links.Count!=3||!links.Select(x=>Id(x["ticket_id"])).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(ids))throw new Exception("An existing demo problem has different ticket links. It was not changed.");
            }else{
                problemId=Guid.NewGuid().ToString("D");var number=await (Task<long>)allocateProblem.Invoke(null,[db,tx,ct])!;problemNumber="PRB-"+number.ToString("D6",CultureInfo.InvariantCulture);
                await Write("INSERT INTO problems(id,number,problem_number,client_id,site_id,title,description,priority,status,owner_user_id,created_by_user_id,root_cause,workaround,resolution) VALUES(@id,@number,@problemNumber,@client,@site,@title,@body,'P4_LOW','KNOWN_ERROR',@actor,@actor,@root,@workaround,@solution)",("id",problemId),("number",number),("problemNumber",problemNumber),("client",plan.Client),("site",plan.Site),("title",problemTitle),("body","DEMO ONLY. Three recurring sample tickets share one cause and solution. Related tickets: "+string.Join(", ",ticketNumbers)+". Read the root cause, workaround and permanent resolution in the problem solution chart."),("actor",actorId),("root",plan.Group.Root),("workaround",plan.Group.Workaround),("solution",plan.Group.Solution));
                await Write("INSERT INTO problem_history(id,problem_id,actor_user_id,action,details) VALUES(@event,@problem,@actor,'PROBLEM.CREATED',CAST(@details AS JSON))",("event",Guid.NewGuid().ToString("D")),("problem",problemId),("actor",actorId),("details",JsonSerializer.Serialize(new{demo=Marker})));
                foreach(var ticket in ids){
                    await Write("INSERT INTO problem_tickets(problem_id,ticket_id,client_id,linked_by_user_id) VALUES(@problem,@ticket,@client,@actor)",("problem",problemId),("ticket",ticket),("client",plan.Client),("actor",actorId));
                    await Write("INSERT INTO problem_history(id,problem_id,actor_user_id,action,details) VALUES(@event,@problem,@actor,'PROBLEM.TICKET_LINKED',CAST(@details AS JSON))",("event",Guid.NewGuid().ToString("D")),("problem",problemId),("actor",actorId),("details",JsonSerializer.Serialize(new{ticketId=ticket,demo=Marker})));
                }
                await Audit("PROBLEM.CREATED","PROBLEM",problemId,new{problemNumber,demo=Marker,tickets=ids});createdProblems++;
            }
            var verified=await Rows("SELECT COUNT(*) AS linked FROM problem_tickets pt JOIN tickets t ON t.id=pt.ticket_id JOIN problems p ON p.id=pt.problem_id WHERE p.id=@problem AND p.client_id=@client AND t.client_id=@client AND (t.site_id<=>p.site_id)",("problem",problemId),("client",plan.Client));
            if(Convert.ToInt32(verified[0]["linked"],CultureInfo.InvariantCulture)!=3)throw new Exception("Each problem must link exactly three same-organization/site tickets. Nothing committed.");
            output.Add(plan.Group.Organization+" · "+plan.SiteName+" · "+string.Join(", ",ticketNumbers)+" → "+problemNumber);
        }
        await tx.CommitAsync(ct);
        Console.WriteLine("COMMITTED: "+createdTickets+" new demo tickets and "+createdProblems+" new linked demo problems. Existing matching demos were skipped.");
        foreach(var line in output)Console.WriteLine(line);
        Console.WriteLine("Search "+Marker+". Tickets remain New; the Known Error problems document the sample shared solutions. No external messages, backups or data exports. Existing tickets and users were not changed.");
    }finally{await using var release=new MySqlCommand("SELECT RELEASE_LOCK('astra-linked-problem-demos-v1')",db);await release.ExecuteScalarAsync(ct);}
    return 0;
}catch(MySqlException e){Console.Error.WriteLine("Demo setup not committed: database error "+e.Number+". The transaction was rolled back; no backup or data copy was created.");return 1;}
catch(Exception e){Console.Error.WriteLine("Demo setup not committed: "+(e is TargetInvocationException t?t.InnerException?.Message:e.Message));return 1;}
record DemoGroup(string Organization,string Code,string[] Subjects,string Problem,string Root,string Workaround,string Solution);
