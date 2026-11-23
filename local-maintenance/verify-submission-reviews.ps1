$ErrorActionPreference = 'Stop'
$backend = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $PSScriptRoot ('.submission-check-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
try {
  $api = [Security.SecurityElement]::Escape((Join-Path $backend 'bin/Debug/net10.0/Astra.Api.dll'))
  $mysql = [Security.SecurityElement]::Escape((Join-Path $backend 'bin/Debug/net10.0/MySqlConnector.dll'))
  "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include=`"Microsoft.AspNetCore.App`"/><Reference Include=`"Astra.Api`"><HintPath>$api</HintPath></Reference><Reference Include=`"MySqlConnector`"><HintPath>$mysql</HintPath></Reference></ItemGroup></Project>" | Set-Content -LiteralPath (Join-Path $scratch 'Check.csproj')
  '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath (Join-Path $scratch 'NuGet.Config')
  @'
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production",ContentRootPath=AppContext.BaseDirectory});
builder.Logging.ClearProviders();
builder.Services.AddSingleton<MySqlStore>(_=>throw new Exception("No database access is allowed in this test"));
await using var app=builder.Build();var api=app.MapGroup("/api");
typeof(FeatureApis).GetMethod("MapSubmissionReviews",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[api]);
var endpoints=((IEndpointRouteBuilder)app).DataSources.SelectMany(s=>s.Endpoints).OfType<RouteEndpoint>().ToArray();
if(endpoints.Length!=15||endpoints.Any(e=>e.RequestDelegate is null))throw new Exception("Review endpoints did not build");
object? Call(string name,params object?[] values)=>typeof(MySqlStore).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,values);
foreach(var test in new[]{("PENDING",1,1,true),("PENDING",2,1,false),("APPROVED",2,2,false),("REJECTED",2,2,false),("PENDING",0,0,false)})
 if((bool)Call("SubmissionDecisionCurrent",test.Item1,test.Item2,test.Item3)!=test.Item4)throw new Exception("Stale/repeated decision protection failed");
string Key(string kind,string id,int revision,string phase,object user)=>(string)Call("ReviewNotificationKey",kind,id,revision,phase,user)!;
var recipient=Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
var keys=new[]{Key("CHANGE","first",1,"REQUESTED",recipient),Key("CHANGE","first",2,"REQUESTED",recipient),Key("CHANGE","first",1,"APPROVED",recipient),Key("CHANGE_TEMPLATE","first",1,"REQUESTED",recipient),Key("CHANGE","second",1,"REQUESTED",recipient),Key("CHANGE","first",1,"REQUESTED","different-user")};
if(keys.Distinct().Count()!=keys.Length||keys.Any(key=>key.Length!=64)||keys[0]!=Key("CHANGE","first",1,"REQUESTED",recipient.ToString()))throw new Exception("Review event keys must distinguish recipients, entities, revisions, kinds and decisions, with consistent GUID handling.");
var policy=new {reviewer=(string)Call("SubmissionReviewerSql","@actor","@author")!,visible=(string)Call("SubmissionVisibleSql","KNOWLEDGE","e.id","@actor")!,notificationSql=(string)typeof(MySqlStore).GetField("ReviewNotificationInsertSql",BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()!,keys};
await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(policy));
Console.WriteLine("PASS: actual review endpoints build; stale, repeated and nonpending approvals are rejected.");
'@ | Set-Content -LiteralPath (Join-Path $scratch 'Program.cs')
  & dotnet build (Join-Path $scratch 'Check.csproj') --configfile (Join-Path $scratch 'NuGet.Config') -p:NuGetAudit=false --nologo -v:q
  if ($LASTEXITCODE -ne 0) { throw 'Review test build failed.' }
  & dotnet (Join-Path $scratch 'bin/Debug/net10.0/Check.dll') (Join-Path $scratch 'policy.json')
  if ($LASTEXITCODE -ne 0) { throw 'Review endpoint or version protection failed.' }
  & node (Join-Path $PSScriptRoot 'verify-submission-policy.mjs') (Join-Path $scratch 'policy.json')
  if ($LASTEXITCODE -ne 0) { throw 'Review authorization policy failed.' }
} finally {
  $resolved = [IO.Path]::GetFullPath($scratch)
  $allowed = [IO.Path]::GetFullPath($PSScriptRoot) + [IO.Path]::DirectorySeparatorChar
  if (!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolved -Leaf).StartsWith('.submission-check-')) { throw 'Unexpected test directory.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
