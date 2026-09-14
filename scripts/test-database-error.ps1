param([string]$Kubeconfig = "$PSScriptRoot/../.local/kubeconfig")
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force "$PSScriptRoot/../.local" | Out-Null
$podName = 'logging-db-error-' + [guid]::NewGuid().ToString('N').Substring(0,8)
function Kube([string[]]$Arguments) {
    $output = & kubectl --kubeconfig $Kubeconfig @Arguments
    if ($LASTEXITCODE -ne 0) { throw "kubectl failed: $LASTEXITCODE" }
    return $output
}
$forward = $null
try {
    Kube @('run',$podName,'-n','blt','--image=vafomenko/blt-users-api:6.0.0',
        '--image-pull-policy=IfNotPresent','--restart=Never','--env=Database__Host=127.0.0.1','--env=Database__Port=1')
    Kube @('wait','-n','blt','--for=condition=Ready',"pod/$podName",'--timeout=120s')
    $forward = Start-Process kubectl -ArgumentList @('--kubeconfig',('"' + $Kubeconfig + '"'),'-n','blt','port-forward',"pod/$podName",'18006:8000') `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput "$PSScriptRoot/../.local/db-error-forward.log" -RedirectStandardError "$PSScriptRoot/../.local/db-error-forward.err.log"
    $response = $null
    for ($attempt=0; $attempt -lt 30; $attempt++) {
        try { $response = Invoke-WebRequest http://localhost:18006/user -SkipHttpErrorCheck; break }
        catch { Start-Sleep -Seconds 1 }
    }
    if ($null -eq $response -or $response.StatusCode -ne 503) { throw 'Expected HTTP 503 from unavailable DB' }
    $outputPath = "$PSScriptRoot/../docs/results/logging"
    New-Item -ItemType Directory -Force $outputPath | Out-Null
    Kube @('logs','-n','blt',$podName) | Set-Content "$outputPath/database-error.jsonl"
    @{pod=$podName;status=[int]$response.StatusCode;checkedAt=(Get-Date).ToUniversalTime().ToString('o')} |
        ConvertTo-Json | Set-Content "$outputPath/database-check.json"
    # Allow the node collector to discover this short-lived container before deletion.
    Start-Sleep -Seconds 15
} finally {
    if ($forward -and -not $forward.HasExited) { Stop-Process -Id $forward.Id }
    Kube @('delete','pod','-n','blt',$podName,'--ignore-not-found','--wait=false')
}
