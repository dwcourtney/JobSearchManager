# DeBERTa cheap-reject experiment v1

Offline experiment only. No production imports, rule edits, training, generative inference, or holdout evaluation. See `docs/deberta-cheap-reject-experiment.md` for the decision and full sweep.

The immutable model revision and weight SHA-256 are in `protocol.json`. `predictions.json` contains all frozen scores, input/runner/weight fingerprints, GPU timings, memory, and environment. `metrics-extended.json` contains all 72 configurations with dataset metrics, false rejects, reviewed reject examples, and leakage-family counts. `score_protocol.json` explicitly identifies the supplemental development sweep. `frontier.json` is an optimistic label-informed threshold bound, not validation. The initial 39-configuration sweep is reproducible by omitting `--score-protocol`; no second model inference was needed.

Python 3.12 and the resolved Linux dependencies in `requirements-lock.txt` reproduce the environment. Inference ran in an isolated venv on curiosity's GTX 1070, using CUDA-compatible PyTorch 2.5.1+cu121. No Windows Docker/WSL setup is needed. Model download is about 568 MB; weights are not included here. The archives contain only the previously holdout-excluded public posting snapshot and label-free model inputs (plus synthetic fixtures), not application credentials or user profiles. The parent directory's earlier reports did not bundle the full cache; this experiment does so to make exact replay possible.

From this directory on curiosity (copy the entire parent evaluation directory, including its frozen reference files):

```sh
python3 -m venv /tmp/jsm-deberta-replay
/tmp/jsm-deberta-replay/bin/pip install --extra-index-url https://download.pytorch.org/whl/cu121 -r requirements-lock.txt
gzip -dc eligible.json.gz > /tmp/jsm-deberta-eligible.json
gzip -dc input.json.gz > /tmp/jsm-deberta-input.json
/tmp/jsm-deberta-replay/bin/python prepare_input.py --pool /tmp/jsm-deberta-eligible.json --evaluation-root .. --output /tmp/jsm-deberta-prepared.json
cmp /tmp/jsm-deberta-input.json /tmp/jsm-deberta-prepared.json
/tmp/jsm-deberta-replay/bin/python run_deberta.py --input /tmp/jsm-deberta-input.json --protocol protocol.json --cache /tmp/jsm-deberta-model --output /tmp/jsm-deberta-predictions.json
/tmp/jsm-deberta-replay/bin/python validate_deberta.py --snapshot /tmp/jsm-deberta-model/models--cross-encoder--nli-deberta-v3-small/snapshots/fa2804872c3b4bd748f38c0185cc85775361e735 --protocol protocol.json --input /tmp/jsm-deberta-input.json
```

Score the frozen predictions without GPU/model dependencies (NumPy and scikit-learn are needed by the existing rule evaluator). Replace the prediction path only when explicitly evaluating a new run; do not overwrite the recorded artifacts:

```sh
python score_experiment.py --evaluation-root .. --pool /tmp/jsm-deberta-eligible.json --predictions predictions.json --score-protocol score_protocol.json --output /tmp/jsm-deberta-replayed-metrics.json
python frontier.py --evaluation-root .. --predictions predictions.json --output /tmp/jsm-deberta-replayed-frontier.json
python -m unittest discover -s . -p 'test_*.py' -v
python ../test_evaluate.py
python ../test_leakage.py
```

Timing fields naturally vary. Semantic metrics must reproduce exactly from frozen scores; floating-point inference may vary slightly across hardware/software. The runner uses deterministic algorithms, FP32, fixed seed, eval mode, two NLI pairs per posting, and no gradients. Titles always survive the 512-token budget. The validation script checks every input and compares manual pair construction/logits with the tokenizer's standard pair API. A warning during full-body tokenization concerns the pre-truncation text, not an overlength model input.

Threshold equality means KEEP. Paired scores compare entailment logits and are not calibrated probabilities of job suitability. The separate absolute NLI gate requires negative entailment and little positive entailment. `frontier.py` finds the largest paired threshold satisfying each recall target simultaneously on original development, explicitly corrected development, and deduplicated reviewed references. It uses labels and is an optimistic development bound. It must not be promoted as an independently validated operating threshold.
