from pathlib import Path
import base64,json,os,re,subprocess,sys,tempfile
ROOT=Path.home()
FRONT=ROOT/'ASTRA-Frontend-latest'
BACK=ROOT/'ASTRA-Backend-latest'
paths=[BACK/'Program.cs',BACK/'FeatureAdminEdits.cs',BACK/'FeatureTicketAssignees.cs']
original={p:p.read_text(encoding='utf-8-sig') for p in paths}
updated=dict(original)
p=paths[0]
s=original[p]
old='            var vendorDirectory = Guid.TryParse(clientId, out var selectedClient) &&\n                selectedClient == Guid.Parse("00000000-0000-4000-8000-000000000132");\n            var users = await ListUsers(ct, vendorDirectory ? null : clientId);'
new='            const bool vendorDirectory = false;\n            var users = await ListUsers(ct, clientId);'
if new not in s:
    if s.count(old)!=1:raise SystemExit('STOP: vendor directory differs. No files changed.')
    s=s.replace(old,new,1)
updated[p]=s
p=paths[1]
s=original[p]
if 'var internalLeadClients' in s:
    a=s.find('        // Scope this automatic assignment by client ID')
    b=s.find('        if (!new[] { "INTERNAL", "CLIENT", "BOTH", "ALL" }',a)
    if a<0 or b<0:raise SystemExit('STOP: automatic lead assignment differs. No files changed.')
    s=s[:a]+'        if (scope == "CLIENT" && Ids("clientIds").SequenceEqual(new[] { "00000000-0000-4000-8000-000000000132" }))\n            scope = "INTERNAL";\n'+s[b:]
    a=s.find('        if (!isInternalLead && scope == "BOTH"')
    b=s.find('        if (scope is "INTERNAL" or "BOTH" or "ALL")',a)
    if a<0 or b<0:raise SystemExit('STOP: former TL logic differs. No files changed.')
    s=s[:a]+s[b:]
updated[p]=s
p=paths[2]
s=original[p]
pattern=re.compile(r'((?:private|public)\s+(?:static\s+|const\s+)?string\s+TicketAssigneeScope(?:\([^)]*\))?\s*(?:=>|=)\s*\$?""")[\s\S]*?(""";)')
body='\n        (EXISTS(SELECT 1 FROM client_users cu WHERE cu.user_id=u.id AND cu.client_id=@client)\n         AND (@site IS NULL OR NOT EXISTS(SELECT 1 FROM user_sites us WHERE us.user_id=u.id)\n              OR EXISTS(SELECT 1 FROM user_sites us WHERE us.user_id=u.id AND us.site_id=@site)))\n        '
s,count=pattern.subn(lambda m:m[1]+body+m[2],s)
if count!=1:raise SystemExit('STOP: technician scope differs. No files changed.')
updated[p]=s
print('Removing automatic TL/vendor access shortcuts. Existing memberships remain unchanged for review.',flush=True)
if '--apply' not in sys.argv:raise SystemExit('Review only. No files changed.')
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
print('SUCCESS: MSP membership rules updated. Existing records await the reviewed repair.', flush=True)

