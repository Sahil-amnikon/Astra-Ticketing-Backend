#!/usr/bin/env bash
set -euo pipefail
if [[ ${1:-} != --apply || $# != 1 || $EUID != 0 ]]; then
  echo 'Use: sudo bash deploy-change-dropdown-rows.sh --apply' >&2; exit 2
fi
source_frontend=/home/Sahil/ASTRA-Frontend-latest
target_frontend=/var/www/astra/frontend
[[ $(realpath "$source_frontend") == "$source_frontend" && $(realpath "$target_frontend") == "$target_frontend" && -f "$target_frontend/index.html" ]] || { echo 'STOP: frontend directories differ.' >&2; exit 2; }
runuser -u Sahil -- npm --prefix "$source_frontend" run build
python3 - <<'PY'
from pathlib import Path
import os,pwd,shutil
source=Path('/home/Sahil/ASTRA-Frontend-latest/dist')
target=Path('/var/www/astra/frontend')
if source.is_symlink() or source.resolve()!=source or not (source/'index.html').is_file():
    raise SystemExit('STOP: frontend build directory differs.')
files=sorted((p for p in source.rglob('*') if p.is_file()),key=lambda p:p.relative_to(source).as_posix()=='index.html')
for file in source.rglob('*'):
    if file.is_symlink(): raise SystemExit('STOP: linked build file.')
for file in files:
    dest=target/file.relative_to(source)
    if not dest.resolve().is_relative_to(target) or dest.is_symlink():
        raise SystemExit('STOP: frontend target leaves the reviewed directory.')
    if file.suffix not in {'.html','.js','.css','.svg','.png','.jpg','.jpeg','.gif','.webp','.ico','.woff','.woff2','.ttf','.txt','.json','.map','.webmanifest','.avif'}:
        raise SystemExit('STOP: unexpected frontend build file.')
account=pwd.getpwnam('www-data')
for file in files:
    dest=target/file.relative_to(source)
    dest.parent.mkdir(parents=True,exist_ok=True)
    os.chown(dest.parent,account.pw_uid,account.pw_gid)
    os.chmod(dest.parent,0o755)
    shutil.copyfile(file,dest)
    os.chown(dest,account.pw_uid,account.pw_gid)
    os.chmod(dest,0o644)
print('DEPLOYED: Site, Department, Requested by and Approver use clickable dropdown rows with right-side checkmarks.')
print('Frontend update complete. No backend restart, database changes, backups or data copies.')
PY
