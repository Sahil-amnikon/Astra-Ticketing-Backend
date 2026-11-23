$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the Delete role feature.'
& scp -o ConnectTimeout=10 (Join-Path $PSScriptRoot 'apply-role-deletion.mjs') (Join-Path $PSScriptRoot 'deploy-role-deletion.sh') (Join-Path $PSScriptRoot 'verify-role-startup.py') 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. The Delete role feature was not applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 -o ServerAliveInterval=15 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-role-deletion.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-role-deletion.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Delete role deployment did not finish. Paste the error text.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5. Select an unused custom role to enable Delete role.'
