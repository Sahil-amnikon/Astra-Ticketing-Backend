using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production",ContentRootPath=AppContext.BaseDirectory});builder.Logging.ClearProviders();builder.Services.AddSingleton<MySqlStore>(_=>throw new Exception("No database access allowed."));
await using var app=builder.Build();typeof(FeatureApis).GetMethod("MapProfile",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[app.MapGroup("/api")]);var endpoints=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>().ToArray();
if(endpoints.Length!=5||endpoints.Any(e=>e.RequestDelegate is null))throw new Exception("Personal profile activity routes failed to bind.");
object? Call(string name,params object?[] values)=>typeof(MySqlStore).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,values);
QueryCollection Query(params(string Key,string Value)[] values)=>new(values.ToDictionary(x=>x.Key,x=>new StringValues(x.Value)));
var filter=((string Status,string Review,string Query,int Page,string? Id,int Offset))Call("ProfileActivityFilters",Query(("offset","1000"),("page","-3"),("q","  hello  "),("review","PENDING")))!;
if(filter.Offset!=840||filter.Page!=1||filter.Query!="hello"||filter.Review!="PENDING")throw new Exception("Profile filters failed to normalize.");
foreach(var bad in new[]{Query(("review","EVERYBODY")),Query(("id","bad")),Query(("q",new string('x',201)))}){try{Call("ProfileActivityFilters",bad);throw new Exception("Invalid activity filter accepted.");}catch(TargetInvocationException e)when(e.InnerException is ArgumentException){}}
try{Call("ProfileKind","ALL_USERS");throw new Exception("Invalid activity kind accepted.");}catch(TargetInvocationException e)when(e.InnerException is ArgumentException){}
var kinds=new[]{"CHANGE","PROBLEM","KNOWLEDGE","SERVICE","REQUEST","RESOLVED_TODAY","RESOLVED_30","ASSIGNED","QA"};
var sources=kinds.ToDictionary(kind=>kind,kind=>Call("ProfileSourceFor",kind,true));
var history=(string)typeof(MySqlStore).GetField("ServiceRequestHistorySql",BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()!;
await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(new{sources,serviceCheck=(string)Call("ProfileServiceTemplateSql")!,history}));
Console.WriteLine("PASS: all profile endpoints bind; activity kinds, record IDs, search limits, offsets, pages and approval filters validate. No application database accessed.");
