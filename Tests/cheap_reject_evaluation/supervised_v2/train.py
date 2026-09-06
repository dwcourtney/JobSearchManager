"""Nested employer-disjoint compact classifiers. Run only on curiosity GPU."""
import argparse
import gc
import hashlib
import json
import math
import os
import platform
import random
import resource
import re
import time
from pathlib import Path

os.environ['CUBLAS_WORKSPACE_CONFIG'] = ':4096:8'
os.environ['TOKENIZERS_PARALLELISM'] = 'false'
import numpy as np
import torch
import transformers
from huggingface_hub import snapshot_download
from transformers import AutoTokenizer, AutoModelForSequenceClassification, get_linear_schedule_with_warmup

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def encode(row, tokenizer, view, limit, tail=False):
    title=tokenizer.encode('Title: '+row['title'],add_special_tokens=False)
    budget=limit-tokenizer.num_special_tokens_to_add(pair=False)
    if len(title)>budget:
        return None, dict(titleOverflow=True)
    ids=list(title); body=[]; used=[]
    if view=='title_body':
        prefix=tokenizer.encode('\nBody: ',add_special_tokens=False)
        body=tokenizer.encode(row['body'],add_special_tokens=False)
        if len(title)+len(prefix)<=budget:
            remaining=budget-len(title)-len(prefix)
            used=(body[-remaining:] if tail else body[:remaining]) if remaining else []
            ids+=prefix+used
    ids=tokenizer.build_inputs_with_special_tokens(ids)
    assert len(ids)<=limit and ids[1:len(title)+1]==title
    return dict(input_ids=ids,attention_mask=[1]*len(ids)),dict(titleOverflow=False,tokens=len(ids),bodyTokens=len(body),truncated=len(used)<len(body))

def mask_company(row, companies):
    result=dict(row)
    for field in ['title','body']:
        text=result[field]
        for name in sorted({v for c in companies for v in [c,c.replace('-',' ')]},key=len,reverse=True):
            text=re.sub(re.escape(name),'the employer',text,flags=re.IGNORECASE)
        result[field]=text
    return result

