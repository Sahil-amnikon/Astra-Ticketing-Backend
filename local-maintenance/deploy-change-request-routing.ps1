$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the organization and approval routing update.'
$files = @('apply-change-request-routing.mjs','change-request-routing-storage.cs','migrate-change-request-routing.py','verify-role-startup.py','verify-submission-startup.py','verify-change-lifecycle-startup.py','verify-change-request-routing-startup.py','verify-change-request-routing-deploy.py','deploy-change-request-routing.sh') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No request routing update applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'set -e; bash -n /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-change-request-routing.sh; python3 /home/Sahil/ASTRA-Backend-latest/local-maintenance/verify-change-request-routing-deploy.py; node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-change-request-routing.mjs --apply; sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-change-request-routing.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Change request routing deployment did not complete. Paste the result.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5. Standard can self-approve; Normal needs its selected approver only.'
