# Input-representation experiment

All results are employer-held-out development results against provisional Codex labels. No training-set scores are presented. This is a focused follow-up on reused development folds, not a new independent test.

## Frozen data and training

Same 3,198-posting corpus as supervised_v2: 2,669 binary examples (2,121 KEEP / 548 REJECT); 529 AMBIGUOUS excluded from training/calibration. Binary described: 1,159 (914 KEEP); binary title-only: 1,510 (1,207 KEEP). Exact corpus/cache/split hashes are in baseline-provenance.json. The old 1,503-posting cache is scored by employer-out models; its 347 older labeled references are stress probes, not training data.

Model: cross-encoder/nli-deberta-v3-small @ fa2804872c3b4bd748f38c0185cc85775361e735; initial safetensors SHA256 ebc79588dd73ccfb6a3f6078519cfbf512c5305384c5ea1845bc71cd32216e86. Fresh binary head, full encoder fine-tuning, 141,896,450 parameters. Fifteen new fits (five inputs × three folds) plus three reused prefix256 fits. No BERT-Tiny rerun.

All-data regime only; train-only inverse-frequency class weights; FP32; four epochs; AdamW lr=2e-5, weight decay=.01; batch 4 × accumulation 4; 10% warmup; clip 1; seeds 20260905+fold. Lowest unweighted validation cross entropy selects each checkpoint. See README.md and inputs.py for exact deterministic span construction. Inputs never use labels or adjudication evidence.

Employer/lexical-title-family/duplicate purging is unchanged. Fold train/validation/evaluation counts: 700/481/1441, 1595/351/592, 1830/83/636. Validation KEEP counts are 392, 327, and 42; the last fold cannot permit one false reject at either 99% or 98%. Broad semantic-family generalization remains unproven.

## Evidence coverage

Denominator: 1,159 described binary examples. Any overlap can be only a fragment. Full coverage means all recorded reviewed-evidence characters, not all relevant content in the job. Audit spans never construct model inputs.

| Input | Any evidence | At least half | All evidence | Mean fraction |
| --- | --- | --- | --- | --- |
| prefix256 | 72.22% | 55.22% | 37.96% | 55.49% |
| headtail256 | 35.46% | 19.84% | 11.04% | 21.48% |
| headmidtail256 | 30.28% | 16.57% | 0.09% | 14.22% |
| prefix384 | 93.10% | 86.45% | 75.67% | 85.74% |
| prefix512 | 97.24% | 95.69% | 92.49% | 95.42% |
| sections256 | 75.75% | 57.12% | 8.89% | 56.23% |

All 1,312 described postings, including AMBIGUOUS (audit only):

| Input | Any evidence | At least half | All evidence | Mean fraction |
| --- | --- | --- | --- | --- |
| prefix256 | 72.64% | 55.72% | 38.80% | 56.12% |
| headtail256 | 36.81% | 20.96% | 11.74% | 22.67% |
| headmidtail256 | 31.33% | 17.45% | 0.08% | 14.83% |
| prefix384 | 92.84% | 86.36% | 75.99% | 85.72% |
| prefix512 | 97.03% | 95.35% | 92.23% | 95.11% |
| sections256 | 76.30% | 57.93% | 8.99% | 56.82% |

The JSON also reports each class separately and per-posting selected source ranges. Recorded evidence is concentrated toward the beginning: 1,111/1,159 described binary postings have every recorded span within the first half of the body. Median body length is 5,500 characters and median last-evidence position is 30.1% of body length. These are properties of the adjudication record, not proof that later text is irrelevant.

## Validation-selected comparisons

Reject iff KEEP probability is strictly below the fold threshold; ties KEEP. Thresholds come only from inner validation KEEP-score boundaries. All outer evaluation scores stay untouched. The 100% validation target is the conservative default inherited from v2; 99% and 98% are separate predeclared alternatives, not guarantees on evaluation recall.

