$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the read-only Change Management checker.'
& scp -o ConnectTimeout=10 (Join-Path $PSScriptRoot 'check-change-page.py') (Join-Path $PSScriptRoot 'check-change-page.cs') 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No application changes made.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'sudo python3 /home/Sahil/ASTRA-Backend-latest/local-maintenance/check-change-page.py --check'
if ($LASTEXITCODE -ne 0) { Write-Host 'The check found a failure or could not finish. Paste the PASS/FAIL and storage lines above.' }
else { Write-Host 'Check complete. Paste the PASS/FAIL and storage lines above.' }
