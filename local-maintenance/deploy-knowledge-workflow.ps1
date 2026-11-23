$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the Knowledge Base update. No backups or data copies will be created.'
$names = @('apply-knowledge-workflow.mjs','deploy-knowledge-workflow.sh','verify-knowledge-deploy.py','verify-knowledge-startup.py','verify-submission-startup.py','verify-role-startup.py','verify-change-lifecycle-startup.py','verify-change-request-routing-startup.py')
$files = $names | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No Knowledge Base deployment performed.' }
Write-Host 'Enter your VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'set -e; bash -n /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-knowledge-workflow.sh; python3 /home/Sahil/ASTRA-Backend-latest/local-maintenance/verify-knowledge-deploy.py; node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-knowledge-workflow.mjs --apply; sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-knowledge-workflow.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Knowledge Base deployment did not complete. Paste the result.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5.'
