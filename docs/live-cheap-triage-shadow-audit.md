# Cross-employer Cheap Triage Shadow audit — 2026-09-06

**Recommendation: keep Shadow observational. Investigate the demonstrated safety failures before increasing rejection.** The cache-wide rejection fraction is **10.70%**, not Parsons' 27.99%. The audit supports a focused human-review queue and classification-pipeline design, but does not establish 98–99% KEEP recall or safe production gating.

No rules, historical labels, Job Fit behavior, persistent production data or service configuration were changed. No provider downloads, model inference, commits, pushes or deployment occurred. See [the follow-on design and implementation sequence](classification-pipeline-design.md).

## What was actually observed

Release `e0a28a10bfb158d5ea2c86f4718ca79f3fb99309`; deployed image ID `sha256:1755b587d8280b949a3f7b42cdbe241b0a2ed35262144612c086d8c8d9dbb6ef`. Every observation uses analysis adapter 1, ruleset **1.0.0**, SHA-256 **`269be7264e56f641723c254910d026d20687f5d61aaa39d967c5d52e4bc51983`**.

The 268 Parsons observations were already recorded by production. The other **1,937 unique observations** were reconciled by the **actual deployed application's startup and live Shadow worker against isolated copies** of existing caches. They are not newly observed normal-browser traffic and were not written back to production. Containers ran with `--network none`, synthetic local settings, startup refresh disabled, a read-only root filesystem and only an isolated audit-data mount. No production service was restarted. Normal source switching was avoided because older caches could trigger downloads.

All 14 available employer/query cache documents were included: 2,284 cache instances, reduced to **2,205 stable posting IDs** across 11 employers and two providers. Of 76 IDs appearing in multiple caches, 13 had different input fingerprints but **none had conflicting decisions**. Selection uses newest cache refresh, then description availability, then descending cache key. This removes 79 duplicate instances; distinct requisitions with similar titles/duties remain. Their correlation limits statistical interpretations.

The cache snapshot includes retained/hidden jobs, as the live Admin collector does. **1,838 source-available jobs:** 1,644 KEEP / 192 REJECT / 2 UNDETERMINED, **10.45%** rejection. **367 retained source-unavailable jobs:** 323 / 44 / 0, **11.99%**. Availability is cached status, not a fresh provider check. No workflow/visibility filter was applied.

## Employer results

Counts below are deduplicated. `Title / body` counts describe available input, not the evidence scope chosen by a rule. All rows use the version/hash above.

| Employer | Provider | Total | KEEP | REJECT | UNDET. | Title / body | Rejection |
|---|---|---:|---:|---:|---:|---:|---:|
| AECOM | SmartRecruiters | 102 | 74 | 28 | 0 | 0 / 102 | 27.45% |
| Amentum | Workday | 14 | 13 | 1 | 0 | 0 / 14 | 7.14% |
| Boeing | Workday | 659 | 623 | 36 | 0 | 443 / 216 | 5.46% |
| KBR | Workday | 48 | 40 | 8 | 0 | 0 / 48 | 16.67% |
| Leidos | Workday | 275 | 251 | 23 | 1 | 0 / 275 | 8.36% |
| Northrop Grumman | Workday | 44 | 34 | 10 | 0 | 0 / 44 | 22.73% |
| NVIDIA | Workday | 482 | 480 | 2 | 0 | 7 / 475 | 0.41% |
| NXP Semiconductors | Workday | 12 | 12 | 0 | 0 | 0 / 12 | 0.00% |
| Parsons | Workday | 268 | 192 | 75 | 1 | 0 / 268 | 27.99% |
| RTX | Workday | 158 | 135 | 23 | 0 | 0 / 158 | 14.56% |
| ServiceNow | SmartRecruiters | 143 | 113 | 30 | 0 | 0 / 143 | 20.98% |
| **Total** | **2 providers** | **2,205** | **1,967** | **236** | **2** | **450 / 1,755** | **10.70%** |

