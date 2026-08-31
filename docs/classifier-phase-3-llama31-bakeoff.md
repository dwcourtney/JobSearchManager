# Phase 3 bake-off: Llama 3.1 8B Instruct Q4_K_M

This is the first and only challenger in this bake-off run. It reuses the Phase 3 administrative
classifier and does not alter production regex Job Fit scoring or any normal application workflow.

## Frozen comparison contract

- Prompt version: `phase3-zero-shot-v1`
- Canonical prompt SHA-256:
  `2550a1b61a4b869e8e7c74343eba2357cbcdb7a1d2b50b581112a765ac083d9c`
- Temperature: `0`
- Seed: `42`
- Context: `8192`
- Maximum output: `384`
- Streaming: disabled
- Keep alive: indefinite (`-1`)
- Fixtures: the unchanged 40-fixture / 320-label Phase 3 set
- Qualitative sets: the unchanged six hard negatives and eight generalization cases

The request text contained a 65-character transcription of the prompt digest with an extra `c`.
The adapter-computed, deployed, and previously reported 64-character digest above is authoritative.
The system text, definitions, user template, and strict eight-boolean JSON Schema are unchanged.

## Challenger identity

- Upstream: `meta-llama/Llama-3.1-8B-Instruct`
- Ollama tag: `llama3.1:8b-instruct-q4_K_M`
- Parameters: 8.0B
- Quantization: `Q4_K_M`
- Model manifest SHA-256:
  `46e0c10c039e019119339687c3c1757cc81b9da49709a3b3924863ba87ca666e`
- Primary tensor blob SHA-256 prefix: `667b0c1932bc`
- Payload: approximately 4.9 GB
- Native context: 131,072 tokens; experiment context: 8,192 tokens
- License: Llama 3.1 Community License Agreement
- Runtime: the unchanged hardened Ollama 0.33.2 image documented in
  `classifier-phase-3.md`

Both the Qwen reference model and Llama challenger remain in the private persistent Ollama cache.
No generalized model-selection feature is introduced.

Primary sources:

- <https://huggingface.co/meta-llama/Llama-3.1-8B-Instruct>
- <https://ollama.com/library/llama3.1:8b-instruct-q4_K_M>

## Results

The single benchmark ran on curiosity against signed candidate
`304fe3017a0b9dd11a6c5c3ae28fb6a71424c17b`. The benchmark began from an unloaded model and used
the unchanged Phase 3 fixtures, qualitative cases, prompt, schema, and generation settings. There
was no retry, fixture change, or tuning pass. The complete remote artifact is
`/home/codex/jsm-llama31-artifacts-304fe3017a0b9dd11a6c5c3ae28fb6a71424c17b/benchmark.json`
(SHA-256 `f17d726e12955bf220796bffae2d349dc4de55af2c55651ae6037fa480d25fdf`).

### Fixed 40-fixture result

| Concept | TP | FP | FN | TN | Precision | Recall | F1 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| AI / ML engineering | 5 | 0 | 11 | 24 | 1.0000 | 0.3125 | 0.4762 |
| Software engineering | 23 | 0 | 4 | 13 | 1.0000 | 0.8519 | 0.9200 |
| Software development | 24 | 0 | 3 | 13 | 1.0000 | 0.8889 | 0.9412 |
| Backend development | 13 | 3 | 0 | 24 | 0.8125 | 1.0000 | 0.8966 |
| API development | 19 | 0 | 3 | 18 | 1.0000 | 0.8636 | 0.9268 |
| Automation / scripting | 19 | 0 | 7 | 14 | 1.0000 | 0.7308 | 0.8444 |
| Cloud engineering | 5 | 17 | 0 | 18 | 0.2273 | 1.0000 | 0.3704 |
| Containers | 24 | 0 | 2 | 14 | 1.0000 | 0.9231 | 0.9600 |

