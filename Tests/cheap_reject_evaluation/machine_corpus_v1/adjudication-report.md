# JSM Codex cheap-triage corpus adjudication

Completed 3,198 provisional machine decisions. No Qwen, local/generative labeling service, training, production changes, rule changes, or commits.

Codex directly reviewed every title and recorded explicit decisions. The helper scripts present evidence and serialize those decisions; they do not classify text. Bodies were reviewed via recorded excerpts and targeted additional spans, **not exhaustively in full**. Confidence describes the reviewed evidence, not calibrated probability.

## Distribution

| Evidence availability | Total | KEEP | REJECT | AMBIGUOUS |
|---|---:|---:|---:|---:|
| No description | 1886 | 1207 | 303 | 376 |
| Description available | 1312 | 914 | 245 | 153 |

Overall labels: {'AMBIGUOUS': 529, 'KEEP': 2121, 'REJECT': 548}. Confidence: {'high': 1014, 'low': 529, 'medium': 1655}.
Missing-description title-only decisions: 1886; available-but-insufficient description title-only decisions: 1.

## Label definition and evidence limits

KEEP means plausibly relevant technical or adjacent duties, including software, infrastructure/support, cybersecurity, engineering analysis, controls/electronics and technical integration. REJECT means clearly unrelated primary work. AMBIGUOUS means insufficient or mixed evidence and must not be silently mapped to REJECT or forced into binary training truth. Employer/product names and generic technology terms are not sufficient evidence. Technical presales can KEEP when hands-on architecture, deployment or debugging duties are present. Personal eligibility and final application fit were not assessed.

The 1,886 description-missing rows are 58.97% of this corpus. All decisive labels in that partition are medium confidence, and its AMBIGUOUS labels are low. Treat them as weak title labels, separately from description-supported data. Do not use them as body-classifier ground truth or as a human-gold evaluation set. Even high-confidence machine labels require human validation before drawing 98–99% recall conclusions.

A targeted second read changed Extended Workforce Solutions HR Project Manager from REJECT to KEEP: integration monitoring and root-cause resolution across VNDLY, Workday and ServiceNow are substantive technical duties. This illustrates missing-body risk; it is not an estimated error rate for all title-only rows.

## Sampling and coverage

2659 normalized titles, 2139 lexical title-family clusters, 11 employers. The eligible frame had 3,198 representatives, so all were retained rather than artificially balancing labels. Employer counts: {'aecom': 78, 'amentum': 11, 'boeing': 581, 'kbr': 27, 'leidos': 1789, 'northrop-grumman': 38, 'nvidia': 340, 'nxp-semiconductors': 7, 'parsons': 141, 'rtx': 99, 'servicenow': 87}.
The preparation manifest preserves source paths/hashes, identity versions, duplicate clusters, seed, exclusion indexes and sampling rules. Title families are lexical connected components, not validated occupational categories. Missing bodies limit near-duplicate detection. This is a selected JSM cache distribution, strongly dominated by Leidos, not a random job-market sample.

## Quality and consistency

45 targeted second reads; 13 class changes, retained in quality-checks.json and the final records' initialDecision fields.
Repeated title-only groups: 207 (602 rows); conflicting class labels: 0. Identical nonempty-body groups: 2; conflicting classes: 0.
8 identical-title groups differ across description availability. Examples include Senior Program Manager (title ambiguous, network-delivery duties KEEP), Mid Cartographic Analyst (title ambiguous, GIS duties KEEP), and Material Project Manager (title ambiguous, procurement duties REJECT). Different postings are not paired counterfactual labels.
Observed title/body conflicts: 10; technical-jargon/duty conflicts: 91. Flags apply only to reviewed evidence; missing-body conflict fields remain unknown.
Internal repeated-evidence agreement supports consistency but does not measure correctness. No independent reviewer, random blinded re-adjudication, human accuracy, KEEP recall, or false-reject rate was measured. Do not translate zero duplicate disagreements into 100% labeling accuracy.

