# Declarative classification pipeline — investigation and design, 2026-09-06

**Proposal only.** No runtime/config/rule/UI implementation, training, provider fetch, deployment or mode change accompanies this document. Cheap Triage remains Off/Shadow only. The [cross-employer audit](live-cheap-triage-shadow-audit.md) provides examples and limitations, not new training truth.

Recommended sequence: **resolve the safety/scope review, establish a manifest with exact legacy parity, add independently evaluated domains and Technology tags in Shadow, and expose one maintenance action across the entire manifest.** Design the single maintenance contract with the manifest, rather than bolt it on after separate layer workflows exist.

## Existing architecture and constraints

| Existing component | Finding | Consequence for the proposal |
|---|---|---|
| `CheapRejectRules.cs`; `CheapTriage/rulesets/1.0.0.json` | Immutable rule file, 68 named predicates, 23 ordered rules; bounded matching, Boolean references and evidence. Seven KEEP guards short-circuit occupation matching. | Reuse generic bounded predicates/validation. Current output semantics cannot simply become domain labels: most KEEP is absence of a matching rejection family. |
| `CheapTriageShadow.cs`; `JobCatalog.CheapTriage.cs` | Off/Shadow; title/body fingerprint, adapter and ruleset identity; matching outside locks and merge into latest shared cache. | Extend through additive observation records only in a future implementation. Preserve input checks and merge-only persistence; never mutate history/Job Fit. |
| `RuleMaintenance.cs`; `CheapTriage/maintenance-prompt-v1.txt` | One cheap-triage prompt uses a static packaged evaluation context and current cheap rules identity. | It does not currently inventory all layers or automatically embed live review evidence. Renaming the button alone would not satisfy the requirement. |
| `JobConceptCatalog.json`; `JobConceptCatalog.cs` | External canonical Job Fit concepts with definitions, supersession and location/travel metadata. | Reuse stable concept IDs and taxonomy; do not generate a competing concept namespace. |
| `SemanticRules.cs`; `SqliteSemanticRuleStore.cs`; `RegexSemanticClassifier.cs` | Separate mutable SQLite-backed lifecycle, relationships, matching telemetry, runtime snapshots, exclusions, required-context groups and remote/location signals. | A file-only manifest does not currently describe actual loaded Job Fit rules. Export/identity/migration must be explicit; preserve lifecycle history and match statistics. |
| `wwwroot/job-fit.js` and semantic classification/cache services | Profile-dependent score calculation, preferences, dimensions, caps and existing cache freshness are separate from occupational rejection. Some presentation/scoring knowledge remains in JavaScript. | Do not conflate tags with scores or claim that externalizing C# rules already externalizes the entire scoring/UI system. Preserve these semantics; any later declarative scoring migration needs a separately reviewed parity specification. |
| Existing research CLI, datasets, model archives and frozen evaluations | Useful historical evidence, including known false rejects and weak labels. | Preserve all; distinguish development, diagnostic reuse and locked evaluation. Do not revive model inference or expose retired research execution as an Admin action. |

The historical readiness document describes an earlier offline-only release; the current release has live Shadow. Its warning remains relevant: **normal Jobs does not have a newly established automatic expensive Qwen operation to save**. A richer router can aid classification/explanation without inventing such a workload or gating cheap Job Fit.

## Layer 1: broad occupational domains

Model **primary duties**, not employer industry, as independent domain evidence. Use **multi-label results**, optionally with a display-only leading domain when evidence is decisive. An IT role in a hospital is Technology; a nurse is Healthcare; an engineer implementing clinical systems may have both. A sales engineer can have Sales and Technology. A developer recruiting other developers is not necessarily HR; responsibility and context matter.

Keep three concepts separate:

- `domains`: positive supported occupational evidence, with supporting/counterevidence and input coverage.
- `assessmentStatus`: assessed, insufficient evidence, conflict, timeout or error. **Unknown is not Other**; no evidence must not become an asserted occupation.
- `continuationAdvice`: the separately versioned, conservative user-search policy. During Off/Shadow, every job still continues. A nontechnical domain label is not itself permission to discard.

