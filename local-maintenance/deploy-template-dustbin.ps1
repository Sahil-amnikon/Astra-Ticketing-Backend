$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the template dropdown update.'
$files = @('apply-change-form-templates.mjs','apply-change-template-deletion.mjs','apply-template-dustbin.mjs','change-form-template-storage.cs','migrate-change-form-templates.py','verify-role-startup.py','verify-submission-startup.py','deploy-change-form-templates.sh') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No dropdown update applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'set -e; if [ ! -f /home/Sahil/ASTRA-Backend-latest/FeatureChangePlanTemplates.cs ]; then node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-change-form-templates.mjs --apply; fi; if ! grep -q DeleteChangePlan /home/Sahil/ASTRA-Backend-latest/FeatureChangePlanTemplates.cs; then node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-change-template-deletion.mjs --apply; fi; node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-template-dustbin.mjs --apply; sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-change-form-templates.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Template dropdown deployment did not complete. Paste the result.' }
Write-Host 'DEPLOYED: template dustbin icons and deletion confirmation. Refresh ASTRA with Ctrl+F5.'
