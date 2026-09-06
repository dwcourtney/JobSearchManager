# Cheap-reject leakage audit and refinements — 2026-09-05

**Result:** the recommended offline `electrical-safe` variant rejects **208/1,503 = 13.84%**, versus **130/1,503 = 8.65%** previously. It makes 81 new REJECT decisions and rescues three old rejects (net +78). All 81 new rejects were reviewed and labeled unrelated. It has zero observed false rejects on the unchanged 85-KEEP development set, the 74-KEEP stratified audit, the one-KEEP delta audit, and 30 synthetic KEEP fixtures. These sets overlap and are provisional editorial evidence, not independent validation. Recommend a **non-discarding, human-reviewed shadow evaluation**, not automatic production rejection. No production behavior, second-stage classifier, LLM service, tracked application source, commits or pushes were changed.

## Sampling and evidence boundaries

Used the same frozen 1,503-record public-text cache snapshot as the previous investigation, checked against the canonical hash in `manifest.json`. No existing holdout was opened or retuned during this continuation. Its title-group exclusion was already applied before that snapshot was created. Windows handled this offline CPU-only work; no Docker/WSL setup or Linux/container validation was needed. No Qwen or external LLM inference was invoked.

The initial **196-record audit** is stratified by baseline decision reason. Within each stratum, records are selected by SHA-256 of `jsm-leakage-audit-v1|id`; selection does not depend on review labels. It includes 136 baseline KEEP and 60 baseline REJECT records. Labels were fixed before refinement scores. Every row stores the stratum population/sample sizes, inverse inclusion weight, decision reason, matched protection text, source text and review rationale. The small duty and missing-corroboration strata were reviewed completely. This is a proportionally weighted audit, not a claim that its deliberately oversampled raw proportions represent the cache.

**Label provenance:** coding-assistant editorial review of titles and role-duty text. Explicit technical titles remain KEEP; ambiguity also remains KEEP. These are not human-adjudicated labels, and no inference API was used to generate them. There are 54 records overlapping the earlier development set; no pooled independent-test claim is made.

After refinements, all **41 additional newly rejected records outside the first audit** were reviewed. This delta census contains 40 REJECT and one conservative KEEP. It was selected using candidate predictions and is development error analysis, never a prevalence sample. Refinements and final selection use this audit evidence, so neither audit is an untouched test set.

## Why the baseline keeps so much

| Baseline survival path | Full-cache count | Share of 1,373 survivors | Reviewed | Unrelated in review | Estimated unrelated survivors |
|---|---:|---:|---:|---:|---:|
| explicit technical implementation/support evidence | 22 | 1.60% | 22 | 18 | 18.0 |
| missing corroborating occupational body evidence | 9 | 0.66% | 9 | 8 | 8.0 |
| no explicit unrelated title family | 1236 | 90.02% | 70 | 13 | 229.5 |
| protected technical/engineering title | 106 | 7.72% | 35 | 25 | 75.7 |

The main cause by volume is **missing occupational coverage**, not just erroneous vetoes. In the 70-record random no-family sample, 57 were technical or conservatively ambiguous and 13 unrelated. Most cache survivors really are near-domain technical work under this target. The much smaller title-veto stratum is heavily populated by clearly civil/structural construction designers.

Weighted estimates suggest **331 unrelated postings still survive** (24.1% of survivors). A rough stratified finite-population normal sampling interval is **220–442**, conditional on the labels being correct; it excludes reviewer/target uncertainty. Counts below use sum(N/n) for labeled unrelated survivors. Raw sample counts must not be interpreted as cache prevalence.

