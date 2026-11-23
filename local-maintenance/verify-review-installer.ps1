param([string]$MigrationName = 'migrate-submission-reviews.py')
$ErrorActionPreference = 'Stop'
$backend = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path $PSScriptRoot ('.review-installer-check-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
try {
  $mysql = [Security.SecurityElement]::Escape((Join-Path $backend 'bin/Debug/net10.0/MySqlConnector.dll'))
  "<Project Sdk=`"Microsoft.NET.Sdk`"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><FrameworkReference Include=`"Microsoft.AspNetCore.App`"/><Reference Include=`"MySqlConnector`"><HintPath>$mysql</HintPath></Reference></ItemGroup></Project>" | Set-Content -LiteralPath (Join-Path $scratch 'Check.csproj')
  '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath (Join-Path $scratch 'NuGet.Config')
  $migration = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $MigrationName))
  if ($MigrationName.EndsWith('.cs')) {
    $migration | Set-Content -LiteralPath (Join-Path $scratch 'Program.cs')
  } else {
    $source = [regex]::Match($migration, "(?s)source = r'''\r?\n(.*?)\r?\n'''")
    if (!$source.Success) { throw 'Migration program was not found.' }
    $source.Groups[1].Value | Set-Content -LiteralPath (Join-Path $scratch 'Program.cs')
  }
  & dotnet build (Join-Path $scratch 'Check.csproj') --configfile (Join-Path $scratch 'NuGet.Config') -p:NuGetAudit=false --nologo -v:q
  if ($LASTEXITCODE -ne 0) { throw 'Migration compiler check failed.' }
  if ($MigrationName -eq 'change-request-routing-storage.cs') {
    & dotnet (Join-Path $scratch 'bin/Debug/net10.0/Check.dll') --check-routing-storage (Join-Path $scratch 'routing-storage.sql')
    if ($LASTEXITCODE -ne 0) { throw 'Request routing storage check failed.' }
    & node (Join-Path $PSScriptRoot 'verify-change-routing-storage.mjs') (Join-Path $scratch 'routing-storage.sql')
    if ($LASTEXITCODE -ne 0) { throw 'Request routing schema regression failed.' }
  }
  if ($MigrationName -eq 'change-form-template-storage.cs') {
    & dotnet (Join-Path $scratch 'bin/Debug/net10.0/Check.dll') --check-template-storage
    if ($LASTEXITCODE -ne 0) { throw 'Full template storage check failed.' }
  }
  if ($MigrationName -eq 'change-template-review-migration.cs') {
    & dotnet (Join-Path $scratch 'bin/Debug/net10.0/Check.dll') --check-template-storage (Join-Path $scratch 'template-storage.sql')
    if ($LASTEXITCODE -ne 0) { throw 'Template review storage check failed.' }
    & node (Join-Path $PSScriptRoot 'verify-template-review-storage.mjs') (Join-Path $scratch 'template-storage.sql')
    if ($LASTEXITCODE -ne 0) { throw 'Template review schema regression check failed.' }
  }
  Write-Host 'PASS: migration program compiles. It was not executed; no database was accessed.'
} finally {
  $resolved = [IO.Path]::GetFullPath($scratch)
  $allowed = [IO.Path]::GetFullPath($PSScriptRoot) + [IO.Path]::DirectorySeparatorChar
  if (!$resolved.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolved -Leaf).StartsWith('.review-installer-check-')) { throw 'Unexpected test directory.' }
  Remove-Item -LiteralPath $resolved -Recurse -Force
}
