from pathlib import Path
import base64, json, os, re, subprocess, sys, tempfile, xml.etree.ElementTree as ET
root=Path.home()/'ASTRA-Backend-latest'
listeners=subprocess.check_output(['ss','-ltnp','sport = :5200'],text=True)
pids=set(re.findall(r'pid=(\d+)',listeners))
if len(pids)!=1:raise SystemExit('STOP: expected one running preview backend.')
proc=Path('/proc')/pids.pop()
if proc.stat().st_uid!=os.getuid() or (proc/'cwd').resolve()!=root.resolve():raise SystemExit('STOP: backend ownership or folder differs.')
env={os.fsdecode(k):os.fsdecode(v) for k,v in [p.split(b'=',1) for p in (proc/'environ').read_bytes().split(b'\0') if b'=' in p]}
if (env.get('DOTNET_ENVIRONMENT') or env.get('ASPNETCORE_ENVIRONMENT'))!='Development':raise SystemExit('STOP: expected Development preview.')
account_path=Path(env.get('ASTRA_TEST_ACCOUNTS_FILE',str(root/'App_Data/test-accounts.json')))
if not account_path.is_absolute():account_path=root/account_path
for account in json.loads(account_path.read_text()) if account_path.is_file() else []:
    print('LOGIN IDENTITY: '+json.dumps({key:account.get(key) for key in ['UserId','Login','DisplayName','Role']}),flush=True)
version=next(e.get('Version') for e in ET.parse(root/'Astra.Api.csproj').iter('PackageReference') if e.get('Include')=='MySqlConnector')
if not re.fullmatch(r'\d+(?:\.\d+){1,3}',version):raise SystemExit('STOP: unexpected driver version.')
project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="'+str(root/'Astra.Api.csproj')+'" /></ItemGroup></Project>'
os.umask(0o077)
with tempfile.TemporaryDirectory(prefix='.account-inspection-',dir=root/'local-maintenance') as directory:
    folder=Path(directory)
    (folder/'Inspect.csproj').write_text(project)
    (folder/'Program.cs').write_bytes(base64.b64decode('dXNpbmcgTWljcm9zb2Z0LkV4dGVuc2lvbnMuQ29uZmlndXJhdGlvbjsKdXNpbmcgTXlTcWxDb25uZWN0b3I7CnVzaW5nIFN5c3RlbS5UZXh0Lkpzb247CnRyeSB7CiB2YXIgY29uZmlnPW5ldyBDb25maWd1cmF0aW9uQnVpbGRlcigpLkFkZEVudmlyb25tZW50VmFyaWFibGVzKCkuQnVpbGQoKTt2YXIgcmF3PWNvbmZpZy5HZXRDb25uZWN0aW9uU3RyaW5nKCJNeVNxbCIpITt2YXIgdGFyZ2V0PW5ldyBNeVNxbENvbm5lY3Rpb25TdHJpbmdCdWlsZGVyKHJhdyk7aWYodGFyZ2V0LlNlcnZlciE9IjE3Mi4zMS40LjUifHx0YXJnZXQuRGF0YWJhc2UhPSJhc3RyYV90aWNrZXRpbmciKXRocm93IG5ldyBJbnZhbGlkT3BlcmF0aW9uRXhjZXB0aW9uKCk7CiBhd2FpdCB1c2luZyB2YXIgY29ubmVjdGlvbj1uZXcgTXlTcWxDb25uZWN0aW9uKHJhdyk7YXdhaXQgY29ubmVjdGlvbi5PcGVuQXN5bmMoKTsKIGFzeW5jIFRhc2sgUHJpbnQoc3RyaW5nIGxhYmVsLHN0cmluZyBzcWwpe2F3YWl0IHVzaW5nIHZhciBjb21tYW5kPW5ldyBNeVNxbENvbW1hbmQoc3FsLGNvbm5lY3Rpb24pO2F3YWl0IHVzaW5nIHZhciByZWFkZXI9YXdhaXQgY29tbWFuZC5FeGVjdXRlUmVhZGVyQXN5bmMoKTt3aGlsZShhd2FpdCByZWFkZXIuUmVhZEFzeW5jKCkpe3ZhciByb3c9bmV3IERpY3Rpb25hcnk8c3RyaW5nLG9iamVjdD8+KCk7Zm9yKGludCBpPTA7aTxyZWFkZXIuRmllbGRDb3VudDtpKyspcm93W3JlYWRlci5HZXROYW1lKGkpXT1yZWFkZXIuSXNEQk51bGwoaSk/bnVsbDpyZWFkZXIuR2V0VmFsdWUoaSk7Q29uc29sZS5Xcml0ZUxpbmUobGFiZWwrIjogIitKc29uU2VyaWFsaXplci5TZXJpYWxpemUocm93KSk7fX0KIGF3YWl0IFByaW50KCJFWElTVElORyBVU0VSIiwiU0VMRUNUIHUuaWQsdS5kaXNwbGF5X25hbWUsdS5lbWFpbCx1LmFjdGl2ZSx1Lndvcmtfc2NvcGUsKFNFTEVDVCBHUk9VUF9DT05DQVQoci5yb2xlX2NvZGUpIEZST00gdXNlcl9yb2xlcyByIFdIRVJFIHIudXNlcl9pZD11LmlkKSBBUyByb2xlcywoU0VMRUNUIEdST1VQX0NPTkNBVChjLm5hbWUgT1JERVIgQlkgYy5uYW1lKSBGUk9NIGNsaWVudF91c2VycyBjdSBKT0lOIGNsaWVudHMgYyBPTiBjLmlkPWN1LmNsaWVudF9pZCBXSEVSRSBjdS51c2VyX2lkPXUuaWQpIEFTIG1zcHMgRlJPTSB1c2VycyB1IE9SREVSIEJZIHUuZGlzcGxheV9uYW1lIik7CiBhd2FpdCBQcmludCgiTVNQIERPTUFJTiIsIlNFTEVDVCBpZCxuYW1lLHByaW1hcnlfZW1haWwsc3RhdHVzLG9yZ2FuaXphdGlvbl90eXBlIEZST00gY2xpZW50cyBPUkRFUiBCWSBuYW1lIik7CiBDb25zb2xlLldyaXRlTGluZSgiUkVBRC1PTkxZIFJPU1RFUiBJTlNQRUNUSU9OIENPTVBMRVRFLiIpO3JldHVybiAwOwp9Y2F0Y2goTXlTcWxFeGNlcHRpb24gZSl7Q29uc29sZS5FcnJvci5Xcml0ZUxpbmUoIlJvc3RlciByZXZpZXcgZmFpbGVkLiBNeVNRTCBlcnJvciAiK2UuTnVtYmVyKTtyZXR1cm4gMTt9Y2F0Y2goRXhjZXB0aW9uIGUpe0NvbnNvbGUuRXJyb3IuV3JpdGVMaW5lKCJSb3N0ZXIgcmV2aWV3IHN0b3BwZWQ6ICIrZS5HZXRUeXBlKCkuTmFtZSk7cmV0dXJuIDE7fQo='))
    subprocess.run(['dotnet','build',str(folder/'Inspect.csproj'),'--ignore-failed-sources','-p:NuGetAudit=false'],cwd=root,check=True)
    result=subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Inspect.dll')]+(['--apply'] if '--apply' in sys.argv else []),cwd=root,env=env)
    raise SystemExit(result.returncode)
