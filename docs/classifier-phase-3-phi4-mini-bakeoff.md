# Phase 3 bake-off: Phi-4-mini 3.8B Instruct Q4_K_M

This is the third Phase 3 challenger and the only challenger in this run. It reuses the hardened
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
unchanged. Phi's native Ollama template accepts and preserves the exact `system` and `user`
messages. No request setting or prompt byte is substituted.

## Challenger identity

- Upstream: `microsoft/Phi-4-mini-instruct`
- Ollama tag: `phi4-mini:3.8b-q4_K_M`
- Parameters: 3.84B (Ollama reports 3.8B)
- Architecture: dense decoder-only Phi-3 family GGUF
- Quantization: `Q4_K_M`
- Model manifest SHA-256:
  `78fad5d182a7c33065e153a5f8ba210754207ba9d91973f57dffa7f487363753`
- Primary model blob SHA-256:
  `3c168af1dea0a414299c7d9077e100ac763370e5a98b3c53801a958a47f0a5db`
- Primary model blob size: 2,491,874,624 bytes (approximately 2.5 GB)
- Native context: 131,072 tokens; fixed experiment context: 8,192 tokens
- Embedding length / layers: 3,072 / 32
- License: MIT
- Runtime: unchanged hardened Ollama 0.33.2 image from Phase 3

Primary sources:

- <https://huggingface.co/microsoft/Phi-4-mini-instruct>
- <https://huggingface.co/microsoft/Phi-4-mini-instruct/blob/main/LICENSE>
- <https://ollama.com/library/phi4-mini:3.8b-q4_K_M>
- <https://ollama.com/library/phi4-mini/tags>

## Historical rejected challengers

- Llama 3.1 8B v1: **CLEARLY WORSE THAN QWEN**; macro/micro F1 `0.7919/0.8408`,
  hard negatives `5/6`, generalization `59/64` labels and `5/8` exact, R180395 `6/8`,
  2.702-second average inference, and 5,543 MiB loaded allocation. Evidence: signed draft PR #20.
- Gemma 3 4B v1: **CLEARLY WORSE THAN QWEN**; macro/micro F1 `0.8493/0.8839`,
  hard negatives `4/6`, generalization `57/64` labels and `3/8` exact, R180395 `7/8`,
  2.041-second average inference, and 2,889 MiB loaded allocation. Evidence: signed draft PR #21.

Neither rejected model is part of active runtime selection, and both rejected payloads were absent
before Phi provisioning. Qwen remains the deployed reference winner.

## Results

### Reproducibility and fit gate

- Signed candidate commit: `fda30860e6ab2671d15259910f5177ad854f57bf`
- Candidate CI: run `33452051943`, all required jobs passed against the exact SHA
- Local validation: all 136 deterministic .NET architecture tests, JavaScript/source/security
  tests, and the classifier self-test passed
- Exact proof checkout:
  `/home/codex/jsm-phi4-proof-fda30860e6ab2671d15259910f5177ad854f57bf`
- Evidence directory:
  `/home/codex/jsm-phi4-artifacts-fda30860e6ab2671d15259910f5177ad854f57bf`
- The proof adapter and Ollama containers ran as UID 65532, read-only, with all capabilities
  dropped and `no-new-privileges`, on a private internal Docker network with no published ports.
- The adapter had no mounts. Ollama had only the existing shared model-cache mount.

The required fit gate passed before the benchmark. The cold diagnostic request completed in
`4,315.659 ms`, including a `2.420 s` load, with 324 prompt tokens, 76 output tokens, no malformed
output, and `51.918` tokens/s. Ollama fully offloaded all 33/33 reported layers to the GTX 1070.
The fit sample peaked at 3,637 MiB GPU memory and 100% GPU utilization at the fixed 8,192-token
context. The runtime reported 2,368.57 MiB CUDA model data, 480.81 MiB CPU-mapped model data,
1,024 MiB CUDA KV cache, 136.02 MiB CUDA compute, and 40.02 MiB CUDA host compute. The model was
then explicitly unloaded and the GPU returned to its 3 MiB idle state before the one authorized
benchmark run.

### Fixed benchmark comparison

| Measure | Deployed Qwen reference | Phi-4-mini challenger |
| --- | ---: | ---: |
| Macro F1 | 0.8649 | 0.7798 |
| Micro F1 | 0.8952 | 0.8136 |
| Hard negatives exact | 6/6 | 4/6 |
| Generalization labels | 55/64 | 57/64 |
| Generalization exact | 3/8 | 5/8 |
| Malformed outputs | 0 | 0 |
| Average inference | 1.719 s | 1.640 s |
| Average round trip | 1.723 s | 1.644 s |
| Throughput | 48.619 tokens/s | 51.732 tokens/s |
| Loaded/observed GPU allocation | 3,694 MiB | 3,529 MiB adapter; 3,655 MiB sampled peak |
| R180395 concepts | 8/8 | 6/8 |
| R180395 inference | 3,137.81 ms | 2,822.85 ms |

