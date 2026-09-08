# JSON concept runtime and historical SQLite

Normal production has no SQLite package, native library, store, lifecycle policy, telemetry worker, SQL evaluator or maintenance CLI. It does not open `regex-rules.db`.

`JobConceptCatalog.json` + `rules/concepts-v1.json` + RemoteWork / ExtendedLocation factual outputs -> immutable deterministic matcher -> cached SemanticJobClassification -> shared Jobs list/detail -> existing browser Job Fit.

The Phase 3 rules, taxonomy, pipeline fingerprint and packaged evaluation reports are byte/identity preserved. Removing unused infrastructure does not invalidate caches. CURRENT/STALE/HISTORICAL evaluation presentation remains immutable.

`migration/legacy-sqlite/Jsm.LegacySqlite.csproj` explicitly owns Microsoft.Data.Sqlite and the historical store, seed migration, lifecycle/telemetry contracts, evaluators and maintenance CLI. It references production's generic matching output contract; production never references it. Tests and research explicitly reference this isolated project. Cheap Triage also directly declares its own SQLite package for its archive reader.

Build and inspect historical copies with:

```sh
dotnet run --project migration/legacy-sqlite/Jsm.LegacySqlite.csproj -- --regex-maintenance overview /path/to/disposable-copy.db
python migration/concept-rules/archive_tests.py
```

Never point mutable maintenance commands at the sealed archive. The Phase 1 seal is `/home/codex/jsm-research/archives/concept-rules-phase1-20260908-v1`; verify `archive-reference.json` and the archive manifest before copying its database. The host production DB remains physically present as historical rollback data and must not be deleted. Preserve old images and backups.

Normal deployment uses `--json-concept-audit <cache-root>` to compare all curated confusion matrices against the validated immutable report and benchmark cached descriptions with the actual JSON matcher. The cache mount is read-only; no provider or database is accessed. Existing signature, security, exact-commit, health/version and rollback gates remain. The old unused DB is only copied byte-for-byte as a rollback artifact; nonempty historical WALs require separate archive tooling and are never modified by deployment.

The exact sources that produced Phase 3's immutable metric-implementation identity remain under `migration/concept-rules/phase3/evaluated-source`. Their metadata is historical evidence, not a requirement to compile evaluators into the normal app. Research evaluators retain their metric behavior and accept the shared matcher interface.