Proposed external taxonomy:

| Domain | Scope and present evidence | Gaps / mapping cautions |
|---|---|---|
| Technology | Software, computing, digital systems, cloud, security, enterprise support; many audited KEEP duty examples. | KEEP cannot be imported as Technology truth. Positive duty labels are still needed, including titles that currently REJECT. |
| Engineering / Applied Science | Electronics, controls, physical systems, technical analysis, civil/structural and scientific instrumentation. | Use subdomains; human review must define which are plausibly adjacent. Civil OSP, ECAD and instrument support show why blanket construction/operator mapping fails. |
| Healthcare / Clinical / Counseling | Current clinical rejects and the leaked family counselor. | Small, employer-concentrated sample. Missing nonclinical healthcare administration/IT contrasts. |
| Transportation / Logistics | Driving, shipping, warehousing and vessel operations. | Distinguish transport operations from transport software/sensor engineering. Few examples; supply chain can overlap Business Operations. |
| Finance / Accounting | Tax, accounting, payroll, financial control and pricing work. | Current `auditor` predicate also catches EHS/quality audits; category strings cannot be blindly imported. ERP implementation remains Technology even in finance. |
| Construction / Trades | Building/site execution, physical installation and trades. | Civil design may additionally be Engineering. Separate electrical systems analysis from generic wiring/inspection. |
| Sales / Business Development | Quota sales, commercial accounts and capture. | Technical presales, architecture and product ownership can overlap Technology; commercial product names are not technical duty evidence. |
| Marketing / Communications | PR, messaging, campaigns and product marketing. | Hands-on technical benchmarking/writing may overlap Technology/Engineering (NVIDIA CAE example). |
| HR / Recruiting | Recruiting, talent and personnel programs. | HR integration/support must also receive Technology evidence. |
| Hospitality / Food / Personal Services | Direct service, food and hospitality occupations. | No corroborated rejects in this snapshot; needs future naturally acquired corpus coverage. |
| Physical Security / Protective Services | Guarding, patrol and physical protection. | No current corroborated rejects; distinguish cyber, COMSEC administration, security training and systems installation. |
| Business Operations / Administration / Legal | Procurement, contracts, project administration and legal/compliance work. | Current business/supplier/cost categories mix financial administration and technical quality/process improvement. |
| Field / Manufacturing / Maintenance | Physical production, machine operation, equipment maintenance and environmental fieldwork. | Not intrinsically unrelated: instrument support and automated testing can overlap Technology/Engineering. |
| Other Known Occupation | A positively established occupation outside the listed domains; retain a specific descriptor where possible. | Do not use as the default for unmatched titles. Environmental science/archaeology can be refined later without C# changes. |

This slightly larger taxonomy avoids burying observed manufacturing, environmental and business work in an opaque Other. Names/IDs, subdomain relationships, definitions and examples belong in files. The schema defines generic structures, not an enum of occupations in C#.

**Mapping current rules:** clinical → Healthcare; driving → Transportation; finance → Finance only after audit-type context validation; sales → Sales; HR → HR with technical overlap preserved; hospitality/physical-security → corresponding domains; civil → Engineering/civil with Construction where supported; trades → Construction or Field/Manufacturing depending on duties; operator/technician → no unconditional domain mapping; business-other/supplier/cost/marketing/environment → explicit subdomain review. KEEP guards and engine fallbacks are control results, not occupational domains.

A future router must collect relevant evidence across domains instead of stopping at the first KEEP guard. Preserve the current electrical-safe engine as a frozen reference while evaluating that new behavior offline. Reusing current predicates as **candidate evidence** is reasonable; translating them mechanically into asserted labels is not.

There is enough evidence to draft Technology, Engineering, Finance, Sales, HR, Construction and Business definitions and contrast pairs. There is not enough independently adjudicated evidence to certify any of them. Healthcare/transport/field evidence is thin; hospitality and physical-security coverage is absent or incidental. Additional corpus work should use normal cache acquisition and human adjudication, not new provider traffic for this task.

