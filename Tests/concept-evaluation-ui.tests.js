"use strict";
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
class Element {
  constructor(tag) { this.tag = tag; this.children = []; this.attrs = {}; this.handlers = {}; this.value = ""; }
  append(...items) { this.children.push(...items); if (this.tag === "select" && !this.value) this.value = this.children[0].value; }
  setAttribute(k, v) { this.attrs[k] = v; }
  addEventListener(k, f) { this.handlers[k] = f; }
  replaceChildren(...items) { this.children = items; }
}
global.document = { createElement: tag => new Element(tag), createElementNS: (ns, tag) => new Element(tag) };
const ui = require("../wwwroot/concept-evaluation.js");
const walk = node => [node, ...node.children.flatMap(walk)];
const text = node => walk(node).map(n => n.textContent || "").join("\n");
const curve = { averagePrecision: .8, points: [{ threshold: null, precision: 1, recall: 0, tp: 0, fp: 0, fn: 5, tn: 5 }, { threshold: .75, precision: 1, recall: .8, tp: 4, fp: 0, fn: 1, tn: 5 }, { threshold: 0, precision: .5, recall: 1, tp: 5, fp: 5, fn: 0, tn: 0 }] };
const metric = { tp: 4, fp: 1, fn: 1, tn: 4, precision: .8, recall: .8, f1: .8, support: 5, negativeSupport: 5 };
const run = { runId: "fixture-only", completedUtc: "2026-09-08T00:00:00Z", methodology: "Same-model errors may be correlated.",
  summary: { postings: 10, totalPossible: 10, resolved: 10, unresolved: 0, micro: metric, macro: metric },
  perConcept: [{ id: "a", name: "Alpha", ...metric, curve }], microCurve: curve,
  disagreements: [{ conceptId: "a", type: "FP", title: "<script>untrusted</script>", company: "Example", postingId: "p", reference: false, prediction: true, score: .5, ruleIds: ["rule-1"], evidence: "Exact match", excerpt: "Public excerpt" }],
  authority: { version: "v1", pipelineFingerprint: "full-pipeline-hash" }, technical: { referenceHash: "reference-hash", scoringPolicyHash: "full-score-hash", scoringPolicy: { minimumPositiveSupport: 5, minimumNegativeSupport: 5 } } };
