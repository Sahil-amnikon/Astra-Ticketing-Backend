#!/usr/bin/env bash
# Deploy custom-role code and add its two storage tables. No backups or configuration copies.
set -euo pipefail
if [[ ${1:-} != --apply || $# != 1 ]]; then
  echo 'Use: sudo bash deploy-custom-roles.sh --apply' >&2; exit 2
fi
if [[ $EUID != 0 ]]; then echo 'Run with sudo.' >&2; exit 2; fi
source_backend=/home/Sahil/ASTRA-Backend-latest
source_frontend=/home/Sahil/ASTRA-Frontend-latest
target_backend=/var/www/astra/backend
target_frontend=/var/www/astra/frontend
service=astra-backend.service
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
for file in "$source_backend/FeatureCustomRoles.cs" "$source_backend/local-maintenance/migrate-custom-roles.py" "$source_backend/local-maintenance/custom-roles.sql" "$source_backend/RoleAccess.cs" "$source_backend/Astra.Api.csproj" "$source_frontend/src/pages/admin/user-access-options.ts" "$target_backend/Astra.Api.dll" "$target_frontend/index.html"; do
  [[ -f "$file" && ! -L "$file" ]] || { echo 'STOP: a reviewed application file is missing or linked.' >&2; exit 2; }
done
[[ $(realpath "$target_backend") == "$target_backend" && $(realpath "$target_frontend") == "$target_frontend" ]] || { echo 'STOP: deployed application directories differ.' >&2; exit 2; }
[[ $(systemctl show "$service" --property=WorkingDirectory --value) == "$target_backend" ]] || { echo 'STOP: backend service directory differs.' >&2; exit 2; }
systemctl is-active --quiet "$service" || { echo 'STOP: backend service is not currently active. No deployment attempted.' >&2; exit 2; }

# Verify production startup cannot run automatic schema changes; never print secrets.
python3 - <<'PY'
from pathlib import Path
import json, os, subprocess
pid = subprocess.check_output(['systemctl','show','astra-backend.service','--property=MainPID','--value'],text=True).strip()
proc=Path('/proc')/pid
args=[os.fsdecode(v) for v in (proc/'cmdline').read_bytes().split(b'\0') if v]
if '/var/www/astra/backend/Astra.Api.dll' not in args:
    raise SystemExit('STOP: running backend assembly differs.')
env=dict(os.fsdecode(v).split('=',1) for v in (proc/'environ').read_bytes().split(b'\0') if b'=' in v)
values={k.lower():v for k,v in env.items()}
flag=values.get('astra_skip_feature_schema_startup')
environment=env.get('DOTNET_ENVIRONMENT') or env.get('ASPNETCORE_ENVIRONMENT') or 'Production'
startup=Path('/home/Sahil/ASTRA-Backend-latest/Database/FeatureSchemaStartup.cs').read_text()
production_guard='environment.IsProduction() ||' in startup and 'IHostEnvironment environment' in startup
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
print('Verified: correct service target; the new production build skips automatic schema changes.')
PY

# The SSO-only Ubuntu page has no password setter. Remove only this obsolete logout call.
runuser -u Sahil -- python3 - <<'PY'
from pathlib import Path
import re
page=Path('/home/Sahil/ASTRA-Frontend-latest/src/pages/service-desk.tsx')
text=page.read_text()
pattern=r"(await signOutSession\(csrfToken\);\s*)setPassword\(''\);\s*(setState\(\{ kind: 'signed-out' \}\);)"
fixed,count=re.subn(pattern,r'\1\2',text)
if count>1: raise SystemExit('STOP: unexpected logout source.')
if count: page.write_text(fixed)
elif 'await signOutSession(csrfToken);' not in text:
    raise SystemExit('STOP: reviewed logout function was not found.')
print('Logout compatibility check passed.')
PY
runuser -u Sahil -- npm --prefix "$source_frontend" run build
stage=$(mktemp -d "$source_backend/local-maintenance/.manager-publish.XXXXXX")
chown Sahil:"$(id -gn Sahil)" "$stage"
runuser -u Sahil -- dotnet publish "$source_backend/Astra.Api.csproj" -c Release -o "$stage" -p:EnableDefaultContentItems=false --nologo -v:minimal

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
print('Build preflight passed. Backend configuration, App_Data and migration SQL will be retained.')
PY

python3 "$source_backend/local-maintenance/migrate-custom-roles.py" --apply

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
systemctl start "$service"
service_stopped=false
healthy=false
for attempt in $(seq 1 20); do
  if curl --max-time 3 -fsS http://127.0.0.1:5200/health >/dev/null; then healthy=true;break;fi
  sleep 1
done
if [[ "$healthy" != true ]]; then echo 'Backend health check failed. Inspect astra-backend.service; frontend was not deployed.' >&2;exit 1;fi
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
echo 'DEPLOYED: Add role, custom role permissions and employee assignment.'
echo 'Backend health: OK. SSO settings and App_Data preserved. Custom-role storage added; no existing accounts or tickets removed. No backups.'
