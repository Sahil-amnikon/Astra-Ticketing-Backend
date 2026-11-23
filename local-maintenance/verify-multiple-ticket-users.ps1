$ErrorActionPreference = 'Stop'
$backend = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $PSScriptRoot ('.multiple-users-check-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
try {
  $api = [Security.SecurityElement]::Escape((Join-Path $backend 'bin/Debug/net10.0/Astra.Api.dll'))
  $mysql = [Security.SecurityElement]::Escape((Join-Path $backend 'bin/Debug/net10.0/MySqlConnector.dll'))
  "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include=`"Microsoft.AspNetCore.App`"/><Reference Include=`"Astra.Api`"><HintPath>$api</HintPath></Reference><Reference Include=`"MySqlConnector`"><HintPath>$mysql</HintPath></Reference></ItemGroup></Project>" | Set-Content -LiteralPath (Join-Path $scratch 'Check.csproj')
  '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath (Join-Path $scratch 'NuGet.Config')
  @'
using System.Reflection;
using System.Text.Json;
object? Call(string name,params object?[] values)=>typeof(MySqlStore).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,values);
var primary="11111111-2222-4333-8444-555555555555";
var extra="AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE";
var ids=(string[])Call("NormalizeAdditionalTicketUsers",primary,new[]{extra,extra.ToLowerInvariant(),primary})!;
if(ids.Length!=1||ids[0]!=extra.ToLowerInvariant())throw new Exception("Primary/duplicate requester normalization failed.");
if(((string[])Call("NormalizeAdditionalTicketUsers",primary,null)!).Length!=0)throw new Exception("Single-requester compatibility failed.");
foreach(var invalid in new[]{new[]{"not-a-contact-id"},Enumerable.Repeat(extra,50).ToArray()}){
 try{Call("NormalizeAdditionalTicketUsers",primary,invalid);throw new Exception("Invalid/over-limit request accepted.");}
 catch(TargetInvocationException e)when(e.InnerException is ArgumentException){}
}
foreach(var test in new[]{(false,true,true,true),(false,false,true,false),(false,true,false,false),(true,false,false,true)})
 if((bool)Call("MayAssignNewTicket",test.Item1,test.Item2,test.Item3)!!=test.Item4)throw new Exception("Self-assignment permission boundary failed.");
var active=(string)typeof(MySqlStore).GetProperty("ActiveTicketStaff",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
var scope=(string)Call("TicketAssigneeScope","false")!;
await File.WriteAllTextAsync(args[0],JsonSerializer.Serialize(new {active,scope}));
Console.WriteLine("PASS: multiple users normalize correctly, invalid and over-limit IDs are rejected, and self-assignment grants no permission to assign other staff.");
'@ | Set-Content -LiteralPath (Join-Path $scratch 'Program.cs')
  & dotnet build (Join-Path $scratch 'Check.csproj') --configfile (Join-Path $scratch 'NuGet.Config') -p:NuGetAudit=false --nologo -v:q
  if ($LASTEXITCODE -ne 0) { throw 'Multiple-user test build failed.' }
  & dotnet (Join-Path $scratch 'bin/Debug/net10.0/Check.dll') (Join-Path $scratch 'scope.json')
  if ($LASTEXITCODE -ne 0) { throw 'Multiple-user validation failed.' }
  & node (Join-Path $PSScriptRoot 'verify-ticket-user-scopes.mjs') (Join-Path $scratch 'scope.json')
  if ($LASTEXITCODE -ne 0) { throw 'Ticket user/client scope check failed.' }
} finally {
  $resolved = [IO.Path]::GetFullPath($scratch)
  $allowed = [IO.Path]::GetFullPath($PSScriptRoot) + [IO.Path]::DirectorySeparatorChar
  if (!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolved -Leaf).StartsWith('.multiple-users-check-')) { throw 'Unexpected test directory.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
