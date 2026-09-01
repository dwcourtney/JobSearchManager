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
const styles = fs.readFileSync(path.join(root, "wwwroot", "styles.css"), "utf8");

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
  "Admin must preserve Overview and Detector Evaluation subtabs.");
assert.match(app, /admin-human-labeling-tab[\s\S]*?Human Labeling[\s\S]*?admin-machine-labeling-tab[\s\S]*?Machine Labeling/,
  "Admin must expose distinct human and machine labeling tabs only after server-confirmed role state.");
assert.match(index, /annotation-labeling-ui\.js\?v=3[\s\S]*?app\.js\?v=37/,
  "The tested annotation module must load before its application consumer.");
assert.match(app, /AnnotationLabeling\.mountHuman\(elements\.adminHumanLabelingPanel\)/);
assert.match(app, /AnnotationLabeling\.mountMachine\(elements\.adminMachineLabelingPanel\)/);
assert.match(app, /function adminSectionFromLocation\(\)[\s\S]*?human-labeling\|machine-labeling/,
  "Human and Machine Labeling must support direct URL navigation and refresh.");
assert.doesNotMatch(app, /admin-(?:accounts|workspaces)-tab/,
  "Unimplemented Accounts and Workspaces placeholders must not be added.");
assert.match(app, /fetch\("\/api\/admin\/detector-evaluation"/);
assert.match(app, /\["Concept", "Family", "Tier \/ class", "Pos", "Neg", "Total", "Maturity", "TP", "FP", "FN", "TN", "Precision", "Recall", "F1"\]/,
  "Detector metrics must expose labeled positive, negative, and total support with the confusion matrix.");
assert.match(app, /metric\.sampleSize/,
  "Every concept must display its deterministic sample-size classification.");
assert.match(app, /Labeled example review[\s\S]*?Positive labels[\s\S]*?Negative labels[\s\S]*?Errors only/,
  "Administrators must be able to filter and review every labeled example.");
assert.match(app, /Expected .*Present[\s\S]*?Predicted .*Present[\s\S]*?example\.result/,
  "Example review must identify expected label, prediction, and TP/FP/FN/TN result.");
assert.match(app, /Label note:[\s\S]*?Detector evidence:/,
  "Example review must expose label rationale and detector evidence.");
assert.match(app, /False positives[\s\S]*?False negatives/,
  "Detector errors must be reviewable by classification.");
assert.match(app, /Search detector concepts[\s\S]*?Tier 1[\s\S]*?Tier 2[\s\S]*?Tier 3[\s\S]*?Not evaluated[\s\S]*?Errors present/,
  "Full-taxonomy evaluation needs search, tier, maturity, and error filters.");
assert.match(index, /detector-evaluation-ui\.js\?v=1[\s\S]*?app\.js\?v=37/,
  "The deterministic Detector Evaluation filter helper must load before its application consumer.");
assert.match(app, /DetectorEvaluationUi\.matchesMetric/,
  "Rendered filtering must use the deterministic tested helper.");
assert.match(app, /All evaluated · Macro[\s\S]*?All evaluated · Micro[\s\S]*?report\.tierAggregates/,
  "Overall and tier-level Macro/Micro metrics must remain distinct.");
assert.match(app, /not an independently adjudicated research gold standard/,
  "Development label-source limitations must be explicit.");
assert.match(app, /not Job Fit score quality, qualifications, application decisions, or preference sentiment/,
  "The UI must distinguish detector evaluation from Job Fit scoring and decisions.");

assert.match(program,
  /MapGet\("\/api\/admin\/status"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)/);
assert.match(program,
  /MapGet\("\/api\/admin\/detector-evaluation"[\s\S]*?RequireAuthorization\(AdminAuthorization\.Policy\)/,
  "Detector Evaluation must retain server-side admin authorization.");
for (const route of ["queue", "generation-status", "generate", "decision", "source", "export", "import"]) {
  assert.match(program,
    new RegExp(`api/admin/annotations[\\s\\S]*?${route}[\\s\\S]*?RequireAuthorization\\(AdminAuthorization\\.Policy\\)`),
    `Annotation ${route} access must retain server-side admin authorization.`);
}
const labelingUi = fs.readFileSync(path.join(root, "wwwroot", "annotation-labeling-ui.js"), "utf8");
const humanMount = labelingUi.slice(labelingUi.indexOf("function mountHuman"), labelingUi.indexOf("function mountMachine"));
const machineMount = labelingUi.slice(labelingUi.indexOf("function mountMachine"));
assert.doesNotMatch(humanMount, /Items to add|Add all eligible|Export all JSONL|Import machine review JSONL/,
  "Human Labeling must not contain machine generation or exchange controls.");
assert.match(humanMount, /Review queue[\s\S]*?Correct[\s\S]*?Unsure \/ Skip[\s\S]*?Show full posting/,
  "Human Labeling must retain the one-card review workflow.");
assert.match(machineMount, /Items to add[\s\S]*?Add items[\s\S]*?Add all eligible[\s\S]*?Current corpus:[\s\S]*?Eligible ungenerated items:/,
  "Machine Labeling must expose unambiguous append-count generation and availability.");
assert.match(machineMount, /Export all JSONL[\s\S]*?Export unreviewed[\s\S]*?Import machine review JSONL/,
  "Machine Labeling must retain browser import/export controls.");
assert.doesNotMatch(machineMount, /data-decision|Unsure \/ Skip|annotation-card/,
  "Machine Labeling must not contain the human decision card.");
assert.match(machineMount, /importButton\.disabled = true[\s\S]*?!importFile\.files/,
  "Machine import must be genuinely disabled until a file is selected.");
assert.match(styles, /\.secondary-link-button[\s\S]*?cursor:\s*pointer[\s\S]*?\.secondary-link-button:hover[\s\S]*?--color-secondary-button-hover/,
  "Enabled export links must use clear token-based interactive styling.");
assert.match(styles, /\.annotation-labeling-panel button:disabled[\s\S]*?cursor:\s*not-allowed[\s\S]*?--opacity-disabled-control/,
  "Disabled labeling controls must have a distinct token-based state.");
assert.doesNotMatch(styles, /#[0-9a-f]{3,8}\b|(?:rgb|hsl)a?\s*\(/i,
  "Labeling styles must not introduce hard-coded colors.");
assert.match(program,
  /MapPost\("\/api\/account\/admin-bootstrap"[\s\S]*?RequireAuthorization\(\)[\s\S]*?RequireRateLimiting\("admin-bootstrap"\)/);
assert.match(program, /PermitLimit = 5[\s\S]*?Window = TimeSpan\.FromMinutes\(15\)/);
assert.match(accounts, /public List<string> Roles \{ get; set; \} = \[\];/);
assert.match(security, /CryptographicOperations\.FixedTimeEquals/);
assert.match(security, /SHA256\.HashData/);
assert.match(security, /UnixFileMode\.UserRead \| UnixFileMode\.UserWrite/);
assert.match(compose,
  /JOBSEARCHMANAGER_ADMIN_BOOTSTRAP_PATH: \/app\/data\/admin-bootstrap-code/);
assert.equal(fixtures.version, 3);
assert.equal(fixtures.fixtures.length, 148);
assert.equal(new Set(fixtures.fixtures.map(item => item.id)).size, fixtures.fixtures.length,
  "Evaluation fixture IDs must be stable and unique.");
assert.deepEqual(Object.keys(fixtures.labelScopes).sort(), ["tier1-target", "tier2-strong-negative"]);
assert.ok(fixtures.fixtures.every(item =>
  typeof item.expectedPresent === "boolean" ||
  typeof item.labelScope === "string" && Array.isArray(item.expectedPresentConceptIds)),
  "Ground-truth labels must be explicit legacy labels or closed multi-label scope values.");
assert.ok(fixtures.fixtures.every(item => !item.provenance ||
  ["public-posting", "synthetic", "synthetic-positive", "synthetic-hard-negative"].includes(item.provenance)),
  "Every explicit provenance value must use a supported class.");
assert.match(evaluation, /fixture\.Provenance \?\?[\s\S]*?"synthetic"[\s\S]*?"public-posting"/,
  "Legacy fixtures must receive a deterministic provenance class in API output.");
for (const conceptId of new Set(fixtures.fixtures.map(item => item.conceptId).filter(Boolean))) {
  const conceptFixtures = fixtures.fixtures.filter(item => item.conceptId === conceptId);
  assert.equal(conceptFixtures.filter(item => item.expectedPresent).length, 8,
    `${conceptId} must have eight reviewed positive examples.`);
  assert.equal(conceptFixtures.filter(item => !item.expectedPresent).length, 8,
    `${conceptId} must have eight reviewed negative examples.`);
}
assert.ok(fixtures.fixtures.filter(item => item.labelScope === "tier1-target").length >= 30,
  "Tier 1 target concepts need a substantial reviewed scenario set.");
assert.ok(fixtures.fixtures.filter(item => item.labelScope === "tier2-strong-negative").length >= 10,
  "Tier 2 concepts need an initial reviewed scenario set.");
assert.ok(fixtures.fixtures.filter(item => item.labelScope).every(item => item.labelSource === "Codex-reviewed"),
  "New multi-label fixtures must disclose their development label source.");
assert.match(evaluation, /Fixture\.ExpectedPresent/,
  "Expected labels must be read independently from fixture data.");
assert.match(evaluation, /_detector\.Analyze/,
  "Evaluation predictions must use the production JobConceptDetector.");
assert.match(evaluation, /ErrorFixtureIds[\s\S]*?errorFixtureIds/,
  "Machine-readable metrics must include deterministic error fixture IDs.");
assert.doesNotMatch(evaluation, /double\.NaN/,
  "Undefined metrics must not emit NaN.");
assert.match(evaluation, /positiveSupport == 0 \|\| negativeSupport == 0 \? "Not evaluated"[\s\S]*?Math\.Min\(positiveSupport, negativeSupport\) < 5 \? "Small sample"[\s\S]*?< 15 \? "Developing sample"[\s\S]*?"Established sample"/,
  "Maturity must use deterministic balanced-support thresholds and distinguish unevaluated concepts.");
assert.match(evaluation, /_catalog\.Concepts[\s\S]*?CalculateConcept/,
  "Every canonical detector concept must receive an evaluation-status record.");
assert.match(evaluation, /Tier1Concepts[\s\S]*?Tier2Concepts[\s\S]*?PartialConcepts/,
  "Tier and partial-evaluation classifications must be deterministic.");
assert.match(evaluation, /Travel Tolerance preference[\s\S]*?Normal Work Location preference[\s\S]*?Group Hard Conflict overrides[\s\S]*?Configured \/ Not Set preference state/,
  "Non-detector Job Fit constructs must be explicitly excluded with rationale.");

console.log("All Admin role, bootstrap, detector-evaluation, and labeling UI integration tests passed.");
