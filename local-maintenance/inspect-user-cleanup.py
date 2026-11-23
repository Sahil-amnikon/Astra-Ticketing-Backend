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
 if(target.Server!="172.31.4.5" || target.Database!="astra_ticketing") throw new InvalidOperationException("Unexpected target");
 await using var c=new MySqlConnection(raw);await c.OpenAsync();
 await using var tx=await c.BeginTransactionAsync();
 async Task<List<Dictionary<string,object?>>> Read(string sql) {
  await using var cmd=new MySqlCommand(sql,c,tx);await using var r=await cmd.ExecuteReaderAsync();var rows=new List<Dictionary<string,object?>>();
  while(await r.ReadAsync()){var row=new Dictionary<string,object?>();for(int i=0;i<r.FieldCount;i++)row[r.GetName(i)]=r.IsDBNull(i)?null:r.GetValue(i);rows.Add(row);}return rows;
 }
 const string keep="'mohammed.sahil@amnikontechnologies.com','hamza.rajmohammed@amnikontechnologies.com'";
 var users=await Read("SELECT id,email,display_name,active,(SELECT GROUP_CONCAT(role_code ORDER BY role_code) FROM user_roles r WHERE r.user_id=u.id) AS roles FROM users u ORDER BY email");
 foreach(var u in users) {
  var email=Convert.ToString(u["email"])??"";
  var retained=email.Equals("mohammed.sahil@amnikontechnologies.com",StringComparison.OrdinalIgnoreCase)||email.Equals("hamza.rajmohammed@amnikontechnologies.com",StringComparison.OrdinalIgnoreCase);
  Console.WriteLine((retained?"KEEP: ":"REMOVE CANDIDATE: ")+JsonSerializer.Serialize(u));
 }
 var kept=users.Where(u=>new[]{"mohammed.sahil@amnikontechnologies.com","hamza.rajmohammed@amnikontechnologies.com"}.Contains(Convert.ToString(u["email"]),StringComparer.OrdinalIgnoreCase)).ToList();
 Console.WriteLine("Total users: "+users.Count+"; keep matches: "+kept.Count+"; removal candidates: "+(users.Count-kept.Count));
 var fks=await Read("SELECT k.TABLE_NAME,k.COLUMN_NAME,r.DELETE_RULE FROM information_schema.KEY_COLUMN_USAGE k JOIN information_schema.REFERENTIAL_CONSTRAINTS r ON r.CONSTRAINT_SCHEMA=k.CONSTRAINT_SCHEMA AND r.CONSTRAINT_NAME=k.CONSTRAINT_NAME AND r.TABLE_NAME=k.TABLE_NAME WHERE k.CONSTRAINT_SCHEMA=DATABASE() AND k.REFERENCED_TABLE_SCHEMA=DATABASE() AND k.REFERENCED_TABLE_NAME='users' AND k.REFERENCED_COLUMN_NAME='id' ORDER BY k.TABLE_NAME,k.COLUMN_NAME");
 string Quote(object? value)=>"`"+(Convert.ToString(value)??"").Replace("`","``")+"`";
 foreach(var fk in fks) {
  var count=await Read("SELECT COUNT(*) AS affected_rows FROM "+Quote(fk["TABLE_NAME"])+" x JOIN users u ON x."+Quote(fk["COLUMN_NAME"])+"=u.id WHERE LOWER(TRIM(u.email)) NOT IN ("+keep+") OR u.email IS NULL");
  Console.WriteLine("LINKED DATA: "+JsonSerializer.Serialize(new {table=fk["TABLE_NAME"],column=fk["COLUMN_NAME"],deleteRule=fk["DELETE_RULE"],count=count[0]["affected_rows"]}));
 }
 await tx.RollbackAsync();
 Console.WriteLine(kept.Count==2?"READ-ONLY REVIEW COMPLETE. Both keep accounts found. No users or data deleted.":"STOP: both keep accounts must exist uniquely before any cleanup. No changes made.");
 return kept.Count==2?0:2;
} catch(Exception e) {
 Console.Error.WriteLine("Read-only review stopped: "+e.GetType().Name);
 for(Exception? cause=e.InnerException;cause!=null;cause=cause.InnerException) {
  Console.Error.WriteLine("Cause type: "+cause.GetType().Name);
  if(cause is System.IO.FileNotFoundException missing)Console.Error.WriteLine("Missing assembly: "+Path.GetFileName(missing.FileName));
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
    print('READ-ONLY user cleanup impact review. No deletion enabled.',flush=True)
    raise SystemExit(subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Inspect.dll')],cwd=folder,env=env).returncode)
