"""Rename the existing Amnikon MSP only. No backup, export or data copy."""
from pathlib import Path
import json, os, subprocess, sys, tempfile
from xml.sax.saxutils import escape

if sys.argv[1:] != ['--apply'] or os.geteuid() != 0:
    raise SystemExit('Use: sudo python3 rename-amnikon.py --apply')
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
using MySqlConnector;
const string id = "00000000-0000-4000-8000-000000000132";
const string oldName = "Amnikon Internal";
const string newName = "Amnikon";
var connectionString = Environment.GetEnvironmentVariable("ASTRA_RENAME_CONNECTION") ?? throw new Exception("Database settings missing.");
if (!string.Equals(new MySqlConnectionStringBuilder(connectionString).Database, "astra_ticketing", StringComparison.OrdinalIgnoreCase))
    throw new Exception("Unexpected database. No changes made.");
await using var connection = new MySqlConnection(connectionString);
await connection.OpenAsync();
await using var tx = await connection.BeginTransactionAsync();
await using var query = new MySqlCommand("SELECT name FROM clients WHERE id=@id FOR UPDATE", connection, tx);
query.Parameters.AddWithValue("@id", id);
var found = await query.ExecuteScalarAsync();
if (found is null || found is DBNull) throw new Exception("The existing Amnikon MSP was not found. No changes made.");
var name = Convert.ToString(found);
if (name == newName) { Console.WriteLine("Already named Amnikon. No changes needed."); return; }
if (name != oldName) throw new Exception("The protected MSP has a different name. No changes made.");
await using var update = new MySqlCommand("UPDATE clients SET name=@new WHERE id=@id AND name=@old", connection, tx);
update.Parameters.AddWithValue("@new", newName);
update.Parameters.AddWithValue("@old", oldName);
update.Parameters.AddWithValue("@id", id);
if (await update.ExecuteNonQueryAsync() != 1) throw new Exception("Expected exactly one MSP update. Transaction rolled back.");
if (Convert.ToString(await query.ExecuteScalarAsync()) != newName) throw new Exception("Rename verification failed. Transaction rolled back.");
await tx.CommitAsync();
Console.WriteLine("UPDATED: Amnikon Internal -> Amnikon. All screens using the MSP name will show Amnikon after refresh.");
Console.WriteLine("Only the existing MSP name changed. No backups or data copies created.");
'''
maintenance = Path(__file__).resolve().parent
os.umask(0o077)
with tempfile.TemporaryDirectory(prefix='.amnikon-name-code-', dir=maintenance) as tmp:
    folder = Path(tmp)
    (folder/'Rename.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /><Reference Include="MySqlConnector"><HintPath>'+escape(str(driver))+'</HintPath></Reference></ItemGroup></Project>')
    (folder/'Program.cs').write_text(source)
    (folder/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    subprocess.run(['dotnet','build',str(folder/'Rename.csproj'),'--configfile',str(folder/'NuGet.Config'),'-p:NuGetAudit=false','--nologo','-v:q'],cwd=folder,check=True)
    result = subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Rename.dll')],cwd=folder,
        env=dict(os.environ, ASTRA_RENAME_CONNECTION=connection, DOTNET_CLI_TELEMETRY_OPTOUT='1'))
    if result.returncode:
        raise SystemExit(result.returncode)
