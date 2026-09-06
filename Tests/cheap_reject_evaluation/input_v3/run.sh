#!/bin/sh
set -eu
cd /tmp/jsm-input-v3
PY=/tmp/jsm-deberta.dxVPsh/venv/bin/python
export PYTHONDONTWRITEBYTECODE=1
export HF_HUB_OFFLINE=1
export TRANSFORMERS_OFFLINE=1
$PY smoke.py
$PY audit.py --root . --cache /tmp/jsm-deberta.dxVPsh/model-cache
$PY train.py --root . --cache /tmp/jsm-deberta.dxVPsh/model-cache
$PY score.py --root .
for view in prefix256 headtail256 headmidtail256 prefix384 prefix512 sections256; do
 $PY benchmark.py --root . --tag deberta-small-$view-all-fold2
done
$PY archive.py
printf 'PIPELINE COMPLETE\n'