| Leakage family | Reviewed unrelated survivors | Estimated cache count | Share of estimated leakage | Why it survives |
|---|---:|---:|---:|---|
| civil-construction | 32 | 112.0 | 33.8% | Generic engineer veto; drainage missing from titles; CAD/software use or “consistent application” treated as technical duties; some generic project bodies lack corroboration. |
| sales-marketing-communications | 8 | 60.0 | 18.1% | Missing public-relations/marketing/BD titles; AI/platform product words protect commercial titles; business-development/training prose triggers duty veto. |
| finance-scheduling | 8 | 58.0 | 17.5% | Missing schedule/payroll title families; narrow financial body terms; “program controls … software” and professional networking look technical. |
| procurement-supplier-admin | 6 | 41.3 | 12.5% | Missing supplier administration titles; employer suffix “Space Systems”; asset/software inventory tracking misread as maintenance. |
| environment-field-safety | 4 | 37.3 | 11.3% | Missing archaeology/environment/field-safety occupations; employer “technical practice network” boilerplate; sparse matching body terms. |
| physical-maintenance-production | 3 | 19.7 | 5.9% | Missing aircraft-maintenance training family; narrow machining/chemical-process body terms. Ambiguous electronic/field support intentionally survives. |
| clinical | 2 | 2.0 | 0.6% | “Maintain a computerized database” protects mental-health care occupations; using patient records is not system implementation. |
| hr-recruiting | 1 | 1.0 | 0.3% | “Build … talent pipelines … recruit … cloud/software” is a recruiting duty, not software development. |

Concrete false protections include a recruiter matching `build strong talent pipelines, and recruit top-tier cyber, cloud, software`; a construction manager matching `development and consistent application`; a finance intern matching `build a lasting professional network`; a bridge drafter matching `developing plans, using CAD software`; and a procurement agent protected by the employer descriptor `Millennium Space Systems`. Word stems such as `program\w*` match the noun “program”, and unconstrained verb/object proximity does not establish who builds or administers the technology.

Not every technical-looking commercial role is a leak. Sales Operations Analyst includes configuration, troubleshooting and analytics/automation. Technical solution consultants and Federal Physical AI Business Development Lead include technical demonstrations/integration. Those remain KEEP. Generic field, simulator and electronic-support roles are also ambiguity-kept. The refinements intentionally leave clinical database use, some asset-tracking prose, broad “application” uses and uncertain business/technical mixtures alone where a narrowly demonstrated distinction was not established.

## Reject audit: the earlier zero-error claim does not generalize

Of **60 randomly selected baseline rejects, 58 were clearly unrelated under the review policy and two should be kept**. Confirmed unrelated examples include nurse/clinical psychologist care, tax accounting, quota-carrying account executives, procurement negotiation, civil bridge detailing, construction estimating, machining and HR recruiting. Two important exceptions:

- **Principal Specialist, General Finance**: SQL reporting, financial-system module/configuration maintenance, advanced system support and troubleshooting. The earlier frozen development label was REJECT; this audit labels it KEEP. That label discrepancy is explicitly recorded in results. The old development file was not rewritten to conceal it.
- **Field Technician 4**: the title hides field engineering, root-cause analysis, custom tooling design and control-system analysis. Under the conservative adjacent mechanical-engineering boundary it is KEEP. Shipboard/travel requirements do not decide this occupational stage.

Baseline false-reject risk is **2/60 = 3.33% of reviewed rejects**. A binomial 95% interval is approximately **0.41–11.53%** of rejected jobs (a sampling-only approximation; finite-population and label uncertainty must be distinguished). Design weighting gives about **4.3 falsely rejected postings / 1,503 = 0.29% of all postings**, with an estimated KEEP recall of 99.59%. That latter ratio is not certified accuracy: the raw stratified audit recall is 72/74 = 97.30%, which differs because unrelated-veto strata were oversampled. Neither result licenses automatic rejection.

## Refinements tested in sequence

All experiments remain separate from `evaluate.py` and production C#. The original labels and baseline results are preserved. Every meaningful revision was evaluated on the same development records and fixtures, the initial audit and the full eligible cache. The delta audit was subsequently applied to all variants.

