import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const appSource = await readFile(new URL("../../src/ChurchSubtitle.Web/wwwroot/app.js", import.meta.url), "utf8");
const htmlSource = await readFile(new URL("../../src/ChurchSubtitle.Web/wwwroot/index.html", import.meta.url), "utf8");
const cssSource = await readFile(new URL("../../src/ChurchSubtitle.Web/wwwroot/styles.css", import.meta.url), "utf8");
const appModuleUrl = `data:text/javascript;base64,${Buffer.from(appSource).toString("base64")}`;
const {
  CaptionSessionController,
  TOTAL_PCM_BYTES,
  createBrowserUi,
  fetchSelectedPcm,
  pcmStartByteForVideoTime
} = await import(appModuleUrl);

test("video time maps down to an aligned PCM frame", () => {
  assert.equal(pcmStartByteForVideoTime(0), 0);
  assert.equal(pcmStartByteForVideoTime(2.3), 23 * 4800);
  assert.equal(pcmStartByteForVideoTime(595.099), 595 * 48000);
  assert.equal(pcmStartByteForVideoTime(595.1), 5951 * 4800);
});

test("nonfinite and negative video times are rejected", () => {
  for (const value of [Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY, -0.001]) {
    assert.throws(() => pcmStartByteForVideoTime(value), RangeError);
  }
});

test("invalid video time is rejected before PCM or socket preparation", async () => {
  const harness = createHarness({ playbackStartSeconds: Number.NaN });
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  const fetchCalls = harness.fetchCalls;
  const socketFactoryCalls = harness.socketFactoryCalls;
  if (socketFactoryCalls > 0) {
    harness.controller.stop();
  }

  assert.equal(await started, false);
  assert.equal(fetchCalls, 0);
  assert.equal(socketFactoryCalls, 0);
  assert.deepEqual(harness.ui.lastStatus, ["영상 위치를 확인하지 못했습니다", "error"]);
});

test("duplicate start is ignored while a session is active", async () => {
  const harness = createHarness({ fetchDeferred: true });

  const firstStart = harness.controller.start({ delay: "low" });
  const duplicateResult = await harness.controller.start({ delay: "medium" });

  assert.equal(duplicateResult, false);
  assert.equal(harness.fetchCalls, 1);
  harness.controller.stop();
  await firstStart;
});

test("start primes YouTube playback synchronously before async preparation", async () => {
  const harness = createHarness({ fetchDeferred: true });

  const started = harness.controller.start({ delay: "low" });

  assert.equal(harness.primePlaybackCalls, 1);
  assert.deepEqual(harness.player.seekCalls, [[565, true]]);
  assert.equal(harness.player.playCalls, 1);
  harness.controller.stop();
  await started;
});

