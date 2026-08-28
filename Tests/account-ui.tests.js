"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const index = fs.readFileSync(path.join(root, "wwwroot", "index.html"), "utf8");
const app = fs.readFileSync(path.join(root, "wwwroot", "app.js"), "utf8");
const styles = fs.readFileSync(path.join(root, "wwwroot", "styles.css"), "utf8");

assert.match(index, /id="account-settings-tab"[\s\S]*?aria-controls="account-settings-panel"/);
assert.match(index, /You are using an anonymous workspace/);
assert.match(index, /Creating an account claims this exact workspace/);
assert.match(index, /id="create-account-email"[\s\S]*?type="email"[\s\S]*?maxlength="254"/);
assert.match(index, /id="create-account-password"[\s\S]*?autocomplete="new-password"[\s\S]*?minlength="12"[\s\S]*?maxlength="128"/);
assert.match(index, /This browser session only[\s\S]*?1 day[\s\S]*?7 days[\s\S]*?14 days[\s\S]*?30 days[\s\S]*?Keep me signed in/);
assert.match(index, /Email is used only for sign-in, verification, and password recovery/);
assert.doesNotMatch(index, /password hash|reset token|session token/i,
  "The Account UI must not expose authentication secret material.");

assert.match(app, /fetch\("\/api\/account\/status"/);
assert.match(app, /postAccountJson\("\/api\/account\/create"/);
assert.match(app, /postAccountJson\("\/api\/account\/login"/);
assert.match(app, /postAccountJson\("\/api\/account\/logout"/);
assert.match(app, /postAccountJson\("\/api\/account\/forgot-password"/);
assert.match(app, /postAccountJson\("\/api\/account\/reset-password"/);
assert.match(app, /postAccountJson\("\/api\/account\/change-password"/);
assert.match(app, /postAccountJson\("\/api\/account\/session"/);
assert.match(app, /window\.location\.hash[\s\S]*?URLSearchParams/,
  "Email tokens must be handled from URL fragments rather than server-visible query strings.");
assert.match(app, /copyWorkspaceIdButton\.disabled = authenticated/,
  "Authenticated workspaces must not expose a copyable anonymous workspace credential.");
assert.match(app, /\["job-search", "qualifications", "preferences", "job-fit", "account"\]/,
  "Keyboard navigation must include the Account settings tab.");

const accountStylesStart = styles.indexOf(".account-section");
const accountStylesEnd = styles.indexOf(".settings-section", accountStylesStart);
const accountStyles = styles.slice(accountStylesStart, accountStylesEnd);
assert.ok(accountStylesStart >= 0 && accountStyles.includes("var(--color-text-secondary)"));
assert.doesNotMatch(accountStyles, /#[0-9a-f]{3,8}|rgba?\(/i,
  "Account styling must use semantic theme tokens rather than raw colors.");

console.log("All optional Account UI integration tests passed.");