| Input | Val target | KEEP recall | Described recall | Title-only recall | FN | Eval reject | Old-cache reject | Reject precision |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| electrical-safe | fixed | 99.58% | 99.02% | 100.00% | 9 | 1.57% | 13.84% | 78.57% |
| prefix256 | 100.00% | 99.39% | 99.12% | 99.59% | 13 | 4.68% | 8.25% | 89.60% |
| prefix256 | 99.00% | 97.74% | 95.73% | 99.25% | 48 | 7.53% | 13.57% | 76.12% |
| prefix256 | 98.00% | 96.84% | 94.64% | 98.51% | 67 | 9.78% | 14.57% | 74.33% |
| headtail256 | 100.00% | 98.59% | 97.37% | 99.50% | 30 | 5.13% | 8.85% | 78.10% |
| headtail256 | 99.00% | 97.60% | 95.84% | 98.92% | 51 | 6.59% | 11.44% | 71.02% |
| headtail256 | 98.00% | 96.94% | 94.86% | 98.51% | 65 | 7.49% | 12.84% | 67.50% |
| headmidtail256 | 100.00% | 98.07% | 95.84% | 99.75% | 41 | 6.33% | 13.57% | 75.74% |
| headmidtail256 | 99.00% | 97.69% | 95.08% | 99.67% | 49 | 7.12% | 14.37% | 74.21% |
| headmidtail256 | 98.00% | 94.58% | 88.62% | 99.09% | 115 | 10.83% | 20.49% | 60.21% |
| prefix384 | 100.00% | 98.63% | 98.14% | 99.01% | 29 | 5.81% | 11.11% | 81.29% |
| prefix384 | 99.00% | 96.94% | 94.75% | 98.59% | 65 | 8.24% | 15.70% | 70.45% |
| prefix384 | 98.00% | 94.96% | 90.70% | 98.18% | 107 | 10.53% | 18.83% | 61.92% |
| prefix512 | 100.00% | 98.49% | 97.26% | 99.42% | 32 | 7.31% | 16.03% | 83.59% |
| prefix512 | 99.00% | 96.98% | 94.31% | 99.01% | 64 | 9.82% | 20.16% | 75.57% |
| prefix512 | 98.00% | 96.23% | 92.89% | 98.76% | 80 | 10.72% | 21.69% | 72.03% |
| sections256 | 100.00% | 98.11% | 95.73% | 99.92% | 40 | 8.58% | 19.89% | 82.53% |
| sections256 | 99.00% | 96.28% | 92.01% | 99.50% | 79 | 11.32% | 24.48% | 73.84% |
| sections256 | 98.00% | 95.29% | 90.26% | 99.09% | 100 | 12.59% | 25.82% | 70.24% |

### Fold thresholds

| Input | Val target | Fold 0 | Fold 1 | Fold 2 |
| --- | --- | --- | --- | --- |
| prefix256 | 100.00% | 0.050540157 | 0.282850891 | 0.024894619 |
| prefix256 | 99.00% | 0.319950789 | 0.818946064 | 0.024894619 |
| prefix256 | 98.00% | 0.654808640 | 0.861874938 | 0.024894619 |
| headtail256 | 100.00% | 0.195310175 | 0.248804584 | 0.071161896 |
| headtail256 | 99.00% | 0.214776218 | 0.824002385 | 0.071161896 |
| headtail256 | 98.00% | 0.249575973 | 0.915139198 | 0.071161896 |
| headmidtail256 | 100.00% | 0.194754764 | 0.639438391 | 0.004383922 |
| headmidtail256 | 99.00% | 0.205618724 | 0.844573557 | 0.004383922 |
| headmidtail256 | 98.00% | 0.249313578 | 0.975918472 | 0.004383922 |
| prefix384 | 100.00% | 0.043091331 | 0.880897582 | 0.488100082 |
| prefix384 | 99.00% | 0.098211221 | 0.962489188 | 0.488100082 |
| prefix384 | 98.00% | 0.489641756 | 0.981142223 | 0.488100082 |
| prefix512 | 100.00% | 0.058373261 | 0.995531142 | 0.079567574 |
| prefix512 | 99.00% | 0.108091548 | 0.997903466 | 0.079567574 |
| prefix512 | 98.00% | 0.216759488 | 0.998129070 | 0.079567574 |
| sections256 | 100.00% | 0.102130115 | 0.978094280 | 0.069925755 |
| sections256 | 99.00% | 0.301767975 | 0.991871715 | 0.069925755 |
| sections256 | 98.00% | 0.458287925 | 0.992838681 | 0.069925755 |

