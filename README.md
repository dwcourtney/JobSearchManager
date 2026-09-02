# Job Search Manager

Job Search Manager (JSM) is a self-hosted ASP.NET Core application for collecting job postings,
tracking applications, and ranking jobs against personal preferences. One codebase supports a
loopback-only Windows desktop mode and a hardened Linux/container deployment.

## Supported architecture

- **Application:** ASP.NET Core on .NET 10 with a dependency-light browser client.
- **Persistence:** isolated filesystem workspaces plus a lifecycle-managed SQLite RegEx rule store.
- **Default Semantic Job Fit:** a deterministic in-process RegEx classifier evaluates the canonical
  85-concept taxonomy. It requires no model service, GPU, or network request.
- **Dormant deep-analysis infrastructure:** pinned Qwen/Ollama remains installed for possible later
  restoration, but no normal UI or HTTP route invokes it and its stored results do not affect Job Fit.
- **Deterministic analysis:** salary, clearance, credentials, education, work authorization, remote
  metadata, and extended-location requirements remain ordinary application code.
- **Administration:** admins can inspect, filter, evaluate, approve, activate, review, retire,
  export, import, back up, and atomically hot-reload RegEx rules.

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
immutable image references. JSM, the dormant deep-analysis bridge, and Ollama are non-root,
read-only, capability-dropped containers. The LLM network is internal and publishes no host port.

Important configuration:

| Setting | Purpose |
| --- | --- |
| `JOBSEARCHMANAGER_HOSTING_MODE=Container` | Enables isolated workspaces and same-origin protection. |
| `JOBSEARCHMANAGER_DATA_PROTECTION_PATH` | Persists cookie-protection keys across replacement. |
| `JOBSEARCHMANAGER_PUBLIC_BASE_URL` | Builds account verification and recovery links. |
| `JOBSEARCHMANAGER_ADMIN_BOOTSTRAP_PATH` | Enables the physical-host one-time Admin claim. |
| `DeepAnalysis__BaseUrl` | Internal, optional Qwen deep-analysis bridge. |

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
validation, and unseen production holdout. See `docs/regex-evaluation-methodology.md` for sampling,
label provenance, contamination, support, metrics, and the PR-curve limitation.

Candidate rules start as proposed. Comparative fixed-corpus evidence must be persisted before they
can become validated, and an admin must activate them separately. See `docs/semantic-classifier.md`.

## Validation

```powershell
dotnet restore JobSearchManager.csproj --locked-mode
dotnet restore Tests/JobSearchManager.Tests.csproj --locked-mode
dotnet build Tests/JobSearchManager.Tests.csproj --configuration Release --no-restore
dotnet run --project Tests/JobSearchManager.Tests.csproj --configuration Release --no-build
python classifier-service/classifier_service.py --self-test
pwsh -NoLogo -NoProfile -File scripts/validate-source.ps1
pwsh -NoLogo -NoProfile -File scripts/audit-repository.ps1
```

Hosted CI additionally runs the JavaScript architecture tests, CodeQL, Trivy, Linux image health,
exact-commit identity checks, and confirms the removed `/classify` model endpoint remains absent.

## Repository map

- `SemanticRules.cs`, `SqliteSemanticRuleStore.cs`, `RegexSemanticClassifier.cs` — rule schema,
  lifecycle, hot reload, fingerprints, and telemetry.
- `LegacyJobConceptRules.json`, `RegexValidationCorpus.json`, `RegexEvaluation.cs` — recovered source
  catalog, fixed evaluation corpus, and deterministic evidence reporting.
- `ClassifierClient.cs`, `classifier-service/` — in-process default RegEx integration and opt-in Qwen
  bridge.
- `Program.cs`, `JobCatalog.cs`, `JobModels.cs` — HTTP application, ingestion, cache, workflow, and
  persistent LLM request lifecycle.
- `wwwroot/`, `Tests/`, `deploy/`, `scripts/` — browser UI, regression coverage, and operations.
