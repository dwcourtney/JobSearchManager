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

Results are recorded only after the exact signed candidate passes local validation, protected CI,
and the fixed-context GPU fit gate on curiosity.