### Conservative threshold confidence and cache slices

| Input | High-confidence recall | Medium-confidence recall | Unreviewed cache reject | AMBIGUOUS reject |
| --- | --- | --- | --- | --- |
| prefix256 | 99.62% | 99.25% | 7.44% | 11.53% |
| headtail256 | 98.21% | 98.81% | 8.04% | 12.29% |
| headmidtail256 | 97.06% | 98.66% | 12.37% | 13.04% |
| prefix384 | 98.98% | 98.43% | 10.29% | 16.64% |
| prefix512 | 98.21% | 98.66% | 13.67% | 14.56% |
| sections256 | 97.95% | 98.21% | 18.69% | 15.69% |

AMBIGUOUS rejection is a review signal, not measured accuracy. The unreviewed cache remainder has 1,156 postings. All subset/threshold rows are in selected-thresholds.csv.

## Diagnostic retrospective frontiers — NOT deployment thresholds

The following global thresholds use evaluation labels and therefore measure ranking potential only. They cannot be selected for production or cited as independently validated operating points.

| Input | Diagnostic target | Threshold | Eval recall | FN | Eval reject | Cache reject |
| --- | --- | --- | --- | --- | --- | --- |
| prefix256 | 100.00% | 0.010196130 | 100.00% | 0 | 0.97% | 0.00% |
| prefix256 | 99.00% | 0.047913112 | 99.01% | 21 | 5.96% | 11.98% |
| prefix256 | 98.00% | 0.089499429 | 98.02% | 42 | 8.69% | 19.83% |
| headtail256 | 100.00% | 0.009610179 | 100.00% | 0 | 0.90% | 0.00% |
| headtail256 | 99.00% | 0.084746800 | 99.01% | 21 | 4.35% | 7.58% |
| headtail256 | 98.00% | 0.196658880 | 98.02% | 42 | 5.81% | 10.38% |
| headmidtail256 | 100.00% | 0.001969629 | 100.00% | 0 | 0.30% | 1.26% |
| headmidtail256 | 99.00% | 0.007051000 | 99.01% | 21 | 3.00% | 6.79% |
| headmidtail256 | 98.00% | 0.049520228 | 98.02% | 42 | 6.59% | 15.70% |
| prefix384 | 100.00% | 0.050853983 | 100.00% | 0 | 3.15% | 4.52% |
| prefix384 | 99.00% | 0.108579852 | 99.01% | 21 | 5.10% | 8.18% |
| prefix384 | 98.00% | 0.262895852 | 98.02% | 42 | 6.52% | 11.24% |
| prefix512 | 100.00% | 0.004942704 | 100.00% | 0 | 2.40% | 3.53% |
| prefix512 | 99.00% | 0.111717306 | 99.01% | 21 | 7.53% | 17.03% |
| prefix512 | 98.00% | 0.228797495 | 98.02% | 42 | 9.97% | 23.82% |
| sections256 | 100.00% | 0.008025953 | 100.00% | 0 | 1.35% | 1.66% |
| sections256 | 99.00% | 0.077223219 | 99.01% | 21 | 6.63% | 15.97% |
| sections256 | 98.00% | 0.213858083 | 98.02% | 42 | 8.80% | 20.16% |

diagnostic-frontiers.csv separately includes described, title-only, and confidence slices. fixed-threshold-sweep.csv contains the entire fixed threshold grid. Neither selects a deployable threshold.

### Diagnostic comparison at the rule false-reject count

A separate evaluation-selected threshold allows the same nine false rejects as electrical-safe. This is not a proposed threshold; it checks whether the apparent ranking gains persist at the rule baseline's observed recall.

