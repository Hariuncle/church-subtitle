# Current-Position Two-Line Caption Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the local YouTube PoC transcribe from the player's current position until stop/video end and render a rolling two-line broadcast caption.

**Architecture:** Prepare one full-length 24 kHz mono PCM asset and keep the existing range-enabled local endpoint. The browser converts the YouTube start time into a frame-aligned byte range, resets playback to that captured time after async preparation, and streams 100 ms PCM frames until EOF or explicit stop. Caption events remain unchanged on the WebSocket; the UI projects them into two rolling lines.

**Tech Stack:** .NET 8 / ASP.NET Core, browser ES modules, YouTube IFrame API, PowerShell, FFmpeg, Node `node:test`, xUnit.

**Repository note:** The repository has an unborn `HEAD` and the user has not authorized commits. Preserve all working files and omit commit commands until the user explicitly requests an initial commit.

---

## File map

- Generate: `data/poc-source-full/service-full-24k-mono-s16le.pcm` — full 00:00–24:26 PCM test asset, ignored binary data.
- Generate: `data/poc-source-full/source-metadata.json` — source URL, duration, and authorization record.
- Modify: `src/ChurchSubtitle.Web/Program.cs` — serve the full PCM asset with range processing.
- Modify: `scripts/run-web.ps1` — validate the exact full PCM path served by the web app.
- Modify: `tests/powershell/RunWeb.Tests.ps1` — enforce launcher/server path coupling and updated documentation contract.
- Modify: `src/ChurchSubtitle.Web/wwwroot/app.js` — current-position mapping, unbounded-to-EOF test stream, player synchronization, two-line projection.
- Modify: `src/ChurchSubtitle.Web/wwwroot/index.html` — remove fixed start/duration/history UI and add two caption rows.
- Modify: `src/ChurchSubtitle.Web/wwwroot/styles.css` — fixed-height two-line broadcast presentation.
- Modify: `tests/web-ui/app.test.mjs` — behavior tests for offsets, lifetime, playback, and two-line rolling captions.
- Modify: `tests/ChurchSubtitle.Core.Tests/WebUiAssetTests.cs` — static accessibility/security/protocol contract.
- Modify: `README.md` — explain arbitrary-position testing and the distinction from production live audio.

### Task 1: Prepare and validate the full source PCM

**Files:**
- Generate: `data/poc-source-full/service-full-24k-mono-s16le.pcm`
- Generate: `data/poc-source-full/source-metadata.json`
- Source: `data/poc-source-15m/original.webm`

- [ ] **Step 1: Resolve the bundled FFmpeg path and inspect the original duration**

Run:

```powershell
$mediaRoot = Join-Path $env:LOCALAPPDATA 'church-subtitle-tools\media'
$ffmpeg = Get-ChildItem -LiteralPath $mediaRoot -Filter 'ffmpeg.exe' -Recurse | Select-Object -First 1 -ExpandProperty FullName
$ffprobe = Get-ChildItem -LiteralPath $mediaRoot -Filter 'ffprobe.exe' -Recurse | Select-Object -First 1 -ExpandProperty FullName
& $ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 'data\poc-source-15m\original.webm'
```

Expected: approximately `1466` seconds and both tools resolve beneath `%LOCALAPPDATA%\church-subtitle-tools\media`.

- [ ] **Step 2: Generate full PCM without overwriting an existing artifact**

Run after confirming `data\poc-source-full\service-full-24k-mono-s16le.pcm` does not already exist:

```powershell
New-Item -ItemType Directory -Force -Path 'data\poc-source-full' | Out-Null
& $ffmpeg -hide_banner -n -i 'data\poc-source-15m\original.webm' -t '00:24:26' -vn -ac 1 -ar 24000 -c:a pcm_s16le -f s16le 'data\poc-source-full\service-full-24k-mono-s16le.pcm'
```

Expected: exit code `0`; output is approximately `sourceDurationSeconds * 48,000` bytes.

- [ ] **Step 3: Validate PCM framing and duration**

Run:

```powershell
$pcm = Get-Item 'data\poc-source-full\service-full-24k-mono-s16le.pcm'
if ($pcm.Length % 4800 -ne 0) { throw 'PCM is not aligned to 100 ms frames.' }
$pcm.Length / 48000
```

