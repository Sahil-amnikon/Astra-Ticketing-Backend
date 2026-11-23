$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the HTTPS sign-in correction.'
& scp -o ConnectTimeout=10 (Join-Path $PSScriptRoot 'apply-proxy-https.mjs') (Join-Path $PSScriptRoot 'deploy-proxy-https.sh') 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. The HTTPS correction was not applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 -o ServerAliveInterval=15 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-proxy-https.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-proxy-https.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'HTTPS deployment did not complete. Paste the error text.' }
Write-Host 'Completed. Open https://astra.amnikontechnologies.com and start a new Microsoft sign-in.'
