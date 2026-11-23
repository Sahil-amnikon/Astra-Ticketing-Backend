$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the change dropdown update.'
$names = @('apply-change-dropdown-rows.mjs','deploy-change-dropdown-rows.sh')
$files = $names | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No deployment performed.' }
Write-Host 'Enter your VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'set -e; bash -n /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-change-dropdown-rows.sh; node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-change-dropdown-rows.mjs --apply; sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-change-dropdown-rows.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Change dropdown deployment did not complete. Paste the result.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5.'
