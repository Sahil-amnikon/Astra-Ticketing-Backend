from pathlib import Path
import base64, json, os, re, subprocess, tempfile, xml.etree.ElementTree as ET
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
accounts=json.loads(account_path.read_text()) if account_path.is_file() else []
matching=[a for a in accounts if a.get('Login','').lower()=='amin@geometry.com']
print('READ-ONLY: inspecting amin@geometry.com only.',flush=True)
print('Login-store records for this email: '+str(len(matching)),flush=True)
if matching:
    env['ASTRA_INSPECT_ACCOUNT_ID']=matching[0]['UserId']
    print('Login-store display name: '+matching[0].get('DisplayName',''),flush=True)
version=next(e.get('Version') for e in ET.parse(root/'Astra.Api.csproj').iter('PackageReference') if e.get('Include')=='MySqlConnector')
if not re.fullmatch(r'\d+(?:\.\d+){1,3}',version):raise SystemExit('STOP: unexpected driver version.')
project='<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><PackageReference Include="MySqlConnector" Version="'+version+'" /></ItemGroup></Project>'
os.umask(0o077)
with tempfile.TemporaryDirectory(prefix='.account-inspection-',dir=root/'local-maintenance') as directory:
    folder=Path(directory)
    (folder/'Inspect.csproj').write_text(project)
    (folder/'Program.cs').write_bytes(base64.b64decode('dXNpbmcgTXlTcWxDb25uZWN0b3I7CnVzaW5nIFN5c3RlbS5UZXh0Lkpzb247CnRyeSB7CiAgICB2YXIgcmF3ID0gRW52aXJvbm1lbnQuR2V0RW52aXJvbm1lbnRWYXJpYWJsZXMoKS5DYXN0PFN5c3RlbS5Db2xsZWN0aW9ucy5EaWN0aW9uYXJ5RW50cnk+KCkKICAgICAgICAuRmlyc3RPckRlZmF1bHQoZT0+c3RyaW5nLkVxdWFscyhlLktleS5Ub1N0cmluZygpPy5SZXBsYWNlKCJfXyIsICI6IiksIkNvbm5lY3Rpb25TdHJpbmdzOk15U3FsIixTdHJpbmdDb21wYXJpc29uLk9yZGluYWxJZ25vcmVDYXNlKSkuVmFsdWU/LlRvU3RyaW5nKCk7CiAgICBpZihzdHJpbmcuSXNOdWxsT3JXaGl0ZVNwYWNlKHJhdykpIHRocm93IG5ldyBJbnZhbGlkT3BlcmF0aW9uRXhjZXB0aW9uKCJCYWNrZW5kIGRhdGFiYXNlIHNldHRpbmcgaXMgbWlzc2luZy4iKTsKICAgIHZhciB0YXJnZXQ9bmV3IE15U3FsQ29ubmVjdGlvblN0cmluZ0J1aWxkZXIocmF3KTsKICAgIGlmKHRhcmdldC5TZXJ2ZXIhPSIxNzIuMzEuNC41IiB8fCB0YXJnZXQuRGF0YWJhc2UhPSJhc3RyYV90aWNrZXRpbmciIHx8IHRhcmdldC5Qb3J0IT0zMzA2KSB0aHJvdyBuZXcgSW52YWxpZE9wZXJhdGlvbkV4Y2VwdGlvbigiVW5leHBlY3RlZCBkYXRhYmFzZSB0YXJnZXQuIik7CiAgICBhd2FpdCB1c2luZyB2YXIgY29ubmVjdGlvbj1uZXcgTXlTcWxDb25uZWN0aW9uKHJhdyk7CiAgICBhd2FpdCBjb25uZWN0aW9uLk9wZW5Bc3luYygpOwogICAgdmFyIHNxbD0iU0VMRUNUIHUuaWQsdS5kaXNwbGF5X25hbWUsdS5lbWFpbCx1LmFjdGl2ZSwoU0VMRUNUIEdST1VQX0NPTkNBVChyLnJvbGVfY29kZSBPUkRFUiBCWSByLnJvbGVfY29kZSkgRlJPTSB1c2VyX3JvbGVzIHIgV0hFUkUgci51c2VyX2lkPXUuaWQpIEFTIHJvbGVzIEZST00gdXNlcnMgdSBXSEVSRSBMT1dFUih1LmVtYWlsKT1AZW1haWwgT1IgdS5pZD1AaWQiOwogICAgdmFyIGlkcz1uZXcgTGlzdDxzdHJpbmc+KCk7CiAgICBhd2FpdCB1c2luZyh2YXIgY29tbWFuZD1uZXcgTXlTcWxDb21tYW5kKHNxbCxjb25uZWN0aW9uKSkgewogICAgICAgIGNvbW1hbmQuUGFyYW1ldGVycy5BZGRXaXRoVmFsdWUoIkBlbWFpbCIsImFtaW5AZ2VvbWV0cnkuY29tIik7CiAgICAgICAgY29tbWFuZC5QYXJhbWV0ZXJzLkFkZFdpdGhWYWx1ZSgiQGlkIixFbnZpcm9ubWVudC5HZXRFbnZpcm9ubWVudFZhcmlhYmxlKCJBU1RSQV9JTlNQRUNUX0FDQ09VTlRfSUQiKT8/IiIpOwogICAgICAgIGF3YWl0IHVzaW5nIHZhciByZWFkZXI9YXdhaXQgY29tbWFuZC5FeGVjdXRlUmVhZGVyQXN5bmMoKTsKICAgICAgICB3aGlsZShhd2FpdCByZWFkZXIuUmVhZEFzeW5jKCkpIHsKICAgICAgICAgICAgdmFyIHZhbHVlcz1uZXcgRGljdGlvbmFyeTxzdHJpbmcsb2JqZWN0Pz4oKTsKICAgICAgICAgICAgZm9yKHZhciBpPTA7aTxyZWFkZXIuRmllbGRDb3VudDtpKyspdmFsdWVzW3JlYWRlci5HZXROYW1lKGkpXT1yZWFkZXIuSXNEQk51bGwoaSk/bnVsbDpyZWFkZXIuR2V0VmFsdWUoaSk7CiAgICAgICAgICAgIGlkcy5BZGQocmVhZGVyLkdldFN0cmluZygwKSk7Q29uc29sZS5Xcml0ZUxpbmUoIkRBVEFCQVNFIEFDQ09VTlQ6ICIrSnNvblNlcmlhbGl6ZXIuU2VyaWFsaXplKHZhbHVlcykpOwogICAgICAgIH0KICAgIH0KICAgIGlmKGlkcy5Db3VudD09MClDb25zb2xlLldyaXRlTGluZSgiREFUQUJBU0UgQUNDT1VOVDogbWlzc2luZyIpOwogICAgZm9yZWFjaCh2YXIgaWQgaW4gaWRzKSB7CiAgICAgICAgYXdhaXQgdXNpbmcgdmFyIGNvbW1hbmQ9bmV3IE15U3FsQ29tbWFuZCgiU0VMRUNUIGMubmFtZSBGUk9NIGNsaWVudF91c2VycyBjdSBKT0lOIGNsaWVudHMgYyBPTiBjLmlkPWN1LmNsaWVudF9pZCBXSEVSRSBjdS51c2VyX2lkPUBpZCBPUkRFUiBCWSBjLm5hbWUiLGNvbm5lY3Rpb24pOwogICAgICAgIGNvbW1hbmQuUGFyYW1ldGVycy5BZGRXaXRoVmFsdWUoIkBpZCIsaWQpOwogICAgICAgIGF3YWl0IHVzaW5nIHZhciByZWFkZXI9YXdhaXQgY29tbWFuZC5FeGVjdXRlUmVhZGVyQXN5bmMoKTt2YXIgbmFtZXM9bmV3IExpc3Q8c3RyaW5nPigpOwogICAgICAgIHdoaWxlKGF3YWl0IHJlYWRlci5SZWFkQXN5bmMoKSluYW1lcy5BZGQocmVhZGVyLkdldFN0cmluZygwKSk7CiAgICAgICAgQ29uc29sZS5Xcml0ZUxpbmUoIk1TUCBNRU1CRVJTSElQUzogIitKc29uU2VyaWFsaXplci5TZXJpYWxpemUobmFtZXMpKTsKICAgIH0KICAgIENvbnNvbGUuV3JpdGVMaW5lKCJSRUFELU9OTFkgQ0hFQ0sgQ09NUExFVEUuIE5vIGFjY291bnRzIG9yIGRhdGFiYXNlIHJlY29yZHMgY2hhbmdlZC4iKTtyZXR1cm4gMDsKfSBjYXRjaChNeVNxbEV4Y2VwdGlvbiBlcnJvcikge0NvbnNvbGUuRXJyb3IuV3JpdGVMaW5lKCJEYXRhYmFzZSBjaGVjayBmYWlsZWQuIE15U1FMIGVycm9yOiAiK2Vycm9yLk51bWJlcik7cmV0dXJuIDE7fQpjYXRjaChFeGNlcHRpb24gZXJyb3IpIHtDb25zb2xlLkVycm9yLldyaXRlTGluZSgiQWNjb3VudCBpbnNwZWN0aW9uIHN0b3BwZWQ6ICIrZXJyb3IuR2V0VHlwZSgpLk5hbWUpO3JldHVybiAxO30K'))
    subprocess.run(['dotnet','build',str(folder/'Inspect.csproj'),'--ignore-failed-sources','-p:NuGetAudit=false'],cwd=root,check=True)
    result=subprocess.run(['dotnet',str(folder/'bin/Debug/net10.0/Inspect.dll')],cwd=root,env=env)
    raise SystemExit(result.returncode)
