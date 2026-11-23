#!/usr/bin/env bash
set -euo pipefail
[[ $EUID == 0 && ${1:-} == --apply && $# == 1 ]] || { echo 'Use: sudo bash deploy-change-review-presentation.sh --apply' >&2; exit 2; }
source_frontend=/home/Sahil/ASTRA-Frontend-latest
target_frontend=/var/www/astra/frontend
[[ $(realpath "$source_frontend") == "$source_frontend" && $(realpath "$target_frontend") == "$target_frontend" && -f "$target_frontend/index.html" ]] || { echo 'STOP: application directories differ.' >&2; exit 2; }
runuser -u Sahil -- npm --prefix "$source_frontend" run build
python3 - <<'PY'
from pathlib import Path
import os,pwd,shutil
source=Path('/home/Sahil/ASTRA-Frontend-latest/dist');target=Path('/var/www/astra/frontend')
if not (source/'index.html').is_file(): raise SystemExit('STOP: frontend build is missing.')
account=pwd.getpwnam('www-data')
files=sorted((p for p in source.rglob('*') if p.is_file()),key=lambda p:p.relative_to(source).as_posix()=='index.html')
for file in files:
    dest=target/file.relative_to(source)
    if file.is_symlink() or not dest.resolve().is_relative_to(target): raise SystemExit('STOP: unexpected linked build or target file.')
for file in files:
    dest=target/file.relative_to(source)
    dest.parent.mkdir(parents=True,exist_ok=True)
    os.chown(dest.parent,account.pw_uid,account.pw_gid);os.chmod(dest.parent,0o755)
    shutil.copyfile(file,dest)
    os.chown(dest,account.pw_uid,account.pw_gid);os.chmod(dest,0o644)
PY
echo 'DEPLOYED: Change review headings are bold and the review dialog closes after successful decisions. No database changes or backups.'
