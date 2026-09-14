param([string]$BaseUrl = 'http://localhost:8000')
$ErrorActionPreference = 'Stop'
$started = (Get-Date).ToUniversalTime().ToString('o')
$checks = @()
function Request([string]$Method, [string]$Path, [int]$Expected, [string]$Body = '', [hashtable]$Headers = @{}) {
    $options = @{Uri="$BaseUrl$Path"; Method=$Method; SkipHttpErrorCheck=$true; Headers=$Headers}
    if ($Body) { $options.Body=$Body; $options.ContentType='application/json' }
    $response = Invoke-WebRequest @options
    if ([int]$response.StatusCode -ne $Expected) { throw "$Method $Path returned $($response.StatusCode), expected $Expected" }
    $script:checks += @{method=$Method; path=$Path; status=[int]$response.StatusCode}
    return $response.Content
}
$suffix = [guid]::NewGuid().ToString('N').Substring(0,12)
$body = @{username="logging-$suffix";firstName='Log';lastName='Test';email="logging-$suffix@example.test"} | ConvertTo-Json -Compress
$user = Request POST /user 201 $body | ConvertFrom-Json
Request GET /user 200 | Out-Null
Request GET "/user/$($user.id)" 200 | Out-Null
Request PUT "/user/$($user.id)" 200 '{"firstName":"Updated"}' | Out-Null
Request POST /user 409 $body | Out-Null
Request POST /user 400 '{}' | Out-Null
Request POST /user 400 '{broken' | Out-Null
Request DELETE "/user/$($user.id)" 204 | Out-Null
Request GET "/user/$($user.id)" 404 | Out-Null
Request GET /user 500 '' @{'X-Demo-Error'='500'} | Out-Null
$result = @{startedAt=$started; finishedAt=(Get-Date).ToUniversalTime().ToString('o'); user_id=$user.id; checks=$checks}
New-Item -ItemType Directory -Force "$PSScriptRoot/../docs/results/logging" | Out-Null
$result | ConvertTo-Json -Depth 8 | Set-Content "$PSScriptRoot/../docs/results/logging/http-checks.json"
$result | ConvertTo-Json -Depth 8
