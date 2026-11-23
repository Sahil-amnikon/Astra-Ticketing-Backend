from pathlib import Path
import base64,json,os,re,subprocess,sys,tempfile
ROOT=Path.home()
FRONT=ROOT/'ASTRA-Frontend-latest'
BACK=ROOT/'ASTRA-Backend-latest'
changes=json.loads(base64.b64decode('W3sicGF0aCI6IkFTVFJBLUJhY2tlbmQtbGF0ZXN0L0ZlYXR1cmVPZmZpY2lhbFRlYW1zLmNzIiwib2xkIjoiIiwibmV3VGV4dCI6InVzaW5nIE15U3FsQ29ubmVjdG9yO1xucHVibGljIHNlYWxlZCBwYXJ0aWFsIGNsYXNzIE15U3FsU3RvcmVcbntcbiAgICBwdWJsaWMgYXN5bmMgVGFzazxMaXN0PERpY3Rpb25hcnk8c3RyaW5nLCBvYmplY3Q/Pj4+IEZpbHRlck9mZmljaWFsVGVhbU1lbWJlcnMoc3RyaW5nIGFjdG9yLCBMaXN0PERpY3Rpb25hcnk8c3RyaW5nLCBvYmplY3Q/Pj4gbWVtYmVycywgQ2FuY2VsbGF0aW9uVG9rZW4gY3QpXG4gICAge1xuICAgICAgICB2YXIgYWxsb3dlZCA9IGF3YWl0IEZRdWVyeShAXCJTRUxFQ1QgdS5pZCBGUk9NIHVzZXJzIHUgV0hFUkUgdS5hY3RpdmU9MVxuICAgICAgICAgICAgQU5EICh1LmFjY2Vzc19leHBpcmVzX2F0IElTIE5VTEwgT1IgdS5hY2Nlc3NfZXhwaXJlc19hdD5VVENfVElNRVNUQU1QKDYpKVxuICAgICAgICAgICAgQU5EIEVYSVNUUyhTRUxFQ1QgMSBGUk9NIHVzZXJfcm9sZXMgciBXSEVSRSByLnVzZXJfaWQ9dS5pZCBBTkQgci5yb2xlX2NvZGUgSU4gKCdJTlRFUk4nLCdURUNITklDSUFOJywnVEVBTV9MRUFEJywnTUFOQUdFUicsJ0FETUlOJykpXG4gICAgICAgICAgICBBTkQgKEVYSVNUUyhTRUxFQ1QgMSBGUk9NIHVzZXJfcm9sZXMgciBXSEVSRSByLnVzZXJfaWQ9QGFjdG9yIEFORCByLnJvbGVfY29kZT0nQURNSU4nKVxuICAgICAgICAgICAgICBPUiBFWElTVFMoU0VMRUNUIDEgRlJPTSBjbGllbnRfdXNlcnMgbWluZSBKT0lOIGNsaWVudF91c2VycyBjb2xsZWFndWUgT04gY29sbGVhZ3VlLmNsaWVudF9pZD1taW5lLmNsaWVudF9pZFxuICAgICAgICAgICAgICAgICAgICAgICAgV0hFUkUgbWluZS51c2VyX2lkPUBhY3RvciBBTkQgY29sbGVhZ3VlLnVzZXJfaWQ9dS5pZCBBTkQgbWluZS5jbGllbnRfaWQ8PicwMDAwMDAwMC0wMDAwLTQwMDAtODAwMC0wMDAwMDAwMDAxMzInKVxuICAgICAgICAgICAgICBPUiAoRVhJU1RTKFNFTEVDVCAxIEZST00gY2xpZW50X3VzZXJzIG1pbmUgV0hFUkUgbWluZS51c2VyX2lkPUBhY3RvciBBTkQgbWluZS5jbGllbnRfaWQ9JzAwMDAwMDAwLTAwMDAtNDAwMC04MDAwLTAwMDAwMDAwMDEzMicpXG4gICAgICAgICAgICAgICAgICBBTkQgTk9UIEVYSVNUUyhTRUxFQ1QgMSBGUk9NIGNsaWVudF91c2VycyBtaW5lIFdIRVJFIG1pbmUudXNlcl9pZD1AYWN0b3IgQU5EIG1pbmUuY2xpZW50X2lkPD4nMDAwMDAwMDAtMDAwMC00MDAwLTgwMDAtMDAwMDAwMDAwMTMyJylcbiAgICAgICAgICAgICAgICAgIEFORCBFWElTVFMoU0VMRUNUIDEgRlJPTSBjbGllbnRfdXNlcnMgY29sbGVhZ3VlIFdIRVJFIGNvbGxlYWd1ZS51c2VyX2lkPXUuaWQgQU5EIGNvbGxlYWd1ZS5jbGllbnRfaWQ9JzAwMDAwMDAwLTAwMDAtNDAwMC04MDAwLTAwMDAwMDAwMDEzMicpXG4gICAgICAgICAgICAgICAgICBBTkQgTk9UIEVYSVNUUyhTRUxFQ1QgMSBGUk9NIGNsaWVudF91c2VycyBjb2xsZWFndWUgV0hFUkUgY29sbGVhZ3VlLnVzZXJfaWQ9dS5pZCBBTkQgY29sbGVhZ3VlLmNsaWVudF9pZDw+JzAwMDAwMDAwLTAwMDAtNDAwMC04MDAwLTAwMDAwMDAwMDEzMicpKSlcIiwgY3QsIChcImFjdG9yXCIsYWN0b3IpKTtcbiAgICAgICAgdmFyIGlkcz1hbGxvd2VkLlNlbGVjdChyb3c9PkNvbnZlcnQuVG9TdHJpbmcocm93W1wiaWRcIl0pISkuVG9IYXNoU2V0KFN0cmluZ0NvbXBhcmVyLk9yZGluYWxJZ25vcmVDYXNlKTtcbiAgICAgICAgcmV0dXJuIG1lbWJlcnMuV2hlcmUocm93PT5pZHMuQ29udGFpbnMoQ29udmVydC5Ub1N0cmluZyhyb3dbXCJpZFwiXSkhKSkuVG9MaXN0KCk7XG4gICAgfVxuICAgIHB1YmxpYyBhc3luYyBUYXNrPGJvb2w+IElzT2ZmaWNpYWxUZWFtQ29sbGVhZ3VlKHN0cmluZyBhY3Rvciwgc3RyaW5nIGNvbGxlYWd1ZSwgQ2FuY2VsbGF0aW9uVG9rZW4gY3QpID0+XG4gICAgICAgIChhd2FpdCBGaWx0ZXJPZmZpY2lhbFRlYW1NZW1iZXJzKGFjdG9yLFtuZXcgRGljdGlvbmFyeTxzdHJpbmcsb2JqZWN0Pz4geyBbXCJpZFwiXT1jb2xsZWFndWUgfV0sY3QpKS5Db3VudD09MTtcbn1cbiIsImFsbCI6ZmFsc2V9LHsicGF0aCI6IkFTVFJBLUJhY2tlbmQtbGF0ZXN0L1Byb2dyYW0uY3MiLCJvbGQiOiIgICAgcmV0dXJuIFJlc3VsdHMuT2soYXNzaWduZWVzKTsiLCJuZXdUZXh0IjoiICAgIHJldHVybiBSZXN1bHRzLk9rKGF3YWl0IHN0b3JlLkZpbHRlck9mZmljaWFsVGVhbU1lbWJlcnModXNlcklkLCBhc3NpZ25lZXMsIGN0KSk7IiwiYWxsIjpmYWxzZX0seyJwYXRoIjoiQVNUUkEtQmFja2VuZC1sYXRlc3QvUHJvZ3JhbS5jcyIsIm9sZCI6IiAgICAgICAgdmFyIHJlc3VsdCA9IGF3YWl0IHN0b3JlLkFzc2lnblRpY2tldCh1c2VySWQsIGlkLCBib2R5LCBjYW5jZWxsYXRpb25Ub2tlbik7IiwibmV3VGV4dCI6IiAgICAgICAgaWYgKCFhd2FpdCBzdG9yZS5Jc09mZmljaWFsVGVhbUNvbGxlYWd1ZSh1c2VySWQsIGJvZHkuQXNzaWduZWRVc2VySWQsIGNhbmNlbGxhdGlvblRva2VuKSkgcmV0dXJuIFJlc3VsdHMuRm9yYmlkKCk7XG4gICAgICAgIHZhciByZXN1bHQgPSBhd2FpdCBzdG9yZS5Bc3NpZ25UaWNrZXQodXNlcklkLCBpZCwgYm9keSwgY2FuY2VsbGF0aW9uVG9rZW4pOyIsImFsbCI6ZmFsc2V9LHsicGF0aCI6IkFTVFJBLUJhY2tlbmQtbGF0ZXN0L1Byb2dyYW0uY3MiLCJvbGQiOiIgICAgICAgIHZhciBjcmVhdGVkID0gYXdhaXQgc3RvcmUuQ3JlYXRlVGlja2V0KHVzZXJJZCwgYm9keSwgY2FuY2VsbGF0aW9uVG9rZW4pOyIsIm5ld1RleHQiOiIgICAgICAgIGlmIChib2R5LkFzc2lnbmVkVXNlcklkIGlzIG5vdCBudWxsICYmICFhd2FpdCBzdG9yZS5Jc09mZmljaWFsVGVhbUNvbGxlYWd1ZSh1c2VySWQsIGJvZHkuQXNzaWduZWRVc2VySWQsIGNhbmNlbGxhdGlvblRva2VuKSkgcmV0dXJuIFJlc3VsdHMuRm9yYmlkKCk7XG4gICAgICAgIHZhciBjcmVhdGVkID0gYXdhaXQgc3RvcmUuQ3JlYXRlVGlja2V0KHVzZXJJZCwgYm9keSwgY2FuY2VsbGF0aW9uVG9rZW4pOyIsImFsbCI6ZmFsc2V9LHsicGF0aCI6IkFTVFJBLUJhY2tlbmQtbGF0ZXN0L1Byb2dyYW0uY3MiLCJvbGQiOiIgICAgICAgIHJldHVybiByb3dzO1xuICAgIH1cblxuICAgIHB1YmxpYyBhc3luYyBUYXNrPENyZWF0ZWRFbnRpdHk/PiBDcmVhdGVEZXBhcnRtZW50IiwibmV3VGV4dCI6IiAgICAgICAgcmV0dXJuIGF3YWl0IEZpbHRlck9mZmljaWFsVGVhbU1lbWJlcnModXNlcklkLCByb3dzLCBjdCk7XG4gICAgfVxuXG4gICAgcHVibGljIGFzeW5jIFRhc2s8Q3JlYXRlZEVudGl0eT8+IENyZWF0ZURlcGFydG1lbnQiLCJhbGwiOmZhbHNlfV0='))
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
