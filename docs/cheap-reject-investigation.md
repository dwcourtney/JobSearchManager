# Conservative first-stage rejection investigation — 2026-09-05

> Follow-up: [leakage audit and refinements](cheap-reject-leakage.md) supersedes the candidate recommendation. It finds two baseline audit false rejects, including a General Finance development-label error, and reports a refined 13.84% cache rejection rate. The historical metrics below remain unchanged; they are provisional development evidence.

**Recommendation:** retain the title + occupational body confirmation + technical veto candidate for further independent evaluation. Do not activate production rejection from this evidence alone. It rejected 49/166 development records (29.52%) with 0/85 false rejects; its unlabeled cache rejection fraction was 130/1,503 (8.65%). The tested methods do not establish 40% workload reduction at 98–99% KEEP recall. No production code, inference service, second-stage funnel, or existing holdout evaluation was changed or run. Nothing was committed.

## Why the previous target could not support useful rejection

`TriageEvaluation.cs:61` defines `TriageEvaluationService`; `FreezeReferenceAsync` derives `triage-reference-labels-v2.json` from `holdout.json` and the frozen `ai-reference-labels-v1.json`. Its irrelevant-occupation ontology covers only six physical/manual families. Any present **or unresolved** technical role/skill concept protects a posting; all remaining indeterminate cases also become KEEP. Thus a missing occupational label, technical employer language or ambiguous technical evidence can count as relevant even when the actual primary work is unrelated. Generic responsibility concepts were removed in v2, but the residual target still measures conservative concept-derived plausibility rather than directly adjudicated primary job duties. These historical references were AI-adjudicated, not human ground truth.

`CheapTriageClassifier` at `TriageEvaluation.cs:363` implements the current experiment. Its first stage scans broad title/body technical buckets, then rejects full-text hard-conflict matches regardless of technical evidence, or a limited physical title if no technical bucket matches. Broad terms such as “application”, “software”, “AI” and “routing” can protect unrelated duties through boilerplate. Conversely, unscoped shipboard/deployment expressions can discard technical work, including negated statements. The new occupational target does not reuse these travel vetoes. The existing second stage was not redesigned or evaluated here.

Existing entry points are `Program.cs:238` / `:243` maintenance commands and `Program.cs:874` / `:881` admin diagnostic endpoints. Existing tests are in `Tests/Program.cs` (`TestCheapTriageAsync`); existing documentation is `docs/triage-evaluation-methodology.md`. This remains evaluation-only code, not an active production intake filter.

Only historical aggregate metrics were inspected: run `5980bff55a1d408e9b195df41fd2cb4e`, 2026-09-02, 200 records, 189 KEEP, 11 REJECT, 75 ambiguous KEEP. Stage one rejected six, of which five were false rejects: 97.35% KEEP recall, 3.00% rejection and 16.67% rejection precision. At 99% recall, at most one of 189 KEEP jobs can be lost: even perfect rejection of all 11 negatives permits only 12/200 = 6% total rejection. At 98%, the maximum is 14/200 = 7%. This is a target/sample limitation, not merely a weak threshold. No historical per-posting predictions or errors were used for tuning.

## Binary definition and development design

**KEEP:** primary duties plausibly include software, systems/integration, automation, cloud/platform, data/AI, technical security, or relevant infrastructure/admin/support. Preserve uncertain adjacent technical work, specialist mechanical/electrical engineering, mixed technical support, and technical presales/solution consulting. A broad engineering title alone is insufficient grounds for rejection.

**REJECT:** affirmative evidence that the primary occupation is unrelated clinical care, vehicle driving, physical trades/construction, accounting, pure selling/capture, recruiting/HR, hospitality, physical guarding, production/machine operation, or nontechnical administration. Merely using software/AI, selling a technical product, or working at a technology employer does not qualify as technical work. Explicit civil/bridge/roadway construction design with no software/IT duties may be REJECT. Location, travel, seniority, education, clearance and final Job Fit suitability are outside this stage. Unresolved cases stay KEEP.

