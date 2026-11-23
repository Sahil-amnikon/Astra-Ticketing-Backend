using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production",ContentRootPath=AppContext.BaseDirectory});
builder.Logging.ClearProviders();builder.Services.AddSingleton<MySqlStore>(_=>throw new Exception("No database access allowed."));
await using var app=builder.Build();var api=app.MapGroup("/api");
typeof(FeatureApis).GetMethod("MapSubmissionReviews",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[api]);
var endpoints=((IEndpointRouteBuilder)app).DataSources.SelectMany(source=>source.Endpoints).OfType<RouteEndpoint>().ToArray();
if(endpoints.Length!=15||endpoints.Any(e=>e.RequestDelegate is null)||!endpoints.Any(e=>e.RoutePattern.RawText=="/api/changes/templates/{id:guid}" && e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("DELETE")==true))throw new Exception("Review queue or template deletion routes did not build.");
object? Call(string name,params object?[] values)=>typeof(MySqlStore).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,values);
QueryCollection Query(params (string Key,string Value)[] pairs)=>new(pairs.ToDictionary(p=>p.Key,p=>new StringValues(p.Value)));
var defaults=(ChangeReviewFilters)Call("ParseChangeReviewFilters",new QueryCollection())!;
if(defaults.Status!="PENDING"||defaults.Page!=1||defaults.PageSize!=20)throw new Exception("The queue must default to waiting requests.");
var filtered=(ChangeReviewFilters)Call("ParseChangeReviewFilters",Query(("status","ALL"),("q","  printer  "),("clientId","AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE"),("from","2026-11-18T00:00:00Z"),("to","2026-11-19T00:00:00Z"),("page","2"),("pageSize","500")))!;
if(filtered.Status is not null||filtered.Search!="printer"||filtered.Page!=2||filtered.PageSize!=100||filtered.Client!="aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"||filtered.From?.Kind!=DateTimeKind.Utc)throw new Exception("Review filters did not normalize correctly.");
foreach(var invalid in new[]{Query(("status","UNKNOWN")),Query(("clientId","wrong")),Query(("from","not-a-date")),Query(("from","2026-11-19T00:00:00Z"),("to","2026-11-18T00:00:00Z"))}){
 try{Call("ParseChangeReviewFilters",invalid);throw new Exception("Invalid filter accepted.");}catch(TargetInvocationException e)when(e.InnerException is ArgumentException){}
}
string Property(string name)=>(string)typeof(MySqlStore).GetProperty(name,BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
string Constant(string name)=>(string)typeof(MySqlStore).GetField(name,BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()!;
var sql=new {source=Property("ChangeReviewSource"),scope=Property("ChangeReviewScope"),filter=Property("ChangeReviewFilterSql"),columns=Property("ChangeReviewColumns"),role=Constant("ChangeReviewRoleSql"),planAuthor=Constant("ChangePlanAuthorSql"),published=Constant("PublishedChangePlansSql"),planScope=Property("ChangePlanReviewScope"),planDecision=Property("ChangePlanDecisionScope")};
var data=new Dictionary<string,object?>{["title"]="Printer update",["description"]="Reusable plan",["category"]="SERVER",["changeType"]="NORMAL",["risk"]="LOW",["downtimeMinutes"]=10,["outageExpected"]=true,["clientNotificationEnabled"]=true,["clientId"]="private",["ownerId"]="private",["status"]="APPROVED"};
foreach(var field in new[]{"impact","reason","businessJustification","implementationPlan","testPlan","rollbackPlan","communicationPlan"})data[field]="Reusable instructions";
JsonElement Validate(Dictionary<string,object?> values)=>(JsonElement)Call("ValidatedChangePlan",JsonSerializer.SerializeToElement(values))!;
var plan=Validate(data);
foreach(var field in new[]{"clientId","ownerId","status"})if(plan.TryGetProperty(field,out _))throw new Exception("Private data entered a reusable template.");
foreach(var pair in new (string Key,object? Value)[]{("risk","BAD"),("changeType","BAD"),("downtimeMinutes",-1),("plannedStart","2026-11-19T12:00:00Z"),("implementationPlan","")}){
 var invalid=new Dictionary<string,object?>(data){[pair.Key]=pair.Value};
 try{Validate(invalid);throw new Exception("Invalid reusable template accepted: "+pair.Key);}catch(TargetInvocationException e)when(e.InnerException is ArgumentException){}
}
Console.WriteLine("PASS: template validation rejects invalid common fields and excludes client, owner and approval state.");
await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(sql));
Console.WriteLine("PASS: actual Review CM endpoints build; waiting is the default, history filters normalize, and invalid filters are rejected. No database accessed.");
