# Live Cheap Triage observations

Cheap Triage is observational only. There is no Active mode, filtering branch, saved-call counter, provider download or Qwen invocation. Job Fit's semantic classification, list projection, scoring, visibility and workflow decisions are unchanged. The electrical-safe rules and frozen research artifacts are unchanged.

## Pipeline and persistence

`JobCatalog` loads jobs from the existing company/query cache on initialization, recent-source switches and list hydration. Refresh obtains provider listings/details under the existing source coordinator. Detail reads adopt shared analysis; deterministic semantic classification runs on demand and in the existing semantic backfill worker. Existing parser versions and semantic content/taxonomy/ruleset fingerprints still govern Job Fit freshness.

After normal hydration, refresh and detail reads, `ScheduleCheapTriage` schedules a background pass when an observation is missing or stale. Matching runs outside catalog/source locks. The worker then acquires the existing catalog/source coordinator, reloads the latest shared document, verifies input identity, and merges only the observation. It never persists history. A concurrent input change cannot receive an old observation; normal shared hydration schedules the new input. Concurrent Job Fit updates are preserved. Another catalog's current observation wins, avoiding a redundant write. Simultaneous first-time observers may both match an input before one persists it; they still coalesce persistence under the existing coordinator.

`JobRecord.CheapTriage` is an optional additive cache field containing analysis version, UTC timestamp, description availability and the engine decision: rule version/content hash, input hash, category, reason, matched rule IDs/evidence, guards and fail-open status. It is stored using the existing compressed-description shared company/query cache abstraction, including its existing file/blob storage and concurrency handling. No new database, separate per-user copy of public analysis, or storage migration is introduced. Existing records without the field are valid. Old readers ignore the additional field. Off does not erase previous observations.

Freshness includes adapter analysis version, ruleset version, exact rules bytes and title/description input. It deliberately excludes salary preferences, user profile, saved/hidden state and Job Fit results. Observations do not mark a posting changed or unseen. Provider refresh preserves the previous observation for inspection until reconciliation replaces it. Current observations keep their original timestamp; a pass with no changed result does not write the cache.

The engine's title-only behavior is preserved: no body is fetched for corroboration. Fail-open limits/timeouts or missing input are represented as `UNDETERMINED`, equivalent to continuing normally. All KEEP and REJECT results also continue normally. A persistence error is reported in Admin and logged; the next normal hydration or observation refresh can retry. No diagnostic error controls downstream work.

## Persistent configuration

`appsettings.json` explicitly defaults `CheapTriage:Mode` to `Off`. Supported exact values are `Off` and `Shadow`; unsupported values, including Active, fail configuration validation. Configuration and the loaded ruleset are immutable startup snapshots.

- **Off:** no live matching or recording in normal Jobs. Retained observations remain inspectable, and stale observations are identified without recomputing them.
- **Shadow:** background matching/recording occurs for all cached jobs in the selected source, including title-only and retained/hidden jobs. Every job retains normal Job Fit, search visibility and workflow behavior.

On curiosity, persist `CheapTriage__Mode=Shadow` in `/home/codex/jsm-cicd/jsm-runtime.env`, then recreate only the JSM application with the normal Compose manifest and current image references. The manifest reads this optional host-owned environment file on every deployment; an absent file preserves the Off default. Keep this runtime file outside Git. Alternatively persist `CheapTriage:Mode` in the deployed application configuration. Returning that setting to Off and restarting stops new observations without deleting data. This implementation does not change curiosity's environment or deploy anything. There is no live mode-toggle control.

For a new immutable ruleset, configure `CheapTriage:RulesetPath` and restart. Old hashes/versions become stale. Loaded sources reconcile from cached data; inactive source caches reconcile when selected/loaded. This is not an all-storage background crawler. Frozen metrics are shown only when their packaged ruleset hash matches the loaded rules. No provider refresh is needed for triage reconciliation.

## Inspection and review

Job detail contains a compact expandable Cheap Triage diagnostic independent of Job Fit eligibility. It shows mode, recorded decision, stale/pending status, reason, version/hash, timestamp, input coverage, matched rules and literal evidence. Refresh observation only reloads that diagnostic; it does not fetch provider details or change workflow state. Late responses cannot show a different job's evidence.

Admin separates **Offline evaluation** from **Live Shadow observation**. The live report is explicitly scoped to the current workspace's selected company/query cache, including retained and hidden records; it is not global traffic or unbiased prevalence. Counts include current analyzed, KEEP/REJECT/UNDETERMINED, title-only/description-backed, rejection percentage, missing/stale observations, previous-rule observations, running/error state, latest persisted analysis and last process-local reconciliation. Current counts exclude stale decisions. Counts are a snapshot; the Refresh observations button requests an updated view without polling providers.

The expandable rejected-job review includes employer/title/stable ID, current/stale status, recorded reason/evidence/rules, source availability, workspace workflow state and the same client Job Fit calculation as the list. Unscored jobs show TBD. JSON export retains those fields, the loaded rule identity and export time for human review. It includes stale recorded rejects explicitly marked as such; it is neither a human reference set nor a claim of safe filtering. Only the latest observation per job is retained, not a historical append-only ledger. JSON avoids spreadsheet formula interpretation and all UI evidence uses textContent.

The review/status endpoint is Admin-authorized and no-store; the per-job diagnostic uses the existing account/anonymous workspace isolation boundary and reads only that catalog. No new cross-workspace visibility is introduced.

## Validation scope

Tests exercise cache hydration, Off, unchanged-content write reuse, title/body coverage, unknown/fail-open decisions, rule/input invalidation, shared-catalog reuse, a concurrent content/analysis write, and real deterministic Job Fit classification on both KEEP and REJECT jobs. UI tests cover diagnostics, stale/pending/Off/error states, response races, safe text, metrics, review and JSON export. Full existing Job Fit/workspace/source/UI tests and frozen parity remain required. Container checks run against an isolated marked uncommitted snapshot on curiosity; no production deployment or model inference is required.
