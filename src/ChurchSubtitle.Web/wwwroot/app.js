"use strict";

const VIDEO_ID = "YLsWQq04R10";
export const TOTAL_PCM_BYTES = 70_368_000;
const PCM_BYTES_PER_SECOND = 48000;
const AUDIO_FRAME_BYTES = 4800;
const FRAME_INTERVAL_MS = 100;
const MAX_BUFFERED_BYTES = 256 * 1024;
const BACKPRESSURE_TIMEOUT_MS = 2500;
const CONNECTION_TIMEOUT_MS = 5000;
const PLAYER_TIMEOUT_MS = 10000;
const PLAYBACK_WAIT_TIMEOUT_MS = 8000;
const PLAYBACK_POLL_MS = 100;
const MAX_PLAYBACK_DRIFT_SECONDS = 0.75;
const MAX_UI_FINAL_FRAGMENTS = 64;

function normalizeCaptionFragment(text) {
  return text.trim().replace(/\s+/g, " ");
}

export function pcmStartByteForVideoTime(
  videoSeconds,
  bytesPerSecond = PCM_BYTES_PER_SECOND,
  frameBytes = AUDIO_FRAME_BYTES
) {
  if (!Number.isFinite(videoSeconds) || videoSeconds < 0) {
    throw new RangeError("invalid video time");
  }
  const framePosition = videoSeconds * bytesPerSecond / frameBytes;
  const boundaryEpsilon = Number.EPSILON * Math.max(1, Math.abs(framePosition)) * 4;
  return Math.floor(framePosition + boundaryEpsilon) * frameBytes;
}

export class CaptionSessionController {
  constructor({
    ui,
    fetchPcm,
    getPlayer,
    primePlayback,
    pausePlayer,
    createSocket,
    now,
    waitUntil,
    setTimer,
    clearTimer,
    bytesPerSecond = PCM_BYTES_PER_SECOND,
    frameBytes = AUDIO_FRAME_BYTES,
    frameIntervalMs = FRAME_INTERVAL_MS,
    maxBufferedBytes = MAX_BUFFERED_BYTES,
    backpressureTimeoutMs = BACKPRESSURE_TIMEOUT_MS,
    connectionTimeoutMs = CONNECTION_TIMEOUT_MS,
    playbackWaitTimeoutMs = PLAYBACK_WAIT_TIMEOUT_MS,
    playbackPollMs = PLAYBACK_POLL_MS,
    maxPlaybackDriftSeconds = MAX_PLAYBACK_DRIFT_SECONDS
  }) {
    this._ui = ui;
    this._fetchPcm = fetchPcm;
    this._getPlayer = getPlayer;
    this._primePlayback = primePlayback ?? (() => {});
    this._pausePlayer = pausePlayer ?? (() => {});
    this._createSocket = createSocket;
    this._now = now;
    this._waitUntil = waitUntil;
    this._setTimer = setTimer;
    this._clearTimer = clearTimer;
    this._bytesPerSecond = bytesPerSecond;
    this._frameBytes = frameBytes;
    this._frameIntervalMs = frameIntervalMs;
    this._maxBufferedBytes = maxBufferedBytes;
    this._backpressureTimeoutMs = backpressureTimeoutMs;
    this._connectionTimeoutMs = connectionTimeoutMs;
    this._playbackWaitTimeoutMs = playbackWaitTimeoutMs;
    this._playbackPollMs = playbackPollMs;
    this._maxPlaybackDriftSeconds = maxPlaybackDriftSeconds;
    this._activeRun = null;
    this._generation = 0;
    this._activePartialLineId = null;
    this._partialCaptions = new Map();
    this._recentFinals = [];
    this._lineWatermarks = new Map();
  }

