from pathlib import Path
import base64,json,os,re,subprocess,sys,tempfile
ROOT=Path.home()
FRONT=ROOT/'ASTRA-Frontend-latest'
BACK=ROOT/'ASTRA-Backend-latest'
changes=json.loads(base64.b64decode('W3sicGF0aCI6IkFTVFJBLUJhY2tlbmQtbGF0ZXN0L1Byb2dyYW0uY3MiLCJvbGQiOiJ2YXIgaW5mbyA9IFN5c3RlbS5UZXh0Lkpzb24uSnNvblNlcmlhbGl6ZXIuU2VyaWFsaXplVG9FbGVtZW50KGNob29zZXIpOyIsIm5ld1RleHQiOiJ2YXIgaW5mbyA9IFN5c3RlbS5UZXh0Lkpzb24uSnNvblNlcmlhbGl6ZXIuU2VyaWFsaXplVG9FbGVtZW50KGNob29zZXIsIG5ldyBTeXN0ZW0uVGV4dC5Kc29uLkpzb25TZXJpYWxpemVyT3B0aW9ucyhTeXN0ZW0uVGV4dC5Kc29uLkpzb25TZXJpYWxpemVyRGVmYXVsdHMuV2ViKSk7IiwiYWxsIjpmYWxzZX1d'))
original={ROOT/c['path']:((ROOT/c['path']).read_text(encoding='utf-8-sig') if (ROOT/c['path']).exists() else None) for c in changes}
updated={p:(s if s is not None else '') for p,s in original.items()}
for change in changes:
    path=ROOT/change['path'];source=updated[path]
    if change['newText'] in source:continue
    if not change['all'] and source.count(change['old'])!=1:raise SystemExit('STOP: source differs: '+change['path']+'. No files changed.')
    if change['old'] not in source:raise SystemExit('STOP: source differs: '+change['path']+'. No files changed.')
    updated[path]=source.replace(change['old'],change['newText']) if change['all'] else source.replace(change['old'],change['newText'],1)
paths=list(updated)
print('Applying internal-request-only employee access and official-team display.',flush=True)
if '--apply' not in sys.argv:raise SystemExit('Review only. No source changes.')
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
        if updated[p] != original[p]:
            if original[p] is None: p.unlink(missing_ok=True)
            else: p.write_text(original[p], encoding='utf-8')
    print('Update failed; original source restored from memory. No backup files created.', flush=True)
    raise
print('SUCCESS: employee access source update is healthy.', flush=True)