| Input | Diagnostic threshold | KEEP recall | Described recall | FN | Cache reject |
| --- | --- | --- | --- | --- | --- |
| prefix256 | 0.035078287 | 99.58% | 99.12% | 9 | 7.58% |
| headtail256 | 0.053536374 | 99.58% | 99.34% | 9 | 5.12% |
| headmidtail256 | 0.003785943 | 99.58% | 99.02% | 9 | 4.59% |
| prefix384 | 0.068363979 | 99.58% | 99.02% | 9 | 5.85% |
| prefix512 | 0.056265451 | 99.58% | 99.12% | 9 | 6.12% |
| sections256 | 0.022924813 | 99.58% | 99.02% | 9 | 9.58% |

## Inference performance

Fresh process per representation; same deterministic 40-posting fold-2 sample; GTX 1070 8 GB, i7-4770K, four CPU threads. Includes complete body tokenization and span selection. GPU batch=16 and batch-one measurements; CPU batch-one. Memory is measured allocated tensor memory plus reserved allocator memory, not total system/GPU-driver consumption. This small benchmark is comparative, not a production load test.

| Input | GPU ms/post | GPU post/s | GPU single ms | GPU allocated/reserved MiB | CPU single ms | CPU post/s | RSS MiB |
| --- | --- | --- | --- | --- | --- | --- | --- |
| prefix256 | 13.43 | 74.5 | 16.54 | 1006/1092 | 155.2 | 6.3 | 1629 |
| headtail256 | 13.62 | 73.4 | 16.55 | 1006/1092 | 153.1 | 6.4 | 1631 |
| headmidtail256 | 13.54 | 73.9 | 16.81 | 1006/1092 | 154.0 | 6.1 | 1631 |
| prefix384 | 21.59 | 46.3 | 24.48 | 1363/1456 | 253.7 | 3.8 | 1663 |
| prefix512 | 32.01 | 31.2 | 33.37 | 1815/2102 | 393.8 | 2.4 | 1719 |
| sections256 | 13.51 | 74.0 | 16.73 | 1006/1092 | 153.7 | 6.5 | 1627 |

Electrical-safe prior same-host measurement: 0.0913 ms/post, approximately 10,949 postings/s. No Qwen inference or matched Qwen benchmark was run, so no measured cost ratio to Qwen is claimed.

## Errors at the conservative validation target

The paired analysis uses separately validation-calibrated thresholds for each representation; it measures the whole trained configuration, not a causal span-only perturbation. Full error IDs, scores, source spans and recovered/new errors are in paired-error-analysis.json.

### prefix256

False rejects: 13. Recovered prefix256 false rejects: 0; new false rejects: 0.

| False-rejected title | Employer | Body | Confidence | P(KEEP) | Threshold |
| --- | --- | --- | --- | --- | --- |
| Partner Technology Architect | servicenow | True | medium | 0.064889 | 0.282851 |
| Flight Test Lead | boeing | False | medium | 0.122190 | 0.282851 |
| Senior Specialized Test Equipment (STE) Integration Lead | boeing | False | medium | 0.152750 | 0.282851 |
| Lab Test Program Integrator (Experienced or Lead) | boeing | False | medium | 0.035078 | 0.282851 |
| Quality Systems Specialist (Associate or Experienced)) | boeing | True | medium | 0.074985 | 0.282851 |
| Senior Low Observables (LO) RCS Analyst | boeing | False | medium | 0.010196 | 0.282851 |
| ICITAP Forensic Digital Evidence Advisor | amentum | True | high | 0.076959 | 0.282851 |
| Mid-Level or Experienced Electronics Programs Unit IPT Lead (Project Management Specialist) | boeing | False | medium | 0.055156 | 0.282851 |
| ABAD COMSEC Custodian**This position is located at Ramstein AFB, Germany (International Assignment) **NO REMOTE WORK** | parsons | True | high | 0.024331 | 0.024895 |
| Boeing Summer 2027 Internship Program (Paid) - Quality Engineering Intern | boeing | True | medium | 0.270258 | 0.282851 |
| Director of Expert Services, ITAM and ITOM | servicenow | True | medium | 0.117399 | 0.282851 |
| Partner Technical Architect | servicenow | True | high | 0.093362 | 0.282851 |
| Implementation Manager - Moveworks | servicenow | True | medium | 0.043165 | 0.282851 |