**Safety:** uncertainty, missing bodies, timeout, conflicting duties or a plausible technical subdomain always preserve continuation. Strong nontechnical and technical evidence can coexist; neither erases the other. A future decision policy must explicitly inspect substantive technical duty evidence, not just the largest domain score. No Active execution mode is proposed in the next implementation.

## Layer 2: independent weighted Technology relevance tags

Use the requested ten independent tags:

| Tag | Useful positive evidence | Important contrast |
|---|---|---|
| Software Engineering | Design/implement/test code, APIs, services and applications. | Selling software or “application” paperwork. |
| Cloud Engineering | Build/administer cloud infrastructure/services and cloud solution architecture. | Selling cloud subscriptions. |
| DevOps / Platform | CI/CD, infrastructure automation, deployment platforms, reliability/operations. | Generic business “operations” or pipeline sales. |
| Systems Administration / Infrastructure | Servers, storage, identity, networks and enterprise operations. | Logistics “systems” or procurement ERP usage. |
| Cybersecurity | Technical security controls, engineering, incident response and security systems administration. | Recruiting cleared cyber staff or physical guarding. |
| AI / ML / Data | Build/train/evaluate non-generative or generative systems as a job duty, data pipelines/analytics engineering. | Merely using an AI writing tool. This classification design does not authorize invoking one. |
| Technical Support | Diagnose/configure/repair IT or enterprise applications and support users technically. | Payroll transaction inquiries without system implementation/support. |
| Integration / Systems Engineering | Interfaces, requirements, integration, verification and cross-system technical architecture. | Professional networking or coordination without substantive technical work. |
| Controls / Electronics / Engineering | PCB/ECAD, embedded/electronic systems, instrumentation, controls and engineering analysis. | Generic assembly, unrelated physical maintenance, or unqualified “engineer” title. |
| Other Technical | Specific demonstrated technical duties not represented above. | Not a fallback for no evidence or a mechanism to hide taxonomy gaps. |

Tag values measure **rule-supported relevance**, initially on 0–1. They are **not calibrated probabilities**, confidence, suitability or Job Fit. Values need not sum to one. Track coverage/conflict/unknown separately; a missing description is not negative duty evidence. Use `null`/unassessed for unavailable assessment and distinguish it from an assessed zero.

Technology tags should be eligible for Technology **or plausibly adjacent Engineering or unresolved domain evidence**. In initial Shadow evaluation, run them on all cached postings to expose Layer 1 misses and avoid circular evaluation. Do not let a provisional Layer 1 domain suppress collection of the evidence needed to test that layer. Job Fit concept detection remains independent and runs normally for every posting.

### Declarative evidence and aggregation

Extend bounded predicate vocabulary only where a real requirement exists: source scope, text spans, Boolean combinations, bounded contextual co-occurrence, exclusions, stable evidence-group IDs and numeric weights/caps. No arbitrary C# snippets, scripts, model calls or unbounded expression interpreter in a rule file. Domain/tag IDs remain data. Generic English context/exclusion patterns should be externalized too if they contain classification knowledge.

Each emitted evidence item has a rule/predicate ID, tag/domain ID, source scope and offsets into a versioned normalized input, exact matched text, evidence group, sign, weight and reason. Preserve title separately. Scan the bounded whole cached body; do not import the failed classifier's body-prefix truncation. Oversized input should be explicitly incomplete/fail-open, not silently presented as a full assessment. Duty sections can boost quality, but postings without headings must still work; employer introductions, benefits and product descriptions are weak context rather than sufficient occupational proof.

One candidate **generic** scoring operator for offline evaluation:

```text
positive_g(tag) = max(eligible positive evidence weights in group g, default 0)
negative_g(tag) = max(eligible counterevidence weights in group g, default 0)
raw(tag) = bias(tag) + sum_g positive_g(tag) - sum_g negative_g(tag)
score(tag) = clamp(raw(tag), 0, 1)
```

