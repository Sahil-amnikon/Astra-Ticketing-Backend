$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the six-ticket/two-problem demo creator. No backup will be created.'
& scp -o ConnectTimeout=10 (Join-Path $PSScriptRoot 'create-linked-problem-demos.py') (Join-Path $PSScriptRoot 'create-linked-problem-demos.cs') 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No demo records were created.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
& ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'sudo python3 /home/Sahil/ASTRA-Backend-latest/local-maintenance/create-linked-problem-demos.py --apply'
if ($LASTEXITCODE -ne 0) { throw 'Demo setup did not finish. Paste the result; rerunning skips matching existing demos.' }
Write-Host 'Completed. Search for DEMO-PM-COMMON-FIX-V1 in Tickets and Problem Management.'
