$ErrorActionPreference = 'Stop'
throw 'Technician template creation was rolled back at your request. This installer is retired; no files or database records were changed.'
Write-Host 'Enter your VM password to copy the technician change-template update.'
$files = @('apply-technician-change-templates.mjs','change-template-review-migration.cs','migrate-technician-change-templates.py','verify-role-startup.py','deploy-technician-change-templates.sh') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No template update was applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-technician-change-templates.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-technician-change-templates.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Technician template deployment did not complete. Paste the result.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5 and open Change Management > Change templates.'
