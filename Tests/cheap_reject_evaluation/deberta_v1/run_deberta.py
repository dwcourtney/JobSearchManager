"""Pinned encoder-only inference on curiosity. Input contains no review labels."""
from __future__ import annotations
import argparse
import hashlib
import json
import os
import platform
import resource
import statistics
import subprocess
import time
from pathlib import Path


def body_slice(tokens, budget, view):
    if budget <= 0:
        return []
    if len(tokens) <= budget or view == 'title_body_head':
        return tokens[:budget]
    if view != 'title_body_head_tail':
        raise ValueError(view)
    head = (budget + 1) // 2
    return tokens[:head] + tokens[-(budget-head):] if budget > head else tokens[:head]


def make_pairs(row, view, tokenizer, hypotheses, max_length):
    title = tokenizer.encode('Title: ' + row['title'], add_special_tokens=False)
    reserved = max(map(len, hypotheses)) + tokenizer.num_special_tokens_to_add(pair=True)
    budget = max_length - reserved
    if len(title) > budget:
        return None, dict(titleTokens=len(title), titleOverflow=True)
    premise = list(title)
    body = []
    used = []
    if view != 'title':
        prefix = tokenizer.encode('\nBody: ', add_special_tokens=False)
        body = tokenizer.encode(row['body'], add_special_tokens=False)
        if budget - len(title) >= len(prefix):
            used = body_slice(body, budget-len(title)-len(prefix), view)
            premise += prefix + used
    pairs = []
    for hypothesis in hypotheses:
        ids = tokenizer.build_inputs_with_special_tokens(premise, hypothesis)
        assert len(ids) <= max_length
        pairs.append(dict(input_ids=ids, attention_mask=[1]*len(ids)))
    return pairs, dict(titleTokens=len(title), titleOverflow=False,
                      bodyTokens=len(body), bodyTokensUsed=len(used),
                      truncated=len(used)<len(body), pairTokens=max(len(p['input_ids']) for p in pairs))


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--input', type=Path, required=True)
    p.add_argument('--protocol', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--cache', type=Path, required=True)
    args = p.parse_args()
    os.environ['CUBLAS_WORKSPACE_CONFIG'] = ':4096:8'
    os.environ['TOKENIZERS_PARALLELISM'] = 'false'
    import numpy as np
    import torch
    import transformers
    from huggingface_hub import snapshot_download
    from transformers import AutoTokenizer, AutoModelForSequenceClassification
    cfg = json.loads(args.protocol.read_text())
    rows = json.loads(args.input.read_text())
    assert len({r['id'] for r in rows}) == len(rows)
    assert all(set(r) == {'id','title','body'} for r in rows)
    torch.manual_seed(20260905)
    torch.set_num_threads(4)
    torch.use_deterministic_algorithms(True)
    assert torch.cuda.is_available(), 'GPU required; do not silently switch environments'
    start = time.perf_counter()
    snapshot = Path(snapshot_download(cfg['model'], revision=cfg['revision'], cache_dir=str(args.cache),
                                    allow_patterns=['*.json','*.model','model.safetensors','README.md']))
    weights = snapshot/'model.safetensors'
    weight_sha = hashlib.sha256(weights.read_bytes()).hexdigest()
    assert weight_sha == cfg['expected_weight_sha256'], 'Unexpected checkpoint weights'
    tokenizer = AutoTokenizer.from_pretrained(snapshot, local_files_only=True, trust_remote_code=False)
    model = AutoModelForSequenceClassification.from_pretrained(snapshot, local_files_only=True,
                trust_remote_code=False, use_safetensors=True, torch_dtype=torch.float32).eval().to('cuda')
    assert model.config.id2label == {0:'contradiction',1:'entailment',2:'neutral'}
    assert cfg['max_length'] <= model.config.max_position_embeddings
    hypotheses = [tokenizer.encode(h,add_special_tokens=False) for h in cfg['hypotheses']]
    startup = time.perf_counter()-start
    env = dict(host=platform.node(), python=platform.python_version(), torch=torch.__version__,
               transformers=transformers.__version__, numpy=np.__version__, gpu=torch.cuda.get_device_name(),
               capability=torch.cuda.get_device_capability(), cuda=torch.version.cuda, dtype=str(next(model.parameters()).dtype),
               parameters=sum(p.numel() for p in model.parameters()), startupAndDownloadSeconds=startup,
               nvidiaSmi=subprocess.check_output(['nvidia-smi','--query-gpu=name,driver_version,memory.total','--format=csv,noheader'],text=True).strip())
    result = dict(protocol=cfg, environment=env, modelWeightSha256=weight_sha,
                  inputSha256=hashlib.sha256(args.input.read_bytes()).hexdigest(),
                  scriptSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), views={})
    benchmark_rows = sorted(rows,key=lambda r:hashlib.sha256(('timing-v1|'+r['id']).encode()).hexdigest())[:40]

    def predict(batch, view):
        features=[]; meta=[]; active=[]
        for i,row in enumerate(batch):
            pairs, info = make_pairs(row,view,tokenizer,hypotheses,cfg['max_length'])
            meta.append(info)
            if pairs is not None:
                features.extend(pairs);active.append(i)
        predictions=[None]*len(batch)
        if features:
            tensors=tokenizer.pad(features,padding=True,return_tensors='pt').to('cuda')
            with torch.inference_mode():
                logits=model(**tensors).logits.reshape(-1,2,3)
                absolute=logits.softmax(dim=-1).cpu().tolist()
                paired=logits[:,:,1].softmax(dim=-1)[:,0].cpu().tolist()
            for i,prob,score in zip(active,absolute,paired):
                predictions[i]=dict(keepScore=score,keepNli=prob[0],rejectNli=prob[1],failOpen=False)
        for i,prediction in enumerate(predictions):
            if prediction is None:predictions[i]=dict(keepScore=1.0,keepNli=[0.,1.,0.],rejectNli=[1.,0.,0.],failOpen=True)
        return [dict(id=row['id'],**prediction,**info) for row,prediction,info in zip(batch,predictions,meta)]

    for view in cfg['views']:
        print('START',view,flush=True)
        predict(rows[:cfg['batch_postings']],view)
        torch.cuda.synchronize();torch.cuda.reset_peak_memory_stats()
        started=time.perf_counter();predictions=[]
        for offset in range(0,len(rows),cfg['batch_postings']):
            predictions.extend(predict(rows[offset:offset+cfg['batch_postings']],view))
            if offset % 200 == 0:print(view,offset,'/',len(rows),flush=True)
        torch.cuda.synchronize();seconds=time.perf_counter()-started
        single=[]
        for row in benchmark_rows:
            torch.cuda.synchronize();started=time.perf_counter();predict([row],view);torch.cuda.synchronize()
            single.append((time.perf_counter()-started)*1000)
        result['views'][view]=dict(predictions=predictions,
            timing=dict(batchPostings=cfg['batch_postings'],hypothesesPerPosting=2,
                        totalSeconds=seconds,amortizedMsPerPosting=seconds*1000/len(rows),postingsPerSecond=len(rows)/seconds,
                        singleMedianMs=statistics.median(single),singleP95Ms=float(np.percentile(single,95)),singleSample=len(single),
                        includesTokenizationAndTransfers=True,coldStartExcluded=True),
            memory=dict(peakCudaAllocatedMiB=torch.cuda.max_memory_allocated()/2**20,
                        peakCudaReservedMiB=torch.cuda.max_memory_reserved()/2**20,
                        peakProcessRssMiB=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024),
            truncation=dict(postingsTruncated=sum(r.get('truncated',False) for r in predictions),
                            titleOverflows=sum(r['titleOverflow'] for r in predictions)))
        args.output.write_text(json.dumps(result,indent=2)+'\n')
        print('DONE',view,result['views'][view]['timing'],flush=True)
    print('OUTPUT',args.output,flush=True)


if __name__ == '__main__':
    main()