Representative low-KEEP-probability rejects that agree with the provisional REJECT labels:

| Title | Employer | P(KEEP) |
| --- | --- | --- |
| Nurse Practitioner | boeing | 0.007717 |
| Experienced Facilities Plant Maintenance Specialist | boeing | 0.007729 |
| Silk Screener Decorative Panels B - 79805 | boeing | 0.008134 |
| Assembler Power Plant B - 91104 | boeing | 0.008151 |
| Mid-Level Disability Management Specialist | boeing | 0.008195 |
| Numerical Control Tape Laminator Operator - 57006 | boeing | 0.008256 |

### headtail256

False rejects: 30. Recovered prefix256 false rejects: 5; new false rejects: 22.

| False-rejected title | Employer | Body | Confidence | P(KEEP) | Threshold |
| --- | --- | --- | --- | --- | --- |
| Principal / Sr Principal Systems Field Test Engineer (CONUS) | northrop-grumman | True | high | 0.047660 | 0.071162 |
| Enterprise Security Information System (ESIS) Team Lead | northrop-grumman | True | medium | 0.056388 | 0.071162 |
| Flight Test Lead | boeing | False | medium | 0.127397 | 0.248805 |
| Air Dominance Flight Test Operations Instrumentation Manager | boeing | True | high | 0.127468 | 0.248805 |
| Lead Databricks Data Security Engineer | northrop-grumman | True | high | 0.066795 | 0.071162 |
| Staff Security Incident Commander | servicenow | True | high | 0.140186 | 0.248805 |
| NOC / SOC Shift Lead (2nd Shift) | leidos | True | high | 0.167172 | 0.195310 |
| Global Director, Global Technology Partnerships | servicenow | True | high | 0.039501 | 0.248805 |
| MUOS Level 1 Testbed IPT Lead (Level L) | boeing | True | high | 0.206247 | 0.248805 |
| Senior Test Program Manager | boeing | True | high | 0.026374 | 0.248805 |
| Senior Specialized Test Equipment (STE) Integration Lead | boeing | False | medium | 0.099805 | 0.248805 |
| Sr Staff Outbound Product Manager | servicenow | True | medium | 0.141071 | 0.248805 |
| Sr Customer Success Manager (Armis/Veza) | servicenow | True | medium | 0.070059 | 0.248805 |
| Director of Expert Services, Security and Risk | servicenow | True | medium | 0.074777 | 0.248805 |
| Lab Test Program Integrator (Experienced or Lead) | boeing | False | medium | 0.019642 | 0.248805 |
| Associate Space Vehicle Controller - Analyst | boeing | False | medium | 0.189375 | 0.248805 |

Representative low-KEEP-probability rejects that agree with the provisional REJECT labels:

| Title | Employer | P(KEEP) |
| --- | --- | --- |
| Experienced Facilities Plant Maintenance Specialist | boeing | 0.006725 |
| Nurse Practitioner | boeing | 0.006977 |
| Group Coordinator Assembler | boeing | 0.007343 |
| Mid-Level Disability Management Specialist | boeing | 0.007633 |
| Procurement Agent (Procurement Admin) | boeing | 0.007659 |
| Procurement Agent (Procurement Agent-General) | boeing | 0.007791 |

### headmidtail256

False rejects: 41. Recovered prefix256 false rejects: 6; new false rejects: 34.

