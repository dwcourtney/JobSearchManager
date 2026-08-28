"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const index = fs.readFileSync(path.join(root, "wwwroot", "index.html"), "utf8");
const app = fs.readFileSync(path.join(root, "wwwroot", "app.js"), "utf8");
const styles = fs.readFileSync(path.join(root, "wwwroot", "styles.css"), "utf8");

assert.match(index, /id="job-fit-settings-tab"[\s\S]*?>\s*Job Fit\s*</);
assert.match(index, /id="job-fit-settings-panel"/);
assert.match(index, /id="job-fit-enabled"/);
assert.match(index, /id="job-fit-concept-select"/,
  "Job Fit must select from canonical concepts.");
assert.match(index, /id="job-fit-category-filter"/,
  "The Add Signal control must filter the expanded corpus by category.");
assert.doesNotMatch(index, /id="job-fit-(?:keyword|phrase|custom-concept)"/,
  "Job Fit must not expose arbitrary concept entry.");
assert.match(app, /fetch\("\/api\/job-fit\/concepts"/);
assert.match(app, /createElement\("details"\)[\s\S]*?job-fit-category-section/,
  "Configured Job Fit signals must render in collapsible category sections.");
assert.match(app, /state\.jobFitCategoryFilter === "all" \|\| concept\.category/);
assert.match(app, /if \(jobFit\) \{[\s\S]*?Job Fit \$\{jobFit\.score\}\/10/,
  "The badge must be conditional on an enabled Job Fit assessment.");

const badgeStart = styles.indexOf(".job-card .job-fit-badge.score-low");
const badgeEnd = styles.indexOf(".job-card .analysis-pending-badge", badgeStart);
const badgeStyles = styles.slice(badgeStart, badgeEnd);
assert.ok(badgeStart >= 0 && badgeStyles.includes("var(--color-fit-blocker-border)"));
assert.doesNotMatch(badgeStyles, /#[0-9a-f]{3,8}|rgba?\(/i,
  "Job Fit badge styles must use theme tokens rather than raw colors.");
const categoryStart = styles.indexOf(".job-fit-category-section");
const categoryEnd = styles.indexOf(".settings-save-note", categoryStart);
const categoryStyles = styles.slice(categoryStart, categoryEnd);
assert.ok(categoryStart >= 0 && categoryStyles.includes("var(--color-accordion-header-background)"));
assert.doesNotMatch(categoryStyles, /#[0-9a-f]{3,8}|rgba?\(/i,
  "Job Fit category sections must use existing theme tokens.");

console.log("All Job Fit UI integration tests passed.");
