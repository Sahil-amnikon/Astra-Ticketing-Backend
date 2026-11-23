$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the article and service page update. No backups or data copies will be created.'
$names = @('apply-knowledge-service-pages.mjs','deploy-knowledge-service-pages.sh','verify-knowledge-service-pages-deploy.py','verify-problem-ticket-actions-startup.py','verify-submission-startup.py','verify-role-startup.py','verify-change-lifecycle-startup.py','verify-change-request-routing-startup.py')
$files = $names | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No Article and service page deployment performed.' }
Write-Host 'Enter your VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'set -e; bash -n /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-knowledge-service-pages.sh; python3 /home/Sahil/ASTRA-Backend-latest/local-maintenance/verify-knowledge-service-pages-deploy.py; node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-knowledge-service-pages.mjs --apply; sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-knowledge-service-pages.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Article and service page deployment did not complete. Paste the result.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5.'