| False-rejected title | Employer | Body | Confidence | P(KEEP) | Threshold |
| --- | --- | --- | --- | --- | --- |
| Staff Inbound Product Manager | servicenow | True | high | 0.110043 | 0.639438 |
| GEOINT Sensor Support Engineer (Ramstein AFB, Germany) | rtx | True | high | 0.004323 | 0.004384 |
| S/4 HANA Business Analyst – Quality Management (Remote) | rtx | True | high | 0.001972 | 0.004384 |
| Sr. Principal Electrical Engineer (Remote) | rtx | True | high | 0.003536 | 0.004384 |
| Sr EWM Configuration Lead (Implementation & Maintenance) | rtx | True | high | 0.003030 | 0.004384 |
| Mid-Level Backend Software Engineer | leidos | True | high | 0.177002 | 0.194755 |
| Audiovisual Design & Integration Specialist (Associate, Mid-Level or Senior) | boeing | True | high | 0.422797 | 0.639438 |
| Enterprise RunMyJobs Administrator | rtx | True | high | 0.003724 | 0.004384 |
| DSO Engineer (Remote) | rtx | True | high | 0.001970 | 0.004384 |
| Advisory Solution Consultant - State and Local | servicenow | True | medium | 0.136160 | 0.639438 |
| SAP MRO Aftermarket Functional/Technical Analyst (Remote) | rtx | True | high | 0.004221 | 0.004384 |
| Senior Project Management Specialist, Cost Account Management Integrated Product Team Lead | boeing | True | medium | 0.083140 | 0.639438 |
| Global Director, Global Technology Partnerships | servicenow | True | high | 0.504659 | 0.639438 |
| MUOS Level 1 Testbed IPT Lead (Level L) | boeing | True | high | 0.069022 | 0.639438 |
| Senior Test Program Manager | boeing | True | high | 0.080491 | 0.639438 |
| Technical Program Manager, Agent Policy Fabric | nvidia | True | high | 0.004160 | 0.004384 |

Representative low-KEEP-probability rejects that agree with the provisional REJECT labels:

| Title | Employer | P(KEEP) |
| --- | --- | --- |
| Principal Product Procurement Specialist (Remote - Puerto Rico) | rtx | 0.001504 |
| Integrated Program Planning & Control (IPP&C) Manager EV Cost Analyst. | rtx | 0.001557 |
| Analyst, Procurement (P1) - REMOTE | rtx | 0.001749 |
| ITAR/EAR Internal Audit Professional (Remote) | rtx | 0.001759 |
| Commodity Performance Lead | rtx | 0.001763 |
| Mgr, Supply Chain Mgmt – Remote | rtx | 0.001790 |

### prefix384

False rejects: 29. Recovered prefix256 false rejects: 4; new false rejects: 20.

| False-rejected title | Employer | Body | Confidence | P(KEEP) | Threshold |
| --- | --- | --- | --- | --- | --- |
| Senior DL Algorithms Engineer - Inference Performance | nvidia | True | high | 0.478055 | 0.488100 |
| Interiors Certification Engineering Manager (K Level) | boeing | False | medium | 0.742708 | 0.880898 |
| Flight Test Lead | boeing | False | medium | 0.241231 | 0.880898 |
| Staff Security Incident Commander | servicenow | True | high | 0.401454 | 0.880898 |
| Senior Project Management Specialist, Cost Account Management Integrated Product Team Lead | boeing | True | medium | 0.839786 | 0.880898 |
| Global Director, Global Technology Partnerships | servicenow | True | high | 0.482043 | 0.880898 |
| Senior Test Program Manager | boeing | True | high | 0.845166 | 0.880898 |
| Senior Specialized Test Equipment (STE) Integration Lead | boeing | False | medium | 0.640179 | 0.880898 |
| Principal Inbound Product Manager | servicenow | True | high | 0.870345 | 0.880898 |
| Nondestructive Test (NDT) Technician - UT & RT | boeing | True | medium | 0.295459 | 0.880898 |
| Senior Machine Learning Engineer II - FlightAware (Remote) | rtx | True | medium | 0.485381 | 0.488100 |
| Director of Expert Services, Security and Risk | servicenow | True | medium | 0.285164 | 0.880898 |
| Lab Test Program Integrator (Experienced or Lead) | boeing | False | medium | 0.136673 | 0.880898 |
| Associate Space Vehicle Controller - Analyst | boeing | False | medium | 0.393230 | 0.880898 |
| Senior Resources Geologist | aecom | True | medium | 0.483496 | 0.488100 |
| Boeing Summer 2027 Internship Program (Paid) - Facilities Engineering | boeing | False | medium | 0.833530 | 0.880898 |

Representative low-KEEP-probability rejects that agree with the provisional REJECT labels:

