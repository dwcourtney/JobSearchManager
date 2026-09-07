# No further rule change justified by review snapshot e5a8c89d

Recommendation: retain the exact released Cheap Triage ruleset 1.0.1. No 1.0.2 candidate was created; no rule, engine, configuration, historical label, UI or runtime changes were made in this investigation.

## Why this is a repeat, not seven new false rejects

The human-review file is byte-identical to the earlier b2c269 review snapshot: SHA-256 5010d49f71c996d4591f0aac9be2be55326e882f3ea22f52e919a441c79952f2. The original queue retains its historical machine REJECT decisions for provenance. Those snapshots do not describe the current 1.0.1 engine results. Fresh replay already matches every human decision: 7 KEEP and 22 REJECT.

The current uncommitted smoke build cannot satisfy the previous committed-release receipt. Its existing workflow therefore allowed another preparation request against 1.0.1. This does not establish a new rules defect or require repeating the release. Workflow semantics were not changed to accommodate a no-change result.

## Verified identities

- Bundle: e5a8c89ddebabc0587b12a8fe6483c86c921c7a69af0bc35d4ba79c77fa221e6
- Human-review SHA-256: 5010d49f71c996d4591f0aac9be2be55326e882f3ea22f52e919a441c79952f2
- Live-shadow SHA-256: 9c6fae72aa10c8607400385751bfea6acdba3ddaebcc44563082365f7e69262b
- Ruleset 1.0.1 SHA-256: ecb3621ce7157f044fa68dd1b2653052b8102549ecdf0e52c00fd2f1de10b1d1
- Queue human_review_v1 / 2c66bac39adcc36b69090927cf0c244ca3f1a2eb9c52288e0e0ab38e01542b61
- Source manifest: 0d0f75269554b9e0cc0ba61f9bb9cea283f5a84a8e4a877ed79cdfdf6e250c7e

All three snapshot files were copied with the established sync script and verified against the supplied hashes. The evaluation context matches the current rules fingerprint. Historical 1.0.0 and 1.0.1 bytes remain intact.

## Fresh baseline / retain-current comparison

| Measure | Current 1.0.1 | Retain 1.0.1 | Change |
|---|---:|---:|---:|
| Human KEEP matched | 7/7 | 7/7 | 0 |
| Human REJECT matched | 22/22 | 22/22 | 0 |
| Frozen binary KEEP recall (2,669 postings) | 99.72% | 99.72% | 0 |
| Described KEEP recall (1,159 postings) | 99.34% | 99.34% | 0 |
| Title-only KEEP recall (1,510 postings) | 100% | 100% | 0 |
| Provisional false rejects | 6 | 6 | 0 |
| Frozen binary rejection | 39/2,669 (1.46%) | 39/2,669 | 0 |
| Old-cache rejection | 203/1,503 (13.51%) | 203/1,503 | 0 |
| Historical live rejection | 228/2,205 (10.34%) | 228/2,205 | 0 |
| Exact current-cache rejection | 23/275 (8.36%) | 23/275 | 0 |

Historical replay: 1,977 KEEP / 228 REJECT / 0 fail-open. Exact Leidos snapshot replay: 252 KEEP / 23 REJECT / 0 fail-open; all 275 described. All 23 exported rejects match full decision/evidence/input fingerprints. Snapshot reports no stale observations. Historical counts are replay of cached inputs, not newly observed traffic.

Additional frozen development, leakage-audit, leakage-delta-audit and fixtures (166 / 196 / 41 / 60 rows) retain 100% labeled KEEP recall. These reused datasets are correlated development evidence, not independent safety certification. No blinded holdout was read or tuned against.

Every new changed decision: none. changed-decisions.json is an empty array. Rule IDs/predicates added, changed or removed: none.

## Residual risk and label limitations

The six unchanged provisional KEEP labels rejected by the current engine are:

- Civil Site Lead, Power Delivery
- Cable Test Technician-2nd shift
- Nondestructive Test (NDT) Technician - UT & RT
- McMurdo Seasonal Instrument Technician
- Substation Structural Designer
- Structural Engineer - Power Delivery

The civil/structural examples raise a scope mismatch with the user's narrower Technology rubric; their provisional labels remain untouched. Cable assembly/testing, aerospace NDT inspection, and scientific instrument calibration/repair are boundary cases. Engineering tools or a technology employer do not alone establish Technology primary duties. Instrument/electronics work can also be legitimately adjacent. The supplied 29 reviews do not adjudicate these particular remaining examples; a future targeted human pass is appropriate before proposing protections or broad exclusions. No new labels were created and no occupation-family regex was added to force a metric improvement.

KEEP remains continuation advice, not proof of Technology membership. Shadow rejects do not discard jobs or measure saved LLM calls.

## Validation and reproducibility

- Fresh C# frozen run: scripts/evaluate-cheap-rules.py --baseline CheapTriage/rulesets/1.0.1.json --candidate CheapTriage/rulesets/1.0.1.json --output <new-directory>.
- Frozen source hashes verified against supervised_v2/splits.json by the existing runner.
- The runner's original electrical-safe reference is 1.0.0. Its ten differences are exactly the previously approved 1.0.1 frozen changes, not regressions. comparison.json preserves these as legacyElectricalSafeDifferences and explicitly uses approved 1.0.1 for the zero-mismatch current-baseline comparison. The raw runner output remains in the scratch frozen/ directory.
- Cached historical inputs: SHA-256 74e179387458058789620a3d46d8989bc19b0451f87ba4e422e8394d9159abdc; checked against the preserved human-reviewed-1.0.1 manifest.
- Current-cache inputs: SHA-256 9e1e784fcb1684b1a0557335a70758728734d27c435e809c750a3548b4f86101; full exported reject evidence and aggregate counts verified against this exact snapshot.
- Windows/Linux full result/evidence parity: PASS for 4,701 corpus/cache + 2,205 historical + 275 snapshot rows. Overlapping rows are not independent observations.
- Linux used existing image jsm:smoke-uncommitted-cb1e7dba27af with --network none, --read-only, and isolated read-only input mounts; no production data mount, provider download, inference, restart or deployment.
- All 140 deterministic .NET tests: PASS. Complete current UI/source/theme suite: PASS.
- Existing Trivy gates on that unchanged evaluation image: PASS. /healthz Healthy.
- git diff --check: PASS.
- Detailed raw runs, logs and reproduction scripts: D:/David/Documents/ChatGPT/Job Search Manager/step3-e5a8/; Linux copies: /home/codex/jsm-lab/experiments/step3-e5a8/.

## Requested candidate-result.json

Generated using scripts/package-cheap-triage-candidate.py with the exact snapshot, unchanged 1.0.1 rules, paired results, all 29 human records and zero changed decisions. It is an EVALUATION-ONLY no-change package. PASS describes completed evaluation; it is not an approval or a new release candidate.

Do not import or approve this package: the existing Admin importer deliberately requires a different candidate version. It cannot represent NO_CHANGE_REQUIRED and will reject a same-version result. We did not fabricate a 1.0.2 version, weaken the importer, or alter the package to conceal that limitation. No further review, approval or release is needed for these already-addressed decisions.
