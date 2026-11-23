$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the custom-role update.'
$customRoleFiles = @('apply-custom-roles.mjs', 'migrate-custom-roles.py', 'custom-roles.sql', 'deploy-custom-roles.sh') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @customRoleFiles 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. The custom-role update was not applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 -o ServerAliveInterval=15 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-custom-roles.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-custom-roles.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Custom-role deployment did not finish. Paste the error text.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5, then open People & Permissions > Role Permissions > Add role.'
