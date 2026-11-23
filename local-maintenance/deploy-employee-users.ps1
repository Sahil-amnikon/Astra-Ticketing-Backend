$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the User and Manager company setup.'
$employeeFiles = @('apply-employee-users.mjs', 'deploy-employee-users.sh', 'repair-employee-users.py', 'verify-role-startup.py') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @employeeFiles 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. The User employee update was not applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 -o ServerAliveInterval=15 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-employee-users.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-employee-users.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'User employee update did not finish. Paste the error text.' }
Write-Host 'Completed. Refresh with Ctrl+F5. Users and Managers belong to Amnikon. Manager full access is retained; affected User-profile employees should sign in again.'
