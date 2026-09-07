# Human-reviewed Technology safety candidate 1.0.1

Candidate SHA-256: `ecb3621ce7157f044fa68dd1b2653052b8102549ecdf0e52c00fd2f1de10b1d1`.
Baseline 1.0.0 SHA-256: `269be7264e56f641723c254910d026d20687f5d61aaa39d967c5d52e4bc51983`.

**Recommendation: retain this inactive candidate for review. Do not enable Active gating.** No active rule path, default, engine, UI, Job Fit behavior, human decisions, historical labels or deployed state was changed by this investigation. Earlier uncommitted UI work remains intact.

## Finding and scope

The reject families were corroborated, but seven Technology occupations lacked an appropriate KEEP guard. Protecting every technician, engineering, business, finance or sales title would contradict the human decisions. This candidate adds seven generic guards, each requiring a title signal plus two body signals; no employer, product brand, requisition ID or gold evidence offset is matched. Existing predicate definitions and reject rules remain byte-semantically unchanged; all additions return KEEP before reject rules. No engine extension is needed.

| Added KEEP rule (`keep-reviewed-` prefix) | Title + corroborating body evidence | Main collateral risk |
|---|---|---|
| electronic-design | ECAD/PCB/electronics + circuit-design object + layout/routing/schematic duties | Hardware design is intentionally included; routine assembly may mention these words |
| test-systems | Test/evaluation/lab + electrical drawings/instrumentation + systems integration/design | Physical test laboratories with incidental integration may survive |
| application-integration | Project manager/support/analyst/admin + file transfers/data flows/dependencies + integration errors/configuration/triage | Some business administrators with incidental systems work may survive |
| software-product | Product manager + software/AI-native/SaaS + product strategy/customer deployment/implementation | Broad software-company boilerplate could protect nontechnical product work |
| solution-sales | Solution sales + AI/software platform/workflow automation + demos/technical questions/discovery | More sales may KEEP; this is consistent with the human software solution-sales judgment |
| telecom-infrastructure | OSP/outside-plant/telecommunications + telecommunications infrastructure + design | Civil telecom infrastructure intentionally survives, not all civil engineering |
| it-architecture | Digital Technology/program architecture + IT/engineering/program architects + requirements/Agile/proposal duties | Business proposal support with real IT context may survive |

These are cautious safety exceptions, not new occupational labels for all KEEP jobs. Word presence is still an imperfect proxy for primary duties; no claim of universal precision is made.

## Evidence and provenance

- Human snapshot: 29 reviewed, 7 KEEP / 22 REJECT / 0 AMBIGUOUS. Exact decisions/notes/timestamps and queue hash are in `human-snapshot.json`. All originally machine REJECT. This is a selected error-review set used for development, not an independent holdout.
- Frozen baseline: 2,669 provisional binary labels, 529 ambiguous examples, 1,503 old-cache rows, 166 development, 196 leakage audit, 41 leakage delta, 60 fixtures. Source hashes verified against frozen split metadata. No blinded production holdout or model inference was used. Historical labels are not silently converted into the narrower human Technology definition.
- Historical live archive: 14 source caches / 11 employers, 2,284 cache instances, 2,205 deduplicated jobs. All input-cache and artifact hashes match the original manifest. Selection repeats newest source refresh, description availability, descending cache key. All 2,205 engine posting fingerprints match the archived observations.
- Fresh read-only Parsons cache: 268 jobs copied from the existing cache, no provider fetch. Input hash is recorded in `manifest.json`.
- Full changed-decision records: `changed-decisions.csv` and `changed-decisions.json`. They contain every changed evaluation row, employer/title, stable/evaluation ID, old/new rules and reasons, matched predicates/evidence, human decision/note where an exact stable ID exists. Legacy old-cache identifiers remain explicitly labeled rather than fabricating stable IDs.

## Before/after results

| Dataset/cohort | Baseline | Candidate |
|---|---:|---:|
| Human KEEP recall (development evidence, 7 KEEP) | 0/7 | 7/7 |
| Human REJECT retained (22 REJECT) | 22/22 | 22/22 |
| Frozen combined KEEP recall | 99.5757% (9 false rejects) | 99.7171% (6 false rejects) |
| Frozen described KEEP recall | 99.0153% (9 false rejects) | 99.3435% (6 false rejects) |
| Frozen title-only KEEP recall | 100% (0 false rejects) | 100% (0 false rejects) |
| Frozen binary rejection | 42/2669, 1.5736% | 39/2669, 1.4612% |
| Frozen ambiguous rejection (not scored as binary truth) | 10/529 | 8/529 |
| Old-cache rejection | 208/1503, 13.8390% | 203/1503, 13.5063% |
| Historical live replay rejection | 237/2205, 10.7483% | 228/2205, 10.3401% |
| Historical live described rejection | 237/1755, 13.5043% | 228/1755, 12.9915% |
| Historical live title-only rejection | 0/450 | 0/450 |
| Fresh Parsons replay rejection | 76/268 | 76/268 |

