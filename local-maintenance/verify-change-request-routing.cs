using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production",ContentRootPath=AppContext.BaseDirectory});builder.Logging.ClearProviders();builder.Services.AddSingleton<MySqlStore>(_=>throw new Exception("No database access allowed in delegate checks."));
await using var app=builder.Build();typeof(FeatureApis).GetMethod("MapChanges",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[app.MapGroup("/api")]);
var endpoints=((IEndpointRouteBuilder)app).DataSources.SelectMany(source=>source.Endpoints).OfType<RouteEndpoint>().ToArray();
foreach(var route in new[]{"/api/changes/people","/api/changes/review-access","/api/changes/review-session"})if(!endpoints.Any(endpoint=>endpoint.RoutePattern.RawText==route&&endpoint.RequestDelegate is not null))throw new Exception("New change routing endpoint failed to bind: "+route);
object? Call(string name,params object?[] values)=>typeof(MySqlStore).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,values);
bool Approve(string type,string actor,string creator,string owner,string? requester,string selected)=>(bool)Call("ChangeApprovalRule",type,actor,creator,owner,requester,selected)!;
if(!Approve("STANDARD","a","a","a","a","a"))throw new Exception("Standard self-approval failed.");
foreach(var field in new[]{"creator","owner","requester"})if(Approve("NORMAL","a",field=="creator"?"a":"c",field=="owner"?"a":"o",field=="requester"?"a":"r","a"))throw new Exception("Normal self-approval accepted.");
if(!Approve("NORMAL","approver","creator","owner","requester","approver")||Approve("NORMAL","manager","creator","owner","requester","approver")||Approve("EMERGENCY","a","c","o","r","a"))throw new Exception("Designated approver or removed Emergency rule failed.");
var id="aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
var body=JsonSerializer.SerializeToElement(new{siteIds=new[]{id,id}});
if(((string[])Call("ChangeSelectionIds",body,"siteIds","siteId",50)!).Length!=1)throw new Exception("Duplicate sites not normalized.");
foreach(var invalid in new[]{JsonSerializer.SerializeToElement(new{siteIds=new[]{"bad"}}),JsonSerializer.SerializeToElement(new{siteIds="not-an-array"})}){
 try{Call("ChangeSelectionIds",invalid,"siteIds","siteId",50);throw new Exception("Invalid site selection accepted.");}catch(TargetInvocationException error)when(error.InnerException is ArgumentException){}
}
var sql=new{selected=(string)Call("SelectedChangeApproverSql","@actor","c.id")!,visible=(string)Call("ChangeRequestVisibleToReviewerSql","@actor","c.id","r.submitted_by_user_id")!,decide=(string)Call("ChangeRequestDecisionSql","@actor","c.id","r.submitted_by_user_id")!,person=(string)Call("ChangePersonScopeSql","@person","@client")!,sites=(string)Call("ChangePersonSiteScopeSql","@person","@sites")!};
await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(sql));
Console.WriteLine("PASS: Standard self-approval allowed; Normal creator/owner/requester self-approval denied; only the selected approver can decide; Emergency rejected; multiple-site values validate and deduplicate.");
