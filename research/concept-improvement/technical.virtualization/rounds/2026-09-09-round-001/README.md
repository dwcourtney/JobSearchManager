# Virtualization improvement — accepted

One sealed candidate adds ten target-only rules and narrows the original literal
rule locally for data and UI paging virtualization. All original stable IDs remain.
The other 84 concepts, taxonomy, scoring, engine and regex policy are unchanged.
309 total rules / 85 concepts; 13 Virtualization rules. Production JSON is logically
identical to the sealed candidate; it retains existing property order and mechanically
renumbers canonical execution positions. The pinned ruleset version remains 1.0.0;
new byte/pipeline hashes identify the revision.

10,971 copied records yield 1,863 usable unique postings. Prior global-500/holdout-200
reference overlap excludes 622, leaving 1,241 eligible for new Virtualization labels.
Some text appeared in earlier AI/CI-CD-specific work; this is not never-seen text.
Mining used 622 historical records plus 100 discovery records before sealing.
Predeclared lexical enrichment: discovery 25 cues + 75 controls; each validation
29 cues + 171 controls. Labels are blinded independent A/B AI references with third
adjudication, not independent human ground truth or population accuracy estimates.

| Sample (positive support) | Baseline P/R/F1/AP | Candidate P/R/F1/AP |
|---|---|---|
| Discovery 100 (24) | 100 / 62.50 / 76.92 / 71.50 | 100 / 95.83 / 97.87 / 96.83 |
| Validation 200 (29) | 100 / 68.97 / 81.63 / 73.47 | 100 / 82.76 / 90.57 / 85.26 |
| Second validation 200 (30) | 94.74 / 60 / 73.47 / 63.02 | 96.15 / 83.33 / 89.29 / 84.29 |

No fresh false positives added. One inherited service-virtualization FP remains.
Post-hoc exclusion of near-duplicate text retains gains on both samples: F1
82.93→88.37 and 70→89.36. Historical global-500 reaches 100% for this target, but
is development-exposed regression evidence. Old holdout-200 worsens (F1 75→72.73)
with two extra machine-reference FPs: explicit VMware familiarity and VM backup
work. Their labels remain unchanged; the review explains why the matches are
plausible under the existing definition. No silent relabeling or metric adjustment.

All 84 other concepts have exact presence, evidence and accepted-rule parity across
4,620 dataset comparisons. Whole-corpus target changes: 27 additions, 2 removals.
Current eligible cache: 19 changes / 1,369 postings. All changed IDs and evidence
are in regression-results.json. Actual Job Fit caller comparisons preserve all
neutral-preference scores and all non-target dimensions; directional preference
effects follow the unchanged scorer.

The 34 authored boundary checks have one documented pre-existing negation failure
("does not use virtualization" matches in baseline and candidate). The targeted
production tests cover 29 semantic cases. KVM/VM/Horizon/VMS ambiguity is guarded;
unobserved product families are omitted. Harvester has only one supporting example
and requires Kubernetes context (see https://docs.harvesterhci.io/v1.4/).

Raw cache exports, AI sessions and host logs stay outside Git. This source-safe
research evidence is excluded from normal application compile/publish/Docker context.
The immutable previous AI candidate is linked into the test project only as the
exact pre-Virtualization rule baseline.
