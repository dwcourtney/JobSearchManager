# RegEx rule lifecycle and operations

## Authority and schema

`regex-rules.db` is the runtime authority. `LegacyJobConceptRules.json` is only the deterministic
first-run migration source when the database contains no rules. SQLite uses WAL mode, foreign keys,
normal synchronous durability, and a busy timeout.

The schema stores:

- rules with concept, pattern/category, scope, rule type, status, timestamps, usage counters,
  provenance, reason, and optional context group;
- `supersedes` and `derived-from` relationships;
- versioned evaluation runs, per-rule evidence, and per-concept confusion counts;
- candidate-validation evidence bound to a fingerprint of the exact proposed rule.

Runtime fingerprints cover active/review-due rules, relationships, taxonomy identity, and detector
configuration. Posting classifications additionally bind content and taxonomy fingerprints.

## Lifecycle

The normal path is `proposed → validated → active → review-due → retired → deleted`.

1. Create or import a candidate. Imports always become proposed regardless of source status.
2. Run candidate validation. JSM online-backs up SQLite to a temporary database, activates the
   candidate only in that isolated copy, evaluates baseline and candidate snapshots on the fixed
   corpus, and persists comparative evidence against the original proposed-rule fingerprint.
3. Transition to validated. The store rejects this transition if matching evidence is absent.
4. Review the rule-level matches, false positives, unique coverage, redundancy, and aggregate deltas.
5. Activate separately. A successful compile atomically swaps the immutable runtime snapshot.
6. Review or retire rules when their review interval is reached. Retirement preserves provenance and
   history; deletion is a lifecycle tombstone, not an immediate physical purge.

Legacy migrated rules start active with honest telemetry: zero counters and no invented last-matched
time. Review-due status continues to run in production until an admin approves or retires the rule.

## Admin and maintenance interfaces

The Admin RegEx Rules view shows status, concept, scope, type, pattern/category, provenance, created
date, last match, lifetime matches, and matches since review. It filters by status, concept, usage,
and provenance and provides deterministic evaluation, candidate validation, activation/review/
retirement, hot reload, and stale-cache reclassification.

The local maintenance entry point is:

```text
dotnet JobSearchManager.dll --regex-maintenance overview <database>
dotnet JobSearchManager.dll --regex-maintenance evaluate <database>
dotnet JobSearchManager.dll --regex-maintenance benchmark-cache <database> <cache-root>
dotnet JobSearchManager.dll --regex-maintenance export <database> <json-file>
dotnet JobSearchManager.dll --regex-maintenance import <database> <json-file>
dotnet JobSearchManager.dll --regex-maintenance review-stale <database>
dotnet JobSearchManager.dll --regex-maintenance retention <database>
dotnet JobSearchManager.dll --regex-maintenance backup <database> <backup-file>
```

JSON exports include schema and taxonomy fingerprints. Imports reject mismatched schema/taxonomy and
never activate rules automatically. A malformed pattern, invalid metadata, failed compile, or failed
database operation leaves the prior runtime snapshot active.

## Telemetry and review

Production matches accumulate in memory and flush in batches on a bounded interval and shutdown.
Failed flushes are re-queued. Evaluation and diagnostics pass `productionUsage: false` and therefore
cannot inflate counters. `last matched` reflects successful production rule matches; `last reviewed`
reflects lifecycle approval, not inference activity.

The default review interval is 30 days and retired retention is 180 days. The explicit retention
operation turns expired retired rows into deleted tombstones; it does not physically erase their
provenance or evaluation history.

## Backup, restore, and failure behavior

The database lives under the existing `/app/data` persistent mount in production. Use the maintenance
`backup` action or SQLite online backup API while JSM is running; do not copy a live WAL database as a
single file. Store backups in `/home/codex/jsm-lab/backups` with the deployment SHA and timestamp.

Before restoring, stop JSM, retain the current database and its WAL/SHM files as one rollback set,
restore a verified online-backup file, then start JSM and run `overview` plus `evaluate`. Deployment
image rollback does not roll back the rule database. Schema migrations are forward-only and reject a
database newer than the application.

## Security boundaries

All mutation/evaluation/import endpoints require the Admin policy and state-changing rate limit.
Patterns and metadata have size bounds; RegEx execution uses `NonBacktracking` where supported and a
strict timeout fallback for recovered constructs. Imports are capped, parsed as structured JSON, and
checked against the canonical taxonomy. The rule store contains no account secrets or model weights.