Public posting caches were exported read-only from the established `curiosity-codex` Linux host. The exporter removed matching stable IDs/content hashes and **every normalized title group** in both existing frozen holdout manifests before development text was reviewed. The two manifests were byte-identical. Exclusion removed 198 title groups; 250 excluded records had descriptions (253 including three empty-description records). Replaying the exporter produced the identical 1,503 eligible public-text records across 11 employers. No account settings, classifier outputs or saved model labels were exported. Exact title exclusion is conservative but cannot guarantee absence of semantic near-duplicates; future confirmation must use newly collected prospective data.

A fixed SHA-256 seed/order selected distinct titles using per-family quotas (15 each; core technical 60; background 30). Selection happened before labeling and did not depend on candidate scores. The resulting real development set contains **166 records, 85 KEEP and 81 REJECT**, across 10 employers. It is deliberately negative-rich (48.8% REJECT) and not a prevalence sample. The manifest and every row retain provenance and rationale. **These are provisional coding-assistant editorial labels, not independent human-adjudicated truth.** Labels were frozen before candidate predictions. No Qwen/server LLM or external model API inference was spent.

| Sampling stratum | KEEP | REJECT | Total |
|---|---:|---:|---:|
| clinical | 1 | 4 | 5 |
| driving | 3 | 1 | 4 |
| construction | 3 | 12 | 15 |
| finance | 5 | 10 | 15 |
| sales | 1 | 14 | 15 |
| hr | 0 | 6 | 6 |
| physical-security | 1 | 0 | 1 |
| operator-technician | 4 | 11 | 15 |
| core-technical | 57 | 3 | 60 |
| background | 10 | 20 | 30 |

There were no real hospitality matches and the physical-security stratum supplied only an information system security officer (KEEP). Some apparent driving and finance negatives were actually technical title collisions. **60 separately labeled synthetic contrast records (30 KEEP/30 REJECT)** cover these gaps, near-domain title protections, generic AI/software boilerplate and negated travel language. Synthetic results are software checks, not measured deployment accuracy. All requested near-domain title families have survival checks, including .NET/C#, full stack/backend, DevOps/DevSecOps/platform, AWS/Azure/cloud, systems/integration, AI/ML/NLP, automation and infrastructure/admin.

## Candidates and protocol

Four deterministic candidates were frozen before the first score run: a semantic Python port of only the existing C# stage-one predicate (patterns extracted from source); broad unrelated title keyword families without guards; the same families with broad technical/engineering title guards; and guarded titles plus matching occupational body evidence with an explicit technical-duty veto. The final predicate scans the body only for possible rejects. It requires an implementation/admin verb near a specific technical object for its body veto, rather than generic standalone software/AI words. It still uses approximate sentence proximity and can over-keep from boilerplate; this is intentionally conservative, not full semantic understanding.

Two classical models use TF-IDF word unigrams/bigrams with logistic regression: concatenated title/body, and separately vectorized title/body scaled 3:1. Features use `min_df=1`, `max_features=30000`, sublinear TF; logistic regression uses balanced class weights, `C=1`, liblinear, fixed seed 20260905. Fixed reject thresholds are KEEP score < 0.05, 0.1, 0.2 or 0.5. These scores are **not calibrated probabilities**. Vocabulary and model fitting occur inside five employer-disjoint folds; each real row receives one out-of-company prediction. No synthetic record is used for training. Full-development fits are used only for timing, synthetic checks and unlabeled shadow counts.

Rules are descriptive development-set measurements after domain review; classical results are employer-held-out predictions. Neither is an untouched final evaluation. The rule recommendation was selected from development results and requires fresh validation. Dataset hashes, evaluator/source fingerprints, model configuration, folds, per-row predictions, rejection reasons and all false rejects are recorded in `Tests/cheap_reject_evaluation/results.json`.

## Real development metrics

