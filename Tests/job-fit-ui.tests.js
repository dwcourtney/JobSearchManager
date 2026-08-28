"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const index = fs.readFileSync(path.join(root, "wwwroot", "index.html"), "utf8");
const app = fs.readFileSync(path.join(root, "wwwroot", "app.js"), "utf8");
const styles = fs.readFileSync(path.join(root, "wwwroot", "styles.css"), "utf8");
const catalog = JSON.parse(fs.readFileSync(path.join(root, "JobConceptCatalog.json"), "utf8"));

assert.match(index, /id="job-fit-settings-tab"[\s\S]*?>\s*Job Fit\s*</);
assert.match(index, /src="\/job-fit\.js\?v=3"/,
  "The revised Job Fit runtime must use a new cache-busting asset version.");
assert.match(index, /id="job-fit-settings-panel"/);
assert.match(index, /id="job-fit-enabled"/);
assert.match(index, /id="job-fit-concept-search"/);
assert.match(index, /id="job-fit-survey"/,
  "Job Fit must expose the canonical-concept survey.");
assert.match(index, /id="job-fit-survey-status"[^>]*aria-live="polite"/);
assert.doesNotMatch(index, /id="job-fit-(?:category-filter|concept-select|preference-select|add-signal|signal-list)"/,
  "The former add/configure/remove workflow must not remain in the survey UI.");
assert.doesNotMatch(index, />\s*(?:Add Signal|Remove)\s*</,
  "The survey must not expose Add Signal or Remove actions.");
assert.doesNotMatch(index, /id="job-fit-(?:keyword|phrase|custom-concept)"/,
  "Job Fit must not expose arbitrary concept entry.");
assert.match(app, /fetch\("\/api\/job-fit\/concepts"/);
assert.match(app, /createElement\("details"\)[\s\S]*?job-fit-survey-category/,
  "All Job Fit concepts must render in collapsible category sections.");
assert.match(app, /row\.setAttribute\("role", "radiogroup"\)/);
assert.match(app, /radio\.name = `job-fit-\$\{concept\.id\}`/,
  "Every concept must have an independent radio group.");
assert.match(app, /radio\.checked = \(configured\.get\(concept\.id\) \|\| "neutral"\) === value/,
  "Absence from sparse configuration must render as Neutral.");
assert.match(app, /if \(radio\.value !== "neutral"\)/,
  "Returning a concept to Neutral must omit it from the sparse settings array.");
assert.match(app, /\["negative", "NEG"\][\s\S]*?\["ideal", "I"\]/,
  "The survey must use the balanced Hard Conflict, Negative, Neutral, Positive, Ideal scale.");
assert.doesNotMatch(app, /\["strong(?:Negative|Positive)"/,
  "Legacy preference names must not remain as survey columns.");
assert.deepEqual([...new Set(catalog.concepts.map(concept => concept.category))].sort(), [
  "Responsibility Shape",
  "Role Type / Career Direction",
  "Technical Domain",
  "Work Arrangement",
  "Work Environment"
]);
assert.match(app, /const categoryOrder = Object\.keys\(JobFit\.dimensionLimits\)/,
  "The survey category order must reuse the scoring engine's canonical dimensions.");
assert.equal(catalog.concepts.length, 76, "The complete canonical catalog must remain available.");
assert.match(app, /if \(jobFit\) \{[\s\S]*?Job Fit \$\{jobFit\.score\}\/10/,
  "The badge must be conditional on an enabled Job Fit assessment.");

const badgeStart = styles.indexOf(".job-card .job-fit-badge.score-low");
const badgeEnd = styles.indexOf(".job-card .analysis-pending-badge", badgeStart);
const badgeStyles = styles.slice(badgeStart, badgeEnd);
assert.ok(badgeStart >= 0 && badgeStyles.includes("var(--color-fit-blocker-border)"));
assert.doesNotMatch(badgeStyles, /#[0-9a-f]{3,8}|rgba?\(/i,
  "Job Fit badge styles must use theme tokens rather than raw colors.");
const categoryStart = styles.indexOf(".job-fit-survey-category");
const categoryEnd = styles.indexOf(".settings-save-note", categoryStart);
const categoryStyles = styles.slice(categoryStart, categoryEnd);
assert.ok(categoryStart >= 0 && categoryStyles.includes("var(--color-accordion-header-background)"));
assert.doesNotMatch(categoryStyles, /#[0-9a-f]{3,8}|rgba?\(/i,
  "Job Fit category sections must use existing theme tokens.");
assert.match(styles, /@media \(max-width: 560px\)[\s\S]*?\.job-fit-survey-row/,
  "The radio matrix must include a compact small-screen layout.");

console.log("All Job Fit UI integration tests passed.");
