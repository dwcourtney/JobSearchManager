# Classifier service Phase 3 local LLM experiment

Phase 3 replaces BGE only as the active **experimental** classifier. Production Job Fit scoring
remains the existing regex detector. No model call occurs during ingestion, browsing, scoring, or
other normal workflows; inference is reachable only through the private classifier service and
Admin/evaluation endpoints.

## Immutable model and runtime

- Upstream model: `Qwen/Qwen3-4B-Instruct-2507`
- Parameters: 4.0B total / 3.6B non-embedding, 36 layers
- Ollama package: `qwen3:4b-instruct-2507-q4_K_M`
- Quantization: `Q4_K_M`
- Model manifest SHA-256:
  `0edcdef34593eac1aa2be9c7d06c432dcf81945adca5eca2f27662c18f168ba0`
- Model tensor blob SHA-256:
  `85e4a5b7b8ef0e48af0e8658f5aaab9c2324c76c1641493f4d1e25fce54b18b9`
- Model tensor blob size: 2,497,280,480 bytes
- License: Apache-2.0
- Upstream native context: 262,144 tokens; experiment cap: 8,192 tokens to bound GTX 1070 memory
- Runtime: Ollama 0.33.2 source commit
  `f96e7aa0513b9973a0ccc71be414c2ecb9d65b1a`, using CUDA runners from the official
  Linux/amd64 manifest `sha256:9e7d782e99880c70f9563c51633da875ca605518a8f8d95c2532bda70a027b7a`

The exact requested Qwen instruction model is available as an official Ollama package, so no
substitution was made. It was selected as the sole initial model because it is materially more
capable than the failed encoder experiments while its 2.5 GB Q4 weights fit the 8 GB GTX 1070.

The official 0.33.2 image failed the repository's unsuppressed Trivy gate because its static Go
binary contained 41 dependencies/standard-library findings with available fixes. The runtime
therefore rebuilds only that control binary from the exact 0.33.2 source commit with Go 1.26.6 and
fixed module versions, embeds stable 0.33.2 version metadata, and retains the official image's
CUDA runners. The security rebuild itself must pass the same High/Critical and secret gates; no
Trivy ignore or policy exception is used.

Primary sources:

- <https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507>
- <https://ollama.com/library/qwen3:4b-instruct-2507-q4_K_M>
- <https://docs.ollama.com/capabilities/structured-outputs>
- <https://docs.ollama.com/api/usage>

## Architecture and security

The private `classifier` network contains two processes:

1. `ollama` owns the single GPU and a dedicated persistent `/models` cache. It has no published
   port, Docker socket, or JSM data mount. It is non-root, read-only except for the model volume
   and `/tmp`, capability-free, and uses `no-new-privileges`.
2. `job-classifier` is a small standard-library Python adapter with no GPU, CUDA, or model mount.
   It owns the stable JSM protocol, prompt, JSON Schema, strict output validation, and telemetry.

JSM has no CUDA/NVIDIA dependency. The Ollama base image/source/toolchain/modules, model manifest,
adapter base image, and Git revision are pinned. Deployment provisions and hashes the model while temporary registry egress
is available; the long-running runtime remains on the internal network.

## Fixed prompt method

One request includes the posting title, complete posting text, and the eight unchanged canonical
concept IDs/descriptions. One Ollama chat call returns a flat JSON object containing exactly eight
booleans. The JSON Schema requires every key and forbids additional properties; the adapter also
checks exact keys and strict boolean types. Free-form prose is never parsed.

Generation is fixed at temperature `0`, seed `42`, context `8192`, and maximum output `384`.
The active prompt `phase3-zero-shot-v1` explicitly distinguishes direct hands-on responsibility
from incidental product, customer, team, management, or technology mentions. Its honest exact-SHA
baseline was macro/micro F1 `0.8649/0.8952`, all six hard negatives correct, `55/64`
generalization labels correct (`3/8` cases exact), and zero malformed outputs. Cloud and backend
responsibilities were over-inferred, so the one allowed bounded general prompt pass tested
`phase3-general-evidence-v2`: evaluate every concept independently, require explicit evidence for
that specific activity, and do not infer platform or backend responsibility from adjacent work.
V2 retained all six hard negatives and improved generalization to `56/64` labels (`4/8` exact), but
degraded macro/micro F1 to `0.8456/0.8761` and was therefore rejected. V1 remains active. Neither
prompt contains fixture, company, or posting-specific exceptions. The active prompt's SHA-256 is
reported with every response.

The official result uses the unchanged 40 fixtures, 320 labels, and eight concepts. The six
historical semantic hard-negative categories are reported separately, as is the bounded eight-case
cross-company generalization set in `LlmQualitativeFixtures.json`; neither alters benchmark labels.
The script reports exact counts, macro/micro metrics, malformed outputs, token telemetry, average
and p95 inference, round trip, model load, hard negatives, and generalization observations.

Historical evidence remains in the Phase 2, 2B, and 2C documents. The recorded aggregate comparison
is regex macro/micro F1 `0.9976/0.9969`, DistilRoBERTa macro F1 `0.6425`, DeBERTa macro/micro F1
`0.7955/0.8184`, and BGE macro/micro F1 `0.5742/0.5951` at threshold `0.50` with all six hard
negatives overmatched. Runtime measurements and the Phase 3 recommendation are recorded only from
the exact signed candidate on curiosity, never inferred from development hardware.