  async start({ delay }) {
    if (this._activeRun) {
      return false;
    }

    const run = {
      generation: ++this._generation,
      abortController: new AbortController(),
      socket: null,
      player: null,
      endSent: false,
      serverError: false,
      phase: "preparing",
      videoStartSeconds: null,
      pcmStartByte: null,
      pcmReaderHandle: null,
      detachSocketHandlers: null
    };
    this._activeRun = run;
    this._ui.setControlsRunning(true);
    this._resetCaptions();
    this._ui.setStatus("오디오 준비 중", "connecting");

    try {
      try {
        const capturedVideoSeconds = this._primePlayback();
        run.pcmStartByte = pcmStartByteForVideoTime(
          capturedVideoSeconds,
          this._bytesPerSecond,
          this._frameBytes
        );
        run.videoStartSeconds = run.pcmStartByte / this._bytesPerSecond;
      } catch (error) {
        if (error instanceof RangeError) {
          throw new VideoPositionError();
        }
        throw error;
      }
      const pcmSource = await this._fetchPcm(
        run.pcmStartByte,
        run.abortController.signal
      );
      if (!pcmSource
        || typeof pcmSource.reader?.read !== "function"
        || !Number.isSafeInteger(pcmSource.expectedBytes)
        || pcmSource.expectedBytes <= 0
        || pcmSource.expectedBytes % 2 !== 0) {
        throw new PcmFetchError();
      }
      run.pcmReaderHandle = {
        reader: pcmSource.reader,
        expectedBytes: pcmSource.expectedBytes,
        cancelled: false,
        released: false
      };
      this._ensureCurrent(run);
      const player = await this._getPlayer(run.abortController.signal);
      this._ensureCurrent(run);
      run.player = player;

      const socket = this._createSocket();
      run.socket = socket;
      run.phase = "connecting";
      run.detachSocketHandlers = this._attachSocketHandlers(run);
      await this._waitForSocketOpen(run);
      this._ensureCurrent(run);

      socket.send(JSON.stringify({ type: "start", delay: delay }));
      run.phase = "playback";
      this._ui.setStatus("영상 동기화 중", "connecting");
      try {
        player.seekAndPlay(run.videoStartSeconds);
      } catch {
        throw new PlayerError();
      }
      await this._waitForPlayback(run, run.videoStartSeconds, true);
      this._ensureCurrent(run);
      run.phase = "streaming";
      this._ui.setStatus("자막 송출 중", "live");

      await this._streamPcm(run);
      this._ensureCurrent(run);
      await this._waitForBackpressure(run);
      socket.send(JSON.stringify({ type: "end" }));
      run.endSent = true;
      run.phase = "finishing";
      this._ui.setStatus("자막 마무리 중", "connecting");
      return true;
    } catch (error) {
      if (!this._isCurrent(run)) {
        this._cancelPcmReader(run);
        this._releasePcmReader(run);
        return false;
      }

      const message = error instanceof VideoPositionError
        ? "영상 위치를 확인하지 못했습니다"
        : error instanceof PcmFetchError
          ? "테스트 오디오를 불러오지 못했습니다"
          : error instanceof PlayerError || error instanceof PlaybackSyncError
            ? "영상 재생을 동기화하지 못했습니다"
            : run.phase === "streaming"
              ? "자막 전송이 중단되었습니다"
              : "자막 연결을 시작하지 못했습니다";
      this._finishRun(run, message, "error", true);
      return false;
    }
  }

  stop() {
    const run = this._activeRun;
    if (!run) {
      return false;
    }

    this._finishRun(run, "중지됨", "idle", true);
    return true;
  }

  handleExternalPlayerError() {
    const run = this._activeRun;
    if (run) {
      this._finishRun(run, "영상을 재생하지 못했습니다", "error", true);
      return;
    }
    this._ui.setPlayerAvailable(false);
    this._ui.setControlsRunning(false);
    this._ui.setStatus("영상 플레이어를 불러오지 못했습니다", "error");
  }

  _attachSocketHandlers(run) {
    const socket = run.socket;
    const onMessage = event => this._receiveCaptionUpdate(run, event.data);
    const onError = () => {
      if (this._isCurrent(run)) {
        run.serverError = true;
        this._ui.setStatus(this._phaseFailureMessage(run), "error");
      }
    };
    const onClose = event => {
      if (!this._isCurrent(run)) {
        return;
      }

      if (run.serverError || event.code !== 1000) {
        this._finishRun(run, this._phaseFailureMessage(run), "error", false);
      } else if (run.endSent) {
        this._finishRun(run, "처리 완료", "complete", false);
      } else {
        this._finishRun(run, "연결이 종료되었습니다", "error", false);
      }
    };

    socket.addEventListener("message", onMessage);
    socket.addEventListener("error", onError);
    socket.addEventListener("close", onClose);
    return () => {
      socket.removeEventListener("message", onMessage);
      socket.removeEventListener("error", onError);
      socket.removeEventListener("close", onClose);
    };
  }

