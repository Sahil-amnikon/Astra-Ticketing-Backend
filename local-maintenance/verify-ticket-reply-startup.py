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
var assembly=typeof(MySqlStore).Assembly;
var validate=assembly.GetType("PublicTicketReplyPolicy")!.GetMethod("Validate",BindingFlags.NonPublic|BindingFlags.Static)!;
foreach(var reply in new[]{"Please restart the workstation and try again.","The replacement device is ready for collection.","I have escalated this to the network team.","Test reply","Please use the official password reset portal."})
    validate.Invoke(null,[reply,new[]{"Outlook fails to open","The email application does not start."}]);
foreach(var reply in new[]{"Please send your password.","Share your verification code.","Please give me your API key."})
{
    try {validate.Invoke(null,[reply,Array.Empty<string>()]);throw new Exception("Credential request was accepted.");}
    catch(TargetInvocationException e) when(e.InnerException is FeatureFailure failure && failure.Code=="PUBLIC_REPLY_CREDENTIAL_REQUEST") {}
}
var convert=typeof(MySqlStore).GetMethod("TicketNotificationRecipientId",BindingFlags.NonPublic|BindingFlags.Static)!;
var id=Guid.NewGuid();
if((string)convert.Invoke(null,[id])! != id.ToString("D") || (string)convert.Invoke(null,[id.ToString("D")])! != id.ToString("D"))
    throw new Exception("Native GUID and string IDs must produce the same notification recipient.");
try {convert.Invoke(null,["invalid"]);throw new Exception("Invalid recipient was accepted.");}
catch(TargetInvocationException e) when(e.InnerException is InvalidOperationException) {}
Console.WriteLine("PASS: normal public replies pass validation without word-overlap rejection; credential-request protection remains; native GUID notification recipients convert correctly. No application database, email or network accessed.");

'''
with tempfile.TemporaryDirectory(prefix='.ticket-reply-preflight-',dir=maintenance) as tmp:
    folder=Path(tmp)
    project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App"/><Reference Include="Astra.Api"><HintPath>'+escape(str(stage/'Astra.Api.dll'))+'</HintPath></Reference><Reference Include="MySqlConnector"><HintPath>'+escape(str(stage/'MySqlConnector.dll'))+'</HintPath></Reference></ItemGroup></Project>'
    (folder/'Check.csproj').write_text(project)
    (folder/'Program.cs').write_text(source)
    (folder/'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources></configuration>')
    env=dict(os.environ,DOTNET_CLI_TELEMETRY_OPTOUT='1')
    subprocess.run(['dotnet','build',str(folder/'Check.csproj'),'--configfile',str(folder/'NuGet.Config'),'-p:NuGetAudit=false','--nologo','-v:q'],cwd=folder,env=env,check=True)
    subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Check.dll')],cwd=folder,env=env,check=True)
