"use strict";
(function (root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  else root.HumanReview = api;
})(globalThis, () => {
  const endpoint = "/api/admin/cheap-triage/human-review";
  function mount(panel, env = globalThis, options = {}) {
    const doc = env.document;
    let report, index = 0, selected = "", note = "", busy = false, initialized = false;
    const make = (tag, value) => { const n = doc.createElement(tag); n.textContent = value; return n; };
    const heading = make("h4", "Human Review");
    const message = make("p", "Loading human review queue…"); message.setAttribute("role", "status");
    const content = doc.createElement("div"); content.className = "human-review-case";
    panel.replaceChildren(heading, message, content);
    const button = (label, action) => { const b = make("button", label); b.type = "button"; b.className = "primary-button admin-evaluation-action"; b.addEventListener("click", action); return b; };
    async function load() {
      try {
        const response = await env.fetch(endpoint, { cache: "no-store" });
        if (!response.ok) throw new Error();
        report = await response.json(); index = Math.min(index, report.cases.length - 1);
        if (!initialized && options.guided) { const remaining = report.cases.findIndex(c => !report.reviews[c.stableJobId]); if (remaining >= 0) index = remaining; }
        initialized = true; draw();
      } catch { message.textContent = "Human review unavailable. Retry to load the queue."; content.replaceChildren(button("Retry", load)); }
    }
    function draw() {
      const item = report.cases[index], saved = report.reviews[item.stableJobId];
      selected = saved?.decision || ""; note = saved?.note || ""; busy = false;
      content.replaceChildren();
      message.textContent = `${index + 1} of ${report.cases.length} · ${report.reviewed} reviewed · ${report.remaining} remaining${report.complete ? " · Review Complete" : ""}`;
      if (report.complete && !options.guided) {
        const completion = doc.createElement("section"); completion.className = "human-review-complete admin-evaluation-card";
        completion.append(make("h4", "Human Review Complete"),
          make("p", `${report.reviewed} reviewed · KEEP ${report.counts.KEEP} · REJECT ${report.counts.REJECT} · AMBIGUOUS ${report.counts.AMBIGUOUS}`),
          make("p", `Queue ${report.queueVersion} · SHA-256 ${report.queueFingerprint}`),
          make("p", "Prepare a request using your saved decisions. Rules have NOT been changed. Nothing runs automatically."));
        const maintenance = typeof module === "object" && module.exports ? require("./rule-maintenance.js") : env.RuleMaintenance;
        completion.append(maintenance.createButton(env, { title: "Prepare Rule Update", endpoint: `${endpoint}/maintenance-prompt` }));
        content.append(completion);
      }
      const question = make("h3", `Does this job belong to ${report.categoryName}?`);
      question.className = "human-review-question";
      const controls = doc.createElement("div"); controls.className = "human-review-controls human-review-decisions";
      const choices = ["KEEP", "REJECT", "AMBIGUOUS"].map(value => button(value, () => { selected = value; sync(); }));
      if (options.guided) choices.forEach(b => b.className += " confirmation-secondary-button");
      controls.append(...choices);
      const help = doc.createElement("div"); help.className = "human-review-help";
      const legend = doc.createElement("dl"); legend.className = "human-review-legend";
      for (const [decision, meaning] of [["KEEP", `belongs to ${report.categoryName}`],
        ["REJECT", `does not belong to ${report.categoryName}`], ["AMBIGUOUS", "genuinely unclear"]]) {
        const entry = doc.createElement("div"); entry.append(make("dt", decision), make("dd", `— ${meaning}`)); legend.append(entry);
      }
      help.append(legend, make("p", "Judge the job's primary duties, not whether you would personally apply."));
      content.append(question, controls, help,

        make("h4", item.title), make("p", item.employer));
      for (const excerpt of [item.dutyExcerpt1, item.dutyExcerpt2]) if (excerpt) content.append(make("blockquote", excerpt));
      if (!item.descriptionAvailable) content.append(make("p", "No description available. Review the title; choose AMBIGUOUS if unsure."));
      // Presentation labels only: preserve the exact machine reason in the audit details.
      const families = {
        "trades-construction": "construction or trades", civil: "civil or structural engineering",
        "operator-technician": "operator or technician work", hr: "human resources or recruiting",
        "environment-field": "environmental field work", "aircraft-maintenance-training": "aircraft maintenance or training",
        "supplier-administration": "supplier administration", "business-other": "business administration",
        sales: "sales", finance: "finance or accounting"
      };
      const reasons = (item.reason || "").split("; ").map(reason => {
        const family = reason.replace("Corroborated unrelated occupational duties: ", "");
        return families[family];
      });
      const explanation = reasons.length && reasons.every(Boolean)
        ? `The rules found title and duties evidence of ${[...new Set(reasons)].join(" and ")}.`
        : "The rules classified this posting using its title and available duties. Review the excerpt to decide whether you agree.";
      content.append(make("p", `Current Cheap Triage: ${item.currentDecision}. ${explanation}`));
      const evidence = doc.createElement("details");
      evidence.append(make("summary", "Show technical details"),
        make("p", `Audit: ${item.auditClassification} · Family: ${item.family}`),
        make("p", `Original reason: ${item.reason} · Category: ${item.category}`),
        make("p", `Matched rule IDs: ${item.matchedRuleIds}`), make("p", `Matched predicates and raw evidence: ${item.matchedEvidence}`),
        make("p", `Description: ${item.descriptionAvailable ? "backed by cached description" : "title only"}. Historical Job Fit: ${item.jobFitScore || "not captured"}.`),
        make("p", `Workflow snapshot: ${item.workflowState}`), make("p", `Why queued: ${item.reviewReason}`),
        make("p", `${report.queueVersion} · Queue ${report.queueFingerprint} · Source manifest ${report.sourceManifestHash}`),
        make("p", `Ruleset version: ${item.rulesetVersion || report.rulesetVersion || "not captured in this queue"}. Ruleset hash: ${item.rulesetHash || report.rulesetHash || "not captured in this queue"}.`),
        make("pre", JSON.stringify({categoryName: report.categoryName, case: item,
          revisions: report.revisions?.filter(r => r.stableJobId === item.stableJobId) || [], saved}, null, 2)));
      const label = make("label", "Reviewer note (optional, up to 2000 characters)");
      const notes = doc.createElement("textarea"); notes.className = "text-control"; notes.maxLength = 2000; notes.rows = 3; notes.value = note;
      notes.setAttribute("aria-label", "Reviewer note"); label.append(notes);
      notes.addEventListener("input", () => { note = notes.value; sync(); });
      const state = make("p", ""); state.setAttribute("role", "status");
      const save = button(index === report.cases.length - 1 ? "Save review" : "Save and Next", async () => {
        busy = true; sync(); state.textContent = "Saving…";
        try {
          const response = await env.fetch(endpoint, { method: "POST", cache: "no-store", headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ queueFingerprint: report.queueFingerprint, stableJobId: item.stableJobId,
              decision: selected, note, expectedRevision: saved?.revision || 0 }) });
          if (!response.ok) throw new Error(response.status === 409 ? "Review or queue changed. Discard draft and reload before saving." : "Save failed. Your draft is still here; retry.");
          report = await response.json(); index = Math.min(index + 1, report.cases.length - 1); draw(); options.onSaved?.(report);
        } catch (error) { busy = false; sync(); state.textContent = error.message; }
      });
      if (options.guided) save.setAttribute("data-workflow-primary", "true");
      const discard = button("Discard draft / Reload", load);
      const previous = button("Previous", () => { index--; draw(); });
      const next = button("Next", () => { index++; draw(); });
      const download = make("a", "Download human-reviewed JSON"); download.className = "source-posting-button"; download.href = `${endpoint}/export`;
      download.setAttribute("download", "jsm-human-review.json");
      function sync() {
        const dirty = selected !== (saved?.decision || "") || note !== (saved?.note || "");
        choices.forEach((b, i) => { b.disabled = busy; b.setAttribute("aria-pressed", String(selected === ["KEEP", "REJECT", "AMBIGUOUS"][i])); });
        notes.disabled = busy; save.disabled = busy || !selected || !dirty; discard.disabled = busy;
        previous.disabled = busy || dirty || index === 0; next.disabled = busy || dirty || index === report.cases.length - 1;
        state.textContent = busy ? "Saving…" : dirty ? "Unsaved changes. Save or discard before navigating." : saved ? `Saved selection: ${saved.decision}` : "Not reviewed";
      }
      const navigation = doc.createElement("div"); navigation.className = "human-review-controls";
      for (const secondary of [previous, next, discard]) secondary.className += " confirmation-secondary-button";
      navigation.append(previous, save, next, discard);
      const audit = doc.createElement("div"); audit.className = "human-review-audit";
      audit.append(evidence, download);
      content.append(label, navigation, state,
        make("p", `KEEP ${report.counts.KEEP} · REJECT ${report.counts.REJECT} · AMBIGUOUS ${report.counts.AMBIGUOUS}. Rules have NOT been changed.`), audit); sync();
    }
    const ready = load();
    return { ready };
  }
  return { mount };
});
