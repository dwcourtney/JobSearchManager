"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const unseen = require("../wwwroot/job-unseen-state.js");

function cardMock() {
  const classes = new Set(["job-card"]);
  return {
    classes,
    classList: {
      toggle(name, enabled) {
        if (enabled) classes.add(name);
        else classes.delete(name);
      }
    }
  };
}

const unseenIds = new Set(["company:REQ-NEW"]);
const card = cardMock();
unseen.applyToCard(card, unseenIds, "company:REQ-NEW");
assert.equal(card.classes.has("unseen"), true);

assert.equal(unseen.markViewed(unseenIds, "company:REQ-NEW"), true);
unseen.applyToCard(card, unseenIds, "company:REQ-NEW");
assert.equal(card.classes.has("unseen"), false);
assert.equal(unseen.markViewed(unseenIds, "company:REQ-NEW"), false);

unseen.restoreUnseen(unseenIds, "company:REQ-NEW");
unseen.applyToCard(card, unseenIds, "company:REQ-NEW");
assert.equal(card.classes.has("unseen"), true);

const root = path.resolve(__dirname, "..");
const app = fs.readFileSync(path.join(root, "wwwroot", "app.js"), "utf8");
const index = fs.readFileSync(path.join(root, "wwwroot", "index.html"), "utf8");
const styles = fs.readFileSync(path.join(root, "wwwroot", "styles.css"), "utf8");
const theme = fs.readFileSync(path.join(root, "wwwroot", "theme.css"), "utf8");

assert.match(app, /JobUnseenState\.applyToCard\(card, state\.newJobIds, job\.stableId\)/);
assert.match(app, /JobUnseenState\.markViewed\(state\.newJobIds, job\.stableId\)/);
assert.match(app, /JobUnseenState\.restoreUnseen\(state\.newJobIds, job\.stableId\)/);
assert.match(app,
  /JobUnseenState\.isUnseen\(state\.newJobIds, job\.stableId\)[\s\S]*?unseenIndicator\.textContent = "NEW"[\s\S]*?dateIndicators\.append\(unseenIndicator\)/);
assert.doesNotMatch(index, /detail-new-badge|class="new-badge"[^>]*>NEW/);
assert.doesNotMatch(styles, /\.new-badge\s*{/);
assert.match(app, /button\.append\(dateColumn, title, location, requisition, pay\)/);
assert.doesNotMatch(app, /badges\.append\((?:unseenIndicator|newBadge)\)/);
assert.match(styles,
  /\.job-date-column\s*{[\s\S]*?grid-template-rows:\s*auto minmax\(1\.35rem, auto\)/);
assert.match(styles,
  /\.job-card-main \.job-date-new-indicator\s*{[\s\S]*?--color-new-badge-background/);

const unseenRule = styles.indexOf(".job-card.unseen:not(.dismissed)");
const selectedRule = styles.indexOf(".job-card.selected");
assert.ok(unseenRule >= 0 && selectedRule > unseenRule,
  "Selected styling must follow and override the subtler unseen styling.");
const unseenStyle = styles.slice(unseenRule, styles.indexOf(".job-card.selected", unseenRule));
assert.match(unseenStyle, /--unseen-job-indicator-width[\s\S]*?--color-job-selected-indicator/);
assert.doesNotMatch(unseenStyle, /background:/,
  "Unseen jobs must not reuse the hover background as their persistent state.");
assert.match(styles, /\.job-card\.selected[\s\S]*?--shadow-job-selected[\s\S]*?--color-job-selected/);
assert.match(styles,
  /\.job-card\.unseen\.selected:not\(\.dismissed\)[\s\S]*?--shadow-job-selected[\s\S]*?--color-job-selected/);
assert.match(index, /job-unseen-state\.js\?v=1/);
assert.match(index, /theme\.css\?v=10/);
assert.match(index, /styles\.css\?v=27/);
assert.match(theme, /--unseen-job-indicator-width:\s*2px/);
assert.match(theme, /--selected-job-outline-width:\s*2px/);
assert.match(theme,
  /--shadow-job-selected:[\s\S]*?--selected-job-indicator-width[\s\S]*?--selected-job-outline-width/);

console.log("All deterministic unseen-job state and rendering tests passed.");
