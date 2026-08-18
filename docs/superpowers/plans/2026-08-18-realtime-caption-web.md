# Realtime Caption Web API and Test UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a localhost-only WebSocket API that accepts live 24 kHz mono PCM and returns `CaptionUpdate` JSON, plus a browser client showing the source video above live captions.

**Architecture:** A new ASP.NET Core project accepts a start command followed by binary PCM and an end command. Incoming bytes flow through a `Pipe` and a fixed-size reblocking stream into the existing OpenAI provider; a WebSocket sink returns partial/final captions. The browser is a real API client: it streams the prepared PCM in real time while controlling a YouTube player at the matching start offset.

**Tech Stack:** .NET 8, ASP.NET Core minimal APIs, System.Net.WebSockets, System.IO.Pipelines, vanilla HTML/CSS/JavaScript, xUnit

**2026-08-19 API compatibility correction:** Live diagnostics showed that `?model=gpt-live-transcribe` is rejected as `invalid_model`, while `?intent=transcription` creates a session. The current model rejects a server VAD value, so the implementation sends the official manual-commit shape `turn_detection: null`. It commits every 480,000 PCM bytes plus one final nonempty remainder, waits up to 10 seconds for each `input_audio_buffer.committed.item_id` ACK before clearing the bounded replay window, and uses that item ID to match completed events. Completions share one bounded end drain, are deduplicated and emitted by application segment ordinal, and never fabricate VAD latency metrics. A transient failure before ACK replays the bounded window even for a nonseekable source; an ACK-loss replay may duplicate audio and therefore always marks the run incomplete.

**Continuous-session clarification:** The 10-second commits do not end the OpenAI or `/ws/captions` session. Production input continues with no arbitrary duration cap until the exact end command or disconnect. The browser's 30/120/900-second controls limit only the prepared-file test client. Unmapped partial deltas are published immediately; only finals wait for commit ACK mapping and chronological segment delivery. Finalized projector/tracker state is evicted, while incomplete and recent-duplicate state has a fixed bound; an unorderable overflow fails explicitly instead of silently dropping captions.

---

### Task 1: Reblock arbitrary client frames into 100 ms PCM reads

**Files:**
- Create: `src/ChurchSubtitle.Core/Streaming/FixedChunkReadStream.cs`
- Create: `tests/ChurchSubtitle.Core.Tests/FixedChunkReadStreamTests.cs`

- [ ] Write tests proving two short source reads are combined into one requested chunk and EOF returns the remaining bytes once.
- [ ] Run `dotnet test tests/ChurchSubtitle.Core.Tests/ChurchSubtitle.Core.Tests.csproj -c Release --filter FullyQualifiedName~FixedChunkReadStreamTests` and confirm failure because the type is missing.
- [ ] Implement a read-only `Stream` wrapper whose `ReadAsync(Memory<byte>)` loops until the destination is full or the inner stream reaches EOF; delegate disposal according to a `leaveOpen` constructor argument and reject seek/write operations.
- [ ] Re-run the focused test and expect all cases to pass.

The core read loop is:

```csharp
public override async ValueTask<int> ReadAsync(
    Memory<byte> buffer,
    CancellationToken cancellationToken = default)
{
    var total = 0;
    while (total < buffer.Length)
    {
        var read = await _inner.ReadAsync(buffer[total..], cancellationToken);
        if (read == 0) break;
        total += read;
    }
    return total;
}
```

### Task 2: Web protocol and concurrency boundaries

**Files:**
- Create: `src/ChurchSubtitle.Web/ChurchSubtitle.Web.csproj`
- Create: `src/ChurchSubtitle.Web/Protocol/CaptionSocketCommand.cs`
- Create: `src/ChurchSubtitle.Web/Protocol/CaptionSocketProtocol.cs`
- Create: `src/ChurchSubtitle.Web/Services/SingleSessionGate.cs`
- Modify: `tests/ChurchSubtitle.Core.Tests/ChurchSubtitle.Core.Tests.csproj`
- Create: `tests/ChurchSubtitle.Core.Tests/CaptionSocketProtocolTests.cs`
- Create: `tests/ChurchSubtitle.Core.Tests/SingleSessionGateTests.cs`

- [ ] Add a project reference from the test project to `ChurchSubtitle.Web` and write failing tests for `{"type":"start","delay":"low"}`, `medium`, `end`, invalid delay/type, and a gate that permits only one lease at a time.
- [ ] Create the Web SDK project targeting `net8.0` with a reference to `ChurchSubtitle.Core`.
- [ ] Implement `CaptionSocketProtocol.Parse(string)` with `System.Text.Json`; return immutable `StartCaptionSocketCommand` or `EndCaptionSocketCommand`, and throw `InvalidDataException` for invalid input.
- [ ] Implement `SingleSessionGate.TryEnter()` using `Interlocked.CompareExchange`; return an `IDisposable` lease that releases exactly once.
- [ ] Run both focused test classes and expect pass.

### Task 3: WebSocket caption sink and endpoint

