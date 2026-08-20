import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const outputSource = await readFile(new URL("../../src/ChurchSubtitle.Web/wwwroot/output.js", import.meta.url), "utf8");
const outputHtml = await readFile(new URL("../../src/ChurchSubtitle.Web/wwwroot/output.html", import.meta.url), "utf8");
const outputCss = await readFile(new URL("../../src/ChurchSubtitle.Web/wwwroot/output.css", import.meta.url), "utf8");
const outputModuleUrl = `data:text/javascript;base64,${Buffer.from(outputSource).toString("base64")}`;
const { createCaptionBoard } = await import(outputModuleUrl);

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

test("caption messages replace the board text", () => {
  const boardText = new CountingTextElement({ clientHeight: 100, scrollHeight: 100 });
  const board = createCaptionBoard(boardText);

  board.handleMessage({ type: "caption", text: "축복합니다. 사랑하는 성도 여러분" });

  assert.equal(boardText.textContent, "축복합니다. 사랑하는 성도 여러분");
});

test("an empty caption clears the board for a new session", () => {
  const boardText = new CountingTextElement({ clientHeight: 100, scrollHeight: 100 });
  const board = createCaptionBoard(boardText);

  board.handleMessage({ type: "caption", text: "이전 자막" });
  board.handleMessage({ type: "caption", text: "" });

  assert.equal(boardText.textContent, "");
});

test("invalid payloads leave the board unchanged", () => {
  const boardText = new CountingTextElement({ clientHeight: 100, scrollHeight: 100 });
  const board = createCaptionBoard(boardText);
  board.handleMessage({ type: "caption", text: "유지되는 자막" });

  for (const payload of [
    null,
    undefined,
    "caption",
    { type: "hello" },
    { type: "caption" },
    { type: "caption", text: 42 },
    { type: "status", text: "무시되는 상태" },
    { type: "caption", text: "긴".repeat(10001) }
  ]) {
    board.handleMessage(payload);
  }

  assert.equal(boardText.textContent, "유지되는 자막");
  assert.equal(boardText.setCalls, 1);
});

test("board rendering scrolls only the overflowing top visual line", () => {
  const boardText = new CountingTextElement({ clientHeight: 100, scrollHeight: 100 });
  const board = createCaptionBoard(boardText);

  board.handleMessage({ type: "caption", text: "축복합니다. 사랑하는 성도 여러분" });
  assert.equal(boardText.scrollTop, 0);

  boardText.scrollHeight = 150;
  board.handleMessage({ type: "caption", text: "축복합니다. 사랑하는 성도 여러분 다음 문장" });
  board.handleMessage({ type: "caption", text: "축복합니다. 사랑하는 성도 여러분 다음 문장" });

  assert.equal(boardText.textContent, "축복합니다. 사랑하는 성도 여러분 다음 문장");
  assert.equal(boardText.scrollTop, 50);
  assert.equal(boardText.setCalls, 2);
});

test("board markup and script mutate one continuous text node only", () => {
  assert.match(outputHtml, /id="board-text"[^>]*aria-live="polite"[^>]*aria-atomic="false"/);
  assert.equal((outputHtml.match(/id="board-text"/g) ?? []).length, 1);
  assert.equal(outputSource.includes("innerHTML"), false);
  assert.equal(outputSource.includes("replaceChildren"), false);
  assert.match(outputSource, /boardText\.textContent = data\.text/);
});

test("board styles reproduce the legacy 2021-07-01 output spec", () => {
  assert.match(outputCss, /html,\s*body\s*\{[^}]*background:\s*#000;/s);
  assert.match(outputCss, /\.caption-band\s*\{[^}]*height:\s*19\.537vh;[^}]*background:\s*rgb\(71 71 71\);/s);
  assert.match(outputCss, /\.board-text\s*\{[^}]*color:\s*#fff;[^}]*font-size:\s*6\.42vh;[^}]*font-weight:\s*800;[^}]*line-height:\s*1\.2;/s);
  assert.match(outputCss, /font-family:\s*"나눔고딕 ExtraBold"/);
  assert.match(outputCss, /\.board-text\s*\{[^}]*overflow:\s*hidden;[^}]*word-break:\s*keep-all;[^}]*overflow-wrap:\s*anywhere;/s);
});
