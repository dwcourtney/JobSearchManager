# Decision: KEEP REGEX; MODEL PATH STILL INSUFFICIENT

The input changes substantially improve recorded-evidence coverage, but they do not fix the deployment-relevant recall/rejection tradeoff. Retain electrical-safe as the existing shadow/audit baseline. No production behavior is enabled by this experiment.

The frozen DeBERTa encoder was trained on each new representation, not merely tested on different slices using the old weights. Fifteen new fits isolate input changes within the prior all-data training protocol. Three exact prefix256 fits were reused after verifying identical tokens on all 4,701 corpus/cache postings. No BERT-Tiny fits, new labels, occupational rules, holdout tuning, generative inference, or commits were added.

## Does the wrong slice explain the failure?

It explains a real information-loss problem, but not most of the observed false rejects. Among 1,159 described binary examples, any recorded-evidence coverage rises from 72.22% at prefix256 to 93.10% at prefix384 and 97.24% at prefix512. Full recorded-evidence coverage rises from 37.96% to 75.67% and 92.49%, respectively.

Yet at the conservative, validation-selected operating point:

- Prefix256 has 13 false rejects, including eight described jobs. All eight include some recorded evidence; five include all of it.
- Prefix384 has 29 false rejects, including 17 described jobs. Twelve of those 17 include all recorded evidence.
- Prefix512 has 32 false rejects, including 25 described jobs. Twenty-three of those 25 include all recorded evidence. Only one omits all recorded evidence.

Thus the earlier 28% omission figure described the whole dataset, not the explanation for the baseline's false rejects. Additional input context changes what the model learns and how it calibrates, but recovering recorded excerpts alone does not solve the problem. Title-only inputs are unchanged across representations, yet their false rejects also change (5 at prefix256, 12 at prefix384, 7 at prefix512). This further shows that the comparison involves learned behavior and calibration, not just recovering evidence at inference time.

This does not prove that DeBERTa cannot learn the task. It leaves label ambiguity, employer transfer, calibration, optimization, and model limitations unresolved. The corpus is provisional Codex adjudication, including weak title-only labels and excerpt-based description review. The evidence spans are not exhaustive human ground truth. No new labels or adjudication were introduced to resolve disagreements.

## Rejection gains come with unsafe described-job errors

All main comparisons use independently validation-selected fold thresholds. The conservative target is 100% validation KEEP recall, inherited from supervised_v2; the 99% and 98% validation targets are also fully reported.

- Head/tail: 98.59% overall recall, 97.37% described recall, 30 false rejects, 8.85% old-cache rejection.
- Head/middle/tail: 98.07% overall recall, 95.84% described recall, 41 false rejects, 13.57% cache rejection.
- Prefix384: 98.63% overall recall, 98.14% described recall, 29 false rejects, 11.11% cache rejection.
- Prefix512: 98.49% overall recall, 97.26% described recall, 32 false rejects, 16.03% cache rejection.
- Generic sections: 98.11% overall recall, 95.73% described recall, 40 false rejects, 19.89% cache rejection.

The original prefix256 remains 99.39% overall / 99.12% described recall, with 13 false rejects and 8.25% cache rejection. Electrical-safe remains 99.58% overall / 99.02% described recall, with nine false rejects and 13.84% cache rejection.

If judged only on pooled recall >=98%, prefix512 and sections appear to beat the cache target. That hides their substantially poorer described-job recall, the specific cohort this experiment was intended to repair. The more aggressive validation targets further increase false rejects; none provides a convincing high-recall replacement. Prefix384 keeps both described and title-only recall above 98%, but stays below the rule rejection rate and produces more false rejects than the rules.

Head/tail and head/middle/tail reduce recorded-evidence coverage. In this corpus, 1,111/1,159 described binary postings have all recorded spans in the first half of the body. An inspection of five evenly spaced described examples found legal, pay, export-control, and equal-opportunity text at their tails. This small inspection is explanatory, not a new occupational labeling exercise. The generic section selector modestly improves any-overlap coverage to 75.75%, but includes all recorded evidence in only 8.89%; short windows and approximate heading matches leave fragments.

## Technical roles and calibration

Prefix512 rescues four baseline false rejects, including COMSEC Custodian, but introduces 23 new false rejects. It still rejects Help Desk Administrator, NOC/SOC Shift Lead, Staff Security Incident Commander, Senior Anti-Tamper Systems/Hardware Engineer, and First Line Systems Integration Manager. These examples include all their recorded evidence. Technical leadership remains difficult, including partner architects and service-delivery roles.

Head/tail newly rejects Principal Enterprise-Wide Applications Analyst. The section selector rejects ServiceNow Software Engineer, Principal Site Reliability Engineer, Senior Machine Learning Engineer, SAP Systems Expert, Electronic Systems Technician, and an identity-governance architect. It therefore fails even some core or clearly adjacent technical families. The named SATCOM/application-development probes survive in the other configurations at the conservative target. All six unchanged older technical stress probes (including application developer, PC support, enterprise systems, controls, and cyber) survive in all six configurations; passing those few probes does not outweigh the larger held-out error set.

Threshold calibration varies greatly across employer folds. Prefix512's conservative thresholds are 0.05837, 0.99553, and 0.07957; sections uses 0.10213, 0.97809, and 0.06993. A validation target of 100% is not an evaluation recall guarantee. The third fold has only 42 validation KEEP examples, so both 99% and 98% targets allow zero validation false rejects. Full per-employer and employer-masking diagnostics are in results.json. This experiment cannot separate employer/boilerplate transfer from other causes of calibration instability.

## Retrospective frontiers are diagnostic only

There is limited ranking improvement at looser pooled recall targets: at an evaluation-selected >=99% overall recall threshold, prefix512 rejects 17.03% of the old cache versus 11.98% for prefix256. However, described recall at that prefix512 point is only 97.81%. These thresholds use evaluation labels and are not deployment candidates.

At the electrical-safe false-reject count of nine (99.58% overall recall), even diagnostic global thresholds yield only 4.59–9.58% cache rejection across the six encoders. Prefix512 gives 6.12%, below prefix256's 7.58%; sections is best at 9.58%, still below the rules' 13.84%. The apparent ranking gain therefore does not persist at the rules' observed recall. See rule-matched-diagnostic.json and the explicitly diagnostic tables in report.md.

## Runtime is not the reason for rejection

Matched 40-posting GTX 1070 benchmarks include tokenization and deterministic span selection. Prefix256 costs 13.43 ms/post at batch 16 (74.48 postings/s); prefix384 costs 21.59 ms (46.31/s); prefix512 costs 32.01 ms (31.24/s). Their single-posting GPU medians are 16.54, 24.48, and 33.37 ms. Prefix512 peaks at approximately 1,815 MiB allocated / 2,102 MiB reserved GPU memory. Its CPU median is 393.76 ms on four threads, with 2.45 postings/s measured throughput. All representations' timings and memory figures are in report.md.

The larger encoder input is operationally plausible for further offline work; its extra runtime is not the blocker. No Qwen inference or matched Qwen benchmark was run, so there is no measured cost ratio to claim. The current evidence rejects replacement because of safety and calibration, not because 512 tokens are too slow.

Work stops at this report. A future independent human-reviewed technical challenge set and better-supported calibration would be needed to establish replacement safety. These reused development folds cannot establish that by further retrospective threshold selection.