- Macro precision / recall / F1: `0.8800 / 0.8213 / 0.7919`
- Micro precision / recall / F1: `0.8684 / 0.8148 / 0.8408`
- Malformed outputs: `0`
- Average prompt / completion tokens: `368.125 / 76.000`
- Average throughput: `32.774 tokens/s`
- Average inference / p95 inference / average round trip: `2701.85 / 2533.03 / 2705.13 ms`
- Cold first inference: `10754.76 ms`, including `7699.61 ms` model load

The average is above p95 because it includes the single cold-load outlier; the other fixed-fixture
inferences cluster near 2.5 seconds.

### Semantic and generalization checks

Five of six hard negatives were exact. The one failure was `hard-api-incidental`, where Llama
incorrectly enabled only `technical.api-development`. The other five cases returned all eight
labels correctly:

- `hard-manager-ai-software`: exact
- `hard-cloud-sales`: exact
- `hard-manufacturing-software-context`: exact
- `hard-kubernetes-other-team`: exact
- `hard-api-incidental`: incorrect (API false positive)
- `hard-industrial-automation`: exact

Generalization scored `59/64` individual labels and `5/8` exact cases. The five label errors were
three false negatives on the health ML-platform case (backend, API, and automation), one automation
false negative on the research CI case, and one automation false negative on the fintech SRE case.

The same public Parsons R180395 posting was also classified in full. The current browser,
DOMPurify, and `JobPostingText` pipeline deterministically produced 6,126 characters. The preserved
Qwen result says 6,215 characters but did not preserve that older normalization command or input,
so the text was not padded or approximated. Llama completed the 6,126-character posting in
`4820.25 ms` inference / `4842.28 ms` round trip at `32.143 tokens/s` with 1,484 prompt tokens and
76 completion tokens. It enabled six concepts but missed AI / ML engineering and containers. Qwen's
preserved 6,215-character result enabled all eight in `3137.81 ms` inference.

### Runtime fit

- GTX 1070 device count/name: `1 / NVIDIA GeForce GTX 1070`
- GPU offload: `33/33` layers (`100%`)
- Ollama loaded allocation: `5,543 MiB`; sampled host GPU peak: `5,655 MiB`
- Peak GPU utilization: `100%`
- Fixed 8,192-token context headroom at fit time: approximately `2,474 MiB`
- Ollama container host RAM after load: approximately `696 MiB`
- Host used-RAM baseline / sampled peak: `1,974,370,304 / 2,926,030,848` bytes
- Host minimum available RAM during the run: `30,667,558,912` bytes

The exact model fit safely without changing context, quantization, model, production services, or
runtime architecture. CUDA offload used the compatible CUDA 12 runner on compute capability 6.1.

### Apples-to-apples comparison

| Metric | Qwen3 4B v1 | Llama 3.1 8B v1 |
| --- | ---: | ---: |
| Macro F1 | 0.8649 | 0.7919 |
| Micro F1 | 0.8952 | 0.8408 |
| Hard negatives exact | 6/6 | 5/6 |
| Generalization labels | 55/64 | 59/64 |
| Generalization exact cases | 3/8 | 5/8 |
| Malformed outputs | 0 | 0 |
| Average inference | 1.719 s | 2.702 s |
| Average round trip | 1.723 s | 2.705 s |
| Average throughput | 48.619 tokens/s | 32.774 tokens/s |
| Loaded allocation reported by adapter | 3,694 MiB | 5,543 MiB |

Llama improves the small generalization set, but that does not offset materially lower fixed-suite
quality, a hard-negative regression, an important real-posting regression, 57% slower average
inference, 33% lower throughput, and about 1.8 GiB more loaded VRAM. It is especially weak on the
core AI / ML label (recall `0.3125`) and continues the cloud over-inference seen in Qwen.

## Recommendation

**CLEARLY WORSE THAN QWEN.** Keep Qwen3 4B v1 as the administrative classifier reference. The
challenger implementation must not be merged or automatically deployed because the comparison
quality gate failed. Production regex scoring remains unchanged, both model artifacts remain
cached, and no second challenger or prompt-tuning pass is started by this experiment.
