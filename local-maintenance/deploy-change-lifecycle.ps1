$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the Change Management workflow update.'
$files = @('apply-review-notification-keys.mjs','apply-change-lifecycle.mjs','verify-role-startup.py','verify-submission-startup.py','verify-change-lifecycle-startup.py','verify-change-lifecycle-deploy.py','deploy-change-lifecycle.sh') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No workflow update applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'set -e; bash -n /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-change-lifecycle.sh; python3 /home/Sahil/ASTRA-Backend-latest/local-maintenance/verify-change-lifecycle-deploy.py; if ! grep -q ReviewNotificationInsertSql /home/Sahil/ASTRA-Backend-latest/FeatureSubmissionReviews.cs; then node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-review-notification-keys.mjs --apply; fi; node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-change-lifecycle.mjs --apply; sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-change-lifecycle.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Change Management workflow deployment did not complete. Paste the result.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5. Submit > Review CM approval > Start work > Mark completed > Close change.'
