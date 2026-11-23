param([string]$BaseUrl = 'http://arch.homework')
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoPath
New-Item -ItemType Directory -Force .local/saga-results | Out-Null
& npx --yes newman@6.2.1 run postman/homework-9.postman_collection.json `
    --environment postman/homework-9.postman_environment.json `
    --env-var "baseUrl=$BaseUrl" --reporters cli,json --reporter-json-export .local/saga-results/newman.json --color off `
    --timeout 180000 --timeout-request 30000 --delay-request 200 --bail |
    Tee-Object -FilePath .local/saga-results/newman-output.txt
if ($LASTEXITCODE -ne 0) { throw "Newman failed with exit code $LASTEXITCODE" }
