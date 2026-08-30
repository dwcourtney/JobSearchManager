"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const root = path.resolve(__dirname, "..");
const index = fs.readFileSync(path.join(root, "wwwroot", "index.html"), "utf8");
const app = fs.readFileSync(path.join(root, "wwwroot", "app.js"), "utf8");
const program = fs.readFileSync(path.join(root, "Program.cs"), "utf8");
const accounts = fs.readFileSync(path.join(root, "AccountSecurity.cs"), "utf8");
const security = fs.readFileSync(path.join(root, "AdministratorSecurity.cs"), "utf8");
const evaluation = fs.readFileSync(path.join(root, "DetectorEvaluation.cs"), "utf8");
const fixtures = JSON.parse(fs.readFileSync(path.join(root, "DetectorEvaluationFixtures.json"), "utf8"));
const compose = fs.readFileSync(path.join(root, "deploy", "compose.curiosity.yaml"), "utf8");

assert.doesNotMatch(index, /id="admin-tab"/,
  "Anonymous/non-admin markup must not contain an Admin navigation tab.");
assert.match(index,
  /id="administrator-bootstrap-section"[^>]*hidden[\s\S]*?id="administrator-bootstrap-code"[\s\S]*?minlength="8"[\s\S]*?maxlength="8"[\s\S]*?Claim Administrator/);
assert.doesNotMatch(index, /\/app\/data|sha-?256|token hash/i,
  "The bootstrap UI must not expose server paths or validation internals.");

assert.match(app, /synchronizeAdminNavigation\(account\.isAdmin === true\)/);
assert.match(app, /if \(!isAdmin\)[\s\S]*?adminTab\?\.remove\(\)[\s\S]*?return;/,
  "Non-admin state must remove rather than disable the Admin UI.");
assert.match(app,
  /document\.createElement\("button"\)[\s\S]*?tab\.id = "admin-tab"[\s\S]*?tab\.textContent = "Admin"/,
  "The Admin tab must be created only after server-confirmed role state.");
assert.match(app,
  /account\.authenticated && account\.administratorBootstrapAvailable === true/,
  "Bootstrap UI visibility must require an authenticated account and server availability.");
assert.match(app, /postAccountJson\("\/api\/account\/admin-bootstrap"/);
assert.match(app, /fetch\("\/api\/admin\/status"/,
  "Admin Overview must verify its reusable server authorization endpoint.");
assert.match(app, /admin-overview-tab[\s\S]*?Overview[\s\S]*?admin-detector-evaluation-tab[\s\S]*?Detector Evaluation/,
  "Admin must expose only Overview and Detector Evaluation subtabs.");
assert.doesNotMatch(app, /admin-(?:accounts|workspaces)-tab/,
  "Unimplemented Accounts and Workspaces placeholders must not be added.");
assert.match(app, /fetch\("\/api\/admin\/detector-evaluation"/);
assert.match(app, /Small labeled sample/,
  "Low-support metrics must be visually qualified.");
assert.match(app, /False positives[\s\S]*?False negatives/,
  "Detector errors must be reviewable by classification.");

assert.match(program,
  /MapGet\("\/api\/admin\/status"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)/);
assert.match(program,
  /MapGet\("\/api\/admin\/detector-evaluation"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)/,
  "Detector Evaluation must retain server-side admin authorization.");
assert.match(program,
  /MapPost\("\/api\/account\/admin-bootstrap"[\s\S]*?RequireAuthorization\(\)[\s\S]*?RequireRateLimiting\("admin-bootstrap"\)/);
assert.match(program, /PermitLimit = 5[\s\S]*?Window = TimeSpan\.FromMinutes\(15\)/);
assert.match(accounts, /public List<string> Roles \{ get; set; \} = \[\];/);
assert.match(security, /CryptographicOperations\.FixedTimeEquals/);
assert.match(security, /SHA256\.HashData/);
assert.match(security, /UnixFileMode\.UserRead \| UnixFileMode\.UserWrite/);
assert.match(compose,
  /JOBSEARCHMANAGER_ADMIN_BOOTSTRAP_PATH: \/app\/data\/admin-bootstrap-code/);
assert.equal(fixtures.fixtures.length, 36);
assert.equal(new Set(fixtures.fixtures.map(item => item.id)).size, fixtures.fixtures.length,
  "Evaluation fixture IDs must be stable and unique.");
assert.ok(fixtures.fixtures.every(item => typeof item.expectedPresent === "boolean"),
  "Ground-truth labels must be explicit fixture values.");
assert.match(evaluation, /Fixture\.ExpectedPresent/,
  "Expected labels must be read independently from fixture data.");
assert.match(evaluation, /_detector\.Analyze/,
  "Evaluation predictions must use the production JobConceptDetector.");
assert.doesNotMatch(evaluation, /double\.NaN/,
  "Undefined metrics must not emit NaN.");

console.log("All Admin role, bootstrap, and detector-evaluation UI integration tests passed.");
