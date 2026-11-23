param([switch]$SkipBuild, [ValidateSet(9, 10)][int]$Homework = 9)
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
$clusterName = "otus-hw$Homework"
$profileName = if ($Homework -eq 10) { 'idempotency' } else { 'saga' }
$configPath = "$repoPath/.local/kubeconfig-$profileName"
$imageTag = "$Homework.0.0"
$nodeImage = 'registry-1.docker.io/kindest/node:v1.34.11@sha256:44e222ee2132dab25ff87301682f89eb82c7880ea3a1bf543bfe9708fd08d67d'
$clusters = & $kindPath get clusters
if ($LASTEXITCODE -ne 0) { throw 'Docker Desktop must be running.' }
$networks = & docker network ls --format '{{.Name}}'
if ($networks -notcontains $clusterName) {
    Run docker @('network','create','--driver','bridge','--subnet',"10.208.$Homework.0/24",$clusterName)
}
$previousNetwork = $env:KIND_EXPERIMENTAL_DOCKER_NETWORK
$env:KIND_EXPERIMENTAL_DOCKER_NETWORK = $clusterName
try {
if ($clusters -notcontains $clusterName) {
    Run $kindPath @('create','cluster','--name',$clusterName,'--image',$nodeImage,'--config',"infra/kind-$profileName.yaml",'--kubeconfig',$configPath,'--wait','180s')
} else {
    Run $kindPath @('export','kubeconfig','--name',$clusterName,'--kubeconfig',$configPath)
}
} finally { $env:KIND_EXPERIMENTAL_DOCKER_NETWORK = $previousNetwork }
function Kube([string[]]$Arguments) { Run kubectl (@('--kubeconfig',$configPath) + $Arguments) }
function Helm([string[]]$Arguments) { Run $helmPath (@('--kubeconfig',$configPath) + $Arguments) }

if (-not $SkipBuild) {
    Run docker @('build','--platform','linux/amd64','--tag',"vafomenko/blt-users-api:$imageTag",'.')
    foreach ($service in @('Billing','Orders','Notifications','Warehouse','Delivery')) {
        $image = 'vafomenko/blt-' + $service.ToLowerInvariant() + "-api:$imageTag"
        Run docker @('build','--platform','linux/amd64','--file','Dockerfile.streaming','--build-arg',"SERVICE=$service",'--tag',$image,'.')
    }
}
foreach ($service in @('users','billing','orders','notifications','warehouse','delivery')) {
    Run $kindPath @('load','docker-image',"vafomenko/blt-$service-api:$imageTag",'--name',$clusterName)
}
$ingressChartPath = "$repoPath/.tools/ingress-nginx-4.15.1.tgz"
if (-not (Test-Path -LiteralPath $ingressChartPath)) {
    Invoke-WebRequest https://github.com/kubernetes/ingress-nginx/releases/download/helm-chart-4.15.1/ingress-nginx-4.15.1.tgz `
        -OutFile $ingressChartPath -MaximumRetryCount 3 -RetryIntervalSec 2
}
Helm @('upgrade','--install','ingress-nginx',$ingressChartPath,'--namespace','ingress-nginx','--create-namespace','--values','infra/nginx-ingress.yaml','--wait','--timeout','5m')
Helm @('upgrade','--install','blt-saga','./helm/blt-saga','--namespace','blt','--create-namespace','--set-string',"image.tag=$imageTag",'--wait','--wait-for-jobs','--timeout','10m')
foreach ($service in @('users','billing','orders','notifications','warehouse','delivery')) {
    Kube @('rollout','restart',"deployment/blt-saga-$service",'-n','blt')
    Kube @('rollout','status',"deployment/blt-saga-$service",'-n','blt','--timeout=180s')
}
$ingressPort = if ($Homework -eq 10) { 8100 } else { 8099 }
Write-Output "API доступен по http://127.0.0.1:$ingressPort с заголовком Host: arch.homework."
Write-Output 'Приложение, PostgreSQL и Kafka установлены в namespace blt, ingress-контроллер в ingress-nginx.'
Write-Output "Проверить стенд можно командой ./scripts/test-$profileName.ps1 -BaseUrl http://127.0.0.1:$ingressPort"
