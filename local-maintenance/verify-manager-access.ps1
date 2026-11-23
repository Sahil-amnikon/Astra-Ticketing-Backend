$ErrorActionPreference = 'Stop'
$backend = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $PSScriptRoot ('.manager-check-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
try {
  $api = [Security.SecurityElement]::Escape((Join-Path $backend 'bin/Debug/net10.0/Astra.Api.dll'))
  $mysql = [Security.SecurityElement]::Escape((Join-Path $backend 'bin/Debug/net10.0/MySqlConnector.dll'))
  @"
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App"/><Reference Include="Astra.Api"><HintPath>$api</HintPath></Reference><Reference Include="MySqlConnector"><HintPath>$mysql</HintPath></Reference></ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $scratch 'Check.csproj')
  '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath (Join-Path $scratch 'NuGet.Config')
  @'
using System.Reflection;
static void Check(bool value,string reason) {if(!value)throw new Exception(reason);}
Check(RoleAccess.HasFullAccess(["MANAGER"]),"Manager must have full access");
Check(RoleAccess.HasFullAccess(["ADMIN"]),"Administrator must keep full access");
foreach(var role in new[]{"TEAM_LEAD","TECHNICIAN","INTERN","USER"}) Check(!RoleAccess.HasFullAccess([role]),role+" must not gain full access");
Check(RoleAccess.WorkspaceRole(["MANAGER"])=="MANAGER","Manager label must stay Manager");
Check(RoleAccess.WorkspaceRole(["ADMIN"])=="ADMIN","Administrator label must stay Administrator");
string[] Policy(string method,string role)=>(string[])typeof(MySqlStore).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[role])!;
foreach(var method in new[]{"DefaultRolePolicy","AvailableRolePolicy"}) {
 var manager=Policy(method,"MANAGER");var admin=Policy(method,"ADMIN");
 Check(manager.Length==12 && manager.ToHashSet().SetEquals(admin),"Manager must receive the complete administrator permission set");
 Check(!Policy(method,"TEAM_LEAD").Contains("USERS_MANAGE"),"Team Lead permissions must remain restricted");
 Check(!Policy(method,"TECHNICIAN").Contains("USERS_MANAGE"),"Technician permissions must remain restricted");
}
var resolve=typeof(MySqlStore).GetMethod("ResolvePermissions",BindingFlags.Static|BindingFlags.NonPublic)!;
var result=await (Task<string[]>)resolve.Invoke(null,[null!,"test","MANAGER",true,Array.Empty<string>(),CancellationToken.None])!;
Check(result.SequenceEqual(new[]{"*"}),"Manager must receive wildcard access without a database call");
Console.WriteLine("PASS: full Manager policies, wildcard permissions, preserved role labels and unchanged lower-role access. No database accessed.");
'@ | Set-Content -LiteralPath (Join-Path $scratch 'Program.cs')
  & dotnet build (Join-Path $scratch 'Check.csproj') --configfile (Join-Path $scratch 'NuGet.Config') -p:NuGetAudit=false --nologo -v:q
  if ($LASTEXITCODE -ne 0) {throw 'Manager policy test build failed.'}
  & dotnet (Join-Path $scratch 'bin/Debug/net10.0/Check.dll')
  if ($LASTEXITCODE -ne 0) {throw 'Manager policy tests failed.'}
} finally {
  $resolved = [IO.Path]::GetFullPath($scratch)
  $allowed = [IO.Path]::GetFullPath($PSScriptRoot) + [IO.Path]::DirectorySeparatorChar
  if (!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolved -Leaf).StartsWith('.manager-check-')) {throw 'Unexpected test directory.'}
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
