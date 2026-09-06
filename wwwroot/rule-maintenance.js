"use strict";
(function (root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  else root.RuleMaintenance = api;
})(globalThis, () => {
  function createButton(env = globalThis) {
    const button = env.document.createElement("button");
    button.type = "button";
    button.textContent = "Update RegEx Rules";
    button.addEventListener("click", () => open(button, env));
    return button;
  }

  async function open(opener, env = globalThis) {
    if (opener.disabled) return;
    opener.disabled = true;
    const doc = env.document;
    const dialog = doc.createElement("dialog");
    dialog.className = "rule-maintenance-dialog";
    dialog.setAttribute("aria-labelledby", "rule-maintenance-title");
    const title = doc.createElement("h2");
    title.id = "rule-maintenance-title";
    title.textContent = "Update RegEx Rules";
    const status = doc.createElement("p");
    status.setAttribute("role", "status");
    status.textContent = "Preparing the rule maintenance prompt…";
    const prompt = doc.createElement("textarea");
    prompt.readOnly = true;
    prompt.setAttribute("aria-label", "Complete Codex maintenance prompt");
    prompt.rows = 18;
    const copy = doc.createElement("button");
    copy.type = "button"; copy.textContent = "Copy Prompt"; copy.disabled = true;
    const close = doc.createElement("button");
    close.type = "button"; close.textContent = "Close";
    const abort = new AbortController();
    let closed = false;
    dialog.addEventListener("close", () => {
      closed = true; abort.abort(); dialog.remove(); opener.disabled = false; opener.focus();
    });
    close.addEventListener("click", () => dialog.close());
    copy.addEventListener("click", async () => {
      copy.disabled = true;
      try {
        try { await env.navigator.clipboard.writeText(prompt.value); }
        catch {
          prompt.focus(); prompt.select();
          if (!doc.execCommand("copy")) throw new Error("Clipboard unavailable");
        }
        if (!closed) status.textContent = "Complete prompt copied. Paste it into Codex to begin a reviewed update.";
      } catch {
        if (!closed) status.textContent = "Clipboard unavailable. Select the prompt and copy it manually.";
      } finally { copy.disabled = false; }
    });
    dialog.append(title, status, prompt, copy, close);
    doc.body.appendChild(dialog);
    dialog.showModal(); close.focus();
    try {
      const response = await env.fetch("/api/admin/cheap-triage/maintenance-prompt", { cache: "no-store", signal: abort.signal });
      if (!response.ok) throw new Error("Prompt unavailable");
      const value = await response.json();
      if (typeof value.prompt !== "string" || !value.prompt.trim()) throw new Error("Empty prompt");
      if (!closed) {
        prompt.value = value.prompt;
        status.textContent = `Ruleset ${value.rulesetVersion} · Prompt template ${value.templateVersion}. This action prepares instructions; it does not run Codex or change rules.`;
        copy.disabled = false;
      }
    } catch {
      if (!closed) status.textContent = "Unable to prepare the prompt. Close this window and try again.";
    }
    return dialog;
  }
  return { createButton, open };
});
