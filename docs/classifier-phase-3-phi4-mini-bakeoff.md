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

Results are recorded only after the exact signed candidate passes local validation, protected CI,
and the fixed-context GPU fit gate on curiosity.
