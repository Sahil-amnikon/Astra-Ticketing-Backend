#!/usr/bin/env python3
"""Create only an isolated LAB SQL Server container; never replace PostgreSQL."""
from pathlib import Path
import hashlib, json, os, secrets, shutil, subprocess, time

root = Path('/home/itadmin/astra-dotnet-migration')
assert os.geteuid() == 0, 'Interactive server authentication is required'
root.mkdir(exist_ok=True)
private = root / 'private'
private.mkdir(exist_ok=True)
os.chmod(private, 0o700)
owner = root.stat()
os.chown(private, owner.st_uid, owner.st_gid)
environment = private / 'sqlserver.env'
if not environment.exists():
    environment.write_text('MSSQL_SA_PASSWORD=Astra1!' + secrets.token_hex(32) + '\n')
    os.chown(environment, owner.st_uid, owner.st_gid)
os.chmod(environment, 0o600)
assert shutil.disk_usage(root).free > 15 * 1024**3, 'Insufficient free disk space'
compose = ['docker', 'compose', '--env-file', str(environment), '-f', str(root / 'compose.sqlserver.lab.yml')]
before = [json.loads(line) for line in subprocess.check_output(['docker', 'ps', '-a', '--format', '{{json .}}']).decode().splitlines()]
protected = {c['ID'] for c in before if not c['Names'].startswith('astra-dotnet-migration-')}
with (root / 'sqlserver-setup.log').open('w') as log:
    os.chown(root / 'sqlserver-setup.log', owner.st_uid, owner.st_gid)
    os.chmod(root / 'sqlserver-setup.log', 0o600)
    subprocess.run(compose + ['up', '-d', 'sqlserver'], check=True, stdout=log, stderr=subprocess.STDOUT)
container = 'astra-dotnet-migration-sqlserver-1'
for attempt in range(36):
    state = json.loads(subprocess.check_output(['docker', 'inspect', container]))[0]
    if state['State'].get('Health', {}).get('Status') == 'healthy':
        break
    assert state['State']['Running'], 'SQL Server stopped during initialization'
    time.sleep(5)
else:
    raise RuntimeError('SQL Server did not become healthy')
after = {c['ID'] for c in (json.loads(line) for line in subprocess.check_output(['docker', 'ps', '-a', '--format', '{{json .}}']).decode().splitlines())}
assert protected.issubset(after), 'An existing container was unexpectedly replaced'
result = {'status': 'healthy', 'purpose': 'isolated LAB migration testing', 'imageId': state['Image'], 'bindAddress': '127.0.0.1:11433', 'existingContainersPreserved': True, 'liveApplicationSwitched': False}
(root / 'sqlserver-result.json').write_text(json.dumps(result, indent=2))
os.chown(root / 'sqlserver-result.json', owner.st_uid, owner.st_gid)
print(json.dumps(result, indent=2))
