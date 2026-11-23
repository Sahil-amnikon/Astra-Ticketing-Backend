#!/usr/bin/env bash
# Publish frontend code only; no database operations, backup or service restart.
set -euo pipefail
[[ ${1:-} == --apply && $# == 1 && $EUID == 0 ]] || { echo 'Use sudo bash deploy-access-dropdowns.sh --apply'; exit 2; }
python3 - <<'PY'
from pathlib import Path
import os, pwd, re, shutil
source=Path('/home/Sahil/ASTRA-Frontend-latest/dist')
target=Path('/var/www/astra/frontend')
if source.resolve()!=source or target.resolve()!=target:
    raise SystemExit('STOP: frontend directories differ from the reviewed locations.')
if not (target/'index.html').is_file() or not (source/'index.html').is_file():
    raise SystemExit('STOP: source or live frontend index missing.')
files=list(source.rglob('*'))
if any(p.is_symlink() for p in files):
    raise SystemExit('STOP: linked build output.')
files=[p for p in files if p.is_file()]
for file in files:
    dest=target/file.relative_to(source)
    if not dest.resolve().is_relative_to(target) or dest.is_symlink():
        raise SystemExit('STOP: deployment destination is linked or outside the frontend directory.')
    if file.relative_to(source).parts[0] in ('App_Data','appsettings.json'):
        raise SystemExit('STOP: unexpected application data in frontend output.')
index=(source/'index.html').read_text()
for asset in re.findall(r'(?:src|href)="(/assets/[^"?#]+)',index):
    if not (source/asset.lstrip('/')).is_file():
        raise SystemExit('STOP: an index asset is missing. Nothing deployed.')
account=pwd.getpwnam('www-data')
# New assets first; replace the entry document only after all referenced files exist.
for file in sorted(files,key=lambda p:p.relative_to(source).as_posix()=='index.html'):
    dest=target/file.relative_to(source)
    dest.parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(file,dest)
    os.chown(dest,account.pw_uid,account.pw_gid)
    os.chmod(dest,0o644)
print('DEPLOYED: ASTRA frontend updated. Refresh ASTRA with Ctrl+F5.')
print('Frontend update only. No database changes, backups or service restarts.')
PY
