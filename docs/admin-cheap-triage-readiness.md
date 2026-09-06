# Admin architecture cleanup and cheap-triage readiness — 2026-09-06

Decision: **do not implement or activate a production gate in this change**. Clean up normal Admin exposure, retain Job Fit maintenance/diagnostics, and expose honest offline cheap-triage status. No new labels, rules, training, generative inference, commit, push or deployment is part of this review.

## Admin inventory and resulting navigation

| Existing feature | Classification | Result |
|---|---|---|
| Overview / administrator status | Current production diagnostics | Retain server-authorized account/operational status. |
| RegEx Rules: search, lifecycle/provenance, match counters, stale-cache reconciliation, verify/apply | Current production maintenance | Rename **Job Fit Rules**. These are SQLite semantic/concept rules, not cheap-reject occupation rules. |
| RegEx Rules: curated benchmark | Current production diagnostics | Retain known-case concept-detection regression. |
| Evaluation -> RegEx: curated benchmark and AI-adjudicated frozen holdout | Current production diagnostics | Rename **Job Fit Evaluation**; retain separate datasets and non-human-label limitations. The holdout measures 85-concept detection, not KEEP/REJECT. No holdout was run or used for tuning here. |
| Update RegEx Rules | Current cheap-triage maintenance preparation | Move to **Cheap Triage**, with separate version/hash/frozen-evidence status. Generates instructions only. |
| Evaluation -> LLM: run controls, GTX/RTX comparison, timing/resource metrics and polling | Historical/research-only | Remove browser rendering, event handlers, polling/state, obsolete CSS and normal-web run/status APIs. Keep backend research implementations, CLI, reports, frozen predictions, hardware manifests and scripts. |
| Evaluation -> Triage: earlier two-stage prototype, progress, example/throughput panels | Historical/research-only | Remove browser controls/rendering/polling and normal-web run/status APIs. Keep `TriageEvaluation.cs`, `--regex-maintenance evaluate-triage`/reference CLI and all research inputs/results. |
| Persisted LLM/Triage browser subtab selection | Obsolete | Remove restore logic so old session preferences cannot expose retired workflows. |

The normal `/api/admin/evaluations` ledger now loads only Job Fit evaluation artifacts, without constructing historical LLM/triage services. Their normal-host DI registrations are removed; explicit research CLI constructors remain. No production Qwen client, model, deep-analysis method, taxonomy, Job Fit cache or score implementation is removed. Deleted UI source remains reproducible from signed commit `387121ee7dced0359a5547c0b8ef52e81a10d5e9`; no research copy is needed in shipped assets.

## Actual pipeline and workload boundary

`CheapRejectRules` is an immutable startup-loaded ruleset used by the offline `--cheap-triage evaluate` command and read-only Admin maintenance service. There are **no calls from JobCatalog, source refresh, Job Fit classification or cache reconciliation**. The previous phrase “shadow-only” describes non-discard intent, not an implemented live Shadow collector. No live cheap-triage decisions, mode setting, avoided-work counters or timestamps exist. The UI therefore says **Offline evaluation only / Not active**, rather than displaying fabricated zero counts or implying production Shadow telemetry.

`JobCatalog.GetJobDetailAsync` and the semantic backfill worker call `SemanticClassificationService.ClassifyAsync`; `ClassifyCoreAsync` executes the local `RegexSemanticClassifier` and persists/reuses current 85-concept classifications. Current cache entries bypass repeated classification. All postings continue through this existing path regardless of what the offline cheap rules would decide.

`JobCatalog.DeepAnalyzeWithQwenAsync`, its client/protocol and model infrastructure remain available in code. Normal Jobs exposes no deep-analysis execution endpoint. Qwen inference is used by separately invoked research/maintenance entry points, not automatically for every listed job. Removing historical research run controls does not change Job Fit semantics. Do not prefilter the frozen blind benchmark prediction population: that would invalidate its evaluation protocol.

**Current avoided Qwen work from activating a live prefilter would be zero in normal Jobs**, because that automatic inference workload does not exist. Gating existing deterministic Job Fit would instead remove useful concept detection and could change score freshness/availability for viable jobs. The old-cache rejection fraction is neither measured CPU savings nor proof of a current 13.84% LLM saving. If an explicitly scoped future expensive executor processed every eligible cache posting once, 208/1,503 is the raw maximum candidate fraction skipped by this ruleset before safety exclusions, retries and existing-cache reuse; the safe achievable fraction is unknown and likely lower.

