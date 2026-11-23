$ErrorActionPreference = 'Stop'
$backend = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $PSScriptRoot ('.role-endpoint-check-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
try {
  $api = [Security.SecurityElement]::Escape((Join-Path $backend 'bin/Debug/net10.0/Astra.Api.dll'))
  $mysql = [Security.SecurityElement]::Escape((Join-Path $backend 'bin/Debug/net10.0/MySqlConnector.dll'))
  "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include=`"Microsoft.AspNetCore.App`"/><Reference Include=`"Astra.Api`"><HintPath>$api</HintPath></Reference><Reference Include=`"MySqlConnector`"><HintPath>$mysql</HintPath></Reference></ItemGroup></Project>" | Set-Content -LiteralPath (Join-Path $scratch 'Check.csproj')
  '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath (Join-Path $scratch 'NuGet.Config')
  @'
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Production",ContentRootPath=AppContext.BaseDirectory});
builder.Logging.ClearProviders();
builder.Services.AddSingleton<MySqlStore>(_=>throw new Exception("The store must not be resolved in this test"));
await using var app=builder.Build();
var api=app.MapGroup("/api");
typeof(FeatureApis).GetMethod("MapRoleDeletion",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[api]);
var endpoints=((IEndpointRouteBuilder)app).DataSources.SelectMany(source=>source.Endpoints).OfType<RouteEndpoint>().ToArray();
if(endpoints.Length!=1||endpoints[0].RequestDelegate is null)throw new Exception("Delete role endpoint was not built");
Console.WriteLine("PASS: the actual DELETE role endpoint builds its request delegate successfully. No server, network or database started.");
'@ | Set-Content -LiteralPath (Join-Path $scratch 'Program.cs')
  & dotnet build (Join-Path $scratch 'Check.csproj') --configfile (Join-Path $scratch 'NuGet.Config') -p:NuGetAudit=false --nologo -v:q
  if ($LASTEXITCODE -ne 0) { throw 'Endpoint test build failed.' }
  & dotnet (Join-Path $scratch 'bin/Debug/net10.0/Check.dll')
  if ($LASTEXITCODE -ne 0) { throw 'Role endpoint registration failed.' }
} finally {
  $resolved = [IO.Path]::GetFullPath($scratch)
  $allowed = [IO.Path]::GetFullPath($PSScriptRoot) + [IO.Path]::DirectorySeparatorChar
  if (!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolved -Leaf).StartsWith('.role-endpoint-check-')) { throw 'Unexpected test directory.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
