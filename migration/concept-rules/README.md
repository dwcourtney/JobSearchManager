# Concept-rule migration, Phase 1

This directory is an **offline migration oracle**, not application configuration.
Production remains `SqliteSemanticRuleStore` / `regex-rules.db`. No Admin, cache,
taxonomy, matching, Job Fit, or lifecycle behavior is changed. The application
project excludes this directory from publish; Docker excludes it from build inputs.

## Frozen authority

Baseline commit: `2c8a2e2735b1b812853ff6eeb0da19d969d82ebb`.

* Runtime fingerprint: `9d491a52ed1046fa319f2b6b9f4d81048b77f9d4a6774d803a14afee5ae06d2a`.
* Taxonomy v9: `514ed1c8c644d1eec426b5fdcf4d5a2c447aa61ce5572ae70b2d03fc3815a049`.
* Policy: `5ac8b473aabdbab1904cbcfd4f7e1076df5536b5b5f62fd0a93fe54922ee89dc`.
* Online backup SHA-256: `ce8469d4bd067ce3cfdb0de44e9cd9c994bd8d5da403c598890197387085ed3c`.
* Private immutable archive on curiosity:
  `/home/codex/jsm-research/archives/concept-rules-phase1-20260908-v1`.

The archive contains the SQLite backup, exact table JSON exports, SQL schema,
packaged taxonomy/seed/corpus bytes, evaluation artifacts, the pre-existing Triage
history archive, and private cache input copies. It is intentionally outside Git.
`archive-reference.json` contains its manifest hash, table counts, artifact inventory,
and the hash of `sqlite-effective-rules.json`. Read-only permissions prevent ordinary
accidental writes; hashes detect alteration. This is not a claim of WORM storage.

Counts: SemanticRules 288; RuleRelationships 0; CandidateValidations 0;
EvaluationRuns 4; RuleEvaluationResults 576; ConceptEvaluationResults 302;
LlmEvaluationRunDetails 1; SchemaInfo 3 (versions 2, 4, 5).

`sqlite-effective-rules.json` preserves all six matching fields, stable IDs,
provenance, reason, and SQL execution order. The 263 regex rules and 25 parsed-fact
selectors are explicitly distinguished. All 288 active rules / 85 concepts match
the independently reproduced v9 seed exactly. Full mutable metadata and history
remain in the archive, separate from effective definitions. No missing historical
transition events are invented: SQLite did not retain an append-only transition log.

## Reproduce safely

`archive.py <unused-absolute-directory>` runs on curiosity. It opens the source with
SQLite `mode=ro` and `query_only`, uses the online backup API, closes the backup,
then verifies it offline. It never instantiates the application's initializing store.
Inspect its explicit host paths before reuse. It rejects unexpected policy overrides.
Failure leaves an incomplete directory for inspection; do not treat it as an archive
unless the manifest and hash file exist and verification succeeds.

```text
python migration/concept-rules/export.py <private-archive> <new-export-directory>
python migration/concept-rules/archive_tests.py
dotnet run --project Tests/JobSearchManager.Tests.csproj -c Release
dotnet Tests/bin/Release/net10.0/JobSearchManager.Tests.dll --concept-oracle-parity <inputs.json> <report.json> <archived-authority.db>
node Tests/concept-oracle-downstream.tests.cjs <repository> <report.json.concepts.json>
```

Set `PHASE1_ARCHIVE` when running Python tests to also verify all archive hashes,
integrity, foreign keys, table counts, and every exported row. The C# harness copies
the supplied archive DB to a unique temporary directory before constructing the
production store. Never pass a live WAL database: provide the closed online backup.
`seed` instead of the database path exercises the actual C# seed migrator.

Inputs are JSON arrays with `id`, `title`, `html`, `primaryLocation`,
`additionalLocations`, and optional `remoteWork` / `extendedLocation` parsed facts.
When facts are supplied, both engines receive those exact objects. Otherwise the
unchanged factual detectors provide identical inputs to both engines. No labels
or model runs are produced. Cache copies are per-file snapshots, not an atomic
cross-workspace cache transaction, and may repeat postings across sources/workspaces.

## Oracle boundary and comparison

`Tests/FrozenSqliteConceptOracle.cs` freezes the baseline matching branches,
normalization, local negation, regex options, fallback, evidence order/truncation,
fact selection, context AND / group OR, exclusions and timeout behavior. Mutable
store/reload/telemetry code is omitted. Frozen taxonomy, policy, definition identity,
and a SHA-256 of complete 40-fixture output prevent accidental baseline movement.

For every posting, the harness compares all 85 presence decisions, complete
classifications, matched rule IDs, evidence order, timeout IDs, and exception
type/message. The production classifier does not expose exclusion/context traces;
a **test-only reflection adapter replays those branches using its actual compiled
rules and private FirstMatch method**. That diagnostic comparison supplements the
independent comparison of actual production return values; it is not production
instrumentation. It must be adapted explicitly when a future engine exposes traces.

Fixtures cover all seven live rule types, both live scopes, all three context groups,
signals, vetoes, local negation, HTML, repeated evidence, first match and truncation.
Separate temporary synthetic rules cover the unused `posting` scope, unsupported
NonBacktracking fallback, invalid regex errors and a pathological 100 ms timeout.
These synthetic rules never enter the frozen 288-rule export or production.
The actual Jobs caller is extracted from `wwwroot/app.js` and evaluated with null,
remote/travel, ideal/avoid, missing, disabled and pending configurations. Existing
list/detail regression tests remain part of the full source suite.

## Evaluation evidence is historical

Curated regression has 148 known cases and 1,740 scoped decisions (590 positive,
1,150 negative), covering 66 concepts. Macro F1 is 0.894598; micro F1 0.915771.
Its normalized dataset hash is
`615f9db007cb9a8cfc33936c9acbe0671525036e50d2c017248f904786508f2b`.
The older curated run lacks complete current identity metadata and must not be
presented as a current evaluation. Historical eight-concept subset scores are
not interchangeable with the full scoped benchmark.

Frozen Codex-reference holdout: 200 postings × 85 concepts = 17,000 decisions;
97 unresolved, 16,903 eligible (1,910 positive / 14,993 negative). Sample hash:
`2a5f532f4241368e1b21d30ef9a50ad16886749076e9111dce354baaa045b963`.
Reference hash:
`a0844aea385906da94073fffe76a22b03d583c21efcf7e4cf60cfd978bc86282`.
Historical RegEx macro/micro F1: 0.454672 / 0.359490. Historical LLM comparison:
0.566777 / 0.656896, with rule identity `not-applicable-llm`. LLM prediction hash:
`1fd46cfdab81feebe58255a6408ac9bc06a56bfda3b26a1ebe697abb9b7ccd44`.

These reports retain their original dates, denominators, provenance and limitations.
No labels/models were rerun. Matching historical rule/taxonomy fingerprints do not
prove equivalence of all factual-parser/engine versions, which old reports did not
fully identify. Phase 1 parity proves preservation of behavior, not model accuracy
or improved real-world generalization. Original reports and complete database rows
are preserved for exact metric and metadata inspection.