Described cohort: **1,517 / 236 / 2**, rejection **13.45%**. All **450 title-only jobs KEEP**. Boeing's described rejection is 36/216 = **16.67%**, so its 5.46% overall rate partly reflects missing descriptions. NVIDIA's described rate remains only 2/475 = **0.42%**. Provider rates are 58/245 = **23.67%** SmartRecruiters versus 178/1,960 = **9.08%** Workday; employer/job mix and missing bodies confound this comparison. It is not evidence of a provider defect.

Parsons is near the top of the observed range, similar to AECOM, and is **not representative of the aggregate**. Sixty of Parsons' 75 rejects match civil-domain and/or construction rules. The frozen old-cache rate of 13.84% and this sample's 10.70% measure different populations/input coverage; neither measures actual saved Qwen calls. Shadow saves no calls, and no new expensive normal-Jobs operation was introduced here.

## Exact cache coverage

All queries are US. `Remote + city` means the original query includes both; it is not an extra audit download. Full query fingerprints, original paths, source/output hashes and counts are in the [manifest](../Tests/cheap_reject_evaluation/live_shadow_v1/manifest.json).

| Cache key prefix | Query | Refreshed UTC date | Raw jobs |
|---|---|---|---:|
| aecom-3c55b12f805599 | Remote + Orlando | 2026-08-31 | 90 |
| aecom-e06784f416eb9f | Remote | 2026-08-27 | 77 |
| amentum-6508afb282c3 | Remote | 2026-08-27 | 14 |
| boeing-4d21f41f376cd | Remote + Orlando/Daytona Beach | 2026-09-04 | 17 |
| boeing-887541f2dc785 | Remote + Orlando | 2026-08-30 | 3 |
| boeing-79fbd27ebfc41 | All US locations | 2026-08-30 | 653 |
| kbr-b694b75d628deb17 | Remote | 2026-08-27 | 48 |
| leidos-697e9a0d8c749 | Remote + Orlando | 2026-09-05 | 275 |
| northrop-grumman-c70 | Remote | 2026-09-05 | 44 |
| nvidia-68d60566b13a1 | Remote | 2026-08-31 | 482 |
| nxp-semiconductors-3 | Remote | 2026-08-31 | 12 |
| parsons-cfd0f01f9118 | Remote | 2026-09-06 | 268 |
| rtx-363a4d87be07d612 | Remote | 2026-08-27 | 158 |
| servicenow-a6b6217f7 | Remote + Orlando | 2026-08-31 | 143 |

This is a census of the available cache documents, not a random sample of the labor market. Defense, aerospace, semiconductors, engineering consulting, remote and Florida searches dominate. Some entries overlap prior research: this audit is **not an independent holdout**. No blinded production holdout was opened or tuned against.

## Reject audit and safety findings

Codex reviewed titles and cached duty passages, consulting additional context for ambiguous cases. These are fresh **audit interpretations**, not human ground truth, training labels or replacements for historical labels. `Clearly correct` requires plainly unrelated primary work; `questionable` includes mixed duties or unresolved technical-adjacency boundaries; `likely wrong` has substantive plausible technical duties. User/location/clearance preferences and Job Fit scores were not used as occupational truth.

The core sample uses a fixed SHA-256 order within rule-category buckets and round-robin selection of up to 12 rejects per employer. It includes **93 rejects**, five keeps per employer (**55**), and both UNDETERMINED cases. All reject titles were screened for risk; **seven additional targeted rejects** were inspected separately. No reject exists to sample at NXP.

| Employer | Rejects reviewed | Clearly correct | Questionable | Likely wrong |
|---|---:|---:|---:|---:|
| AECOM | 13 | 8 | 4 | 1 |
| Amentum | 1 | 0 | 1 | 0 |
| Boeing | 16 | 9 | 6 | 1 |
| KBR | 8 | 8 | 0 | 0 |
| Leidos | 13 | 7 | 5 | 1 |
| Northrop Grumman | 10 | 9 | 0 | 1 |
| NVIDIA | 2 | 2 | 0 | 0 |
| NXP | 0 | 0 | 0 | 0 |
| Parsons | 12 | 9 | 3 | 0 |
| RTX | 13 | 9 | 3 | 1 |
| ServiceNow | 12 | 10 | 2 | 0 |
| **Total** | **100** | **71** | **24** | **5** |

