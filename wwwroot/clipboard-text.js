"use strict";

(function registerClipboardText(root, factory) {
  const api = factory(root);
  if (typeof module === "object" && module.exports) {
    module.exports = api;
  } else {
    root.ClipboardText = api;
  }
})(typeof globalThis !== "undefined" ? globalThis : this, root => {
  async function copyText(value, environment = root) {
    const text = String(value ?? "");
    const clipboard = environment?.navigator?.clipboard;
    if (typeof clipboard?.writeText === "function") {
      try {
        await clipboard.writeText(text);
        return true;
      } catch {
        // Permission and secure-context failures can still use the synchronous fallback.
      }
    }

    return copyUsingExecCommand(text, environment);
  }

  function copyUsingExecCommand(text, environment) {
    const document = environment?.document;
    const parent = document?.body || document?.documentElement;
    if (!document?.createElement || !parent?.appendChild || typeof document.execCommand !== "function") {
      return false;
    }

    const activeElement = document.activeElement;
    const selection = typeof environment.getSelection === "function"
      ? environment.getSelection()
      : typeof document.getSelection === "function"
        ? document.getSelection()
        : null;
    const savedRanges = [];
    if (selection) {
      for (let index = 0; index < selection.rangeCount; index += 1) {
        savedRanges.push(selection.getRangeAt(index).cloneRange());
      }
    }

    const textarea = document.createElement("textarea");
    textarea.value = text;
    textarea.setAttribute("readonly", "");
    textarea.setAttribute("aria-hidden", "true");
    textarea.tabIndex = -1;
    textarea.style.position = "fixed";
    textarea.style.inset = "0 auto auto -9999px";
    textarea.style.opacity = "0";

    let copied = false;
    try {
      parent.appendChild(textarea);
      textarea.focus();
      textarea.select();
      textarea.setSelectionRange?.(0, textarea.value.length);
      copied = document.execCommand("copy") === true;
    } catch {
      copied = false;
    } finally {
      if (typeof textarea.remove === "function") {
        textarea.remove();
      } else if (textarea.parentNode) {
        textarea.parentNode.removeChild(textarea);
      }

      if (selection && savedRanges.length) {
        selection.removeAllRanges();
        for (const range of savedRanges) {
          selection.addRange(range);
        }
      }
      activeElement?.focus?.();
    }

    return copied;
  }

  return { copyText };
});
