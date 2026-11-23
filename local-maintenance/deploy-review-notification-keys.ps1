$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the review notification fix.'
$files = @('apply-review-notification-keys.mjs','verify-role-startup.py','verify-submission-startup.py','deploy-review-notification-keys.sh') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No notification fix applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-review-notification-keys.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-review-notification-keys.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Review notification deployment did not complete. Paste the result.' }
Write-Host 'Completed. Retry submitting the change. No database schema changes or backups.'
