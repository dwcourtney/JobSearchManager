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

Results are recorded only after the exact signed candidate passes the fixed benchmark on curiosity.
