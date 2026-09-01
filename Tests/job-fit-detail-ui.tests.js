"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const index = fs.readFileSync(path.join(root, "wwwroot", "index.html"), "utf8");
const app = fs.readFileSync(path.join(root, "wwwroot", "app.js"), "utf8");
const styles = fs.readFileSync(path.join(root, "wwwroot", "styles.css"), "utf8");

const glance = index.indexOf('id="at-a-glance-tab"');
const jobFit = index.indexOf('id="job-fit-detail-tab"');
const posting = index.indexOf('id="full-posting-tab"');
assert.ok(glance >= 0 && jobFit > glance && posting > jobFit,
  "Detail tabs must render as At a Glance, Job Fit, Full Posting.");
assert.match(index, /id="job-fit-detail-tab"[\s\S]*?aria-controls="job-fit-detail-panel"[\s\S]*?hidden/);
assert.match(index, /id="job-fit-detail-panel"[\s\S]*?role="tabpanel"[\s\S]*?aria-labelledby="job-fit-detail-tab"/);
assert.match(index, /id="at-a-glance-panel"/);
assert.match(index, /id="full-posting-panel"/);

assert.match(app, /function evaluateJobFit\(job\)[\s\S]*?JobFit\.evaluate/,
  "The card and detail view must share the authoritative Job Fit evaluator.");
assert.match(app,
  /const defaultJobFit = evaluateJobFit\(job\);[\s\S]*?const llmJobFit = evaluateLlmJobFit\(job\);[\s\S]*?const jobFit = llmJobFit \|\| defaultJobFit;[\s\S]*?Job Fit \$\{jobFit\.score\}\/10/,
  "The card badge must use the current optional LLM score or the shared default Job Fit score.");
assert.match(app, /renderJobFitDetail\(evaluateJobFit\(job\), job\)/,
  "The detail tab must render the same shared Job Fit result.");
assert.match(app, /score\.textContent = `\$\{result\.score\} \/ 10`/);
assert.match(app, /dimension\.rawImpact[\s\S]*?dimension\.impact[\s\S]*?dimension\.capped/,
  "Raw and bounded category contributions must be rendered from engine output.");
assert.match(app, /result\.hardConflictCap\.applied[\s\S]*?result\.scoreBeforeHardConflictCap/,
  "Hard-conflict cap state must be rendered from engine output.");
assert.match(app, /Superseded — not counted[\s\S]*?dimension\.supersededSignals/);
assert.match(app, /Detected but Neutral[\s\S]*?dimension\.neutralSignals/);
assert.match(app, /text\.textContent = `“\$\{signal\.travelComparison\?\.sourceEvidence \|\| signal\.locationComparison\?\.sourceEvidence \|\| signal\.evidence\}”`/,
  "Evidence shown in the detail tab must come from the detected signal.");
assert.match(app, /signal\.travelComparison[\s\S]*?"Detected travel requirement"[\s\S]*?"Detected travel level"[\s\S]*?"Your maximum travel tolerance"[\s\S]*?"Result"/,
  "Travel explanations must show the detected requirement, level, configured maximum, and comparison result.");
assert.match(app, /signal\.locationComparison[\s\S]*?"Detected normal work location"[\s\S]*?"Your preferred normal work location"[\s\S]*?"Distance"[\s\S]*?"Work Arrangement impact"/,
  "Normal-location explanations must expose detected level, preference, distance, and bounded contribution input.");
assert.match(app, /elements\.jobFitDetailTab\.hidden = !result/,
  "The Job Fit tab must be hidden when scoring is disabled.");
assert.match(app, /const tabs = elements\.jobFitDetailTab\.hidden[\s\S]*?\["glance", "fit", "posting"\]/,
  "Keyboard navigation must retain all three detail tabs when Job Fit is enabled.");

const detailStylesStart = styles.indexOf(".job-fit-detail-panel");
const detailStylesEnd = styles.indexOf(".summary-dossier", detailStylesStart);
const detailStyles = styles.slice(detailStylesStart, detailStylesEnd);
assert.ok(detailStylesStart >= 0 && detailStyles.includes("var(--color-glance-background)"));
assert.doesNotMatch(detailStyles, /#[0-9a-f]{3,8}|rgba?\(/i,
  "Job Fit detail styling must use theme tokens rather than raw colors.");

console.log("All Job Fit detail-tab UI integration tests passed.");
