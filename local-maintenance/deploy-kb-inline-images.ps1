$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the Article inline images fix.'
$files = @('apply-kb-inline-images.mjs','deploy-kb-inline-images.sh') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& scp -o ConnectTimeout=10 @files 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No article image update applied.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'node /home/Sahil/ASTRA-Backend-latest/local-maintenance/apply-kb-inline-images.mjs --apply && sudo bash /home/Sahil/ASTRA-Backend-latest/local-maintenance/deploy-kb-inline-images.sh --apply'
if ($LASTEXITCODE -ne 0) { throw 'Article inline images deployment did not complete. Paste the result.' }
Write-Host 'Completed. Refresh ASTRA with Ctrl+F5.'
