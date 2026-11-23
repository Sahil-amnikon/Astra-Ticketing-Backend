$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the technician review update.'
$names = @('apply-submission-reviews.mjs','submission-reviews.sql','migrate-submission-reviews.py','verify-submission-startup.py','verify-role-startup.py','deploy-submission-reviews.sh')
$files = @($names | ForEach-Object { Join-Path $PSScriptRoot $_ })
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. The review update was not applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-submission-reviews.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-submission-reviews.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Review deployment did not complete. Paste the error text.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5.'
