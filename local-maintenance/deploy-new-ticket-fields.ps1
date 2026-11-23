param([string]$UpdateScript = 'apply-new-ticket-fields.py')
$ErrorActionPreference = 'Stop'
if ($UpdateScript -notmatch '^apply-[a-z-]+\.py$') { throw 'Invalid update script name.' }
$Host.UI.RawUI.WindowTitle = if ($UpdateScript -eq 'apply-account-form.py') { 'ASTRA account form - Ubuntu sign in' } else { 'ASTRA new ticket fields - Ubuntu sign in' }
$helper = Join-Path $PSScriptRoot $UpdateScript
Write-Host 'Enter your Ubuntu VM password below. It will not appear while typing.'
$logDirectory = Join-Path $PSScriptRoot 'logs'
[IO.Directory]::CreateDirectory($logDirectory) | Out-Null
$updateName = [IO.Path]::GetFileNameWithoutExtension($UpdateScript)
$log = Join-Path $logDirectory ($updateName + '-deploy.log')
$ErrorActionPreference = 'Continue'
Get-Content -LiteralPath $helper -Raw -Encoding UTF8 | & ssh -o ConnectTimeout=10 Sahil@172.31.3.13 'python3 - --apply' 2>&1 | Tee-Object -FilePath $log
$result = $LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $logDirectory ($updateName + '-deploy-status.txt')), [string]$result)
Write-Host "Deployment exit code: $result"
Read-Host 'Press Enter to close this window'
exit $result
