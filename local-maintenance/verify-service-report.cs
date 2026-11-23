using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production",ContentRootPath=AppContext.BaseDirectory});builder.Logging.ClearProviders();builder.Services.AddSingleton<MySqlStore>(_=>throw new Exception("No database access allowed."));
await using var app=builder.Build();typeof(FeatureApis).GetMethod("MapReports",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[app.MapGroup("/api")]);
var endpoints=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>().ToArray();if(endpoints.Length!=3||endpoints.Any(e=>e.RequestDelegate is null))throw new Exception("Report endpoint failed to bind.");
object? Call(string name,params object?[] values)=>typeof(MySqlStore).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,values);
var id=Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
foreach(var value in new object[]{id,id.ToString(),id.ToString().ToUpperInvariant()})if(!(bool)Call("ReportIdentityMatches",value,id.ToString())!)throw new Exception("A valid client or technician identifier was rejected.");
if((bool)Call("ReportIdentityMatches",Guid.NewGuid(),id.ToString())!||(bool)Call("ReportIdentityMatches",null,id.ToString())!)throw new Exception("Invalid scope identifier accepted.");
var period=((DateTime Start,DateTime End,string From,string To))Call("FPeriod","2026-10-22","2026-11-20")!;
if(period.Start!=new DateTime(2026,10,22)||period.End!=new DateTime(2026,11,21))throw new Exception("Inclusive reporting dates changed.");
try{Call("FPeriod","2024-01-01","2026-11-20");throw new Exception("Oversized reporting period accepted.");}catch(TargetInvocationException e)when(e.InnerException is ArgumentException){}
await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(new{broken="SELECT t.status,COUNT(*) AS count FROM changes t WHERE "+(string)Call("FScope","t")!+" GROUP BY t.status",fixedSql=(string)Call("ServiceChangeReportSql")!}));
Console.WriteLine("PASS: report endpoints bind; GUID/string filter identifiers match safely; inclusive UTC reporting dates validate. No application database accessed.");
