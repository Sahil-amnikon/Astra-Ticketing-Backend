# Verify the published DELETE endpoint before replacing the running application.
from pathlib import Path
import os, subprocess, sys, tempfile
from xml.sax.saxutils import escape
if len(sys.argv)!=2: raise SystemExit('Pass the staged backend directory.')
maintenance=Path(__file__).resolve().parent
stage=Path(sys.argv[1]).resolve()
if stage.parent!=maintenance or not stage.name.startswith('.manager-publish.'):
    raise SystemExit('Unexpected staged build location.')
for name in ['Astra.Api.dll','MySqlConnector.dll']:
    if not (stage/name).is_file(): raise SystemExit('Published startup-test dependency missing.')
source=r'''
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
Console.WriteLine("PASS: all profile endpoints bind; activity kinds, record IDs, search limits, offsets, pages and approval filters validate. No application database accessed.");

'''
with tempfile.TemporaryDirectory(prefix='.profile-activity-preflight-',dir=maintenance) as tmp:
    folder=Path(tmp)
    project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App"/><Reference Include="Astra.Api"><HintPath>'+escape(str(stage/'Astra.Api.dll'))+'</HintPath></Reference><Reference Include="MySqlConnector"><HintPath>'+escape(str(stage/'MySqlConnector.dll'))+'</HintPath></Reference></ItemGroup></Project>'
    (folder/'Check.csproj').write_text(project)
    (folder/'Program.cs').write_text(source)
    (folder/'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources></configuration>')
    env=dict(os.environ,DOTNET_CLI_TELEMETRY_OPTOUT='1')
    subprocess.run(['dotnet','build',str(folder/'Check.csproj'),'--configfile',str(folder/'NuGet.Config'),'-p:NuGetAudit=false','--nologo','-v:q'],cwd=folder,env=env,check=True)
    subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Check.dll')],cwd=folder,env=env,check=True)