def run(a):
    torch.set_num_threads(4);torch.use_deterministic_algorithms(True)
    assert torch.cuda.is_available()
    root=a.root;cfg=json.loads((root/'protocol.json').read_text());splits=json.loads((root/'splits.json').read_text())
    assert sha(root/'corpus.jsonl')==splits['corpusSha256'] and sha(root/'cache.jsonl')==splits['cacheSha256']
    rows=[json.loads(x) for x in (root/'corpus.jsonl').read_text().splitlines()]
    cache=[json.loads(x) for x in (root/'cache.jsonl').read_text().splitlines()]
    byid={r['id']:r for r in rows+cache};output=root/'output';output.mkdir(exist_ok=True)
    environment=dict(host=platform.node(),gpu=torch.cuda.get_device_name(),python=platform.python_version(),torch=torch.__version__,transformers=transformers.__version__,cuda=torch.version.cuda)
    for key,mc in cfg['models'].items():
        snapshot=Path(snapshot_download(mc['name'],revision=mc['revision'],cache_dir=str(a.cache),local_files_only=True))
        if 'weightSha256' in mc:assert sha(snapshot/'model.safetensors')==mc['weightSha256']
        tokenizer=AutoTokenizer.from_pretrained(snapshot,local_files_only=True,trust_remote_code=False)
        for view in cfg['views']:
            features={i:encode(r,tokenizer,view,cfg['maxLength']) for i,r in byid.items()}
            for regime in cfg['regimes']:
                for fold in splits['folds']:
                    tag=f"{key}-{view}-{regime}-fold{fold['fold']}"
                    if (output/(tag+'.json')).exists():print('SKIP complete',tag,flush=True);continue
                    print('START',tag,flush=True)
                    seed=cfg['seed']+fold['fold'];random.seed(seed);np.random.seed(seed);torch.manual_seed(seed)
                    parts=fold['partitions'];tr=[i for i in parts['train'] if regime!='description' or byid[i]['bodyAvailable']]
                    va=parts['validation'];ev=parts['evaluation']
                    assert all(features[i][0] is not None for i in tr+va)
                    model=AutoModelForSequenceClassification.from_pretrained(snapshot,local_files_only=True,trust_remote_code=False,use_safetensors=True,num_labels=2,id2label={0:'REJECT',1:'KEEP'},label2id={'REJECT':0,'KEEP':1},ignore_mismatched_sizes=True).to('cuda')
                    counts=np.bincount([int(byid[i]['label']=='KEEP') for i in tr],minlength=2)
                    weights={i:len(tr)/(2*counts[int(byid[i]['label']=='KEEP')])*(1 if regime!='weighted' or byid[i]['confidence']=='high' else .75 if byid[i]['bodyAvailable'] else .25) for i in tr}
                    mean=sum(weights.values())/len(tr);weights={i:v/mean for i,v in weights.items()}
                    opt=torch.optim.AdamW(model.parameters(),lr=mc['learningRate'],weight_decay=cfg['weightDecay'],foreach=False)
                    steps=math.ceil(math.ceil(len(tr)/cfg['microBatch'])/cfg['gradientAccumulation'])*mc['epochs']
                    schedule=get_linear_schedule_with_warmup(opt,int(steps*cfg['warmupFraction']),steps)
                    checkpoint=output/tag;best=float('inf');history=[]
                    def predict(ids,feats=features,batch=None,device='cuda'):
                        batch=batch or cfg['inferenceBatch'];result=[];model.eval()
                        with torch.inference_mode():
                            for start in range(0,len(ids),batch):
                                selected=ids[start:start+batch];active=[i for i in selected if feats[i][0] is not None];scores={i:1. for i in selected}
                                if active:
                                    tensors=tokenizer.pad([feats[i][0] for i in active],padding=True,return_tensors='pt').to(device)
                                    probs=model(**tensors).logits.softmax(-1)[:,1].cpu().tolist();scores.update(zip(active,probs))
                                result.extend(dict(id=i,keepScore=scores[i],failOpen=feats[i][0] is None) for i in selected)
                        return result
                    torch.cuda.reset_peak_memory_stats();started=time.perf_counter()
                    for epoch in range(mc['epochs']):
                        model.train();order=list(tr);random.Random(seed+epoch).shuffle(order);opt.zero_grad(set_to_none=True)
                        batches=[order[j:j+cfg['microBatch']] for j in range(0,len(order),cfg['microBatch'])]
                        for j,ids in enumerate(batches):
                            tensors=tokenizer.pad([features[i][0] for i in ids],padding=True,return_tensors='pt').to('cuda')
                            labels=torch.tensor([int(byid[i]['label']=='KEEP') for i in ids],device='cuda')
                            group=(j//cfg['gradientAccumulation'])*cfg['gradientAccumulation'];n=sum(len(b) for b in batches[group:group+cfg['gradientAccumulation']])
                            loss=(torch.nn.functional.cross_entropy(model(**tensors).logits,labels,reduction='none')*torch.tensor([weights[i] for i in ids],device='cuda')).sum()/n
                            loss.backward()
                            if (j+1)%cfg['gradientAccumulation']==0 or j+1==len(batches):
                                torch.nn.utils.clip_grad_norm_(model.parameters(),cfg['gradientClip']);opt.step();schedule.step();opt.zero_grad(set_to_none=True)
                        vp=predict(va);ce=float(np.mean([-math.log(max(1e-7,p['keepScore'] if byid[p['id']]['label']=='KEEP' else 1-p['keepScore'])) for p in vp]))
                        history.append(dict(epoch=epoch+1,validationCrossEntropy=ce))
                        if ce<best:best=ce;best_epoch=epoch+1;model.save_pretrained(checkpoint,safe_serialization=True);tokenizer.save_pretrained(checkpoint)
                        print(tag,'epoch',epoch+1,'validationCE',round(ce,5),flush=True)
                    torch.cuda.synchronize();training=dict(seconds=time.perf_counter()-started,n=len(tr),peakGpuAllocatedMiB=torch.cuda.max_memory_allocated()/2**20,peakGpuReservedMiB=torch.cuda.max_memory_reserved()/2**20)
                    del opt,schedule,model;gc.collect();torch.cuda.empty_cache()
                    model=AutoModelForSequenceClassification.from_pretrained(checkpoint,local_files_only=True,use_safetensors=True).to('cuda').eval()
                    ids=[r['id'] for r in rows+cache if splits['companyFold'][r['employer']]==fold['fold']]
                    predict(ids[:16]);torch.cuda.synchronize();torch.cuda.reset_peak_memory_stats();t=time.perf_counter()
                    fresh={i:encode(byid[i],tokenizer,view,cfg['maxLength']) for i in ids};pred=predict(ids,fresh);torch.cuda.synchronize();elapsed=time.perf_counter()-t
                    bench=sorted(ev,key=lambda i:hashlib.sha256(i.encode()).hexdigest())[:24];single=[]
                    for i in bench:
                        torch.cuda.synchronize();t=time.perf_counter();predict([i],{i:encode(byid[i],tokenizer,view,cfg['maxLength'])},1);torch.cuda.synchronize();single.append((time.perf_counter()-t)*1000)
                    memory=dict(peakGpuAllocatedMiB=torch.cuda.max_memory_allocated()/2**20,peakGpuReservedMiB=torch.cuda.max_memory_reserved()/2**20,processPeakRssMiB=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024)
                    diagnostics={}
                    for mode in ['company_masked','body_tail']:
                        ft={i:encode(mask_company(byid[i],splits['companyFold']) if mode=='company_masked' else byid[i],tokenizer,view,cfg['maxLength'],tail=mode=='body_tail') for i in ev}
                        diagnostics[mode]=predict(ev,ft)
                    vp=predict(va)
                    record=dict(tag=tag,model=key,view=view,regime=regime,fold=fold['fold'],environment=environment,scriptSha256=sha(Path(__file__)),protocolSha256=sha(root/'protocol.json'),splitSha256=sha(root/'splits.json'),baseRevision=mc['revision'],baseWeightSha256=sha(snapshot/'model.safetensors'),checkpointPath=str(checkpoint),checkpointSha256=sha(checkpoint/'model.safetensors'),parameters=sum(p.numel() for p in model.parameters()),selectedEpoch=best_epoch,history=history,training=training,validation=vp,predictions=pred,diagnostics=diagnostics,truncation=dict(bodyTruncated=sum(fresh[i][1].get('truncated',False) for i in ids),titleOverflows=sum(fresh[i][0] is None for i in ids)),gpuTiming=dict(totalSeconds=elapsed,n=len(ids),batch=cfg['inferenceBatch'],msPerPosting=elapsed*1000/len(ids),postingsPerSecond=len(ids)/elapsed,singleMedianMs=float(np.median(single)),singleP95Ms=float(np.percentile(single,95)),includesTokenization=True),inferenceMemory=memory)
                    (output/(tag+'.json')).write_text(json.dumps(record,indent=2)+'\n');print('DONE',tag,'trainSeconds',round(training['seconds'],1),flush=True)
                    del model;gc.collect();torch.cuda.empty_cache()
    print('ALL COMPLETE',flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--cache',type=Path,required=True);run(p.parse_args())

