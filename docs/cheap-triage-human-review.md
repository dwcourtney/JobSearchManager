# Cheap Triage Human Review

Admin → Cheap Triage → Human Review presents the frozen `human_review_v1` queue one case at a time. Choose KEEP, REJECT or AMBIGUOUS and press **Save review**. A note is optional. Previous/Next preserve saved selections; unsaved changes must be saved or explicitly discarded before navigating between cases. Decisions can be revised. Progress counts unique reviewed jobs, not revision events. Completing all 29 displays **Review Complete**, class totals and a JSON download. Partial exports are also supported and contain saved decisions only.

This is human adjudication of broad technical/engineering occupational relevance, not personal suitability. Historical Job Fit and workflow snapshots are context, not refreshed live scores. The original queue has 24 QUESTIONABLE and 5 LIKELY WRONG cases, all description-backed. Missing historical scores are shown as “not captured.” No provider fetch or model inference occurs.

## Provenance and persistence

- Read-only packaged source: `CheapTriage/review-queues/human-review-v1.json`, derived from `Tests/cheap_reject_evaluation/human_review_v1/queue.csv`. Its source-manifest SHA-256 and exact 29 stable IDs are checked against the research artifacts by tests. Original machine labels and blank human fields remain untouched.
- The exact packaged file bytes determine a SHA-256 queue fingerprint. Each saved revision records that fingerprint, queue version, source-manifest hash, stable job ID, decision, note, UTC timestamp and authenticated reviewer ID.
- A separate SQLite database defaults to `/app/data/cheap-triage-human-review.db` in containers and `<content-root>/data/cheap-triage-human-review.db` otherwise. `HumanReview__DatabasePath` can override it. The existing durable `/app/data` mount is sufficient; no migration of job/account/rule stores is needed. Normal deployments now make a verified online SQLite backup to the existing curiosity backups directory whenever this database exists. The dedicated `--human-review-backup <source> <destination>` CLI opens the source read-only, preserves revision/provenance data and refuses to overwrite an existing backup. Custom database paths require an explicit deployment backup mapping and fail closed. Include this database in routine state backups as well.
- Adjudications are shared among administrators. The latest revision per case is current; prior revisions remain append-only. An expected-revision check rejects stale writes with HTTP 409 rather than overwriting another session's decision. Reload and consciously revise after a conflict.
- Changing the bundled queue bytes creates a new review scope with blank decisions; old revisions remain stored, without automatic carry-forward. Preserve the old queue and export before replacing it if its review history will be needed outside the database.

## API and boundaries

Admin authorization protects GET/POST `/api/admin/cheap-triage/human-review` and GET `/api/admin/cheap-triage/human-review/export`. Existing same-origin write protection and state rate limits apply. Responses are not cached. Notes are limited to 2,000 characters and rendered as plain text. Export includes case snapshots, latest decisions, all revisions for the current queue, provenance, counts and export time; treat reviewer notes/identity as private Admin data.

This store has no connection to the production decision/rule writer. Saving, revising, completing and exporting do not change rules, machine labels, job visibility, workflow or Job Fit. Cheap Triage remains Off/Shadow only. No automatic Codex execution or downstream classification is started. Any later rule adjustment requires separate review and authorization.

## Validation

`HumanReviewTests` checks exact queue membership, initial blanks, persistence through reopening, all three decisions, revisions, stale-write rejection, progress/completion, exported provenance, queue-version isolation and unchanged queue/machine/rule hashes. `Tests/human-review.tests.js` exercises rendering, save failure recovery, navigation, revision, reopening, conflict recovery, completion and export wiring. Both run in the normal repository validation entry points. Existing Cheap Triage and Job Fit regressions continue to apply.
