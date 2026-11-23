$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to restore the template popup.'
$files = @('undo-template-popup-labels.mjs','deploy-change-owner-name.sh') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No popup changes applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/undo-template-popup-labels.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-change-owner-name.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Template popup restore did not complete. Paste the result.' }
Write-Host 'DEPLOYED: template popup restored. Refresh ASTRA with Ctrl+F5.'