Core alone: **70 correct / 21 questionable / 2 likely wrong**. Targeted additions: **1 / 3 / 3**. Unequal category sampling, targeted selection, repeated title families and machine judgment prevent treating 71/100 as population precision or deriving KEEP recall. The five likely errors establish a concrete safety problem, not its true frequency. The 136 unreviewed rejects may contain more. No “99% recall” claim is justified.

| Example / stable ID | Rule IDs | Assessment and duty evidence |
|---|---|---|
| AECOM Civil OSP Project Manager – QA/QC Lead, `aecom:J10155853` | `reject-trades-construction`, `reject-civil` | **Likely wrong**, targeted: leads telecommunications infrastructure design, OSP quality and multidisciplinary technical delivery. |
| Boeing Associate Test and Evaluation Laboratory Technician, `boeing:JR2026518584` | `reject-operator-technician` | **Likely wrong**, targeted: designs test systems/components/software, integrates systems and troubleshoots lab tests. |
| Leidos McMurdo Seasonal Instrument Technician, `leidos:R-00182300` | `reject-operator-technician` | **Likely wrong**, targeted: scientific instrument installation, calibration and technical troubleshooting. Antarctica deployment is a separate preference. |
| Northrop Grumman Extended Workforce Solutions HR Project Manager, `northrop-grumman:R10245576` | `reject-hr` | **Likely wrong**, core: monitors integration errors, resolves root causes and maps enterprise application dependencies. |
| RTX ECAD Layout Designer Technician, `rtx:01868753` | `reject-operator-technician` | **Likely wrong**, core: PCB layout, schematic implementation, routing constraints and design outputs. |
| RTX Digital Technology Capture Engagement Lead, `rtx:01865916` | `reject-sales` | **Questionable**: mixed proposal coordination, program architecture and IT/engineering requirements analysis. Keep for human review. |
| Amentum VP Finance Excellence and Transformation, `amentum:R0158818` | `reject-finance` | **Questionable**: finance ownership overlaps enterprise ERP/integration transformation. |
| AECOM structural data-center design; Leidos substation civil design; Parsons drainage/bridge engineering | `reject-civil`, often `reject-trades-construction` | **Questionable**: real engineering analysis, but civil discipline versus realistic search-space boundary remains unresolved. Do not force labels to make recall look better. |

Most recurring safety risk: operator/technician wording plus broad `assembly`, `materials`, or `repairs` corroboration; HR titles masking enterprise support; civil labels hiding adjacent telecom/engineering duties; capture/supplier/finance work that can contain technical leadership. The last three include legitimate nontechnical work too, so indiscriminate family exemptions would also be wrong.

Clearly correct examples include KBR clinical psychologist/physical therapist, Boeing procurement/machining, Parsons recruiting for cyber/cloud/software, ServiceNow sales executives and NVIDIA commercial business development. Technical employer/product mentions do not automatically protect these jobs. Leidos Warehouse Associate ships COMSEC equipment; its duties differ from administering COMSEC systems.

The KEEP review includes actual Azure administration, SharePoint/Power Automate support, SAP development/application support, MILSATCOM systems engineering, cyber controls, electronic-warfare field integration, AI/software/platform work and technical leadership. They survived appropriately, but a small sample cannot certify every sensitive family. PC-support and COMSEC-administration safety need deliberate future human coverage; a warehouse COMSEC mention is not a substitute.

Nine already-rendered, profile-dependent Parsons Job Fit scores were captured in [review.jsonl](../Tests/cheap_reject_evaluation/live_shadow_v1/review.jsonl), including six reviewed rejects. Other scores remain explicitly unavailable, not zero/TBD and not inferred. No detail/provider requests were made to obtain scores. A score is contextual information, not an audit label.

## KEEP leakage and what it means for a router

Of 55 KEEP cases reviewed: **32 appropriate continuations, 10 borderline, 13 obvious unrelated leaks**. These counts describe this equal-employer sample only; they are not estimates for all 1,967 KEEP decisions.

