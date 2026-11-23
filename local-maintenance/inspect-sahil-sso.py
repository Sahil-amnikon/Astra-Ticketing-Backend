"""Read-only account inspection using the running ASTRA service's private settings."""
from pathlib import Path
import json, os, subprocess, tempfile
from xml.sax.saxutils import escape

if os.geteuid() != 0:
    raise SystemExit('Run with sudo so the www-data service settings can be read privately.')
pid = subprocess.check_output(['systemctl', 'show', 'astra-backend.service', '--property=MainPID', '--value'], text=True).strip()
if not pid.isdigit() or pid == '0':
    raise SystemExit('STOP: ASTRA service is not running.')
proc = Path('/proc') / pid
runtime = dict(os.fsdecode(item).split('=', 1) for item in (proc/'environ').read_bytes().split(b'\0') if b'=' in item)
cwd = (proc/'cwd').resolve()
argv = [os.fsdecode(v) for v in (proc/'cmdline').read_bytes().split(b'\0') if v]
dlls = [Path(v) if Path(v).is_absolute() else cwd/v for v in argv if v.endswith('.dll')]
if len(dlls) != 1 or not dlls[0].is_file():
    raise SystemExit('STOP: cannot identify the running application assembly.')
settings = {}
environment = runtime.get('DOTNET_ENVIRONMENT') or runtime.get('ASPNETCORE_ENVIRONMENT') or 'Production'
for filename in ['appsettings.json', f'appsettings.{environment}.json']:
    file = cwd/filename
    if file.exists():
        try:
            data = json.loads(file.read_text(encoding='utf-8-sig'))
        except Exception:
            raise SystemExit('STOP: settings require a configuration parser; no changes made.')
        settings.update(data.get('ConnectionStrings', {}))
connection = next((v for k,v in runtime.items() if k.lower() == 'connectionstrings__mysql'), settings.get('MySql'))
if not connection:
    raise SystemExit('STOP: database settings were not found; no changes made.')
driver = dlls[0].parent/'MySqlConnector.dll'
if not driver.is_file():
    raise SystemExit('STOP: service MySQL driver was not found.')
home = Path('/home/Sahil')
maintenance = home/'ASTRA-Backend-latest/local-maintenance'
if not maintenance.is_dir():
    raise SystemExit('STOP: maintenance directory was not found.')
os.umask(0o077)
source = r'''
using MySqlConnector;
using System.Text.Json;
try {
 var raw=Environment.GetEnvironmentVariable("ASTRA_INSPECTION_CONNECTION")!;
 var target=new MySqlConnectionStringBuilder(raw);
 if(target.Server!="172.31.4.5" || target.Database!="astra_ticketing") throw new InvalidOperationException("Unexpected database target");
 await using var c=new MySqlConnection(raw); await c.OpenAsync();
 await using var tx=await c.BeginTransactionAsync();
 await using var cmd=new MySqlCommand(@"SELECT u.id,u.email,u.display_name,u.active,u.access_expires_at,u.entra_tenant_id,u.entra_object_id,
 (SELECT GROUP_CONCAT(role_code ORDER BY role_code) FROM user_roles WHERE user_id=u.id) AS roles,
 (SELECT GROUP_CONCAT(c.name ORDER BY c.name) FROM client_users cu JOIN clients c ON c.id=cu.client_id WHERE cu.user_id=u.id) AS msps
 FROM users u WHERE LOWER(u.email) IN ('mohammed.sahil@amnikontechnologies.com','sahil@amnikon.com','sahil@geometry.com') OR u.entra_object_id=@oid ORDER BY u.email",c,tx);
 cmd.Parameters.AddWithValue("@oid","a5895f3c-5dc8-4e70-89e3-6d938718f23f");
 await using(var r=await cmd.ExecuteReaderAsync()) {
  int count=0; while(await r.ReadAsync()) {count++;var row=new Dictionary<string,object?>(); for(int i=0;i<r.FieldCount;i++)row[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i);Console.WriteLine(JsonSerializer.Serialize(row));}
  Console.WriteLine("Matching account records: "+count);
 }
 await tx.RollbackAsync();
 Console.WriteLine("READ-ONLY COMPLETE. No accounts, roles, passwords or tickets changed.");
 return 0;
} catch(Exception e) {
 Console.Error.WriteLine("Inspection stopped ("+e.GetType().Name+"). No database changes made.");
 for(Exception? cause=e;cause!=null;cause=cause.InnerException) {
  Console.Error.WriteLine("Error type: "+cause.GetType().FullName);
  if(cause is System.IO.FileNotFoundException missing && missing.FileName!=null)
   Console.Error.WriteLine("Missing assembly: "+Path.GetFileName(missing.FileName));
 }
 return 1;
}
'''
with tempfile.TemporaryDirectory(prefix='.sso-inspect-', dir=maintenance) as tmp:
    folder=Path(tmp)
    (folder/'Inspect.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /><Reference Include="MySqlConnector"><HintPath>'+escape(str(driver))+'</HintPath></Reference></ItemGroup></Project>')
    (folder/'Program.cs').write_text(source)
    (folder/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    env=dict(os.environ, ASTRA_INSPECTION_CONNECTION=connection, DOTNET_CLI_TELEMETRY_OPTOUT='1')
    subprocess.run(['dotnet','build',str(folder/'Inspect.csproj'),'--configfile',str(folder/'NuGet.Config'),'-p:NuGetAudit=false','--nologo','-v:q'],cwd=folder,env=env,check=True)
    print('Service environment: '+environment, flush=True)
    print('Confirmed Microsoft tenant: 31f2256c-3ca0-41bb-a69d-0f4a02a58d87',flush=True)
    raise SystemExit(subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Inspect.dll')],cwd=folder,env=env).returncode)