| Title | Employer | P(KEEP) |
| --- | --- | --- |
| Nurse Practitioner | boeing | 0.006481 |
| Associate Shipping/Receiving Specialist | boeing | 0.006609 |
| Numerical Control Tape Laminator Operator - 57006 | boeing | 0.006790 |
| Government & Capital Property Manager | boeing | 0.006932 |
| Assembly Mechanic | boeing | 0.007064 |
| Mid-Level Disability Management Specialist | boeing | 0.007107 |

### prefix512

False rejects: 32. Recovered prefix256 false rejects: 4; new false rejects: 23.

| False-rejected title | Employer | Body | Confidence | P(KEEP) | Threshold |
| --- | --- | --- | --- | --- | --- |
| Technical Analyst | leidos | True | high | 0.049450 | 0.058373 |
| Regional Service Delivery Manager | leidos | True | high | 0.056265 | 0.058373 |
| Partner Technology Architect | servicenow | True | medium | 0.685199 | 0.995531 |
| Regional Service Delivery Manager | leidos | True | high | 0.054261 | 0.058373 |
| Senior Technical Business Analyst | leidos | True | high | 0.050358 | 0.058373 |
| Wire Design & Install Engr (Electrical/Electronic Installation Design) | boeing | False | medium | 0.981381 | 0.995531 |
| Staff Security Incident Commander | servicenow | True | high | 0.992753 | 0.995531 |
| NOC / SOC Shift Lead (2nd Shift) | leidos | True | high | 0.047036 | 0.058373 |
| Senior Program Manager | leidos | True | high | 0.039931 | 0.058373 |
| Senior Project Management Specialist, Cost Account Management Integrated Product Team Lead | boeing | True | medium | 0.985534 | 0.995531 |
| Global Director, Global Technology Partnerships | servicenow | True | high | 0.990975 | 0.995531 |
| Engineering Multi-Skill Mgr (Eng Multi-Skill LDR Management) | boeing | False | medium | 0.994617 | 0.995531 |
| Nondestructive Test (NDT) Technician - UT & RT | boeing | True | medium | 0.856724 | 0.995531 |
| Director of Expert Services, Security and Risk | servicenow | True | medium | 0.984552 | 0.995531 |
| Lab Test Program Integrator (Experienced or Lead) | boeing | False | medium | 0.994705 | 0.995531 |
| Principal Product Manager, Voice AI | servicenow | True | medium | 0.356892 | 0.995531 |

Representative low-KEEP-probability rejects that agree with the provisional REJECT labels:

| Title | Employer | P(KEEP) |
| --- | --- | --- |
| Associate Shipping/Receiving Specialist | boeing | 0.002217 |
| Mid-Level Disability Management Specialist | boeing | 0.002219 |
| Assembly Mechanic | boeing | 0.002329 |
| Senior Integrated Planning and Scheduling Specialist | boeing | 0.002365 |
| Associate Procurement Agent | boeing | 0.002419 |
| Inspector Assembly & Installation - 51406 | boeing | 0.002432 |

### sections256

False rejects: 40. Recovered prefix256 false rejects: 7; new false rejects: 34.

| False-rejected title | Employer | Body | Confidence | P(KEEP) | Threshold |
| --- | --- | --- | --- | --- | --- |
| Staff Inbound Product Manager | servicenow | True | high | 0.977026 | 0.978094 |
| ABAD Test Coordinator and Analyst**This position is located at Ramstein AFB, Germany (International Assignment) **NO REMOTE WORK** | parsons | True | medium | 0.022510 | 0.069926 |
| Principal Site Reliability Engineer - ARINCDirect (Remote) | rtx | True | high | 0.009194 | 0.069926 |
| Partner Technology Architect | servicenow | True | medium | 0.770264 | 0.978094 |
| Regional Service Delivery Manager | leidos | True | high | 0.100445 | 0.102130 |
| Flight Test Lead | boeing | False | medium | 0.973780 | 0.978094 |
| Senior Technical Director | parsons | True | medium | 0.036220 | 0.069926 |
| Regional Service Delivery Manager | leidos | True | high | 0.077223 | 0.102130 |
| Sr. Principal Quality Engineer | northrop-grumman | True | high | 0.067468 | 0.069926 |
| Advisory Solution Consultant - State and Local | servicenow | True | medium | 0.018022 | 0.978094 |
| Senior Program Manager | leidos | True | high | 0.022867 | 0.102130 |
| Senior Project Management Specialist, Cost Account Management Integrated Product Team Lead | boeing | True | medium | 0.680037 | 0.978094 |
| ServiceNow Software Engineer | leidos | True | medium | 0.068832 | 0.102130 |
| Senior Test Program Manager | boeing | True | high | 0.975046 | 0.978094 |
| SAP Principal SLP/MDG Systems Expert | rtx | True | high | 0.008026 | 0.069926 |
| Nondestructive Test (NDT) Technician - UT & RT | boeing | True | medium | 0.969241 | 0.978094 |

