$ErrorActionPreference = 'Stop'

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$smokeClient = Join-Path $repositoryRoot 'scripts\smoke-caption-websocket.ps1'
$canonicalPcm = Join-Path $repositoryRoot 'data\poc-source-15m\service-segment-24k-mono-s16le.pcm'
$temporaryDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ([Guid]::NewGuid())

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Start-SilentWebSocketServer {
    param([string]$ReadyPath)

    Start-Job -ArgumentList $ReadyPath -ScriptBlock {
        param($ReadyPath)

        $listener = [System.Net.Sockets.TcpListener]::new(
            [System.Net.IPAddress]::Loopback,
            0)
        $client = $null
        try {
            $listener.Start()
            $port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
            [System.IO.File]::WriteAllText($ReadyPath, [string]$port)

            $client = $listener.AcceptTcpClient()
            $stream = $client.GetStream()
            $headerBytes = [System.Collections.Generic.List[byte]]::new()
            while ($headerBytes.Count -lt 16384) {
                $next = $stream.ReadByte()
                if ($next -lt 0) {
                    throw 'Client disconnected during the WebSocket handshake.'
                }

                $headerBytes.Add([byte]$next)
                $count = $headerBytes.Count
                if ($count -ge 4 -and
                    $headerBytes[$count - 4] -eq 13 -and
                    $headerBytes[$count - 3] -eq 10 -and
                    $headerBytes[$count - 2] -eq 13 -and
                    $headerBytes[$count - 1] -eq 10) {
                    break
                }
            }

            $headers = [System.Text.Encoding]::ASCII.GetString($headerBytes.ToArray())
            $keyMatch = [regex]::Match($headers, '(?im)^Sec-WebSocket-Key:\s*([^\r\n]+)')
            if (-not $keyMatch.Success) {
                throw 'No WebSocket key was received.'
            }

            $acceptInput = $keyMatch.Groups[1].Value.Trim() +
                '258EAFA5-E914-47DA-95CA-C5AB0DC85B11'
            $sha1 = [System.Security.Cryptography.SHA1]::Create()
            try {
                $accept = [Convert]::ToBase64String(
                    $sha1.ComputeHash([System.Text.Encoding]::ASCII.GetBytes($acceptInput)))
            }
            finally {
                $sha1.Dispose()
            }

            $response = "HTTP/1.1 101 Switching Protocols`r`n" +
                "Upgrade: websocket`r`n" +
                "Connection: Upgrade`r`n" +
                "Sec-WebSocket-Accept: $accept`r`n`r`n"
            $responseBytes = [System.Text.Encoding]::ASCII.GetBytes($response)
            $stream.Write($responseBytes, 0, $responseBytes.Length)
            $stream.Flush()

            # Keep the connection silent so the client has a pending ReceiveAsync
            # when its bounded completion timeout fires.
            Start-Sleep -Seconds 10
        }
        finally {
            if ($null -ne $client) {
                $client.Dispose()
            }

            $listener.Stop()
        }
    }
}

