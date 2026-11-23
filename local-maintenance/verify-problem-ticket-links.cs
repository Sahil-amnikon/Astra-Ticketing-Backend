using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production",ContentRootPath=AppContext.BaseDirectory});builder.Logging.ClearProviders();builder.Services.AddSingleton<MySqlStore>(_=>throw new Exception("No database access allowed."));
await using var app=builder.Build();typeof(FeatureApis).GetMethod("MapSubmissionReviews",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[app.MapGroup("/api")]);
var endpoints=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>().ToArray();if(!endpoints.Any(e=>e.RoutePattern.RawText=="/api/problems/ticket-options"&&e.RequestDelegate is not null))throw new Exception("Ticket picker endpoint failed to bind.");
object? Call(string name,params object?[] values)=>typeof(MySqlStore).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,values);
var owner=Guid.NewGuid();if(!(bool)Call("ProblemOwnerIsActor",owner,owner.ToString())!||(bool)Call("ProblemOwnerIsActor",Guid.NewGuid(),owner.ToString())!)throw new Exception("Owner GUID comparison failed.");
if((bool)Call("ProblemBatchStatusAllowed","CLOSED")!||!(bool)Call("ProblemBatchStatusAllowed","OPEN")!)throw new Exception("Closed problem linking guard failed.");
if(!endpoints.Any(e=>e.RoutePattern.RawText=="/api/problems/{id:guid}/tickets/batch"&&e.RequestDelegate is not null))throw new Exception("Batch attachment route failed to bind.");
var id=Guid.NewGuid().ToString("D");var ids=(string[])Call("ProblemTicketIds",new object?[]{new[]{id,id.ToUpperInvariant()}})!;if(ids.Length!=1)throw new Exception("Duplicate ticket references were not normalized.");
foreach(var bad in new[]{new[]{"not-an-id"},Enumerable.Range(0,101).Select(_=>Guid.NewGuid().ToString("D")).ToArray()}){try{Call("ProblemTicketIds",new object?[]{bad});throw new Exception("Invalid ticket references accepted.");}catch(TargetInvocationException e)when(e.InnerException is ArgumentException){}}
await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(new{visibility=(string)Call("ProblemTicketVisibilitySql")!,search=(string)Call("ProblemTicketSearchSql")!,check=(string)Call("InitialProblemTicketCheckSql")!,released=(string)Call("SubmissionVisibleSql","PROBLEM","p.id","@id")!,insert=(string)typeof(MySqlStore).GetField("ProblemTicketInsertSql",BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()!,options=(string)Call("ProblemTicketOptionsFromSql")!,batch=(string)Call("ProblemBatchParentSql")!}));
Console.WriteLine("PASS: actual problem ticket picker binds; references validate, deduplicate and enforce the 100-ticket limit. No application database accessed.");
