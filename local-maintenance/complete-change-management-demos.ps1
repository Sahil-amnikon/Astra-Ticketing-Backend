$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the review-notification fix and four-change demo setup. No backups or data copies will be created.'
$names = @('apply-review-notification-compat.mjs','deploy-review-notification-compat.sh','verify-review-notification-compat-deploy.py','verify-problem-ticket-actions-startup.py','verify-submission-startup.py','verify-role-startup.py','verify-review-notification-compat-startup.py','create-change-management-demos.py','create-change-management-demos.cs','verify-change-lifecycle-startup.py','verify-change-request-routing-startup.py')
$files = $names | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No Review notification fix and demo setup performed.' }
Write-Host 'Enter your VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'set -e; bash -n /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-review-notification-compat.sh; python3 /home/Sahil/ASTRA-Backend-latest/local-maintenance/verify-review-notification-compat-deploy.py; node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-review-notification-compat.mjs --apply; sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-review-notification-compat.sh --apply; sudo python3 /home/Sahil/ASTRA-Backend-latest/local-maintenance/create-change-management-demos.py --apply'
if ($LASTEXITCODE -ne 0) { throw 'Review notification fix and demo setup did not complete. Paste the result.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5 and open Review CM. The demo creator printed each CHG number above.'