## Human review

human-review.jsonl and human-review.csv contain 281 cases, including 201 low-confidence cases. Selection includes all flagged conflicts plus up to 100 ambiguous cases per description partition with employer/category coverage. It is an intentionally enriched review queue, not a population-risk estimate.
Difficult title-only examples include technical writers, systems/engineering technicians, program managers, intelligence analysts, product owners and integration-adjacent support roles. Missing descriptions prevent a reliable distinction between hands-on technical duties, operational use of systems and administrative coordination.

## Examples

**KEEP**

- .NET Developer (leidos, 3150747f02ec7a7bbd431747): Explicit software development or software engineering work. [high; both]
- 3D ORD Modeler (parsons, d9c4460a0c2d86f7df2a9775): Plausibly adjacent engineering, controls, electronics, integration or technical analysis. [high; both]
- ABAD COMSEC Custodian**This position is located at Ramstein AFB, Germany (International Assignment) **NO REMOTE WORK** (parsons, 5807086e7a0842bd876e3239): Cybersecurity or technical information-security work. [high; both]
- ABAD Systems Engineer - Ramstein AFB, Germany (International Assignment) **NO REMOTE WORK** (parsons, ab365679c317a406365c480d): Plausibly adjacent engineering, controls, electronics, integration or technical analysis. [high; both]
- ABAD Test Coordinator and Analyst**This position is located at Ramstein AFB, Germany (International Assignment) **NO REMOTE WORK** (parsons, 51390a21f4bf3297f6f60f36): Plausibly adjacent engineering, controls, electronics, integration or technical analysis. [medium; both]

**REJECT**

- 01856815 Supplier Performance Specialist (Remote - Residing in Phoenix, AZ Area) (rtx, 975293daced4b75782155ecd): Primary duties are nontechnical business administration, clerical or procurement work. [medium; both]
- ABAD Project Scheduler**This position is located in Ramstein AFB, Germany (International Assignment) **NO REMOTE WORK** (parsons, 06df5ff2376c1e68cb16c25a): Primary duties are nontechnical business administration, clerical or procurement work. [high; both]
- Aeronautical Information Specialist (leidos, b39b71f3d52b319902594308): Primary duties are nontechnical business administration, clerical or procurement work. [high; both]
- AH-64 Product Repair Modification Tech (boeing, 49cfefabdda2cb095c618603): Primary occupation is physical operations, driving, trades or nontechnical production. [high; both]
- Airborne Mission Systems Instructor (leidos, 92307a3977dedc2d3cde107b): Primary occupation is nontechnical instruction or training administration. [high; both]

**AMBIGUOUS**

- ,SENIOR FACILITY ASSESSOR (parsons, 86e312c45964a75d61513939): Adjacent role could involve technical work, but the available evidence is insufficient to decide safely. [low; both]
- AI Product Strategy & Operation Senior Manager (servicenow, 7bc2ca9a2e023441e561975f): Technical title/context and primary-duty evidence are mixed or conflicting. [low; both]
- Airport Site Lead (leidos, 48923071900220412d0ccc91): Adjacent role could involve technical work, but the available evidence is insufficient to decide safely. [low; both]
- ALTERNATIVE PROJECT DELIVERY DESIGN MANAGER RAIL & TRANSIT (parsons, 60369f0c60cc2b7b51c4abbf): Adjacent role could involve technical work, but the available evidence is insufficient to decide safely. [low; both]
- Architect (leidos, 375947ca2814f16cb0e3bec8): Adjacent role could involve technical work, but the available evidence is insufficient to decide safely. [low; both]

## Reproduce and validate

From this directory, run `python -X utf8 quality_reviews.py`, `python -X utf8 build_labeled_corpus.py`, and `python -X utf8 -m unittest discover -s . -p "test_*.py" -v`. Frozen decision ledgers are the semantic input. Original sample and exclusions remain immutable.

See validation-results.md for the actual checks run. Stop here: no classifier has been trained or enabled.
