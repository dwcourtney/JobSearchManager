"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { copyText } = require("../wwwroot/clipboard-text.js");

const exactPosting = "Heading\n\n• First responsibility\n• Second responsibility";

function createFallbackEnvironment({ clipboard, execResult = true } = {}) {
  const state = {
    appended: [],
    removed: [],
    execCalls: 0,
    copiedText: null,
    focusRestored: 0,
    selectionCleared: 0,
    restoredRanges: []
  };
  const parent = {
    appendChild(node) {
      node.parentNode = this;
      state.appended.push(node);
      state.current = node;
    },
    removeChild(node) {
      state.removed.push(node);
      node.parentNode = null;
      state.current = null;
    }
  };
  const activeElement = { focus() { state.focusRestored += 1; } };
  const originalRange = { cloneRange() { return { id: "saved-range" }; } };
  const selection = {
    rangeCount: 1,
    getRangeAt() { return originalRange; },
    removeAllRanges() { state.selectionCleared += 1; },
    addRange(range) { state.restoredRanges.push(range); }
  };
  const document = {
    body: parent,
    activeElement,
    createElement(tagName) {
      assert.equal(tagName, "textarea");
      const node = {
        style: {},
        setAttribute() {},
        focus() {},
        select() {},
        setSelectionRange() {},
        remove() { parent.removeChild(node); }
      };
      return node;
    },
    execCommand(command) {
      state.execCalls += 1;
      assert.equal(command, "copy");
      state.copiedText = state.current?.value;
      return execResult;
    }
  };
  return {
    environment: {
      navigator: clipboard === undefined ? {} : { clipboard },
      document,
      getSelection() { return selection; }
    },
    state
  };
}

async function runTransportTests() {
  {
    const writes = [];
    const { environment, state } = createFallbackEnvironment({
      clipboard: { async writeText(text) { writes.push(text); } }
    });
    assert.equal(await copyText(exactPosting, environment), true);
    assert.deepEqual(writes, [exactPosting]);
    assert.equal(state.execCalls, 0, "Successful modern copy must not use the fallback.");
    assert.equal(state.appended.length, 0);
  }

  {
    const { environment, state } = createFallbackEnvironment();
    assert.equal(await copyText(exactPosting, environment), true);
    assert.equal(state.copiedText, exactPosting, "Fallback must copy the exact posting text.");
    assert.equal(state.appended.length, 1);
    assert.equal(state.removed.length, 1, "Fallback textarea must be removed.");
    assert.equal(state.focusRestored, 1);
    assert.equal(state.selectionCleared, 1);
    assert.deepEqual(state.restoredRanges, [{ id: "saved-range" }]);
  }

  {
    const { environment, state } = createFallbackEnvironment({
      clipboard: { async writeText() { throw new Error("NotAllowedError"); } }
    });
    assert.equal(await copyText(exactPosting, environment), true);
    assert.equal(state.execCalls, 1, "Rejected modern copy must attempt the fallback.");
    assert.equal(state.copiedText, exactPosting);
    assert.equal(state.removed.length, 1);
  }

  {
    const { environment, state } = createFallbackEnvironment({
      clipboard: { async writeText() { throw new Error("NotAllowedError"); } },
      execResult: false
    });
    assert.equal(await copyText(exactPosting, environment), false);
    assert.equal(state.execCalls, 1);
    assert.equal(state.removed.length, 1, "Failed fallback must still clean up its textarea.");
  }
}

async function runUiIntegrationTests() {
  const app = fs.readFileSync(path.join(__dirname, "..", "wwwroot", "app.js"), "utf8");
  const start = app.indexOf("async function copySelectedJobPosting()");
  const end = app.indexOf("function renderQualificationFit(", start);
  assert.ok(start >= 0 && end > start, "Copy Posting functions must remain testable as one integration block.");
  const source = `${app.slice(start, end)}\nthis.copySelectedJobPosting = copySelectedJobPosting;`;

  async function execute(copyResult) {
    let suppliedText;
    let timerCallback;
    const context = {
      state: {
        selectedJobId: "job-1",
        jobs: [{ stableId: "job-1", descriptionHtml: "<p>source</p>" }],
        copyFeedbackTimer: null
      },
      elements: {
        detailDescription: {},
        copyPostingLabel: { textContent: "Copy posting" },
        copyPostingButton: { title: "Copy full job posting" },
        copyPostingStatus: { textContent: "" }
      },
      JobPostingText: { toPlainText() { return exactPosting; } },
      ClipboardText: { async copyText(text) { suppliedText = text; return copyResult; } },
      COPY_FEEDBACK_MS: 1800,
      setTimeout(callback) { timerCallback = callback; return 1; },
      clearTimeout() {}
    };
    vm.runInNewContext(source, context);
    await context.copySelectedJobPosting();
    return { context, suppliedText, timerCallback };
  }

  const success = await execute(true);
  assert.equal(success.suppliedText, exactPosting, "UI must supply unchanged posting text to clipboard transport.");
  assert.equal(success.context.elements.copyPostingLabel.textContent, "Copied");
  assert.equal(success.context.elements.copyPostingStatus.textContent, "Full job posting copied to clipboard.");
  success.timerCallback();
  assert.equal(success.context.elements.copyPostingLabel.textContent, "Copy posting");
  assert.equal(success.context.elements.copyPostingButton.title, "Copy full job posting");
  assert.equal(success.context.elements.copyPostingStatus.textContent, "");

  const failure = await execute(false);
  assert.equal(failure.context.elements.copyPostingLabel.textContent, "Copy failed");
  assert.match(failure.context.elements.copyPostingStatus.textContent, /could not be copied/);
}

(async () => {
  await runTransportTests();
  await runUiIntegrationTests();
  console.log("All clipboard transport and Copy Posting UI regression tests passed.");
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
