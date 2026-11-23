import os
import re
import signal
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.request
from pathlib import Path

root = (Path.home() / 'ASTRA-Backend-latest').resolve()
build = Path(sys.argv[1]).resolve()
if not build.is_dir() or not (build / 'Astra.Api.dll').is_file():
    raise SystemExit('STOP: compiled backend not found.')
if build.parent != Path('/tmp') or not build.name.startswith('astra-ticket-ready.'):
    raise SystemExit('STOP: expected an isolated /tmp/astra-ticket-ready.* build.')
startup = (root / 'Database/FeatureSchemaStartup.cs').read_text()
if 'ASTRA_SKIP_FEATURE_SCHEMA_STARTUP' not in startup:
    raise SystemExit('STOP: database-setup skip setting is missing.')
listeners = subprocess.check_output(['ss', '-ltnp', 'sport = :5200'], text=True)
pids = set(re.findall(r'pid=(\d+)', listeners))
if len(pids) != 1:
    raise SystemExit('STOP: could not identify exactly one backend on port 5200.')
pid = int(pids.pop())
proc = Path('/proc') / str(pid)
if proc.stat().st_uid != os.getuid() or (proc / 'cwd').resolve() != root:
    raise SystemExit('STOP: backend owner or project folder differs.')
env = dict(part.split(b'=', 1) for part in (proc / 'environ').read_bytes().split(b'\0') if b'=' in part)
env = {os.fsdecode(k): os.fsdecode(v) for k, v in env.items()}
environment = env.get('DOTNET_ENVIRONMENT') or env.get('ASPNETCORE_ENVIRONMENT')
if environment != 'Development':
    raise SystemExit('STOP: this helper only restarts the Development preview.')
if env.get('ASPNETCORE_URLS') != 'http://127.0.0.1:5200':
    raise SystemExit('STOP: expected the existing loopback-only backend address.')
old_command = [os.fsdecode(x) for x in (proc / 'cmdline').read_bytes().split(b'\0') if x]
if len(old_command) < 2 or Path(old_command[0]).name != 'dotnet' or not old_command[1].endswith('Astra.Api.dll'):
    raise SystemExit('STOP: expected an existing dotnet Astra.Api.dll process; no process stopped.')
old_dll = Path(old_command[1])
if not old_dll.is_absolute():
    old_dll = root / old_dll
old_dll = old_dll.resolve()
trusted_temporary_build = old_dll.parent.parent == Path('/tmp') and old_dll.parent.name.startswith(
    ('astra-ticket-ready.', 'astra-ticket-access.', 'astra-ticket-rollback.'))
if (root not in old_dll.parents and not trusted_temporary_build) or old_dll.parent == root:
    raise SystemExit('STOP: unexpected original runtime folder; no process stopped.')
new_env = dict(env, ASTRA_SKIP_FEATURE_SCHEMA_STARTUP='true')
log_path = root / 'local-maintenance/logs/astra-ticket-preview.log'
log_path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
if not log_path.parent.resolve().is_relative_to(root):
    raise SystemExit('STOP: the preview log folder must be inside the backend project.')


def open_preview_log():
    fd = os.open(log_path, os.O_WRONLY | os.O_CREAT | os.O_APPEND | os.O_NOFOLLOW, 0o600)
    os.fchmod(fd, 0o600)
    return os.fdopen(fd, 'ab')

def healthy():
    try:
        with urllib.request.urlopen('http://127.0.0.1:5200/health', timeout=1) as response:
            return response.status == 200
    except Exception:
        return False

def wait_ready(child):
    for _ in range(60):
        if child.poll() is not None:
            return False
        if healthy():
            return True
        time.sleep(0.25)
    return False

print('Restarting Development preview only. Existing private settings stay in memory.')
print('Database setup SQL will be skipped. Log:', log_path)
with open_preview_log() as log:
    log.write(f'\n--- Preview restart {time.strftime("%Y-%m-%d %H:%M:%S %z")} ---\n'.encode())
os.kill(pid, signal.SIGTERM)
for _ in range(60):
    if not proc.exists():
        break
    time.sleep(0.25)
else:
    raise SystemExit('STOP: original process did not stop; no forced termination used.')
new_process = None
try:
    with open_preview_log() as log:
        new_process = subprocess.Popen(['dotnet', str(build / 'Astra.Api.dll')], cwd=root,
            env=new_env, stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT,
            start_new_session=True)
        if wait_ready(new_process):
            print('Updated backend is healthy. PID:', new_process.pid)
            sys.exit(0)
except Exception as error:
    print('Updated preview failed to start:', type(error).__name__)
if new_process is not None and new_process.poll() is None:
    new_process.terminate()
    try:
        new_process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        raise SystemExit('STOP: updated process did not exit. Check the private log before restarting.')
# Prepare the isolated recovery runtime only after a failed restart. Its empty
# startup SQL also protects older builds during recovery.
rollback_dir = Path(tempfile.mkdtemp(prefix='astra-ticket-rollback.'))
shutil.copytree(old_dll.parent, rollback_dir, dirs_exist_ok=True)
(rollback_dir / 'feature-runtime.sql').write_text('')
rollback_command = [old_command[0], str(rollback_dir / old_dll.name), *old_command[2:]]
with open_preview_log() as log:
    restored = subprocess.Popen(rollback_command, cwd=root, env=new_env, stdin=subprocess.DEVNULL,
        stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
    restored_ok = wait_ready(restored)
raise SystemExit('Update failed. Original backend restored.' if restored_ok else
    'Update and recovery failed. Inspect the private log: ' + str(log_path))