Biases, weights, group definitions and caps live in the layer file. Same-span/synonym evidence in the same group contributes once; repeated job boilerplate cannot inflate a score. A phrase may support multiple tags legitimately, with independent configured weights, but not be counted repeatedly within one tag via overlapping groups. Define deterministic ownership/tie-breaking for overlaps and record the aggregation trace. Counterevidence limits the affected sense/span/group, not every tag in the posting. Missing evidence is not counterevidence.

Title evidence can suggest a tag but should not saturate a high score without duties. Body support must represent what the employee builds, operates or analyzes, not what the employer sells. “Develop a network of relationships” must not count as network engineering. “HR Project Manager” must not suppress direct integration-error/root-cause duties. These are evaluation requirements, **not new regex rules added by this task**.

Illustration only: Cloud 0.65, DevOps/Platform 0.70, Software 0.25 is valid and sums to 1.60. No actual weights, thresholds or confidence claims are fitted here. Compare transparent capped additive scoring against simpler binary evidence before adding complexity; do not invent data-derived weights from Shadow outputs.

### Evaluation and Job Fit separation

Human reviewers should annotate independent tag presence, primary/secondary/incidental relevance and decisive duty spans, with disagreement and description availability retained. Agreement on ordinal categories precedes claims about precise numeric scores. Build employer/time-disjoint development and evaluation sets, group duplicate/near-duplicate requisitions, and keep existing blinded production holdouts unavailable to tuning. Include all five live likely false rejects and the mixed cases as **development/sentinel examples**, not independent test evidence.

Cover at least dozens of positive and hard-negative duty examples per tag to establish definitions, then size the independent evaluation to the intended error claim. For orientation, zero misses in 299 independent representative human-labeled positives gives a one-sided 95% binomial lower recall bound of about 99%; 149 gives about 98%. These are mathematical minimums for that zero-error scenario, **not a claim that 299 total mixed jobs certify ten tags**, and clustered employer/title samples violate the simple independence assumption. Errors, subgroups and multiple-tag claims require larger samples or weaker conclusions.

Evaluate per-tag recall/precision, ordinal agreement, title-only/described results, employer/title-family shift, overlap correctness, conflict/unknown frequency, latency and changed-record lists. Report both unconditional tag performance and conditional results among Layer 1 entrants; otherwise Layer 1 misses disappear from the denominator. Weight/threshold choices come from development/validation only. Keep a separate human-review queue for errors and contradictions.

The present 2,205 caches offer **bootstrap examples, not tag truth**. The 55 KEEP reviews show most proposed tags, but do not exhaustively label them. Existing 85 Job Fit concepts may provide explainable candidate evidence, not independent tag ground truth. No human labels or calibrated weights were created here.

Later, a separately versioned Job Fit consumer could read immutable tag outputs through a documented interface. It must decide how overlapping skills avoid double reward, include user-preference identity in score freshness, and handle missing tags without inventing 0/10 or stale TBD. Do not inject tags into `DetectedConcepts`, replace current concept IDs, or gate existing list/detail scoring. In the first migration **Job Fit ignores the new tag layer**, preserving parity.

## Unified declarative pipeline

The target is one manifest-driven stack, not three disconnected rule systems. A possible organization:

```text
ClassificationPipeline/
  pipeline.json
  schemas/
  layers/
    broad-domain/<version>.json
    technology-tags/<version>.json
    job-fit-concepts/<version>.json
  taxonomies/
  input-profiles/
```

Paths are illustrative. Initially reference existing immutable CheapTriage files and a compatibility adapter for the actual Job Fit snapshot; do not move/copy history simply to match this layout. The transition is not “fully declarative” until actual runtime knowledge is represented in external files and parity is proven.

