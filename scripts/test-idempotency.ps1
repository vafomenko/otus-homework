param(
    [string]$BaseUrl = 'http://arch.homework',
    [string]$Kubeconfig = "$PSScriptRoot/../.local/kubeconfig-idempotency"
)
$ErrorActionPreference = 'Stop'
$postman = "$PSScriptRoot/../postman"
$export = [IO.Path]::GetTempFileName()
try {
    & npx --yes newman@6.2.1 run "$postman/homework-10.postman_collection.json" `
        --environment "$postman/homework-10.postman_environment.json" --env-var "baseUrl=$BaseUrl" `
        --export-environment $export --timeout 180000 --timeout-request 30000 --color off --bail
    if ($LASTEXITCODE -ne 0) { throw 'Newman failed.' }
    $variables = (Get-Content -Raw -LiteralPath $export | ConvertFrom-Json).values
    $paid = [guid]($variables | Where-Object key -eq 'paidOrderId').value
    $declined = [guid]($variables | Where-Object key -eq 'declinedOrderId').value
} finally {
    Remove-Item -LiteralPath $export
}

$kubeArgs = @('--kubeconfig', $Kubeconfig, '--namespace', 'blt')
$broker = 'blt-saga-kafka-0'
$topic = 'blt.order.events.v1'
$group = 'blt.notifications.v1'

function Query-Database([string]$Service, [string]$Sql) {
    & kubectl @kubeArgs exec "blt-saga-$Service-db-0" -- psql -XAtq -v ON_ERROR_STOP=1 -U $Service -d $Service -c $Sql
    if ($LASTEXITCODE -ne 0) { throw "Failed to query $Service." }
}

function Read-ConsumerOffsets {
    $lines = & kubectl @kubeArgs exec $broker -- /opt/kafka/bin/kafka-consumer-groups.sh `
        --bootstrap-server localhost:9092 --describe --group $group
    if ($LASTEXITCODE -ne 0) { throw "Failed to read offsets for $group." }
    foreach ($line in $lines) {
        $fields = $line.Trim() -split '\s+'
        if ($fields[0] -eq $group -and $fields[1] -eq $topic) {
            # Kafka reports "-" for a partition without a committed offset.
            [pscustomobject]@{
                Committed = if ($fields[3] -eq '-') { 0L } else { [long]$fields[3] }
                End = [long]$fields[4]
            }
        }
    }
}

$orderIds = "('$paid', '$declined')"
$events = @(Query-Database orders "SELECT payload::text FROM outbox WHERE order_id IN $orderIds ORDER BY (order_id='$paid') DESC")
$selectLetters = "SELECT row_to_json(n)::text FROM notifications n WHERE order_id IN $orderIds ORDER BY order_id"
$before = @(Query-Database notifications $selectLetters)
if ($events.Count -ne 2 -or $before.Count -ne 2) { throw 'Expected one outbox event and one letter per test order.' }
$offsets = @(Read-ConsumerOffsets)
if ($offsets.Count -eq 0) { throw "Consumer group $group has no partitions for $topic." }
$partitions = $offsets.Count
$initialEnd = ($offsets | Measure-Object End -Sum).Sum

# A single Kafka key keeps B, A, B, A in one partition in the specified order.
$records = foreach ($index in 1, 0, 1, 0) { "$paid|$($events[$index])" }
$records | & kubectl @kubeArgs exec -i $broker -- /opt/kafka/bin/kafka-console-producer.sh `
    --bootstrap-server localhost:9092 --topic $topic --sync --producer-property acks=all `
    --property parse.key=true --property 'key.separator=|'
if ($LASTEXITCODE -ne 0) { throw 'Kafka replay failed.' }

$deadline = [DateTime]::UtcNow.AddSeconds(60)
do {
    $offsets = @(Read-ConsumerOffsets)
    $end = ($offsets | Measure-Object End -Sum).Sum
    $lagging = @($offsets | Where-Object { $_.Committed -lt $_.End })
    if ($offsets.Count -eq $partitions -and $end -ge ($initialEnd + $records.Count) -and $lagging.Count -eq 0) { break }
    if ([DateTime]::UtcNow -ge $deadline) { throw 'Notifications did not commit the replayed offsets in time.' }
    Start-Sleep -Milliseconds 500
} while ($true)

$after = @(Query-Database notifications $selectLetters)
if (($before -join "`n") -cne ($after -join "`n")) { throw 'Kafka replay changed the stored notifications.' }
Write-Output "Сервис Notifications подтвердил все повторно доставленные записи ($($records.Count)). Оба письма совпадают с исходными."
