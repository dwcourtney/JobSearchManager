# JSON concept-rule authority

Normal Jobs classification loads `JobConceptCatalog.json` and the exact packaged
`rules/concepts-v1.json` / `rules/concepts-v1.schema.json` bytes before creating the
web host. `ConceptRuleSnapshot` validates the contract, compiles all rules once and
supplies the existing `RegexSemanticClassifier` matching mechanics. No SQLite store,
seed importer, lifecycle worker, telemetry flush or evaluator is registered in normal
composition. Invalid rules fail before listening; there is no fallback authority.

The ruleset remains 1.0.0, SHA-256
`aaf6d97ff6ad7b6c48d12ebc1d1c485722bb4817fee791d418d09172eccbc1a8`.
The preserved Phase 2 candidate fingerprint is
`348080299ffe13518d29668fb8da61ce0ff2ee2215c6ef802a5b60ecc2d55f70`.
Production additionally binds the `json-regex-v1` authority tag, RemoteWork v1.0.0
analysis v3, ExtendedLocation v1.0.0 analysis v4, and posting/fact input contract v1.
Its pipeline fingerprint is
`0bc0baf2fc1ac410f605eb177b6d52bb6d804292cdc2c442405a2078a4cad76b`.
No classification timestamps enter those identities.

Freshness binds title, normalized description, company/location inputs and complete
consumed remote/extended parsed facts, including evidence. Old SQLite classifications
are retained but treated as stale; existing local cache backfill replaces them without
provider refresh. Missing descriptions remain pending. The persistence guard checks
both current input and pipeline identities before updating shared cache data.

Admin Concept Detection is read-only: identity/validation/coverage diagnostics and
searchable, paginated rule inspection. Rule edits require source review and CI.
The stale-cache repair action changes cached results only. All old rule lifecycle,
import/export, relationship, reload and evaluation execution HTTP routes are absent.

Admin evaluation cards read checksum-verified immutable files indexed by
`evaluation/concept-detection/index-v1.json`. CURRENT requires the complete current
authority, dataset, reference and metric implementation identities. Curated evidence
also checks the currently packaged corpus bytes (with CRLF normalized as in the
existing evaluator). Holdout dataset/reference identities are pinned by the index;
private labels are not shipped. Prior authorities or mismatched identities are STALE;
research entries are always HISTORICAL. Machine-reference labels are not independent
human ground truth. No model or evaluator runs from Admin.

## Verification and report updates

The Phase 1 frozen oracle and archived DB remain the migration baseline. Phase 2
tests now call the promoted production loader through a test-only alias, so parity
checks exercise actual production mechanics rather than a second candidate engine.
Run the existing test executable with `--json-concept-parity`, then execute
`Tests/json-concept-downstream.tests.cjs` on its `.concepts.json` output. The complete
preserved corpus has 2,674 records and 227,290 concept decisions; downstream coverage
has 320,880 score/explanation comparisons.

Run `--json-concept-evaluation <phase1-archive> <new-comparison-directory>` to create
new SQLite/JSON curated and frozen holdout comparisons. It reads archived labels,
never invokes models and writes only a disposable DB/new report directories.
`migration/concept-rules/phase3/package-evaluations.py <comparison-directory>` packages
verified JSON results and a new index without overwriting an existing report. Every
artifact records exact authority/data/reference/metric identities and the uncommitted
validation source identity. When metric code changes, update the metric implementation
constant and rerun evaluation; tests prevent a silent identity mismatch.

`--json-concept-cache-reconcile <cache-root>` is an explicit offline repair command
using the normal JSON snapshot. Use a copied cache for validation, or stop the owning
application first. It preserves missing-description records, reanalyzes sufficient
cached input locally, and uses atomic per-file replacement. A second run should find
zero stale sufficient-input records. It never opens the SQLite rules database.

`migration/concept-rules/phase3/normal-startup-check.py` tests the real host on Windows
or in a normal Linux image. The inherited Windows error mode suppresses OS crash
dialogs only; rejected configurations must exit nonzero without opening a socket.

## Rollback and Phase 4 boundary

Keep `Microsoft.Data.Sqlite`, `SqliteSemanticRuleStore`, `LegacySemanticRuleMigrator`,
the seed, frozen oracle, research consumers, archived DB and live `regex-rules.db`.
The explicit legacy `--regex-maintenance` CLI remains compatibility tooling and can
write its supplied database; it is not part of normal startup or Admin.

Normal deployment composition, exact-commit checks, data/key mounts and rollback
handling are unchanged. Rolling back to the previous image can locally recompute its
own SQLite identities from retained cache descriptions. Do not delete rollback data.
Final SQLite package/source retirement belongs to Phase 4.

## Phase 4 dependency retirement

SQLite is now isolated outside normal compilation and packaging. See [SQLite retirement](sqlite-retirement.md) for the current production boundary, historical tooling and deployment checks. Earlier Phase 3 retention notes above describe the prior checkpoint.