  _waitForSocketOpen(run) {
    const socket = run.socket;
    const signal = run.abortController.signal;
    return new Promise((resolve, reject) => {
      let settled = false;
      const timeoutId = this._setTimer(
        () => complete(reject, new ConnectionTimeoutError()),
        this._connectionTimeoutMs
      );
      const onOpen = () => complete(resolve);
      const onError = () => complete(reject, new Error("socket"));
      const onAbort = () => complete(reject, abortReason(signal));
      const complete = (callback, value) => {
        if (settled) {
          return;
        }
        settled = true;
        this._clearTimer(timeoutId);
        socket.removeEventListener("open", onOpen);
        socket.removeEventListener("error", onError);
        signal.removeEventListener("abort", onAbort);
        callback(value);
      };

      socket.addEventListener("open", onOpen);
      socket.addEventListener("error", onError);
      signal.addEventListener("abort", onAbort, { once: true });
    });
  }

  _phaseFailureMessage(run) {
    return run.phase === "finishing"
      ? "자막 처리를 완료하지 못했습니다"
      : run.phase === "streaming"
        ? "자막 전송이 중단되었습니다"
        : "자막 연결 오류";
  }

  async _streamPcm(run) {
    const handle = run.pcmReaderHandle;
    let pacingOrigin = this._now();
    let frameNumber = 0;
    let totalBytes = 0;
    let carry = new Uint8Array(0);

    try {
      while (true) {
        const { done, value } = await handle.reader.read();
        this._ensureCurrent(run);
        if (done) {
          break;
        }
        if (!(value instanceof Uint8Array)) {
          throw new PcmFetchError();
        }

        totalBytes += value.byteLength;
        if (totalBytes > handle.expectedBytes) {
          throw new PcmFetchError();
        }

        let offset = 0;
        if (carry.byteLength > 0) {
          const needed = this._frameBytes - carry.byteLength;
          if (value.byteLength < needed) {
            const nextCarry = new Uint8Array(carry.byteLength + value.byteLength);
            nextCarry.set(carry);
            nextCarry.set(value, carry.byteLength);
            carry = nextCarry;
            continue;
          }

          const frame = new Uint8Array(this._frameBytes);
          frame.set(carry);
          frame.set(value.subarray(0, needed), carry.byteLength);
          pacingOrigin = await this._sendPcmFrame(
            run,
            frame.buffer,
            frameNumber,
            pacingOrigin
          );
          frameNumber += 1;
          offset = needed;
          carry = new Uint8Array(0);
        }

        while (value.byteLength - offset >= this._frameBytes) {
          const frame = value.slice(offset, offset + this._frameBytes);
          pacingOrigin = await this._sendPcmFrame(
            run,
            frame.buffer,
            frameNumber,
            pacingOrigin
          );
          frameNumber += 1;
          offset += this._frameBytes;
        }

        if (offset < value.byteLength) {
          carry = value.slice(offset);
        }
      }

      if (totalBytes !== handle.expectedBytes) {
        throw new PcmFetchError();
      }
      if (carry.byteLength > 0) {
        if (carry.byteLength % 2 !== 0) {
          throw new PcmFetchError();
        }
        await this._sendPcmFrame(run, carry.buffer, frameNumber, pacingOrigin);
      }
    } catch (error) {
      this._cancelPcmReader(run, handle);
      throw error;
    } finally {
      this._releasePcmReader(run, handle);
    }
  }

  async _sendPcmFrame(run, frame, frameNumber, pacingOrigin) {
    await this._waitUntil(
      pacingOrigin + frameNumber * this._frameIntervalMs,
      run.abortController.signal
    );
    this._ensureCurrent(run);
    const expectedVideoTime = run.videoStartSeconds
      + frameNumber * this._frameIntervalMs / 1000;
    const playbackWait = await this._waitForPlayback(
      run,
      expectedVideoTime,
      false
    );
    const playbackAdjustedOrigin = pacingOrigin + playbackWait;
    const backpressureWait = await this._waitForBackpressure(run);
    this._ensureSocketOpen(run.socket);
    run.socket.send(frame);
    return playbackAdjustedOrigin + backpressureWait;
  }

