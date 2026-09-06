# Cheap-triage refactor validation — 2026-09-05

No commit, push, deployment, training, model inference, label change or new occupation family was performed. The current cheap rules remain shadow/evaluation only. See [architecture and inventory](cheap-triage-rules.md).

## Decision preservation

C# ruleset `1.0.0` SHA-256: `269be7264e56f641723c254910d026d20687f5d61aaa39d967c5d52e4bc51983`.

All **4,701** frozen corpus/cache decisions match the historical electrical-safe baseline exactly; **zero** matching timeouts/fail-open results. The comparator also ran a no-op baseline/candidate comparison to exercise version comparison, with no changed decisions or safety regression. External version/reload semantics are separately tested with a temporary `1.0.1` ruleset.

| Cohort | Rows | KEEP recall | False rejects | Rejection |
|---|---:|---:|---:|---:|
| Provisional binary corpus | 2,669 | 99.5757% | 9 / 2,121 KEEP | 42 (1.5736%) |
| Described binary | 1,159 | 99.0153% | 9 / 914 KEEP | 42 (3.6238%) |
| Title-only binary | 1,510 | 100% | 0 / 1,207 KEEP | 0 |
| AMBIGUOUS corpus | 529 | Not scored | Not scored | 10 (1.8904%) |
| Old cache | 1,503 | Unlabeled prevalence; audited subset only | No new errors | 208 (13.8390%) |
| Earlier balanced development | 166 | 100% | 0 / 85 KEEP | 56 (33.7349%) |
| Leakage audit | 196 | 100% | 0 / 74 KEEP | 99 (50.5102%) |
| Delta audit | 41 | 100% | 0 / 1 KEEP | 40 (97.5610%) |
| Synthetic fixtures | 60 | 100% | 0 / 30 KEEP | 30 (50%) |

The nine existing provisional false rejects are preserved, not concealed or fixed by new rules. Their IDs, titles, evidence and rule diagnostics are in `Tests/cheap_triage_rules/refactor-comparison.json`. Full-cache rejection is workload volume, not an accuracy claim. Existing labels are machine-adjudicated and title-only labels are weak evidence. No blinded holdout was used.

The 4,701-row C# run took about 9.54 seconds including two process startups and full JSON diagnostics (about 493 postings/second). This is a reproducibility timing, not an isolated regex microbenchmark.

## Test coverage and execution

- Locked .NET restore; Release build with zero warnings/errors.
- All 138 deterministic .NET architecture tests on Windows; new tests exercise strict JSON/schema validation, unknown fields, invalid versions/regex and predicate combinations, nulls, duplicate properties/IDs/orders, missing/cyclic references, external loading, explicit ordering, guarded KEEP/corroborated REJECT, normalization, reason/evidence/provenance, oversized-input KEEP, immutable reload behavior, prompt tokens, fresh/stale metrics and configuration-path snapshots.
- Full `scripts/validate-source.ps1`: all existing JavaScript/UI and architecture checks plus the new modal, exact full clipboard content, fallback/error, close/focus/abort, Admin authorization/rate limit, current-version rendering and retained Job Fit runtime checks.
- Existing repository history/credential audit, canonical origin verification, and repository identity shell tests.
- Research preservation: all 247 pre-existing research files/manifests/scripts/reports checked against `Tests/cheap_triage_rules/preserved-research-sha256.json`; unchanged.
- Whitespace checks cover tracked diff and every new task file.

Final Linux results: **PASS** all 138 .NET tests in the pinned SDK container, deep-analysis Python self-tests, source dependency/secret/configuration gates, security-policy fail-closed self-tests, and all four image builds/scans (JSM, hardware benchmark, deep analysis, Ollama). The existing fixed High/Critical vulnerability gates and all-severity secret gates passed unchanged. App image/health/version identity, benchmark corpus exclusions, deep-analysis health/85-concept protocol identity, malformed-request validation and retired `/classify` 404 checks passed. Hardened containers ran non-root, read-only, with dropped capabilities and were removed afterward. No model inference was performed.

## Linux validation and supporting package correction

Only `ssh curiosity-codex` and Docker on that established host were used. The source snapshot is isolated at `/tmp/jsm-rules-validation/source`; no deployed JSM container or model was changed. The snapshot is based on `84ff420719bbd1100f6854aba5d024a3b3ea5d54` plus the uncommitted candidate and a file manifest. Image tags use `*-rules-local` to distinguish them from published images. OCI revision labels identify the base commit; the snapshot manifest identifies uncommitted contents.

The first complete container attempt stopped at the unchanged deep-analysis package pin. `apk policy libuuid` in the pinned official base showed installed `2.41.2-r0` and available `2.41.6-r1`; `2.41.6-r0` was no longer available. The narrow correction changes that exact pin to `2.41.6-r1` and its regression assertion. All other package pins/base digests remain unchanged.

An attempted partial-validation harness was rejected by automatic approval review because it removed the failing deep-analysis checks. It was not used. The package pin was corrected instead, followed by the complete original image/security checks, with no scanner exclusions or weakened policies.

