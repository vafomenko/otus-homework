param(
    [string]$BaseUrl = 'http://127.0.0.1:8099',
    [string]$Kubeconfig = '.local/kubeconfig-saga'
)
$ErrorActionPreference = 'Stop'
$repoPath = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoPath

function Kube([string[]]$Arguments) {
    & kubectl --kubeconfig $Kubeconfig -n blt @Arguments
    if ($LASTEXITCODE -ne 0) { throw "kubectl failed with exit code $LASTEXITCODE" }
}
function Assert-Equal($Actual, $Expected, [string]$Message) {
    if ($Actual -ne $Expected) { throw "$Message (expected $Expected, got $Actual)" }
    Write-Output "PASS: $Message"
}
$requestHeaders = @{ Host = 'arch.homework' }
function Api([string]$Method, [string]$Path, $Body = $null) {
    $requestParameters = @{ Method=$Method; Uri=($BaseUrl+$Path); Headers=$requestHeaders; TimeoutSec=30 }
    if ($null -ne $Body) { $requestParameters.Body = $Body | ConvertTo-Json -Compress; $requestParameters.ContentType = 'application/json' }
    $readDeadline = [DateTimeOffset]::UtcNow.AddSeconds(20)
    do {
        try {
            $apiResponse = Invoke-RestMethod @requestParameters -StatusCodeVariable responseStatusCode
            break
        } catch {
            $failureStatus = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
            if ($Method -ne 'GET' -or $failureStatus -notin @(0,502,503,504) -or [DateTimeOffset]::UtcNow -ge $readDeadline) { throw }
            Start-Sleep -Milliseconds 500
        }
    } while ($true)
    $script:lastApiStatusCode = [int]$responseStatusCode
    if ($apiResponse -is [array]) { foreach ($element in $apiResponse) { Write-Output $element } }
    else { $apiResponse }
}
$runId = [Guid]::NewGuid().ToString('N')
$password = 'Hw9!' + $runId
$username = 'recovery_' + $runId
$null = Api POST /auth/register @{ username=$username; password=$password; firstName='Saga'; lastName='Recovery'; email=($username+'@example.com') }
$login = Api POST /auth/login @{ username=$username; password=$password }
$requestHeaders.Authorization = 'Bearer ' + $login.accessToken
$null = Api POST /billing/deposits @{ operationId=[Guid]::NewGuid().ToString(); amount=10000 }
$stockBefore = (Api GET /warehouse/products | Where-Object sku -eq 'SKU-1').available
$slot = Api GET /delivery/slots | Where-Object available -eq 1 | Select-Object -First 1
if (-not $slot) { throw 'No free demo courier slots.' }
$originalDeliveryReplicas = [int]((& kubectl --kubeconfig $Kubeconfig -n blt get deployment blt-saga-delivery -o json | ConvertFrom-Json).spec.replicas)
if ($LASTEXITCODE -ne 0 -or $originalDeliveryReplicas -lt 1) { throw 'Delivery must be running before this test.' }
try {
    Kube @('scale','deployment/blt-saga-delivery','--replicas=0')
    Kube @('wait','--for=delete','pod','-l','app=blt-saga-delivery','--timeout=60s')
    $requestHeaders['Idempotency-Key'] = [Guid]::NewGuid().ToString()
    $order = Api POST /orders @{ price=3000; sku='SKU-1'; quantity=2; slotId=$slot.id }
    Assert-Equal $script:lastApiStatusCode 202 'Pending compensation returns HTTP 202'
    Assert-Equal $order.status 'compensating' 'Unavailable Delivery leaves a durable pending compensation'
    Assert-Equal $order.step 'undo_delivery' 'Failed compensation step is persisted'
    $letters = @(Api GET ('/notifications?orderId='+$order.id))
    Assert-Equal $letters.Count 0 'No final notification before compensation finishes'
    Assert-Equal (Api GET /billing/account).balance 7000 'Payment is durably debited before rollback'
    Assert-Equal (Api GET /warehouse/products | Where-Object sku -eq 'SKU-1').available ($stockBefore-2) 'Stock is durably reserved before rollback'
    foreach ($participant in @('billing','warehouse')) {
        Kube @('rollout','restart',"deployment/blt-saga-$participant")
        Kube @('rollout','status',"deployment/blt-saga-$participant",'--timeout=120s')
    }
    Assert-Equal (Api GET /billing/account).balance 7000 'Billing reboot preserves the debit'
    Assert-Equal (Api GET ('/warehouse/reservations/'+$order.id)).status 'reserved' 'Warehouse reboot preserves the reservation'
    & ./scripts/inspect-sagas.ps1 -Kubeconfig $Kubeconfig
    $oldOrderPodNames = @((Kube @('get','pods','-l','app=blt-saga-orders','-o','json') | ConvertFrom-Json).items.metadata.name)
    Kube @('delete','pod','-l','app=blt-saga-orders','--grace-period=0','--force','--wait=false')
    $replacementDeadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
    do {
        $replacement = (Kube @('get','pods','-l','app=blt-saga-orders','-o','json') | ConvertFrom-Json).items |
            Where-Object { $_.metadata.name -notin $oldOrderPodNames } | Select-Object -First 1
        if ($replacement) { break }
        if ([DateTimeOffset]::UtcNow -gt $replacementDeadline) { throw 'Replacement Orders pod did not appear.' }
        Start-Sleep -Milliseconds 250
    } while ($true)
    Kube @('wait','--for=condition=Ready',('pod/'+$replacement.metadata.name),'--timeout=120s')
    Kube @('rollout','status','deployment/blt-saga-orders','--timeout=120s')
    Kube @('scale','deployment/blt-saga-delivery',"--replicas=$originalDeliveryReplicas")
    Kube @('rollout','status','deployment/blt-saga-delivery','--timeout=120s')
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        $result = Api GET ('/orders/'+$order.id)
        if ($result.status -eq 'rejected') { break }
        if ([DateTimeOffset]::UtcNow -gt $deadline) { throw 'Recovery did not finish within 60 seconds.' }
        Start-Sleep -Milliseconds 500
    } while ($true)
    Assert-Equal $result.step 'done' 'New Orders process finishes the persisted saga'
    Assert-Equal $result.reason 'delivery_unavailable' 'Transport failure reason is retained'
    Assert-Equal (Api GET /billing/account).balance 10000 'Full payment is refunded'
    Assert-Equal (Api GET /warehouse/products | Where-Object sku -eq 'SKU-1').available $stockBefore 'All stock is restored'
    Assert-Equal (Api GET ('/warehouse/reservations/'+$order.id)).status 'released' 'Warehouse reservation is released'
    Assert-Equal (Api GET ('/delivery/reservations/'+$order.id)).status 'released' 'Delivery cancellation is fenced'
    $payment = Api GET ('/billing/payments/'+$order.id)
    Assert-Equal $payment.refundedAmount 3000 'Payment is refunded exactly once'
    $retry = Api POST /orders @{ price=3000; sku='SKU-1'; quantity=2; slotId=$slot.id }
    Assert-Equal $script:lastApiStatusCode 200 'Completed order retry returns HTTP 200'
    Assert-Equal $retry.id $order.id 'Retry returns the original order'
    Assert-Equal (Api GET /billing/account).balance 10000 'Retry does not add a second refund'
    Write-Output ('Recovery test passed for order ' + $order.id)
} finally {
    Kube @('scale','deployment/blt-saga-delivery',"--replicas=$originalDeliveryReplicas")
    Kube @('rollout','status','deployment/blt-saga-delivery','--timeout=120s')
}
