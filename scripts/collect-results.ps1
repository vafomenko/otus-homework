$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoPath
$runInfo = Get-Content docs/results/k6-run.json -Raw | ConvertFrom-Json
if (-not $runInfo.finishedAt) { throw 'Wait until run-load.ps1 has completed.' }
# ConvertFrom-Json in PowerShell 7 may already return DateTime; avoid reparsing
# its culture-dependent string representation (07/09 vs 09/07).
$startSeconds = ([DateTimeOffset]$runInfo.startedAt).ToUnixTimeSeconds()
$endSeconds = ([DateTimeOffset]$runInfo.finishedAt).ToUnixTimeSeconds()
$verifyArgs = @('scripts/verify-monitoring.py','--start',"$startSeconds",'--end',"$endSeconds")
if ($runInfo.faults) { $verifyArgs += '--require-faults' }
& python @verifyArgs
if ($LASTEXITCODE -ne 0) { throw 'Monitoring verification failed.' }
node scripts/capture-grafana.cjs recovery
if ($LASTEXITCODE -ne 0) { throw 'Screenshot capture failed.' }
$sinceTime = ([DateTimeOffset]$runInfo.startedAt).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")
kubectl --kubeconfig .local/kubeconfig logs -n monitoring deployment/alert-receiver "--since-time=$sinceTime" > docs/results/alert-notifications.txt
if ($LASTEXITCODE -ne 0) { throw 'Cannot export webhook notifications.' }
kubectl --kubeconfig .local/kubeconfig get pods -A -o json > docs/results/cluster-pods.json
if ($LASTEXITCODE -ne 0) { throw 'Cannot export pod status.' }
Write-Output 'Results saved to docs/results and docs/screenshots.'