  async _waitForPlayback(run, expectedVideoTime, initial) {
    const waitStartedAt = this._now();
    const deadline = waitStartedAt + this._playbackWaitTimeoutMs;

    while (true) {
      this._ensureCurrent(run);
      this._ensureSocketOpen(run.socket);
      const playerState = run.player.getState();
      if (playerState === "playing") {
        const currentTime = run.player.getCurrentTime();
        if (!Number.isFinite(currentTime)
          || Math.abs(currentTime - expectedVideoTime) > this._maxPlaybackDriftSeconds) {
          throw new PlaybackSyncError();
        }
        return this._now() - waitStartedAt;
      }

      const mayStillStart = initial && (playerState === "unstarted" || playerState === "cued");
      const mayResume = playerState === "buffering" || playerState === "paused";
      if (!mayStillStart && !mayResume) {
        throw new PlaybackSyncError();
      }
      if (this._now() >= deadline) {
        throw new PlaybackSyncError();
      }

      await this._waitUntil(
        Math.min(this._now() + this._playbackPollMs, deadline),
        run.abortController.signal
      );
    }
  }

  async _waitForBackpressure(run) {
    if (run.socket.bufferedAmount <= this._maxBufferedBytes) {
      return 0;
    }

    const pauseStartedAt = this._now();
    const resumeSeconds = run.player.getCurrentTime();
    if (!Number.isFinite(resumeSeconds)) {
      throw new PlaybackSyncError();
    }
    try {
      this._pausePlayer();
    } catch {
      throw new PlayerError();
    }

    const deadline = this._now() + this._backpressureTimeoutMs;
    while (run.socket.bufferedAmount > this._maxBufferedBytes) {
      if (this._now() >= deadline) {
        throw new Error("backpressure");
      }
      await this._waitUntil(this._now() + 25, run.abortController.signal);
      this._ensureCurrent(run);
      this._ensureSocketOpen(run.socket);
    }

    try {
      run.player.seekAndPlay(resumeSeconds);
    } catch {
      throw new PlayerError();
    }
    await this._waitForPlayback(run, resumeSeconds, true);
    return this._now() - pauseStartedAt;
  }

  _receiveCaptionUpdate(run, rawData) {
    if (!this._isCurrent(run) || typeof rawData !== "string") {
      return;
    }

    let update;
    try {
      update = JSON.parse(rawData);
    } catch {
      return;
    }

    if (!isCaptionUpdate(update)) {
      return;
    }

    if (update.state === "status") {
      if (update.provider === "server:error") {
        run.serverError = true;
        this._finishRun(run, "서버 처리 오류", "error", true);
      } else {
        this._ui.setStatus(
          run.endSent ? "처리 완료" : "서버 연결됨",
          run.endSent ? "complete" : "live"
        );
      }
      return;
    }

    if (update.state === "partial") {
      this._upsertPartial(update);
    } else if (update.state === "final") {
      this._upsertFinal(update);
    }
  }

  _upsertPartial(update) {
    if (!this._canApplyLineUpdate(update)) {
      return;
    }

    this._lineWatermarks.set(update.lineId, {
      revision: update.revision,
      state: "partial"
    });
    this._recentFinals = this._recentFinals.filter(item => item.lineId !== update.lineId);
    this._partialCaptions.set(update.lineId, {
      revision: update.revision,
      text: update.text
    });
    this._activePartialLineId = update.lineId;
    this._renderCaptionText();
  }

  _upsertFinal(update) {
    if (!this._canApplyLineUpdate(update)) {
      return;
    }

    this._lineWatermarks.set(update.lineId, {
      revision: update.revision,
      state: "final"
    });
    this._recentFinals = this._recentFinals.filter(item => item.lineId !== update.lineId);
    if (update.text.trim()) {
      this._recentFinals.push({
        lineId: update.lineId,
        revision: update.revision,
        text: update.text
      });
    }
    this._recentFinals = this._recentFinals.slice(-MAX_UI_FINAL_FRAGMENTS);
    this._partialCaptions.delete(update.lineId);
    if (this._activePartialLineId === update.lineId) {
      this._activePartialLineId = Array.from(this._partialCaptions.keys()).at(-1) ?? null;
    }
    this._renderCaptionText();
  }

