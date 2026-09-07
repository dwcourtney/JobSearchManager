# Step 3: exact b2c269 snapshot candidate evaluation

Candidate 1.0.1 is unchanged, inactive, and ready for user evaluation in Step 4. Import `candidate-result.json` using Choose File. The package is bound to the complete b2c269cc766fe41cd82ed594f8bcbff04f940103361dbff172b7a480f051db0c snapshot, not the earlier ce2297 snapshot. No import, approval, rule activation, commit, push or deployment was performed.

All snapshot content hashes, the manifest identity, the baseline rule hash and evaluation-context fingerprint were verified. All 29 exact saved human decisions/revisions match the supplied immutable human snapshot. Baseline historical 1.0.0 is preserved. This pass adds only evaluation artifacts; no additional rules or source changes were necessary.

## Candidate and safety rationale

Seven additive KEEP guards already present in 1.0.1 address electronic design, test systems, application integration, software product work, solution sales, telecom infrastructure and IT architecture. Each requires a title signal and two corroborating body signals. Existing predicates and rejection rules are semantically unchanged. See ../human-reviewed-1.0.1/README.md for each predicate/rule rationale and collateral risks; this evaluation reconfirmed that exact candidate hash. Employer names, posting IDs and gold offsets are not exceptions.

| Measure | Baseline 1.0.0 | Candidate 1.0.1 |
|---|---:|---:|
| Human KEEP recall (7 selected development cases) | 0/7 | 7/7 |
| Human REJECT retained | 22/22 | 22/22 |
| Frozen binary KEEP recall | 99.5757% | 99.7171% |
| Described KEEP recall | 99.0153% | 99.3435% |
| Title-only KEEP recall | 100% | 100% |
| Provisional binary false rejects | 9 | 6 |
| Binary rejection (2669) | 42 (1.5736%) | 39 (1.4612%) |
| Old-cache rejection (1503) | 208 (13.8390%) | 203 (13.5063%) |
| Historical live rejection (2205) | 237 (10.7483%) | 228 (10.3401%) |
| Exact-snapshot Leidos cache (275) | 23 (8.3636%) | 23 (8.3636%) |

Leidos: 252 KEEP / 23 REJECT / 0 UNDETERMINED under both versions, all 275 description-backed. All 23 exported rejected-job input fingerprints match the read-only cache replay; total and decision counts match the immutable snapshot. The export omits individual KEEP input fingerprints, so equivalence of every KEEP to the historical export cannot independently be proven. Cache bytes and replay-input hashes are retained in manifest.json.

All 2205 historical input fingerprints match the archived observations. Original stored historical counts (1967 KEEP /236 REJECT /2 UNDETERMINED) differ from replay because two historical timeouts resolve under BOTH versions: Parsons R182565 to REJECT and Leidos R-00172727 to KEEP. These are not candidate changes. No timeout/fail-open occurred in this replay.

## Every changed decision and remaining risk

changed-decisions.csv and changed-decisions.json enumerate all 19 changed evaluation rows: ten frozen and nine historical-live, all REJECT to KEEP. There are zero current Leidos changes and no KEEP to REJECT changes. Repeated jobs across cohorts remain explicit and are not independent examples. human-comparison.json contains all 29 exact saved decisions and before/after evidence.

Six provisional KEEP false rejects remain: Civil Site Lead, Power Delivery; Cable Test Technician-2nd shift; NDT Technician - UT & RT; McMurdo Seasonal Instrument Technician; Substation Structural Designer; Structural Engineer - Power Delivery. Human NDT/McMurdo judgments are REJECT in the narrower Technology scope; frozen labels were preserved, not silently relabeled. Broader civil/cable boundaries need separate human evidence.

The seven reviewed KEEP corrections are development evidence, not proof of population safety. Product/sales boilerplate and technical-adjacent laboratory duties can cause additional false KEEP. Two unreviewed ServiceNow solution-sales siblings also survive; their corroborated similar duties support continuation, but they are correlated cases, not new human labels. Frozen ambiguous labels stay ambiguous. No blinded holdout or inference was used. This safety candidate gives up 0.33 percentage points old-cache rejection and 0.41 points historical-live rejection. Shadow has no demonstrated workload savings and KEEP is not itself a Technology tag.

## Reproduction and validation

Run the existing scripts/evaluate-cheap-rules.py with --candidate CheapTriage/rulesets/1.0.1.json --require-frozen-parity and a new --output directory. Raw frozen source provenance is unchanged in CheapTriage/evaluation-context.json and the prior candidate manifest. This folder retains the exact replay/export/compare scripts used. They expect the documented Windows checkout, scratch step3-b2c269 and earlier reviewed-rules-candidate scratch inputs; replay.py rechecks prior source hashes. Current input JSONL and historical input JSONL are also retained on curiosity at the manifest's linuxInputs directory. Do not fetch providers or substitute a newer snapshot.

For Linux parity use the existing jsm:smoke-uncommitted-0faf18d3ca8b image with --cheap-triage evaluate, a read-only /inputs mount, --network none, --read-only, --cap-drop ALL and --tmpfs /tmp. Compare all JSON decision/evidence objects to Windows. Package using scripts/package-cheap-triage-candidate.py with this exact snapshot, comparison.json, changed-decisions.json, human-comparison.json, the unchanged ruleset and --validation PASS.

Fresh validation: 140 .NET tests; full source/Admin/theme/JS/UI tests; repository audit; frozen parity and all regression cohorts; Linux/Windows full output parity; Trivy current JSM image/source/policy gates; whitespace checks. Prior full other-image/build/runtime validation remains historical evidence, not a newly run exact-commit CI claim. No code or image contents were changed in this Step 3 pass. See validation.json and manifest.json for log hashes and artifact identities.

Only this new report directory was added by this pass. Existing uncommitted UI/research/rules work remains intact. Main and origin/main remain equal; no commits were made.