| Leak family | Observed cases | Why it survived |
|---|---:|---|
| Commercial account/distribution/sales leadership | 3 | NXP titles such as Global Account Manager and Region Leader miss an explicit rejection family. |
| Finance/pricing/project controls | 3 | Parsons Senior Scheduler and abbreviated PROJ CTRL ENGR/SPEC; ServiceNow Pricing Manager miss a family. |
| Supplier planning/administration | 2 | Northrop supply planning and Supplier Management Engineer miss a family despite business duties. |
| Legal/labor compliance | 2 | Amentum SCA/DBA compliance and KBR Business Integrity miss a family; DBA here is labor-law terminology. |
| HR career development | 1 | Organizational Development Representative misses a family. |
| Counseling | 1 | Military Family Life Counselor misses a family despite explicit counseling duties. |
| Proposal administration | 1 | Proposal Coordinator misses a family; using AI tools is not AI engineering. |

All 13 obvious leaks stopped at **`keep-no-possible`**, not an inferred employer/software veto. Across the full cache, 1,774/1,967 KEEP decisions (**90.19%**) have no explicit unrelated title family; 94 have protected-title guards, 72 lack body corroboration, 18 have a technical-duty veto, five a support guard and four ERP/support evidence. These are engine reasons, not a manual classification of 1,774 leaks.

One additional borderline case exposes a spurious technical-duty veto: RTX Senior Supplier Development Lead matches **“develop a network”** in a sentence about professional relationships. Another supplier lead with similar quality/recovery duties is rejected. This is evidence-context sensitivity, not evidence that both are safely rejectable. Generic technical words must not become domain truth.

## UNDETERMINED

Both described cases have `failOpen=true`, category `timeout`, rule/guard `engine-timeout`; the engine's raw KEEP is correctly exposed as live **UNDETERMINED**:

- Parsons Compensation Partner – Federal Solutions: original production timestamp **2026-09-06 15:57:15 UTC**, preceding evidence `hr-title: compensation`.
- Leidos Senior Transmission Line Engineer: isolated reconciliation timestamp **2026-09-06 16:14:49 UTC**, no preceding evidence.

The engine has bounded 25 ms regex operations. Stored observations identify the timeout class, **not the exact failed predicate, elapsed time or underlying scheduling/regex cause**. Prior evidence is not proof that that predicate timed out. No malformed-input, missing-input or oversized-input failure was recorded. Title-only is generally KEEP, not UNDETERMINED. No retries silently replaced either observation.

## Readiness and next evidence

1. **Enough evidence to plan focused safety tuning, not to increase rejection yet.** Have a human review the five likely errors and 24 questionable rejects first, retaining original observations and separate review provenance. Several cases recur from earlier research; live replay confirms persistence of known weaknesses, not independent discovery.
2. Then, in a separately authorized change, test narrowly contextual candidate changes against the frozen development artifacts and this audit, inspecting changed identities rather than just counts. No candidate was created here. Keep evaluation holdouts separate.
3. More observation is needed before generalization claims: naturally acquired future caches from other industries/providers, complete bodies when normal use acquires them, employer/time-disjoint human review, title-family clustering and technical-family sentinel cases. Do not download merely for this audit. A genuinely representative human-labeled KEEP cohort is needed to measure recall; reject review alone cannot supply its denominator.
4. Layer 1 taxonomy and Layer 2 **design can begin now**. These duties provide useful specification examples and counterexamples. They do not supply independent Technology-tag labels or validated weights. No automatic Technology label may be derived from KEEP.

## Artifacts and validation

Portable evidence and reproducible reporting live under [live_shadow_v1](../Tests/cheap_reject_evaluation/live_shadow_v1/README.md): all cache-instance decisions/provenance, 157 review entries, summary, manifest, offline builder and manual audit notes/available-score input. Full descriptions and isolated runtime logs remain in `/home/codex/jsm-lab/experiments/live-shadow-audit-20260906` on curiosity, referenced by hashes; they are not duplicated into Git. Original research remains intact.

Offline validation checks raw/per-employer totals, stable-ID deduplication, current version/hash/adapter, preserved original observations, deterministic sampling, review joins, judgment domains, metric recalculation and artifact hashes. Final production health/state and source/whitespace validation are recorded in [validation.json](../Tests/cheap_reject_evaluation/live_shadow_v1/validation.json). No .NET/runtime implementation changed, so deployment/build/security reruns are unnecessary for these documentation/reporting additions.
