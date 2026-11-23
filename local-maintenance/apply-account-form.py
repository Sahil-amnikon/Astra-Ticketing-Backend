from pathlib import Path
import base64, json, os, re, subprocess, sys, tempfile
ROOT = Path.home()
FRONT = ROOT / 'ASTRA-Frontend-latest'
BACK = ROOT / 'ASTRA-Backend-latest'
paths = [FRONT/'src/pages/admin/record-form.tsx', BACK/'TestPasswordAccounts.cs']
original = {p:p.read_text(encoding='utf-8-sig') for p in paths}
updated = dict(original)
changes = json.loads(base64.b64decode('W1sia2V5PVwiZGVtby1jbGllbnRcIlxuICAgICAgICAgICAgICAgIHJlc291cmNlPVwiY2xpZW50c1wiXG4gICAgICAgICAgICAgICAgbGFiZWw9XCJDbGllbnRcIiIsImtleT1cImRlbW8tY2xpZW50XCJcbiAgICAgICAgICAgICAgICByZXNvdXJjZT1cImNsaWVudHNcIlxuICAgICAgICAgICAgICAgIGxhYmVsPVwiQ2xpZW50XCJcbiAgICAgICAgICAgICAgICBzZWFyY2hhYmxlPXtmYWxzZX0iXSxbIiAgICAgICAgICAgIHtlcnJvciAmJiA8ZGl2IGNsYXNzTmFtZT1cIm5vdGljZSBlcnJvciB3aWRlXCIgcm9sZT1cImFsZXJ0XCI+e2Vycm9yfTwvZGl2Pn1cbiIsIiAgICAgICAgICAgIHtlcnJvciAmJiAoXG4gICAgICAgICAgICAgIDxkaXYgY2xhc3NOYW1lPVwibm90aWNlIGVycm9yIHdpZGVcIiByb2xlPVwiYWxlcnRcIj5cbiAgICAgICAgICAgICAgICA8cD57L2FscmVhZHkgKGV4aXN0c3xyZWdpc3RlcmVkKS9pLnRlc3QoZXJyb3IpXG4gICAgICAgICAgICAgICAgICA/ICdUaGlzIGVtYWlsIGlzIGFscmVhZHkgcmVnaXN0ZXJlZC4gT3BlbiBQZW9wbGUgdG8gZWRpdCB0aGUgZXhpc3RpbmcgYWNjb3VudCwgb3IgZW50ZXIgYSBkaWZmZXJlbnQgZW1haWwgZm9yIHRoZSBuZXcgbWVtYmVyLidcbiAgICAgICAgICAgICAgICAgIDogZXJyb3J9PC9wPlxuICAgICAgICAgICAgICAgIHsvYWxyZWFkeSAoZXhpc3RzfHJlZ2lzdGVyZWQpL2kudGVzdChlcnJvcikgJiYgKFxuICAgICAgICAgICAgICAgICAgPGJ1dHRvbiB0eXBlPVwiYnV0dG9uXCIgY2xhc3NOYW1lPVwic2Vjb25kYXJ5XCIgb25DbGljaz17b25DYW5jZWx9PlxuICAgICAgICAgICAgICAgICAgICBWaWV3IGV4aXN0aW5nIGFjY291bnRzXG4gICAgICAgICAgICAgICAgICA8L2J1dHRvbj5cbiAgICAgICAgICAgICAgICApfVxuICAgICAgICAgICAgICA8L2Rpdj5cbiAgICAgICAgICAgICl9XG4iXV0='))
f = updated[paths[0]]
for old, new in changes:
    if new in f: continue
    if f.count(old) != 1: raise SystemExit('STOP: account form differs. No files changed.')
    f = f.replace(old,new,1)
updated[paths[0]] = f
old_message = 'A test account already exists for this email.'
new_message = 'An account is already registered for this email. Open People to edit the existing account, or use a different email for a new member.'
b = updated[paths[1]]
if old_message not in b and new_message not in b: raise SystemExit('STOP: account validation differs. No files changed.')
updated[paths[1]] = b.replace(old_message,new_message)
print('Applying account dropdown and clearer duplicate-email guidance.', flush=True)
print('No accounts, passwords, memberships or database records will be changed.', flush=True)
if '--apply' not in sys.argv:
    print('Review only. Use --apply to update the Development preview.')
    raise SystemExit(0)
# Confirm a single owned Development preview before touching source.
listeners = subprocess.check_output(['ss','-ltnp','sport = :5200'], text=True)
pids = set(re.findall(r'pid=(\d+)', listeners))
if len(pids) != 1: raise SystemExit('STOP: expected one existing backend on port 5200.')
proc = Path('/proc') / pids.pop()
if proc.stat().st_uid != os.getuid() or (proc/'cwd').resolve() != BACK.resolve():
    raise SystemExit('STOP: backend owner or project folder differs.')
env = dict(part.split(b'=',1) for part in (proc/'environ').read_bytes().split(b'\0') if b'=' in part)
env = {os.fsdecode(k):os.fsdecode(v) for k,v in env.items()}
if (env.get('DOTNET_ENVIRONMENT') or env.get('ASPNETCORE_ENVIRONMENT')) != 'Development':
    raise SystemExit('STOP: only the Development preview can be updated.')
if env.get('ASPNETCORE_URLS') != 'http://127.0.0.1:5200':
    raise SystemExit('STOP: expected loopback-only preview.')
helper = BACK/'local-maintenance/restart-ticket-preview.py'
if not helper.is_file(): raise SystemExit('STOP: project restart helper is missing.')
try:
    for p in paths:
        if updated[p] != original[p]: p.write_text(updated[p], encoding='utf-8')
    subprocess.run(['npm','run','build'], cwd=FRONT, check=True)
    build = tempfile.mkdtemp(prefix='astra-ticket-ready.', dir='/tmp')
    subprocess.run(['dotnet','build','Astra.Api.csproj','-o',build], cwd=BACK, check=True)
    subprocess.run(['python3',str(helper),build], cwd=BACK, check=True)
except BaseException:
    for p in paths:
        if updated[p] != original[p]: p.write_text(original[p], encoding='utf-8')
    print('Update failed; original source restored from memory. No backup files created.', flush=True)
    raise
print('SUCCESS: Ubuntu account form updated. Refresh People & Permissions.', flush=True)
