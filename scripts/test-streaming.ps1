param([string]$BaseUrl = 'http://arch.homework')
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoPath
New-Item -ItemType Directory -Force .local/streaming-results | Out-Null
& npx --yes newman@6.2.1 run postman/homework-8.postman_collection.json `
    --environment postman/homework-8.postman_environment.json `
    --env-var "baseUrl=$BaseUrl" --reporters cli --color off `
    --timeout 180000 --timeout-request 15000 --delay-request 200 --bail |
    Tee-Object -FilePath .local/streaming-results/newman-output.txt
if ($LASTEXITCODE -ne 0) { throw "Newman failed with exit code $LASTEXITCODE" }