KEEP recall = retained KEEP / all KEEP. False rejects count discarded KEEP. Rejection precision = true REJECT / all discarded. Rejection rate is discarded / all inputs. A dash means nothing was rejected.

| Candidate | KEEP recall | False rejects / 85 | Rejected / 166 | Rejection rate | Rejection precision |
|---|---:|---:|---:|---:|---:|
| legacy-stage1-port | 98.82% | 1 | 6 | 3.61% | 83.33% |
| title-unguarded | 82.35% | 15 | 91 | 54.82% | 83.52% |
| title-guarded | 97.65% | 2 | 63 | 37.95% | 96.83% |
| title-body-confirmed | 100.00% | 0 | 49 | 29.52% | 100.00% |
| tfidf-lr@0.05 | 100.00% | 0 | 0 | 0.00% | — |
| tfidf-lr@0.1 | 100.00% | 0 | 0 | 0.00% | — |
| tfidf-lr@0.2 | 100.00% | 0 | 0 | 0.00% | — |
| tfidf-lr@0.5 | 85.88% | 12 | 86 | 51.81% | 86.05% |
| weighted-title-body-lr@0.05 | 100.00% | 0 | 0 | 0.00% | — |
| weighted-title-body-lr@0.1 | 98.82% | 1 | 7 | 4.22% | 85.71% |
| weighted-title-body-lr@0.2 | 95.29% | 4 | 21 | 12.65% | 80.95% |
| weighted-title-body-lr@0.5 | 90.59% | 8 | 74 | 44.58% | 89.19% |

## Latency and unlabeled cache counts

Warm single-process Windows CPU timings include normalization and prediction, exclude file IO/training, and use three rounds over the same 166 real records after warmup. Single-record median/p95 and batch throughput are separate measurements. Regexes are precompiled. Timing includes scanning already-decompressed normalized cache text; HTML decompression and network/ingestion costs are not measured. This is not a direct Qwen benchmark and the legacy Python port is not a .NET runtime benchmark.

| Candidate | Median µs/job | p95 µs/job | Batch jobs/sec | Unlabeled cache rejection |
|---|---:|---:|---:|---:|
| legacy-stage1-port | 1425.5 | 4728.1 | 533 | 36/1503 (2.40%) |
| title-unguarded | 19.6 | 29.6 | 51373 | 267/1503 (17.76%) |
| title-guarded | 22.4 | 37.0 | 44471 | 161/1503 (10.71%) |
| title-body-confirmed | 26.1 | 937.7 | 3637 | 130/1503 (8.65%) |
| tfidf-lr@0.05 | 1725.3 | 2529.2 | 747 | 0/1503 (0.00%) |
| tfidf-lr@0.1 | 1761.1 | 2518.9 | 744 | 0/1503 (0.00%) |
| tfidf-lr@0.2 | 1750.5 | 2586.1 | 742 | 0/1503 (0.00%) |
| tfidf-lr@0.5 | 1745.6 | 2571.0 | 739 | 806/1503 (53.63%) |
| weighted-title-body-lr@0.05 | 2300.6 | 3110.5 | 732 | 11/1503 (0.73%) |
| weighted-title-body-lr@0.1 | 2282.6 | 3134.7 | 738 | 109/1503 (7.25%) |
| weighted-title-body-lr@0.2 | 2302.1 | 3091.8 | 732 | 327/1503 (21.76%) |
| weighted-title-body-lr@0.5 | 2287.4 | 3085.9 | 727 | 674/1503 (44.84%) |

Environment: Python 3.12.5, NumPy 2.0.2, scikit-learn 1.5.2; AMD64 Family 23 Model 113 Stepping 0, AuthenticAMD; Windows-10-10.0.19045-SP0.

## Error examples and synthetic checks

