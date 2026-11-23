$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the affected-ticket color update.'
$names = @('apply-problem-ticket-colors.mjs','deploy-problem-ticket-colors.sh')
$files = $names | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No deployment performed.' }
Write-Host 'Enter your VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'set -e; bash -n /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-problem-ticket-colors.sh; node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-problem-ticket-colors.mjs --apply; sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-problem-ticket-colors.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Affected-ticket color deployment did not complete. Paste the result.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5.'
