$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
foreach ($command in @('dotnet', 'node', 'npm')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw ('Install the required development tool: ' + $command) }
}
Push-Location $sourceRoot
try {
    & dotnet restore Astra.sln
    if ($LASTEXITCODE -ne 0) { throw '.NET restore failed.' }
    & dotnet build Astra.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw '.NET build failed.' }
    & npm ci
    if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
    & npm run typecheck:web
    if ($LASTEXITCODE -ne 0) { throw 'Frontend TypeScript validation failed.' }
    & npm run build:web
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
} finally { Pop-Location }
Write-Host 'Migration source builds. This candidate does not implement every application endpoint.'
