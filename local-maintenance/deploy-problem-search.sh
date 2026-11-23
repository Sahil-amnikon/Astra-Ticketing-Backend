#!/usr/bin/env bash
# Deploy permitted Team Ticket search and problem attachment actions. No database migration or backups.
set -euo pipefail
if [[ ${1:-} != --apply || $# != 1 ]]; then
  echo 'Use: sudo bash deploy-problem-search.sh --apply' >&2; exit 2
fi
if [[ $EUID != 0 ]]; then echo 'Run with sudo.' >&2; exit 2; fi
source_backend=/home/Sahil/ASTRA-Backend-latest
source_frontend=/home/Sahil/ASTRA-Frontend-latest
target_backend=/var/www/astra/backend
target_frontend=/var/www/astra/frontend
service=astra-backend.service
python3 "$source_backend/local-maintenance/verify-problem-search-deploy.py"
stage=''
service_stopped=false
cleanup() {
  local result=$?
  if [[ -n "$stage" && "$stage" == "$source_backend"/local-maintenance/.manager-publish.* && -d "$stage" && ! -L "$stage" ]]; then
    rm -rf -- "$stage"
  fi
  if [[ $result != 0 ]]; then
    echo 'Deployment did not complete. No backup or database copy was created.' >&2
    if [[ "$service_stopped" == true ]]; then echo 'The backend was stopped; check astra-backend.service before proceeding.' >&2; fi
  fi
}
trap cleanup EXIT
for file in "$source_backend/FeatureChangeRequestRouting.cs" "$source_backend/local-maintenance/change-request-routing-storage.cs" "$source_backend/local-maintenance/migrate-change-request-routing.py" "$source_backend/local-maintenance/verify-change-request-routing-startup.py" "$source_backend/FeatureChangeLifecycle.cs" "$source_backend/local-maintenance/verify-change-lifecycle-startup.py" "$source_backend/FeatureChangePlanTemplates.cs" "$source_backend/FeatureChangeReviewQueue.cs" "$source_backend/local-maintenance/verify-submission-startup.py" "$source_backend/FeatureRoleDeletion.cs" "$source_backend/local-maintenance/verify-role-startup.py" "$source_backend/Astra.Api.csproj" "$target_backend/Astra.Api.dll" "$target_frontend/index.html"; do
  [[ -f "$file" && ! -L "$file" ]] || { echo 'STOP: a reviewed application file is missing or linked.' >&2; exit 2; }
done
[[ $(realpath "$target_backend") == "$target_backend" && $(realpath "$target_frontend") == "$target_frontend" ]] || { echo 'STOP: deployed application directories differ.' >&2; exit 2; }
[[ $(systemctl show "$service" --property=WorkingDirectory --value) == "$target_backend" ]] || { echo 'STOP: backend service directory differs.' >&2; exit 2; }
[[ $(systemctl show "$service" --property=LoadState --value) == loaded ]] || { echo 'STOP: backend service unit is unavailable.' >&2; exit 2; }

# Verify production startup cannot run automatic schema changes; never print secrets.
python3 - <<'PY'
from pathlib import Path
import json, re, shlex, subprocess
def setting(name):
    return subprocess.check_output(['systemctl','show','astra-backend.service','--property='+name,'--value'],text=True).strip()
command=setting('ExecStart')
if '/var/www/astra/backend/Astra.Api.dll' not in command or '--environment' in command.lower():
    raise SystemExit('STOP: backend service command differs from the reviewed target.')
# Read only relevant unit settings when a crashed process has no /proc entry.
needed={'DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT','ASTRA_SKIP_FEATURE_SCHEMA_STARTUP'}
env={}
for entry in shlex.split(setting('Environment')):
    key,sep,value=entry.partition('=')
    if sep and key in needed: env[key]=value
raw_files=setting('EnvironmentFiles')
files=re.findall(r'(\S+) \(ignore_errors=(yes|no)\)',raw_files)
if raw_files and not files: raise SystemExit('STOP: service environment-file syntax requires review.')
for name,optional in files:
    file=Path(name)
    if not file.exists():
        if optional=='yes': continue
        raise SystemExit('STOP: a required service environment file is missing.')
    for line in file.read_text().splitlines():
        match=re.match(r'^\s*(DOTNET_ENVIRONMENT|ASPNETCORE_ENVIRONMENT|ASTRA_SKIP_FEATURE_SCHEMA_STARTUP)\s*=\s*(.*)$',line)
        if match:
            tokens=shlex.split(match.group(2),comments=True)
            if len(tokens)!=1: raise SystemExit('STOP: environment setting requires review.')
            env[match.group(1)]=tokens[0]
values={k.lower():v for k,v in env.items()}
flag=values.get('astra_skip_feature_schema_startup')
environment=env.get('DOTNET_ENVIRONMENT') or env.get('ASPNETCORE_ENVIRONMENT') or 'Production'
startup=Path('/home/Sahil/ASTRA-Backend-latest/Database/FeatureSchemaStartup.cs').read_text()
production_guard='environment.IsProduction() ||' in startup and 'IHostEnvironment environment' in startup
endpoint=Path('/home/Sahil/ASTRA-Backend-latest/FeatureRoleDeletion.cs').read_text()
if '[Microsoft.AspNetCore.Mvc.FromBody] DeleteRoleInput body' not in endpoint:
    raise SystemExit('STOP: explicit DELETE-body repair is missing.')
if flag is None:
    runtime=Path('/var/www/astra/backend')
    environment=env.get('DOTNET_ENVIRONMENT') or env.get('ASPNETCORE_ENVIRONMENT') or 'Production'
    for name in ['appsettings.json',f'appsettings.{environment}.json']:
        file=runtime/name
        if file.exists():
            try:
                config=json.loads(file.read_text(encoding='utf-8-sig'))
            except Exception:
                raise SystemExit('STOP: cannot verify the schema-startup setting; service remains unchanged.')
            for key,value in config.items():
                if key.lower()=='astra_skip_feature_schema_startup': flag=value
if str(flag).lower()!='true' and not (environment.lower()=='production' and production_guard):
    raise SystemExit('STOP: this environment is not covered by the reviewed production schema guard. Service settings remain unchanged.')
print('Verified: correct service target and startup repair; recovery can proceed without a running process.')
PY

runuser -u Sahil -- npm --prefix "$source_frontend" run build
stage=$(mktemp -d "$source_backend/local-maintenance/.manager-publish.XXXXXX")
chown Sahil:"$(id -gn Sahil)" "$stage"
runuser -u Sahil -- dotnet publish "$source_backend/Astra.Api.csproj" -c Release -o "$stage" -p:EnableDefaultContentItems=false --nologo -v:minimal
runuser -u Sahil -- python3 "$source_backend/local-maintenance/verify-role-startup.py" "$stage"
runuser -u Sahil -- python3 "$source_backend/local-maintenance/verify-submission-startup.py" "$stage"
runuser -u Sahil -- python3 "$source_backend/local-maintenance/verify-change-lifecycle-startup.py" "$stage"
runuser -u Sahil -- python3 "$source_backend/local-maintenance/verify-change-request-routing-startup.py" "$stage"
runuser -u Sahil -- python3 "$source_backend/local-maintenance/verify-problem-ticket-actions-startup.py" "$stage"

# Preflight the complete code-file list before stopping the existing service.
python3 - "$stage" <<'PY'
from pathlib import Path
import sys
stage=Path(sys.argv[1])
if not (stage/'Astra.Api.dll').is_file(): raise SystemExit('STOP: published backend assembly missing.')
for file in stage.rglob('*'):
    if file.is_file() and (file.name.lower().startswith('appsettings') or 'app_data' in [p.lower() for p in file.parts]):
        raise SystemExit('STOP: configuration or user-data content unexpectedly appeared in publish output. Nothing deployed.')
for origin,target in [(stage,Path('/var/www/astra/backend')),(Path('/home/Sahil/ASTRA-Frontend-latest/dist'),Path('/var/www/astra/frontend'))]:
    for file in origin.rglob('*'):
        if not file.is_file(): continue
        if file.is_symlink(): raise SystemExit('STOP: unexpected linked build file.')
        if origin==stage and not (file.suffix in ('.dll','.so','.dylib') or file.name in ('Astra.Api.deps.json','Astra.Api.runtimeconfig.json')): continue
        dest=target/file.relative_to(origin)
        if not dest.resolve().is_relative_to(target): raise SystemExit('STOP: deployment file leaves its approved directory.')
        if dest.is_symlink(): raise SystemExit('STOP: linked deployment target.')
if (Path('/var/www/astra/backend/App_Data/knowledge').resolve() != Path('/var/www/astra/backend/App_Data/knowledge')): raise SystemExit('STOP: knowledge storage uses an unexpected link.')
print('Build preflight passed. Backend configuration, App_Data and migration SQL will be retained.')
PY



install -d -o www-data -g www-data -m 0750 "$target_backend/App_Data/knowledge"

systemctl stop "$service"
service_stopped=true
python3 - "$stage" <<'PY'
from pathlib import Path
import os, pwd, shutil, sys
source=Path(sys.argv[1]);target=Path('/var/www/astra/backend')
account=pwd.getpwnam('www-data')
for file in source.rglob('*'):
    if not file.is_file(): continue
    if not (file.suffix in ('.dll','.so','.dylib') or file.name in ('Astra.Api.deps.json','Astra.Api.runtimeconfig.json')): continue
    dest=target/file.relative_to(source)
    dest.parent.mkdir(parents=True,exist_ok=True)
    os.chown(dest.parent,account.pw_uid,account.pw_gid)
    os.chmod(dest.parent,0o755)
    shutil.copyfile(file,dest)
    os.chown(dest,account.pw_uid,account.pw_gid);os.chmod(dest,0o644)
PY
systemctl reset-failed "$service"
if ! systemctl start "$service"; then
  journalctl -u "$service" --since '2 minutes ago' --no-pager -n 60
  exit 1
fi
service_stopped=false
healthy=false
for attempt in $(seq 1 40); do
  if curl --max-time 3 -fsS http://127.0.0.1:5200/health >/dev/null 2>&1; then healthy=true;break;fi
  sleep 1
done
if [[ "$healthy" != true ]]; then
  echo 'Backend health check failed. Frontend was not deployed. Startup log follows:' >&2
  journalctl -u "$service" --since '2 minutes ago' --no-pager -n 60
  exit 1
fi
python3 - <<'PY'
from pathlib import Path
import os,pwd,shutil
source=Path('/home/Sahil/ASTRA-Frontend-latest/dist');target=Path('/var/www/astra/frontend')
account=pwd.getpwnam('www-data')
# Put index.html last, after every referenced asset exists. No existing data is backed up or deleted.
files=sorted((p for p in source.rglob('*') if p.is_file()),key=lambda p:p.relative_to(source).as_posix()=='index.html')
for file in files:
    dest=target/file.relative_to(source)
    dest.parent.mkdir(parents=True,exist_ok=True)
    os.chown(dest.parent,account.pw_uid,account.pw_gid);os.chmod(dest.parent,0o755)
    shutil.copyfile(file,dest)
    os.chown(dest,account.pw_uid,account.pw_gid);os.chmod(dest,0o644)
PY
echo 'DEPLOYED: search problems by problem details or linked tickets, open the full investigation, and attach or update from the top right.'
echo 'Backend health: OK. No database migration, backups or data copies. Existing articles retained.'
