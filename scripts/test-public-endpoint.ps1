param([Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Address)
# Creates two isolated diagnostic accounts, then closes the test room and logs out.
# Random passwords/tokens stay in process memory and are never printed.
$ErrorActionPreference = 'Stop'
$uri = $null
if (-not [Uri]::TryCreate($Address, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or $uri.AbsolutePath -ne '/') {
    throw '请提供不含账号信息的 HTTPS 服务器根地址。'
}
$Address = $uri.AbsoluteUri.TrimEnd('/')
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$probeHost = $null
$probeGuest = $null
$hostSocket = [System.Net.WebSockets.ClientWebSocket]::new()
$guestSocket = [System.Net.WebSockets.ClientWebSocket]::new()
$timeout = [System.Threading.CancellationTokenSource]::new(30000)
function Send-Api($Path, $Body, $Session) {
    $headers = @{}
    if ($Session) { $headers.Authorization = 'Bearer ' + $Session.token }
    Invoke-RestMethod -Uri ($Address + $Path) -Method Post -Headers $headers -ContentType 'application/json' -Body (ConvertTo-Json -InputObject $Body -Compress) -TimeoutSec 15
}
try {
    $health = Invoke-RestMethod -Uri ($Address + '/health') -TimeoutSec 15
    if ($health.status -ne 'ok') { throw '健康检查失败。' }
    if ($health.protocolVersion -ne 2) { throw '请先部署支持原生地图流程的 0.3.0 服务端。' }
    $probeHost = Send-Api '/api/register' @{username="probeH_$suffix"; password=[Guid]::NewGuid().ToString('N')} $null
    $probeGuest = Send-Api '/api/register' @{username="probeG_$suffix"; password=[Guid]::NewGuid().ToString('N')} $null
    $room = Send-Api '/api/rooms' @{name="部署验证_$suffix";password='';gameVersion='1.27.0.52240';capacity=2} $probeHost
    $null = Send-Api "/api/rooms/$($room.id)/join" @{password='';gameVersion='1.27.0.52240'} $probeGuest
    $packet = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($packet)
    $writer.Write([byte[]]@(247,48,0,0,80,88,51,87))
    $writer.Write([uint32]27); $writer.Write([uint32]305419896); $writer.Write([uint32]2271560481)
    $writer.Write([Text.Encoding]::UTF8.GetBytes("Test Game`0`0"))
    $stat = [byte[]]::new(13) + [Text.Encoding]::UTF8.GetBytes("Maps\fixture.w3x`0Host`0`0") + [byte[]]::new(20)
    for ($i = 0; $i -lt $stat.Length; $i += 7) {
        [byte]$mask = 1
        [byte[]]$block = $stat[$i..([Math]::Min($i + 6, $stat.Length - 1))]
        for ($j = 0; $j -lt $block.Length; $j++) {
            if (($block[$j] -band 1) -eq 1) { $mask = $mask -bor (1 -shl ($j + 1)) } else { $block[$j]++ }
        }
        $writer.Write($mask); $writer.Write($block)
    }
    $writer.Write([byte]0)
    foreach ($value in @(8,9,1,7,100)) { $writer.Write([uint32]$value) }
    $writer.Write([uint16]6112)
    $bytes = $packet.ToArray()
    $length = [BitConverter]::GetBytes([uint16]$bytes.Length)
    $bytes[2] = $length[0]; $bytes[3] = $length[1]
    $writer.Dispose(); $packet.Dispose()
    $null = Send-Api "/api/rooms/$($room.id)/game" @{packet=[Convert]::ToBase64String($bytes)} $probeHost
    $tunnel = Send-Api "/api/rooms/$($room.id)/tunnels" @{} $probeGuest
    $wsUri = [Uri]::new(($Address -replace '^https:', 'wss:') + '/relay/' + $tunnel.id)
    $hostSocket.Options.SetRequestHeader('Authorization', 'Bearer ' + $probeHost.token)
    $guestSocket.Options.SetRequestHeader('Authorization', 'Bearer ' + $probeGuest.token)
    $null = $hostSocket.ConnectAsync($wsUri, $timeout.Token).GetAwaiter().GetResult()
    $null = $guestSocket.ConnectAsync($wsUri, $timeout.Token).GetAwaiter().GetResult()
    function Check-Transfer($From, $To, [byte[]]$Payload) {
        $null = $From.SendAsync([ArraySegment[byte]]::new($Payload), [System.Net.WebSockets.WebSocketMessageType]::Binary, $true, $timeout.Token).GetAwaiter().GetResult()
        $received = [IO.MemoryStream]::new()
        try {
            $buffer = [byte[]]::new(4096)
            while ($received.Length -lt $Payload.Length) {
                $result = $To.ReceiveAsync([ArraySegment[byte]]::new($buffer), $timeout.Token).GetAwaiter().GetResult()
                if ($result.MessageType -ne [System.Net.WebSockets.WebSocketMessageType]::Binary) { throw '中继连接意外关闭。' }
                $received.Write($buffer, 0, $result.Count)
            }
            if ([Convert]::ToBase64String($received.ToArray()) -ne [Convert]::ToBase64String($Payload)) { throw '中继数据不一致。' }
        } finally { $received.Dispose() }
    }
    Check-Transfer $hostSocket $guestSocket ([byte[]]@(247,1,8,0,1,2,3,4))
    $payload = [byte[]]::new(24000)
    [Security.Cryptography.RandomNumberGenerator]::Fill($payload)
    Check-Transfer $guestSocket $hostSocket $payload
    Write-Output 'PASS: 公网可信 HTTPS、注册登录、房间加入、WSS 双向中继与分片传输。'
    Write-Output "诊断账号：probeH_$suffix / probeG_$suffix（随机密码未保存）。"
} finally {
    $hostSocket.Dispose(); $guestSocket.Dispose(); $timeout.Dispose()
    foreach ($session in @($probeHost, $probeGuest)) {
        if ($session) { try { $null = Send-Api '/api/logout' @{} $session } catch { Write-Warning '测试账号退出失败，请检查服务端。' } }
    }
}
