# Phase 3 Qwen prompt bake-off

This is a controlled comparison of three independently designed zero-shot prompt strategies
against the frozen deployed Qwen v1 control. All three variants were defined and fingerprinted
together before any variant benchmark began. Benchmark results cannot be used to change another
variant, and no fourth prompt is permitted.

## Frozen common contract

- Model: `qwen3:4b-instruct-2507-q4_K_M`
- Model manifest SHA-256:
  `0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0`
- Temperature / seed: `0 / 42`
- Context / maximum output: `8192 / 384`
- Stream / keep-alive: `false / -1`
- Fixtures: unchanged 40 fixtures, 320 labels, eight concepts
- Qualitative sets: unchanged six hard negatives and eight generalization cases
- Output: unchanged strict object containing exactly the eight required boolean properties and no
  additional properties

Every prompt fingerprint is SHA-256 over the same canonical compact, key-sorted JSON object with
the keys `version`, `system`, `concepts`, and `schema`. The concept definitions and schema below
are therefore part of every fingerprint.

Exact common concept definitions:

```text
- role.ai-ml-engineering: Hands-on engineering that builds, integrates, or operationalizes machine-learning models, AI systems, pipelines, or production AI applications.
- role.software-engineering: Direct design, implementation, testing, and maintenance of software systems as an engineering responsibility.
- technical.software-development: Hands-on implementation, testing, debugging, or maintenance of software applications, services, or systems.
- technical.backend-development: Server-side software development involving services, business logic, databases, microservices, APIs, and backend systems.
- technical.api-development: Direct design, implementation, integration, operation, or maintenance of programmatic service interfaces and APIs.
- technical.automation-scripting: Automating technical workflows, deployments, operations, testing, or repetitive tasks with scripts or software tooling.
- role.cloud-engineering: Hands-on design, implementation, operation, or reliability engineering of cloud infrastructure, services, and platforms.
- technical.containers: Direct implementation or operation of containerization and orchestration using Docker, Kubernetes, or related platforms.
```

Exact common user-message template and insertion points:

```text
Concept definitions:
{one "- concept-id: definition" line for each definition above, in the displayed order}

Job title:
{title}

Full job posting:
{description}

Classify all eight concepts according to actual candidate responsibilities.
```

The unchanged schema is an object with the eight concept IDs above as boolean properties, all
eight IDs in `required`, and `additionalProperties: false`. The model must return only that object.

## Frozen v1 control

Version: `phase3-zero-shot-v1`

Fingerprint: `2550a1b61a4b869e8e7c74343eba2357cbcdb7a1d2b50b581112a765ac083d9c`

```text
You are a careful job-posting responsibility classifier.
Classify the role itself, not technologies merely mentioned as products, customer environments,
desired awareness, team context, or work managed by someone else. A label is true only when the
posting assigns the candidate direct, hands-on responsibility matching its definition.
Return exactly the requested JSON object with one boolean for every concept. Do not add prose.
```

## Variant A: Independent Evidence Gate

Version: `phase3-prompt-independent-evidence-v3`

Fingerprint: `ea911a0a1aef3850104ee2a714f3c4e393dda7aa53d85528002405bcc3108a0d`

```text
You are a careful job-posting responsibility classifier.
Evaluate each supplied concept independently. Mark a concept true only when the posting provides
sufficient semantic evidence that the candidate personally performs the activity in its definition.
Do not infer one concept from a related concept, neighboring technical work, technologies or
products, team or customer context, or mere association. Literal keyword matches are not required
when the responsibility is clearly assigned.
Return exactly the requested JSON object with one boolean for every concept. Do not add prose.
```

## Variant B: Role Assignment / Ownership

Version: `phase3-prompt-role-ownership-v1`

Fingerprint: `b2562340acde90b924ef5692ad5a85680a56f01dc5c4c2cf32c6b914ff1aa1a9`

```text
You are a careful job-posting responsibility classifier.
For each supplied concept, determine who owns the relevant work. Mark it true only when the role
itself performs or owns responsibility matching the definition. Distinguish the candidate's work
from management or oversight, work assigned to another team, customer or environment context,
requested familiarity or awareness, and a supported product or service that happens to use the
technology. Semantic responsibility is sufficient even without literal technology names.
Return exactly the requested JSON object with one boolean for every concept. Do not add prose.
```

## Variant C: Conservative Positive-Claim Verification

Version: `phase3-prompt-positive-verification-v1`

Fingerprint: `79f822cbe03e4076dd624080170a810a4d7e3e32df692899e03199072df8dda3`