const rendered = ui.render({ latest: { artifact: run, status: "CURRENT", ageDays: 1 }, history: [], freshnessDays: 45, errors: [] });
assert.match(text(rendered), /Not human ground truth/);
assert.match(text(rendered), /independently labeled A\/B/);
assert.match(text(rendered), /10 possible decisions/);
assert.match(text(rendered), /80.0%/);
assert.equal(walk(rendered).filter(n => n.tag === "svg").length, 1);
assert.equal(walk(rendered).filter(n => n.tag === "circle").length, 3);
for (const disclosure of walk(rendered).filter(n => n.tag === "details")) assert.ok(!disclosure.open);
const technical = walk(rendered).find(n => n.tag === "details" && n.children[0].textContent === "Technical details");
assert.match(text(technical), /full-pipeline-hash/);
assert.match(text(technical), /full-score-hash/);
const visibleText = node => node.tag === "details" && !node.open ? node.children[0].textContent : (node.textContent || "") + node.children.map(visibleText).join(" ");
assert.doesNotMatch(visibleText(rendered), /full-pipeline-hash|full-score-hash/);
const search = walk(rendered).find(n => n.tag === "input"); search.value = "missing"; search.handlers.input();
const rowsText = () => walk(rendered).filter(n => n.tag === "tbody").map(text).join(" ");
assert.doesNotMatch(rowsText(), /Alpha/);
search.value = "Alpha"; search.handlers.input(); assert.match(rowsText(), /Alpha/);
assert.equal(ui.filteredConcepts([{ name: "Beta", id: "b", f1: .2 }, { name: "Alpha", id: "a", f1: .5 }], "", "f1")[0].id, "b");
assert.match(ui.comparison(run, { ...run, runId: "previous", technical: { referenceHash: "different" } }), /cannot be attributed solely/);
assert.doesNotMatch(text(ui.render({ latest: null, errors: [] })), /80.0%/);
const app = fs.readFileSync(path.join(root, "wwwroot/app.js"), "utf8");
assert.match(app, /adminEvaluationAccuracy\.hidden = Boolean\(result\.evaluation\?\.latest\)/);
assert.match(app, /ConceptEvaluationUi\.render\(result\.evaluation\), renderEvaluationNavigation\(result\)/);
const moduleSource = fs.readFileSync(path.join(root, "wwwroot/concept-evaluation.js"), "utf8");
assert.doesNotMatch(moduleSource, /innerHTML|fetch\(|localStorage|JobFit\.evaluate/);
console.log("PASS evaluation metrics, raw-point PR chart, filtering, comparison, collapsed identities/history, escaped evidence and honest fallback");

// Presentation contracts use the real shared theme/component vocabulary.
const styles = fs.readFileSync(path.join(root, "wwwroot/styles.css"), "utf8");
const theme = fs.readFileSync(path.join(root, "wwwroot/theme.css"), "utf8");
const evaluationStyles = styles.slice(styles.indexOf("/* Evaluation presentation composes"));
const hasClass = (n, name) => (n.className || "").split(" ").includes(name);
const metricCards = walk(rendered).find(n => hasClass(n, "evaluation-metrics"));
assert.ok(hasClass(metricCards, "admin-evaluation-metrics"));
assert.equal(metricCards.children.length, 4);
for (const card of metricCards.children) assert.deepEqual(card.children.map(n => n.tag), ["dt", "dd"]);
assert.equal(walk(rendered).filter(n => hasClass(n, "admin-evaluation-card")).length, 3);
assert.ok(walk(rendered).some(n => hasClass(n, "summary-status") && n.textContent === "CURRENT"));
for (const control of walk(rendered).filter(n => ["input", "select"].includes(n.tag))) {
  assert.ok(hasClass(control, "text-control"));
  assert.ok(control.attrs["aria-label"]);
}
for (const t of walk(rendered).filter(n => n.tag === "table")) assert.ok(hasClass(t, "admin-compact-table"));
for (const viewport of walk(rendered).filter(n => hasClass(n, "evaluation-table-scroll"))) {
  assert.ok(hasClass(viewport, "admin-rule-table-viewport"));
  assert.equal(viewport.tabIndex, 0);
  assert.equal(viewport.attrs.role, "region");
  assert.ok(viewport.attrs["aria-label"]);
}
for (const d of walk(rendered).filter(n => n.tag === "details")) assert.ok(hasClass(d, "admin-evaluation-references"));
for (const [name, token] of [["line", "accent"], ["point", "accent"], ["axis", "text-secondary"], ["grid", "border"]]) {
  assert.match(evaluationStyles, new RegExp(`\\.evaluation-pr-${name}\\s*\\{[^}]*var\\(--color-${token}\\)`));
}
assert.match(evaluationStyles, /svg text\s*\{[^}]*fill: var\(--color-text-primary\)/);
assert.match(evaluationStyles, /svg\s*\{[^}]*background: var\(--color-surface-secondary\)/);
assert.match(evaluationStyles, /:focus-visible\s*\{[^}]*var\(--color-focus-ring\)/);
assert.doesNotMatch(evaluationStyles, /#[0-9a-f]{3,8}\b|\b(?:rgb|hsl)a?\(|(?:color|background|fill|stroke):\s*(?:white|black|blue)\b/i);
for (const match of evaluationStyles.matchAll(/var\((--[\w-]+)\)/g)) assert.ok(theme.includes(match[1] + ":"), `Existing token ${match[1]}`);
for (const mode of ["light", "dark", "nord-polar-night", "nord-snow-storm", "dracula"]) assert.ok(theme.includes(`[data-theme="${mode}"]`));
assert.match(evaluationStyles, /max-width: 760px[\s\S]*repeat\(2, minmax\(0, 1fr\)\)/);
assert.equal(walk(rendered).find(n => n.attrs.class === "evaluation-pr-line").attrs.d, "M 50 20 V 20 H 442 V 137.5 H 540");
assert.equal(walk(rendered).filter(n => n.attrs.class === "evaluation-pr-grid").length, 5);
assert.ok(!walk(rendered).some(n => n.attrs.fill || n.attrs.stroke));
console.log("PASS evaluation shared themed cards, controls, tables, badges, disclosures, chart tokens, keyboard focus, all theme structures and unchanged PR geometry");
