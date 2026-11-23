from pathlib import Path
import base64,json,os,re,subprocess,sys,tempfile
ROOT=Path.home()
FRONT=ROOT/'ASTRA-Frontend-latest'
BACK=ROOT/'ASTRA-Backend-latest'
changes=json.loads(base64.b64decode('W3sicGF0aCI6IkFTVFJBLUJhY2tlbmQtbGF0ZXN0L0ZlYXR1cmVSZXBvcnRzLmNzIiwib2xkIjoiICAgIHByaXZhdGUgc3RhdGljIHN0cmluZyBGU2NvcGUoc3RyaW5nIGFsaWFzKSA9PiAkXCIoQGFkbWluPTEgT1IgKEVYSVNUUyhTRUxFQ1QgMSBGUk9NIGNsaWVudF91c2VycyBjdSBXSEVSRSBjdS51c2VyX2lkPUBhY3RvciBBTkQgY3UuY2xpZW50X2lkPXthbGlhc30uY2xpZW50X2lkKSBBTkQgKE5PVCBFWElTVFMoU0VMRUNUIDEgRlJPTSB1c2VyX3NpdGVzIHVzIFdIRVJFIHVzLnVzZXJfaWQ9QGFjdG9yKSBPUiBFWElTVFMoU0VMRUNUIDEgRlJPTSB1c2VyX3NpdGVzIHVzIFdIRVJFIHVzLnVzZXJfaWQ9QGFjdG9yIEFORCB1cy5zaXRlX2lkPXthbGlhc30uc2l0ZV9pZCkpKSlcIjsiLCJuZXdUZXh0IjoiICAgIHByaXZhdGUgc3RhdGljIHN0cmluZyBJbnRlcm5hbFRpY2tldFZpc2liaWxpdHkoc3RyaW5nIGFsaWFzLCBzdHJpbmcgYWN0b3IpID0+IGFsaWFzICE9IFwidFwiID8gXCIxPTFcIiA6ICRcIih7YWxpYXN9LmNsaWVudF9pZDw+JzAwMDAwMDAwLTAwMDAtNDAwMC04MDAwLTAwMDAwMDAwMDEzMicgT1Ige2FsaWFzfS5jcmVhdGVkX2J5X3VzZXJfaWQ9e2FjdG9yfSBPUiBFWElTVFMoU0VMRUNUIDEgRlJPTSBjb250YWN0cyBvd24gV0hFUkUgb3duLmlkPXthbGlhc30ucmVxdWVzdGVyX2lkIEFORCBvd24udXNlcl9pZD17YWN0b3J9KSBPUiBFWElTVFMoU0VMRUNUIDEgRlJPTSB1c2VyX3JvbGVzIGlyIFdIRVJFIGlyLnVzZXJfaWQ9e2FjdG9yfSBBTkQgKGlyLnJvbGVfY29kZT0nQURNSU4nIE9SIChpci5yb2xlX2NvZGUgSU4gKCdURUFNX0xFQUQnLCdNQU5BR0VSJykgQU5EICh7YWxpYXN9LmFzc2lnbmVkX3VzZXJfaWQ9e2FjdG9yfSBPUiBFWElTVFMoU0VMRUNUIDEgRlJPTSBjbGllbnRfdXNlcnMgbWluZSBKT0lOIGNsaWVudF91c2VycyB0YXJnZXQgT04gdGFyZ2V0LmNsaWVudF9pZD1taW5lLmNsaWVudF9pZCBXSEVSRSBtaW5lLnVzZXJfaWQ9e2FjdG9yfSBBTkQgdGFyZ2V0LnVzZXJfaWQ9e2FsaWFzfS5hc3NpZ25lZF91c2VyX2lkIEFORCBtaW5lLmNsaWVudF9pZDw+JzAwMDAwMDAwLTAwMDAtNDAwMC04MDAwLTAwMDAwMDAwMDEzMicpKSkpKSlcIjtcbiAgICBwcml2YXRlIHN0YXRpYyBzdHJpbmcgRlNjb3BlKHN0cmluZyBhbGlhcykgPT4gJFwiKHtJbnRlcm5hbFRpY2tldFZpc2liaWxpdHkoYWxpYXMsIFwiQGFjdG9yXCIpfSkgQU5EIChAYWRtaW49MSBPUiAoRVhJU1RTKFNFTEVDVCAxIEZST00gY2xpZW50X3VzZXJzIGN1IFdIRVJFIGN1LnVzZXJfaWQ9QGFjdG9yIEFORCBjdS5jbGllbnRfaWQ9e2FsaWFzfS5jbGllbnRfaWQpIEFORCAoTk9UIEVYSVNUUyhTRUxFQ1QgMSBGUk9NIHVzZXJfc2l0ZXMgdXMgV0hFUkUgdXMudXNlcl9pZD1AYWN0b3IpIE9SIEVYSVNUUyhTRUxFQ1QgMSBGUk9NIHVzZXJfc2l0ZXMgdXMgV0hFUkUgdXMudXNlcl9pZD1AYWN0b3IgQU5EIHVzLnNpdGVfaWQ9e2FsaWFzfS5zaXRlX2lkKSkpKVwiOyIsImFsbCI6ZmFsc2V9LHsicGF0aCI6IkFTVFJBLUJhY2tlbmQtbGF0ZXN0L1Byb2dyYW0uY3MiLCJvbGQiOiIgICAgICAgIHZhciBhY2Nlc3NQcmVkaWNhdGUgPSBpc0FkbWluID8gXCIxPTFcIiA6IFwiKCh0LmNsaWVudF9pZCBJTiAoU0VMRUNUIGNsaWVudF9pZCBGUk9NIGNsaWVudF91c2VycyBXSEVSRSB1c2VyX2lkPUBpZCkgT1IgdC5zaXRlX2lkIElOIChTRUxFQ1Qgc2l0ZV9pZCBGUk9NIHVzZXJfc2l0ZXMgV0hFUkUgdXNlcl9pZD1AaWQpKSBBTkQgKE5PVCBFWElTVFMoU0VMRUNUIDEgRlJPTSB1c2VyX3NpdGVzIFdIRVJFIHVzZXJfaWQ9QGlkKSBPUiB0LnNpdGVfaWQgSU4gKFNFTEVDVCBzaXRlX2lkIEZST00gdXNlcl9zaXRlcyBXSEVSRSB1c2VyX2lkPUBpZCkpKVwiOyIsIm5ld1RleHQiOiIgICAgICAgIHZhciBhY2Nlc3NQcmVkaWNhdGUgPSBpc0FkbWluID8gXCIxPTFcIiA6IFwiKCh0LmNsaWVudF9pZCBJTiAoU0VMRUNUIGNsaWVudF9pZCBGUk9NIGNsaWVudF91c2VycyBXSEVSRSB1c2VyX2lkPUBpZCkgT1IgdC5zaXRlX2lkIElOIChTRUxFQ1Qgc2l0ZV9pZCBGUk9NIHVzZXJfc2l0ZXMgV0hFUkUgdXNlcl9pZD1AaWQpKSBBTkQgKE5PVCBFWElTVFMoU0VMRUNUIDEgRlJPTSB1c2VyX3NpdGVzIFdIRVJFIHVzZXJfaWQ9QGlkKSBPUiB0LnNpdGVfaWQgSU4gKFNFTEVDVCBzaXRlX2lkIEZST00gdXNlcl9zaXRlcyBXSEVSRSB1c2VyX2lkPUBpZCkpKVwiO1xuICAgICAgICBhY2Nlc3NQcmVkaWNhdGUgPSAkXCIoe2FjY2Vzc1ByZWRpY2F0ZX0pIEFORCB7SW50ZXJuYWxUaWNrZXRWaXNpYmlsaXR5KFwidFwiLCBcIkBpZFwiKX1cIjsiLCJhbGwiOmZhbHNlfSx7InBhdGgiOiJBU1RSQS1CYWNrZW5kLWxhdGVzdC9Qcm9ncmFtLmNzIiwib2xkIjoiICAgICAgICB2YXIgYWNjZXNzID0gaXNBZG1pbiA/IFwiMT0xXCIgOiBcIih0LmNsaWVudF9pZCBJTiAoU0VMRUNUIGNsaWVudF9pZCBGUk9NIGNsaWVudF91c2VycyBXSEVSRSB1c2VyX2lkPUBpZCkgT1IgdC5jbGllbnRfaWQ9JzAwMDAwMDAwLTAwMDAtNDAwMC04MDAwLTAwMDAwMDAwMDEzMicgT1IgdC5zaXRlX2lkIElOIChTRUxFQ1Qgc2l0ZV9pZCBGUk9NIHVzZXJfc2l0ZXMgV0hFUkUgdXNlcl9pZD1AaWQpKVwiOyIsIm5ld1RleHQiOiIgICAgICAgIHZhciBhY2Nlc3MgPSBpc0FkbWluID8gXCIxPTFcIiA6IFwiKHQuY2xpZW50X2lkIElOIChTRUxFQ1QgY2xpZW50X2lkIEZST00gY2xpZW50X3VzZXJzIFdIRVJFIHVzZXJfaWQ9QGlkKSBPUiB0LmNsaWVudF9pZD0nMDAwMDAwMDAtMDAwMC00MDAwLTgwMDAtMDAwMDAwMDAwMTMyJyBPUiB0LnNpdGVfaWQgSU4gKFNFTEVDVCBzaXRlX2lkIEZST00gdXNlcl9zaXRlcyBXSEVSRSB1c2VyX2lkPUBpZCkpXCI7XG4gICAgICAgIGFjY2VzcyA9ICRcIih7YWNjZXNzfSkgQU5EIHtJbnRlcm5hbFRpY2tldFZpc2liaWxpdHkoXCJ0XCIsIFwiQGlkXCIpfVwiOyIsImFsbCI6ZmFsc2V9XQ=='))
original={ROOT/c['path']:(ROOT/c['path']).read_text(encoding='utf-8-sig') for c in changes}
updated=dict(original)
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
        if updated[p] != original[p]: p.write_text(original[p], encoding='utf-8')
    print('Update failed; original source restored from memory. No backup files created.', flush=True)
    raise
print('SUCCESS: employee access source update is healthy.', flush=True)