```text
You are a careful job-posting responsibility classifier.
Begin without assuming that any supplied concept applies. Mark a concept true when the posting
provides adequate semantic evidence that the candidate is responsible for the defined activity.
Before returning true, verify that the evidence describes the candidate's work rather than
association or context. Do not make speculative positive classifications merely because the role
is adjacent to a domain. Do not reject a clearly supported responsibility solely because explicit
technology names are absent.
Return exactly the requested JSON object with one boolean for every concept. Do not add prose.
```

## Historical controls

The frozen v1 reference is macro/micro F1 `0.8649/0.8952`, hard negatives `6/6`, generalization
`55/64` labels and `3/8` exact, zero malformed outputs, average inference/round trip
`1.719/1.723 s`, throughput `48.619` tokens/s, loaded allocation 3,694 MiB, and R180395 `8/8`.

The prior bounded `phase3-general-evidence-v2` strategy is historical rejected evidence and is not
recreated here. It scored macro/micro F1 `0.8456/0.8761`, retained hard negatives `6/6`, improved
generalization only slightly to `56/64` and `4/8` exact, and worsened backend overmatching.

## Results

### Reproducibility

- Signed pre-result freeze commit: `b083c614ff03b8b74e5456915f015c65d1d605da`
- Exact-SHA CI: run `33454923300`, all required jobs passed
- Local validation: all 136 deterministic .NET architecture tests, the complete deterministic
  JavaScript/source suite, all four prompt self-tests, strict schema checks, and repository audit
  passed before any benchmark
- Exact detached checkout:
  `/home/codex/jsm-prompt-proof-b083c614ff03b8b74e5456915f015c65d1d605da`
- Evidence directory:
  `/home/codex/jsm-prompt-artifacts-b083c614ff03b8b74e5456915f015c65d1d605da`
- Input checksum manifest: `f902a71dde0fc7f8544919e2d27516b59da466ba39405c6296c9e1e545dc5e25`
- Consolidated summary: `e95f09f46b4713f87977763fa6b9b36f896cc36f6e164cb49c9188e6bdeabf06`

The proof adapter and Ollama runtime ran non-root, read-only, capability-free, with
`no-new-privileges`, on a private internal network with no published ports. The adapter had no
mounts. The existing pinned Qwen cache was reused; no other model was installed. Before each run,
the previous adapter was removed, Qwen was unloaded, Ollama's active-model list was empty, and the
GTX 1070 had returned to 3 MiB used. Each immutable prompt received exactly one benchmark
invocation. No result was used to modify another prompt.

### Aggregate comparison

| Prompt | Macro F1 | Micro F1 | Hard | General labels | General exact | R180395 | Malformed | Avg inference | Assessment |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Frozen Qwen v1 | 0.864942 | 0.895184 | 6/6 | 55/64 | 3/8 | 8/8 | 0 | 1.719 s | control / retain |
| Historical rejected v2 | 0.845610 | 0.876081 | 6/6 | 56/64 | 4/8 | not separately recorded | 0 | 1.733 s | rejected historical evidence |
| A: Independent Evidence Gate | 0.860263 | 0.891365 | 6/6 | 56/64 | 3/8 | 8/8 | 0 | 1.843 s | **NO MATERIAL IMPROVEMENT** |
| B: Role Assignment / Ownership | 0.834732 | 0.861972 | 6/6 | 53/64 | 3/8 | 8/8 | 0 | 1.798 s | **CLEARLY WORSE THAN V1** |
| C: Positive-Claim Verification | 0.855488 | 0.887671 | 6/6 | 54/64 | 3/8 | 8/8 | 0 | 1.806 s | **NO MATERIAL IMPROVEMENT** |

V2's R180395 field is explicitly shown as unrecorded because the historical evidence contains a
benchmark and model diagnostic but no separately preserved full-posting R180395 response. It is not
inferred.

### Variant A complete metrics

Aggregate precision/recall/F1:

- Macro: `0.814104 / 0.990741 / 0.860263`
- Micro: `0.812183 / 0.987654 / 0.891365`

