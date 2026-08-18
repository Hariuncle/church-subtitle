# Inline Scrolling Caption Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render browser-test captions as one left-aligned, space-joined text flow that starts at the top and scrolls away exactly the overflowing visual line while the API remains unchanged.

**Architecture:** Keep the controller's revision/watermark logic, but project up to 64 recent final fragments plus the active partial into one normalized string. A single browser paragraph owns the fixed two-line viewport; after each `textContent` update it sets `scrollTop` to `max(0, scrollHeight - clientHeight)`, which naturally moves one line whenever a third wrapped line appears. Server and `CaptionUpdate` code remain untouched.

**Tech Stack:** Browser ES modules, HTML/CSS, Node.js built-in test runner, .NET 8 xUnit asset tests, PowerShell documentation contracts

---

### Task 1: Specify the continuous projection and scroll behavior in tests

**Files:**
- Modify: `tests/web-ui/app.test.mjs:400-545`
- Modify: `tests/ChurchSubtitle.Core.Tests/WebUiAssetTests.cs:8-82`

- [ ] **Step 1: Replace two-row expectations with one normalized caption string**

Update controller behavior tests to assert `harness.ui.caption`, including:

```javascript
assert.equal(harness.ui.caption, "첫 문장 둘째 문장 초안");
assert.equal(harness.ui.caption.includes("\n"), false);
```

For three finals expect `"첫 문장 둘째 문장 셋째 문장"`. Keep revision, blank-final, delayed-partial, correction, and reset coverage using a single string. Add a whitespace case:

```javascript
harness.socket.message(update("final", "line-a", 1, "  축복합니다.  "));
harness.socket.message(update("partial", "line-b", 1, " 사랑하는   성도 여러분 "));
assert.equal(harness.ui.caption, "축복합니다. 사랑하는 성도 여러분");
```

Add 65 short finals and assert the UI buffer contains lines 2 through 65 but not line 1, proving the 64-fragment memory bound without changing API delivery.

- [ ] **Step 2: Specify a single DOM paragraph and browser scroll math**

Replace the markup/CSS assertions with checks for exactly one `id="caption-text"`, no `caption-line-1`/`caption-line-2`, `text-align: left`, `block-size: 2.9em`, and `overflow: hidden`.

Use a fake text element with `clientHeight`, `scrollHeight`, and `scrollTop`, then assert:

```javascript
const captionText = new CountingTextElement({ clientHeight: 100, scrollHeight: 100 });
const ui = createBrowserUi({ captionText });
ui.renderCaption("축복합니다. 사랑하는 성도 여러분");
assert.equal(captionText.scrollTop, 0);

captionText.scrollHeight = 150;
ui.renderCaption("축복합니다. 사랑하는 성도 여러분 다음 문장");
assert.equal(captionText.scrollTop, 50);
```

Also assert the repeated identical text does not rewrite `textContent`, while scroll positioning still remains correct.

Update `WebUiAssetTests` to require `captionText.textContent`, `renderCaption`, and one `caption-text` element, and to reject the removed two row IDs.

- [ ] **Step 3: Run focused tests and confirm RED**

Run:

```powershell
node --test --test-name-pattern="space|scroll|single continuous|64" tests/web-ui/app.test.mjs
$dotnet = Join-Path $env:LOCALAPPDATA 'church-subtitle-tools\dotnet\dotnet.exe'
& $dotnet test tests/ChurchSubtitle.Core.Tests/ChurchSubtitle.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~WebUiAssetTests
```

Expected: FAIL because the current UI renders two centered block rows, stores only two finals, and has no overflow scroll positioning.

### Task 2: Implement one flowing caption paragraph

**Files:**
- Modify: `src/ChurchSubtitle.Web/wwwroot/app.js:1-825`
- Modify: `src/ChurchSubtitle.Web/wwwroot/index.html:56-59`
- Modify: `src/ChurchSubtitle.Web/wwwroot/styles.css:234-263`

- [ ] **Step 1: Project normalized fragments into one bounded string**

Add:

```javascript
const MAX_UI_FINAL_FRAGMENTS = 64;

function normalizeCaptionFragment(text) {
  return text.trim().replace(/\s+/g, " ");
}
```

Change final retention to:

```javascript
this._recentFinals = this._recentFinals.slice(-MAX_UI_FINAL_FRAGMENTS);
```

Replace `_renderCaptionLines()` with `_renderCaptionText()`:

```javascript
_renderCaptionText() {
  const partial = this._activePartialLineId
    ? normalizeCaptionFragment(this._partialCaptions.get(this._activePartialLineId)?.text ?? "")
    : "";
  const fragments = this._recentFinals
    .map(item => normalizeCaptionFragment(item.text))
    .filter(Boolean);
  if (partial) {
    fragments.push(partial);
  }
  this._ui.renderCaption(fragments.join(" "));
}
```

