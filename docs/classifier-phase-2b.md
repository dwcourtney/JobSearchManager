# Classifier service Phase 2B model replacement

Phase 2B is a focused replacement of the experimental zero-shot model. It does not change the
classifier protocol, concepts, hypotheses, 384-token chunks with 64-token overlap, thresholds,
40-fixture/320-label evaluation set, Admin workflow, Job Fit scoring, or production persistence.
The complete Phase 2 DistilRoBERTa investigation remains in `classifier-phase-2.md` as historical
evidence.

## Generic and model-specific boundary

Generic infrastructure remains unchanged: the private HTTP service, length-delimited request
validation, offline loading through Transformers auto classes, dynamic NLI-label discovery,
maximum entailment aggregation, GPU diagnostics, one-inference lock, JSM client, evaluation UI,
benchmark schema, CI, protected-main deployment, rollback, networks, and container hardening.

The model-specific boundary is limited to the immutable Hugging Face model ID/revision, expected
weight and config SHA-256 values, offline cache path, tokenizer-file allowlist, deployment mount,
and contract fixture metadata.

## Selection report

The sole replacement is `cross-encoder/nli-deberta-v3-base` at immutable revision
`6c749ce3425cd33b46d187e45b92bbf96ee12ec7`. It is an Apache-2.0 licensed DeBERTa-v3-base
sequence classifier trained on SNLI and MultiNLI with contradiction, entailment, and neutral
outputs. Its safetensors file contains 184,424,963 parameters and is 737,726,552 bytes. The exact
offline cache is 748,856,131 bytes including download metadata. The weights alone require about
704 MiB in FP32; the expected steady GPU footprint is approximately 1.0-1.6 GiB, to be measured
on the GTX 1070 rather than treated as a pass criterion.

The pinned fast tokenizer loads from `tokenizer.json` and is DeBERTa-v2 compatible. `spm.model`,
`tokenizer_config.json`, `special_tokens_map.json`, and `added_tokens.json` are also cached so the
model snapshot is complete for the supported tokenizer path. The existing pinned Transformers
5.16.1 auto classes and PyTorch 2.6 CUDA runtime support the architecture; no runtime dependency
change is required.

This is materially stronger than the historical 82,121,221-parameter, six-layer distilled
RoBERTa model: it has about 2.25 times as many parameters and uses a 12-layer DeBERTa-v3 encoder.
The replacement model card reports 92.38% SNLI test accuracy and 90.04% MNLI mismatched accuracy.
RoBERTa-large-MNLI was rejected because its roughly 355 million parameters and 1.43 GB weights
would impose substantially more latency and memory while changing model lineage. Smaller DeBERTa
variants were rejected because they make the required strength increase less clear.

The pinned artifacts are:

- `model.safetensors`: `d8148c6d49e0a7925134294c56326c71fe0ab1dc390e37355e00c7efbb488afa`
- `config.json`: `897e756eb59d3183adb505952e7910e7cbc7750a43f3b3747a96b688d2b02a47`

## Evaluation and decision

The replacement is compared against both the fixed regex baseline and the historical pinned
DistilRoBERTa results at thresholds 0.3, 0.5, and 0.7. Evidence must include per-concept precision,
recall, and F1; macro and micro aggregates; load/cold/warm/full-posting latency; RAM and VRAM;
peak sampled GPU use; the full R180395 posting; and the specified hard-negative categories.

The signed topic SHA must pass hosted CI and an isolated, exact-image GPU proof on curiosity before
merge. Production JSM, Mailpit, and ai801 remain healthy during that proof. Merge is through
protected main, and only automatic curiosity CD may replace the running classifier. Historical
Phase 2 reports remain preserved; only the obsolete active DistilRoBERTa cache may be removed after
the replacement is deployed and verified.

The final classification remains **PROMISING**, **MIXED**, or **NOT PROMISING** under the Phase 2
decision rule. No outcome authorizes training, fine-tuning, another model, Phase 3, or a production
Job Fit change.
