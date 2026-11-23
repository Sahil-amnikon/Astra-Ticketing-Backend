$Host.UI.RawUI.WindowTitle = 'ASTRA SSO account check - Ubuntu sign in'
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'inspect-sahil-sso.py'
$logDirectory = Join-Path $PSScriptRoot 'logs'
[IO.Directory]::CreateDirectory($logDirectory) | Out-Null
$log = Join-Path $logDirectory 'sahil-sso-inspection.log'
$status = Join-Path $logDirectory 'sahil-sso-inspection-status.txt'
Remove-Item -LiteralPath $status -ErrorAction SilentlyContinue
$ErrorActionPreference = 'Continue'
Write-Host 'Enter the Ubuntu VM password to copy the read-only account checker.'
& scp $source 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/inspect-sahil-sso.py' 2>&1 | Tee-Object -FilePath $log
$result = $LASTEXITCODE
if ($LASTEXITCODE -eq 0) {
    Write-Host 'Enter the VM password again to connect, then at the sudo prompt. No database changes will be made.'
    & ssh -t Sahil@172.31.3.13 'sudo python3 /home/Sahil/ASTRA-Backend-latest/local-maintenance/inspect-sahil-sso.py' 2>&1 | Tee-Object -FilePath $log -Append
    $result = $LASTEXITCODE
}
[IO.File]::WriteAllText($status, [string]$result)
Read-Host 'Press Enter to close after reviewing the result'
