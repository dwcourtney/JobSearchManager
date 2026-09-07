# Job Search Manager

Job Search Manager (JSM) is a self-hosted ASP.NET Core application for collecting job postings,
tracking applications, and ranking jobs against personal preferences. One codebase supports a
loopback-only Windows desktop mode and a hardened Linux/container deployment.

## Supported architecture

- **Application:** ASP.NET Core on .NET 10 with a dependency-light browser client.
- **Persistence:** isolated filesystem workspaces plus a lifecycle-managed SQLite RegEx rule store.
- **Default Semantic Job Fit:** a deterministic in-process RegEx classifier evaluates the canonical
  85-concept taxonomy. It requires no model service, GPU, or network request.
- **Model research:** Qwen/Ollama tooling is isolated in the explicit `research/qwen/` workflow.
  Normal builds, startup and deployment require no model services or GPU tooling.
- **Deterministic analysis:** salary, clearance, credentials, education, work authorization, remote
  metadata, and extended-location requirements remain ordinary application code.
- **Administration:** admins can inspect, filter, evaluate, approve, activate, review, retire,
  export, import, back up, and atomically hot-reload RegEx rules.

Cheap Triage, Human Review and maintenance/discovery are research-only. Normal JSM contains no
triage execution or Admin workflow and needs no triage rules, queues or databases. Historical
artifacts and passive cache compatibility are preserved; see
[`research/cheap-triage/README.md`](research/cheap-triage/README.md).

DeBERTa and the former automatic model-classifier service are not part of the runtime architecture.

## Local Windows mode

Prerequisite: the .NET 10 SDK selected by `global.json`.

```powershell
dotnet restore JobSearchManager.csproj --locked-mode
dotnet run --project JobSearchManager.csproj
```

Local mode listens only on `http://127.0.0.1:54321`, opens the browser by default, and stores its
single local workspace under the application data directory. Default Job Fit works without Docker,
Ollama, a GPU, or an account.

## Linux/container mode

`compose.yaml` is the development example. Production uses `deploy/compose.curiosity.yaml` with exact
immutable image references. Both normal manifests declare only JSM, running non-root, read-only
and capability-dropped. Existing Mailpit/SMTP integration remains on the normal network.
Model experiments use the separate `research/qwen/compose.yaml` project.

Important configuration:

| Setting | Purpose |
| --- | --- |
| `JOBSEARCHMANAGER_HOSTING_MODE=Container` | Enables isolated workspaces and same-origin protection. |
| `JOBSEARCHMANAGER_DATA_PROTECTION_PATH` | Persists cookie-protection keys across replacement. |
| `JOBSEARCHMANAGER_PUBLIC_BASE_URL` | Builds account verification and recovery links. |
| `JOBSEARCHMANAGER_ADMIN_BOOTSTRAP_PATH` | Enables the physical-host one-time Admin claim. |

The SQLite database defaults to `/app/data/regex-rules.db` in container mode, so it shares the
existing persistent application bind mount. See `docs/curiosity-cicd.md` for exact-SHA deployment and
`docs/regex-rule-lifecycle.md` for rule operations and backup procedures.

## Semantic classification

The SQLite rule set is authoritative. Runtime startup migrates the recovered legacy catalog only when
the database is empty, compiles active/review-due rules into an immutable snapshot, and atomically
swaps snapshots after a successful reload. Invalid candidates never replace the current snapshot.

Persisted job classifications carry posting, ruleset, taxonomy, and classification fingerprints.
Changed text, taxonomy, rules, or detector configuration invalidates only the affected cached result.
Rule match counters are buffered and periodically committed; evaluation traffic never increments
production usage.

List and detail views project the same current RegEx classification, so selecting a job is
observational. The Admin evaluation ledger separates the **CURATED REGRESSION BENCHMARK**,
validation, and **AI-ADJUDICATED PRODUCTION HOLDOUT**. The holdout uses prediction-blinded Codex A/B
passes plus disagreement adjudication; its machine-derived references are not human ground truth.
LLM comparison and hardware preflight commands belong to the separate research executable; see
[`research/qwen/README.md`](research/qwen/README.md). Persisted model fields remain readable for
cache compatibility and do not affect production Job Fit.
See `docs/regex-evaluation-methodology.md` for sampling,
label provenance, contamination, support, metrics, and the PR-curve limitation.

Candidate rules start as proposed. Comparative fixed-corpus evidence must be persisted before they
can become validated, and an admin must activate them separately. See `docs/semantic-classifier.md`.

## Validation

```powershell
dotnet restore JobSearchManager.csproj --locked-mode
dotnet restore Tests/JobSearchManager.Tests.csproj --locked-mode
dotnet build Tests/JobSearchManager.Tests.csproj --configuration Release --no-restore
dotnet run --project Tests/JobSearchManager.Tests.csproj --configuration Release --no-build
python Tests/deployment-script.tests.py
pwsh -NoLogo -NoProfile -File scripts/validate-source.ps1
pwsh -NoLogo -NoProfile -File scripts/audit-repository.ps1
```

Hosted CI additionally runs the JavaScript architecture tests, CodeQL, Trivy, Linux image health,
exact-commit identity checks, and JSM-only Compose/deployment checks. Model validation runs only
through the separate research workflow.

## Repository map

- `SemanticRules.cs`, `SqliteSemanticRuleStore.cs`, `RegexSemanticClassifier.cs` — rule schema,
  lifecycle, hot reload, fingerprints, and telemetry.
- `LegacyJobConceptRules.json`, `RegexValidationCorpus.json`, `RegexEvaluation.cs`,
  `AiHoldoutEvaluation.cs`, `HoldoutMetrics.cs` — recovered source catalog, curated regression
  evaluation and frozen-reference metrics.
- `SemanticClassificationService.cs`, `SemanticClassificationContracts.cs` — deterministic RegEx integration.
- `research/qwen/` — isolated model executable, Compose, provisioning and validation. Historical
  model source paths and artifacts are retained and excluded from normal compilation/builds.
- `Program.cs`, `JobCatalog.cs`, `JobModels.cs` — HTTP application, ingestion, cache, workflow, and
  compatible historical model fields (without model execution).
- `wwwroot/`, `Tests/`, `deploy/`, `scripts/` — browser UI, regression coverage, and operations.
