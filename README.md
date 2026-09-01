# Job Search Manager

Job Search Manager (JSM) is a self-hosted ASP.NET Core application for collecting job postings,
tracking applications, and ranking jobs against personal preferences. One codebase supports a
loopback-only Windows desktop mode and a hardened Linux/container deployment.

## Supported architecture

- **Application:** ASP.NET Core on .NET 10 with a static browser client.
- **Persistence:** JSON files on the local filesystem. Container mode isolates each anonymous or
  authenticated workspace and persists account, workspace, cache, and Data Protection state in
  explicit bind mounts.
- **Default Semantic Job Fit:** the pinned `cross-encoder/nli-deberta-v3-base` model evaluates all
  85 canonical concepts using bounded NLI inference.
- **Optional deep analysis:** `qwen3:4b-instruct-2507-q4_K_M` remains available through Ollama only
  when a user selects **Deep Analyze with Qwen** for one posting.
- **Deterministic analysis:** salary, clearance, credentials/licenses, education, citizenship/work
  authorization, explicit remote metadata, and extended-location requirements remain ordinary
  application code. They do not depend on either model.
- **Accounts:** optional local accounts use ASP.NET Core Identity password hashing, protected
  authentication cookies, email verification/recovery, and portable workspace export/import.
- **Administration:** the Admin page contains operational status and DeBERTa backfill controls only.

Azure storage/hosting, the annotation and Training Data subsystem, semantic regex matching, and
rejected model-experiment plumbing are not part of the supported product.

## Local Windows mode

Prerequisites: the .NET 10 SDK selected by `global.json`.

```powershell
dotnet restore JobSearchManager.csproj --locked-mode
dotnet run --project JobSearchManager.csproj
```

Local mode listens only on `http://127.0.0.1:54321`, opens the browser by default, and stores its
single local workspace under the application data directory. It does not require Docker, Ollama,
or account setup. When no classifier is reachable, ingestion and all deterministic analysis keep
working; semantic classifications remain pending or unavailable unless a valid cached result exists.

Useful local settings in `appsettings.json`:

- `Application:OpenBrowser` controls automatic browser launch.
- `Application:RefreshOnStartup` controls the initial source refresh.
- `Classifier:BaseUrl` may point to a private compatible classifier during development.

## Linux/container mode

`compose.yaml` is the development example. Create writable persistent directories first, then start
the stack with Docker Compose. The JSM and classifier containers are non-root, read-only, capability-
dropped processes. The classifier network is internal and publishes no Ollama or adapter port.

Production uses `deploy/compose.curiosity.yaml` with exact immutable image references supplied by the
deployment workflow. Required configuration includes:

| Setting | Purpose |
| --- | --- |
| `JOBSEARCHMANAGER_HOSTING_MODE=Container` | Enables browser-isolated filesystem workspaces and same-origin protection. |
| `JOBSEARCHMANAGER_DATA_PROTECTION_PATH` | Persists cookie-protection keys across replacement. |
| `JOBSEARCHMANAGER_PUBLIC_BASE_URL` | Builds account verification and recovery links. |
| `JOBSEARCHMANAGER_ADMIN_BOOTSTRAP_PATH` | Optionally enables the physical-host one-time Admin claim. |
| `JOBSEARCHMANAGER_SMTP_HOST` and related SMTP settings | Deliver account messages; production currently uses internal Mailpit. |
| `Classifier__BaseUrl` | Internal semantic-classifier adapter endpoint. |

The curiosity deployment procedure, exact-SHA gates, rollback behavior, and operational boundaries are
documented in `docs/curiosity-cicd.md`.

## Semantic classification

DeBERTa classifies every canonical Job Fit concept asynchronously. A persisted result contains posting
content hash, taxonomy version/fingerprint, model identity/revision/digest, classifier-configuration
version/fingerprint, timestamp,
classification fingerprint, and all 85 predictions. Cache reuse requires every output-affecting input
to remain unchanged. Classification prioritizes active and recent jobs, then newer posting dates.

Classifier outages are non-fatal: valid cached results remain usable; otherwise the job records a
pending/unavailable semantic state. Admins can inspect coverage or start an idempotent existing-cache
backfill. See `docs/semantic-classifier.md` for the contract and reusable validation set.

## Data and privacy boundaries

JSM writes only known account/workspace documents and explicit deployment state. Never commit runtime
data, credentials, account stores, Data Protection keys, model weights, Mailpit state, runner state, or
environment files. Reset and import operations are scoped to the current workspace. Model weights and
semantic requests stay on the internal local classifier network.

## Validation

Run the deterministic suite on Windows:

```powershell
dotnet restore JobSearchManager.csproj --locked-mode
dotnet restore Tests/JobSearchManager.Tests.csproj --locked-mode
dotnet build JobSearchManager.csproj --configuration Release --no-restore
dotnet run --project Tests/JobSearchManager.Tests.csproj --configuration Release --no-restore
python classifier-service/classifier_service.py --self-test
pwsh -NoLogo -NoProfile -File scripts/validate-source.ps1
pwsh -NoLogo -NoProfile -File scripts/audit-repository.ps1
```

Hosted CI additionally performs CodeQL and Trivy scans, builds the three Linux images, verifies
non-root/read-only health, and proves exact commit identity. The live semantic acceptance set can be
run only against an isolated GPU-capable classifier candidate:

```bash
python3 scripts/validate-semantic-classifier.py --url http://job-classifier:8081
```

## Repository map

- `Program.cs`, `JobCatalog.cs`, `JobModels.cs` — HTTP application, ingestion, cache, and workflow.
- `JobConceptCatalog.json`, `ClassifierClient.cs`, `classifier-service/` — canonical 85-concept
  semantic contract, persistence provenance, default DeBERTa runtime, and opt-in Qwen adapter.
- `*Detector.cs`, `JobAnalysis.cs` — deterministic non-semantic parsers.
- `wwwroot/` — dependency-light browser application and theme system.
- `Tests/` — deterministic .NET and JavaScript regression coverage.
- `deploy/`, `.github/workflows/`, `scripts/` — hardened exact-SHA CI/CD and operations.

## Historical note

The product previously evaluated Azure Blob persistence, regex-based semantic concepts, annotation
workflows, supervised embeddings, and several local/hosted language-model alternatives. Those paths
were experiments, not supported architecture. DeBERTa is the default screening classifier; Qwen is
an optional per-job deep-analysis tool and never runs as an automatic backfill.