1. **Safety:** preserve generic field/simulator/PC/electronic support titles and affirmative ERP/SQL configuration/support evidence. This fixes the two audit errors and intentionally lowers initial rejection volume.
2. **Families:** add narrowly scoped marketing/public-relations, environmental/archaeology, cost/schedule/payroll, supplier administration and aircraft-maintenance training families, each requiring corresponding body evidence. Retain existing technical protections.
3. **Body context:** disregard individual demonstrated nontechnical veto matches such as recruiting talent pipelines, professional/practice networks, CAD use, and “consistent application”. A second genuine technical match still protects the posting; no whole body or entire veto mechanism is removed.
4. **Civil context:** override generic engineering wording only with a civil/roadway/bridge/geotechnical/drainage/structural title, physical built-object evidence and design/construction evidence, while preserving core technical title and duty guards. This improved volume but failed the delta audit on electrical lighting engineering.
5. **Procurement context:** a clear procurement-agent/specialist function can override a trailing systems employer descriptor; technical job functions and technical body evidence still survive.
6. **Aggressive commercial comparator — rejected:** broaden commercial-role overrides of AI/platform/system title words. This falsely rejects technical AI business development. It is retained only as failed experimental evidence.
7. **Electrical-safe — recommended:** use the conservative refinements, explicitly preserve lighting/electrical/protection/controls/signals in the civil exception, and omit the aggressive commercial override.

## Before/after metrics

All variants retain **85/85 frozen development KEEP records (100% recall)**. This alone missed the audit label issue and the new errors. The stratified audit has 74 KEEP; the disjoint 41-record delta audit has one KEEP. The latter is a targeted error census, so a zero/one recall percentage there is not a population estimate.

| Variant | Dev rejected /166 | Dev rejection | Dev false rejects /85 | Stratified audit false rejects /74 | Delta false rejects /1 | Cache rejected /1503 | Cache rejection | Decision |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| baseline | 49 | 29.52% | 0 | 2 | 0 | 130 | 8.65% | Superseded: audit errors |
| safety | 48 | 28.92% | 0 | 0 | 0 | 127 | 8.45% | Safety improvement |
| families | 48 | 28.92% | 0 | 0 | 0 | 141 | 9.38% | Conservative increment |
| body-context | 52 | 31.33% | 0 | 0 | 0 | 150 | 9.98% | Conservative increment |
| civil-context | 56 | 33.73% | 0 | 0 | 1 | 208 | 13.84% | Not accepted: lighting error |
| procurement-context | 56 | 33.73% | 0 | 0 | 1 | 209 | 13.91% | Not accepted without electrical guard |
| aggressive-commercial | 57 | 34.34% | 0 | 1 | 1 | 211 | 14.04% | Rejected: lighting + technical BD errors |
| electrical-safe | 56 | 33.73% | 0 | 0 | 0 | 208 | 13.84% | Recommend shadow evaluation only |

**False-reject tradeoff:** civil/procurement variants lose **Roadway Lighting Engineers and Designers**. The aggressive commercial variant additionally loses **Federal Physical AI Business Development Lead**, whose duties include technical demonstrations and stack integration. It reaches 14.04%, only three extra rejects over the final 13.84% configuration, with two known KEEP losses across the audits. That tradeoff is rejected even though the old development set still reports zero errors.

The final candidate rejects **56/166 development records (33.73%)**, 99/196 stratified-audit records, and 40/41 delta records, with no observed false rejects in those sets. Development rejection precision is 100% against its unchanged provisional labels. All eight variants preserve the 30 synthetic KEEP examples and reject the 30 synthetic negatives. Those synthetic examples did not catch the lighting/technical-BD boundaries; targeted real-data regression tests now do.

**Coverage of final changes:** 81 newly rejected postings are covered by 41 reviewed transitions in the stratified audit plus 40 in the delta census. All are labeled REJECT. Three previous rejects are rescued. Across the final 208 rejects, 139 have individual reviews and 69 do not. Zero observed final errors is therefore neither a complete census of final decisions nor an independent accuracy result.