## Frozen rules and metrics

Ruleset `1.0.0`, SHA-256 `269be7264e56f641723c254910d026d20687f5d61aaa39d967c5d52e4bc51983`, remains byte-identical. The same `supervised_v2` corpus/cache and comparison script are used; no blinded production holdout is opened.

| Cohort | KEEP recall | False rejects | Total rejected |
|---|---:|---:|---:|
| Binary corpus, 2,669 | 99.5757% (2,112/2,121) | 9 | 42 / 2,669 (1.5736%) |
| Described binary, 1,159 | 99.0153% (905/914) | 9 | 42 / 1,159 (3.6238%) |
| Title-only binary, 1,510 | 100% (1,207/1,207) | 0 | 0 |
| AMBIGUOUS, 529 | Not binary accuracy | Not scored | 10 / 529 |
| Old cache, 1,503 | Unlabeled prevalence; no overall accuracy claim | Unknown | 208 / 1,503 (13.8390%) |
| Earlier balanced development, 166 | 100% (85/85) | 0 | 56 / 166 (33.7349%) |

Only 33 of the 42 binary rejects agree with the provisional REJECT labels (78.57% reject precision). Across the full 3,198 corpus, 52 are rejected: 33 provisional REJECT, nine KEEP, ten AMBIGUOUS. All 1,886 description-missing records survive. This reflects affirmative duty corroboration, not proof that title-only labels are reliable. Body arrival can change a prior KEEP and must invalidate a future cached decision. Timeouts/oversized input and no corroboration fail open. The engine itself returns binary decisions; it does not recognize adjudicator uncertainty. In particular, it currently rejects ten machine-labeled AMBIGUOUS examples, so the desired undetermined-always-continues contract has not been established.

## Nine known false rejects: duty review and production impact

No labels were changed during this review. “False reject” below means disagreement with the frozen provisional KEEP label, not a claim of independently verified suitability. Every row would lose the specifically gated downstream analysis under naive Active mode, even if retained/searchable in the app.

| Posting | ID | Trigger | Duty review / impact |
|---|---|---|---|
| Civil Site Lead, Power Delivery | `9d0215801cd4ecf4c649e049` | civil + construction / stormwater / grading | Primarily civil site design for substations. Genuine scope-boundary uncertainty; power-industry wording alone does not make it software work. Human scope review required; do not silently relabel to improve recall. |
| Cable Test Technician-2nd shift | `5b5f3d0cb938b7f44d42ef89` | technician + manufacturing | Electrical schematics, signal/connectivity troubleshooting and cable repair. Adjacent technical duties would lose analysis. |
| Nondestructive Test (NDT) Technician - UT & RT | `72005173e60e4cf83cee1699` | technician + repairs | Specialized test setup, automated/digital inspection and statistical analysis. Technical-analysis versus inspection-occupation boundary needs review. |
| Associate Test and Evaluation Laboratory Technician | `1e8d2877f53a6d11c2e23f34` | technician + materials | Designs test systems/components/software and supports systems integration and troubleshooting. Strong evidence of plausible technical work missed by the guard. |
| McMurdo Seasonal Instrument Technician | `18452bbf4534bab7c96f42da` | technician + repairs | Instrument installation, calibration, troubleshooting and scientific laboratory support. Technical adjacency survives the occupational definition; deployment preferences belong to later filters. |
| Substation Structural Designer | `3ffbe13f6fc1ba15d707cd2d` | structural + construction / foundation | Structural calculations/drawings and technical design review, primarily civil/structural. Scope-boundary uncertainty, not resolved by a technical employer. |
| ECAD Layout Designer Technician | `afc5e30631d0feea140b1bb7` | technician + assembly | PCB layout, schematic-to-layout implementation, routing/constraints and design outputs. Clear plausible electronics work would lose analysis. |
| Structural Engineer - Power Delivery | `23872427616e788205970739` | structural + steel structures / design | Civil/structural design in power infrastructure. Broad engineering KEEP label requires human scope agreement; not evidence that every construction reject is wrong. |
| Extended Workforce Solutions HR Project Manager | `40a4e12248dbc15959d09073` | HR + candidates | Daily file-transfer/integration error monitoring, root-cause resolution and dependencies across VNDLY, Workday and ServiceNow. Substantive enterprise support is missed despite the HR title. |