The manifest should declare schema/pipeline version, layer IDs, paths, expected hashes, independent layer versions, generic operator/output contract versions, taxonomy/input-profile references, dependency edges and evaluation policy. Use a validated DAG, not magic numeric filenames. Pipeline registration, ordering and named outputs are data; C# implements only bounded generic operators, loading, schema/reference validation, execution, identity and persistence in the final architecture.

Illustrative **non-executable contract**, not a deployable manifest:

```json
{
  "schemaVersion": 1,
  "pipelineVersion": "proposal-only",
  "layers": [
    {"id": "broad-domain", "operator": "bounded-multilabel", "definition": "layers/broad-domain/<version>.json", "dependsOn": []},
    {"id": "technology-tags", "operator": "bounded-weighted-evidence", "definition": "layers/technology-tags/<version>.json", "dependsOn": ["broad-domain"]},
    {"id": "job-fit-concepts", "operator": "concept-evidence", "definition": "layers/job-fit-concepts/<version>.json", "dependsOn": []}
  ]
}
```

`dependsOn` makes preceding output available; it must not silently mean exclusion. Eligibility/unknown behavior is explicit data. Job Fit has no routing dependency. Initially tag collection runs for all jobs in Shadow even if a future display groups them as Technology. Deployment environment retains Off/Shadow settings separately from semantic taxonomy; there is no Active option to add.

### Identity, loading and persistence

- A **layer content hash** covers exact UTF-8/LF definition bytes and hashes of every referenced taxonomy/input/executable-config asset. The file's version is included. A separate execution identity includes operator/normalizer version and resolved dependency-output identities. Weights and exclusions are behavior and must affect it.
- A **pipeline content hash** covers a specified canonical descriptor of manifest version, ordered layer IDs/versions/hashes, dependencies, schemas and referenced input profiles. Define canonicalization, encoding and ordering in the schema specification; exclude the hash field itself. Execution identity additionally records engine build/operator versions. Timestamps, live counters and generated review queues are provenance, not classification content.
- Reject duplicate properties/IDs, unknown operators, escaping paths, missing/mismatched hashes, invalid numeric ranges, cyclic references, incompatible contracts and unbounded regex/evidence complexity. Keep regex/resource limits, explicit timeout/error states and last-known-good behavior. No network rule resolution or runtime file discovery outside the declared bundle.
- Load/validate the entire bundle before publishing one immutable snapshot. In-flight work keeps its snapshot. A file edit alone must not partly activate a new layer. In the migration, immutable startup loading is simpler than adding hot reload. Existing mutable Job Fit reload requires an explicit compatibility boundary until migrated.
- Persist per-layer output with input hash, referenced dependency result hashes, execution identity, ruleset version/hash, evidence and UTC time, plus observed pipeline identity. Recompute only changed/stale layers and true downstream dependencies; an unchanged independent Job Fit layer stays current when Technology weights change. A pipeline hash change alone must not invalidate every concept/score if its dependency closure is unchanged.
- Reuse shared source-cache merge/concurrency semantics. Match outside locks; recheck latest input and snapshot before storing; never overwrite newer Job Fit/history. Keep old observations readable as historical/stale, and distinguish engine timeout from genuine semantic uncertainty. No hidden/deleted/workflow transitions or scoring shortcuts.

### Job Fit SQLite migration is the main architectural risk

Today the active runtime rules can differ from seed JSON. **Do not treat `LegacyJobConceptRules.json` as the current source of truth.** First export the actual loaded semantic snapshot and taxonomy under a stable snapshot identity. Preserve every rule ID, scope/type/context group, relationship, lifecycle metadata and provenance. Separate behavior-bearing definitions from mutable match/timeout counters and review timestamps so telemetry does not continuously change classification hashes.

Stage 1 can register a read-only compatibility adapter with the actual runtime fingerprint; its dynamic state must be visible in the pipeline identity and maintenance prompt. Do not pretend a manifest's seed-file hash proves the live SQLite snapshot. This stage preserves behavior but is an explicitly temporary exception to the generic-engine endpoint.