| Candidate | Real false-reject examples (all errors in machine-readable report) | High-confidence true-reject examples | Synthetic KEEP recall; false rejects; rejection rate |
|---|---|---|---|
| legacy-stage1-port | Ground Penetrating Radar Specialist (Antarctica) | ASC Construction Manager; Machinist Assembler Precision | 96.67%; 1/30; 10.00% |
| title-unguarded | Senior Solutions Architect, Simulations - Clinical Sciences and Autonomous Lab; Senior Systems Software Engineer, CUDA Driver - Multi-Node and Memory Model; Senior Systems Software Engineer, CUDA Driver | Special Operations Nurse Case Manager / Case Manager (Fort Bragg, NC); Special Operations Clinical Psychologist (75th Ranger Regiment, Fort Moore,GA) | 60.00%; 12/30; 70.00% |
| title-guarded | Training Device Technician (Experienced or Senior); Field Service Technician I | Special Operations Nurse Case Manager / Case Manager (Fort Bragg, NC); Special Operations Clinical Psychologist (75th Ranger Regiment, Fort Moore,GA) | 100.00%; 0/30; 50.00% |
| title-body-confirmed | None observed | Special Operations Nurse Case Manager / Case Manager (Fort Bragg, NC); Special Operations Clinical Psychologist (75th Ranger Regiment, Fort Moore,GA) | 100.00%; 0/30; 50.00% |
| tfidf-lr@0.05 | None observed | None | 100.00%; 0/30; 0.00% |
| tfidf-lr@0.1 | None observed | None | 100.00%; 0/30; 0.00% |
| tfidf-lr@0.2 | None observed | None | 100.00%; 0/30; 0.00% |
| tfidf-lr@0.5 | Sr Mechanical Engineer – Federal Experience; Principal Mechanical Producibility Engineer; Advisory Solution Consultant - Finance & Supply Chain Solutions | Special Operations Nurse Case Manager / Case Manager (Fort Bragg, NC); Special Operations Clinical Psychologist (75th Ranger Regiment, Fort Moore,GA) | 100.00%; 0/30; 48.33% |
| weighted-title-body-lr@0.05 | None observed | None | 100.00%; 0/30; 0.00% |
| weighted-title-body-lr@0.1 | Ground Penetrating Radar Specialist (Antarctica) | Special Operations Nurse Case Manager / Case Manager (Fort Bragg, NC); Manager, Program Finance & Control Schedule Analyst (Remote) | 96.67%; 1/30; 3.33% |
| weighted-title-body-lr@0.2 | Advisory Solution Consultant, Partner Sales (Armis); GIS Technician; Director - Product Cybersecurity Supply Chain Risk Management | Special Operations Nurse Case Manager / Case Manager (Fort Bragg, NC); ASC Construction Manager | 96.67%; 1/30; 15.00% |
| weighted-title-body-lr@0.5 | Advisory Solution Consultant - Finance & Supply Chain Solutions; Advisory Solution Consultant, Partner Sales (Armis); GIS Technician | Special Operations Nurse Case Manager / Case Manager (Fort Bragg, NC); Special Operations Clinical Psychologist (75th Ranger Regiment, Fort Moore,GA) | 96.67%; 1/30; 46.67% |

Title-only guards lose **Training Device Technician** and **Field Service Technician I**. Both have ambiguous adjacent technical duties and therefore KEEP labels; body evidence preserves them in the recommended candidate. Unguarded rules additionally lose CUDA driver software engineers, financial systems analysts, information systems security officers and GIS technicians. Logistic regression at 0.5 loses systems/cloud/network/security roles; weighting titles does not eliminate this risk. The current-stage port rejects the Antarctica radar specialist because of location/deployment evidence even though the new target keeps its adjacent technical duties. A synthetic systems-integration posting with “No sea-going assignment is required” exposes the legacy negation failure.

Observed true rejects for the recommended candidate include direct-care nurse/clinical psychologist/social-worker roles, Automotive Mechanic/Truck Driver, ASC Construction Manager and Machinist Assembler Precision. Hospitality/driving/security contrast fixtures show that Cook vs Cloud Engineer, Truck Driver vs CUDA Driver Engineer, and Security Guard vs Information Systems Security Officer can be separated. Perfect synthetic performance does not establish coverage of real hospitality or guarding postings.