Five trigger the operator/technician rule, three civil/construction rules, and one HR. The known AMBIGUOUS rejects also include a digital-technology capture role, a flight-line electrical/environmental technician and a product-manager role; uncertainty cannot be equated with a safe reject. The exact existing rule IDs, evidence and posting fingerprints remain in `Tests/cheap_triage_rules/refactor-comparison.json`; original duty evidence remains in the frozen corpus.

## Minimum promotion path and proposed controlled workflow

1. First define the actual expensive work to avoid. Preserve cheap production Job Fit scoring unless a separately reviewed experiment demonstrates meaningful savings with acceptable product behavior. Do not enable a new bulk Qwen workload merely to justify gating it.
2. Have a human review all nine false rejects and ten rejected ambiguous cases, and agree on technical-adjacency boundaries. Review the 208 old-cache rejects for leakage/error families, without treating this reused sample as an independent certification set. Do not memorize exceptions by posting ID or employer.
3. If rule changes are needed, create a new immutable version, preserve this baseline, rerun all frozen comparisons and review every changed decision. No such rule refinement is made here.
4. Implement a genuine non-blocking Shadow collector only as a separately reviewed next step, with input/rule fingerprints and human-review sampling. Measure counts and actual downstream eligibility/cost on representative live traffic.
5. Validate on a new independent, employer-diverse, human-labeled sample disjoint from development and existing blinded holdouts. For scale, zero false rejects among 299 independently sampled worthwhile postings supports a one-sided 95% binomial lower recall bound of approximately 99%; described-job recall needs its own adequate cohort. This calculation assumes independent representative labels and does not certify performance under employer/title shifts. Observed errors require reassessment and potentially a larger sample. Adjudicate ambiguous cases rather than force them into REJECT.
6. Only after evidence and workload justify promotion, add a persisted, audited Admin mode control: **Off** performs no new cheap evaluation/filter recording; **Shadow** records decisions but passes every job onward; **Active** skips only the explicitly named expensive work for safe REJECT. KEEP, missing evidence, undetermined/AMBIGUOUS and failures always continue. Current implementation adds no mode setting or persistence, and no activation control.
7. Keep rejected postings searchable/visible, preserving existing Job Fit results and showing a separate “analysis skipped by cheap triage” reason. Offer deliberate reevaluation/override. Do not map REJECT to hide/delete, invent a zero Job Fit score, or call skipped analysis “no score exists.”
8. Persist job identity, raw/effective decision, input fingerprint, ruleset version/hash, matched rule IDs/evidence, category/reason, timestamp and selected executor/version. Decision validity must include input and ruleset identity. Reconcile on mode/ruleset/body changes; stale/unavailable decisions fail open. Keep history, invalidate memoized decisions deterministically, and provide reevaluation without deleting the job. Publish a new immutable ruleset snapshot atomically; an in-flight request retains its snapshot.

Off/Shadow/Active transition, persistence, per-job auditing and skipped-analysis tests are **not claimed to exist** in this cleanup. They are acceptance criteria for the future collector/gate implementation, contingent on readiness. Current tests protect the absence of any cheap-triage filter in classification, visibility or reconciliation; current engine tests/replay protect KEEP/fail-open behavior, version/hash/evidence and exact offline parity. There is no destructive job operation in this change.

## Validation and preservation

Run full Windows .NET/source/UI tests, offline research invariants and `scripts/evaluate-cheap-rules.py --require-frozen-parity`; the source suite now executes Admin navigation behavior and verifies all 241 archived file hashes. Existing Job Fit regression coverage remains enabled. Read-only status tests cover authorization, loaded hash/version, stale-metric invalidation, errors and response races; the modal/copy tests remain.

Linux validation uses an explicitly marked uncommitted source snapshot on curiosity, all original container/security commands, and source-manifest integrity instead of claiming a clean committed candidate. No commit, deployment or model inference is needed. Final execution results are recorded in the task report and local validation artifacts; this document must not be read as a claim that a test passed merely because it is listed here.