test("current playback position selects the PCM range and is reused after preparation", async () => {
  const harness = createHarness({ playbackStartSeconds: 595.099, pcmBytes: 9600 });
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

test("selected PCM fetch requests the aligned range through EOF", async () => {
  const originalFetch = globalThis.fetch;
  const startByte = 595 * 48000;
  const expectedBytes = 70_368_000 - startByte;
  let request = null;
  globalThis.fetch = async (url, options) => {
    request = { url, options };
    return createFetchResponse({
      contentRange: `bytes ${startByte}-${70_368_000 - 1}/70368000`,
      contentLength: String(expectedBytes),
      chunks: [new Uint8Array(9600)]
    });
  };

  try {
    const pcmSource = await fetchSelectedPcm(startByte, new AbortController().signal);
    assert.equal(TOTAL_PCM_BYTES, 70_368_000);
    assert.equal(pcmSource.expectedBytes, expectedBytes);
    assert.equal(typeof pcmSource.reader.read, "function");
    assert.equal(request.url, "/test-assets/service.pcm");
    assert.deepEqual(request.options.headers, { Range: `bytes=${startByte}-` });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("selected PCM fetch rejects invalid status, body, and byte length metadata", async () => {
  const originalFetch = globalThis.fetch;
  const cases = [
    { status: 200, contentRange: "bytes 0-9599/9600", chunks: [new Uint8Array(9600)] },
    {
      contentRange: "bytes 0-70367999/70368000",
      contentLength: "70368000",
      body: null
    },
    {
      contentRange: "bytes 1-70367999/70368000",
      contentLength: "70367999",
      chunks: [new Uint8Array(1)]
    }
  ];

  try {
    for (const responseCase of cases) {
      let cancelled = false;
      globalThis.fetch = async () => createFetchResponse({
        ...responseCase,
        onCancel: () => { cancelled = true; }
      });
      await assert.rejects(
        fetchSelectedPcm(0, new AbortController().signal),
        error => error?.constructor?.name === "PcmFetchError"
      );
      if (responseCase.body !== null) {
        assert.equal(cancelled, true);
      }
    }
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("selected PCM fetch rejects missing, malformed, and wrong-start Content-Range", async () => {
  const originalFetch = globalThis.fetch;
  const contentRanges = [
    null,
    "bytes nope",
    "bytes 4800-70367999/*",
    "bytes 1-70367999/70368000"
  ];

  try {
    for (const contentRange of contentRanges) {
      globalThis.fetch = async () => createFetchResponse({
        contentRange,
        contentLength: "70368000",
        chunks: [new Uint8Array(9600)]
      });
      await assert.rejects(
        fetchSelectedPcm(0, new AbortController().signal),
        error => error?.constructor?.name === "PcmFetchError"
      );
    }
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("selected PCM fetch rejects a self-consistent noncanonical total and a nonfinal end", async () => {
  const originalFetch = globalThis.fetch;
  const cases = [
    { contentRange: "bytes 0-9599/9600", contentLength: "9600" },
    { contentRange: "bytes 0-70367998/70368000", contentLength: "70367999" }
  ];

  try {
    for (const responseCase of cases) {
      globalThis.fetch = async () => createFetchResponse({
        ...responseCase,
        chunks: [new Uint8Array(2)]
      });
      await assert.rejects(
        fetchSelectedPcm(0, new AbortController().signal),
        error => error?.constructor?.name === "PcmFetchError"
      );
    }
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("selected PCM fetch rejects missing, malformed, and wrong Content-Length", async () => {
  const originalFetch = globalThis.fetch;
  const contentLengths = [null, "not-a-number", "70367999", "70368001"];

  try {
    for (const contentLength of contentLengths) {
      globalThis.fetch = async () => createFetchResponse({
        contentRange: "bytes 0-70367999/70368000",
        contentLength,
        chunks: [new Uint8Array(2)]
      });
      await assert.rejects(
        fetchSelectedPcm(0, new AbortController().signal),
        error => error?.constructor?.name === "PcmFetchError"
      );
    }
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("selected PCM fetch rejects starts outside the canonical byte range without fetching", async () => {
  const originalFetch = globalThis.fetch;
  let fetchCalls = 0;
  globalThis.fetch = async () => {
    fetchCalls += 1;
    throw new Error("fetch must not be called");
  };

  try {
    for (const startByte of [-1, 70_368_000, 70_372_800]) {
      await assert.rejects(
        fetchSelectedPcm(startByte, new AbortController().signal),
        error => error?.constructor?.name === "PcmFetchError"
      );
    }
    assert.equal(fetchCalls, 0);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("invalid Content-Range prevents WebSocket creation", async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async () => createFetchResponse({
    contentRange: "bytes 27120000-27129599/27129600",
    contentLength: "9600",
    chunks: [new Uint8Array(9600)]
  });

  try {
    const harness = createHarness({ fetchPcm: fetchSelectedPcm });
    const started = harness.controller.start({ delay: "low" });
    await flushMicrotasks();
    const socketFactoryCalls = harness.socketFactoryCalls;
    if (socketFactoryCalls > 0) {
      harness.controller.stop();
    }
    assert.equal(await started, false);
    assert.equal(socketFactoryCalls, 0);
    assert.deepEqual(harness.ui.lastStatus, ["테스트 오디오를 불러오지 못했습니다", "error"]);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("canonical EOF position is rejected before WebSocket creation", async () => {
  const harness = createHarness({
    playbackStartSeconds: 70_368_000 / 48000,
    fetchPcm: fetchSelectedPcm
  });

  assert.equal(await harness.controller.start({ delay: "low" }), false);
  assert.equal(harness.socketFactoryCalls, 0);
  assert.deepEqual(harness.ui.lastStatus, ["테스트 오디오를 불러오지 못했습니다", "error"]);
});

test("player preparation failure cancels an already-open PCM reader", async () => {
  const harness = createHarness({ playerPreparationError: true });

  assert.equal(await harness.controller.start({ delay: "low" }), false);
  assert.equal(harness.socketFactoryCalls, 0);
  assert.equal(harness.pcmReader.cancelCalls, 1);
  assert.equal(harness.pcmReader.releaseCalls, 1);
});

test("truncated and overlong PCM streams fail without sending end", async () => {
  for (const pcmBytes of [4800, 14400]) {
    const harness = createHarness({ pcmBytes, reportedPcmBytes: 9600 });
    const started = harness.controller.start({ delay: "low" });
    await flushMicrotasks();
    harness.socket.open();

    assert.equal(await started, false);
    assert.equal(harness.socket.sent.includes('{"type":"end"}'), false);
    assert.equal(harness.pcmReader.cancelCalls, 1);
    assert.equal(harness.pcmReader.releaseCalls, 1);
    assert.deepEqual(harness.ui.lastStatus, ["테스트 오디오를 불러오지 못했습니다", "error"]);
  }
});

test("arbitrary stream chunks assemble exact frames and a final even remainder", async () => {
  const harness = createHarness({ pcmChunkBytes: [1000, 3900, 5001, 3199, 500] });
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();

  assert.equal(await started, true);
  assert.deepEqual(
    harness.socket.sent.filter(value => value instanceof ArrayBuffer).map(value => value.byteLength),
    [4800, 4800, 4000]
  );
});

test("application script no longer declares a fixed start or duration option", () => {
  assert.equal(appSource.includes("VIDEO_START_SECONDS"), false);
  assert.equal(appSource.includes("VALID_DURATIONS"), false);
  assert.equal(appSource.includes("durationSeconds"), false);
});

test("protocol sends strict start, binary chunks, then strict end", async () => {
  const harness = createHarness();
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();

  assert.equal(await started, true);
  assert.deepEqual(harness.socket.sent.map(value => typeof value === "string" ? value : value.byteLength), [
    '{"type":"start","delay":"low"}',
    4800,
    4800,
    '{"type":"end"}'
  ]);
});

test("stop during CONNECTING closes immediately and prevents traffic", async () => {
  const harness = createHarness();
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();

  assert.equal(harness.socket.readyState, FakeSocket.CONNECTING);
  harness.controller.stop();

  assert.equal(harness.socket.readyState, FakeSocket.CLOSED);
  assert.deepEqual(harness.socket.closeCalls, [{ code: 1000, reason: "client stopped" }]);
  assert.equal(await started, false);
  assert.deepEqual(harness.socket.sent, []);
  assert.equal(harness.player.pauseCalls, 1);
  assert.equal(harness.ui.running, false);
});

test("connection timeout restores controls and closes CONNECTING immediately", async () => {
  const harness = createHarness();
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();

  harness.scheduler.advance(5000);

  assert.equal(await started, false);
  assert.deepEqual(harness.ui.lastStatus, ["자막 연결을 시작하지 못했습니다", "error"]);
  assert.equal(harness.ui.running, false);
  assert.equal(harness.socket.closeCalls.length, 1);
  assert.equal(harness.socket.readyState, FakeSocket.CLOSED);
});

test("throwing CONNECTING close retains a late-open close fallback", async () => {
  const harness = createHarness({ throwOnConnectingClose: true });
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();

  harness.controller.stop();

  assert.equal(harness.socket.closeAttempts, 1);
  assert.equal(harness.socket.closeCalls.length, 0);
  harness.socket.open();
  assert.equal(await started, false);
  assert.equal(harness.socket.closeAttempts, 2);
  assert.deepEqual(harness.socket.closeCalls, [{ code: 1000, reason: "client stopped" }]);
});

test("stop closes an open socket, pauses video, and restores controls", async () => {
  const harness = createHarness();
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();
  await started;

  harness.controller.stop();

  assert.equal(harness.socket.closeCalls[0].code, 1000);
  assert.equal(harness.player.pauseCalls, 1);
  assert.equal(harness.ui.running, false);
  assert.deepEqual(harness.ui.lastStatus, ["중지됨", "idle"]);
});

test("backpressure wait is bounded and aborts without queueing audio", async () => {
  const harness = createHarness({ bufferedAmount: 300000, backpressureTimeoutMs: 100 });
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();

  assert.equal(await started, false);
  assert.equal(harness.socket.sent.length, 1, "only the start command may be sent");
  assert.ok(harness.waitCalls <= 6, `bounded wait count was ${harness.waitCalls}`);
  assert.equal(harness.ui.lastStatus[1], "error");
});

test("final and partial fragments form one space-joined caption flow", async () => {
  const harness = await createConnectedHarness();

  harness.socket.message(update("final", "line-a", 1, "첫 문장"));
  harness.socket.message(update("partial", "line-b", 1, "둘째 문장 초안"));

  assert.equal(harness.ui.caption, "첫 문장 둘째 문장 초안");
  assert.equal(harness.ui.caption.includes("\n"), false);
});

test("final fragments remain a single continuous caption flow", async () => {
  const harness = await createConnectedHarness();

  harness.socket.message(update("final", "line-a", 1, "첫 문장"));
  harness.socket.message(update("final", "line-b", 1, "둘째 문장"));
  harness.socket.message(update("final", "line-c", 1, "셋째 문장"));

  assert.equal(harness.ui.caption, "첫 문장 둘째 문장 셋째 문장");
});

test("caption fragments normalize surrounding and repeated whitespace to one space", async () => {
  const harness = await createConnectedHarness();

  harness.socket.message(update("final", "line-a", 1, "  축복합니다.  "));
  harness.socket.message(update("partial", "line-b", 1, " 사랑하는   성도\n여러분 "));

  assert.equal(harness.ui.caption, "축복합니다. 사랑하는 성도 여러분");
});

test("browser caption projection retains a bounded 64 final fragments", async () => {
  const harness = await createConnectedHarness();

  for (let index = 1; index <= 65; index += 1) {
    harness.socket.message(update("final", `line-${index}`, 1, `문장${index}`));
  }

  assert.equal(harness.ui.caption.startsWith("문장2 "), true);
  assert.equal(harness.ui.caption.endsWith(" 문장65"), true);
  assert.equal(harness.ui.caption.includes("문장1 "), false);
});

test("a revised final is upserted by lineId and moved to newest", async () => {
  const harness = await createConnectedHarness();

  harness.socket.message(update("final", "line-a", 1, "첫 문장"));
  harness.socket.message(update("final", "line-b", 1, "둘째 문장"));
  harness.socket.message(update("final", "line-a", 1, "오래된 확정"));
  harness.socket.message(update("final", "line-a", 2, "수정된 첫 문장"));

  assert.equal(harness.ui.caption, "둘째 문장 수정된 첫 문장");
});

test("a delayed partial cannot resurrect a finalized line", async () => {
  const harness = await createConnectedHarness();

  harness.socket.message(update("final", "line-a", 3, "확정 문장"));
  harness.socket.message(update("partial", "line-a", 2, "늦은 초안"));
  harness.socket.message(update("partial", "line-a", 3, "같은 revision 초안"));
  harness.socket.message(update("partial", "line-a", 4, "높은 revision 초안"));

  assert.equal(harness.ui.caption, "확정 문장");
});

test("higher-revision final corrections remain accepted for a finalized line", async () => {
  const harness = await createConnectedHarness();

  harness.socket.message(update("final", "line-a", 3, "확정 문장"));
  harness.socket.message(update("final", "line-a", 4, "수정 확정 문장"));

  assert.equal(harness.ui.caption, "수정 확정 문장");
});

test("blank finals finalize their lines without consuming visible final slots", async () => {
  const harness = await createConnectedHarness();

  harness.socket.message(update("final", "line-a", 1, "첫 문장"));
  harness.socket.message(update("final", "line-b", 1, "   \n  "));
  harness.socket.message(update("final", "line-c", 1, "셋째 문장"));

  assert.equal(harness.ui.caption, "첫 문장 셋째 문장");
  harness.socket.message(update("partial", "line-b", 2, "되살아난 빈 줄"));
  assert.equal(harness.ui.caption, "첫 문장 셋째 문장");
});

test("an authoritative blank final removes an earlier visible revision", async () => {
  const harness = await createConnectedHarness();

  harness.socket.message(update("final", "line-a", 1, "삭제될 문장"));
  harness.socket.message(update("final", "line-b", 1, "남는 문장"));
  harness.socket.message(update("final", "line-a", 2, "\t "));

  assert.equal(harness.ui.caption, "남는 문장");
  harness.socket.message(update("partial", "line-a", 3, "되살아난 문장"));
  assert.equal(harness.ui.caption, "남는 문장");
});

test("an older final cannot delete a newer partial", async () => {
  const harness = await createConnectedHarness();

  harness.socket.message(update("partial", "line-a", 4, "최신 초안"));
  harness.socket.message(update("final", "line-a", 3, "뒤늦은 확정"));

  assert.equal(harness.ui.caption, "최신 초안");
});

test("starting a session resets the continuous caption flow", async () => {
  const harness = createHarness({ fetchDeferred: true });
  harness.ui.caption = "이전 첫 줄 이전 둘째 줄";

  const started = harness.controller.start({ delay: "low" });

  assert.equal(harness.ui.caption, "");
  harness.controller.stop();
  await started;
});

test("caption markup and styles expose one top-left two-line viewport", () => {
  assert.match(htmlSource, /id="caption-lines"[^>]*aria-live="polite"[^>]*aria-atomic="false"/);
  assert.match(htmlSource, /id="caption-text"[^>]*aria-label="실시간 자막"/);
  assert.equal((htmlSource.match(/id="caption-text"/g) ?? []).length, 1);
  assert.doesNotMatch(htmlSource, /id="caption-line-[12]"/);
  assert.doesNotMatch(htmlSource, /caption-history/);
  assert.doesNotMatch(htmlSource, /current-caption/);
  assert.doesNotMatch(htmlSource, /id="duration"/);
  assert.doesNotMatch(cssSource, /\.caption-history/);
  assert.match(cssSource, /\.caption-lines\s*\{[^}]*font-size:\s*clamp\([^}]*line-height:\s*1\.45;/s);
  assert.match(cssSource, /\.caption-text\s*\{[^}]*block-size:\s*2\.9em;[^}]*overflow:\s*hidden;[^}]*text-align:\s*left;/s);
  assert.match(cssSource, /\.caption-text\s*\{[^}]*word-break:\s*break-word;[^}]*word-break:\s*keep-all;[^}]*overflow-wrap:\s*anywhere;/s);
});

test("browser caption rendering mutates one continuous text node only", () => {
  assert.equal(appSource.includes("innerHTML"), false);
  assert.equal(appSource.includes("replaceChildren"), false);
  assert.equal(appSource.includes('document.createElement("li")'), false);
  assert.match(appSource, /renderCaption\(text = ""\)/);
  assert.match(appSource, /captionText\.textContent = text/);
});

test("browser caption rendering scrolls only the overflowing top visual line", () => {
  const captionText = new CountingTextElement({ clientHeight: 100, scrollHeight: 100 });
  const ui = createBrowserUi({ captionText });

  ui.renderCaption("축복합니다. 사랑하는 성도 여러분");
  assert.equal(captionText.scrollTop, 0);

  captionText.scrollHeight = 150;
  ui.renderCaption("축복합니다. 사랑하는 성도 여러분 다음 문장");
  ui.renderCaption("축복합니다. 사랑하는 성도 여러분 다음 문장");

  assert.equal(captionText.textContent, "축복합니다. 사랑하는 성도 여러분 다음 문장");
  assert.equal(captionText.scrollTop, 50);
  assert.equal(captionText.setCalls, 2);
});

test("privacy notice discloses OpenAI transcription and local-server-only API key storage", () => {
  assert.match(htmlSource, /오디오는[^<]*브라우저[^<]*로컬 자막 서버[^<]*OpenAI[^<]*전사/);
  assert.match(htmlSource, /API 키는[^<]*로컬 서버에만/);
  assert.equal(htmlSource.includes("브라우저와 로컬 자막 서버 사이에서만 전송"), false);
});

test("initial player load failure reports a safe error and keeps start disabled", () => {
  const harness = createHarness();

  harness.controller.handleExternalPlayerError();

  assert.equal(harness.ui.running, false);
  assert.equal(harness.ui.playerAvailable, false);
  assert.deepEqual(harness.ui.lastStatus, ["영상 플레이어를 불러오지 못했습니다", "error"]);
});

test("browser controls only enable start when the player is available", () => {
  const elements = {
    delay: { disabled: false },
    startButton: { disabled: false },
    stopButton: { disabled: false }
  };
  const ui = createBrowserUi(elements);

  ui.setPlayerAvailable(false);
  ui.setControlsRunning(false);
  assert.equal(elements.startButton.disabled, true);
  assert.equal(elements.stopButton.disabled, true);

  ui.setPlayerAvailable(true);
  ui.setControlsRunning(false);
  assert.equal(elements.startButton.disabled, false);

  ui.setControlsRunning(true);
  assert.equal(elements.startButton.disabled, true);
  assert.equal(elements.stopButton.disabled, false);
});

test("PCM waits until delayed initial playback is PLAYING", async () => {
  const harness = createHarness({
    playerStateAt: now => now < 500 ? "paused" : "playing",
    playerTimeAt: now => 565 + Math.max(0, now - 500) / 1000
  });
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();

  assert.equal(await started, true);
  assert.equal(harness.socket.sentAt[0], 0, "start command is sent before playback wait");
  assert.equal(harness.socket.sentAt[1], 500, "first PCM waits for PLAYING");
});

test("temporary buffering shifts pacing origin without a catch-up burst", async () => {
  const harness = createHarness({
    pcmBytes: 14400,
    playerStateAt: now => now >= 100 && now < 600 ? "buffering" : "playing",
    playerTimeAt: now => now < 100
      ? 565 + now / 1000
      : 565.1 + Math.max(0, now - 600) / 1000
  });
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();

  assert.equal(await started, true);
  assert.deepEqual(harness.socket.sentAt.slice(1, 4), [0, 600, 700]);
});

test("transient socket congestion pauses playback and resumes without a catch-up burst", async () => {
  const harness = createHarness({
    pcmBytes: 14400,
    bufferedAmountAt: now => now >= 100 && now < 600 ? 300000 : 0,
    playerTimeAt: now => now < 100
      ? 565 + now / 1000
      : 565.1 + Math.max(0, now - 600) / 1000
  });
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();

  assert.equal(await started, true);
  assert.equal(harness.player.pauseCalls, 1);
  assert.deepEqual(harness.player.seekCalls.at(-1), [565.1]);
  assert.deepEqual(harness.socket.sentAt.slice(1, 4), [0, 600, 700]);
});

test("initial ended playback state fails immediately", async () => {
  const harness = createHarness({ playerStateAt: () => "ended" });
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();

  assert.equal(await started, false);
  assert.equal(harness.scheduler.now, 0);
  assert.equal(harness.socket.sent.length, 1);
  assert.deepEqual(harness.ui.lastStatus, ["영상 재생을 동기화하지 못했습니다", "error"]);
});

test("playback resume timeout aborts before server idle timeout", async () => {
  const harness = createHarness({ playerStateAt: () => "paused" });
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();

  assert.equal(await started, false);
  assert.equal(harness.scheduler.now, 8000);
  assert.ok(harness.scheduler.now < 15000);
  assert.equal(harness.socket.sent.length, 1);
  assert.deepEqual(harness.ui.lastStatus, ["영상 재생을 동기화하지 못했습니다", "error"]);
});

test("material playback drift aborts instead of streaming desynchronized audio", async () => {
  let timeReads = 0;
  const harness = createHarness({
    playerTimeAt: () => timeReads++ === 0 ? 565 : 570
  });
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();

  assert.equal(await started, false);
  assert.equal(harness.socket.sent.length, 1);
  assert.deepEqual(harness.ui.lastStatus, ["영상 재생을 동기화하지 못했습니다", "error"]);
});

test("server errors and unknown payloads never expose raw server text", async () => {
  const harness = await createConnectedHarness();
  const rawError = "RAW_SERVER_SECRET";
  const rawUnknown = "RAW_UNKNOWN_PAYLOAD";

  harness.socket.message(JSON.stringify({ state: "unknown", text: rawUnknown }));
  harness.socket.message(update("status", "status-1", 0, rawError, "server:error"));

  assert.deepEqual(harness.ui.lastStatus, ["서버 처리 오류", "error"]);
  assert.equal(harness.ui.running, false);
  assert.equal(harness.socket.closeCalls.length, 1);
  assert.equal(JSON.stringify(harness.ui).includes(rawError), false);
  assert.equal(JSON.stringify(harness.ui).includes(rawUnknown), false);
});

test("normal server completion closes the run and restores controls", async () => {
  const harness = await createConnectedHarness();

  harness.socket.message(update("final", "line-a", 1, "남는 첫 문장"));
  harness.socket.message(update("final", "line-b", 1, "남는 둘째 문장"));
  harness.socket.message(update("status", "status-1", 0, "raw completed text", "server"));
  harness.socket.serverClose(1000);

  assert.equal(harness.ui.running, false);
  assert.deepEqual(harness.ui.lastStatus, ["처리 완료", "complete"]);
  assert.equal(harness.ui.caption, "남는 첫 문장 남는 둘째 문장");
  assert.equal(JSON.stringify(harness.ui).includes("raw completed text"), false);
});

test("abnormal server close reports a phase-aware safe failure", async () => {
  const harness = await createConnectedHarness();

  harness.socket.serverClose(1011);

  assert.equal(harness.ui.running, false);
  assert.deepEqual(harness.ui.lastStatus, ["자막 처리를 완료하지 못했습니다", "error"]);
});

test("midstream stop aborts the pending frame without sending end", async () => {
  const harness = createHarness({ pcmBytes: 14400, blockWaitAt: 100 });
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();
  await flushUntil(() => harness.socket.sent.some(value => value instanceof ArrayBuffer));

  assert.equal(harness.socket.sent.filter(value => value instanceof ArrayBuffer).length, 1);
  harness.controller.stop();

  assert.equal(await started, false);
  assert.equal(harness.socket.sent.includes('{"type":"end"}'), false);
  assert.equal(harness.pcmReader.cancelCalls, 1);
  assert.equal(harness.pcmReader.releaseCalls, 1);
  assert.deepEqual(harness.ui.lastStatus, ["중지됨", "idle"]);
});

test("midstream server failure aborts pacing with a phase-aware message", async () => {
  const harness = createHarness({ pcmBytes: 14400, blockWaitAt: 100 });
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();
  await flushUntil(() => harness.socket.sent.some(value => value instanceof ArrayBuffer));

  harness.socket.serverClose(1011);

  assert.equal(await started, false);
  assert.equal(harness.socket.sent.includes('{"type":"end"}'), false);
  assert.equal(harness.pcmReader.cancelCalls, 1);
  assert.equal(harness.pcmReader.releaseCalls, 1);
  assert.deepEqual(harness.ui.lastStatus, ["자막 전송이 중단되었습니다", "error"]);
});

async function createConnectedHarness() {
  const harness = createHarness();
  const started = harness.controller.start({ delay: "low" });
  await flushMicrotasks();
  harness.socket.open();
  await started;
  return harness;
}

function createHarness(options = {}) {
  const ui = new FakeUi();
  const scheduler = new FakeScheduler();
  const playbackStartSeconds = options.playbackStartSeconds ?? 565;
  const player = {
    seekCalls: [],
    playCalls: 0,
    pauseCalls: 0,
    seekTo(...args) { this.seekCalls.push(args); },
    playVideo() { this.playCalls += 1; },
    pauseVideo() { this.pauseCalls += 1; },
    seekAndPlay(...args) { this.seekCalls.push(args); this.playCalls += 1; },
    getState() { return options.playerStateAt?.(scheduler.now) ?? "playing"; },
    getCurrentTime() {
      return options.playerTimeAt?.(scheduler.now) ?? playbackStartSeconds + scheduler.now / 1000;
    },
    pause() { this.pauseCalls += 1; }
  };
  const socket = new FakeSocket(
    options.bufferedAmountAt ?? (() => options.bufferedAmount ?? 0),
    options.throwOnConnectingClose ?? false,
    () => scheduler.now
  );
  const pcmChunkBytes = options.pcmChunkBytes ?? [options.pcmBytes ?? 9600];
  const pcmReader = new FakePcmReader(
    pcmChunkBytes.map(byteLength => new Uint8Array(byteLength))
  );
  const actualPcmBytes = pcmChunkBytes.reduce((total, byteLength) => total + byteLength, 0);
  let fetchCalls = 0;
  let fetchStartByte = null;
  let waitCalls = 0;
  let primePlaybackCalls = 0;
  let socketFactoryCalls = 0;

  const fetchPcm = (startByte, signal) => {
    fetchCalls += 1;
    fetchStartByte = startByte;
    if (options.fetchPcm) {
      return options.fetchPcm(startByte, signal);
    }
    if (!options.fetchDeferred) {
      return Promise.resolve({
        reader: pcmReader,
        expectedBytes: options.reportedPcmBytes ?? actualPcmBytes
      });
    }
    return new Promise((resolve, reject) => {
      signal.addEventListener("abort", () => reject(signal.reason), { once: true });
    });
  };

  const harness = {
    ui,
    player,
    socket,
    pcmReader,
    scheduler,
    get fetchCalls() { return fetchCalls; },
    get fetchStartByte() { return fetchStartByte; },
    get waitCalls() { return waitCalls; },
    get primePlaybackCalls() { return primePlaybackCalls; },
    get socketFactoryCalls() { return socketFactoryCalls; }
  };
  harness.controller = new CaptionSessionController({
    ui,
    fetchPcm,
    getPlayer: async () => {
      if (options.playerPreparationError) {
        throw new Error("player preparation failed");
      }
      return player;
    },
    primePlayback: () => {
      primePlaybackCalls += 1;
      const startSeconds = player.getCurrentTime();
      player.seekTo(startSeconds, true);
      player.playVideo();
      return startSeconds;
    },
    pausePlayer: () => player.pauseVideo(),
    createSocket: () => {
      socketFactoryCalls += 1;
      return socket;
    },
    now: () => scheduler.now,
    waitUntil: async (deadline, signal) => {
      waitCalls += 1;
      if (options.blockWaitAt !== undefined && deadline >= options.blockWaitAt) {
        await new Promise((resolve, reject) => {
          signal.addEventListener("abort", () => reject(signal.reason), { once: true });
        });
        return;
      }
      scheduler.now = Math.max(scheduler.now, deadline);
    },
    setTimer: (callback, delay) => scheduler.set(callback, delay),
    clearTimer: id => scheduler.clear(id),
    bytesPerSecond: 48000,
    frameBytes: 4800,
    frameIntervalMs: 100,
    connectionTimeoutMs: 5000,
    backpressureTimeoutMs: options.backpressureTimeoutMs ?? 2500
  });
  return harness;
}

function update(state, lineId, revision, text, provider = "test") {
  return JSON.stringify({ state, lineId, revision, text, provider });
}

async function flushMicrotasks() {
  await Promise.resolve();
  await Promise.resolve();
  await Promise.resolve();
}

async function flushUntil(predicate) {
  for (let attempt = 0; attempt < 20 && !predicate(); attempt += 1) {
    await Promise.resolve();
  }
}

function createFetchResponse(options = {}) {
  const reader = new FakePcmReader(options.chunks ?? []);
  const inferredContentLength = (options.chunks ?? [])
    .reduce((total, chunk) => total + chunk.byteLength, 0);
  const contentLength = Object.hasOwn(options, "contentLength")
    ? options.contentLength
    : String(inferredContentLength);
  const body = Object.hasOwn(options, "body")
    ? options.body
    : {
        getReader: () => reader,
        cancel: async () => {
          options.onCancel?.();
          await reader.cancel();
        }
      };
  return {
    status: options.status ?? 206,
    headers: {
      get(name) {
        const normalizedName = name.toLowerCase();
        if (normalizedName === "content-range") {
          return options.contentRange ?? null;
        }
        if (normalizedName === "content-length") {
          return contentLength;
        }
        return null;
      }
    },
    body,
    arrayBuffer: async () => {
      throw new Error("whole-response buffering is forbidden");
    }
  };
}

class FakePcmReader {
  constructor(chunks) {
    this.chunks = [...chunks];
    this.cancelCalls = 0;
    this.releaseCalls = 0;
    this.cancelled = false;
  }

  async read() {
    if (this.cancelled || this.chunks.length === 0) {
      return { done: true, value: undefined };
    }
    return { done: false, value: this.chunks.shift() };
  }

  async cancel() {
    this.cancelCalls += 1;
    this.cancelled = true;
    this.chunks = [];
  }

  releaseLock() {
    this.releaseCalls += 1;
  }
}

class FakeUi {
  constructor() {
    this.running = false;
    this.playerAvailable = true;
    this.caption = "";
    this.statuses = [];
  }

  setControlsRunning(running) { this.running = running; }
  setPlayerAvailable(available) { this.playerAvailable = available; }
  setStatus(text, state) { this.statuses.push([text, state]); }
  resetCaptions() {
    this.caption = "";
  }
  renderCaption(text) {
    this.caption = text;
  }
  get lastStatus() { return this.statuses.at(-1); }
}

class CountingTextElement {
  constructor({ clientHeight = 0, scrollHeight = 0 } = {}) {
    this.value = "";
    this.setCalls = 0;
    this.clientHeight = clientHeight;
    this.scrollHeight = scrollHeight;
    this.scrollTop = 0;
  }

  get textContent() { return this.value; }
  set textContent(value) {
    this.value = value;
    this.setCalls += 1;
  }
}

class FakeSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;

  constructor(bufferedAmountAt, throwOnConnectingClose, now) {
    this.readyState = FakeSocket.CONNECTING;
    this.bufferedAmountAt = bufferedAmountAt;
    this.throwOnConnectingClose = throwOnConnectingClose;
    this.now = now;
    this.closeAttempts = 0;
    this.sent = [];
    this.sentAt = [];
    this.closeCalls = [];
    this.listeners = new Map();
  }

  get bufferedAmount() {
    return this.bufferedAmountAt(this.now());
  }

  addEventListener(type, handler, options = {}) {
    const listeners = this.listeners.get(type) ?? [];
    listeners.push({ handler, once: options.once === true });
    this.listeners.set(type, listeners);
  }

  removeEventListener(type, handler) {
    this.listeners.set(type, (this.listeners.get(type) ?? []).filter(item => item.handler !== handler));
  }

  send(value) {
    assert.equal(this.readyState, FakeSocket.OPEN);
    this.sent.push(value);
    this.sentAt.push(this.now());
  }

  close(code, reason) {
    this.closeAttempts += 1;
    if (this.readyState === FakeSocket.CONNECTING && this.throwOnConnectingClose) {
      this.throwOnConnectingClose = false;
      throw new Error("CONNECTING close is not supported");
    }
    this.closeCalls.push({ code, reason });
    this.readyState = FakeSocket.CLOSED;
    this.dispatch("close", { code });
  }

  open() {
    this.readyState = FakeSocket.OPEN;
    this.dispatch("open", {});
  }

  message(data) {
    this.dispatch("message", { data });
  }

  serverClose(code) {
    this.readyState = FakeSocket.CLOSED;
    this.dispatch("close", { code });
  }

  dispatch(type, event) {
    for (const item of [...(this.listeners.get(type) ?? [])]) {
      item.handler(event);
      if (item.once) {
        this.removeEventListener(type, item.handler);
      }
    }
  }
}

class FakeScheduler {
  constructor() {
    this.now = 0;
    this.nextId = 1;
    this.timers = new Map();
  }

  set(callback, delay) {
    const id = this.nextId++;
    this.timers.set(id, { callback, due: this.now + delay });
    return id;
  }

  clear(id) {
    this.timers.delete(id);
  }

  advance(milliseconds) {
    this.now += milliseconds;
    for (const [id, timer] of [...this.timers]) {
      if (timer.due <= this.now) {
        this.timers.delete(id);
        timer.callback();
      }
    }
  }
}
