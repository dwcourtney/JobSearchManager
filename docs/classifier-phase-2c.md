# Classifier service Phase 2C embedding experiment

Phase 2C is an evaluation-only replacement of the active experimental NLI model with one
embedding formulation. Normal Job Fit remains regex-only. The existing private classifier
network, non-root/read-only container, GPU gate, exact-SHA deployment, rollback, Admin
authorization, fixed fixtures, CodeQL, and Trivy controls are reused.

Historical investigations remain unchanged in `classifier-phase-2.md` (DistilRoBERTa) and
`classifier-phase-2b.md` (DeBERTa-v3-base).

## Pre-implementation model selection

The reviewed compact shortlist was:

| Model | Relevant characteristics | Selection decision |
|---|---|---|
| `sentence-transformers/all-MiniLM-L6-v2` | 384 dimensions; 256-wordpiece default; Apache-2.0; very small and fast | Rejected as the deliberately tiny cheapest baseline rather than the requested stronger compact trial. |
| `sentence-transformers/all-mpnet-base-v2` | 768 dimensions; MPNet base; Apache-2.0 | Strong established sentence-similarity baseline, but BGE v1.5 provides a 512-token context and an explicit short-query/long-passage retrieval formulation. |
| `intfloat/e5-base-v2` | 768 dimensions; 512 tokens; MIT; documented `query:`/`passage:` prefixes | Strong alternative, but its compressed absolute-score distribution is awkward for a simple global classification threshold. |
| `BAAI/bge-small-en-v1.5` | 384 dimensions; 512 tokens; MIT | Rejected to avoid testing only the smallest member when base remains inexpensive on the GTX 1070. |
| **`BAAI/bge-base-en-v1.5`** | **BERT base; 768 dimensions; 512 tokens; MIT; normalized embeddings and documented retrieval instruction** | **Selected as the sole Phase 2C model.** |

Primary sources:

- <https://huggingface.co/BAAI/bge-base-en-v1.5>
- <https://huggingface.co/intfloat/e5-base-v2>
- <https://huggingface.co/sentence-transformers/all-mpnet-base-v2>
- <https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2>

## Selected immutable model

- Model: `BAAI/bge-base-en-v1.5`
- Architecture: 12-layer BERT-base bi-encoder using the final `[CLS]` vector
- Parameters: 109,482,240 F32 parameters plus 512 I64 entries; 109,482,752 tensors total
- Embedding dimension: 768
- Maximum model input: 512 tokens
- License: MIT
- Immutable revision: `a5beb1e3e68b9ab74eb54cfd186867f64f240e1a`
- Safetensors: 437,955,512 bytes; SHA-256
  `c7c1988aae201f80cf91a5dbbd5866409503b89dcaba877ca6dba7dd0a5167d7`
- Config: 777 bytes; SHA-256
  `bc00af31a4a31b74040d73370aa83b62da34c90b75eb77bfa7db039d90abd591`
- Selected model/tokenizer files: approximately 438.9 MB before local Hugging Face metadata
- Expected loaded VRAM: comfortably below the existing 1.6 GiB DeBERTa allocation; exact values
  must be measured on the GTX 1070 before merge
- Runtime: existing Python 3.12, PyTorch `2.6.0+cu126`, Transformers `5.16.1`, CUDA 12.6;
  no SentenceTransformers/FlagEmbedding runtime dependency is added

The official model card reports an English MTEB average of 63.55 and classification average of
75.53. It also documents that v1.5 improves similarity-score distribution, recommends the query
instruction `Represent this sentence for searching relevant passages: ` for short-query to
long-passage retrieval, requires no passage instruction, uses normalized embeddings, and warns
that downstream absolute thresholds must be calibrated on the target data.

## Fixed embedding formulation

The eight concept IDs and independently labeled 40-fixture/320-label scope are unchanged. Concept
representations are generic, versioned descriptions derived from the canonical Job Fit concept
descriptions and made explicit about direct technical responsibility. They are not tailored to
individual fixtures.

For each process load:

1. Prefix each concept description with the model's documented retrieval instruction.
2. Embed each concept once with `[CLS]` pooling.
3. L2-normalize and retain the eight 768-dimensional F32 vectors in memory.

For each posting:

1. Tokenize the original title and complete original description, without detector output,
   evidence, score, preferences, or summary input.
2. Split description tokens into the established 384-token chunks with 64-token overlap.
3. Include the title in every chunk and batch only those chunks from the one posting.
4. Embed each chunk with `[CLS]` pooling and L2 normalization.
5. Calculate cosine similarity as the matrix product of normalized posting and concept vectors.
6. Aggregate each concept with maximum similarity across all posting chunks.

The API calls the value `similarity`, never probability. It reports model type, embedding
dimension, concept-cache key/memory, load and initialization times, aggregation, global threshold,
and per-concept similarity/match results.

The documented BGE distribution motivates a fixed global threshold grid from `0.50` through
`0.90` in `0.05` increments. The official comparison selects the lowest threshold tied for highest
macro F1. There is no per-concept threshold tuning. A simple positive-description formulation is
used; no learned or fixture-tuned contrastive classifier is introduced.

The fixed 40-posting calibration selected `0.50` as the single global runtime default. It is
applied uniformly to all eight concepts; the complete threshold sweep remains the authoritative
evaluation evidence.

## Gate

Merge is allowed only after a signed exact topic SHA passes local/hosted tests and isolated
curiosity proof: exact images and hashes, Trivy, CUDA on the single GTX 1070, model inference,
40/320 benchmark, R180395, qualitative hard negatives, JSM protocol round trip, and proof that
production JSM/classifier/Mailpit/ai801 remained healthy and untouched. Only protected main and
automatic curiosity CD may replace the deployed experiment.
