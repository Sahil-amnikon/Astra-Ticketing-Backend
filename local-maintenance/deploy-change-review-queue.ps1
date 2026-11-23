$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the Review CM update.'
$files = @('apply-change-review-source.mjs','apply-change-review-queue.mjs','rollback-technician-change-templates.mjs','verify-role-startup.py','verify-submission-startup.py','deploy-change-review-queue.sh') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No Review CM update applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-change-review-source.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-change-review-queue.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Review CM deployment did not complete. Paste the result.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5.'
