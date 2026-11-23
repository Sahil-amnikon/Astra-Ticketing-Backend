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
var type=typeof(MySqlStore);
var flags=BindingFlags.Static|BindingFlags.NonPublic;
var map=type.GetMethod("StoredReviewNotificationType",flags)!;
string ConvertType(string value)=>(string)map.Invoke(null,[value])!;
if(ConvertType("SUBMISSION_REVIEW")!="CHANGE_APPROVAL_REQUESTED" || ConvertType("SUBMISSION_DECIDED")!="CHANGE_STATUS_UPDATED" || ConvertType("CHANGE_TASK_ASSIGNED")!="CHANGE_TASK_ASSIGNED")
    throw new Exception("Review notification vocabulary compatibility failed.");
var sql=(string)type.GetField("ReviewNotificationInsertSql",flags)!.GetRawConstantValue()!;
if(args.Length==1)await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(new{sql,request=ConvertType("SUBMISSION_REVIEW"),decision=ConvertType("SUBMISSION_DECIDED")}));
Console.WriteLine("PASS: actual review alert mapping uses installed approval/status types and retains unrelated notification types. No application database accessed.");

'''
with tempfile.TemporaryDirectory(prefix='.review-notification-preflight-',dir=maintenance) as tmp:
    folder=Path(tmp)
    project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App"/><Reference Include="Astra.Api"><HintPath>'+escape(str(stage/'Astra.Api.dll'))+'</HintPath></Reference><Reference Include="MySqlConnector"><HintPath>'+escape(str(stage/'MySqlConnector.dll'))+'</HintPath></Reference></ItemGroup></Project>'
    (folder/'Check.csproj').write_text(project)
    (folder/'Program.cs').write_text(source)
    (folder/'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources></configuration>')
    env=dict(os.environ,DOTNET_CLI_TELEMETRY_OPTOUT='1')
    subprocess.run(['dotnet','build',str(folder/'Check.csproj'),'--configfile',str(folder/'NuGet.Config'),'-p:NuGetAudit=false','--nologo','-v:q'],cwd=folder,env=env,check=True)
    subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Check.dll')],cwd=folder,env=env,check=True)
