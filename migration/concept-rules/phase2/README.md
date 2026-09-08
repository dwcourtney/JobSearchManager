# Phase 2: JSON candidate, SQLite still authoritative

The candidate is `rules/concepts-v1.json` (1.0.0). Its only source is the verified
Phase 1 effective export, SHA-256
`e6a3e05ceccec71e7e2a50e4aaea64e36e667ed9278fe4eb234da951a1acb3f0`.
Run `python migration/concept-rules/phase2/generate-candidate.py` to reproduce it.
The generator never reads the legacy seed as candidate authority.

The 288 stable IDs retain all matching strings, kinds, scopes, context membership,
provenance, descriptions and explicit execution positions. Parsed facts have typed
selectors; there is no regex-pattern field on those JSON rules. Lifecycle/status,
telemetry, counters, relationships and evaluation history remain in the Phase 1 archive.

## Boundary

Only the test/migration project compiles this directory. Normal application publish
excludes this directory and both candidate JSON files. Production DI, startup,
Admin, cache identities and SQLite package/storage remain unchanged.

`RegexSemanticClassifier` gains a small internal immutable-snapshot constructor.
Its matching branches are unchanged. The public constructor still receives SQLite
and its existing policy. The candidate has no store and rejects reloads and production
telemetry; no generalized storage interface or duplicated matching engine was added.

The JSON snapshot itself is immutable and typed. A small compatibility adapter passes
its definitions to the existing matching records. For parsed facts, the existing
non-regex branches receive selector categories; these values are never regex-compiled.
The transient matching records use neutral lifecycle placeholders solely because the
existing internal record contains those fields. They do not represent a JSON lifecycle
store and never get persisted.

## Validation and identity

The loader checks the supported schema bytes before loading a candidate. Strict C#
contract validation rejects duplicate JSON keys, unmapped fields, nulls, unsupported
versions, duplicate IDs/positions, unknown taxonomy concepts, invalid scopes/kinds,
bad regexes, excessive patterns, invalid contexts and malformed/unknown selectors.
Category vocabularies are obtained from the existing factual rules, not a second
hard-coded geographic vocabulary.

Positions must be contiguous and match `ConceptId → kind → RuleId` using ordinal
comparison. The engine sorts by validated explicit positions; reversing the physical
JSON array preserves execution. AND within a context group, OR across successful
groups/other evidence, exclusion precedence, first acceptable matches, local negation,
evidence deduplication/truncation and timeout behavior stay in the shared engine.

The schema covers structure and local constraints. Duplicate identities/positions,
canonical ordering, taxonomy membership, context membership and regex compilation
require the additional loader checks. `schema-tests.py` exercises this distinction.

The candidate exposes exact raw-byte and schema hashes, ruleset version, taxonomy,
engine contract `jsm-concept-matching` v1, policy hash and a distinct pipeline fingerprint.
The pipeline binds these identities plus both remote-work and extended-location rule
hashes, the factual inputs consumed by this matcher. The engine contract identifies
the frozen matching/normalization behavior; changes to that contract require a version
change. The old SQLite fingerprint is provenance only. No production cache identity is
changed or falsely reused as the JSON identity.

## Reproduce comparisons

```text
dotnet run --project Tests/JobSearchManager.Tests.csproj -c Release
dotnet Tests/bin/Release/net10.0/JobSearchManager.Tests.dll --json-concept-parity <inputs.json> <output.json> <archived-authority.db>
node Tests/json-concept-downstream.tests.cjs <repository> <output.json.concepts.json>
dotnet Tests/bin/Release/net10.0/JobSearchManager.Tests.dll --json-concept-evaluation <phase1-archive> <new-output-directory>
dotnet Tests/bin/Release/net10.0/JobSearchManager.Tests.dll --json-concept-cases <case-directory>
python migration/concept-rules/phase2/schema-tests.py <repository> --cases <case-directory>
python migration/concept-rules/phase2/startup-check.py <Tests.dll> <case-directory> <results.json>
```

Use the existing closed online backup, never a live WAL database. Parity creates a
temporary writable copy, compares all 85 presence decisions and full classifications,
and reuses the Phase 1 test-only trace adapter for exclusions/context outcomes. Rule
fingerprints are deliberately compared separately; timestamps and authority identities
are normalized only in the semantic comparison. Evidence and rule ordering are not
normalized away. The frozen Phase 1 engine/fixtures/export are unchanged.

Evaluation uses the existing deterministic curated and holdout services with the two
classifiers. The archived rule inventory supplies attribution IDs to both evaluators;
JSON detection does not read SQLite. Run IDs, evaluation times and authority identities
are excluded from metric equality, while all per-rule attribution, concept confusion
matrices, denominators and metrics must match exactly. Holdout inputs and existing
labels/prompts are copied into new directories; no models run and historical reports
are never overwritten. Ledger/status output is confined to a copied DB and new reports.

## Isolated validation image

`Dockerfile.validation` consumes a private build context containing only published
test/migration output under `application/`. It is separate from normal Compose and
the normal Dockerfile. It can run the same parity commands against read-only mounted
archive inputs and a separate writable results mount.

The optional diagnostic command `--json-concept-host <candidate> <loopback-url>` loads
and validates JSON **before creating the host**. It exposes only `/healthz`, `/version`
and `/migration/classify`. It has no normal Admin, account, provider or persistence
services. It never runs in the production container. Normal runtime checks separately
exercise accounts/workspaces/workflow against isolated copied data with SQLite active;
`--json-concept-dto` compares JSON predictions and actual list/detail presentations
without writing candidate classifications back to those caches.

Stop here: no production JSON authority switch, Admin retirement, SQLite removal,
cache-identity migration, commit, push or production deployment belongs to Phase 2.
