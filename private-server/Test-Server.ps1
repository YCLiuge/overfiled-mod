param([string]$BaseUrl = 'http://127.0.0.1:18080')
$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')
$root = Invoke-WebRequest "$BaseUrl/" -TimeoutSec 5 -UseBasicParsing
if ($root.Content -ne 'Lolo!') { throw 'Unexpected server root response.' }
$body = @{ version = '2026-10-21-14-05-50'; version2 = '2026-11-07-22-39-35'; accountType = '1'; os = 'Windows' }
$region = Invoke-RestMethod "$BaseUrl/dispatch/region_info" -Method Post -Body $body -TimeoutSec 5
if (!$region.status -or !$region.is_server_open) { throw 'Region endpoint did not report an open server.' }
foreach ($endpoint in @(@($region.gate_tcp_ip, $region.gate_tcp_port), @($region.client_log_tcp_ip, $region.client_log_tcp_port))) {
    $tcp = [Net.Sockets.TcpClient]::new()
    try {
        $task = $tcp.ConnectAsync([string]$endpoint[0], [int]$endpoint[1])
        if (!$task.Wait(5000)) { throw "TCP connect timed out: $endpoint" }
        $null = $task.GetAwaiter().GetResult()
        Write-Host "PASS TCP listener $($endpoint[0]):$($endpoint[1])"
    } finally { $tcp.Dispose() }
}
Write-Host 'PASS root, region_info and advertised TCP listeners. This does not prove protocol login or in-game play.'
$region | Select-Object status, gate_tcp_ip, gate_tcp_port, client_log_tcp_ip, client_log_tcp_port, currentVersion | Format-List
