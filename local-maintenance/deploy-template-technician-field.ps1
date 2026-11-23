$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the template Technician field update.'
$files = @('apply-template-technician-field.mjs','deploy-change-owner-name.sh') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No template field update applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-template-technician-field.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-change-owner-name.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Template Technician deployment did not complete. Paste the result.' }
Write-Host 'DEPLOYED: template Technician uses the signed-in name. Refresh ASTRA with Ctrl+F5.'
