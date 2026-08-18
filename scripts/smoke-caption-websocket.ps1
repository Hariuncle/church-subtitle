[CmdletBinding()]
param(
    [uri]$Endpoint = 'ws://127.0.0.1:5287/ws/captions',
    [ValidateSet('low', 'medium')]
    [string]$Delay = 'low',
    [ValidateRange(1, 900)]
    [int]$DurationSeconds = 30,
    [string]$PcmPath,
    [ValidateRange(1, 120)]
    [int]$ConnectTimeoutSeconds = 10,
    [ValidateRange(1, 120)]
    [int]$CompletionTimeoutSeconds = 30,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($PcmPath)) {
    $PcmPath = Join-Path $PSScriptRoot '..\data\poc-source-15m\service-segment-24k-mono-s16le.pcm'
}

$frameBytes = 4800
$framesPerSecond = 10
$bytesPerSecond = 48000
$frameCount = $DurationSeconds * $framesPerSecond
$audioBytes = [long]$DurationSeconds * $bytesPerSecond
$startCommand = "{`"type`":`"start`",`"delay`":`"$Delay`"}"
$endCommand = '{"type":"end"}'

function Assert-LocalWebSocketEndpoint {
    param([uri]$Uri)

    if ($Uri.Scheme -notin @('ws', 'wss')) {
        throw 'The caption endpoint must use the ws or wss scheme.'
    }

    if (-not $Uri.IsLoopback) {
        throw 'The caption endpoint must resolve to a loopback URI.'
    }
}

function Send-WebSocketFrame {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [byte[]]$Bytes,
        [System.Net.WebSockets.WebSocketMessageType]$MessageType,
        [System.Threading.CancellationToken]$SessionToken
    )

    $sendCancellation = [System.Threading.CancellationTokenSource]::CreateLinkedTokenSource(
        $SessionToken)
    try {
        $sendCancellation.CancelAfter([TimeSpan]::FromSeconds(5))
        $segment = [System.ArraySegment[byte]]::new($Bytes)
        try {
            [void]$Socket.SendAsync(
                $segment,
                $MessageType,
                $true,
                $sendCancellation.Token).GetAwaiter().GetResult()
        }
        catch {
            throw 'A bounded WebSocket send failed.'
        }
    }
    finally {
        $sendCancellation.Dispose()
    }
}

function Start-WebSocketReceive {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [hashtable]$State,
        [System.Threading.CancellationToken]$SessionToken
    )

    if ($null -ne $State.ReceiveTask -or $State.CloseObserved) {
        return
    }

    $segment = [System.ArraySegment[byte]]::new($State.Buffer)
    $State.ReceiveTask = $Socket.ReceiveAsync($segment, $SessionToken)
}

function Complete-WebSocketReceive {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [hashtable]$State,
        [System.Threading.CancellationToken]$SessionToken
    )

    try {
        $result = $State.ReceiveTask.GetAwaiter().GetResult()
    }
    catch {
        throw 'A bounded WebSocket receive failed.'
    }
    finally {
        $State.ReceiveTask = $null
    }

    if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
        $State.CloseObserved = $true
        $State.CloseStatus = $result.CloseStatus
        return
    }

    if ($null -eq $State.MessageType) {
        $State.MessageType = $result.MessageType
    }
    elseif ($State.MessageType -ne $result.MessageType) {
        throw 'The server changed message type inside a fragmented response.'
    }

    if ($State.Message.Length + $result.Count -gt 65536) {
        throw 'A server response exceeded the safe message limit.'
    }

    $State.Message.Write($State.Buffer, 0, $result.Count)
    if ($result.EndOfMessage) {
        if ($State.MessageType -ne [System.Net.WebSockets.WebSocketMessageType]::Text) {
            throw 'The caption server returned a non-text response.'
        }

        try {
            $json = [System.Text.Encoding]::UTF8.GetString($State.Message.ToArray())
            $update = $json | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            throw 'The caption server returned malformed JSON.'
        }
        finally {
            $State.Message.SetLength(0)
            $State.MessageType = $null
        }

        if ($null -eq $update.state -or $null -eq $update.provider) {
            throw 'The caption server returned an invalid update shape.'
        }

        switch ([string]$update.state) {
            'partial' { $State.PartialCount++ }
            'final' { $State.FinalCount++ }
            'status' {
                $State.StatusCount++
                if ([string]$update.provider -ceq 'server:error') {
                    $State.ServerErrorCount++
                }
                elseif ([string]$update.provider -ceq 'server') {
                    $State.CompletionObserved = $true
                }
            }
        }
    }

    Start-WebSocketReceive -Socket $Socket -State $State -SessionToken $SessionToken
}

function Drain-WebSocketReceives {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [hashtable]$State,
        [System.Threading.CancellationToken]$SessionToken
    )

    while ($null -ne $State.ReceiveTask -and $State.ReceiveTask.IsCompleted) {
        Complete-WebSocketReceive -Socket $Socket -State $State -SessionToken $SessionToken
    }
}

function Observe-PendingWebSocketReceive {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [hashtable]$State,
        [int]$TimeoutMilliseconds = 2000
    )

    if ($null -eq $State -or $null -eq $State.ReceiveTask) {
        return
    }

    $receiveTask = $State.ReceiveTask
    if (-not $receiveTask.IsCompleted) {
        try {
            [void]$receiveTask.Wait($TimeoutMilliseconds)
        }
        catch {
            # A canceled or aborted receive can throw from Wait; it is observed
            # below through GetResult without exposing transport details.
        }
    }

    if (-not $receiveTask.IsCompleted) {
        throw 'The pending WebSocket receive did not stop within the cleanup timeout.'
    }

    try {
        [void]$receiveTask.GetAwaiter().GetResult()
    }
    catch [System.OutOfMemoryException] {
        throw
    }
    catch {
        # Cancellation and WebSocket abort failures are expected during cleanup.
    }
    finally {
        $State.ReceiveTask = $null
    }
}

function Assert-StreamingState {
    param([hashtable]$State)

    if ($State.ServerErrorCount -gt 0) {
        throw 'The caption server reported an error status.'
    }

    if ($State.CloseObserved) {
        throw 'The caption WebSocket closed before audio upload completed.'
    }
}

function Wait-UntilPacingDeadline {
    param(
        [System.Net.WebSockets.ClientWebSocket]$Socket,
        [hashtable]$State,
        [System.Threading.CancellationToken]$SessionToken,
        [System.Diagnostics.Stopwatch]$Stopwatch,
        [double]$DeadlineMilliseconds
    )

    while ($Stopwatch.Elapsed.TotalMilliseconds -lt $DeadlineMilliseconds) {
        Drain-WebSocketReceives -Socket $Socket -State $State -SessionToken $SessionToken
        Assert-StreamingState -State $State
        if ($SessionToken.IsCancellationRequested) {
            throw 'The caption smoke session timed out.'
        }

        $remaining = $DeadlineMilliseconds - $Stopwatch.Elapsed.TotalMilliseconds
        $sleepMilliseconds = [Math]::Max(1, [Math]::Min(10, [int][Math]::Ceiling($remaining)))
        Start-Sleep -Milliseconds $sleepMilliseconds
    }
}

Assert-LocalWebSocketEndpoint -Uri $Endpoint

$resolvedPcmPath = [System.IO.Path]::GetFullPath($PcmPath)
if (-not (Test-Path -LiteralPath $resolvedPcmPath -PathType Leaf)) {
    throw "The prepared PCM file was not found: $resolvedPcmPath"
}

$pcmLength = (Get-Item -LiteralPath $resolvedPcmPath).Length
if ($pcmLength -lt $audioBytes) {
    throw "The PCM file must contain at least $('{0:N0}' -f $audioBytes) bytes for this run."
}

$validation = [pscustomobject]@{
    Mode = if ($DryRun) { 'DryRun' } else { 'Live' }
    Endpoint = $Endpoint.AbsoluteUri
    Delay = $Delay
    DurationSeconds = $DurationSeconds
    FrameBytes = $frameBytes
    FrameCount = $frameCount
    AudioBytes = $audioBytes
    StartCommand = $startCommand
    EndCommand = $endCommand
    PcmPath = $resolvedPcmPath
}

if ($DryRun) {
    return $validation
}

$socket = [System.Net.WebSockets.ClientWebSocket]::new()
$socket.Options.KeepAliveInterval = [TimeSpan]::FromSeconds(5)
$connectCancellation = [System.Threading.CancellationTokenSource]::new()
$sessionCancellation = $null
$pcmStream = $null
$receiveState = $null

try {
    $connectCancellation.CancelAfter([TimeSpan]::FromSeconds($ConnectTimeoutSeconds))
    try {
        [void]$socket.ConnectAsync($Endpoint, $connectCancellation.Token).GetAwaiter().GetResult()
    }
    catch {
        throw 'The local caption WebSocket connection failed or timed out.'
    }

    $sessionCancellation = [System.Threading.CancellationTokenSource]::new()
    $sessionCancellation.CancelAfter(
        [TimeSpan]::FromSeconds($DurationSeconds + $CompletionTimeoutSeconds + 10))
    $receiveState = @{
        Buffer = [byte[]]::new(8192)
        ReceiveTask = $null
        Message = [System.IO.MemoryStream]::new()
        MessageType = $null
        PartialCount = 0
        FinalCount = 0
        StatusCount = 0
        ServerErrorCount = 0
        CompletionObserved = $false
        CloseObserved = $false
        CloseStatus = $null
    }
    Start-WebSocketReceive -Socket $socket -State $receiveState `
        -SessionToken $sessionCancellation.Token

    $startBytes = [System.Text.Encoding]::UTF8.GetBytes($startCommand)
    Send-WebSocketFrame -Socket $socket -Bytes $startBytes `
        -MessageType ([System.Net.WebSockets.WebSocketMessageType]::Text) `
        -SessionToken $sessionCancellation.Token

    $pcmStream = [System.IO.File]::Open(
        $resolvedPcmPath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    $frame = [byte[]]::new($frameBytes)
    $pacingClock = [System.Diagnostics.Stopwatch]::StartNew()
    $sentBytes = [long]0

    for ($frameIndex = 0; $frameIndex -lt $frameCount; $frameIndex++) {
        Wait-UntilPacingDeadline -Socket $socket -State $receiveState `
            -SessionToken $sessionCancellation.Token -Stopwatch $pacingClock `
            -DeadlineMilliseconds ($frameIndex * 100.0)

        $read = 0
        while ($read -lt $frameBytes) {
            $nextRead = $pcmStream.Read($frame, $read, $frameBytes - $read)
            if ($nextRead -eq 0) {
                throw 'The PCM stream ended before the validated byte count was read.'
            }

            $read += $nextRead
        }

        Send-WebSocketFrame -Socket $socket -Bytes $frame `
            -MessageType ([System.Net.WebSockets.WebSocketMessageType]::Binary) `
            -SessionToken $sessionCancellation.Token
        $sentBytes += $read
        Drain-WebSocketReceives -Socket $socket -State $receiveState `
            -SessionToken $sessionCancellation.Token
        Assert-StreamingState -State $receiveState
    }

    if ($sentBytes -ne $audioBytes) {
        throw 'The smoke client did not send the exact requested PCM byte count.'
    }

    $endBytes = [System.Text.Encoding]::UTF8.GetBytes($endCommand)
    Send-WebSocketFrame -Socket $socket -Bytes $endBytes `
        -MessageType ([System.Net.WebSockets.WebSocketMessageType]::Text) `
        -SessionToken $sessionCancellation.Token

    $completionClock = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not $receiveState.CloseObserved) {
        Drain-WebSocketReceives -Socket $socket -State $receiveState `
            -SessionToken $sessionCancellation.Token
        if ($receiveState.ServerErrorCount -gt 0) {
            throw 'The caption server reported an error status.'
        }

        if ($completionClock.Elapsed.TotalSeconds -ge $CompletionTimeoutSeconds -or
            $sessionCancellation.IsCancellationRequested) {
            throw 'The caption server did not complete within the bounded timeout.'
        }

        Start-Sleep -Milliseconds 10
    }

    if ($receiveState.CloseStatus -ne [System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure) {
        throw 'The caption WebSocket closed abnormally.'
    }

    if (-not $receiveState.CompletionObserved) {
        throw 'The caption server closed without a completion status.'
    }

    if (($receiveState.PartialCount + $receiveState.FinalCount) -eq 0) {
        throw 'The caption server returned no partial or final updates.'
    }

    if ($socket.State -eq [System.Net.WebSockets.WebSocketState]::CloseReceived) {
        $closeCancellation = [System.Threading.CancellationTokenSource]::new()
        try {
            $closeCancellation.CancelAfter([TimeSpan]::FromSeconds(2))
            try {
                [void]$socket.CloseOutputAsync(
                    [System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure,
                    'smoke complete',
                    $closeCancellation.Token).GetAwaiter().GetResult()
            }
            catch {
                throw 'The bounded WebSocket close acknowledgement failed.'
            }
        }
        finally {
            $closeCancellation.Dispose()
        }
    }

    [pscustomobject]@{
        Mode = 'Live'
        Endpoint = $Endpoint.AbsoluteUri
        Delay = $Delay
        DurationSeconds = $DurationSeconds
        FrameCount = $frameCount
        AudioBytes = $sentBytes
        PartialCount = $receiveState.PartialCount
        FinalCount = $receiveState.FinalCount
        StatusCount = $receiveState.StatusCount
        Completed = $true
        CloseStatus = 'NormalClosure'
    }
}
finally {
    if ($null -ne $pcmStream) {
        $pcmStream.Dispose()
    }

    if ($null -ne $sessionCancellation) {
        $sessionCancellation.Cancel()
    }

    if ($socket.State -notin @(
        [System.Net.WebSockets.WebSocketState]::Closed,
        [System.Net.WebSockets.WebSocketState]::Aborted,
        [System.Net.WebSockets.WebSocketState]::None)) {
        $socket.Abort()
    }

    try {
        Observe-PendingWebSocketReceive -Socket $socket -State $receiveState
    }
    finally {
        if ($null -ne $receiveState -and $null -ne $receiveState.Message) {
            $receiveState.Message.Dispose()
        }

        if ($null -ne $sessionCancellation) {
            $sessionCancellation.Dispose()
        }

        $connectCancellation.Dispose()
        $socket.Dispose()
    }
}
