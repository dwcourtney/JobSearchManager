"use strict";
(function (root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  else root.HumanReview = api;
})(globalThis, () => {
  const endpoint = "/api/admin/cheap-triage/human-review";
  function mount(panel, env = globalThis) {
    const doc = env.document;
    let report, index = 0, selected = "", note = "", busy = false;
    const make = (tag, value) => { const n = doc.createElement(tag); n.textContent = value; return n; };
    const heading = make("h4", "Human Review");
    const message = make("p", "Loading human review queue…"); message.setAttribute("role", "status");
    const content = doc.createElement("div");
    panel.replaceChildren(heading, message, content);
    const button = (label, action) => { const b = make("button", label); b.type = "button"; b.addEventListener("click", action); return b; };
    async function load() {
      try {
        const response = await env.fetch(endpoint, { cache: "no-store" });
        if (!response.ok) throw new Error();
        report = await response.json(); index = Math.min(index, report.cases.length - 1); draw();
      } catch { message.textContent = "Human review unavailable. Retry to load the queue."; content.replaceChildren(button("Retry", load)); }
    }
    function draw() {
      const item = report.cases[index], saved = report.reviews[item.stableJobId];
      selected = saved?.decision || ""; note = saved?.note || ""; busy = false;
      content.replaceChildren();
      message.textContent = `${index + 1} of ${report.cases.length} · ${report.reviewed} reviewed · ${report.remaining} remaining${report.complete ? " · Review Complete" : ""}`;
      content.append(make("p", `KEEP ${report.counts.KEEP} · REJECT ${report.counts.REJECT} · AMBIGUOUS ${report.counts.AMBIGUOUS}. Rules have NOT been changed. All jobs continue normally.`));
      content.append(make("h5", `${item.employer} · ${item.title}`), make("p", `${item.stableJobId} · ${item.family}`),
        make("p", `Current Cheap Triage: ${item.currentDecision} · Audit: ${item.auditClassification}`),
        make("p", `${item.category}: ${item.reason}`), make("p", `Matched rules: ${item.matchedRuleIds}`),
        make("p", `Description: ${item.descriptionAvailable ? "backed by cached description" : "title only"}. Historical Job Fit: ${item.jobFitScore || "not captured"}.`),
        make("p", `Workflow snapshot: ${item.workflowState}`),
        make("p", "Review broad technical/engineering duties, not personal suitability. Historical score/workflow are context only. Choose a decision, then Save review."));
      for (const excerpt of [item.dutyExcerpt1, item.dutyExcerpt2]) if (excerpt) content.append(make("blockquote", excerpt));
      content.append(make("p", `Why queued: ${item.reviewReason}`));
      const evidence = doc.createElement("details"); evidence.append(make("summary", "Matched evidence and queue provenance"),
        make("p", item.matchedEvidence), make("p", `${report.queueVersion} · Queue ${report.queueFingerprint} · Source manifest ${report.sourceManifestHash}`));
      content.append(evidence);
      if (saved) content.append(make("p", `Saved ${saved.decision} · ${saved.reviewedAtUtc} · Reviewer ${saved.reviewer}`));
      const controls = doc.createElement("div"); controls.className = "human-review-controls";
      const choices = ["KEEP", "REJECT", "AMBIGUOUS"].map(value => button(value, () => { selected = value; sync(); }));
      const label = make("label", "Reviewer note (optional, up to 2000 characters)");
      const notes = doc.createElement("textarea"); notes.maxLength = 2000; notes.rows = 3; notes.value = note;
      notes.setAttribute("aria-label", "Reviewer note"); label.append(notes);
      notes.addEventListener("input", () => { note = notes.value; sync(); });
      const state = make("p", ""); state.setAttribute("role", "status");
      const save = button("Save review", async () => {
        busy = true; sync(); state.textContent = "Saving…";
        try {
          const response = await env.fetch(endpoint, { method: "POST", cache: "no-store", headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ queueFingerprint: report.queueFingerprint, stableJobId: item.stableJobId,
              decision: selected, note, expectedRevision: saved?.revision || 0 }) });
          if (!response.ok) throw new Error(response.status === 409 ? "Review or queue changed. Discard draft and reload before saving." : "Save failed. Your draft is still here; retry.");
          report = await response.json(); draw();
        } catch (error) { busy = false; sync(); state.textContent = error.message; }
      });
      const discard = button("Discard draft / Reload", load);
      const previous = button("Previous", () => { index--; draw(); });
      const next = button("Next", () => { index++; draw(); });
      const download = make("a", "Download human-reviewed JSON"); download.href = `${endpoint}/export`;
      download.setAttribute("download", "jsm-human-review.json");
      function sync() {
        const dirty = selected !== (saved?.decision || "") || note !== (saved?.note || "");
        choices.forEach((b, i) => { b.disabled = busy; b.setAttribute("aria-pressed", String(selected === ["KEEP", "REJECT", "AMBIGUOUS"][i])); });
        notes.disabled = busy; save.disabled = busy || !selected || !dirty; discard.disabled = busy;
        previous.disabled = busy || dirty || index === 0; next.disabled = busy || dirty || index === report.cases.length - 1;
        state.textContent = busy ? "Saving…" : dirty ? "Unsaved changes. Save review or discard before navigating. Export contains saved decisions only." : saved ? `Saved selection: ${saved.decision}` : "Not reviewed";
      }
      controls.append(...choices, save, discard, previous, next);
      content.append(label, controls, state, download); sync();
    }
    const ready = load();
    return { ready };
  }
  return { mount };
});