with tempfile.TemporaryDirectory(prefix='.msp-review-',dir=BACK/'local-maintenance') as directory:
    folder=Path(directory)
    project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="'+str(BACK/'Astra.Api.csproj')+'" /></ItemGroup></Project>'
    (folder/'Review.csproj').write_text(project)
    (folder/'Program.cs').write_bytes(base64.b64decode('dXNpbmcgTWljcm9zb2Z0LkV4dGVuc2lvbnMuQ29uZmlndXJhdGlvbjsKdXNpbmcgTXlTcWxDb25uZWN0b3I7CnVzaW5nIFN5c3RlbS5UZXh0Lkpzb247CnRyeSB7CiAgICB2YXIgY29uZmlnPW5ldyBDb25maWd1cmF0aW9uQnVpbGRlcigpLkFkZEVudmlyb25tZW50VmFyaWFibGVzKCkuQnVpbGQoKTsKICAgIHZhciByYXc9Y29uZmlnLkdldENvbm5lY3Rpb25TdHJpbmcoIk15U3FsIikhOwogICAgdmFyIHRhcmdldD1uZXcgTXlTcWxDb25uZWN0aW9uU3RyaW5nQnVpbGRlcihyYXcpOwogICAgaWYodGFyZ2V0LlNlcnZlciE9IjE3Mi4zMS40LjUifHx0YXJnZXQuRGF0YWJhc2UhPSJhc3RyYV90aWNrZXRpbmciKXRocm93IG5ldyBJbnZhbGlkT3BlcmF0aW9uRXhjZXB0aW9uKCk7CiAgICBhd2FpdCB1c2luZyB2YXIgY29ubmVjdGlvbj1uZXcgTXlTcWxDb25uZWN0aW9uKHJhdyk7YXdhaXQgY29ubmVjdGlvbi5PcGVuQXN5bmMoKTsKICAgIGFzeW5jIFRhc2sgU2hvdyhzdHJpbmcgbGFiZWwsc3RyaW5nIHNxbCkgewogICAgICAgIGF3YWl0IHVzaW5nIHZhciBjb21tYW5kPW5ldyBNeVNxbENvbW1hbmQoc3FsLGNvbm5lY3Rpb24pO2F3YWl0IHVzaW5nIHZhciByZWFkZXI9YXdhaXQgY29tbWFuZC5FeGVjdXRlUmVhZGVyQXN5bmMoKTsKICAgICAgICB3aGlsZShhd2FpdCByZWFkZXIuUmVhZEFzeW5jKCkpe3ZhciByb3c9bmV3IERpY3Rpb25hcnk8c3RyaW5nLG9iamVjdD8+KCk7Zm9yKGludCBpPTA7aTxyZWFkZXIuRmllbGRDb3VudDtpKyspcm93W3JlYWRlci5HZXROYW1lKGkpXT1yZWFkZXIuSXNEQk51bGwoaSk/bnVsbDpyZWFkZXIuR2V0VmFsdWUoaSk7Q29uc29sZS5Xcml0ZUxpbmUobGFiZWwrIjogIitKc29uU2VyaWFsaXplci5TZXJpYWxpemUocm93KSk7fQogICAgfQogICAgYXdhaXQgU2hvdygiVEwgTUVNQkVSU0hJUCBSRVZJRVciLCAiU0VMRUNUIHUuZGlzcGxheV9uYW1lLHUuZW1haWwsdS53b3JrX3Njb3BlLChTRUxFQ1QgR1JPVVBfQ09OQ0FUKHIucm9sZV9jb2RlKSBGUk9NIHVzZXJfcm9sZXMgciBXSEVSRSByLnVzZXJfaWQ9dS5pZCkgQVMgcm9sZXMsR1JPVVBfQ09OQ0FUKGMubmFtZSBPUkRFUiBCWSBjLm5hbWUpIEFTIG1zcHMgRlJPTSB1c2VycyB1IExFRlQgSk9JTiBjbGllbnRfdXNlcnMgY3UgT04gY3UudXNlcl9pZD11LmlkIExFRlQgSk9JTiBjbGllbnRzIGMgT04gYy5pZD1jdS5jbGllbnRfaWQgV0hFUkUgTE9XRVIodS5lbWFpbCkgSU4gKCdub29yQGdlb21ldHJ5LmNvbScsJ2FyaWYua2hhbkByYXZpZC5jb20nKSBHUk9VUCBCWSB1LmlkIik7CiAgICBhd2FpdCBTaG93KCJHRU9NRVRSWSBDTElFTlQgUkVWSUVXIiwgIlNFTEVDVCBzLmlkLHMubmFtZSxzLmFjdGl2ZSxjLm5hbWUgQVMgbXNwLChTRUxFQ1QgQ09VTlQoKikgRlJPTSB0aWNrZXRzIHQgV0hFUkUgdC5zaXRlX2lkPXMuaWQpIEFTIGxpbmtlZF90aWNrZXRzLChTRUxFQ1QgR1JPVVBfQ09OQ0FUKGQuZG9tYWluKSBGUk9NIGNsaWVudF9lbWFpbF9kb21haW5zIGQgV0hFUkUgZC5zaXRlX2lkPXMuaWQgQU5EIGQuYWN0aXZlPTEpIEFTIGFjdGl2ZV9kb21haW5zIEZST00gc2l0ZXMgcyBKT0lOIGNsaWVudHMgYyBPTiBjLmlkPXMuY2xpZW50X2lkIFdIRVJFIExPV0VSKHMubmFtZSk9J2Ftbmlrb24nIEFORCBMT1dFUihjLm5hbWUpPSdnZW9tZXRyeSciKTsKICAgIENvbnNvbGUuV3JpdGVMaW5lKCJSRUFELU9OTFkgbWVtYmVyc2hpcCByZXZpZXcgY29tcGxldGUuIE5vIGRhdGFiYXNlIHJlY29yZHMgY2hhbmdlZC4iKTtyZXR1cm4gMDsKfSBjYXRjaChNeVNxbEV4Y2VwdGlvbiBlKXtDb25zb2xlLkVycm9yLldyaXRlTGluZSgiTWVtYmVyc2hpcCBpbnNwZWN0aW9uIGZhaWxlZC4gTXlTUUwgZXJyb3IgIitlLk51bWJlcik7cmV0dXJuIDE7fWNhdGNoKEV4Y2VwdGlvbiBlKXtDb25zb2xlLkVycm9yLldyaXRlTGluZSgiTWVtYmVyc2hpcCBpbnNwZWN0aW9uIHN0b3BwZWQ6ICIrZS5HZXRUeXBlKCkuTmFtZSk7cmV0dXJuIDE7fQo='))
    subprocess.run(['dotnet','build',str(folder/'Review.csproj'),'--ignore-failed-sources','-p:NuGetAudit=false'],cwd=BACK,check=True)
    subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Review.dll')],cwd=BACK,env=env,check=True)
