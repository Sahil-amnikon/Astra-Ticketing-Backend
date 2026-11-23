$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the multiple-user ticket update.'
$files = @('apply-multiple-ticket-users.mjs','multiple-ticket-users.sql','migrate-multiple-ticket-users.py','verify-role-startup.py','deploy-multiple-ticket-users.sh') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No ticket update was applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-multiple-ticket-users.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-multiple-ticket-users.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Multiple-user ticket update did not complete. Paste the error text.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5.'
