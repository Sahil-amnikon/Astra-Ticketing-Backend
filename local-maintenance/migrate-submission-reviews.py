"""Create storage for technician submission reviews only. No backup or user-data copy."""
from pathlib import Path
import json, os, subprocess, sys, tempfile
from xml.sax.saxutils import escape

if sys.argv[1:] != ['--apply'] or os.geteuid() != 0:
    raise SystemExit('Use: sudo python3 migrate-submission-reviews.py --apply')
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
using System.Text.RegularExpressions;
using MySqlConnector;
var settings=Environment.GetEnvironmentVariable("ASTRA_SUBMISSION_CONNECTION") ?? throw new Exception("Database settings missing.");
if(new MySqlConnectionStringBuilder(settings).Database!="astra_ticketing")throw new Exception("Unexpected database.");
await using var connection=new MySqlConnection(settings);await connection.OpenAsync();
await using var column=new MySqlCommand("SELECT COLUMN_TYPE,CHARACTER_SET_NAME,COLLATION_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users' AND COLUMN_NAME='id'",connection);
await using var reader=await column.ExecuteReaderAsync();
if(!await reader.ReadAsync())throw new Exception("User ID definition missing.");
var type=reader.GetString(0);var charset=reader.GetString(1);var collation=reader.GetString(2);
if(!Regex.IsMatch(type,@"^(?:char|varchar)\(36\)$",RegexOptions.IgnoreCase)||!Regex.IsMatch(charset,@"^[a-zA-Z0-9_]+$")||!Regex.IsMatch(collation,@"^[a-zA-Z0-9_]+$"))throw new Exception("Unexpected user ID type. Nothing changed.");
await reader.DisposeAsync();
var schema=await File.ReadAllTextAsync(Environment.GetEnvironmentVariable("ASTRA_SUBMISSION_SCHEMA")!);
schema=schema.Replace("__USER_ID_DEFINITION__",type+" CHARACTER SET "+charset+" COLLATE "+collation);
foreach(var statement in schema.Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)){
 await using var command=new MySqlCommand(statement,connection);await command.ExecuteNonQueryAsync();
}
foreach(var table in new[]{"submission_reviews"}){
 await using var command=new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@table",connection);command.Parameters.AddWithValue("@table",table);
 if(Convert.ToInt32(await command.ExecuteScalarAsync())!=1)throw new Exception("Submission review storage verification failed.");
}
await using var keys=new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='submission_reviews' AND CONSTRAINT_TYPE='FOREIGN KEY'",connection);
if(Convert.ToInt32(await keys.ExecuteScalarAsync())!=2)throw new Exception("Submission review relationship verification failed.");
await using var shape=new MySqlCommand("SELECT kind,entity_id,submitted_by_user_id,status,revision,submitted_at,reviewed_by_user_id,reviewed_at,comment FROM submission_reviews LIMIT 0",connection);
await using var shapeReader=await shape.ExecuteReaderAsync();await shapeReader.DisposeAsync();
Console.WriteLine("Submission review storage ready. Existing accounts, roles and ticket data were not changed. No backups.");
'''

maintenance = Path(__file__).resolve().parent
os.umask(0o077)
with tempfile.TemporaryDirectory(prefix='.submission-migration-code-', dir=maintenance) as tmp:
    folder = Path(tmp)
    (folder/'Migrate.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /><Reference Include="MySqlConnector"><HintPath>'+escape(str(driver))+'</HintPath></Reference></ItemGroup></Project>')
    (folder/'Program.cs').write_text(source)
    (folder/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    subprocess.run(['dotnet','build',str(folder/'Migrate.csproj'),'--configfile',str(folder/'NuGet.Config'),'-p:NuGetAudit=false','--nologo','-v:q'],cwd=folder,check=True)
    result = subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Migrate.dll')],cwd=folder,
        env=dict(os.environ, ASTRA_SUBMISSION_CONNECTION=connection, ASTRA_SUBMISSION_SCHEMA=str(maintenance/'submission-reviews.sql'), DOTNET_CLI_TELEMETRY_OPTOUT='1'))
    if result.returncode:
        raise SystemExit(result.returncode)