  _canApplyLineUpdate(update) {
    const watermark = this._lineWatermarks.get(update.lineId);
    if (!watermark) {
      return true;
    }
    if (watermark.state === "final" && update.state === "partial") {
      return false;
    }
    if (update.revision !== watermark.revision) {
      return update.revision > watermark.revision;
    }
    return watermark.state === "partial" && update.state === "final";
  }

  _renderCaptionText() {
    const partial = this._activePartialLineId
      ? normalizeCaptionFragment(
        this._partialCaptions.get(this._activePartialLineId)?.text ?? ""
      )
      : "";
    const fragments = this._recentFinals
      .map(item => normalizeCaptionFragment(item.text))
      .filter(Boolean);
    if (partial) {
      fragments.push(partial);
    }
    this._ui.renderCaption(fragments.join(" "));
  }

  _resetCaptions() {
    this._partialCaptions.clear();
    this._recentFinals = [];
    this._activePartialLineId = null;
    this._lineWatermarks.clear();
    this._ui.resetCaptions();
  }

  _cancelPcmReader(run, handle = run.pcmReaderHandle) {
    if (!handle || handle.cancelled) {
      return;
    }
    handle.cancelled = true;
    try {
      const cancellation = handle.reader.cancel();
      cancellation?.catch?.(() => {});
    } catch {
      // Reader cancellation is best effort during session cleanup.
    }
  }

  _releasePcmReader(run, handle = run.pcmReaderHandle) {
    if (!handle || handle.released) {
      return;
    }
    try {
      handle.reader.releaseLock();
      handle.released = true;
      if (run.pcmReaderHandle === handle) {
        run.pcmReaderHandle = null;
      }
    } catch {
      // A pending read releases its lock from the streaming finally block.
    }
  }

  _finishRun(run, statusText, statusState, closeSocket) {
    if (!this._isCurrent(run)) {
      return;
    }

    this._activeRun = null;
    run.abortController.abort();
    this._cancelPcmReader(run);
    this._releasePcmReader(run);
    run.detachSocketHandlers?.();
    if (closeSocket) {
      closeSocketNormally(run.socket);
    }
    this._pausePlayer();
    this._ui.setControlsRunning(false);
    this._ui.setStatus(statusText, statusState);
  }

  _ensureCurrent(run) {
    if (!this._isCurrent(run)) {
      throw abortReason(run.abortController.signal);
    }
  }

  _isCurrent(run) {
    return this._activeRun === run
      && run.generation === this._generation
      && !run.abortController.signal.aborted;
  }

  _ensureSocketOpen(socket) {
    if (!socket || socket.readyState !== 1) {
      throw new Error("socket-closed");
    }
  }
}

function isCaptionUpdate(value) {
  return value !== null
    && typeof value === "object"
    && ["partial", "final", "status"].includes(value.state)
    && typeof value.lineId === "string"
    && value.lineId.length > 0
    && value.lineId.length <= 256
    && Number.isSafeInteger(value.revision)
    && value.revision >= 0
    && typeof value.text === "string"
    && value.text.length <= 10000
    && typeof value.provider === "string";
}

function closeSocketNormally(socket) {
  if (!socket) {
    return;
  }

  if (socket.readyState === 1) {
    try {
      socket.close(1000, "client stopped");
    } catch {
      // Cleanup is best effort; transport details are never displayed.
    }
    return;
  }
  if (socket.readyState !== 0) {
    return;
  }

  let needsLateOpenFallback = false;
  try {
    socket.close(1000, "client stopped");
    needsLateOpenFallback = socket.readyState === 0;
  } catch {
    needsLateOpenFallback = true;
  }
  if (!needsLateOpenFallback) {
    return;
  }

  try {
    const cleanup = () => {
      socket.removeEventListener("open", closeWhenOpen);
      socket.removeEventListener("error", cleanup);
      socket.removeEventListener("close", cleanup);
    };
    const closeWhenOpen = () => {
      cleanup();
      if (socket.readyState === 1) {
        try {
          socket.close(1000, "client stopped");
        } catch {
          // Cleanup is best effort; transport details are never displayed.
        }
      }
    };
    socket.addEventListener("open", closeWhenOpen, { once: true });
    socket.addEventListener("error", cleanup, { once: true });
    socket.addEventListener("close", cleanup, { once: true });
  } catch {
    // Cleanup is best effort; transport details are never displayed.
  }
}