## Recommendation and achievable reduction

Use this **single-stage** decision threshold for the next independent evaluation: reject only when (1) the title matches an explicit unrelated occupational family, (2) body text corroborates that family, (3) no protected technical/engineering title exists, and (4) no explicit technical implementation/admin/support evidence exists. Otherwise KEEP. It is a conjunction of explainable evidence, not an inferred confidence percentage. There is no second “Maybe” pass.

The observed development point is 100% KEEP recall, 0 false rejects, 29.52% rejection, 100% rejection precision, and 60.49% recall of the negative class. The 1,503-record cache gives a **conditional workload projection of 8.65% fewer downstream evaluations (130 jobs; about 86 per 1,000)** if this snapshot resembles arrivals and every posting would otherwise incur one evaluation. The cache is technical-employer-heavy, unlabeled and overlaps the development pool: this is an observed firing rate, not independent accuracy evidence or measured production savings. Caching/repeated postings and actual inference eligibility can change real savings. The old-stage port fires on 2.40% of this pool, about 6.25 percentage points less.

40% is not supported by these experiments. At 60.49% negative detection with no KEEP loss, reaching 40% rejection would require roughly 66.1% unrelated inputs, versus 48.8% in the deliberately enriched development sample. More aggressive tested methods reach 44–55% rejection there only by reducing KEEP recall to roughly 82–91%. This does not prove 40% impossible on a different feed or with better evidence, but it cannot be claimed here.

Even if these labels were independent and correct, 0/85 errors gives only a 96.54% one-sided 95% binomial lower bound on KEEP recall. Selection and editorial-label uncertainty make it weaker as a deployment guarantee. With zero observed misses, at least 299 independently labeled KEEP cases are needed for a 99% lower bound (and about 149 for 98%), under ordinary independent-sampling assumptions. Recommend fresh prospective, human-adjudicated KEEP/REJECT evidence with negative strata and a representative prevalence slice before allowing automatic discard. Do not reuse the old holdout for this tuning.

## Files and validation

Only new offline evaluation/report files were added:

- `docs/cheap-reject-investigation.md` (this report)
- `Tests/cheap_reject_evaluation/README.md`
- `Tests/cheap_reject_evaluation/development.jsonl`
- `Tests/cheap_reject_evaluation/fixtures.jsonl`
- `Tests/cheap_reject_evaluation/manifest.json`
- `Tests/cheap_reject_evaluation/results.json`
- `Tests/cheap_reject_evaluation/evaluate.py`
- `Tests/cheap_reject_evaluation/export_pool.py`
- `Tests/cheap_reject_evaluation/sample.py`
- `Tests/cheap_reject_evaluation/test_evaluate.py`

Validation: 10 new Python tests passed (metrics, all requested protected-title families, technical body vetoes, blank-body KEEP, dataset hashes/negative coverage, employer fold isolation, training-only TF-IDF vocabulary, 60 contrast fixtures, exclusion/deduplication/allowlisted export, deterministic label-independent sampling and legacy first-stage boundaries). All 12 candidate configurations completed, and a repeat run preserved all reported metric counts. Export replay matched all 1,503 records exactly; sampling replay matched all 166 IDs. Existing `scripts/validate-source.ps1` passed its JavaScript/UI suites and source audits. Existing .NET suite: all 137 deterministic architecture tests passed (`dotnet run --project Tests/JobSearchManager.Tests.csproj --configuration Release --no-restore`). Whitespace and production-content isolation checks: passed, including whitespace scans of every new file and confirmation that MSBuild application Content contains none of the new artifacts. No container rebuild was needed for standalone offline Python/doc additions; no production inference was invoked. The existing .NET tests use fixtures/mocks, not the live blinded holdout or a live LLM.

Final status: `main...origin/main`, with the ten files above untracked; existing tracked files unchanged. No commit or push. Investigation stops at this first-stage recommendation.
