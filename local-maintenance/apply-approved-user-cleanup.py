"""Apply the explicitly approved two-administrator cleanup. Never makes data backups."""
from pathlib import Path
import json, os, subprocess, sys, tempfile
from xml.sax.saxutils import escape

if sys.argv[1:] != ["--apply-reviewed-cleanup"]:
    raise SystemExit("STOP: --apply-reviewed-cleanup is required.")
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
source = (Path(__file__).resolve().parent/'UserCleanup.cs').read_text()
keep_ids = {
    '493631ad-fc93-493d-9e10-bb18eda281ed',
    '3d3ea7e7-2352-446a-a67b-150b895d2392',
}
# Validate legacy login-store files before making database changes. Never copy or back them up.
credential_paths = {cwd/'App_Data/test-accounts.json', maintenance.parent/'App_Data/test-accounts.json'}
if runtime.get('ASTRA_TEST_ACCOUNTS_FILE'):
    account_file = Path(runtime['ASTRA_TEST_ACCOUNTS_FILE'])
    credential_paths.add(account_file if account_file.is_absolute() else cwd/account_file)
credential_updates = []
for account_file in sorted(credential_paths):
    if not account_file.exists():
        continue
    if account_file.is_symlink() or not account_file.is_file():
        raise SystemExit('STOP: unexpected credential-store file type.')
    records = json.loads(account_file.read_text())
    if not isinstance(records, list) or any(not isinstance(row, dict) or 'UserId' not in row for row in records):
        raise SystemExit('STOP: unexpected credential-store format.')
    filtered = []
    for row in records:
        if row['UserId'] in keep_ids:
            row['Role'] = 'ADMIN'
            filtered.append(row)
    credential_updates.append((account_file, filtered))
with tempfile.TemporaryDirectory(prefix='.approved-user-cleanup-', dir=maintenance) as tmp:
    folder=Path(tmp)
    (folder/'Cleanup.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /><Reference Include="MySqlConnector"><HintPath>'+escape(str(driver))+'</HintPath></Reference></ItemGroup></Project>')
    (folder/'Program.cs').write_text(source)
    (folder/'NuGet.Config').write_text('<configuration><packageSources><clear /></packageSources></configuration>')
    env=dict(os.environ, ASTRA_INSPECTION_CONNECTION=connection, DOTNET_CLI_TELEMETRY_OPTOUT='1')
    subprocess.run(['dotnet','build',str(folder/'Cleanup.csproj'),'--configfile',str(folder/'NuGet.Config'),'-p:NuGetAudit=false','--nologo','-v:q'],cwd=folder,env=env,check=True)
    subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Cleanup.dll'),'--self-test'],cwd=folder,env=env,check=True)
    print('Service environment: '+environment,flush=True)
    print('APPLYING APPROVED CLEANUP: keep the two named administrators, remove the 24 reviewed accounts and blocking dependencies. No backups.',flush=True)
    result = subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Cleanup.dll'),'--apply-reviewed-cleanup'],cwd=folder,env=env)
    if result.returncode:
        raise SystemExit(result.returncode)
for account_file, filtered in credential_updates:
    try:
        with account_file.open('w') as stream:
            json.dump(filtered, stream, indent=2)
            stream.flush()
            os.fsync(stream.fileno())
    except Exception as error:
        print('DATABASE COMMITTED, but legacy credential-store cleanup needs attention: '+type(error).__name__,flush=True)
        raise SystemExit(3)
print('COMPLETE: exactly two administrator accounts remain. Legacy removed-user login entries purged. No backups or data-copy files created.',flush=True)