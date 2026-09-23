param([string]$BaseUrl = 'http://arch.homework')
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoPath
New-Item -ItemType Directory -Force .local/auth-results | Out-Null

# The collection prints request/response details and masks passwords and tokens.
# Do not add --reporter-cli-no-console, or the HTTP details will be hidden.
& npx --yes newman@6.2.1 run postman/homework-7.postman_collection.json `
    --environment postman/homework-7.postman_environment.json `
    --env-var "baseUrl=$BaseUrl" --reporters cli --color off `
    --timeout-request 15000 --bail |
    Tee-Object -FilePath .local/auth-results/newman-output.txt
if ($LASTEXITCODE -ne 0) { throw "Newman failed with exit code $LASTEXITCODE" }
