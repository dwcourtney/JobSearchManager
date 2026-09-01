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

Results will be appended only after this complete three-prompt definition and all fingerprints have
been committed, signed, validated locally, and passed exact-SHA CI. Each prompt then receives
exactly one benchmark run and no post-result edits.
