$ErrorActionPreference = 'Stop'
$backend = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $PSScriptRoot ('.employee-user-check-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
try {
  $api = [Security.SecurityElement]::Escape((Join-Path $backend 'bin/Debug/net10.0/Astra.Api.dll'))
  $mysql = [Security.SecurityElement]::Escape((Join-Path $backend 'bin/Debug/net10.0/MySqlConnector.dll'))
  "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include=`"Microsoft.AspNetCore.App`"/><Reference Include=`"Astra.Api`"><HintPath>$api</HintPath></Reference><Reference Include=`"MySqlConnector`"><HintPath>$mysql</HintPath></Reference></ItemGroup></Project>" | Set-Content -LiteralPath (Join-Path $scratch 'Check.csproj')
  '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath (Join-Path $scratch 'NuGet.Config')
  @'
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
static object Call(string method, params object?[] args) => typeof(MySqlStore).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args)!;
static void Check(bool value,string message) { if(!value) throw new Exception(message); }
static void Reject(string method, params object?[] args) {
 try { Call(method,args); throw new Exception("Expected invalid role to be rejected"); }
 catch(TargetInvocationException ex) when(ex.InnerException is ArgumentException) { }
}
Check((string)Call("ValidateCustomRoleName","  Support Engineer  ")=="Support Engineer","Names must be trimmed");
foreach(var name in new string?[]{null,"","A","Administrator","administrator","TEAM_LEAD","Technician","Bad\nRole",new string('x',81)}) Reject("ValidateCustomRoleName",name);
var tech=(string[])Call("CustomRolePermissions","TECHNICIAN",new[]{"TICKETS_TEAM_VIEW","REPORTS_VIEW","REPORTS_VIEW"});
Check(tech.ToHashSet().SetEquals(new[]{"TICKETS_TEAM_VIEW","REPORTS_VIEW"}),"Custom permissions must be deduplicated");
Reject("CustomRolePermissions","TECHNICIAN",new[]{"USERS_MANAGE"});
Reject("CustomRolePermissions","TECHNICIAN",new[]{"TICKETS_ASSIGN"});
Reject("CustomRolePermissions","UNKNOWN",Array.Empty<string>());
Reject("CustomRolePermissions","TECHNICIAN",null);
Check(((string[])Call("CustomRolePermissions","TEAM_LEAD",new[]{"TICKETS_ASSIGN"})).SequenceEqual(new[]{"TICKETS_ASSIGN"}),"Lead-based profile must support assignment");
foreach(var role in new[]{"ADMIN","MANAGER"}) Check(((string[])Call("CustomRolePermissions",role,Array.Empty<string>())).Length==12,"Full-access base profiles remain fixed");
foreach(var role in new[]{"USER","INTERN"}) Check(((string[])Call("CustomRolePermissions",role,Array.Empty<string>())).Length==0,"Requester and intern profiles cannot gain staff capabilities");
Check((string)Call("RoleDeletionId","custom-11111111-1111-4111-8111-111111111111",1)=="11111111-1111-4111-8111-111111111111","Deletion must resolve only the selected custom profile");
foreach(var code in new[]{"USER","INTERN","TECHNICIAN","TEAM_LEAD","MANAGER","ADMIN","custom-not-a-guid","11111111-1111-4111-8111-111111111111"}) Reject("RoleDeletionId",code,1);
Reject("RoleDeletionId","custom-11111111-1111-4111-8111-111111111111",0);
Reject("RoleDeletionId","custom-11111111-1111-4111-8111-111111111111",-1);
foreach(var email in new[]{"mahinoor@amnikontechnologies.com","hamza.rajmohammed@amnikontechnologies.com","user@amnikon.com","  USER@AMNIKONTECHNOLOGIES.COM  "}){
 Check((bool)Call("IsAmnikonEmployeeEmail",email),"Company email must be recognized");
 Check((bool)Call("IsEmployeeRequester","USER",email),"User employee must belong to Amnikon");
 foreach(var role in new[]{"ADMIN","MANAGER","TEAM_LEAD","TECHNICIAN","INTERN"})Check(!(bool)Call("IsEmployeeRequester",role,email),"Higher-access roles must not be converted to User");
}
foreach(var email in new string?[]{null,"","person@drinkpak.com","user@amnikontechnologies.com.evil.test","user@evilamnikontechnologies.com","name <user@amnikontechnologies.com>","user@dept.amnikontechnologies.com"})Check(!(bool)Call("IsAmnikonEmployeeEmail",email),"Unrelated addresses must not be treated as company employees");
var productionConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
 ["ConnectionStrings:MySql"]="invalid-on-purpose", ["ASTRA_SKIP_FEATURE_SCHEMA_STARTUP"]="false"
}).Build();
// A null store proves this branch returns before attempting any database work.
await new FeatureSchemaStartup(null!,productionConfig,new TestEnvironment {EnvironmentName="Production"}).StartAsync(CancellationToken.None);
var skipConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
 ["ConnectionStrings:MySql"]="invalid-on-purpose", ["ASTRA_SKIP_FEATURE_SCHEMA_STARTUP"]="true"
}).Build();
await new FeatureSchemaStartup(null!,skipConfig,new TestEnvironment {EnvironmentName="Development"}).StartAsync(CancellationToken.None);
Console.WriteLine("PASS: role permission validation, protected built-in roles, custom deletion IDs/revisions and schema-startup guards. No database accessed.");
sealed class TestEnvironment : IHostEnvironment {
 public string EnvironmentName {get;set;}="Production";
 public string ApplicationName {get;set;}="Test";
 public string ContentRootPath {get;set;}=".";
 public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
}
'@ | Set-Content -LiteralPath (Join-Path $scratch 'Program.cs')
  & dotnet build (Join-Path $scratch 'Check.csproj') --configfile (Join-Path $scratch 'NuGet.Config') -p:NuGetAudit=false --nologo -v:q
  if ($LASTEXITCODE -ne 0) { throw 'Custom role test build failed.' }
  & dotnet (Join-Path $scratch 'bin/Debug/net10.0/Check.dll')
  if ($LASTEXITCODE -ne 0) { throw 'Custom role tests failed.' }
} finally {
  $resolved = [IO.Path]::GetFullPath($scratch)
  $allowed = [IO.Path]::GetFullPath($PSScriptRoot) + [IO.Path]::DirectorySeparatorChar
  if (!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolved -Leaf).StartsWith('.employee-user-check-')) { throw 'Unexpected test directory.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