**Files:**
- Create: `src/ChurchSubtitle.Web/Services/WebSocketCaptionSink.cs`
- Create: `src/ChurchSubtitle.Web/Endpoints/CaptionWebSocketEndpoint.cs`
- Create: `src/ChurchSubtitle.Web/Program.cs`
- Create: `tests/ChurchSubtitle.Core.Tests/WebSocketCaptionSinkTests.cs`

- [ ] Write a serialization test around an internal `Serialize(CaptionUpdate)` method and assert camelCase properties, string state, and absence of `audioStartMs`.
- [ ] Implement `WebSocketCaptionSink` with a `SemaphoreSlim` so only one text send occurs at a time; expose `PublishStatusAsync(text, isError)` using a normal `CaptionUpdate` with `state=status`.
- [ ] Implement the endpoint flow:

```text
accept WebSocket
acquire SingleSessionGate or send busy status
receive and validate start text command
create Pipe and FixedChunkReadStream(pipe.Reader.AsStream(), leaveOpen:false)
start OpenAiRealtimeTranscriptionProvider.TranscribeAsync
for each client frame: binary -> pipe.Writer.WriteAsync; end text -> pipe.Writer.CompleteAsync
await transcription; send completed or error status; close socket
on disconnect: cancel linked token and complete pipe
```

- [ ] Validate binary frames as even length and no more than 96,000 bytes. Reject binary before start and text other than `end` after start.
- [ ] Configure `Program.cs` with `UseWebSockets`, `Map("/ws/captions", ...)`, localhost test-asset route, static files, and a health route. Construct OpenAI options with the chosen delay, Korean church prompt, and `config/keywords.txt`.
- [ ] Add `ChurchSubtitle.Web` to `ChurchSubtitle.sln`, build Release, and expect zero warnings.

### Task 4: Test asset route and browser UI

**Files:**
- Create: `src/ChurchSubtitle.Web/wwwroot/index.html`
- Create: `src/ChurchSubtitle.Web/wwwroot/styles.css`
- Create: `src/ChurchSubtitle.Web/wwwroot/app.js`

- [ ] Build a single page with a 16:9 YouTube player above a caption stage, controls for delay and 30/120/900 seconds, start/stop buttons, status badge, current partial, and final history.
- [ ] In `app.js`, load the YouTube IFrame API, fetch `/test-assets/service.pcm`, open `/ws/captions`, send the start command, seek/play video at 565 seconds, then send 4,800-byte slices against cumulative `performance.now()` 100 ms deadlines.
- [ ] Receive `CaptionUpdate` JSON: replace the current partial for `partial`, append/deduplicate by `lineId` for `final`, and render `status` without exposing server details.
- [ ] On end send `{"type":"end"}`; on stop close the socket, stop playback, cancel timers, and restore controls.
- [ ] Make the page responsive but keep the requested hierarchy: video top, captions bottom.

### Task 5: Local launch and documentation

**Files:**
- Create: `scripts/run-web.ps1`
- Modify: `README.md`

- [ ] Implement `run-web.ps1` to dot-source `import-local-env.ps1`, reject an empty key, validate the PCM file, and execute:

```powershell
dotnet run --project src\ChurchSubtitle.Web\ChurchSubtitle.Web.csproj `
  --urls http://127.0.0.1:5287
```

- [ ] Document `http://127.0.0.1:5287`, the default 30-second paid smoke test, WebSocket wire protocol, PCM format, and the rule that the browser never receives the OpenAI key.
- [ ] Parse every PowerShell script and run the existing hard-coded-key scan.

### Task 6: Automated and paid smoke verification

**Files:**
- Create: `scripts/smoke-caption-websocket.ps1`

- [ ] Implement a PowerShell `ClientWebSocket` smoke client that connects to localhost, sends `start`, streams exactly 30 seconds (1,440,000 bytes) in 4,800-byte frames at cumulative 100 ms deadlines, sends `end`, and counts partial/final/status responses without printing transcript text or the API key.
- [ ] Start the web server in a hidden background process, wait for `/health`, and verify `index.html`, `styles.css`, `app.js`, and the PCM route return success.
- [ ] Run the WebSocket smoke client once with `delay=low`; require at least one partial or final event and a non-error completion status.
- [ ] Stop only the verified server process started for the test.
- [ ] Run `dotnet test ChurchSubtitle.sln -c Release`, `dotnet build ChurchSubtitle.sln -c Release --no-restore`, and `dotnet format ChurchSubtitle.sln --verify-no-changes --no-restore`.
- [ ] Record smoke counts and output directory while keeping key and transcript text out of logs.

## Plan self-review

- The browser and future broadcast integration use the same WebSocket API.
- PCM framing, concurrency, cancellation, API-key isolation, and reconnect failure behavior are explicit.
- The UI test uses the authorized source and matching `09:25` offset.
- The paid call is limited to one 30-second `low` run after all key-free checks pass.
- This repository has no baseline commit, so implementation checkpoints are verified by tests and are not committed independently.