The working-tree harness uses the original CI source/policy and all four image/contract commands. The CI assertion requiring a clean committed checkout cannot apply to this explicitly uncommitted task; canonical source identity and an archive/file manifest establish provenance instead. Hosted GitHub CodeQL/signature checks are not run without a commit/push. Their existing local integration tests pass; no hosted CodeQL completion is claimed.

Local raw logs and the snapshot manifest are in `D:\David\Documents\ChatGPT\Job Search Manager\rule-refactor-*.log` and `rule-refactor-source-manifest.json`; Linux logs are under `/tmp/jsm-rules-validation/`.

## Files

New subsystem: `CheapRejectRules.cs`, `RuleMaintenance.cs`, `CheapTriage/rulesets/1.0.0.json`, `CheapTriage/ruleset-schema-v1.json`, `CheapTriage/evaluation-context.json`, `CheapTriage/maintenance-prompt-v1.txt`.

Integration: `.gitattributes` (LF-pinned rules/prompt bytes), `JobSearchManager.csproj` (published assets), `Program.cs` (startup, evaluation CLI, read-only Admin prompt endpoint), `wwwroot/app.js`, `wwwroot/index.html`, `wwwroot/styles.css`, new `wwwroot/rule-maintenance.js`.

Tests/workflow: `Tests/Program.cs`, new `Tests/CheapRejectRuleTests.cs`, new `Tests/rule-maintenance.tests.js`, `scripts/validate-source.ps1`, new `scripts/evaluate-cheap-rules.py`; asset-version assertions in `Tests/admin-ui.tests.js`, `Tests/cache-status.tests.js`, `Tests/theme-settings.tests.js`, `Tests/job-card-badges.tests.js`, `Tests/job-unseen-state.tests.js`.

Supporting validation blocker: `classifier-service/Dockerfile`, `Tests/security-scanning.tests.js`.

Documentation/artifacts: `docs/cheap-triage-rules.md`, `docs/cheap-triage-validation.md`, `Tests/cheap_triage_rules/refactor-comparison.json`, `Tests/cheap_triage_rules/preserved-research-sha256.json`.

No components or historical files were deleted. Existing SQLite Job Fit semantics, Qwen functionality and saved evaluation formats remain compatible. There is no data migration. Invalid/missing configured cheap rules fail startup explicitly; reviewed configuration changes require restart to replace the immutable snapshot.

## Final diff and status

Local `main` and the existing `origin/main` tracking ref are both `84ff420719bbd1100f6854aba5d024a3b3ea5d54` (0 ahead / 0 behind). Nothing is staged or committed. There are 15 modified tracked files and 14 new task files. Pre-existing untracked research/documents remain untracked and unchanged.

Tracked diff summary (new files listed above are additional):

```text
 .gitattributes                   |  1 +
 JobSearchManager.csproj          |  2 ++
 Program.cs                       | 29 +++++++++++++++++++++++++++++
 Tests/Program.cs                 |  1 +
 Tests/admin-ui.tests.js          |  2 +-
 Tests/cache-status.tests.js      |  4 ++--
 Tests/job-card-badges.tests.js   |  2 +-
 Tests/job-unseen-state.tests.js  |  2 +-
 Tests/security-scanning.tests.js |  2 +-
 Tests/theme-settings.tests.js    |  4 ++--
 classifier-service/Dockerfile    |  2 +-
 scripts/validate-source.ps1      |  5 ++++-
 wwwroot/app.js                   |  7 +++++--
 wwwroot/index.html               |  5 +++--
 wwwroot/styles.css               |  5 +++++
 15 files changed, 59 insertions(+), 14 deletions(-)
```

Final `git status --short --branch`:

```text
## main...origin/main
 M .gitattributes
 M JobSearchManager.csproj
 M Program.cs
 M Tests/Program.cs
 M Tests/admin-ui.tests.js
 M Tests/cache-status.tests.js
 M Tests/job-card-badges.tests.js
 M Tests/job-unseen-state.tests.js
 M Tests/security-scanning.tests.js
 M Tests/theme-settings.tests.js
 M classifier-service/Dockerfile
 M scripts/validate-source.ps1
 M wwwroot/app.js
 M wwwroot/index.html
 M wwwroot/styles.css
?? CheapRejectRules.cs
?? CheapTriage/
?? RuleMaintenance.cs
?? Tests/CheapRejectRuleTests.cs
?? Tests/cheap_reject_evaluation/
?? Tests/cheap_triage_rules/
?? Tests/rule-maintenance.tests.js
?? docs/cheap-reject-investigation.md
?? docs/cheap-reject-leakage.md
?? docs/cheap-triage-rules.md
?? docs/cheap-triage-validation.md
?? docs/deberta-cheap-reject-experiment.md
?? docs/supervised-cheap-reject-experiment.md
?? scripts/evaluate-cheap-rules.py
?? wwwroot/rule-maintenance.js
```
