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
Console.WriteLine("PASS: actual PM/service review endpoint binds; recurrence and impact persist; oversized values and stale/repeated decisions are rejected. No database accessed.");

'''
with tempfile.TemporaryDirectory(prefix='.delivery-preflight-',dir=maintenance) as tmp:
    folder=Path(tmp)
    project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App"/><Reference Include="Astra.Api"><HintPath>'+escape(str(stage/'Astra.Api.dll'))+'</HintPath></Reference><Reference Include="MySqlConnector"><HintPath>'+escape(str(stage/'MySqlConnector.dll'))+'</HintPath></Reference></ItemGroup></Project>'
    (folder/'Check.csproj').write_text(project)
    (folder/'Program.cs').write_text(source)
    (folder/'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources></configuration>')
    env=dict(os.environ,DOTNET_CLI_TELEMETRY_OPTOUT='1')
    subprocess.run(['dotnet','build',str(folder/'Check.csproj'),'--configfile',str(folder/'NuGet.Config'),'-p:NuGetAudit=false','--nologo','-v:q'],cwd=folder,env=env,check=True)
    subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Check.dll')],cwd=folder,env=env,check=True)
