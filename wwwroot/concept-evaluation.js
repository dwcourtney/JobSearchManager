(function (root) {
  "use strict";
  const percent = value => Number.isFinite(value) ? `${(value * 100).toFixed(1)}%` : "—";
  const number = value => Number(value).toLocaleString();
  function element(tag, text, className) {
    const node = document.createElement(tag);
    if (text !== undefined) node.textContent = text;
    if (className) node.className = className;
    return node;
  }
  function disclosure(title) {
    const node = element("details", undefined, "admin-evaluation-references evaluation-disclosure");
    node.append(element("summary", title));
    return node;
  }
  function select(label, options) {
    const wrapper = element("label", undefined, "evaluation-control");
    wrapper.append(element("span", label));
    const control = element("select", undefined, "text-control admin-evaluation-filter-select");
    control.setAttribute("aria-label", label);
    for (const [value, text] of options) {
      const option = element("option", text); option.value = value; control.append(option);
    }
    wrapper.append(control);
    return { wrapper, control };
  }
  function table(headers, rows) {
    const wrapper = element("div", undefined, "admin-rule-table-viewport evaluation-table-scroll");
    wrapper.tabIndex = 0; wrapper.setAttribute("role", "region"); wrapper.setAttribute("aria-label", `${headers[0]} metrics table`);
    const node = element("table", undefined, "admin-compact-table evaluation-table");
    const head = element("thead"), tr = element("tr"), body = element("tbody");
    for (const header of headers) { const th = element("th", header); th.scope = "col"; tr.append(th); }
    head.append(tr);
    for (const row of rows) { const line = element("tr"); for (const value of row) line.append(element("td", String(value))); body.append(line); }
    node.append(head, body); wrapper.append(node); return wrapper;
  }
  function filteredConcepts(concepts, query, sort) {
    return concepts.filter(c => `${c.name} ${c.id}`.toLowerCase().includes(query.toLowerCase()))
      .slice().sort((a, b) => sort === "name" ? a.name.localeCompare(b.name) : a[sort] - b[sort] || a.name.localeCompare(b.name));
  }
  function comparison(latest, previous) {
    if (!previous) return "This is the first refreshable evaluation run.";
    const delta = (latest.summary.micro.f1 - previous.summary.micro.f1) * 100;
    const same = latest.technical.referenceHash === previous.technical.referenceHash;
    return `Micro F1 change: ${delta >= 0 ? "+" : ""}${delta.toFixed(1)} percentage points versus ${previous.runId}. ${same ? "Same frozen reference set." : "Different reference samples or labels: this difference cannot be attributed solely to rule changes."}`;
  }
  function chart(curve, label) {
    const box = element("div", undefined, "evaluation-pr");
    if (!curve || !curve.points.length) { box.append(element("p", "Insufficient positive/negative support for a PR curve.")); return box; }
    const ns = "http://www.w3.org/2000/svg";
    const svg = document.createElementNS(ns, "svg");
    svg.setAttribute("viewBox", "0 0 560 300"); svg.setAttribute("role", "img"); svg.setAttribute("aria-label", `${label} precision-recall curve from observed diagnostic-score thresholds`);
    const title = document.createElementNS(ns, "title"); title.textContent = `${label}: precision versus recall`; svg.append(title);
    function shape(tag, attrs, text) { const node = document.createElementNS(ns, tag); for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, String(value)); if (text) node.textContent = text; svg.append(node); }

    for (const value of [0, .25, .5, .75, 1]) {
      const x = 50 + 490 * value, y = 255 - 235 * value;
      shape("path", { d: `M ${x} 20 V 255 M 50 ${y} H 540`, class: "evaluation-pr-grid" });
      shape("text", { x, y: 274, "text-anchor": "middle" }, String(value));
      shape("text", { x: 38, y: y + 4, "text-anchor": "end" }, String(value));
    }
    shape("path", { d: "M 50 20 V 255 H 540", class: "evaluation-pr-axis" });
    shape("text", { x: 295, y: 297, "text-anchor": "middle" }, "Recall");
    shape("text", { x: 12, y: 140, transform: "rotate(-90 12 140)", "text-anchor": "middle" }, "Precision");
    // Step display of raw threshold points. AP is calculated from counts, never chart pixels.
    const points = curve.points;
    let path = `M ${50 + 490 * points[0].recall} ${255 - 235 * points[0].precision}`;
    for (const point of points.slice(1)) path += ` V ${255 - 235 * point.precision} H ${50 + 490 * point.recall}`;
    shape("path", { d: path, class: "evaluation-pr-line" });
    for (const point of points) shape("circle", { cx: 50 + 490 * point.recall, cy: 255 - 235 * point.precision, r: 3, class: "evaluation-pr-point" });
    box.append(svg, element("p", `Average precision: ${percent(curve.averagePrecision)}. Tied scores enter together; ${points.length - 1} observed thresholds. Evidence ranking, not calibrated confidence.`));
    const raw = disclosure("PR thresholds and confusion counts");
    raw.append(table(["Threshold ≥", "Precision", "Recall", "TP", "FP", "FN", "TN"], points.map(p => [p.threshold === null ? "Above all scores" : p.threshold.toFixed(6), percent(p.precision), percent(p.recall), p.tp, p.fp, p.fn, p.tn])));
    box.append(raw); return box;
  }
  function render(view) {
    const section = element("section", undefined, "evaluation-run");
    for (const error of view?.errors || []) section.append(element("p", error));
    if (!view?.latest) return section;
    const { artifact: run, status, ageDays } = view.latest;
    const overview = element("section", undefined, "admin-evaluation-card admin-rule-section evaluation-overview");
    const header = element("header", undefined, "evaluation-heading");
    header.append(element("h4", "Latest evaluation"), element("span", status, "summary-status"));
    overview.append(header, element("p", `${new Date(run.completedUtc).toLocaleString()} · ${ageDays} days old`, "admin-evaluation-metadata"),
      element("strong", "AI-reference / machine-adjudicated — Not human ground truth"),
      element("p", "Prediction-blinded, independently labeled A/B passes and a third blinded adjudication of disagreements."),
      element("p", run.methodology));
    const summary = run.summary;
    overview.append(element("p", `${number(summary.postings)} postings · ${number(summary.totalPossible)} possible decisions · ${number(summary.resolved)} resolved · ${number(summary.unresolved)} unresolved/excluded`),
      element("p", `${number(summary.micro.support)} reference positives · ${number(summary.micro.negativeSupport)} reference negatives. CURRENT requires matching identities and age ≤ ${view.freshnessDays} days.`));
    const metrics = element("dl", undefined, "admin-evaluation-metrics evaluation-metrics");
    for (const [name, value] of [["Precision (micro)", summary.micro.precision], ["Recall (micro)", summary.micro.recall], ["F1 (micro)", summary.micro.f1], ["F1 (macro)", summary.macro.f1]]) { const card = element("div"); card.append(element("dt", name), element("dd", percent(value))); metrics.append(card); }
    overview.append(metrics, element("p", "Macro averages all concepts, including those with no positive support (zero-division = 0). Unresolved decisions are excluded from all scored denominators."), element("p", comparison(run, view.previous?.artifact)));
    section.append(overview);
    const chartPanel = element("section", undefined, "admin-evaluation-card admin-rule-section evaluation-chart-panel");
    chartPanel.append(element("h4", "Precision–Recall"));
    const choice = select("Precision–Recall curve", [["micro", "Overall micro"], ...run.perConcept.filter(c => c.curve).map(c => [c.id, c.name])]);
    const plot = element("div");
    const draw = () => { const c = run.perConcept.find(c => c.id === choice.control.value); plot.replaceChildren(chart(c ? c.curve : run.microCurve, c ? c.name : "Overall micro")); };
    choice.control.addEventListener("change", draw); draw();
    const policy = run.technical.scoringPolicy;
    chartPanel.append(choice.wrapper, element("p", `Per-concept curves require at least ${policy.minimumPositiveSupport} reference positives and ${policy.minimumNegativeSupport} negatives. Diagnostic evidence scores do not change production yes/no decisions or Job Fit.`), plot);
    section.append(chartPanel);
    const conceptPanel = element("section", undefined, "admin-evaluation-card admin-rule-section evaluation-concepts");
    conceptPanel.append(element("h4", "Per-concept results"));
    const searchLabel = element("label", undefined, "evaluation-control"); searchLabel.append(element("span", "Filter concepts")); const search = element("input", undefined, "text-control admin-evaluation-filter-input"); search.setAttribute("aria-label", "Filter concepts"); search.type = "search"; searchLabel.append(search);
    const sort = select("Sort concepts", [["f1", "Lowest F1"], ["support", "Lowest support"], ["precision", "Lowest precision"], ["recall", "Lowest recall"], ["name", "Concept name"]]);
    const results = element("div");
    const refresh = () => results.replaceChildren(table(["Concept", "Precision", "Recall", "F1", "Positive support", "TP", "FP", "FN", "TN", "AP"], filteredConcepts(run.perConcept, search.value || "", sort.control.value || "f1").map(c => [c.name, percent(c.precision), percent(c.recall), percent(c.f1), c.support, c.tp, c.fp, c.fn, c.tn, c.curve ? percent(c.curve.averagePrecision) : "Insufficient support"])));
    search.addEventListener("input", refresh); sort.control.addEventListener("change", refresh); refresh(); const filters = element("div", undefined, "admin-rule-filters"); filters.append(searchLabel, sort.wrapper); conceptPanel.append(filters, results); section.append(conceptPanel);
    const errors = disclosure("Inspect disagreements");
    const type = select("Decision type", [["FP", "False positives"], ["FN", "False negatives"], ["unresolved", "Unresolved"]]);
    const concept = select("Disagreement concept", [["", "All concepts"], ...run.perConcept.map(c => [c.id, c.name])]);
    const examples = element("div"), more = element("button", "Show more", "secondary-button admin-evaluation-action"); more.type = "button";
    let limit = 20;
    const renderErrors = () => {
      const selected = run.disagreements.filter(e => e.type === (type.control.value || "FP") && (!concept.control.value || e.conceptId === concept.control.value));
      examples.replaceChildren(element("p", `${Math.min(limit, selected.length)} of ${selected.length} cases`));
      for (const e of selected.slice(0, limit)) {
        const item = disclosure(`${e.title} · ${e.company} · ${e.conceptId}`);
        item.append(element("p", `${e.type} · Reference ${e.reference === null ? "unresolved" : e.reference} · JSM ${e.prediction} · Diagnostic score ${e.score.toFixed(4)}`),
          element("p", `Posting: ${e.postingId}`), element("p", `Matched rules: ${e.ruleIds.join(", ") || "None"}`), element("p", `Accepted evidence: ${e.evidence || "None"}`), element("p", `Reference evidence: ${e.referenceEvidence || "None"}`), element("p", e.excerpt));
        examples.append(item);
      }
      more.hidden = limit >= selected.length;
    };
    for (const control of [type.control, concept.control]) control.addEventListener("change", () => { limit = 20; renderErrors(); });
    more.addEventListener("click", () => { limit += 20; renderErrors(); }); renderErrors(); const errorFilters = element("div", undefined, "admin-rule-filters"); errorFilters.append(type.wrapper, concept.wrapper); errors.append(errorFilters, examples, more); section.append(errors);
    const history = disclosure("Historical evaluation runs");
    if (!view.history?.length) history.append(element("p", "No earlier refreshable evaluation runs."));
    for (const entry of view.history || []) {
      const r = entry.artifact, item = disclosure(`${r.completedUtc.slice(0, 10)} · ${r.runId}`);
      item.children[0].append(element("span", entry.status, "summary-status"));
      item.append(element("p", `Ruleset ${r.authority.version} · ${r.summary.postings} postings · Micro F1 ${percent(r.summary.micro.f1)} · Macro F1 ${percent(r.summary.macro.f1)}`), element("p", comparison(run, r)), chart(r.microCurve, r.runId)); history.append(item);
    }
    section.append(history);
    const technical = disclosure("Technical details");
    technical.append(element("pre", JSON.stringify({ runId: run.runId, authority: run.authority, ...run.technical }, null, 2)));
    section.append(technical); return section;
  }
  const api = { render, chart, filteredConcepts, comparison, percent };
  if (typeof module === "object" && module.exports) module.exports = api;
  else root.ConceptEvaluationUi = api;
})(typeof globalThis !== "undefined" ? globalThis : this);
