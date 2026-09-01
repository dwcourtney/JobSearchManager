# Semantic Job Fit classifier

Job Fit uses the internal Qwen classifier for all 85 canonical concepts in
`JobConceptCatalog.json`. The supported model is
`qwen3:4b-instruct-2507-q4_K_M`, pinned by digest in both the adapter and JSM client.
Ollama and the adapter are reachable only on the internal Docker classifier network.

The current taxonomy is version 9 with fingerprint
`514ed1c8c644d1eec426b5fdcf4d5a2c447aa61ce5572ae70b2d03fc3815a049`. The production prompt is
`job-fit-85-zero-shot-v1` with hash
`e08076fa0cd5c106818770b3b588385f93debce79cca1034045abdfc4fb23568`. The model digest is
`sha256:0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0`.

Classification does not block source ingestion. Each active workspace schedules a bounded
background pass ordered by active/new jobs and newest posting date. Identical classification
fingerprints share an in-process result; persisted results are reused only while posting content,
taxonomy, model digest, prompt, and generation inputs remain unchanged. A classifier outage leaves
valid cached results intact and otherwise reports `pending` or `unavailable` without affecting
browsing or deterministic salary, clearance, credential, citizenship, location, or account logic.

Each persisted result includes the posting content hash, taxonomy version and fingerprint, model
identity and digest, prompt version and hash, classification timestamp, classification fingerprint,
and all 85 boolean predictions. An administrator may inspect or start the current-source backfill
through `/api/admin/classifier/backfill/status` and `/api/admin/classifier/backfill`.

The classification fingerprint covers posting content, taxonomy version and fingerprint, model ID,
tag and digest, prompt version and hash, temperature, seed, context length, and output-token limit.
Changing any of those inputs invalidates the cached semantic result. A backfill skips current results
and therefore does not reclassify unchanged jobs unnecessarily.

## Acceptance evidence

The reusable acceptance set is `SemanticClassifierValidationFixtures.json`; it references every
canonical concept across responsibility shape, role type, technical domain, work arrangement, and
work environment. It contains explicit positives, overlap cases, and hard negatives. Run it against
an isolated candidate adapter with:

```text
python3 scripts/validate-semantic-classifier.py --url http://job-classifier:8081
```

The initial GTX 1070 validation on 2026-09-01 returned exactly 85 unique booleans for all 21 cases.
It passed 148 of 155 labeled checks (95.5%) and every hard-negative check. The seven mismatches were
conservative false negatives; no prompt exceptions or fixture-specific rules were added. The
general acceptance gate is at least 90% labeled accuracy, no more than 5% hard-negative false
positives, and coverage references for all 85 canonical IDs.
