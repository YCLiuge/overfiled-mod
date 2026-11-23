[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Command,
    [ValidateSet('Command', 'Health', 'State', 'Commands', 'History', 'Receipt')]
    [string]$Action = 'Command',
    [ValidateSet('auto', 'logic', 'host')]
    [string]$Target = 'auto',
    [string]$RequestId = ([guid]::NewGuid().ToString('N')),
    [string]$ReceiptId,
    [string]$BaseUri = 'http://127.0.0.1:8080',
    [ValidateRange(1, 60)]
    [int]$TimeoutSeconds = 15
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$base = [uri]$BaseUri
if (!$base.IsLoopback -or $base.Scheme -ne 'http' -or $base.AbsolutePath -ne '/' -or $base.Query -or $base.Fragment -or $base.UserInfo) {
    throw 'BaseUri must be a local HTTP origin, such as http://127.0.0.1:8080.'
}
$baseText = $BaseUri.TrimEnd('/')
$deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
$lastReceiptId = $null
$session = $null

function Invoke-GameApi([string]$Method, [string]$Path, [object]$Body = $null) {
    $remaining = [int][Math]::Ceiling(($deadline - [DateTimeOffset]::UtcNow).TotalSeconds)
    if ($remaining -le 0) { throw 'Client deadline reached. Query the existing receipt; do not resend with a new ID.' }
    $parameters = @{ Uri = ($baseText + $Path); Method = $Method; TimeoutSec = $remaining; ErrorAction = 'Stop' }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json; charset=utf-8'
        $parameters.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Compress))
    }
    try { Invoke-RestMethod @parameters }
    catch {
        $detail = $_.ErrorDetails.Message
        if (!$detail) { $detail = $_.Exception.Message }
        throw "Game API request failed: $detail"
    }
}

try {
    switch ($Action) {
        'Command' {
            if ([string]::IsNullOrWhiteSpace($Command)) { throw 'Supply -Command, or use -Action Health/State/Commands/History/Receipt.' }
            $response = Invoke-GameApi 'POST' '/api/command' @{ command = $Command; target = $Target; id = $RequestId; waitMs = 1500 }
        }
        'Health' { $response = Invoke-GameApi 'GET' '/api/health' }
        'State' { $response = Invoke-GameApi 'GET' '/api/state' }
        'Commands' { $response = Invoke-GameApi 'GET' '/api/commands' }
        'History' { $response = Invoke-GameApi 'GET' '/api/history' }
        'Receipt' {
            if ([string]::IsNullOrWhiteSpace($ReceiptId)) { throw '-ReceiptId is required for -Action Receipt.' }
            $response = Invoke-GameApi 'GET' ('/api/commands/' + [uri]::EscapeDataString($ReceiptId))
        }
    }
    $session = $response.sessionId
    $lastReceiptId = $response.data.id
    while ($response.data.status -eq 'queued' -or $response.data.status -eq 'running') {
        $id = $response.data.id
        if ([DateTimeOffset]::UtcNow -ge $deadline) {
            throw "Still pending. Use -Action Receipt -ReceiptId '$id' to check it later. Do not repeat with a new ID."
        }
        Start-Sleep -Milliseconds 200
        $response = Invoke-GameApi 'GET' ('/api/commands/' + [uri]::EscapeDataString($id))
        if ($response.sessionId -ne $session) { throw 'The game module reloaded during this request; inspect game logs before retrying.' }
    }
    if ($response.data.status -in @('failed', 'expired', 'cancelled')) {
        throw ($response | ConvertTo-Json -Depth 20 -Compress)
    }
    $response | ConvertTo-Json -Depth 20
}
catch {
    if ($lastReceiptId) {
        throw "Receipt ID: $lastReceiptId (session $session). Query with -Action Receipt -ReceiptId '$lastReceiptId'. $($_.Exception.Message)"
    }
    if ($Action -eq 'Command') {
        throw "Command request ID: $RequestId. $($_.Exception.Message)"
    }
    throw
}
