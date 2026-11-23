using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production",ContentRootPath=AppContext.BaseDirectory});builder.Logging.ClearProviders();builder.Services.AddSingleton<MySqlStore>(_=>throw new Exception("No application database access allowed."));
await using var app=builder.Build();typeof(FeatureApis).GetMethod("MapSubmissionReviews",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[app.MapGroup("/api")]);
var endpoints=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>().ToArray();
foreach(var path in new[]{"/api/knowledge-reviews","/api/knowledge/{id:guid}/files/{file}"})if(!endpoints.Any(e=>e.RoutePattern.RawText==path&&e.RequestDelegate is not null))throw new Exception("Knowledge endpoint failed: "+path);
object? Call(string name,params object?[] values)=>typeof(MySqlStore).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,values);
async Task<object> Prepare(string id,string text,string old,KnowledgeArticleRequest request){var task=(Task)Call("PrepareKnowledgeBody",id,text,old,request,CancellationToken.None)!;await task;return task.GetType().GetProperty("Result")!.GetValue(task)!;}
string Body(object value)=>(string)value.GetType().GetProperty("Body")!.GetValue(value)!;
var id=Guid.NewGuid().ToString("D");var directory=Path.Combine(AppContext.BaseDirectory,"App_Data","knowledge",id);
try{
 var data=Convert.ToBase64String(new byte[]{137,80,78,71,13,10,26,10,1,2,3});
 var prepared=await Prepare(id,"Step one: restart the application.","",new KnowledgeArticleRequest{Attachments=[new(){Name="screenshot.png",Data=data}]});
 var body=Body(prepared);if(!body.Contains("image/png")||!body.Contains("screenshot.png"))throw new Exception("Image manifest missing.");
 var detailed=await Prepare(id,"Detailed article","",new KnowledgeArticleRequest{ClientId="client",Details=new KnowledgeDetails{Scope="CLIENT_SPECIFIC",Product="Microsoft 365",Version="Desktop",Environment="PRODUCTION",Keywords="outlook, sign-in",ReviewDate="2027-01-15"}});
 if(!Body(detailed).StartsWith("[ASTRA_KB_DETAILS_V1]") || !Body(detailed).Contains("Microsoft 365"))throw new Exception("Article details were not saved.");
 var issueDetails=new KnowledgeDetails{ContentTitle="Outlook sign-in failure",ErrorMessage="AADSTS50011\nRedirect URI mismatch"};
 var issueBody=Body(await Prepare(id,"Follow the documented solution.","",new KnowledgeArticleRequest{Details=issueDetails}));
 var savedIssue=(KnowledgeDetails)Call("ReadKnowledgeDetails",issueBody)!;
 if(savedIssue.ContentTitle!=issueDetails.ContentTitle||savedIssue.ErrorMessage!=issueDetails.ErrorMessage)throw new Exception("Issue fields failed save/read round trip.");
 var reopenedIssue=(KnowledgeDetails)Call("ReadKnowledgeDetails",Body(await Prepare(id,"Updated solution.",issueBody,new KnowledgeArticleRequest())))!;
 if(reopenedIssue.ErrorMessage!=issueDetails.ErrorMessage)throw new Exception("Issue fields were lost during edit.");
 foreach(var oversized in new[]{new KnowledgeDetails{ContentTitle=new string('x',301)},new KnowledgeDetails{ErrorMessage=new string('x',2001)}}){try{await Prepare(id,"Example text","",new KnowledgeArticleRequest{Details=oversized});throw new Exception("Oversized issue field accepted.");}catch(ArgumentException){}}
 foreach(var invalidDetails in new[]{new KnowledgeDetails{Scope="CLIENT_SPECIFIC"},new KnowledgeDetails{Scope="PUBLIC"},new KnowledgeDetails{Environment="BAD"},new KnowledgeDetails{ArticleType="BAD"},new KnowledgeDetails{ReviewDate="2026-02-31"},new KnowledgeDetails{Product=new string('x',201)}}){try{await Prepare(id,"Example text","",new KnowledgeArticleRequest{Details=invalidDetails});throw new Exception("Invalid scope or specification accepted.");}catch(ArgumentException){}}
 using var manifest=JsonDocument.Parse(body.Split("\n\n[ASTRA_KB_MEDIA_V1]")[1]);var hash=manifest.RootElement[0].GetProperty("id").GetString()!;
 if(!File.Exists(Path.Combine(directory,hash)))throw new Exception("Attachment not stored.");
 var inlineText="Before screenshot\n[[KB_IMAGE:"+hash+"]]\nAfter screenshot";
 var inlineSaved=Body(await Prepare(id,inlineText,body,new KnowledgeArticleRequest{RetainedAttachments=[hash]}));
 if(!inlineSaved.Contains(inlineText)||!inlineSaved.Contains("image/png"))throw new Exception("Inline image position or retained image was lost during save.");
 var edit=await Prepare(id,"Updated instructions",body,new KnowledgeArticleRequest{RetainedAttachments=[hash]});if(!Body(edit).Contains(hash))throw new Exception("Retained attachment lost.");
 var remove=await Prepare(id,"Updated without file",body,new KnowledgeArticleRequest());if(Body(remove).Contains(hash))throw new Exception("Removed attachment remained accessible through manifest.");
 foreach(var request in new[]{new KnowledgeArticleRequest{RetainedAttachments=["not-owned"]},new KnowledgeArticleRequest{Attachments=[new(){Name="bad",Data="not base64"}]},new KnowledgeArticleRequest{Attachments=[new(){Name="empty",Data=""}]},new KnowledgeArticleRequest{Attachments=Enumerable.Repeat(new KnowledgeUpload{Name="file",Data=data},11).ToArray()}}){try{await Prepare(id,"Example text","",request);throw new Exception("Unsafe attachment accepted.");}catch(ArgumentException){}}
 try{await Prepare(id,"Invalid\n\n[ASTRA_KB_MEDIA_V1][]","",new KnowledgeArticleRequest());throw new Exception("Forged manifest accepted.");}catch(ArgumentException){}
 if((string)Call("KnowledgeImageType",System.Text.Encoding.UTF8.GetBytes("<svg onload=alert(1)>"))! != "application/octet-stream")throw new Exception("Active content could be rendered inline.");
}finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(new{scope=(string)Call("KnowledgeReviewScope","@actor","e.author","e.client")!}));
Console.WriteLine("PASS: actual KB endpoints bind; image signatures, manifests, retained files, removal, forged refs, empty files, limits and active-content download handling verified. Test files only; no application database.");
