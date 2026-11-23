"""Associate company User-profile employees with Amnikon. No backups or data exports."""
from pathlib import Path
import json, os, subprocess, sys, tempfile
from xml.sax.saxutils import escape

if sys.argv[1:] != ['--apply'] or os.geteuid() != 0:
    raise SystemExit('Use: sudo python3 repair-employee-users.py --apply')
pid = subprocess.check_output(['systemctl','show','astra-backend.service','--property=MainPID','--value'], text=True).strip()
if not pid.isdigit() or pid == '0':
    raise SystemExit('STOP: backend service is not running.')
proc = Path('/proc') / pid
cwd = (proc/'cwd').resolve()
if cwd != Path('/var/www/astra/backend'):
    raise SystemExit('STOP: service directory differs from the confirmed application.')
runtime = dict(os.fsdecode(v).split('=',1) for v in (proc/'environ').read_bytes().split(b'\0') if b'=' in v)
settings = {}
environment = runtime.get('DOTNET_ENVIRONMENT') or runtime.get('ASPNETCORE_ENVIRONMENT') or 'Production'
for name in ['appsettings.json', f'appsettings.{environment}.json']:
    file = cwd/name
    if file.exists():
        settings.update(json.loads(file.read_text(encoding='utf-8-sig')).get('ConnectionStrings', {}))
connection = next((v for k,v in runtime.items() if k.lower() == 'connectionstrings__mysql'), settings.get('MySql'))
driver = cwd/'MySqlConnector.dll'
if not connection or not driver.is_file():
    raise SystemExit('STOP: service database settings or driver are unavailable.')

source = r'''
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
var settings=Environment.GetEnvironmentVariable("ASTRA_EMPLOYEE_CONNECTION") ?? throw new Exception("Database settings missing.");
if(new MySqlConnectionStringBuilder(settings).Database!="astra_ticketing")throw new Exception("Unexpected database. No changes made.");
string actor;
await using(var connection=new MySqlConnection(settings)){
 await connection.OpenAsync();
 await using var command=new MySqlCommand("SELECT u.id FROM users u WHERE LOWER(u.email)=@email AND u.active=1 AND (u.access_expires_at IS NULL OR u.access_expires_at>UTC_TIMESTAMP(6)) AND EXISTS(SELECT 1 FROM user_roles r WHERE r.user_id=u.id AND r.role_code IN ('ADMIN','MANAGER')) LIMIT 1",connection);
 command.Parameters.AddWithValue("@email","mohammed.sahil@amnikontechnologies.com");
 actor=Convert.ToString(await command.ExecuteScalarAsync()) ?? "";
 if(actor.Length==0)throw new Exception("The approved administrator account was not found. No changes made.");
}
var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:MySql"]=settings}).Build();
var store=new MySqlStore(configuration);
Console.WriteLine("Updating company User-profile employees and Manager company affiliations. Manager permissions and tickets are retained.");
var result=await store.RepairAmnikonEmployeeUsers(actor,CancellationToken.None);
Console.WriteLine("Employee association result: "+JsonSerializer.Serialize(result));
Console.WriteLine("Affected User-profile employees should sign in again. No backups or password changes.");
'''
maintenance=Path(__file__).resolve().parent
api=cwd/'Astra.Api.dll'
if not api.is_file(): raise SystemExit('STOP: running API assembly was not found.')
os.umask(0o077)
with tempfile.TemporaryDirectory(prefix='.employee-user-repair-code-',dir=maintenance) as tmp:
    folder=Path(tmp)
    project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App"/><Reference Include="Astra.Api"><HintPath>'+escape(str(api))+'</HintPath></Reference><Reference Include="MySqlConnector"><HintPath>'+escape(str(driver))+'</HintPath></Reference></ItemGroup></Project>'
    (folder/'Repair.csproj').write_text(project)
    (folder/'Program.cs').write_text(source)
    (folder/'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources></configuration>')
    subprocess.run(['dotnet','build',str(folder/'Repair.csproj'),'--configfile',str(folder/'NuGet.Config'),'-p:NuGetAudit=false','--nologo','-v:q'],cwd=folder,check=True)
    result=subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Repair.dll')],cwd=folder,env=dict(os.environ,ASTRA_EMPLOYEE_CONNECTION=connection,DOTNET_CLI_TELEMETRY_OPTOUT='1'))
    if result.returncode: raise SystemExit(result.returncode)
