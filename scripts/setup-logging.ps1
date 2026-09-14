param([string]$Kubeconfig = "$PSScriptRoot/../.local/kubeconfig", [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
function Run([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed: $LASTEXITCODE" }
}
function Kube([string[]]$Arguments) { Run kubectl (@('--kubeconfig',$Kubeconfig) + $Arguments) }
$kindPath = if (Get-Command kind -ErrorAction SilentlyContinue) { 'kind' } else { '.tools/kind.exe' }
$helmPath = if (Get-Command helm -ErrorAction SilentlyContinue) { 'helm' } else { '.tools/helm/windows-amd64/helm.exe' }
if (-not $SkipBuild) { Run docker @('build','-t','vafomenko/blt-users-api:6.0.0','.') }
Run $kindPath @('load','docker-image','vafomenko/blt-users-api:6.0.0','--name','otus-hw5')
Kube @('apply','-k','logging')
Kube @('rollout','status','statefulset/elasticsearch','-n','logging','--timeout=600s')
Kube @('rollout','status','deployment/kibana','-n','logging','--timeout=600s')
Kube @('rollout','status','daemonset/fluent-bit','-n','logging','--timeout=300s')
Kube @('apply','-f','infra/postgresql.yaml')
Kube @('rollout','status','statefulset/users-db','-n','blt','--timeout=300s')
Run $helmPath @('--kubeconfig',$Kubeconfig,'upgrade','--install','blt-users-api','./helm/blt-users-api','-n','blt','--create-namespace','-f','infra/homework-values.yaml','--set','image.tag=6.0.0','--wait','--timeout','5m')
Write-Output "kubectl --kubeconfig $Kubeconfig -n logging port-forward svc/kibana-service 5601:5601"
