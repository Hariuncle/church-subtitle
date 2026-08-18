# Visual Two-Line Caption Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the local test UI's caption stage to exactly two visible wrapped text lines while leaving the continuous WebSocket caption API unchanged.

**Architecture:** Preserve the existing two stable caption DOM nodes and JavaScript partial/final projection. Move the shared caption typography to the containing stage, make that stage exactly two line boxes high, bottom-align its non-shrinking children, and clip only the older overflow above. Lock the behavior with a static CSS contract test and clarify the UI/API separation in the README.

**Tech Stack:** HTML/CSS, Node.js built-in test runner, .NET 8 xUnit asset tests, PowerShell launcher tests

---

### Task 1: Add a failing visual-line CSS contract

**Files:**
- Modify: `tests/web-ui/app.test.mjs:503`

- [ ] **Step 1: Replace the old minimum-height assertion with the exact two-line-window contract**

```javascript
test("caption markup and styles expose exactly two visible broadcast lines", () => {
  assert.match(htmlSource, /id="caption-lines"[^>]*aria-live="polite"[^>]*aria-atomic="false"/);
  assert.match(htmlSource, /id="caption-line-1"[^>]*aria-label="자막 첫째 줄"/);
  assert.match(htmlSource, /id="caption-line-2"[^>]*aria-label="자막 둘째 줄"/);
  assert.equal((htmlSource.match(/id="caption-line-/g) ?? []).length, 2);
  assert.doesNotMatch(htmlSource, /caption-history/);
  assert.doesNotMatch(htmlSource, /current-caption/);
  assert.match(cssSource, /\.caption-lines\s*\{[^}]*display:\s*flex;[^}]*flex-direction:\s*column;[^}]*justify-content:\s*flex-end;[^}]*block-size:\s*2\.9em;[^}]*overflow:\s*hidden;/s);
  assert.match(cssSource, /\.caption-lines\s*\{[^}]*font-size:\s*clamp\([^}]*line-height:\s*1\.45;/s);
  assert.match(cssSource, /\.caption-line\s*\{[^}]*flex:\s*0 0 auto;[^}]*font:\s*inherit;/s);
  assert.match(cssSource, /\.caption-line\s*\{[^}]*word-break:\s*break-word;[^}]*word-break:\s*keep-all;[^}]*overflow-wrap:\s*anywhere;/s);
});
```

- [ ] **Step 2: Run the focused test and confirm RED**

Run: `node --test --test-name-pattern="exactly two visible broadcast lines" tests/web-ui/app.test.mjs`

Expected: FAIL because `.caption-lines` is still a centered grid with `min-height: 7.2em` and does not have the fixed clipped two-line window.

### Task 2: Implement the fixed bottom-aligned two-line window

**Files:**
- Modify: `src/ChurchSubtitle.Web/wwwroot/styles.css:234-254`

- [ ] **Step 1: Give the stage the typography and exact two-line clipping behavior**

```css
.caption-lines {
  display: flex;
  flex-direction: column;
  justify-content: flex-end;
  block-size: 2.9em;
  margin: clamp(24px, 4vw, 48px) auto 0;
  overflow: hidden;
  font-size: clamp(1.55rem, 4vw, 3.25rem);
  font-weight: 800;
  line-height: 1.45;
  letter-spacing: -0.045em;
  text-align: center;
}

.caption-line {
  flex: 0 0 auto;
  margin: 0;
  color: #fff;
  font: inherit;
  letter-spacing: inherit;
  text-align: inherit;
  word-break: break-word;
  word-break: keep-all;
  overflow-wrap: anywhere;
}
```

- [ ] **Step 2: Run the focused test and confirm GREEN**

Run: `node --test --test-name-pattern="exactly two visible broadcast lines" tests/web-ui/app.test.mjs`

Expected: PASS.

- [ ] **Step 3: Run the complete browser behavior suite**

Run: `node --test tests/web-ui/app.test.mjs`

Expected: all tests pass, including the unchanged continuous partial/final projection tests.

### Task 3: Clarify the local UI/API boundary and verify the repository

**Files:**
- Modify: `README.md:106`
- Create: `docs/api-and-test-ui.md`
- Modify: `tests/powershell/RunWeb.Tests.ps1:180-230`
- Verify only: `src/ChurchSubtitle.Web/wwwroot/app.js`
- Verify only: `src/ChurchSubtitle.Web/Endpoints/CaptionWebSocketEndpoint.cs`

- [ ] **Step 1: Clarify that two lines means two rendered lines, not two retained API events**

```markdown
세션은 화면의 `중지`를 누르거나 영상이 끝날 때까지 계속됩니다. 테스트 UI는 줄바꿈 결과를 포함해 화면에 보이는 최신 텍스트를 최대 `2줄` 높이로 잘라 표시하지만, 이는 UI의 rolling projection일 뿐입니다. WebSocket API는 화면 줄 수와 무관하게 세션 동안 `partial`/`final` 이벤트를 계속 내보내므로 운영 클라이언트는 필요한 누적·표시 정책을 별도로 적용할 수 있습니다.
```

- [ ] **Step 2: Add a dedicated API-versus-test-UI contract document and link it from the README**

Create `docs/api-and-test-ui.md` with a comparison table that identifies the production integration boundary (`/ws/captions`, live PCM input, continuous `CaptionUpdate` events, no two-line limit) separately from the local verification tool (YouTube iframe, prepared PCM Range, exactly two visible lines). Include the start/binary/end client protocol, `partial` revision semantics, `final` semantics, and state explicitly that a production sink chooses its own history and line-breaking policy.

Add a README link named `운영 API와 테스트 UI 구분` and extend `RunWeb.Tests.ps1` to require that link plus the phrases `운영 WebSocket API`, `로컬 테스트 UI`, `CaptionUpdate`, and `2줄 제한 없음` in the referenced document.

- [ ] **Step 3: Confirm production JavaScript and endpoint contain no new two-line API limit**

Run: `rg -n "slice\(-2\)|caption-line|block-size" src/ChurchSubtitle.Web/wwwroot/app.js src/ChurchSubtitle.Web/Endpoints/CaptionWebSocketEndpoint.cs`

Expected: the existing UI-only `recentFinals.slice(-2)` may appear in `app.js`; no caption-line or two-line limitation appears in the endpoint.

- [ ] **Step 4: Run all free verification**

Run:

```powershell
node --check src/ChurchSubtitle.Web/wwwroot/app.js
node --test tests/web-ui/app.test.mjs
dotnet test ChurchSubtitle.sln -c Release --no-restore
dotnet build ChurchSubtitle.sln -c Release --no-restore -warnaserror
dotnet format ChurchSubtitle.sln --verify-no-changes --no-restore
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/powershell/ImportLocalEnv.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/powershell/RunWeb.Tests.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/powershell/SmokeCaptionWebSocket.Tests.ps1
git diff --check
```

Expected: every command exits 0, all Node/.NET/PowerShell tests pass, and build/format produce no warnings or changes.

- [ ] **Step 5: Commit and publish the verified UI correction**

```powershell
git add tests/web-ui/app.test.mjs tests/powershell/RunWeb.Tests.ps1 src/ChurchSubtitle.Web/wwwroot/styles.css README.md docs/api-and-test-ui.md docs/superpowers/plans/2026-08-19-visual-two-line-caption-window.md
git commit -m "Fix caption stage to two visible lines"
git push origin main
```

Expected: `main` and `origin/main` point to the new commit; `.env` and media remain ignored and untracked.
