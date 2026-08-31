# Phase 3 bake-off: Gemma 3 4B Instruct Q4_K_M

This is the second Phase 3 challenger and the only challenger in this run. It reuses the hardened
administrative classifier and does not alter production regex Job Fit scoring or normal workflows.

## Frozen comparison contract

- Prompt version: `phase3-zero-shot-v1`
- Canonical prompt SHA-256:
  `2550a1b61a4b869e8e7c74343eba2357cbcdb7a1d2b50b581112a765ac083d9c`
- Temperature / seed: `0 / 42`
- Context / maximum output: `8192 / 384`
- Streaming: disabled
- Keep alive: indefinite (`-1`)
- Fixtures: unchanged 40 fixtures / 320 labels / eight concepts
- Qualitative sets: unchanged six hard negatives and eight generalization cases

The system text, concept definitions, user template, and strict eight-boolean JSON Schema are
unchanged. Gemma's native Ollama template accepts the exact `system` and `user` messages but
serializes both roles as user turns. No request setting or prompt byte is substituted.

## Challenger identity

- Upstream: `google/gemma-3-4b-it` by Google DeepMind
- Ollama tag: `gemma3:4b-it-q4_K_M`
- Parameters: 4.3B
- Quantization: `Q4_K_M`
- Model manifest SHA-256:
  `a2af6cc3eb7fa8be8504abaf9b04e88f17a119ec3f04a3addf55f92841195f5a`
- Primary model blob SHA-256:
  `aeda25e63ebd698fab8638ffb778e68bed908b960d39d0becc650fa981609d25`
- Payload: approximately 3.3 GB
- Native context: 131,072 tokens; fixed experiment context: 8,192 tokens
- License: Gemma Terms of Use
- Runtime: unchanged hardened Ollama 0.33.2 image from Phase 3

Primary sources:

- <https://huggingface.co/google/gemma-3-4b-it>
- <https://ai.google.dev/gemma/docs/core/model_card_3>
- <https://ai.google.dev/gemma/terms>
- <https://ollama.com/library/gemma3:4b-it-q4_K_M>

## Historical rejected challenger

Llama 3.1 8B v1 was rejected as **CLEARLY WORSE THAN QWEN**: macro/micro F1
`0.7919/0.8408`, hard negatives `5/6`, generalization `59/64` labels and `5/8` exact,
zero malformed outputs, 2.702-second average inference, and 5,543 MiB loaded allocation. Its full
evidence remains in signed draft PR #20 and the preserved curiosity benchmark/report artifacts; it
is not part of active runtime selection and its model payload was removed from the shared cache.

## Results

The exact signed candidate `043706c39a82be2d787612b63d5a65e8942b392e` passed all 136
deterministic architecture tests, the complete local source/security audit, prompt self-test, and
exact-SHA CI run `33449074006`. The fit gate passed before the benchmark was started.

### GPU fit gate

- Cold load: 3.626 seconds
- First diagnostic inference: 5.546 seconds
- Output: schema-valid, zero malformed responses
- Full GPU offload: 35/35 layers (100%)
- Loaded model allocation: 2,889 MiB reported by the adapter
- Sampled total GPU peak: 3,971 MiB, leaving approximately 4,221 MiB headroom
- Ollama process: 3.0 GB, 100% GPU, fixed 8,192-token context
- Host RAM after load: approximately 1.043 GiB for Ollama and 14.75 MiB for the adapter

### One authorized benchmark run

The benchmark ran exactly once against the frozen 40 fixtures (320 labels), six hard negatives,
and eight generalization cases.

| Measure | Gemma 3 4B | Qwen 2.5 3B reference |
| --- | ---: | ---: |
| Macro F1 | 0.8493 | 0.8649 |
| Micro F1 | 0.8839 | 0.8952 |
| Hard negatives (exact) | 4/6 | 6/6 |
| Generalization labels | 57/64 | 55/64 |
| Generalization cases (exact) | 3/8 | 3/8 |
| Malformed responses | 0 | 0 |
| Average inference | 2,041.41 ms | 1,719.40 ms |
| Average round trip | 2,044.82 ms | 1,722.60 ms |
| Average throughput | 49.93 tokens/s | 48.62 tokens/s |
| Loaded allocation | 2,889 MiB | 3,694 MiB |
| R180395 labels | 7/8 | 8/8 |
| R180395 inference | 3,706.21 ms | 3,137.81 ms |

Gemma's macro precision/recall were `0.8160/0.9653`; micro precision/recall were
`0.8168/0.9630`. Its per-concept F1 scores were AI/ML `0.9333`, software engineering
`0.9811`, software development `0.9474`, backend `0.6190`, API `0.9565`, automation
`0.9804`, cloud `0.4167`, and containers `0.9600`. The weak backend and cloud precision
produced 16 and 14 false positives respectively.

The two failed hard negatives were cloud sales (false-positive cloud) and incidental API mention
(false-positive API). On R180395, Gemma returned seven of eight expected labels and missed only
`role.ai-ml-engineering`; Qwen returned all eight under the same frozen contract.

Benchmark resource sampling observed a 3,975 MiB total GPU peak, 100% peak utilization, and a
host-memory peak of 3,289,432,064 bytes used with at least 30,304,157,696 bytes available. The
cold first benchmark inference was 5.699 seconds and the maximum observed cold load was 3.700
seconds.

### Decision

**CLEARLY WORSE THAN QWEN.** Gemma loses both primary aggregate accuracy measures, falls from
6/6 to 4/6 on hard negatives, gains no exact generalization cases, misses an expected R180395
label, and is approximately 18.7% slower on average. Its lower VRAM allocation and slightly higher
token throughput do not offset those regressions.

Do not merge or deploy this challenger. Qwen remains the selected and deployed Phase 3 model.
After this evidence is published and CI is complete, the Gemma payload is removed from the shared
cache while the evidence artifacts remain preserved. No third challenger is started by this work.

### Preserved evidence

Curiosity evidence directory:
`/home/codex/jsm-gemma3-artifacts-043706c39a82be2d787612b63d5a65e8942b392e`

- `benchmark.json`: `d82f6cd35669e0f077525afbe92f9237f8dce0bc6030fcb13717e922ae50aca0`
- `r180395-full-response.json`: `c97a2c0252f31e32edc5cf0d1debc7bc4495d0cbd1a5d17ecec810fbcd8e2660`
- `fit-diagnostic.json`: `7f36981b82e69d3bba04e2b53055f2ca919b1f155993f456c4914bc948fbf0e9`
- `gpu-samples.csv`: `1370ec2e393086074c8ff0ad8d9e558360c0baf061befd0e983d18826c7b6f77`
- `host-samples.csv`: `4a16cd8fc9a169f62692aaafabb5db6de862b991d498a2f17f4807aa2ab73801`
