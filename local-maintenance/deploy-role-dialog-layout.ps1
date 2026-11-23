$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the Add role layout update.'
& scp -o ConnectTimeout=10 (Join-Path $PSScriptRoot 'apply-role-dialog-layout.mjs') (Join-Path $PSScriptRoot 'deploy-access-dropdowns.sh') 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. The layout update was not applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-role-dialog-layout.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-access-dropdowns.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Layout deployment did not complete. Paste the error text.' }
Write-Host 'Completed: centered Add role popup and Add role button in the heading card. Refresh with Ctrl+F5.'
