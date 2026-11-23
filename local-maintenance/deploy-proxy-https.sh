#!/usr/bin/env bash
# Backend-only HTTPS correction. No database changes, data copies or backups.
set -euo pipefail
[[ ${1:-} == --apply && $# == 1 && $EUID == 0 ]] || { echo 'Use sudo bash deploy-proxy-https.sh --apply'; exit 2; }
source_backend=/home/Sahil/ASTRA-Backend-latest
target_backend=/var/www/astra/backend
service=astra-backend.service
stage=''
stopped=false
cleanup() {
  local result=$?
  if [[ -n "$stage" && "$stage" == "$source_backend"/local-maintenance/.https-publish.* && -d "$stage" && ! -L "$stage" ]]; then rm -rf -- "$stage"; fi
  if [[ $result != 0 ]]; then
    echo 'HTTPS deployment did not complete. No database operations or backups were performed.' >&2
    if [[ "$stopped" == true ]]; then echo 'The service was stopped; check astra-backend.service.' >&2; fi
  fi
}
trap cleanup EXIT
[[ $(realpath "$target_backend") == "$target_backend" ]] || { echo 'STOP: backend location differs.'; exit 2; }
[[ $(systemctl show "$service" --property=WorkingDirectory --value) == "$target_backend" ]] || { echo 'STOP: service directory differs.'; exit 2; }
systemctl is-active --quiet "$service" || { echo 'STOP: backend service is not active.'; exit 2; }

python3 - <<'PY'
from pathlib import Path
import os, subprocess
pid=subprocess.check_output(['systemctl','show','astra-backend.service','--property=MainPID','--value'],text=True).strip()
proc=Path('/proc')/pid
args=[os.fsdecode(v) for v in (proc/'cmdline').read_bytes().split(b'\0') if v]
if '/var/www/astra/backend/Astra.Api.dll' not in args: raise SystemExit('STOP: running backend assembly differs.')
if any('ASTRA_PUBLIC_ORIGIN' in arg for arg in args): raise SystemExit('STOP: a command-line public-origin override needs review.')
env=dict(os.fsdecode(v).split('=',1) for v in (proc/'environ').read_bytes().split(b'\0') if b'=' in v)
environment=env.get('DOTNET_ENVIRONMENT') or env.get('ASPNETCORE_ENVIRONMENT') or 'Production'
if environment.lower()!='production': raise SystemExit('STOP: this installer targets the confirmed Production service only.')
root=Path('/home/Sahil/ASTRA-Backend-latest')
startup=(root/'Database/FeatureSchemaStartup.cs').read_text()
if 'environment.IsProduction() ||' not in startup: raise SystemExit('STOP: production schema guard missing. No deployment performed.')
if 'TrustedProxyHttps.CallbackUrl(settings.ApplicationBaseUrl)' not in (root/'Program.cs').read_text(): raise SystemExit('STOP: HTTPS source correction missing.')
folder=Path('/etc/systemd/system/astra-backend.service.d')
file=folder/'99-astra-public-origin.conf'
expected='[Service]\nEnvironment="ASTRA_PUBLIC_ORIGIN=https://astra.amnikontechnologies.com"\n'
if folder.resolve()!=folder or file.is_symlink(): raise SystemExit('STOP: service override path is linked.')
if file.exists() and file.read_text()!=expected: raise SystemExit('STOP: an existing public-origin override differs. Nothing overwritten.')
print('Verified: correct Production service, HTTPS source and no automatic production schema changes.')
PY

stage=$(mktemp -d "$source_backend/local-maintenance/.https-publish.XXXXXX")
chown Sahil:"$(id -gn Sahil)" "$stage"
runuser -u Sahil -- dotnet publish "$source_backend/Astra.Api.csproj" -c Release -o "$stage" -p:EnableDefaultContentItems=false --nologo -v:minimal

python3 - "$stage" <<'PY'
from pathlib import Path
import sys
source=Path(sys.argv[1]);target=Path('/var/www/astra/backend')
if not (source/'Astra.Api.dll').is_file(): raise SystemExit('STOP: build assembly missing.')
for file in source.rglob('*'):
    if file.is_symlink(): raise SystemExit('STOP: linked publish output.')
    if not file.is_file(): continue
    if file.name.lower().startswith('appsettings') or 'app_data' in [p.lower() for p in file.parts]:
        raise SystemExit('STOP: configuration or user data unexpectedly appeared in the build. Nothing deployed.')
    if not (file.suffix in ('.dll','.so','.dylib') or file.name in ('Astra.Api.deps.json','Astra.Api.runtimeconfig.json')): continue
    dest=target/file.relative_to(source)
    if not dest.resolve().is_relative_to(target) or dest.is_symlink(): raise SystemExit('STOP: deployment destination is linked or outside the backend.')
print('Build passed. SSO credentials and App_Data will be retained.')
PY

python3 - <<'PY'
from pathlib import Path
import os
folder=Path('/etc/systemd/system/astra-backend.service.d')
file=folder/'99-astra-public-origin.conf'
expected='[Service]\nEnvironment="ASTRA_PUBLIC_ORIGIN=https://astra.amnikontechnologies.com"\n'
if folder.resolve()!=folder or file.is_symlink(): raise SystemExit('STOP: service override path changed.')
folder.mkdir(mode=0o755,parents=True,exist_ok=True)
if file.exists():
    if file.read_text()!=expected: raise SystemExit('STOP: public-origin override changed during build.')
else:
    with file.open('x') as stream: stream.write(expected)
    os.chmod(file,0o644)
print('Configured only the public HTTPS address. SSO credentials remain unchanged.')
PY
systemctl daemon-reload
systemctl stop "$service"
stopped=true
python3 - "$stage" <<'PY'
from pathlib import Path
import os, pwd, shutil, sys
source=Path(sys.argv[1]);target=Path('/var/www/astra/backend');account=pwd.getpwnam('www-data')
for file in source.rglob('*'):
    if not file.is_file(): continue
    if not (file.suffix in ('.dll','.so','.dylib') or file.name in ('Astra.Api.deps.json','Astra.Api.runtimeconfig.json')): continue
    dest=target/file.relative_to(source)
    dest.parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(file,dest)
    os.chown(dest,account.pw_uid,account.pw_gid);os.chmod(dest,0o644)
PY
systemctl start "$service"
stopped=false
healthy=false
for attempt in $(seq 1 20); do
  if curl --max-time 3 -fsS http://127.0.0.1:5200/health >/dev/null; then healthy=true;break;fi
  sleep 1
done
[[ "$healthy" == true ]] || { echo 'Backend health check failed. Check astra-backend.service.'; exit 1; }
python3 - <<'PY'
from pathlib import Path
import os, subprocess, urllib.parse as u
pid=subprocess.check_output(['systemctl','show','astra-backend.service','--property=MainPID','--value'],text=True).strip()
env=dict(os.fsdecode(v).split('=',1) for v in (Path('/proc')/pid/'environ').read_bytes().split(b'\0') if b'=' in v)
origin='https://astra.amnikontechnologies.com'
if env.get('ASTRA_PUBLIC_ORIGIN')!=origin: raise SystemExit('STOP: another service setting overrode the public origin. Verification failed.')
headers=subprocess.check_output(['curl','--max-time','20','-sS','-D','-','-o','/dev/null','-H','Host: astra.amnikontechnologies.com','-H','X-Forwarded-Proto: https','http://127.0.0.1:5200/api/auth/login'],text=True)
location=next((line.split(':',1)[1].strip() for line in headers.splitlines() if line.lower().startswith('location:')),'')
callback=u.parse_qs(u.urlsplit(location).query).get('redirect_uri',[''])[0]
if callback!=origin+'/api/auth/callback':
    parsed=u.urlsplit(callback)
    print('Callback received:',u.urlunsplit((parsed.scheme,parsed.netloc,parsed.path,'','')) or 'No Microsoft redirect received')
    raise SystemExit('HTTPS callback verification failed. No account permissions were changed.')
# Never print Microsoft state, nonce, authorization URLs or cookies.
print('VERIFIED callback:',callback)
print('DEPLOYED: HTTPS sign-in correction. Start a fresh Microsoft sign-in from '+origin)
print('No account, password, database or certificate-verification changes were made.')
PY
