$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the technician-template rollback.'
$files = @('rollback-technician-change-templates.mjs','verify-role-startup.py','deploy-template-rollback.sh') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No rollback applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/rollback-technician-change-templates.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-template-rollback.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Template rollback did not complete. Paste the result.' }
Write-Host 'Rollback completed. Refresh ASTRA with Ctrl+F5.'
