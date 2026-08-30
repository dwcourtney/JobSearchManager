"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.resolve(__dirname, "..");
const index = fs.readFileSync(path.join(root, "wwwroot", "index.html"), "utf8");
const app = fs.readFileSync(path.join(root, "wwwroot", "app.js"), "utf8");
const styles = fs.readFileSync(path.join(root, "wwwroot", "styles.css"), "utf8");
const theme = fs.readFileSync(path.join(root, "wwwroot", "theme.css"), "utf8");

const qualificationsStart = index.indexOf('id="qualifications-settings-panel"');
const preferencesStart = index.indexOf('id="preferences-settings-panel"');
const jobFitStart = index.indexOf('id="job-fit-settings-panel"');
const accountStart = index.indexOf('id="account-settings-panel"');
const qualifications = index.slice(qualificationsStart, preferencesStart);
const preferences = index.slice(preferencesStart, jobFitStart);
const jobFit = index.slice(jobFitStart, accountStart);

assert.match(qualifications, /id="qualification-basics-tab"[\s\S]*?>\s*Basics\s*</);
assert.match(qualifications, /id="qualification-credentials-tab"[\s\S]*?Certifications &amp; Licenses/);
assert.match(qualifications, /id="credential-inventory-status"/);
assert.match(qualifications, /id="credential-search"[^>]*type="search"/);
assert.match(qualifications, /id="held-credentials"[^>]*credential-inventory-list/);
assert.match(qualifications,
  /id="credential-selection-summary"[^>]*selected-location-summary[^>]*aria-live="polite"/);
assert.doesNotMatch(qualifications, /id="minimum-pay"/);
assert.match(preferences, /id="compensation-heading"[\s\S]*?id="minimum-pay"/);
assert.doesNotMatch(preferences, /exclude-strong-extended-location-requirements/);
assert.match(jobFit,
  /id="work-arrangement-filtering-heading"[\s\S]*?id="exclude-strong-extended-location-requirements"/);
assert.match(jobFit, /extended away-from-home assignments\. Ordinary business travel is not excluded\./);
assert.doesNotMatch(preferences, /id="import-export-heading"|id="import-workspace-button"|id="export-workspace-button"/);
assert.doesNotMatch(preferences, /id="reset-workspace-heading"|id="reset-workspace-button"|id="workspace-id"/);
assert.match(app, /fetch\("\/api\/workspace\/identity", \{ cache: "no-store" \}\)/);
assert.match(app, /async function copyWorkspaceId\(\)/);
assert.ok(app.includes("navigator.clipboard?.writeText"));
assert.ok(app.includes("navigator.clipboard.writeText(workspaceId)"));
assert.ok(app.includes('elements.workspaceIdStatus.textContent = "Workspace ID copied."'));
assert.match(styles,
  /\.workspace-identity input\s*\{[\s\S]*?var\(--color-border-strong\)[\s\S]*?var\(--color-input-background\)/);

const education = qualifications.slice(
  qualifications.indexOf('id="education-heading"'),
  qualifications.indexOf('id="citizenship-sponsorship-heading"'));
const authorization = qualifications.slice(
  qualifications.indexOf('id="citizenship-sponsorship-heading"'),
  qualifications.indexOf('id="security-clearance-heading"'));
const clearance = qualifications.slice(
  qualifications.indexOf('id="security-clearance-heading"'),
  qualifications.indexOf('id="qualification-credentials-panel"'));
assert.match(education, /id="education-level"[\s\S]*?id="hide-strict-education-mismatch"/);
assert.match(authorization,
  /id="us-work-authorization-status"[\s\S]*?id="sponsorship-profile"[\s\S]*?id="hide-strict-work-authorization-mismatch"/);
assert.match(clearance,
  /id="clearance-profile-level"[\s\S]*?id="public-trust-profile"[\s\S]*?id="hide-strict-clearance-mismatch"/);
assert.doesNotMatch(qualifications, /Screening Rules|id="screening-heading"/);

assert.match(index,
  /id="source-confirmation-stay"[\s\S]*?id="source-confirmation-discard"[\s\S]*?id="source-confirmation-apply"/);
assert.match(index, /Discard Changes and go to Jobs/);
assert.match(styles, /\.confirmation-actions\s*{[\s\S]*?flex-wrap:\s*nowrap/);
assert.match(styles, /@media \(max-width: 560px\)[\s\S]*?\.confirmation-actions\s*{[\s\S]*?flex-direction:\s*column/);
assert.match(theme, /--confirmation-panel-width:\s*44rem/);

assert.match(app, /function populateCredentialInventory\(/);
assert.match(app,
  /function updateCredentialSelectionSummary\([\s\S]*?credentialOptions[\s\S]*?heldCredentialIds\.has\(credential\.id\)/);
assert.match(app,
  /prefix\.textContent = `Selected \(\$\{selected\.length\}\):`/);
assert.match(app,
  /chip\.className = "chip selected-credential-chip"[\s\S]*?credential\.name/);
assert.match(app,
  /function removeHeldCredential\(credentialId\)[\s\S]*?heldCredentialIds\.delete\(credentialId\)[\s\S]*?updateCredentialSettingsUi\(\)[\s\S]*?queueSettingsSave\(\)/);
assert.match(styles,
  /\.selected-credential-chip\s*\{[\s\S]*?--color-credential-badge-background/);
assert.match(app, /document\.createElement\("details"\)/);
assert.match(app, /document\.createElement\("input"\)[\s\S]*?checkbox\.type = "checkbox"/);
assert.match(app, /CredentialFit\.jobCardBadges\(credentialFit\)/);
assert.doesNotMatch(app, /credentials\.slice\([^)]*\)\.forEach/);
assert.match(app, /CredentialFit\.assessmentCredentials\(item\)/);

console.log("All deterministic qualification-settings UI tests passed.");
