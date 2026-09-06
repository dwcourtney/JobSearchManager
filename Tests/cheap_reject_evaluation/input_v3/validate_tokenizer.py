"""Prove reused prefix baseline inputs match v2 on every frozen posting."""
import importlib.util
import json
from pathlib import Path
from huggingface_hub import snapshot_download
from transformers import AutoTokenizer
from inputs import encode

root=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('prior_train','/tmp/jsm-supervised-corpus-v2/train.py')
prior=importlib.util.module_from_spec(spec);spec.loader.exec_module(prior)
cfg=json.loads((root/'protocol.json').read_text());m=cfg['models']['deberta-small']
snapshot=snapshot_download(m['name'],revision=m['revision'],cache_dir='/tmp/jsm-deberta.dxVPsh/model-cache',local_files_only=True)
tok=AutoTokenizer.from_pretrained(snapshot,local_files_only=True)
n=0
for name in ['corpus.jsonl','cache.jsonl']:
    for r in map(json.loads,(root/name).read_text().splitlines()):
        expected=prior.encode(r,tok,'title_body',256)[0]
        actual,info=encode(r,tok,'prefix256')
        assert actual==expected,r['id']
        assert not info['titleOverflow']
        n+=1
(root/'tokenizer-validation.json').write_text(json.dumps(dict(postings=n,prefix256ExactParity=True,fullTitlePreserved=True),indent=2)+'\n')
print('EXACT BASELINE TOKEN PARITY',n,flush=True)
