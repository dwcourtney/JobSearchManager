# Classifier service Phase 2 experiment

Phase 2 is an evaluation-only zero-shot NLI experiment. It does not change Job Fit scoring,
production detectors, workspace schemas, posting ingestion, or automatic refresh. It sends only
the explicitly requested title/description to the private classifier and never writes predictions
to a workspace.

## Model investigation and selection

The selected model is `cross-encoder/nli-distilroberta-base` at immutable Hugging Face revision
`b14d131f9d32668a5e6a982729b57ff6ed5dfcbd`. It is Apache-2.0 licensed, trained on SNLI and
MultiNLI, exposes an NLI sequence-classification head, has a 328 MB safetensors artifact, and is
supported by the Transformers zero-shot formulation. It was the smallest reviewed candidate with
both an explicit permissive license and a suitable pretrained NLI head.

Rejected candidates:

- `typeform/distilbert-base-uncased-mnli` is slightly smaller, but its model card does not declare
  a license. That ambiguity is unacceptable for a deployed experiment.
- `cross-encoder/nli-deberta-v3-small` is Apache-2.0 and capable, but its 568 MB weights are
  materially larger without a Phase 2 requirement that justifies the additional footprint.

The runtime is pinned to Python 3.12, PyTorch 2.6.0 CUDA 12.6, Transformers 5.16.1, and the exact
transitive Python versions in `classifier-service/requirements.lock`. The CUDA 12.6.3 cuDNN base
is digest-pinned. PyTorch 2.6 was chosen over 2.12 after an empirical image investigation: 2.12
required newer duplicate CUDA/NCCL/cuDNN packages, while 2.6 uses the pinned CUDA 12.6 base
directly and retains Pascal support.

## Input and inference contract

The model receives `title`, two newlines, and the complete supplied description. The tokenizer
creates deterministic 384-token chunks with 64-token overlap; every chunk is evaluated, so long
postings are not silently truncated. Requests above 500,000 characters are rejected. The service
runs eight hypothesis pairs per chunk in one GPU batch and takes the maximum entailment probability
for each concept across chunks. Entailment is normalized against contradiction, matching multi-label
zero-shot NLI behavior. A process-wide lock allows one GPU inference at a time.

The fixed concepts are AI/ML engineering, software engineering, software development, backend
development, API development, automation/scripting, cloud engineering, and containers. Hypotheses
are generic responsibility statements stored in both the service and JSM contract; they were not
tuned against individual fixture wording. Scores are evaluated at global thresholds 0.3, 0.5, and
0.7. The best threshold is the lowest threshold tied for the highest macro F1.

## Isolation and lifecycle

Model files live only in `/home/codex/jsm-lab/models/nli-distilroberta-base`, outside persistent JSM
account/workspace data. Deployment downloads the exact revision in a one-shot container with egress,
then mounts the cache read-only. The running service sets `HF_HUB_OFFLINE=1` and
`TRANSFORMERS_OFFLINE=1`, has only the internal Compose network, runs as UID/GID 65532, is read-only,
drops all capabilities, and is limited to two CPUs and 4 GB host RAM. GPU access is exactly one GTX
1070. Model output is returned only to the explicit admin evaluation call and is not persisted.

Hosted CI never downloads model weights or requires a GPU. It validates Python schema/chunking,
the C# client and threshold math, the fixed 40-fixture/320-label scope, image health, the Phase 1
echo contract, and a safe HTTP 503 when the model cache is absent. Real inference is a curiosity-only
pre-merge gate against a signed candidate SHA.

## Benchmark and decision rule

`scripts/benchmark-zero-shot.py` evaluates the same independently labeled Tier 1 fixtures used by
the regex detector report. Its artifact records candidate SHA, immutable model identity, runtime,
device, aggregate and per-concept metrics, thresholds, latency, token/chunk counts, and score vectors;
it omits posting text. The qualitative R180395 check uses the full locally cached posting on curiosity
and reports only title/description length, scores, and timings.

The final report classifies the experiment as:

- **PROMISING** when the best zero-shot macro F1 is at least 0.05 above regex, no concept loses more
  than 0.15 F1, and average fixture inference is at most 500 ms.
- **MIXED** when it runs reliably but does not meet every promising condition.
- **NOT PROMISING** when it cannot run within the resource bounds, or best macro F1 is more than 0.05
  below regex.

No result authorizes Phase 3, training, fine-tuning, full-corpus processing, or production scoring
integration.