function abortReason(signal) {
  return signal.reason ?? new DOMException("Aborted", "AbortError");
}

class PcmFetchError extends Error {}
class PlayerError extends Error {}
class PlaybackSyncError extends Error {}
class ConnectionTimeoutError extends Error {}
class VideoPositionError extends Error {}

if (typeof window !== "undefined" && typeof document !== "undefined") {
  bootstrapBrowser();
}

function bootstrapBrowser() {
  const elements = {
    delay: document.getElementById("delay"),
    startButton: document.getElementById("start-button"),
    stopButton: document.getElementById("stop-button"),
    statusBadge: document.getElementById("status-badge"),
    statusText: document.getElementById("status-text"),
    liveIndicator: document.getElementById("live-indicator"),
    captionText: document.getElementById("caption-text")
  };
  const ui = createBrowserUi(elements);
  ui.setPlayerAvailable(false);
  let player = null;
  let controller = null;
  let resolvePlayer;
  let rejectPlayer;
  const playerReady = new Promise((resolve, reject) => {
    resolvePlayer = resolve;
    rejectPlayer = reject;
  });
  playerReady.catch(() => {});

  controller = new CaptionSessionController({
    ui,
    fetchPcm: fetchSelectedPcm,
    getPlayer: signal => waitWithTimeout(playerReady, PLAYER_TIMEOUT_MS, signal),
    primePlayback: () => {
      if (!player) {
        throw new PlayerError();
      }
      const startSeconds = player.getCurrentTime();
      player.seekTo(startSeconds, true);
      player.playVideo();
      return startSeconds;
    },
    pausePlayer: () => {
      try {
        player?.pauseVideo();
      } catch {
        // The player may still be initializing.
      }
    },
    createSocket: () => {
      const protocol = window.location.protocol === "https:" ? "wss:" : "ws:";
      const socket = new WebSocket(`${protocol}//${window.location.host}/ws/captions`);
      socket.binaryType = "arraybuffer";
      return socket;
    },
    now: () => performance.now(),
    waitUntil: browserWaitUntil,
    setTimer: (callback, delay) => window.setTimeout(callback, delay),
    clearTimer: timerId => window.clearTimeout(timerId)
  });

  loadYouTubeApi(
    readyPlayer => {
      player = readyPlayer;
      resolvePlayer(createYouTubePlayerAdapter(readyPlayer));
      ui.setPlayerAvailable(true);
    },
    () => {
      rejectPlayer(new PlayerError());
      controller.handleExternalPlayerError();
    }
  );

  elements.startButton.addEventListener("click", () => {
    void controller.start({ delay: elements.delay.value });
  });
  elements.stopButton.addEventListener("click", () => controller.stop());
}

export function createBrowserUi(elements) {
  let controlsRunning = false;
  let playerAvailable = false;
  return {
    setControlsRunning(running) {
      controlsRunning = running;
      elements.delay.disabled = running;
      elements.startButton.disabled = running || !playerAvailable;
      elements.stopButton.disabled = !running;
    },
    setPlayerAvailable(available) {
      playerAvailable = available;
      elements.startButton.disabled = controlsRunning || !available;
    },
    setStatus(text, state) {
      if (elements.statusText.textContent !== text) {
        elements.statusText.textContent = text;
      }
      elements.statusBadge.dataset.state = state;
      elements.liveIndicator.dataset.state = state;
      const indicatorText = state === "live"
        ? "● LIVE"
        : state === "complete"
          ? "완료"
          : state === "error"
            ? "오류"
            : state === "connecting"
              ? "준비"
              : "대기";
      if (elements.liveIndicator.textContent !== indicatorText) {
        elements.liveIndicator.textContent = indicatorText;
      }
    },
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
  };
}

function createYouTubePlayerAdapter(player) {
  return {
    seekAndPlay(seconds) {
      player.seekTo(seconds, true);
      player.playVideo();
    },
    getState() {
      return new Map([
        [-1, "unstarted"],
        [0, "ended"],
        [1, "playing"],
        [2, "paused"],
        [3, "buffering"],
        [5, "cued"]
      ]).get(player.getPlayerState()) ?? "unknown";
    },
    getCurrentTime() {
      return player.getCurrentTime();
    }
  };
}