| Concept | TP | FP | FN | TN | Precision | Recall | F1 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| AI/ML engineering | 16 | 0 | 0 | 24 | 1.0000 | 1.0000 | 1.0000 |
| software engineering | 26 | 1 | 1 | 12 | 0.9630 | 0.9630 | 0.9630 |
| software development | 26 | 2 | 1 | 11 | 0.9286 | 0.9630 | 0.9455 |
| backend | 13 | 11 | 0 | 16 | 0.5417 | 1.0000 | 0.7027 |
| API | 22 | 2 | 0 | 16 | 0.9167 | 1.0000 | 0.9565 |
| automation | 26 | 0 | 0 | 14 | 1.0000 | 1.0000 | 1.0000 |
| cloud | 5 | 20 | 0 | 15 | 0.2000 | 1.0000 | 0.3333 |
| containers | 26 | 1 | 0 | 13 | 0.9630 | 1.0000 | 0.9811 |

Latency and tokens: average inference `1,842.544 ms`, p95 `1,764.406 ms`, average round trip
`1,845.931 ms`, `48.690` tokens/s, average prompt `389.125` tokens, average completion `77.550`
tokens, and maximum load duration `4.722 s`. The average exceeds p95 because the cold first request
includes model load.

### Variant B complete metrics

Aggregate precision/recall/F1:

- Macro: `0.803990 / 0.953920 / 0.834732`
- Micro: `0.792746 / 0.944444 / 0.861972`

| Concept | TP | FP | FN | TN | Precision | Recall | F1 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| AI/ML engineering | 15 | 0 | 1 | 24 | 1.0000 | 0.9375 | 0.9677 |
| software engineering | 24 | 1 | 3 | 12 | 0.9600 | 0.8889 | 0.9231 |
| software development | 24 | 2 | 3 | 11 | 0.9231 | 0.8889 | 0.9057 |
| backend | 13 | 12 | 0 | 15 | 0.5200 | 1.0000 | 0.6842 |
| API | 21 | 3 | 1 | 15 | 0.8750 | 0.9545 | 0.9130 |
| automation | 26 | 0 | 0 | 14 | 1.0000 | 1.0000 | 1.0000 |
| cloud | 5 | 21 | 0 | 14 | 0.1923 | 1.0000 | 0.3226 |
| containers | 25 | 1 | 1 | 13 | 0.9615 | 0.9615 | 0.9615 |

Latency and tokens: average inference `1,798.450 ms`, p95 `1,783.367 ms`, average round trip
`1,801.699 ms`, `48.208` tokens/s, average prompt `398.125` tokens, average completion `77.725`
tokens, and maximum load duration `2.115 s`.

### Variant C complete metrics

Aggregate precision/recall/F1:

- Macro: `0.800555 / 1.000000 / 0.855488`
- Micro: `0.798030 / 1.000000 / 0.887671`

| Concept | TP | FP | FN | TN | Precision | Recall | F1 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| AI/ML engineering | 16 | 0 | 0 | 24 | 1.0000 | 1.0000 | 1.0000 |
| software engineering | 27 | 1 | 0 | 12 | 0.9643 | 1.0000 | 0.9818 |
| software development | 27 | 2 | 0 | 11 | 0.9310 | 1.0000 | 0.9643 |
| backend | 13 | 13 | 0 | 14 | 0.5000 | 1.0000 | 0.6667 |
| API | 22 | 4 | 0 | 14 | 0.8462 | 1.0000 | 0.9167 |
| automation | 26 | 0 | 0 | 14 | 1.0000 | 1.0000 | 1.0000 |
| cloud | 5 | 20 | 0 | 15 | 0.2000 | 1.0000 | 0.3333 |
| containers | 26 | 1 | 0 | 13 | 0.9630 | 1.0000 | 0.9811 |

Latency and tokens: average inference `1,805.993 ms`, p95 `1,776.884 ms`, average round trip
`1,809.295 ms`, `48.052` tokens/s, average prompt `401.125` tokens, average completion `77.850`
tokens, and maximum load duration `2.105 s`.

### Hard negatives and generalization

Every variant returned the exact all-false result for all six hard negatives: AI/software manager,
cloud sales, manufacturing software context, Kubernetes owned by another team, incidental API, and
industrial automation. Each therefore scored 6/6.

Variant A scored 56/64 labels and 3/8 exact. Health ML, logistics backend, and AI product manager
were exact. Research CI missed automation; fintech SRE incorrectly added software engineering,
software development, and backend; media frontend incorrectly added backend and API; cloud finance
incorrectly added cloud; and API support incorrectly added API. Relative to v1, A fixed all three
health-ML false negatives but introduced one additional fintech-backend false positive and lost the
previously exact API-support case. This produces only one net label improvement and no exact-case
improvement.

Variant B scored 53/64 labels and 3/8 exact. Logistics backend, AI product manager, and cloud
finance were exact. Health ML missed software engineering, software development, and backend;
research CI added backend and missed automation; fintech SRE added software engineering, software
development, and backend; media frontend added backend and API; and API support added API. It loses
two labels relative to v1.

