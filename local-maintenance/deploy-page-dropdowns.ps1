$ErrorActionPreference = 'Stop'
$Host.UI.RawUI.WindowTitle = 'ASTRA page dropdowns - Ubuntu sign in'
$maintenancePath = $PSScriptRoot
Write-Host 'Enter your VM password to copy the dropdown update.'
& scp -o ConnectTimeout=10 (Join-Path $maintenancePath 'apply-friendly-access-layout.mjs') (Join-Path $maintenancePath 'deploy-access-dropdowns.sh') 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'The files were not copied. Ubuntu was not updated.' }
Write-Host 'Enter the VM password to connect, then again for sudo when asked.'
& ssh -t -o ConnectTimeout=10 -o ServerAliveInterval=15 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-friendly-access-layout.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-access-dropdowns.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Ubuntu update did not complete. Send the error text.' }
Write-Host 'Completed. Refresh ASTRA using Ctrl+F5.'
Read-Host 'Press Enter to close'
