$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the problem ticket actions update. No backups or data copies will be created.'
$names = @('apply-problem-ticket-actions.mjs','deploy-problem-ticket-actions.sh','verify-problem-ticket-actions-deploy.py','verify-problem-ticket-actions-startup.py','verify-submission-startup.py','verify-role-startup.py','verify-change-lifecycle-startup.py','verify-change-request-routing-startup.py')
$files = $names | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No Problem ticket actions deployment performed.' }
Write-Host 'Enter your VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'set -e; bash -n /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-problem-ticket-actions.sh; python3 /home/Sahil/ASTRA-Backend-latest/local-maintenance/verify-problem-ticket-actions-deploy.py; node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-problem-ticket-actions.mjs --apply; sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-problem-ticket-actions.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Problem ticket actions deployment did not complete. Paste the result.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5.'