Variant C scored 54/64 labels and 3/8 exact. Health ML, logistics backend, and AI product manager
were exact. Research CI added backend and missed automation; fintech SRE added software engineering,
software development, and backend; media frontend added backend and API; cloud finance added cloud
and containers; and API support added API. It loses one label relative to v1.

### R180395

All three variants classified the same normalized public Parsons posting with job ID `R180395`,
title `Senior Software Developer`, and description length 6,126. All returned the eight expected
labels: AI/ML engineering, software engineering, software development, backend, API, automation,
cloud, and containers. All responses were schema-valid.

| Variant | Inference | Round trip | Prompt tokens | Completion tokens | Tokens/s |
| --- | ---: | ---: | ---: | ---: | ---: |
| A | 3,221.626 ms | 3,242.652 ms | 1,522 | 77 | 46.836 |
| B | 3,259.130 ms | 3,279.920 ms | 1,531 | 78 | 46.294 |
| C | 3,233.960 ms | 3,255.214 ms | 1,534 | 78 | 46.390 |

### Resource evidence

All three adapters reported the same 3,694 MiB loaded Qwen allocation. Host sampling observed a
3,807 MiB GPU peak, 4,300 MiB minimum free, and 100% peak GPU utilization for every run. Ollama and
adapter container-memory peaks were respectively 738.9/22.33 MiB for A, 838.2/28.96 MiB for B,
and 740.8/21.65 MiB for C.

The host has 33,593,589,760 bytes of RAM. Baseline used/available bytes were
2,072,125,440/31,521,464,320 for A, 2,126,536,704/31,467,053,056 for B, and
2,105,618,432/31,487,971,328 for C. Approximate minimum available memory from `vmstat` was
31,345,106,944, 31,363,776,512, and 31,300,579,328 bytes respectively. This approximation uses
free + buffer + cache and is reported only as supporting host-capacity evidence.

### Per-concept effects versus v1

Variant A improved software-engineering F1 from `0.9434` to `0.9630` and software-development F1
from `0.9091` to `0.9455`, but backend F1 fell from `0.7222` to `0.7027`, API from `1.0000` to
`0.9565`, cloud from `0.3448` to `0.3333`, and containers from `1.0000` to `0.9811`.

Variant B reduced recall across AI/ML, software engineering, software development, API, and
containers while increasing backend, API, cloud, and container false positives. Its backend F1
fell to `0.6842`, API to `0.9130`, and cloud to `0.3226`.

Variant C raised software-engineering and software-development recall to 1.0, but did so without
calibrating the problematic adjacent labels: backend false positives rose from 10 to 13 and API
false positives from 0 to 4. Backend F1 fell to `0.6667`, API to `0.9167`, and cloud remained low
at `0.3333`.

### Decision

Prompt engineering did **not** materially improve Qwen in this bounded experiment.

- A is the best new challenger, but it has slightly lower macro/micro F1, no exact-case
  generalization improvement, unchanged hard-negative/R180395 behavior, and approximately 7.2%
  slower average inference than v1. It is **NO MATERIAL IMPROVEMENT**.
- B is **CLEARLY WORSE THAN V1** because both benchmark quality and generalization regress
  materially despite preserving the hard negatives and R180395.
- C is **NO MATERIAL IMPROVEMENT**: perfect benchmark recall is offset by lower precision,
  additional backend/API overmatching, worse generalization, and slower inference.

There is no promotion candidate. Variant A is the best of the three challengers, but frozen v1 is
the best overall prompt and remains the deployed reference. No prompt is merged or deployed.

Key evidence SHA-256 values:

- Variant A benchmark / R180395:
  `931e442a5554f9a11c0a85440e8c1093cf35345794baa0adc493f7b8d13b4818` /
  `f7e27d4bf791c1fd80335d1e4cbad8e1536230678da77795f15cd8157e3e3fea`
- Variant B benchmark / R180395:
  `28a5dcba6ddd0cacf66c646813bca1265888370bf5dd595892b7b4d2c7ec81f7` /
  `b390f96cc92ae5fd449779b3d5a294d40a29557423d78a3bd5f12380f9d16fe2`
- Variant C benchmark / R180395:
  `ebc71b8129cf42ba842182099179e490f1c7771e4df00096ac608ae1431652c4` /
  `83985f2dc5341bd32b7585928d1020fc6489a1e3f06e63a5bec83c75ebba8508`
