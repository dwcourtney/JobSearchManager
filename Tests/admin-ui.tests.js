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
const compose = fs.readFileSync(path.join(root, "deploy", "compose.curiosity.yaml"), "utf8");
const styles = fs.readFileSync(path.join(root, "wwwroot", "styles.css"), "utf8");

assert.doesNotMatch(index, /id="admin-tab"/,
  "Anonymous/non-admin markup must not contain an Admin navigation tab.");
assert.match(index,
  /id="administrator-bootstrap-section"[^>]*hidden[\s\S]*?id="administrator-bootstrap-code"[\s\S]*?minlength="8"[\s\S]*?maxlength="8"[\s\S]*?Claim Administrator/);
assert.doesNotMatch(index, /annotation-labeling-ui|detector-evaluation-ui/,
  "Removed experimental Admin assets must not load.");
assert.match(index, /app\.js\?v=44/);

assert.match(app, /synchronizeAdminNavigation\(account\.isAdmin === true\)/);
assert.match(app, /if \(!isAdmin\)[\s\S]*?adminTab\?\.remove\(\)[\s\S]*?return;/,
  "Non-admin state must remove rather than disable the Admin UI.");
assert.match(app,
  /tab\.id = "admin-tab"[\s\S]*?tab\.textContent = "Admin"/,
  "The Admin tab must be created only after server-confirmed role state.");
assert.match(app,
  /account\.authenticated && account\.administratorBootstrapAvailable === true/,
  "Bootstrap UI visibility must require an authenticated account and server availability.");
assert.match(app, /postAccountJson\("\/api\/account\/admin-bootstrap"/);
assert.match(app, /fetch\("\/api\/admin\/status"/,
  "Admin Overview must verify its server authorization endpoint.");
assert.match(app, /admin-overview-tab[\s\S]*?Overview[\s\S]*?admin-classifier-tab[\s\S]*?RegEx Rules[\s\S]*?admin-evaluation-tab[\s\S]*?Evaluation/,
  "Admin must separate operations, RegEx lifecycle, and evaluation evidence.");
assert.match(app, /RegEx Rules[\s\S]*?Reclassify stale cache[\s\S]*?Run Curated Regression Benchmark[\s\S]*?Verify and Apply Current Rule Set/);
assert.doesNotMatch(`${index}\n${app}`, /Deep Analyze with LLM|LLM deep-analysis score|llmJobFit|evaluateLlmJobFit/,
  "LLM execution and arbitration must be absent from the user-facing workflow.");
assert.match(app, /CURATED REGRESSION BENCHMARK[\s\S]*?AI-ADJUDICATED PRODUCTION HOLDOUT|datasetRoles/);
assert.match(app, /Run AI-Adjudicated Holdout Evaluation/);
assert.match(app, /admin-compact-table[\s\S]*?Previous[\s\S]*?Next/,
  "RegEx rules must use a compact paginated table instead of giant cards.");
assert.match(app, /Worst F1[\s\S]*?Lowest support[\s\S]*?Highest disagreement[\s\S]*?Concept name/);
assert.match(app, /Reference labels were generated through prediction-blinded AI review and adjudication\. They are not human-ground-truth labels\./);
assert.match(app, /Not production accuracy|not production accuracy/i);
assert.match(app, /Job Fit TBD/);
assert.match(app, /fetch\("\/api\/admin\/classifier\/backfill\/status"/);
assert.match(app, /fetch\("\/api\/admin\/classifier\/backfill", \{ method: "POST" \}\)/);
assert.match(app, /result\.current[\s\S]*?result\.total[\s\S]*?result\.pending[\s\S]*?result\.running/,
  "Classifier status must expose persisted coverage and bounded worker state.");
assert.doesNotMatch(app, /Training Data|Human Labeling|Machine Labeling|Detector Evaluation|AnnotationLabeling|DetectorEvaluationUi/,
  "Removed experimental Admin workflows must not remain dormant in the application.");
assert.doesNotMatch(app, /admin-(?:accounts|workspaces)-tab/,
  "Unimplemented Accounts and Workspaces placeholders must not be added.");
assert.doesNotMatch(app, /heading\.textContent = "Administration"/,
  "The redundant Administration page heading must stay removed.");

assert.match(program,
  /MapGet\("\/api\/admin\/status"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)/);
assert.match(program,
  /MapGet\("\/api\/admin\/classifier\/backfill\/status"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)/);
assert.match(program,
  /MapPost\("\/api\/admin\/classifier\/backfill"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)[\s\S]*?RequireRateLimiting\("state"\)/);
assert.match(program, /MapPost\("\/api\/admin\/regex-rules\/evaluate"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)/);
assert.match(program, /MapGet\("\/api\/admin\/evaluations"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)/);
assert.match(program, /MapPost\("\/api\/admin\/evaluations\/ai-holdout"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)[\s\S]*?RequireRateLimiting\("state"\)/);
assert.match(program, /MapPost\("\/api\/admin\/regex-rules\/reload"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)/);
assert.doesNotMatch(program, /api\/admin\/annotations/,
  "Removed annotation APIs must not remain reachable.");
assert.match(program,
  /MapPost\("\/api\/account\/admin-bootstrap"[\s\S]*?RequireAuthorization\(\)[\s\S]*?RequireRateLimiting\("admin-bootstrap"\)/);
assert.match(program, /PermitLimit = 5[\s\S]*?Window = TimeSpan\.FromMinutes\(15\)/);
assert.match(accounts, /public List<string> Roles \{ get; set; \} = \[\];/);
assert.match(security, /CryptographicOperations\.FixedTimeEquals/);
assert.match(security, /SHA256\.HashData/);
assert.match(security, /UnixFileMode\.UserRead \| UnixFileMode\.UserWrite/);
assert.match(compose,
  /JOBSEARCHMANAGER_ADMIN_BOOTSTRAP_PATH: \/app\/data\/admin-bootstrap-code/);
assert.doesNotMatch(styles, /annotation-|detector-|training-data-/,
  "Removed experimental Admin styling must not remain.");

console.log("All Admin role, RegEx lifecycle, and evaluation UI integration tests passed.");
