param([string]$UpdateScript = 'apply-roster-setup.py')
$ErrorActionPreference = 'Stop'
if ($UpdateScript -notmatch '^apply-[a-z-]+\.py$') { throw 'Invalid update script name.' }
$Host.UI.RawUI.WindowTitle = "ASTRA approved roster - Ubuntu sign in"
$helper = Join-Path $PSScriptRoot $UpdateScript
Write-Host 'Enter your Ubuntu VM password below. It will not appear while typing.'
$logDirectory = Join-Path $PSScriptRoot 'logs'
[IO.Directory]::CreateDirectory($logDirectory) | Out-Null
$updateName = [IO.Path]::GetFileNameWithoutExtension($UpdateScript)
$log = Join-Path $logDirectory ($updateName + '-deploy.log')
$ErrorActionPreference = 'Continue'
Get-Content -LiteralPath $helper -Raw -Encoding UTF8 | & ssh -o ConnectTimeout=10 Sahil@172.31.3.13 'python3 - --apply' 2>&1 | Tee-Object -FilePath $log
$result = $LASTEXITCODE
if ($result -eq 0) {
    Write-Host 'The update succeeded. Enter the VM password again to download your private login list.'
    $destination = Join-Path (Split-Path $PSScriptRoot -Parent) 'App_Data'
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    & scp -o ConnectTimeout=10 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/App_Data/staff-logins-20261112.csv' (Join-Path $destination 'staff-logins-20261112.csv')
    if ($LASTEXITCODE -ne 0) { Write-Host 'Accounts were updated, but the password list still needs downloading.' }
}
[IO.File]::WriteAllText((Join-Path $logDirectory ($updateName + '-deploy-status.txt')), [string]$result)
Write-Host "Deployment exit code: $result"
Read-Host 'Press Enter to close this window'
exit $result
