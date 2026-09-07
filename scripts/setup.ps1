param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoPath
New-Item -ItemType Directory -Force .tools,.local,.local/docker | Out-Null

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

$configPath = "$repoPath/.local/kubeconfig"
$nodeImage = 'registry-1.docker.io/kindest/node:v1.34.11@sha256:44e222ee2132dab25ff87301682f89eb82c7880ea3a1bf543bfe9708fd08d67d'
$clusters = & $kindPath get clusters
if ($LASTEXITCODE -ne 0) { throw 'Cannot list kind clusters. Is Docker Desktop running?' }
if ($clusters -notcontains 'otus-hw5') {
    Run $kindPath @('create','cluster','--name','otus-hw5','--image',$nodeImage,'--config','infra/kind.yaml','--kubeconfig',$configPath,'--wait','180s')
} else {
    Run $kindPath @('export','kubeconfig','--name','otus-hw5','--kubeconfig',$configPath)
}
# Every cluster command uses this private kubeconfig, never the user's default context.
function Kube([string[]]$Arguments) { Run kubectl (@('--kubeconfig',$configPath) + $Arguments) }
function Helm([string[]]$Arguments) { Run $helmPath (@('--kubeconfig',$configPath) + $Arguments) }

if (-not $SkipBuild) { Run docker @('build','--platform','linux/amd64','--tag','vafomenko/blt-users-api:5.0.0','.') }
Run $kindPath @('load','docker-image','vafomenko/blt-users-api:5.0.0','--name','otus-hw5')
Helm @('repo','add','ingress-nginx','https://kubernetes.github.io/ingress-nginx','--force-update')
Helm @('upgrade','--install','ingress-nginx','ingress-nginx/ingress-nginx','--version','4.15.1','--namespace','ingress-nginx','--create-namespace','--values','infra/nginx-ingress.yaml','--wait','--timeout','5m')
Kube @('apply','-f','infra/postgresql.yaml')
Kube @('rollout','status','statefulset/users-db','-n','blt','--timeout=300s')
Helm @('upgrade','--install','blt-users-api','./helm/blt-users-api','--namespace','blt','--values','infra/homework-values.yaml','--wait','--timeout','5m')
Kube @('apply','-k','monitoring')
foreach ($deploymentName in @('prometheus','grafana','alert-receiver')) {
    Kube @('rollout','status',"deployment/$deploymentName",'-n','monitoring','--timeout=300s')
}
Kube @('rollout','status','deployment/postgres-exporter','-n','blt','--timeout=300s')
Write-Output 'Grafana: http://localhost:3005/d/blt-users-overview (admin / homework5)'
Write-Output 'Prometheus: http://localhost:9095 | API: http://localhost:8085 (Host: arch.homework)'