function loadYouTubeApi(onReady, onError) {
  window.onYouTubeIframeAPIReady = () => {
    try {
      const player = new window.YT.Player("player", {
        videoId: VIDEO_ID,
        playerVars: {
          playsinline: 1,
          rel: 0
        },
        events: {
          onReady: () => onReady(player),
          onError
        }
      });
    } catch {
      onError();
    }
  };

  const script = document.createElement("script");
  script.src = "https://www.youtube.com/iframe_api";
  script.async = true;
  script.addEventListener("error", onError, { once: true });
  document.head.appendChild(script);
}

export async function fetchSelectedPcm(startByte, signal) {
  if (!Number.isSafeInteger(startByte)
    || startByte < 0
    || startByte >= TOTAL_PCM_BYTES) {
    throw new PcmFetchError();
  }

  let response;
  try {
    response = await fetch("/test-assets/service.pcm", {
      headers: { Range: `bytes=${startByte}-` },
      signal,
      cache: "no-store"
    });
  } catch (error) {
    if (signal.aborted) {
      throw error;
    }
    throw new PcmFetchError();
  }

  if (response.status !== 206) {
    await cancelResponseBody(response);
    throw new PcmFetchError();
  }

  const contentRange = response.headers?.get?.("content-range");
  const range = parsePcmContentRange(contentRange, startByte);
  const contentLength = parseContentLength(response.headers?.get?.("content-length"));
  if (!range
    || contentLength !== range.expectedBytes
    || !response.body
    || typeof response.body.getReader !== "function") {
    await cancelResponseBody(response);
    throw new PcmFetchError();
  }

  try {
    return {
      reader: response.body.getReader(),
      expectedBytes: range.expectedBytes
    };
  } catch {
    await cancelResponseBody(response);
    throw new PcmFetchError();
  }
}

function parsePcmContentRange(value, requestedStartByte) {
  const match = /^bytes ([0-9]+)-([0-9]+)\/([0-9]+)$/.exec(value ?? "");
  if (!match) {
    return null;
  }

  const start = Number(match[1]);
  const end = Number(match[2]);
  const total = Number(match[3]);
  if (!Number.isSafeInteger(start)
    || !Number.isSafeInteger(end)
    || !Number.isSafeInteger(total)
    || start !== requestedStartByte
    || end !== TOTAL_PCM_BYTES - 1
    || total !== TOTAL_PCM_BYTES) {
    return null;
  }

  const expectedBytes = TOTAL_PCM_BYTES - requestedStartByte;
  if (!Number.isSafeInteger(expectedBytes)
    || expectedBytes <= 0
    || expectedBytes % 2 !== 0) {
    return null;
  }
  return { expectedBytes };
}

function parseContentLength(value) {
  if (!/^[0-9]+$/.test(value ?? "")) {
    return null;
  }
  const contentLength = Number(value);
  return Number.isSafeInteger(contentLength) ? contentLength : null;
}

async function cancelResponseBody(response) {
  try {
    await response.body?.cancel?.();
  } catch {
    // Invalid response cleanup is best effort.
  }
}

function browserWaitUntil(deadline, signal) {
  const remaining = Math.max(0, deadline - performance.now());
  if (remaining === 0) {
    return Promise.resolve();
  }

  return new Promise((resolve, reject) => {
    let settled = false;
    const timeoutId = window.setTimeout(() => complete(resolve), remaining);
    const onAbort = () => complete(reject, abortReason(signal));
    const complete = (callback, value) => {
      if (settled) {
        return;
      }
      settled = true;
      window.clearTimeout(timeoutId);
      signal.removeEventListener("abort", onAbort);
      callback(value);
    };
    signal.addEventListener("abort", onAbort, { once: true });
  });
}

function waitWithTimeout(promise, timeoutMs, signal) {
  return new Promise((resolve, reject) => {
    let settled = false;
    const timeoutId = window.setTimeout(() => complete(reject, new PlayerError()), timeoutMs);
    const onAbort = () => complete(reject, abortReason(signal));
    const complete = (callback, value) => {
      if (settled) {
        return;
      }
      settled = true;
      window.clearTimeout(timeoutId);
      signal.removeEventListener("abort", onAbort);
      callback(value);
    };

    signal.addEventListener("abort", onAbort, { once: true });
    promise.then(
      value => complete(resolve, value),
      () => complete(reject, new PlayerError())
    );
  });
}
