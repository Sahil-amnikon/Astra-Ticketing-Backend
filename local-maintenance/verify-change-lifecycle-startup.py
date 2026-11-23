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
string[] Next(string state,string review,bool owner,bool reviewer)=>(string[])typeof(MySqlStore).GetMethod("ReviewedChangeNextStatuses",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[state,review,owner,reviewer])!;
if(Next("SUBMITTED","PENDING",true,false).Length!=0||!Next("APPROVED","APPROVED",true,false).SequenceEqual(new[]{"IN_PROGRESS"})||Next("COMPLETED","APPROVED",true,true).Length!=0||!Next("COMPLETED","APPROVED",false,true).SequenceEqual(new[]{"CLOSED"}))throw new Exception("The published change lifecycle differs from the tested workflow.");
var sql=(string)typeof(MySqlStore).GetField("ReviewNotificationInsertSql",BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()!;
if(!sql.Contains("event_key"))throw new Exception("The required event-key fix is missing.");
Console.WriteLine("PASS: published change lifecycle blocks work before approval and self-closure; notification event keys are present. No database connected.");
'''
with tempfile.TemporaryDirectory(prefix='.change-lifecycle-preflight-',dir=maintenance) as tmp:
    folder=Path(tmp)
    project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App"/><Reference Include="Astra.Api"><HintPath>'+escape(str(stage/'Astra.Api.dll'))+'</HintPath></Reference><Reference Include="MySqlConnector"><HintPath>'+escape(str(stage/'MySqlConnector.dll'))+'</HintPath></Reference></ItemGroup></Project>'
    (folder/'Check.csproj').write_text(project)
    (folder/'Program.cs').write_text(source)
    (folder/'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources></configuration>')
    env=dict(os.environ,DOTNET_CLI_TELEMETRY_OPTOUT='1')
    subprocess.run(['dotnet','build',str(folder/'Check.csproj'),'--configfile',str(folder/'NuGet.Config'),'-p:NuGetAudit=false','--nologo','-v:q'],cwd=folder,env=env,check=True)
    subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Check.dll')],cwd=folder,env=env,check=True)
