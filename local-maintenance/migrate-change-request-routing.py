"""Create full change-form template storage only. No backups or data exports."""
from pathlib import Path
import json, os, subprocess, sys, tempfile
from xml.sax.saxutils import escape

if sys.argv[1:] != ['--apply'] or os.geteuid() != 0:
    raise SystemExit('Use: sudo python3 migrate-change-request-routing.py --apply')
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

source = (Path(__file__).resolve().parent/'change-request-routing-storage.cs').read_text()
maintenance=Path(__file__).resolve().parent
api=cwd/'Astra.Api.dll'
if not api.is_file(): raise SystemExit('STOP: running API assembly was not found.')
os.umask(0o077)
with tempfile.TemporaryDirectory(prefix='.change-request-routing-code-',dir=maintenance) as tmp:
    folder=Path(tmp)
    project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App"/><Reference Include="Astra.Api"><HintPath>'+escape(str(api))+'</HintPath></Reference><Reference Include="MySqlConnector"><HintPath>'+escape(str(driver))+'</HintPath></Reference></ItemGroup></Project>'
    (folder/'Repair.csproj').write_text(project)
    (folder/'Program.cs').write_text(source)
    (folder/'NuGet.Config').write_text('<configuration><packageSources><clear/></packageSources></configuration>')
    subprocess.run(['dotnet','build',str(folder/'Repair.csproj'),'--configfile',str(folder/'NuGet.Config'),'-p:NuGetAudit=false','--nologo','-v:q'],cwd=folder,check=True)
    result=subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Repair.dll')],cwd=folder,env=dict(os.environ,ASTRA_CHANGE_ROUTING_CONNECTION=connection,DOTNET_CLI_TELEMETRY_OPTOUT='1'))
    if result.returncode: raise SystemExit(result.returncode)