Call it from partial/final updates, preserving all watermark rules.

- [ ] **Step 2: Replace the two browser rows with one scrolling text element**

Use this markup inside the existing live region:

```html
<div id="caption-lines" class="caption-lines" aria-live="polite" aria-atomic="false">
  <p id="caption-text" class="caption-text" aria-label="실시간 자막">시작하면 이곳에 자막이 표시됩니다.</p>
</div>
```

Bootstrap with `captionText: document.getElementById("caption-text")`. Implement:

```javascript
resetCaptions() {
  elements.captionText.textContent = "";
  elements.captionText.scrollTop = 0;
},
renderCaption(text = "") {
  if (elements.captionText.textContent !== text) {
    elements.captionText.textContent = text;
  }
  elements.captionText.scrollTop = Math.max(
    0,
    elements.captionText.scrollHeight - elements.captionText.clientHeight
  );
}
```

- [ ] **Step 3: Make the visual viewport top-left aligned and exactly two lines tall**

Use:

```css
.caption-lines {
  margin: clamp(24px, 4vw, 48px) auto 0;
  font-size: clamp(1.55rem, 4vw, 3.25rem);
  font-weight: 800;
  line-height: 1.45;
  letter-spacing: -0.045em;
}

.caption-text {
  block-size: 2.9em;
  margin: 0;
  overflow: hidden;
  color: #fff;
  font: inherit;
  letter-spacing: inherit;
  text-align: left;
  word-break: break-word;
  word-break: keep-all;
  overflow-wrap: anywhere;
}
```

- [ ] **Step 4: Run focused and full Node/UI tests and confirm GREEN**

Run:

```powershell
node --test tests/web-ui/app.test.mjs
$dotnet = Join-Path $env:LOCALAPPDATA 'church-subtitle-tools\dotnet\dotnet.exe'
& $dotnet test tests/ChurchSubtitle.Core.Tests/ChurchSubtitle.Core.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~WebUiAssetTests
```

Expected: all Node tests and focused asset tests pass.

### Task 3: Update the API/UI documentation boundary and publish

**Files:**
- Modify: `README.md:96-123`
- Modify: `docs/api-and-test-ui.md:1-55`
- Modify: `tests/powershell/RunWeb.Tests.ps1:245-310`

- [ ] **Step 1: Add a failing documentation contract**

Require the README section and `docs/api-and-test-ui.md` to contain `왼쪽 위`, `공백 한 칸`, `맨 위 한 줄`, and `개별 CaptionUpdate`, while rejecting the stale claim `두 개의 안정된 DOM 행`.

Construct Korean expected text through `[char]` code points as the existing Windows PowerShell 5 test does, so the script remains encoding-safe.

- [ ] **Step 2: Run the documentation test and confirm RED**

Run: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/powershell/RunWeb.Tests.ps1`

Expected: FAIL because the existing documentation still describes two stable DOM rows.

- [ ] **Step 3: Document the new UI policy without changing the API contract**

State that the test UI joins discrete events using one space, starts at the top-left, and scrolls away only the overflowing top visual line. State separately that the API still emits discrete `CaptionUpdate` events continuously and does not return one ever-growing transcript string.

- [ ] **Step 4: Run all free verification**

Run:

```powershell
node --check src/ChurchSubtitle.Web/wwwroot/app.js
node --test tests/web-ui/app.test.mjs
$dotnet = Join-Path $env:LOCALAPPDATA 'church-subtitle-tools\dotnet\dotnet.exe'
& $dotnet test ChurchSubtitle.sln -c Release --no-restore
& $dotnet build ChurchSubtitle.sln -c Release --no-restore -warnaserror
& $dotnet format ChurchSubtitle.sln --verify-no-changes --no-restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/powershell/ImportLocalEnv.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/powershell/RunWeb.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/powershell/SmokeCaptionWebSocket.Tests.ps1
git diff --check
```

Expected: Node, .NET, and all PowerShell tests pass; Release build has zero warnings/errors; format and diff checks are clean.

- [ ] **Step 5: Commit, merge, and publish**

```powershell
git add README.md docs/api-and-test-ui.md docs/superpowers/plans/2026-08-19-inline-scrolling-caption.md src/ChurchSubtitle.Web/wwwroot/app.js src/ChurchSubtitle.Web/wwwroot/index.html src/ChurchSubtitle.Web/wwwroot/styles.css tests/ChurchSubtitle.Core.Tests/WebUiAssetTests.cs tests/powershell/RunWeb.Tests.ps1 tests/web-ui/app.test.mjs
git commit -m "Render captions as an inline scrolling flow"
git push origin main
```

Expected: local and remote `main` point to the new commit, the worktree is clean, and `.env` plus media remain ignored.
