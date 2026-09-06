"use strict";
(function (root, factory) {
  const api = factory();
  if (typeof module === "object" && module.exports) module.exports = api;
  else root.CheapTriage = api;
})(globalThis, () => {
  const loads = new WeakMap();
  function text(doc, tag, value) {
    const node = doc.createElement(tag); node.textContent = value; return node;
  }
  function evidence(doc, observation) {
    const details = doc.createElement("details");
    details.append(text(doc, "summary", "Matched rules and evidence"));
    const result = observation.result;
    details.append(text(doc, "p", `Rules: ${result.ruleIds.join(", ") || "none"}`));
    details.append(text(doc, "p", `Ruleset ${result.rulesetVersion} · SHA-256 ${result.rulesetFingerprint} · Input ${result.postingFingerprint}`));
    details.append(text(doc, "p", `Analyzed ${observation.analyzedAtUtc} · ${observation.descriptionAvailable ? "Title + description" : "Title only"}`));
    for (const item of result.evidence) details.append(text(doc, "p", `${item.predicateId} (${item.scope}): ${item.text}`));
    return details;
  }
  async function renderDetail(panel, job, env = globalThis, force = false) {
    const previous = loads.get(panel);
    if (!force && previous?.job === job) return;
    previous?.abort.abort();
    const abort = new AbortController(); const load = { job, abort }; loads.set(panel, load);
    panel.replaceChildren();
    if (!job) return;
    const doc = env.document;
    const details = doc.createElement("details");
    const summary = text(doc, "summary", "Cheap Triage · Loading observation…");
    details.append(summary); panel.append(details);
    try {
      const response = await env.fetch(`/api/jobs/cheap-triage?stableId=${encodeURIComponent(job.stableId)}`,
        { cache: "no-store", signal: abort.signal });
      if (!response.ok) throw new Error("Unavailable");
      const value = await response.json();
      if (loads.get(panel) !== load) return;
      const observation = value.observation;
      summary.textContent = `Cheap Triage · ${value.mode} · ${observation ? observation.decision : "Not analyzed"}${observation && !value.current ? " (stale)" : ""}`;
      details.append(text(doc, "p", "Observation only. This decision does not change Job Fit, visibility, or workflow state."));
      if (observation) {
        details.append(text(doc, "p", `${observation.result.category}: ${observation.result.reason}`), evidence(doc, observation));
      } else details.append(text(doc, "p", value.mode === "Off" ? "Live evaluation is disabled." : "Background observation is pending."));
      if (!value.current && value.mode === "Shadow") {
        const refresh = text(doc, "button", "Refresh observation"); refresh.type = "button";
        refresh.addEventListener("click", () => renderDetail(panel, job, env, true)); details.append(refresh);
      }
    } catch {
      if (loads.get(panel) === load) summary.textContent = "Cheap Triage · Observation unavailable; Job Fit is unaffected";
    }
  }
  function renderLive(live, env = globalThis, score = () => null) {
    const doc = env.document; const section = doc.createElement("section");
    section.append(text(doc, "h4", "Live Shadow observation"));
    if (!live) { section.append(text(doc, "p", "Live observations are unavailable.")); return section; }
    section.append(text(doc, "p", `Mode: ${live.mode}. ${live.mode === "Off" ? "No new live evaluation is performed; recorded observations are retained." : "All jobs continue through normal Job Fit processing."}`));
    section.append(text(doc, "p", `Scope: ${live.scope}. Company: ${live.companyId}. Counts describe current cached postings, not traffic or saved calls.`));
    section.append(text(doc, "p", `${live.analyzedJobs} current of ${live.totalJobs} cached · KEEP ${live.keep} · REJECT ${live.reject} · UNDETERMINED ${live.undetermined} · Title only ${live.titleOnly} · Description-backed ${live.descriptionBacked} · Rejection ${live.rejectionPercentage.toFixed(2)}%`));
    section.append(text(doc, "p", `${live.requiringReconciliation} missing/stale observations · ${live.stalePreviousRuleset} under previous rules · ${live.running ? "Reconciliation running" : "Reconciliation idle"} · Latest analysis: ${live.latestAnalysisUtc || "none"} · Last reconciliation (this process): ${live.lastReconciliationUtc || "none"}`));
    if (live.error) section.append(text(doc, "p", live.error));
    const review = doc.createElement("details"); review.append(text(doc, "summary", `Review recorded REJECT decisions (${live.rejectedJobs.length})`));
    review.append(text(doc, "p", "Includes stale recorded rejects, labeled below; stale results are excluded from current counts. These jobs remain in their existing workflow state."));
    const rows = live.rejectedJobs.map(row => ({ ...row, jobFitScore: score(row.job) }));
    const exportButton = text(doc, "button", "Export review JSON"); exportButton.type = "button";
    exportButton.addEventListener("click", () => {
      const blob = new env.Blob([JSON.stringify({ exportedAtUtc: new Date().toISOString(), mode: live.mode,
        scope: live.scope, rulesetVersion: live.rulesetVersion, rulesetFingerprint: live.rulesetFingerprint,
        provisionalReviewOnly: true, jobs: rows }, null, 2)], { type: "application/json" });
      const url = env.URL.createObjectURL(blob); const link = doc.createElement("a");
      link.href = url; link.download = "jsm-shadow-reject-review.json"; link.click(); env.URL.revokeObjectURL(url);
    });
    review.append(exportButton);
    for (const row of rows) {
      const entry = doc.createElement("details");
      entry.append(text(doc, "summary", `${row.employer} · ${row.job.title} · REJECT${row.current ? "" : " (stale)"} · Job Fit ${row.jobFitScore == null ? "TBD" : `${row.jobFitScore}/10`} · ${row.workflowState}`));
      entry.append(text(doc, "p", `${row.job.stableId} · Source ${row.sourceAvailable ? "available" : "unavailable"} · ${row.observation.result.category}: ${row.observation.result.reason}`), evidence(doc, row.observation));
      review.append(entry);
    }
    section.append(review); return section;
  }
  return { renderDetail, renderLive };
});
