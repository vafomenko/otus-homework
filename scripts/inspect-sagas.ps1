param([string]$Kubeconfig = '.local/kubeconfig-saga')
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoPath
Get-Content -LiteralPath docs/saga/diagnostics.sql -Raw |
    & kubectl --kubeconfig $Kubeconfig -n blt exec -i blt-saga-orders-db-0 -- psql -X -v ON_ERROR_STOP=1 -U orders -d orders
if ($LASTEXITCODE -ne 0) { throw "Saga diagnostics failed with exit code $LASTEXITCODE" }
