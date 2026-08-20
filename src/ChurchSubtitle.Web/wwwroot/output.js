"use strict";

const OUTPUT_CHANNEL_NAME = "caption-output";
const MAX_CAPTION_TEXT_LENGTH = 10000;

export function createCaptionBoard(boardText) {
  return {
    handleMessage(data) {
      if (data === null
        || typeof data !== "object"
        || data.type !== "caption"
        || typeof data.text !== "string"
        || data.text.length > MAX_CAPTION_TEXT_LENGTH) {
        return;
      }

      if (boardText.textContent !== data.text) {
        boardText.textContent = data.text;
      }
      boardText.scrollTop = Math.max(
        0,
        boardText.scrollHeight - boardText.clientHeight
      );
    }
  };
}

if (typeof window !== "undefined" && typeof document !== "undefined") {
  bootstrapBoard();
}

function bootstrapBoard() {
  const board = createCaptionBoard(document.getElementById("board-text"));

  if (typeof BroadcastChannel !== "undefined") {
    const channel = new BroadcastChannel(OUTPUT_CHANNEL_NAME);
    channel.addEventListener("message", event => board.handleMessage(event.data));
    channel.postMessage({ type: "hello" });
  }

  document.addEventListener("click", () => {
    try {
      if (document.fullscreenElement) {
        void document.exitFullscreen?.()?.catch?.(() => {});
      } else {
        void document.documentElement.requestFullscreen?.()?.catch?.(() => {});
      }
    } catch {
      // Fullscreen is a convenience; the window still works as a plain page.
    }
  });
}
