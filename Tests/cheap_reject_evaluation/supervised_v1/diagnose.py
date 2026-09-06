"""Post-training, label-free perturbations and clean-process inference benchmark."""
import argparse
import hashlib
import json
import resource
import time
from pathlib import Path
import numpy as np
import torch
from transformers import AutoTokenizer, AutoModelForSequenceClassification
from train import encode

def preserve_case_mask(text, companies):
    # Literal metadata names only, not occupational matching or new rules.
    names=sorted({v for c in companies for v in [c,c.replace('-',' ')]},key=len,reverse=True)
    for name in names:
        start=0
        while True:
            index=text.casefold().find(name,start)
            if index<0:break
            text=text[:index]+'the employer'+text[index+len(name):]
            start=index+len('the employer')
    return text

def main(a):
    torch.set_num_threads(4)
    raw=json.loads((a.root/'output/predictions.json').read_text())
    split=json.loads((a.root/'splits.json').read_text())
    rows={r['id']:r for r in map(json.loads,(a.root/'corrected.jsonl').read_text().splitlines())}
    run=next(r for r in raw['runs'] if r['tag']==a.tag)
    tokenizer=AutoTokenizer.from_pretrained(run['checkpointPath'],local_files_only=True)
    model=AutoModelForSequenceClassification.from_pretrained(run['checkpointPath'],local_files_only=True,use_safetensors=True).eval().to('cuda')
    assert model.config.id2label=={0:'REJECT',1:'KEEP'}
    assert encode(dict(title='word '*1000,body=''),tokenizer,run['view'],384)[0] is None
    assert preserve_case_mask('Keep THIS software title', ['nvidia'])=='Keep THIS software title'
    ids=split['folds'][run['fold']]['partitions']['evaluation']
    def infer(batch, device='cuda'):
        features=[encode(r,tokenizer,run['view'],raw['protocol']['maxLength'])[0] for r in batch]
        assert all(f is not None for f in features)
        tensors=tokenizer.pad(features,padding=True,return_tensors='pt').to(device)
        with torch.inference_mode():
            return model(**tensors).logits.softmax(-1)[:,1].cpu().tolist()
    output=dict(tag=a.tag,checkpointSha256=run['checkpointSha256'],predictions={})
    for mode in ['original','company_mask_preserve_case','casefold_control']:
        selected=[]
        for i in ids:
            row=dict(rows[i])
            for field in ['title','body']:
                if mode=='company_mask_preserve_case':row[field]=preserve_case_mask(row[field],split['companyFold'])
                elif mode=='casefold_control':row[field]=row[field].casefold()
            selected.append(row)
        scores=[]
        for start in range(0,len(selected),16):scores.extend(infer(selected[start:start+16]))
        output['predictions'][mode]=[dict(id=i,keepScore=p) for i,p in zip(ids,scores)]
    # Measure one checkpoint per candidate in a clean process with no optimizer.
    if run['fold']==2:
        sample=sorted([rows[i] for i in ids],key=lambda r:hashlib.sha256(('clean-bench|'+r['id']).encode()).hexdigest())[:40]
        infer(sample[:16]);torch.cuda.synchronize();torch.cuda.reset_peak_memory_stats()
        start=time.perf_counter()
        for j in range(0,len(sample),16):infer(sample[j:j+16])
        torch.cuda.synchronize();elapsed=time.perf_counter()-start
        single=[]
        for row in sample:
            torch.cuda.synchronize();start=time.perf_counter();infer([row]);torch.cuda.synchronize();single.append((time.perf_counter()-start)*1000)
        output['benchmark']=dict(n=len(sample),batch=16,gpuBatchMsPerPosting=elapsed*1000/len(sample),gpuThroughput=len(sample)/elapsed,
                                 gpuSingleMedianMs=float(np.median(single)),gpuSingleP95Ms=float(np.percentile(single,95)),
                                 peakGpuAllocatedMiB=torch.cuda.max_memory_allocated()/2**20,peakGpuReservedMiB=torch.cuda.max_memory_reserved()/2**20,
                                 processRssHighWaterMiB=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024)
        model.to('cpu');torch.cuda.empty_cache();infer(sample[:1],device='cpu')
        single=[]
        for row in sample:
            start=time.perf_counter();infer([row],device='cpu');single.append((time.perf_counter()-start)*1000)
        output['benchmark'].update(cpuSingleMedianMs=float(np.median(single)),cpuSingleP95Ms=float(np.percentile(single,95)),cpuSequentialPerSecond=len(sample)/(sum(single)/1000),cpuThreads=4,
                                   processRssHighWaterAfterCpuMiB=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024)
    (a.root/'output'/(a.tag+'-diagnostics.json')).write_text(json.dumps(output,indent=2)+'\n')
    print(a.tag,'diagnostics PASS',flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--tag',required=True);main(p.parse_args())
