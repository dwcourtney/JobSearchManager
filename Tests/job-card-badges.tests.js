"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "..");
const app = fs.readFileSync(path.join(root, "wwwroot", "app.js"), "utf8");
const index = fs.readFileSync(path.join(root, "wwwroot", "index.html"), "utf8");
const styles = fs.readFileSync(path.join(root, "wwwroot", "styles.css"), "utf8");

assert.match(app, /badges\.className = "job-badges"/);
assert.match(app,
  /dateIndicators\.className = "job-date-indicators"[\s\S]*?saved-badge job-date-state-indicator[\s\S]*?applied-badge job-date-state-indicator[\s\S]*?closed-badge job-date-state-indicator[\s\S]*?hidden-badge job-date-state-indicator[\s\S]*?dateColumn\.append\(date, dateIndicators\)/);
assert.doesNotMatch(app,
  /badges\.append\((?:savedBadge|appliedBadge|closedBadge|hiddenBadge)\)/);
assert.match(app, /appendJobBadge\(badges, "restriction-badge", "⚠ Location restricted"\)/);
assert.match(app, /appendJobBadge\([\s\S]*?badges,[\s\S]*?`remote-work-badge/);
assert.match(app, /appendJobBadge\([\s\S]*?badges,[\s\S]*?`extended-location-badge/);
assert.match(app, /EducationFit\.jobCardBadge\(academicQualification, educationStatus\)/);
assert.match(app, /ClearanceFit\.jobCardBadges\(job, clearanceStatus\)/);
assert.match(app, /ClearanceFit\.workAuthorizationBadges\(job\.workAuthorization, workAuthorizationStatus\)/);
assert.doesNotMatch(app, /academicBadgeLabel\(/);
assert.match(app, /function appendJobBadge\([\s\S]*?document\.createElement\("span"\)[\s\S]*?container\.append\(badge\)/);
assert.doesNotMatch(app, /button\.append\((?:deployment|extendedLocation)/);
assert.match(styles, /\.job-card \.extended-location-badge[\s\S]*?padding: \.14rem \.4rem/);
assert.match(styles, /\.extended-location-badge\.strong[\s\S]*?--color-deployment-strong/);
assert.match(styles, /\.extended-location-badge\.questionable[\s\S]*?--color-deployment-question/);
assert.match(styles,
  /\.job-card-main \.job-date-indicators\s*\{[\s\S]*?display:\s*grid/);
assert.match(index, /education-fit\.js\?v=2/);
assert.match(index, /credential-fit\.js\?v=2/);
assert.match(index, /clearance-fit\.js\?v=1/);
assert.match(index, /job-unseen-state\.js\?v=1/);
assert.match(index, /job-fit\.js\?v=10/);
assert.match(index, /app\.js\?v=33/);
assert.doesNotMatch(app, /badges\.append\((?:unseenIndicator|newBadge)\)/);

console.log("All deterministic job-card badge-path tests passed.");
