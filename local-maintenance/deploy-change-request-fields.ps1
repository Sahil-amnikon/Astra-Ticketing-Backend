$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the change form update.'
$files = @('apply-change-owner-name.mjs','apply-change-request-fields.mjs','deploy-change-owner-name.sh') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No change form update applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-change-owner-name.mjs --apply && node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-change-request-fields.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-change-owner-name.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Change form deployment did not complete. Paste the result.' }
Write-Host 'DEPLOYED: Requested by and Approved by above Title, with automatic Owner name. Refresh ASTRA with Ctrl+F5.'