Frozen development, leakage, leakage-delta and fixtures have identical decisions and 100% KEEP recall before/after. Frozen baseline parity: zero mismatches. No KEEP -> REJECT changes anywhere. No new fail-open result in live replay.

Important timeout distinction: archived live results were 1,967 KEEP / 236 REJECT / 2 UNDETERMINED. `parsons:R182565` and `leidos:R-00172727` were historical regex timeouts. They resolve to REJECT and KEEP respectively under **both** deterministic reruns, with zero fail-open results. This is timing variability, not a candidate rule effect; historical observations remain unchanged. Fresh Parsons still has 75 stored REJECT and one stored UNDETERMINED, while both fresh CLI runs yield 76 rejects. Do not compare 236 to 228 as the candidate delta: the paired reduction is exactly nine.

The nine changed historical-live IDs are: `northrop-grumman:R10245576`, `servicenow:JB0074456`, `servicenow:JB0074533`, `servicenow:JB0074535`, `servicenow:JB0074536`, `aecom:J10155853`, `boeing:JR2026518584`, `rtx:01865916`, `rtx:01868753`. The two unreviewed ServiceNow requisitions have substantially the same solution-sales duties as the reviewed KEEP: executive discovery, demonstrations/technical questions, enterprise software/SaaS. They are plausible KEEP but correlated examples, not independent proof or newly assigned human labels.

The ten frozen changed rows are the test-lab, ECAD, enterprise-integration, software-product and IT-architecture jobs appearing in corpus and old cache. Two corpus labels remain AMBIGUOUS; three are historical KEEP. All ten are individually exported, including the five legacy IDs.

Six provisional frozen KEEP false rejects remain: Civil Site Lead, Power Delivery; Cable Test Technician-2nd shift; NDT Technician - UT & RT; McMurdo Seasonal Instrument Technician; Substation Structural Designer; Structural Engineer - Power Delivery. NDT and McMurdo have human REJECT decisions under the new scope, but their frozen labels were deliberately preserved. The remaining civil and cable-test cases need separate human scope review; they were not forced into an outcome.

## Tradeoff and safety

The candidate improves safety at the cost of five old-cache and nine historical-live additional KEEP decisions (0.33 and 0.41 percentage points). No new leaked human REJECT was observed. Correlated samples and machine-label ambiguity limit confidence; 7/7 is not evidence of population-level 99% recall. Guards can still be fooled by boilerplate, especially software product and solution-sales language. Broader unseen-employer human review remains appropriate before adoption. Shadow saves no calls; these are potential rejection rates, not measured workload savings.

## Reproduction

Build Release first. Run `python scripts/evaluate-cheap-rules.py --candidate CheapTriage/rulesets/1.0.1.json --output <new-output-directory> --require-frozen-parity` from the repository. Do not point output into preserved research.

The accompanying preparation and replay scripts preserve the recorded data selection and run the actual `--cheap-triage evaluate` CLI. Original input JSON files remain under `/home/codex/jsm-lab/experiments/live-shadow-audit-20260906/<cache>/input.json`. The exact selected-input bundle and fresh Parsons snapshot are retained under `/home/codex/jsm-lab/experiments/human-reviewed-rules-1.0.1-20260906/`; hashes are in the manifest. Copy them into a scratch `reviewed-rules-candidate/` folder, along with `human-snapshot.json`; run `prepare-candidate.py`, `evaluate-live.py`, and `evaluate-current.py` from its parent. Scripts use the documented Windows checkout path. Preparation writes a scratch candidate, never the active ruleset. The repository candidate file is the authoritative proposed rule artifact (currently uncommitted).

Validation details are recorded in `validation.json`. Linux image-validation reuses the repository's source/policy security gates and complete image/health/identity section of `ci-validate.sh` against an isolated source copy with the inactive candidate. The identity is a candidate-content test identifier, not a Git commit. No dirty-tree check was claimed as exact-commit CI. No security gate was weakened. Production containers and runtime rules were not changed.