Phi's full aggregate metrics were macro precision `0.775480`, macro recall `0.893684`, macro F1
`0.779769`, micro precision `0.750000`, micro recall `0.888889`, and micro F1 `0.813559`. Average
inference was `1,640.330 ms`, p95 inference was `1,609.569 ms`, average round trip was
`1,643.622 ms`, and average throughput was `51.732` tokens/s. The cold first benchmark request took
`4,499.552 ms`, including a maximum observed load duration of `2.613 s`. All 40 outputs were valid
against the frozen schema.

Per-concept results (`TP/FP/FN/TN`, precision/recall/F1):

- AI/ML engineering: `10/0/6/24`, `1.0000/0.6250/0.7692`
- software engineering: `27/5/0/8`, `0.8438/1.0000/0.9153`
- software development: `27/5/0/8`, `0.8438/1.0000/0.9153`
- backend: `13/17/0/10`, `0.4333/1.0000/0.6047`
- API: `20/2/2/16`, `0.9091/0.9091/0.9091`
- automation: `20/0/6/14`, `1.0000/0.7692/0.8696`
- cloud: `5/18/0/17`, `0.2174/1.0000/0.3571`
- containers: `22/1/4/13`, `0.9565/0.8462/0.8980`

Hard-negative exact outcomes were 4/6. Manufacturing, another-team Kubernetes, incidental API,
and industrial automation were exact. The AI software manager case incorrectly added software
engineering, software development, backend, and API; the cloud-sales case incorrectly added cloud.

Generalization scored 57/64 labels and 5/8 exact cases. Health ML, logistics backend, media
frontend, AI manager, and API support were exact. Research CI added backend and missed automation;
fintech SRE added software engineering, software development, and backend and missed automation;
cloud finance incorrectly added cloud.

The benchmark collected 1,846 GPU samples, 1,645 container samples, and 165 host VM samples. GPU
usage peaked at 3,655 MiB with 4,452 MiB minimum free and 100% utilization. Peak observed container
host memory was 600.6 MiB for Ollama and 30.78 MiB for the adapter. Host baseline was
2,236,215,296 bytes used and 31,357,374,464 bytes available out of 33,593,589,760 bytes total;
minimum sampled available memory was 31,405,600,768 bytes.

### R180395 production-record check

The public Parsons Workday record was normalized through the exact frozen DOMPurify and
`JobPostingText` path. Its description length was 6,126 characters, matching the earlier Qwen and
Gemma checks. Phi returned six concepts: software engineering, software development, backend, API,
cloud, and containers. It missed AI/ML engineering and automation. The response was schema-valid,
used 1,449 prompt tokens and 76 output tokens, and completed in `2,822.849 ms` inference and
`2,844.654 ms` round trip at `48.918` tokens/s. Qwen returned all 8/8 concepts.

### Decision

**CLEARLY WORSE THAN QWEN — REJECTED; DO NOT MERGE OR DEPLOY.**

Phi was approximately 4.6% faster on average inference, approximately 6.4% faster in measured
token throughput, and used about 165 MiB less adapter-reported GPU allocation. It also improved the
secondary generalization set by two labels and two exact cases. Those gains do not offset the large
primary quality regression: macro F1 fell by about 0.0851, micro F1 fell by about 0.0816, hard
negatives fell from 6/6 to 4/6, and R180395 fell from 8/8 to 6/8. Qwen remains the deployed Phase 3
reference. Production was not changed, and no further challenger is started by this experiment.

### Evidence checksums

- `benchmark.json`: `c7624e273fb5f709d28c8338e1000e3a6d7ddbeb3773f67540e9dc1fc1d41150`
- `summary.json`: `887c6a46be1d66374db44c3c14c36f1d9ed45483733b7fb7b3d9752ee0d8df21`
- `regex-report.json`: `8e000a53710594d08b88157a142b7890d4e7a02dcbb09e508e13f4662223ba26`
- `r180395-request.json`: `f00d24a83f201267c6c03ade9e4495f0d070d8e492c0a9386d01b16c1fb47d86`
- `r180395-full-response.json`: `de9375a146b79c95196bb3b2f5ecbb2100b073664d1a285086b2306bdab0afea`
- `fit-diagnostic.json`: `91cb5d60d23a8f3a4ffae467088b57d9609dd84f84911687a84e9f2de905ba59`
- `fit-gpu-samples.csv`: `ba275747d9f408893ccf31c8049927f2612a2ee90389c966efb0bb077f869eda`
- `fit-container-stats.csv`: `6d752f67c93385ab3ea723e3be0270d4df7a3e691cd64dde81c1e85a0685d137`
- `fit-host-vmstat.txt`: `1a22c5f7ecd0d295dfefc12d0109506d67f0c56b1beb3483a6189d7f9ee435fd`
- `benchmark-gpu-samples.csv`: `b5897445eb6d426906c241bcf262bb0090fe057aefbfa80ab5bfd107edb9f32e`
- `benchmark-container-stats.csv`: `93fb107255e9442b422c821637063404cfe583e52eaafc91539334a8e8af8f46`
- `benchmark-host-vmstat.txt`: `302a889714367bfd76edc0061662ebfa574aed2f44766534388a65d304b4f9dc`
