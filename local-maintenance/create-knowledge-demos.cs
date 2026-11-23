using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

var topics = new[] {
 new Topic("Geometry","GEOMETRY","Restore a stalled print queue","Printing","Print jobs remain queued or show Offline.","Confirm the printer name and affected users.\nCheck that the printer is powered on and reachable.\nTry a small test page from one workstation.\nCheck the queue and obtain approval before cancelling other users' jobs.\nUse the approved driver and support procedure; avoid restarting a shared print service without approval.","Print a test page, confirm the affected application can print and record the result."),
 new Topic("Geometry","GEOMETRY","Investigate repeated Outlook sign-in prompts","Microsoft 365","Outlook repeatedly requests sign-in or reports an authentication error.","Record the exact error and time without collecting passwords or verification codes.\nCheck the device clock, internet connectivity and Microsoft 365 service health.\nTest authorized web access to distinguish a desktop issue from an account issue.\nCheck approved client updates and the account's permitted sign-in method.\nEscalate tenant policy, MFA or account changes to the authorized administrator.","Confirm Outlook connects and send a permitted test message. Record whether web and desktop access both work."),
 new Topic("Geometry","GEOMETRY","Troubleshoot access to a shared folder","File","Access denied or network path not found when opening a shared folder.","Record the approved folder path and whether one or several users are affected.\nConfirm network or approved VPN connectivity.\nCheck that the host name resolves to the expected server.\nVerify the user's approved group membership and both share and file permissions with the owner.\nDo not grant broad access or change production permissions without approval.","Ask the authorized user to test the required read/write action and confirm unrelated folders remain restricted."),
 new Topic("Ravid IT","RAVID","Investigate VPN certificate validation errors","Network","VPN connection reports an expired or untrusted certificate.","Record the VPN client, endpoint and non-sensitive error.\nCheck the workstation date, time and approved VPN client version.\nInspect the presented certificate subject, validity period and chain through approved tooling.\nCompare the endpoint with the documented configuration.\nEscalate renewal or gateway changes through Change Management; never disable certificate validation.","After an approved correction, reconnect using an authorized test account and confirm only permitted resources are reachable."),
 new Topic("Ravid IT","RAVID","Troubleshoot internal DNS name resolution","Network","An internal application name cannot be resolved.","Confirm the exact internal name and the affected users.\nCheck connectivity to the corporate network or approved VPN.\nCompare configured DNS servers with the approved network settings.\nUse the operating system's name-resolution tools and compare results with a working device.\nEscalate missing or incorrect DNS records to the network owner; avoid public DNS substitutions for internal services.","Confirm the name resolves to the approved address and the application opens. Document the result and scope of impact."),
 new Topic("Ravid IT","RAVID","Respond to a low disk-space alert","Infrastructure","The device reports low free space or an application cannot write files.","Record the volume, free space, alert time and affected service.\nCheck recent growth and identify approved temporary-file or log retention procedures.\nConfirm ownership before deleting or moving any files.\nEscalate database, application and server volumes to their service owner.\nCapacity expansion, service interruption or retention changes require the appropriate approval.","Confirm free space meets the service's documented threshold, the application can write and monitoring clears. Record the corrective action.")
};
static string Id(object? value) => Guid.TryParse(Convert.ToString(value),out var id) ? id.ToString("D") : throw new Exception("Invalid database record ID.");
KnowledgeArticleRequest Request(Topic topic,string client,string? category) => new() {
 Title=$"[DEMO-KB-V1-{topic.Code}] {topic.Title}", ClientId=client,CategoryId=category,Visibility="STAFF",Status="DRAFT",
 Body=$"SAMPLE KNOWLEDGE ARTICLE — review before operational use.\n\nSymptoms\n{topic.Error}\n\nTroubleshooting\n{topic.Steps}\n\nValidation\n{topic.Validation}\n\nEscalation\nIf unresolved, link the affected ticket, capture non-sensitive evidence and contact the team lead. Follow the approved change process before altering production systems.",
 Details=new KnowledgeDetails {Scope="CLIENT_SPECIFIC",ArticleType="TROUBLESHOOTING",Product=topic.Product,Environment="ALL",ContentTitle=topic.Title,ErrorMessage=topic.Error,Keywords=topic.Product+", troubleshooting, sample",Prerequisites="Authorized support access and an affected ticket. Do not collect credentials or remove user data."}
};
if(args is ["--check-plan"]){
 if(topics.Length!=6||topics.Count(t=>t.Code=="GEOMETRY")!=3||topics.Count(t=>t.Code=="RAVID")!=3)throw new Exception("Expected three per organization.");
 foreach(var topic in topics){var request=Request(topic,Guid.NewGuid().ToString("D"),null);if(request.Title.Length>200||request.Body.Length<100||request.Visibility!="STAFF"||request.Status!="DRAFT"||request.Details?.ErrorMessage.Length<10)throw new Exception("Incomplete article.");}
 Console.WriteLine("PASS: six complete staff-only sample articles, three per organization, submitted for approval. No database or network accessed.");return 0;
}
try {
 var settings=new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ASTRA_DEMO_CONNECTION")??throw new Exception("Application database settings missing.")){GuidFormat=MySqlGuidFormat.None};
 if(settings.Database!="astra_ticketing")throw new Exception("Unexpected database.");
 await using var db=new MySqlConnection(settings.ConnectionString);await db.OpenAsync();
 await using var mutex=new MySqlCommand("SELECT GET_LOCK('astra-knowledge-demos-v1',10)",db);
 if(Convert.ToInt32(await mutex.ExecuteScalarAsync())!=1)throw new Exception("Another article creator is running.");
 try {
  async Task<List<Dictionary<string,object?>>> Rows(string sql,params (string Name,object? Value)[] args){await using var cmd=new MySqlCommand(sql,db);foreach(var a in args)cmd.Parameters.AddWithValue("@"+a.Name,a.Value);await using var reader=await cmd.ExecuteReaderAsync();var rows=new List<Dictionary<string,object?>>();while(await reader.ReadAsync()){var row=new Dictionary<string,object?>();for(var i=0;i<reader.FieldCount;i++)row[reader.GetName(i)]=reader.IsDBNull(i)?null:reader.GetValue(i);rows.Add(row);}return rows;}
  var store=new MySqlStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:MySql"]=settings.ConnectionString}).Build());
  var plans=new List<(Topic Topic,string Client,string Actor,string? Category)>();
  // Resolve both organizations and eligible authors before creating any articles.
  foreach(var group in topics.GroupBy(t=>t.Organization)){
   var clients=await Rows("SELECT id FROM clients WHERE status='ACTIVE' AND LOWER(TRIM(name))=LOWER(@name)",("name",group.Key));
   if(clients.Count!=1)throw new Exception("Expected one active organization named "+group.Key+". No new articles created.");
   var client=Id(clients[0]["id"]);
   var staff=await Rows("SELECT u.id,u.display_name FROM users u JOIN client_users cu ON cu.user_id=u.id WHERE cu.client_id=@client AND u.active=1 AND (u.access_expires_at IS NULL OR u.access_expires_at>UTC_TIMESTAMP(6)) AND EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code='TECHNICIAN') AND NOT EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code IN ('ADMIN','MANAGER','TEAM_LEAD')) ORDER BY (LOWER(u.email)='alamin.shaikh@amnikontechnologies.com') DESC,u.display_name,u.id",("client",client));
   if(staff.Count==0)throw new Exception("No active technician assigned to "+group.Key+". No new articles created.");
   string? author=null;JsonElement options=default;
   foreach(var user in staff){var candidate=Id(user["id"]);var candidateOptions=JsonSerializer.SerializeToElement(await store.GetKnowledgeOptions(candidate,CancellationToken.None));if(candidateOptions.GetProperty("clients").EnumerateArray().Any(c=>Id(c.GetProperty("id").ToString())==client)){author=candidate;options=candidateOptions;Console.WriteLine(group.Key+": author "+user["display_name"]);break;}}
   if(author is null)throw new Exception("No technician has the required article scope for "+group.Key+".");
   var category=options.GetProperty("categories").EnumerateArray().FirstOrDefault();var categoryId=category.ValueKind==JsonValueKind.Undefined?null:Id(category.GetProperty("id").ToString());
   foreach(var topic in group)plans.Add((topic,client,author,categoryId));
  }
  foreach(var plan in plans){
   var request=Request(plan.Topic,plan.Client,plan.Category);
   var existing=await Rows("SELECT a.id,a.status,r.status AS review_status FROM knowledge_articles a LEFT JOIN submission_reviews r ON r.kind='KNOWLEDGE' AND r.entity_id=a.id WHERE a.client_id=@client AND a.title=@title",("client",plan.Client),("title",request.Title));
   if(existing.Count>1)throw new Exception("Duplicate sample titles found; no records removed.");
   if(existing.Count==1){Console.WriteLine("EXISTS: "+request.Title+" · "+existing[0]["status"]);continue;}
   var result=await store.SaveKnowledgeArticle(plan.Actor,null,request,CancellationToken.None)??throw new Exception("Application denied article creation.");
   var saved=await Rows("SELECT a.status,r.status AS review_status FROM knowledge_articles a JOIN submission_reviews r ON r.kind='KNOWLEDGE' AND r.entity_id=a.id WHERE a.id=@id AND a.client_id=@client",("id",result.Id),("client",plan.Client));
   if(saved.Count!=1||Convert.ToString(saved[0]["status"])!="DRAFT"||Convert.ToString(saved[0]["review_status"])!="PENDING")throw new Exception("Article did not reach pending review. Stop and report this result.");
   Console.WriteLine("CREATED: "+request.Title+" · Awaiting review · "+result.Id);
  }
  Console.WriteLine("Complete: three articles per organization. Open Review KB to review and publish them. Existing accounts, roles and articles retained.");
 }finally{await using var release=new MySqlCommand("SELECT RELEASE_LOCK('astra-knowledge-demos-v1')",db);await release.ExecuteScalarAsync();}
 return 0;
}catch(MySqlException ex){Console.Error.WriteLine("Article setup stopped: database error "+ex.Number+". Completed articles retained; rerunning skips existing samples.");return 1;}
catch(Exception ex){Console.Error.WriteLine("Article setup stopped: "+ex.Message+" Completed articles retained; rerunning skips existing samples.");return 1;}
record Topic(string Organization,string Code,string Title,string Product,string Error,string Steps,string Validation);