function Start-SuccessfulWebSocketServer {
    param([string]$ReadyPath)

    Start-Job -ArgumentList $ReadyPath -ScriptBlock {
        param($ReadyPath)

        function Read-ExactBytes {
            param(
                [System.IO.Stream]$Stream,
                [int]$Count
            )

            $bytes = [byte[]]::new($Count)
            $offset = 0
            while ($offset -lt $Count) {
                $read = $Stream.Read($bytes, $offset, $Count - $offset)
                if ($read -eq 0) {
                    throw 'Client disconnected before a complete WebSocket frame was read.'
                }

                $offset += $read
            }

            return ,$bytes
        }

        function Read-ClientWebSocketFrame {
            param([System.IO.Stream]$Stream)

            $prefix = Read-ExactBytes -Stream $Stream -Count 2
            $payloadLength = [long]($prefix[1] -band 0x7f)
            if ($payloadLength -eq 126) {
                $extended = Read-ExactBytes -Stream $Stream -Count 2
                $payloadLength = ([long]$extended[0] -shl 8) -bor $extended[1]
            }
            elseif ($payloadLength -eq 127) {
                throw 'The fake server does not accept 64-bit WebSocket frame lengths.'
            }

            if (($prefix[1] -band 0x80) -eq 0) {
                throw 'A client WebSocket frame was not masked.'
            }

            $mask = Read-ExactBytes -Stream $Stream -Count 4
            $payload = Read-ExactBytes -Stream $Stream -Count ([int]$payloadLength)
            for ($index = 0; $index -lt $payload.Length; $index++) {
                $payload[$index] = $payload[$index] -bxor $mask[$index % 4]
            }

            [pscustomobject]@{
                OpCode = $prefix[0] -band 0x0f
                Payload = $payload
            }
        }

        function Send-ServerWebSocketFrame {
            param(
                [System.IO.Stream]$Stream,
                [byte]$OpCode,
                [byte[]]$Payload
            )

            if ($Payload.Length -gt 125) {
                throw 'The fake server only emits small WebSocket frames.'
            }

            $header = [byte[]]@((0x80 -bor $OpCode), $Payload.Length)
            $Stream.Write($header, 0, $header.Length)
            $Stream.Write($Payload, 0, $Payload.Length)
            $Stream.Flush()
        }

        $listener = [System.Net.Sockets.TcpListener]::new(
            [System.Net.IPAddress]::Loopback,
            0)
        $client = $null
        try {
            $listener.Start()
            $port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
            [System.IO.File]::WriteAllText($ReadyPath, [string]$port)

            $client = $listener.AcceptTcpClient()
            $stream = $client.GetStream()
            $stream.ReadTimeout = 5000
            $headerBytes = [System.Collections.Generic.List[byte]]::new()
            while ($headerBytes.Count -lt 16384) {
                $next = $stream.ReadByte()
                if ($next -lt 0) {
                    throw 'Client disconnected during the WebSocket handshake.'
                }

                $headerBytes.Add([byte]$next)
                $count = $headerBytes.Count
                if ($count -ge 4 -and
                    $headerBytes[$count - 4] -eq 13 -and
                    $headerBytes[$count - 3] -eq 10 -and
                    $headerBytes[$count - 2] -eq 13 -and
                    $headerBytes[$count - 1] -eq 10) {
                    break
                }
            }

            $headers = [System.Text.Encoding]::ASCII.GetString($headerBytes.ToArray())
            $keyMatch = [regex]::Match($headers, '(?im)^Sec-WebSocket-Key:\s*([^\r\n]+)')
            if (-not $keyMatch.Success) {
                throw 'No WebSocket key was received.'
            }

            $acceptInput = $keyMatch.Groups[1].Value.Trim() +
                '258EAFA5-E914-47DA-95CA-C5AB0DC85B11'
            $sha1 = [System.Security.Cryptography.SHA1]::Create()
            try {
                $accept = [Convert]::ToBase64String(
                    $sha1.ComputeHash([System.Text.Encoding]::ASCII.GetBytes($acceptInput)))
            }
            finally {
                $sha1.Dispose()
            }

            $response = "HTTP/1.1 101 Switching Protocols`r`n" +
                "Upgrade: websocket`r`n" +
                "Connection: Upgrade`r`n" +
                "Sec-WebSocket-Accept: $accept`r`n`r`n"
            $responseBytes = [System.Text.Encoding]::ASCII.GetBytes($response)
            $stream.Write($responseBytes, 0, $responseBytes.Length)
            $stream.Flush()

            $startFrame = Read-ClientWebSocketFrame -Stream $stream
            if ($startFrame.OpCode -ne 1) {
                throw 'The first client frame was not text.'
            }

            $binaryFrameCount = 0
            do {
                $frame = Read-ClientWebSocketFrame -Stream $stream
                if ($frame.OpCode -eq 2) {
                    $binaryFrameCount++
                }
            } while ($frame.OpCode -ne 1)

            if ($binaryFrameCount -ne 10) {
                throw "Expected 10 binary frames, received $binaryFrameCount."
            }

            $partial = [System.Text.Encoding]::UTF8.GetBytes(
                '{"state":"partial","provider":"openai"}')
            Send-ServerWebSocketFrame -Stream $stream -OpCode 1 -Payload $partial
            $complete = [System.Text.Encoding]::UTF8.GetBytes(
                '{"state":"status","provider":"server"}')
            Send-ServerWebSocketFrame -Stream $stream -OpCode 1 -Payload $complete
            Send-ServerWebSocketFrame -Stream $stream -OpCode 8 `
                -Payload ([byte[]]@(0x03, 0xe8))
            Start-Sleep -Milliseconds 250
        }
        finally {
            if ($null -ne $client) {
                $client.Dispose()
            }

            $listener.Stop()
        }
    }
}

try {
    New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null

    $processStart = [System.Diagnostics.ProcessStartInfo]::new()
    $processStart.FileName = (Get-Command powershell -ErrorAction Stop).Source
    $processStart.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$smokeClient`" -DryRun"
    $processStart.UseShellExecute = $false
    $processStart.CreateNoWindow = $true
    $processStart.RedirectStandardOutput = $true
    $processStart.RedirectStandardError = $true
    $child = [System.Diagnostics.Process]::new()
    $child.StartInfo = $processStart
    try {
        [void]$child.Start()
        $childOutput = $child.StandardOutput.ReadToEnd()
        $childError = $child.StandardError.ReadToEnd()
        $child.WaitForExit()
        Assert-True ($child.ExitCode -eq 0) `
            "Out-of-process default dry-run failed with exit code $($child.ExitCode)."
        Assert-True ($childOutput -match 'DryRun') `
            'Out-of-process default dry-run did not return its validation summary.'
        Assert-True ([string]::IsNullOrWhiteSpace($childError)) `
            'Out-of-process default dry-run wrote to stderr.'
    }
    finally {
        $child.Dispose()
    }

    $defaultResult = & $smokeClient -DryRun
    Assert-True ($defaultResult.Mode -eq 'DryRun') 'Dry-run mode was not reported.'
    Assert-True ($defaultResult.Endpoint -eq 'ws://127.0.0.1:5287/ws/captions') 'The default local endpoint changed.'
    Assert-True ($defaultResult.Delay -eq 'low') 'The default delay must be low.'
    Assert-True ($defaultResult.DurationSeconds -eq 30) 'The default duration must be 30 seconds.'
    Assert-True ($defaultResult.FrameBytes -eq 4800) 'A 100 ms frame must contain 4,800 bytes.'
    Assert-True ($defaultResult.FrameCount -eq 300) 'A 30 second run must contain 300 frames.'
    Assert-True ($defaultResult.AudioBytes -eq 1440000) 'A 30 second run must contain 1,440,000 PCM bytes.'
    Assert-True ($defaultResult.StartCommand -ceq '{"type":"start","delay":"low"}') 'The low start command is not exact.'
    Assert-True ($defaultResult.EndCommand -ceq '{"type":"end"}') 'The end command is not exact.'
    Assert-True ($defaultResult.PcmPath -eq $canonicalPcm) 'The default PCM is not the canonical prepared asset.'

    $oneSecondPcm = Join-Path $temporaryDirectory 'one-second.pcm'
    [System.IO.File]::WriteAllBytes($oneSecondPcm, [byte[]]::new(48000))
    $mediumResult = & $smokeClient -DryRun -Delay medium -DurationSeconds 1 `
        -PcmPath $oneSecondPcm -Endpoint 'ws://127.0.0.1:1/custom'
    Assert-True ($mediumResult.StartCommand -ceq '{"type":"start","delay":"medium"}') 'The medium start command is not exact.'
    Assert-True ($mediumResult.FrameCount -eq 10) 'A one second run must contain ten frames.'
    Assert-True ($mediumResult.AudioBytes -eq 48000) 'A one second run must contain 48,000 bytes.'

    $shortPcm = Join-Path $temporaryDirectory 'short.pcm'
    [System.IO.File]::WriteAllBytes($shortPcm, [byte[]]::new(47998))
    $shortError = $null
    try {
        & $smokeClient -DryRun -DurationSeconds 1 -PcmPath $shortPcm | Out-Null
    }
    catch {
        $shortError = $_.Exception.Message
    }
    Assert-True ($shortError -match '48,000') 'A short PCM file was not rejected before connecting.'

    $remoteError = $null
    try {
        & $smokeClient -DryRun -Endpoint 'ws://example.com/ws/captions' | Out-Null
    }
    catch {
        $remoteError = $_.Exception.Message
    }
    Assert-True ($remoteError -match 'loopback') 'A non-loopback endpoint was not rejected.'

    $source = Get-Content -LiteralPath $smokeClient -Raw
    Assert-True ($source -notmatch 'OPENAI_API_KEY') 'The smoke client must not access the API key.'
    Assert-True ($source -notmatch 'Write-(Host|Output)[^\r\n]*(Text|Payload|Raw|Message)') `
        'The smoke client contains a likely raw server or transcript output path.'

    $observeDefinition = $source.IndexOf('function Observe-PendingWebSocketReceive')
    $observeCleanupCall = $source.LastIndexOf('Observe-PendingWebSocketReceive -Socket $socket')
    $messageDispose = $source.LastIndexOf('$receiveState.Message.Dispose()')
    Assert-True ($observeDefinition -ge 0) 'No pending-receive observation helper exists.'
    Assert-True ($observeCleanupCall -ge 0) 'Cleanup does not observe the pending receive task.'
    Assert-True ($observeCleanupCall -lt $messageDispose) `
        'The receive task must be observed before related receive state is disposed.'
    Assert-True ($source -match '(?s)try\s*\{\s*Observe-PendingWebSocketReceive -Socket \$socket.*?finally\s*\{.*?\$socket\.Dispose\(\)') `
        'Cleanup resources must still be disposed if bounded receive observation fails.'

    $readyPath = Join-Path $temporaryDirectory 'silent-server.port'
    $serverJob = Start-SilentWebSocketServer -ReadyPath $readyPath
    try {
        $readyDeadline = [DateTime]::UtcNow.AddSeconds(5)
        while (-not (Test-Path -LiteralPath $readyPath) -and [DateTime]::UtcNow -lt $readyDeadline) {
            Start-Sleep -Milliseconds 25
        }
        Assert-True (Test-Path -LiteralPath $readyPath) 'The local fake WebSocket server did not start.'

        $port = [int]([System.IO.File]::ReadAllText($readyPath))
        $failureClock = [System.Diagnostics.Stopwatch]::StartNew()
        $timeoutError = $null
        try {
            & $smokeClient -Endpoint "ws://127.0.0.1:$port/ws/captions" `
                -DurationSeconds 1 -CompletionTimeoutSeconds 1 `
                -PcmPath $oneSecondPcm | Out-Null
        }
        catch {
            $timeoutError = $_.Exception.Message
        }
        finally {
            $failureClock.Stop()
        }

        Assert-True ($timeoutError -match 'bounded timeout') `
            'The silent-server failure did not report the safe bounded timeout.'
        Assert-True ($failureClock.Elapsed.TotalSeconds -lt 7) `
            'Pending-receive cleanup did not finish within a bounded time.'
    }
    finally {
        Stop-Job -Job $serverJob -ErrorAction SilentlyContinue
        Remove-Job -Job $serverJob -Force -ErrorAction SilentlyContinue
    }

    $successfulReadyPath = Join-Path $temporaryDirectory 'successful-server.port'
    $successfulServerJob = Start-SuccessfulWebSocketServer -ReadyPath $successfulReadyPath
    try {
        $readyDeadline = [DateTime]::UtcNow.AddSeconds(5)
        while (-not (Test-Path -LiteralPath $successfulReadyPath) -and
            [DateTime]::UtcNow -lt $readyDeadline) {
            Start-Sleep -Milliseconds 25
        }
        Assert-True (Test-Path -LiteralPath $successfulReadyPath) `
            'The successful local fake WebSocket server did not start.'

        $successfulPort = [int]([System.IO.File]::ReadAllText($successfulReadyPath))
        $escapedClient = $smokeClient.Replace("'", "''")
        $escapedPcm = $oneSecondPcm.Replace("'", "''")
        $childScript = @"
`$ProgressPreference = 'SilentlyContinue'
`$items = @(& '$escapedClient' -Endpoint 'ws://127.0.0.1:$successfulPort/ws/captions' -DurationSeconds 1 -CompletionTimeoutSeconds 3 -PcmPath '$escapedPcm')
[pscustomobject]@{
    Count = `$items.Count
    Types = @(`$items | ForEach-Object { `$_.GetType().FullName })
    Result = `$items[-1]
} | ConvertTo-Json -Depth 6 -Compress
"@
        $encodedChildScript = [Convert]::ToBase64String(
            [System.Text.Encoding]::Unicode.GetBytes($childScript))
        $processStart = [System.Diagnostics.ProcessStartInfo]::new()
        $processStart.FileName = (Get-Command powershell -ErrorAction Stop).Source
        $processStart.Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedChildScript"
        $processStart.UseShellExecute = $false
        $processStart.CreateNoWindow = $true
        $processStart.RedirectStandardOutput = $true
        $processStart.RedirectStandardError = $true
        $child = [System.Diagnostics.Process]::new()
        $child.StartInfo = $processStart
        try {
            [void]$child.Start()
            $childOutput = $child.StandardOutput.ReadToEnd()
            $childError = $child.StandardError.ReadToEnd()
            $child.WaitForExit()
            Assert-True ($child.ExitCode -eq 0) `
                "Out-of-process successful smoke run failed with exit code $($child.ExitCode): $childError"
            Assert-True ([string]::IsNullOrWhiteSpace($childError)) `
                'Out-of-process successful smoke run wrote to stderr.'
            Assert-True ($childOutput -notmatch 'System\.Threading\.Tasks\.VoidTaskResult') `
                'The smoke client leaked VoidTaskResult output.'

            $envelope = $childOutput | ConvertFrom-Json -ErrorAction Stop
            Assert-True ($envelope.Count -eq 1) `
                'The smoke client emitted incidental pipeline values before its final result.'
            $outputTypes = @($envelope.Types)
            Assert-True ($outputTypes.Count -eq 1) `
                'The smoke client emitted more than one output type.'
            Assert-True ([string]$outputTypes[0] -eq 'System.Management.Automation.PSCustomObject') `
                'The smoke client did not emit only its final validation object.'
            Assert-True ($envelope.Result.Completed -eq $true) `
                'The successful fake-server run did not report completion.'
            Assert-True ($envelope.Result.PartialCount -eq 1) `
                'The successful fake-server run did not count the partial update.'
        }
        finally {
            $child.Dispose()
        }
    }
    finally {
        Stop-Job -Job $successfulServerJob -ErrorAction SilentlyContinue
        Remove-Job -Job $successfulServerJob -Force -ErrorAction SilentlyContinue
    }

    $tokens = $null
    $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $smokeClient,
        [ref]$tokens,
        [ref]$errors)
    Assert-True ($errors.Count -eq 0) 'The smoke client has a PowerShell parse error.'

    Write-Output 'PASS'
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
