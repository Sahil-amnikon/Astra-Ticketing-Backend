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

Console.WriteLine("PASS: published request routing endpoints and Standard/Normal approval rules. No database connected.");
'''
with tempfile.TemporaryDirectory(prefix='.change-request-routing-preflight-',dir=maintenance) as tmp:
    folder=Path(tmp)
    project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App"/><Reference Include="Astra.Api"><HintPath>'+escape(str(stage/'Astra.Api.dll'))+'</HintPath></Reference><Reference Include="MySqlConnector"><HintPath>'+escape(str(stage/'MySqlConnector.dll'))+'</HintPath></Reference></ItemGroup></Project>'
    (folder/'Check.csproj').write_text(project)
    (folder/'Program.cs').write_text(source)
    (folder/'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources></configuration>')
    env=dict(os.environ,DOTNET_CLI_TELEMETRY_OPTOUT='1')
    subprocess.run(['dotnet','build',str(folder/'Check.csproj'),'--configfile',str(folder/'NuGet.Config'),'-p:NuGetAudit=false','--nologo','-v:q'],cwd=folder,env=env,check=True)
    subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Check.dll')],cwd=folder,env=env,check=True)
