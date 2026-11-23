$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the Amnikon name update.'
& scp -o ConnectTimeout=10 (Join-Path $PSScriptRoot 'rename-amnikon.py') 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No database changes made.' }
Write-Host 'Enter the VM password to connect, and again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'sudo python3 /home/Sahil/ASTRA-Backend-latest/local-maintenance/rename-amnikon.py --apply'
if ($LASTEXITCODE -ne 0) { throw 'The name update did not complete. Send the error text.' }
Write-Host 'Refresh ASTRA with Ctrl+F5.'
