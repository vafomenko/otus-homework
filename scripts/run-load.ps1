param([switch]$Smoke, [switch]$NoFaults)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoPath
New-Item -ItemType Directory -Force docs/results | Out-Null
docker image inspect registry-1.docker.io/grafana/k6:1.2.0 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    docker pull registry-1.docker.io/grafana/k6:1.2.0
    if ($LASTEXITCODE -ne 0) { throw 'Cannot download the k6 image.' }
}
$runId = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds().ToString()
$filePrefix = if ($Smoke) { 'smoke' } else { 'k6' }
$metadata = @{ startedAt = [DateTimeOffset]::UtcNow.ToString('o'); runId = $runId; smoke = [bool]$Smoke; faults = (-not $NoFaults) }
$metadata | ConvertTo-Json | Set-Content "docs/results/$filePrefix-run.json" -Encoding utf8
$loadArgs = @('run','--rm','--name','otus-hw5-load',
    '--mount',"type=bind,source=$repoPath/loadtests,target=/scripts,readonly",
    '--mount',"type=bind,source=$repoPath/docs/results,target=/results",
    '-e','BASE_URL=http://host.docker.internal:8085',
    '-e',"RUN_ID=$runId", '-e',"K6_SUMMARY_PATH=/results/$filePrefix-summary.json",
    '-e',"SMOKE=$($Smoke.ToString().ToLowerInvariant())",
    '-e',"FAULTS=$(((-not $NoFaults).ToString()).ToLowerInvariant())",
    'registry-1.docker.io/grafana/k6:1.2.0','run','/scripts/stress.js')
& docker @loadArgs 2>&1 | Tee-Object "docs/results/$filePrefix-output.txt"
$loadExit = $LASTEXITCODE
$metadata.finishedAt = [DateTimeOffset]::UtcNow.ToString('o')
$metadata.exitCode = $loadExit
$metadata | ConvertTo-Json | Set-Content "docs/results/$filePrefix-run.json" -Encoding utf8
if ($loadExit -ne 0) { throw "Load test failed with exit code $loadExit" }
