$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the full change-template update.'
$files = @('apply-change-form-templates.mjs','change-form-template-storage.cs','migrate-change-form-templates.py','verify-role-startup.py','verify-submission-startup.py','deploy-change-form-templates.sh') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No template update applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-change-form-templates.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-change-form-templates.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Full change-template deployment did not complete. Paste the result.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5. Open Raise a change > New template.'
