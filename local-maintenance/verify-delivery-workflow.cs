using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production",ContentRootPath=AppContext.BaseDirectory});builder.Logging.ClearProviders();builder.Services.AddSingleton<MySqlStore>(_=>throw new Exception("No database access allowed."));
await using var app=builder.Build();typeof(FeatureApis).GetMethod("MapSubmissionReviews",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[app.MapGroup("/api")]);
var endpoints=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>().ToArray();
if(!endpoints.Any(e=>e.RoutePattern.RawText=="/api/delivery-reviews/{kind}"&&e.RequestDelegate is not null))throw new Exception("Problem/service review route failed to bind.");
object? Call(string name,params object?[] values)=>typeof(MySqlStore).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,values);
foreach(var test in new[]{(true,true,false,false),(true,false,true,false),(true,false,false,true),(false,false,false,false)})if((bool)Call("ServiceActivationAllowed",test.Item1,test.Item2,test.Item3)!=test.Item4)throw new Exception("Unapproved service could become requestable.");
var description=(string)Call("ProblemSubmissionDescription",new CreateProblemRequest{Description="Repeated connection failure",Recurrence="Six tickets this week",BusinessImpact="Warehouse cannot print labels"})!;
foreach(var text in new[]{"Repeated connection failure","Six tickets this week","Warehouse cannot print labels"})if(!description.Contains(text))throw new Exception("Problem context was lost.");
try{Call("ProblemSubmissionDescription",new CreateProblemRequest{Description="Example",Recurrence=new string('x',2001)});throw new Exception("Oversized recurrence accepted.");}catch(TargetInvocationException e)when(e.InnerException is ArgumentException){}
if(!(bool)Call("SubmissionDecisionCurrent","PENDING",1,1)!||(bool)Call("SubmissionDecisionCurrent","APPROVED",1,1)!||(bool)Call("SubmissionDecisionCurrent","PENDING",2,1)!)throw new Exception("Approval version protection failed.");
await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(new{problem=(string)Call("DeliveryReviewScope","PROBLEM","@actor","e.author","e.client","e.site")!,service=(string)Call("DeliveryReviewScope","SERVICE","@actor","e.author","e.client","NULL")!}));
Console.WriteLine("PASS: actual PM/service review endpoint binds; recurrence and impact persist; oversized values and stale/repeated decisions are rejected. No database accessed.");