## Computational cost

CPU-only, deterministic regexes; no model training or inference service. Bodies are only normalized/scanned for possible title rejects; unconditional KEEP paths exit early. That optimization was checked to preserve every cache decision across all eight variants plus every reported labeled-set metric. Timings are warm, three rounds on the same Windows/Python environment as the first experiment, include normalization, and exclude IO/decompression. They are not a direct Qwen benchmark.

| Variant | Median µs/posting | p95 µs/posting | Batch postings/sec |
|---|---:|---:|---:|
| baseline | 27.2 | 940.3 | 3553 |
| safety | 31.9 | 1711.5 | 2127 |
| families | 33.7 | 1346.1 | 2463 |
| body-context | 34.2 | 1339.7 | 2501 |
| civil-context | 39.0 | 1376.3 | 2209 |
| procurement-context | 39.4 | 1353.3 | 2223 |
| aggressive-commercial | 41.5 | 1384.4 | 2098 |
| electrical-safe | 37.6 | 1327.8 | 2237 |

## Recommendation and remaining limits

Use `electrical-safe` as the next **offline or non-discarding shadow-evaluation candidate**, not an automatic discard rule. It projects about **138 avoided downstream evaluations per 1,000 postings**, versus 86 previously, conditional on this cache representing future inputs and each posting otherwise requiring inference. No LLM workload was actually changed. The gain is **5.19 percentage points** (78 additional avoided evaluations in this snapshot); 40% is still unsupported.

The remaining estimated leakage is large enough to investigate later, but broadening technical veto overrides already caused false rejects. The approximate point estimate of total unrelated prevalence is only 30.4% (weighted survivors plus true baseline rejects); this is uncertain, not proof of a hard ceiling. Generic role titles, unclear duties, real technical presales, mixed support work and mere lack of keyword evidence remain KEEP. Do not force a workload target by reclassifying those ambiguities.

Before automatic rejection, obtain independent human adjudication of fresh representative arrivals and the remaining reject types, including disputed adjacent engineering and technical-business roles. Freeze a new evaluation before tuning further. The original 166 development labels demonstrably contain at least one error, and this audit was used to develop the refinements; neither supports a certified 98–99% recall claim. This continuation stops here; no second-stage work or production wiring is performed.

## Files and validation

New this turn:

- `Tests/cheap_reject_evaluation/leakage.py`
- `Tests/cheap_reject_evaluation/leakage-audit.jsonl`
- `Tests/cheap_reject_evaluation/leakage-delta-audit.jsonl`
- `Tests/cheap_reject_evaluation/leakage-manifest.json`
- `Tests/cheap_reject_evaluation/leakage-results.json`
- `Tests/cheap_reject_evaluation/test_leakage.py`
- `docs/cheap-reject-leakage.md` (this report)

Updated: the existing evaluation `README.md` with reproduction instructions and `docs/cheap-reject-investigation.md` with a link/correction notice. The original development/fixture labels, evaluator and baseline results are unchanged. All of these files remain untracked; no application-source diff.

Validation: 23 offline Python tests passed (the original 10 evaluation tests plus 13 leakage tests). Cache sampling replay and canonical fingerprint checks passed. All eight variants were evaluated on the unchanged development set, both audits, original fixtures and the full cache. The early-exit optimization preserved all cache decisions; all 81 new rejects have review coverage. Existing `scripts/validate-source.ps1` also passed its JavaScript/UI suites and source audits. No .NET/container rebuild was necessary for these isolated evaluation Python/data/doc changes; the previous turn’s .NET results are not represented as a new run.

Final Git state: `main...origin/main`, zero ahead/behind, with 17 untracked evaluation/report additions across both turns. Tracked files unchanged. No staging, commit or push.