Expected: a duration close to the probed source duration and no alignment error.

- [ ] **Step 4: Write source metadata**

Create `data/poc-source-full/source-metadata.json` with:

```json
{
  "sourceUrl": "https://www.youtube.com/watch?v=YLsWQq04R10",
  "start": "00:00:00",
  "duration": "00:24:26",
  "authorizedSourceConfirmed": true
}
```

### Task 2: Couple the server and launcher to the full PCM

**Files:**
- Modify: `src/ChurchSubtitle.Web/Program.cs:56-73`
- Modify: `scripts/run-web.ps1:82-87`
- Test: `tests/powershell/RunWeb.Tests.ps1`

- [ ] **Step 1: Change launcher tests to require the full canonical path**

Replace the test path and path-part assertions with:

```powershell
$pcm = Join-Path $repositoryRoot 'data\poc-source-full\service-full-24k-mono-s16le.pcm'

Assert-True (
    $launcherSource.Contains('data\poc-source-full\service-full-24k-mono-s16le.pcm')) `
    'The launcher does not name the canonical full PCM path.'

foreach ($pathPart in @('data', 'poc-source-full', 'service-full-24k-mono-s16le.pcm')) {
    Assert-True ($pcmRoute.Contains("`"$pathPart`"")) "Web PCM route is not coupled to canonical part: $pathPart"
}
```

- [ ] **Step 2: Run the launcher test and verify RED**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\powershell\RunWeb.Tests.ps1
```

Expected: FAIL because the launcher and server still name `poc-source-15m`.

- [ ] **Step 3: Update the production canonical paths**

In `Program.cs`, construct:

```csharp
var path = Path.Combine(
    repositoryRoot,
    "data",
    "poc-source-full",
    "service-full-24k-mono-s16le.pcm");
```

In `run-web.ps1`, resolve:

```powershell
$resolvedPcmPath = Resolve-RepositoryPath 'data\poc-source-full\service-full-24k-mono-s16le.pcm'
```

Keep `enableRangeProcessing: true`, loopback binding, and the non-overridable canonical path.

- [ ] **Step 4: Run the launcher test and verify GREEN**

Run the command from Step 2.

Expected: `PASS` with no API key text.

### Task 3: Map current video position to a PCM range and stream to EOF

**Files:**
- Modify: `src/ChurchSubtitle.Web/wwwroot/app.js`
- Test: `tests/web-ui/app.test.mjs`

- [ ] **Step 1: Add failing unit tests for byte mapping**

Import `pcmStartByteForVideoTime` and add:

```js
test("video time maps down to an aligned PCM frame", () => {
  assert.equal(pcmStartByteForVideoTime(0), 0);
  assert.equal(pcmStartByteForVideoTime(595.099), 595 * 48000);
  assert.equal(pcmStartByteForVideoTime(595.1), 5951 * 4800);
});

test("invalid video time is rejected before a socket is opened", async () => {
  const harness = createHarness({ playbackStartSeconds: Number.NaN });
  assert.equal(await harness.controller.start({ delay: "low" }), false);
  assert.equal(harness.socketFactoryCalls, 0);
  assert.deepEqual(harness.ui.lastStatus, ["영상 위치를 확인하지 못했습니다", "error"]);
});
```

- [ ] **Step 2: Run Node tests and verify RED**

Run:

```powershell
node --test tests\web-ui\app.test.mjs
```

Expected: FAIL because the export, current-position start contract, and validation do not exist.

- [ ] **Step 3: Implement aligned position mapping**

Add:

```js
export function pcmStartByteForVideoTime(
  videoSeconds,
  bytesPerSecond = PCM_BYTES_PER_SECOND,
  frameBytes = AUDIO_FRAME_BYTES
) {
  if (!Number.isFinite(videoSeconds) || videoSeconds < 0) {
    throw new RangeError("invalid video time");
  }
  return Math.floor(videoSeconds * bytesPerSecond / frameBytes) * frameBytes;
}
```

Change `start` to accept only `{ delay }`. Call an injected synchronous `primePlayback()` before the first `await`; require it to return the captured current time. Store both `run.videoStartSeconds` and `run.pcmStartByte`. Convert mapping errors to a safe UI error before creating the WebSocket.

- [ ] **Step 4: Add failing tests for range fetch and no duration cap**

Add:

```js
test("current playback position selects the PCM range and is reused after preparation", async () => {
  const harness = createHarness({ playbackStartSeconds: 595, pcmBytes: 9600 });
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();
  assert.equal(await started, true);
  assert.equal(harness.fetchStartByte, 595 * 48000);
  assert.deepEqual(harness.player.seekCalls.at(-1), [595]);
});

test("stream length comes from remaining PCM rather than a duration option", async () => {
  const harness = createHarness({ pcmBytes: 14400 });
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();
  assert.equal(await started, true);
  assert.equal(harness.socket.sent.filter(value => value instanceof ArrayBuffer).length, 3);
});
```

- [ ] **Step 5: Run Node tests and verify RED**

Expected: FAIL because `fetchPcm` still receives a duration-derived byte count and `start` still assumes 565 seconds.

- [ ] **Step 6: Implement current-position range fetching and EOF lifetime**

Use:

```js
const pcm = await this._fetchPcm(run.pcmStartByte, run.abortController.signal);
// After socket open and before streaming:
run.player.seekAndPlay(run.videoStartSeconds);
await this._waitForPlayback(run, run.videoStartSeconds, true);
```

Calculate drift with:

```js
const expectedVideoTime = run.videoStartSeconds
  + frameNumber * this._frameIntervalMs / 1000;
```

Replace `fetchSelectedPcm(requestedBytes, signal)` with:

```js
async function fetchSelectedPcm(startByte, signal) {
  const response = await fetch("/test-assets/service.pcm", {
    headers: { Range: `bytes=${startByte}-` },
    signal,
    cache: "no-store"
  });
  if (response.status !== 206) {
    await response.body?.cancel();
    throw new PcmFetchError();
  }
  const pcm = await response.arrayBuffer();
  if (pcm.byteLength === 0 || pcm.byteLength % 2 !== 0) {
    throw new PcmFetchError();
  }
  return pcm;
}
```

The controller sends `end` only after PCM EOF; `stop()` still cancels without sending `end` and closes immediately.

- [ ] **Step 7: Update browser bootstrap**

The synchronous callback must capture current time and request playback:

```js
primePlayback: () => {
  if (!player) {
    throw new PlayerError();
  }
  const startSeconds = player.getCurrentTime();
  player.seekTo(startSeconds, true);
  player.playVideo();
  return startSeconds;
}
```

The start handler becomes:

```js
elements.startButton.addEventListener("click", () => {
  void controller.start({ delay: elements.delay.value });
});
```

- [ ] **Step 8: Run Node tests and verify GREEN**

Expected: all Node tests pass, including playback wait, buffer pacing, backpressure, stop, and safe error cases.

### Task 4: Replace history with a rolling two-line caption projector

**Files:**
- Modify: `src/ChurchSubtitle.Web/wwwroot/app.js`
- Modify: `src/ChurchSubtitle.Web/wwwroot/index.html`
- Modify: `src/ChurchSubtitle.Web/wwwroot/styles.css`
- Test: `tests/web-ui/app.test.mjs`

- [ ] **Step 1: Add failing two-line projection tests**

Use a fake UI with `renderLines(lines)` and add:

```js
test("partial occupies the lower line while the latest final stays above it", async () => {
  const harness = await createConnectedHarness();
  harness.socket.message(update("final", "line-a", 1, "첫 문장"));
  harness.socket.message(update("partial", "line-b", 1, "둘째 문장 초안"));
  assert.deepEqual(harness.ui.lines, ["첫 문장", "둘째 문장 초안"]);
});

test("three finals retain only the newest two in chronological order", async () => {
  const harness = await createConnectedHarness();
  harness.socket.message(update("final", "line-a", 1, "첫 문장"));
  harness.socket.message(update("final", "line-b", 1, "둘째 문장"));
  harness.socket.message(update("final", "line-c", 1, "셋째 문장"));
  assert.deepEqual(harness.ui.lines, ["둘째 문장", "셋째 문장"]);
});

test("final captions remain visible after completion", async () => {
  const harness = await createConnectedHarness();
  harness.socket.message(update("final", "line-a", 1, "남는 문장"));
  harness.socket.message(update("status", "status-1", 0, "raw", "server"));
  harness.socket.serverClose(1000);
  assert.deepEqual(harness.ui.lines, ["", "남는 문장"]);
});
```

- [ ] **Step 2: Run Node tests and verify RED**

Expected: FAIL because the UI currently exposes current/history methods rather than two rows.

- [ ] **Step 3: Implement the bounded two-line state**

Replace the final-history projection with a maximum-two recent final list. Render using:

```js
_renderCaptionLines() {
  const partial = this._activePartialLineId
    ? this._partialCaptions.get(this._activePartialLineId)?.text.trim()
    : "";
  const finals = this._recentFinals.map(item => item.text.trim()).filter(Boolean);
  const lines = partial
    ? [finals.at(-1) ?? "", partial]
    : [finals.at(-2) ?? "", finals.at(-1) ?? ""];
  this._ui.renderLines(lines);
}
```

On accepted final, upsert by `lineId`, move it to the newest position, and trim with `this._recentFinals = this._recentFinals.slice(-2)`. Retain watermark handling so stale partial/final events cannot replace newer text.

- [ ] **Step 4: Replace the HTML caption regions**

Remove `#current-caption`, `.history-panel`, and `#caption-history`. Add:

```html
<div id="caption-lines" class="caption-lines" aria-live="polite" aria-atomic="false">
  <p id="caption-line-1" class="caption-line" aria-label="자막 첫째 줄"></p>
  <p id="caption-line-2" class="caption-line" aria-label="자막 둘째 줄">시작하면 이곳에 자막이 표시됩니다.</p>
</div>
```

Remove the duration field. Change the source note to `영상을 원하는 위치로 이동한 뒤 시작하세요`.

- [ ] **Step 5: Implement stable two-line styling**

Replace current/history styles with:

```css
.caption-lines {
  display: grid;
  align-content: center;
  min-height: 7.2em;
  margin: clamp(24px, 4vw, 48px) auto 0;
}

.caption-line {
  min-height: 1.45em;
  margin: 0;
  color: #fff;
  font-size: clamp(1.55rem, 4vw, 3.25rem);
  font-weight: 800;
  line-height: 1.45;
  letter-spacing: -0.045em;
  text-align: center;
  word-break: keep-all;
}
```

Keep focus styles, responsive layout, video aspect ratio, and status colors.

- [ ] **Step 6: Implement DOM-safe line rendering**

Use only `textContent`:

```js
renderLines([first = "", second = ""]) {
  elements.captionLine1.textContent = first;
  elements.captionLine2.textContent = second;
}
```

Reset both rows without deleting the elements so the live region remains stable.

- [ ] **Step 7: Run Node tests and verify GREEN**

Run:

```powershell
node --test tests\web-ui\app.test.mjs
node --check src\ChurchSubtitle.Web\wwwroot\app.js
```

Expected: all behavior tests pass and syntax check exits `0`.

### Task 5: Update static contracts and documentation

**Files:**
- Modify: `tests/ChurchSubtitle.Core.Tests/WebUiAssetTests.cs`
- Modify: `tests/powershell/RunWeb.Tests.ps1`
- Modify: `README.md`

- [ ] **Step 1: Change static asset tests first**

Require `caption-line-1`, `caption-line-2`, `aria-live="polite"`, the current-time byte mapper, `bytes=${startByte}-`, and the absence of `id="duration"`, `VIDEO_START_SECONDS`, `caption-history`, and `innerHTML`.

Representative assertions:

```csharp
Assert.Contains("id=\"caption-line-1\"", html, StringComparison.Ordinal);
Assert.Contains("id=\"caption-line-2\"", html, StringComparison.Ordinal);
Assert.DoesNotContain("id=\"duration\"", html, StringComparison.Ordinal);
Assert.DoesNotContain("VIDEO_START_SECONDS", script, StringComparison.Ordinal);
Assert.Contains("pcmStartByteForVideoTime", script, StringComparison.Ordinal);
Assert.Contains("bytes=${startByte}-", script, StringComparison.Ordinal);
Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
```

- [ ] **Step 2: Run focused .NET test and verify RED**

Run:

```powershell
& (Join-Path $env:LOCALAPPDATA 'church-subtitle-tools\dotnet\dotnet.exe') test tests\ChurchSubtitle.Core.Tests\ChurchSubtitle.Core.Tests.csproj -c Release --filter FullyQualifiedName~WebUiAssetTests
```

Expected: FAIL against the old HTML/script contract until Tasks 3–4 are complete.

- [ ] **Step 3: Update README behavior**

Document:

- prepare/validate the full PCM under `data\poc-source-full`;
- move the YouTube playhead anywhere, click Start, and stream until Stop/video end;
- the browser maps the current time to local PCM and does not capture iframe audio;
- exactly two rolling lines are a UI policy; the API continues emitting `partial` and `final` updates;
- the production WebSocket still has no total duration cap;
- 10-second OpenAI commits are internal and never end the session.

Remove claims that the UI always starts at `09:25` or is limited to 30/120/900 seconds.

- [ ] **Step 4: Update PowerShell README assertions**

Require strings describing `현재 위치`, `2줄`, `/ws/captions`, `24 kHz`, `16-bit`, `mono`, `OPENAI_API_KEY`, and `Ctrl+C`; remove the old `30` and `09:25` requirements.

- [ ] **Step 5: Run focused contracts and verify GREEN**

Run:

```powershell
& (Join-Path $env:LOCALAPPDATA 'church-subtitle-tools\dotnet\dotnet.exe') test tests\ChurchSubtitle.Core.Tests\ChurchSubtitle.Core.Tests.csproj -c Release --filter FullyQualifiedName~WebUiAssetTests
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\powershell\RunWeb.Tests.ps1
```

Expected: xUnit focused tests pass; PowerShell prints `PASS`.

### Task 6: Full regression and local handoff

**Files:**
- Verify all modified files above.

- [ ] **Step 1: Stop and restart the local server**

Stop the existing `run-web.ps1` process with Ctrl+C, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-web.ps1
```

Expected: server listens only on `http://localhost:5287` and reports the local UI URL without exposing the key.

- [ ] **Step 2: Run all free automated verification**

Run:

```powershell
node --test tests\web-ui\app.test.mjs
node --check src\ChurchSubtitle.Web\wwwroot\app.js
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\powershell\ImportLocalEnv.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\powershell\RunWeb.Tests.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\powershell\SmokeCaptionWebSocket.Tests.ps1
& (Join-Path $env:LOCALAPPDATA 'church-subtitle-tools\dotnet\dotnet.exe') test ChurchSubtitle.sln -c Release --artifacts-path .artifacts\current-position-ui
& (Join-Path $env:LOCALAPPDATA 'church-subtitle-tools\dotnet\dotnet.exe') build ChurchSubtitle.sln -c Release --no-restore -warnaserror --artifacts-path .artifacts\current-position-ui
& (Join-Path $env:LOCALAPPDATA 'church-subtitle-tools\dotnet\dotnet.exe') format ChurchSubtitle.sln --verify-no-changes --no-restore
```

Expected: all suites pass; Release build has zero warnings/errors; format exits `0`.

- [ ] **Step 3: Perform a free local asset check**

Verify health and a nonzero range starting at 09:55:

```powershell
Invoke-WebRequest -UseBasicParsing http://127.0.0.1:5287/health
$startByte = 595 * 48000
Invoke-WebRequest -UseBasicParsing -Headers @{ Range = "bytes=$startByte-$($startByte + 4799)" } http://127.0.0.1:5287/test-assets/service.pcm
```

Expected: health `200`; PCM response `206` with exactly `4,800` bytes.

- [ ] **Step 4: User-visible live test**

Ask the user to hard-refresh `http://127.0.0.1:5287`, seek the YouTube player to 09:55, and click Start. Confirm that playback continues beyond 15 seconds, partial text updates in the lower row, finals roll upward, exactly two rows remain, and Stop ends the session. This step uses paid OpenAI audio only after the user's existing test authorization is still applicable.

- [ ] **Step 5: Secret and workspace check**

Scan tracked/source files while excluding `.env`, binaries, build artifacts, and source media:

```powershell
rg -n --hidden --glob '!.env' --glob '!data/**' --glob '!.artifacts/**' --glob '!bin/**' --glob '!obj/**' 'sk-[A-Za-z0-9_-]{20,}' .
git status --short
```

Expected: no API key match. Report modified/untracked files without committing them.
