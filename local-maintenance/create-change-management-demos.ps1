param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
Write-Host 'Enter your VM password to copy the four-change demo creator. No backup will be created.'
& scp -o ConnectTimeout=10 (Join-Path $PSScriptRoot 'create-change-management-demos.py') (Join-Path $PSScriptRoot 'create-change-management-demos.cs') 'Sahil@172.31.3.13:/home/Sahil/ASTRA-Backend-latest/local-maintenance/'
if ($LASTEXITCODE -ne 0) { throw 'Copy failed. No demo records were created.' }
Write-Host 'Enter the VM password to connect, then again for sudo when prompted.'
if ($CheckOnly) {
    & ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'sudo python3 /home/Sahil/ASTRA-Backend-latest/local-maintenance/create-change-management-demos.py --diagnose'
} else {
    & ssh -t -o ConnectTimeout=10 Sahil@172.31.3.13 'sudo python3 /home/Sahil/ASTRA-Backend-latest/local-maintenance/create-change-management-demos.py --apply'
}
if ($LASTEXITCODE -ne 0) { throw 'Demo setup did not finish. Paste the result; rerunning skips matching existing demos.' }
if ($CheckOnly) { Write-Host 'Read-only check complete. Paste the RULE and COLUMN lines above; no demo creation was attempted.' }
else { Write-Host 'Completed. Search for DEMO-CM-REVIEW-V1 in Change Management or Review CM.' }
