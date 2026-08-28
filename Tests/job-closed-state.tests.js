"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "..");
const app = fs.readFileSync(path.join(root, "wwwroot", "app.js"), "utf8");
const index = fs.readFileSync(path.join(root, "wwwroot", "index.html"), "utf8");
const styles = fs.readFileSync(path.join(root, "wwwroot", "styles.css"), "utf8");
const theme = fs.readFileSync(path.join(root, "wwwroot", "theme.css"), "utf8");

assert.match(index, /All Jobs[\s\S]*Saved[\s\S]*Applied[\s\S]*Closed[\s\S]*Hidden/);
assert.match(index, /id="close-application-overlay"[\s\S]*class="loading-overlay confirmation-overlay"/);
for (const reason of [
  "PositionWithdrawn", "NotSelected", "ScreenedOut", "InterviewedOut",
  "Ghosted", "Withdrew", "Other"
]) {
  assert.match(index, new RegExp(`<option value="${reason}">`));
  assert.match(app, new RegExp(`${reason}:`));
}
assert.match(app, /if \(isApplied\)[\s\S]*?job-close-application-button[\s\S]*?showCloseApplicationModal/);
assert.match(app, /else if \(isClosed\)[\s\S]*?job-reopen-application-button[\s\S]*?reopenApplication/);
assert.match(app, /setJobWorkflowState\([\s\S]*?JobWorkflowState\.STATES\.closed[\s\S]*?reason/);
assert.match(app, /JSON\.stringify\(\{ stableId: job\.stableId, state: nextState, closeReason \}\)/);
assert.match(app, /appendJobBadge\([\s\S]*?badges,[\s\S]*?"close-reason-badge"/);
assert.match(app, /if \(!isApplied && !isClosed && !isHidden\)/);
assert.match(styles, /\.job-close-application-button[\s\S]*?var\(--color-closed-action/);
assert.match(styles, /\.job-reopen-application-button[\s\S]*?var\(--color-reopen-action/);
assert.match(styles, /\.close-reason-badge[\s\S]*?var\(--color-close-reason-badge/);
assert.match(theme, /--color-closed-action:[^;]*var\(/);
assert.match(theme, /--color-close-reason-badge-background:[^;]*var\(/);
assert.doesNotMatch(styles, /(?:job-close-application|job-reopen-application|closed-badge|close-reason-badge)[^{]*\{[^}]*(?:#[0-9a-f]{3,8}|rgb\()/i);

console.log("All deterministic Closed-state UI integration tests passed.");