Stage 2 makes approved versioned external definitions authoritative and SQLite a telemetry/history/import projection. Import the **currently active rules**, including review-due rules, not just historically validated seeds. Preserve archived/deleted rule history in SQLite and exports. Migrate existing rule-edit controls to create candidate bundle changes through the same maintenance/review path; no independent mutable runtime writer after cutover. Version/hash conflicts must block stale proposals. Rollback restores the previous bundle/projection without deleting telemetry or user data.

Port semantic matching carefully: title-only exclusions, required-context grouping, positive-evidence/local negation, remote/extended-location signals, supersession and current taxonomy validation are not interchangeable with cheap-triage Boolean rules. Move knowledge-bearing patterns/configuration to files while retaining generic semantics. Structured remote/location inputs must themselves carry producer/version identity. Run exact concept/evidence/rule-match parity before changing the engine behind Job Fit; preserve list/detail freshness and scoring. Do not attempt this as incidental cleanup during a routing change.

The hard requirement applies to all **classification knowledge** in the stack. Existing profile scoring and UI definitions are an adjacent concern: inventory them explicitly and avoid claiming the whole application is generic. Externalizing any of those later must preserve preferences/caps and receive its own versioned contract and tests. One maintenance request still assesses whether a layer change affects the score consumer; it does not silently rewrite preferences.

## One Admin action: Update Classification Rules

The final visible maintenance action is **Update Classification Rules**. Per-layer diagnostics, history, comparison and review tabs can remain; they are not separate update steps the user must remember. One request reviews **every configured layer**, even if only one changes.

The action generates an immutable maintenance request artifact, displayed in a text modal with Copy Prompt, preview/download of evidence references and Close. V1 remains preparation only: no process launcher, model call, scheduler, edits, activation or deployment. A future executor would require separately authorized scope and is not designed into this implementation step.

The server-generated request captures:

| Section | Required content |
|---|---|
| Stack inventory | All manifest-discovered layer IDs, definitions, versions/content and execution hashes, dependency graph, pipeline identity, runtime/checkout drift and Off/Shadow mode. |
| Live evidence | Observation time/window, employer/provider/query scope, unique IDs versus cache instances, current/stale/input coverage, per-layer outcomes, failures and sampling method. Distinguish production traffic from isolated replay. |
| Offline evidence | Dataset hashes, purpose/split/label provenance, per-layer and end-to-end metrics, frozen comparisons, known errors; clearly mark stale or unavailable results. |
| Review queues | Human-confirmed versus machine-suspected errors, unresolved cases, sampled IDs, supporting passages, matched rules, snapshot hashes and reviewer provenance. Never silently convert audit judgments into labels. |
| End-to-end effects | New/changed records by layer and dependent layer, continuation safety, unchanged Job Fit concepts/list/detail scores, missing/stale results and actual measured cost if such a workload exists. No fabricated “calls saved.” |
| Constraints | High-recall safety, unknown continues, no Active gating, no provider downloads/model inference, no hidden holdout tuning, no history/data deletion, no unauthorized commit/push/deploy. |
| Deliverables | Disposition for every layer: unchanged, candidate change or blocked; exact diff, new versions only for changed content, evaluation evidence and unresolved decisions. |

Large evidence must be **referenced with hashes, counts and deterministic samples**, not dumped without bounds into the prompt. The prompt must explicitly state any omitted/truncated content and how to access the complete authorized artifact. Include no credentials, profile preferences unless necessary and explicitly scoped, transient container IDs as rules, or arbitrary cache paths as application config. Sanitize/redact private data; treat job descriptions as untrusted quoted data, not instructions. Retain the existing Admin authorization, rate limiting, no-store response and safe textarea rendering.

Recommended maintenance procedure embedded in the single prompt:

