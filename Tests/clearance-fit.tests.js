"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const fit = require("../wwwroot/clearance-fit.js");

const strictJob = level => ({
  clearanceLevel: level,
  clearanceRequirement: "activeRequired",
  clearanceParseStatus: "parsed",
  polygraphRequired: false
});
const profile = (clearanceLevel, publicTrust = "unknown") => ({ clearanceLevel, publicTrust });

let status = fit.evaluate(strictJob("topSecretSCI"), profile("topSecretSCI"));
assert.equal(status.kind, "meets");
assert.deepEqual(fit.jobCardBadges(strictJob("topSecretSCI"), status), []);

status = fit.evaluate(strictJob("secret"), profile("topSecretSCI"));
assert.equal(status.kind, "meets");
assert.deepEqual(fit.jobCardBadges(strictJob("secret"), status), []);

status = fit.evaluate(strictJob("topSecretSCI"), profile("secret"));
assert.equal(status.kind, "strictMismatch");
assert.equal(fit.jobCardBadges(strictJob("topSecretSCI"), status)[0].text, "TS/SCI required");

status = fit.evaluate(strictJob("secret"), profile("none"));
assert.equal(status.kind, "strictMismatch");
assert.equal(fit.jobCardBadges(strictJob("secret"), status)[0].text, "Secret required");

const obtainable = { ...strictJob("secret"), clearanceRequirement: "obtain" };
status = fit.evaluate(obtainable, profile("none"));
assert.equal(status.kind, "notStrict");
assert.equal(status.hide, false);
assert.deepEqual(fit.jobCardBadges(obtainable, status), []);

const preferred = { ...strictJob("secret"), clearanceRequirement: "preferred" };
status = fit.evaluate(preferred, profile("none"));
assert.equal(status.kind, "preferredOnly");
assert.deepEqual(fit.jobCardBadges(preferred, status), []);

status = fit.evaluate(strictJob("secret"), profile("topSecretSCI"));
assert.equal(status.userLabel, "TS/SCI");
assert.match(status.summary, /Meets strict current-clearance requirement/);
assert.match(status.explanation, /meets or exceeds/);

const publicTrustJob = strictJob("publicTrust");
const publicTrustMissing = fit.evaluate(publicTrustJob, profile("topSecretSCI", "none"));
assert.equal(publicTrustMissing.kind, "strictMismatch");
const publicTrustHeld = fit.evaluate(publicTrustJob, profile("none", "current"));
assert.equal(publicTrustHeld.kind, "meets");
assert.deepEqual(fit.jobCardBadges(publicTrustJob, publicTrustHeld), []);

assert.deepEqual(fit.workAuthorizationBadges(
  { eligibility: "usCitizen", strength: "strict" },
  { kind: "meets", explanation: "U.S. citizenship requirement is satisfied." }), []);

status = fit.evaluate(strictJob("topSecret"), profile("secret"));
assert.equal(status.hide, true, "Strict-clearance filtering must continue to hide a confirmed mismatch.");

const root = path.resolve(__dirname, "..");
const app = fs.readFileSync(path.join(root, "wwwroot", "app.js"), "utf8");
assert.match(app, /ClearanceFit\.evaluate\(job, profile\)/);
assert.match(app, /detailClearanceComparison\.textContent = hasClearance[\s\S]*?clearanceStatus\.summary/);
assert.match(app, /detailClearanceNoteText\.textContent = hasClearanceEvidence/);
assert.match(app, /ClearanceFit\.jobCardBadges\(job, clearanceStatus\)/);
assert.match(app, /ClearanceFit\.workAuthorizationBadges\(job\.workAuthorization, workAuthorizationStatus\)/);
assert.doesNotMatch(app, /function clearanceBadgeLabel/);
assert.doesNotMatch(app, /function workAuthorizationBadgeLabel/);

console.log("All deterministic clearance-fit and compact-badge tests passed.");