Representative low-KEEP-probability rejects that agree with the provisional REJECT labels:

| Title | Employer | P(KEEP) |
| --- | --- | --- |
| Associate Shipping/Receiving Specialist | boeing | 0.004162 |
| Mid-Level Disability Management Specialist | boeing | 0.004258 |
| Government & Capital Property Manager | boeing | 0.004513 |
| Procurement Agent 2 | boeing | 0.004581 |
| Group Coordinator Assembler | boeing | 0.004607 |
| Experienced Facilities Plant Maintenance Specialist | boeing | 0.004736 |

## Recorded evidence in remaining errors

| Input | Described false rejects | No evidence overlap | At least half included | All evidence included |
| --- | --- | --- | --- | --- |
| prefix256 | 8 | 0 | 7 | 5 |
| headtail256 | 24 | 13 | 8 | 4 |
| headmidtail256 | 38 | 29 | 4 | 0 |
| prefix384 | 17 | 2 | 14 | 12 |
| prefix512 | 25 | 1 | 24 | 23 |
| sections256 | 39 | 11 | 21 | 3 |

These are errors against provisional labels. Seeing all recorded evidence does not establish that the label is correct, that the model attended to it, or that the recorded excerpt contains every relevant fact. It does show that missing recorded evidence alone cannot explain those decisions.

## Technical families and employer stability

| Input | software | infrastructure-support | cybersecurity | engineering | technical-leadership | data-ai |
| --- | --- | --- | --- | --- | --- | --- |
| prefix256 | 100.00% | 100.00% | 98.86% | 99.21% | 98.09% | 100.00% |
| headtail256 | 100.00% | 98.94% | 98.30% | 98.95% | 94.27% | 100.00% |
| headmidtail256 | 99.77% | 97.63% | 98.86% | 98.55% | 93.89% | 98.10% |
| prefix384 | 99.77% | 100.00% | 98.86% | 98.16% | 95.80% | 99.05% |
| prefix512 | 100.00% | 98.94% | 98.30% | 98.95% | 93.51% | 100.00% |
| sections256 | 99.54% | 99.47% | 99.43% | 98.29% | 92.37% | 98.10% |

These are frozen provisional label categories, not new rejection rules. Named COMSEC/SATCOM/enterprise-application/PC-support probes are preserved in paired-error-analysis.json. Results include six unchanged older technical stress probes scored strictly employer-out.

| Input | Employer-mask flips | Masked KEEP recall | Mean score change |
| --- | --- | --- | --- |
| prefix256 | 10 | 99.10% | 0.0100 |
| headtail256 | 16 | 98.73% | 0.0155 |
| headmidtail256 | 16 | 97.93% | 0.0108 |
| prefix384 | 7 | 98.63% | 0.0046 |
| prefix512 | 11 | 98.26% | 0.0072 |
| sections256 | 30 | 97.60% | 0.0066 |

Employer masking is an evaluation-only sensitivity test and cannot prove absence of boilerplate or employer proxies. Per-employer metrics and calibration Brier/cross-entropy are in results.json. Training/checkpoint history, all three folds, and GPU peaks are preserved per run.

## Interpretation and validation

See interpretation.md for the decision and detailed reading of the completed results. See validation-results.md for executed checks. No deployment or commit is part of this experiment.