1. Verify the expected pipeline and every layer's loaded identity, repository rules and input/evaluation hashes. Stop on drift before proposing edits.
2. Review current diagnostics and safety queues for **all layers**. Mark evidence absent/stale explicitly. Do not fabricate metrics or regenerate labels.
3. Reproduce the baseline in an isolated offline path; select only the development datasets authorized for tuning. Locked evaluation remains validation-only under its established policy.
4. Propose bounded declarative changes; preserve unchanged layer bytes/version/hash. Increment changed layer versions and pipeline version, explaining dependency impacts. Do not bundle unrelated “improvements.”
5. Validate the complete candidate stack and compare every changed output against the baseline, including dependent effects, technical sentinels and independent Job Fit parity. Equal aggregate counts do not excuse swapped false rejects.
6. Return one review packet for all layers, with tests, rejected ideas, unresolved human decisions and exact candidate identity. Leave activation to normal reviewed release authorization.

A draft made for a different pipeline/hash must be rejected as stale. **Reviewing every layer does not require editing every layer**; maintenance timestamps belong in the request/audit ledger, not unchanged rule files.

## Validation plan for later implementation

This task only validates its offline artifacts/source integrity. Future runtime implementation should add focused tests for:

- schema/reference/DAG/path/hash validation; byte-deterministic cross-platform identities; bounds, timeout and incomplete input;
- legacy electrical-safe parity and independent Job Fit concept/evidence/score/cache parity, including persisted list scores before detail opens;
- mixed HR/IT, ECAD, COMSEC administration versus shipping, SATCOM, PC support, technical leadership and civil/controls scope cases;
- multi-label domain overlap; unknown versus Other; no sum-to-one requirement; capped repeated evidence; negation/context and title/body disagreement;
- dependency-aware invalidation, body arrival, concurrent refresh and atomic snapshot activation without overwriting state;
- all jobs preserved for every decision and error; no Active enum/config/API; no triage-only provider requests;
- one prompt dynamically enumerating all configured layers, live/offline separation, hash freshness, private/untrusted-data handling, copy/modal errors and unchanged-layer preservation;
- full .NET/source/UI, container/image/security and exact-commit CI when runtime code changes, using curiosity for Linux work; no weakened gates.

## Recommended implementation sequence and decisions

Updated by the focused [human-review preparation and rule-safety plan](cheap-triage-human-review-preparation.md). This sequence supersedes the original A–E sketch. Preparation is complete; implementation remains unauthorized.

1. Complete the 29-case human review and agree on broad technical/engineering scope, retaining AMBIGUOUS as continuation and preserving historical labels.
2. Correct Layer 1 safety in a separate minimal declarative candidate, with changed-record review and full evaluation. Do not bundle new rejection-volume families or architecture changes.
3. Freeze the accepted corrected Layer 1 rules/hash, engine/input semantics, outputs/evidence and review provenance. Preserve original electrical-safe too. Subsequent legacy parity refers to the corrected baseline.
4. Introduce the top-level manifest with compatibility adapters and actual loaded identities, including Job Fit’s SQLite snapshot rather than its legacy seed JSON. Define the unified maintenance-request contract now; preserve all behavior.
5. Move corrected Cheap Triage into generic Layer 1 execution with exact parity, additive persistence, bounded/fail-open execution and cached-only reconciliation. No occupation knowledge in C# and no Active gate.
6. Complete one **Update Classification Rules** request covering every configured layer, live/offline evidence and unchanged-layer preservation. Per-layer diagnostics can remain; separate maintenance actions must not be required.
7. Only then implement independently evaluated Layer 2 weighted Technology tags in offline/Shadow mode under that same manifest/action. KEEP does not mean Technology, and tags do not gate or change Job Fit.

Job Fit’s file-authority/SQLite-history migration remains a separately reviewed parity task. A compatibility adapter is an explicit transition, not a claim that the whole stack is already generic/file-authoritative. Do not delay the safety correction for this migration or hide mutable runtime state behind a static seed hash.

**Human decisions before safety changes:** adjudicate the prepared cases using broad occupational scope, especially routine versus analytical technicians, technical versus administrative leadership, and presales/product duties. The clarified scope includes genuine civil/structural/power engineering even when David would not personally apply. No human decisions have been inferred, no rules changed and no manifest or Layer 2 implemented.
