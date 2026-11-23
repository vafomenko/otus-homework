param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoPath
New-Item -ItemType Directory -Force .tools,.local | Out-Null

function Run([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE" }
}

$kindPath = if (Get-Command kind -ErrorAction SilentlyContinue) { 'kind' } else { "$repoPath/.tools/kind.exe" }
$helmPath = if (Get-Command helm -ErrorAction SilentlyContinue) { 'helm' } else { "$repoPath/.tools/helm/windows-amd64/helm.exe" }
$ProgressPreference = 'SilentlyContinue'
if (-not (Get-Command $kindPath -ErrorAction SilentlyContinue)) {
    Invoke-WebRequest https://kind.sigs.k8s.io/dl/v0.33.0/kind-windows-amd64 -OutFile $kindPath
}
if (-not (Get-Command $helmPath -ErrorAction SilentlyContinue)) {
    Invoke-WebRequest https://get.helm.sh/helm-v4.2.4-windows-amd64.zip -OutFile .tools/helm.zip
    Expand-Archive -LiteralPath .tools/helm.zip -DestinationPath .tools/helm -Force
}
$clusterName = 'otus-hw9'
$configPath = "$repoPath/.local/kubeconfig-saga"
$nodeImage = 'registry-1.docker.io/kindest/node:v1.34.11@sha256:44e222ee2132dab25ff87301682f89eb82c7880ea3a1bf543bfe9708fd08d67d'
$clusters = & $kindPath get clusters
if ($LASTEXITCODE -ne 0) { throw 'Docker Desktop must be running.' }
$networks = & docker network ls --format '{{.Name}}'
if ($networks -notcontains $clusterName) {
    Run docker @('network','create','--driver','bridge','--subnet','10.208.9.0/24',$clusterName)
}
$previousNetwork = $env:KIND_EXPERIMENTAL_DOCKER_NETWORK
$env:KIND_EXPERIMENTAL_DOCKER_NETWORK = $clusterName
try {
if ($clusters -notcontains $clusterName) {
    Run $kindPath @('create','cluster','--name',$clusterName,'--image',$nodeImage,'--config','infra/kind-saga.yaml','--kubeconfig',$configPath,'--wait','180s')
} else {
    Run $kindPath @('export','kubeconfig','--name',$clusterName,'--kubeconfig',$configPath)
}
} finally { $env:KIND_EXPERIMENTAL_DOCKER_NETWORK = $previousNetwork }
function Kube([string[]]$Arguments) { Run kubectl (@('--kubeconfig',$configPath) + $Arguments) }
function Helm([string[]]$Arguments) { Run $helmPath (@('--kubeconfig',$configPath) + $Arguments) }

if (-not $SkipBuild) {
    Run docker @('build','--platform','linux/amd64','--tag','vafomenko/blt-users-api:9.0.0','.')
    foreach ($service in @('Billing','Orders','Notifications','Warehouse','Delivery')) {
        $image = 'vafomenko/blt-' + $service.ToLowerInvariant() + '-api:9.0.0'
        Run docker @('build','--platform','linux/amd64','--file','Dockerfile.streaming','--build-arg',"SERVICE=$service",'--tag',$image,'.')
    }
}
foreach ($service in @('users','billing','orders','notifications','warehouse','delivery')) {
    Run $kindPath @('load','docker-image',"vafomenko/blt-$service-api`:9.0.0",'--name',$clusterName)
}
$ingressChartPath = "$repoPath/.tools/ingress-nginx-4.15.1.tgz"
if (-not (Test-Path -LiteralPath $ingressChartPath)) {
    Invoke-WebRequest https://github.com/kubernetes/ingress-nginx/releases/download/helm-chart-4.15.1/ingress-nginx-4.15.1.tgz `
        -OutFile $ingressChartPath -MaximumRetryCount 3 -RetryIntervalSec 2
}
Helm @('upgrade','--install','ingress-nginx',$ingressChartPath,'--namespace','ingress-nginx','--create-namespace','--values','infra/nginx-ingress.yaml','--wait','--timeout','5m')
Helm @('upgrade','--install','blt-saga','./helm/blt-saga','--namespace','blt','--create-namespace','--wait','--wait-for-jobs','--timeout','10m')
foreach ($service in @('users','billing','orders','notifications','warehouse','delivery')) {
    Kube @('rollout','restart',"deployment/blt-saga-$service",'-n','blt')
    Kube @('rollout','status',"deployment/blt-saga-$service",'-n','blt','--timeout=180s')
}
Write-Output 'API: http://127.0.0.1:8099 (Host: arch.homework)'
Write-Output 'Application, PostgreSQL and Kafka namespace: blt; ingress namespace: ingress-nginx.'
Write-Output 'Run: ./scripts/test-saga.ps1 -BaseUrl http://127.0.0.1:8099'
