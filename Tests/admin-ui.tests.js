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
assert.match(index, /app\.js\?v=40/);

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
assert.match(app, /admin-overview-tab[\s\S]*?Overview[\s\S]*?admin-classifier-tab[\s\S]*?Classifier/,
  "Admin must contain only useful Overview and Classifier subtabs.");
assert.match(app, /Qwen Classifier[\s\S]*?Start background backfill/);
assert.match(app, /fetch\("\/api\/admin\/classifier\/backfill\/status"/);
assert.match(app, /fetch\("\/api\/admin\/classifier\/backfill", \{ method: "POST" \}\)/);
assert.match(app, /result\.current[\s\S]*?result\.total[\s\S]*?result\.pending[\s\S]*?result\.unavailable[\s\S]*?result\.running/,
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
assert.doesNotMatch(program, /api\/admin\/(?:annotations|detector-evaluation)/,
  "Removed annotation and regex-evaluation APIs must not remain reachable.");
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

console.log("All Admin role, bootstrap, and Qwen classifier UI integration tests passed.");
