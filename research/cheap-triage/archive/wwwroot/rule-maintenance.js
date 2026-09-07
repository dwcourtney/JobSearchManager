"use strict";
(function (root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  else root.RuleMaintenance = api;
})(globalThis, () => {
  async function copyPrompt(value, env = globalThis) {
    try { await env.navigator.clipboard.writeText(value); }
    catch {
      const doc = env.document, previous = doc.activeElement;
      const field = doc.createElement("textarea"); field.className = "text-control visually-hidden";
      field.readOnly = true; field.value = value; doc.body.appendChild(field);
      try { field.focus(); field.select(); if (!doc.execCommand("copy")) throw new Error("Clipboard unavailable"); }
      finally { field.remove(); previous?.focus(); }
    }
  }
  function createButton(env = globalThis, options = {}) {
    const button = env.document.createElement("button");
    button.type = "button"; button.className = "primary-button admin-evaluation-action";
    button.textContent = options.title || "Update RegEx Rules";
    button.addEventListener("click", () => open(button, env, options));
    return button;
  }

  async function open(opener, env = globalThis, options = {}) {
    if (opener.disabled) return;
    opener.disabled = true;
    const doc = env.document;
    const dialog = doc.createElement("dialog");
    dialog.className = "rule-maintenance-dialog";
    dialog.setAttribute("aria-labelledby", "rule-maintenance-title");
    const title = doc.createElement("h2");
    title.id = "rule-maintenance-title";
    title.textContent = options.title || "Update RegEx Rules";
    const status = doc.createElement("p");
    status.setAttribute("role", "status");
    status.textContent = "Preparing the rule maintenance prompt…";
    const prompt = doc.createElement("textarea");
    prompt.readOnly = true; prompt.className = "text-control";
    prompt.setAttribute("aria-label", "Complete Codex maintenance prompt");
    prompt.rows = 18;
    const copy = doc.createElement("button");
    copy.type = "button"; copy.className = "primary-button"; copy.textContent = "Copy Prompt"; copy.disabled = true;
    const close = doc.createElement("button");
    close.type = "button"; close.className = "primary-button confirmation-secondary-button"; close.textContent = "Close";
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
      const response = options.prepared ? { ok: true, json: async () => options.prepared } : await env.fetch(options.endpoint || "/api/admin/cheap-triage/maintenance-prompt", { cache: "no-store", signal: abort.signal });
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
  async function renderStatus(panel, env = globalThis, score = () => null) {
    const doc = env.document;
    const title = doc.createElement("h3"); title.textContent = "Cheap Triage";
    const description = doc.createElement("p");
    description.textContent = "A separate KEEP/REJECT prefilter under review. It does not drive Job Fit scoring. All jobs continue through the existing Job Fit workflow.";
    const status = doc.createElement("p"); status.setAttribute("role", "status");
    status.className = "admin-evaluation-metadata";
    status.textContent = "Loading Cheap Triage status…";
    panel.replaceChildren(title, description, status);
    let diagnostics = panel, workflowControl;
    if (env.CheapTriageWorkflow) {
      const workflow = doc.createElement("section");
      diagnostics = doc.createElement("details");
      const summary = doc.createElement("summary"); summary.textContent = "Technical Details — live observations and offline evaluation";
      diagnostics.append(summary, description, status);
      panel.replaceChildren(title, workflow, diagnostics);
      workflowControl = env.CheapTriageWorkflow.mount(workflow, env);
    }
    try {
      const response = await env.fetch("/api/admin/cheap-triage/status", { cache: "no-store" });
      if (!response.ok) throw new Error("Unavailable");
      const value = await response.json();
      status.textContent = `Mode: ${value.mode} · Non-gating · Ruleset ${value.rulesetVersion} · SHA-256 ${value.rulesetFingerprint}. Configure CheapTriage:Mode as Off or Shadow and restart to change live observation. Active is not supported.`;
      const metrics = doc.createElement("p");
      const baseline = value.metricsCurrent && value.frozenEvaluation;
      if (baseline) {
        const percent = n => `${(n * 100).toFixed(2)}%`;
        metrics.textContent = `Offline evaluation — frozen provisional evidence: KEEP recall ${percent(baseline.slices.combined.keepRecall)}; described ${percent(baseline.slices.description.keepRecall)}; title-only ${percent(baseline.slices.title_only.keepRecall)}. ${baseline.slices.combined.falseRejects} known false rejects. Old-cache rejection ${percent(baseline.cache.rejectionRate)} is potential volume, not measured production savings. Human review is required before promotion.`;
      } else {
        metrics.textContent = "Frozen metrics do not match the loaded ruleset. Reevaluate before considering promotion.";
      }
      // Each load owns its nodes: an older response cannot overwrite a newer panel.
      if (status.parentNode === diagnostics && (diagnostics === panel || diagnostics.parentNode === panel)) {
        workflowControl?.setStatus?.(value);
        const refresh = doc.createElement("button"); refresh.type = "button"; refresh.className = "primary-button admin-evaluation-action confirmation-secondary-button"; refresh.textContent = "Refresh observations";
        refresh.addEventListener("click", () => renderStatus(panel, env, score));
        const liveApi = typeof module === "object" && module.exports ? require("./cheap-triage.js") : env.CheapTriage;
        diagnostics.append(metrics, refresh, liveApi.renderLive(value.live, env, score));
      }
    } catch {
      status.textContent = "Unable to load Cheap Triage status. Reopen this tab to retry.";
    }
  }
  return { createButton, open, renderStatus, copyPrompt };
});
