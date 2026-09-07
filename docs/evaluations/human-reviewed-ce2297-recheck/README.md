# Conservative candidate verification against immutable review ce2297

Recommendation: retain the existing **inactive 1.0.1** candidate for review. No new rule version or engine edit is justified by this snapshot. No existing files were modified in this task; only this verification report directory was added. No commit, push, deployment, activation, provider download, model inference, human adjudication or historical relabeling occurred.

## Exact provenance

The three supplied snapshot hashes, baseline rules hash, queue hash and source manifest hash all verified. `manifest.json` records their full SHA-256 identities plus frozen and cached replay inputs. Every saved human decision, note, revision and posting-evidence object equals the earlier candidate's human evidence. The newer export packages that same evidence immutably; it is not new independent evaluation data.

Candidate: `CheapTriage/rulesets/1.0.1.json`, SHA-256 `ecb3621ce7157f044fa68dd1b2653052b8102549ecdf0e52c00fd2f1de10b1d1`.
Baseline: `CheapTriage/rulesets/1.0.0.json`, SHA-256 `269be7264e56f641723c254910d026d20687f5d61aaa39d967c5d52e4bc51983`.
The packaged evaluation context still matches baseline 1.0.0. Active default and deployed Shadow rules remain 1.0.0; existing uncommitted UI/export work and research remain intact.

## Candidate and safety finding

Corroborated occupational REJECT rules lacked KEEP protection for seven human-confirmed Technology roles. The existing candidate adds title-plus-two-body-signal guards: `keep-reviewed-electronic-design`, `keep-reviewed-test-systems`, `keep-reviewed-application-integration`, `keep-reviewed-software-product`, `keep-reviewed-solution-sales`, `keep-reviewed-telecom-infrastructure`, and `keep-reviewed-it-architecture`. All pre-existing rules and predicates compare equal; all new rules return KEEP. No employer, requisition ID or gold evidence-offset exceptions were added. The earlier [candidate report](../human-reviewed-1.0.1/README.md) contains each predicate rationale and collateral risk.

The human decisions expressly retain software product management, technical solution sales and telecommunications outside-plant leadership, while rejecting routine avionics maintenance, NDT inspection, civil design and manufacturing occupations. The candidate respects those decisions without expanding all engineering/technician roles. Boilerplate can still fool software/product/sales guards; new false KEEP risk remains. KEEP is continuation advice, not a positive Technology label.

## Fresh paired results

| Cohort | Baseline 1.0.0 | Candidate 1.0.1 |
|---|---:|---:|
| Human KEEP correctly retained | 0/7 | 7/7 |
| Human REJECT correctly retained | 22/22 | 22/22 |
| Frozen combined KEEP recall | 99.5757%, 9 false rejects | 99.7171%, 6 false rejects |
| Frozen described KEEP recall | 99.0153%, 9 false rejects | 99.3435%, 6 false rejects |
| Frozen title-only KEEP recall | 100%, 0 false rejects | 100%, 0 false rejects |
| Old-cache rejection | 208/1503 (13.8390%) | 203/1503 (13.5063%) |
| Historical live replay rejection | 237/2205 (10.7483%) | 228/2205 (10.3401%) |
| Snapshot Parsons cache replay rejection | 76/268 (28.3582%) | 76/268 (28.3582%) |

Frozen baseline parity: zero mismatches. All development/leakage/leakage-delta/fixture decisions unchanged. No fail-open outcomes occurred in these reruns. Every changed row is REJECT -> KEEP: ten frozen rows plus nine historical-live rows; correlated/repeated postings mean these are not 19 independent examples. All 19 are exported in [CSV](changed-decisions.csv) and [JSON with full before/after evidence](changed-decisions.json). No changed decision was omitted. No new KEEP -> REJECT occurred.

Historical stored counts (236 REJECT, 2 UNDETERMINED) are not paired replay counts: Parsons R182565 resolves to REJECT and Leidos R-00172727 to KEEP under both versions. The immutable current snapshot has 75 REJECT and one UNDETERMINED, while both cache replays have 76 REJECT. This timeout variation is not a rule delta. All 75 exported reject input fingerprints match the cached replay; all 2205 selected historical fingerprints match after applying the original newest-refresh/description/cache-key deduplication. The snapshot contains only rejected-job details and aggregate counts, so the full preserved cache is used for replay rather than inventing missing descriptions or KEEP rows.

Six frozen provisional KEEP false rejects remain: Civil Site Lead, Power Delivery; Cable Test Technician-2nd shift; NDT Technician - UT & RT; McMurdo Seasonal Instrument Technician; Substation Structural Designer; Structural Engineer - Power Delivery. NDT and McMurdo are human REJECT under the current scope; historical provisional labels remain unchanged. The other boundaries need human review, not silently amended training truth.

This is selected development evidence, not a blinded holdout or population recall estimate. No holdout was accessed. The safety improvement costs 0.33 percentage points of old-cache rejection and 0.41 points of historical-live rejection. Shadow currently saves no downstream calls; these are potential volumes only. Retain Shadow and seek broader independent human review before any production gating proposal.

## Validation and reproduction

Fresh validation: all 140 .NET tests; full source/Admin/Human Review/theme/UI suite; frozen evaluation with `--require-frozen-parity`; actual C# CLI cache replay on Windows and network-isolated Linux Docker. Both versions' complete decisions and evidence match across Windows/Linux for 2205 historical and 268 snapshot-cache jobs. The replay utility initially used the wrong observation-property nesting and omitted archive deduplication; both assertions were corrected to the documented original selection before reporting passing results. Source data and rules were not changed to obtain parity.

From the checkout, build Release, then run `python scripts/evaluate-cheap-rules.py --candidate CheapTriage/rulesets/1.0.1.json --output <fresh-output> --require-frozen-parity`. Use the preserved replay/preparation scripts described in the earlier candidate report; verify the input hashes in this manifest. Exact full cached inputs and Linux outputs for this verification remain at `/home/codex/jsm-lab/experiments/snapshot-ce2297-recheck/`. CLI: `dotnet bin/Release/net10.0/JobSearchManager.dll --cheap-triage evaluate <ruleset> <input.jsonl>`. Read files only; do not reconcile observations into production. Linux used existing image `jsm:smoke-uncommitted-bc9e1c60a95d`, `--network none --read-only`, read-only inputs, and no production data mounts. No running service was restarted.

Security/container gates for these identical app/rule/image bytes passed during the preceding smoke work and earlier candidate evaluation; this recheck does not claim a new full security run or exact-commit CI. Git whitespace integrity is checked separately. All existing uncommitted changes remain uncommitted.
