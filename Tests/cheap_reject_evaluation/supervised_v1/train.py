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
        text=result[field].casefold()
        for name in sorted({v for c in companies for v in [c,c.replace('-',' ')]},key=len,reverse=True):
            text=text.replace(name,'the employer')
        result[field]=text
    return result

def run(a):
    torch.set_num_threads(4)
    torch.use_deterministic_algorithms(True)
    assert torch.cuda.is_available()
    cfg=json.loads((a.root/'protocol.json').read_text())
    splits=json.loads((a.root/'splits.json').read_text())
    assert sha(a.root/'corrected.jsonl')==splits['correctedSha256']
    rows=[json.loads(line) for line in (a.root/'corrected.jsonl').read_text().splitlines()]
    byid={r['id']:r for r in rows}
    pool=json.loads(a.pool.read_text())
    fixtures=[json.loads(line) for line in a.fixtures.read_text().splitlines()]
    a.output.mkdir(parents=True,exist_ok=True)
    summary=dict(protocol=cfg,scriptSha256=sha(Path(__file__)),splitSha256=sha(a.root/'splits.json'),
                 datasetSha256=sha(a.root/'corrected.jsonl'),poolSha256=sha(a.pool),
                 environment=dict(host=platform.node(),gpu=torch.cuda.get_device_name(),python=platform.python_version(),torch=torch.__version__,transformers=transformers.__version__,numpy=np.__version__,cuda=torch.version.cuda),runs=[])
    for key,modelcfg in cfg['models'].items():
        snapshot=Path(snapshot_download(modelcfg['name'],revision=modelcfg['revision'],cache_dir=str(a.cache),allow_patterns=['*.json','*.model','vocab.txt','model.safetensors','README.md']))
        weightsha=sha(snapshot/'model.safetensors')
        if 'weightSha256' in modelcfg:assert weightsha==modelcfg['weightSha256']
        tokenizer=AutoTokenizer.from_pretrained(snapshot,local_files_only=True,trust_remote_code=False)
        for view in cfg['views']:
            all_features={r['id']:encode(r,tokenizer,view,cfg['maxLength']) for r in pool+fixtures}
            for fold in splits['folds']:
                tag=f"{key}-{view}-fold{fold['fold']}"
                print('START',tag,flush=True)
                seed=cfg['seed']+fold['fold']
                random.seed(seed);np.random.seed(seed);torch.manual_seed(seed)
                part=fold['partitions']
                model=AutoModelForSequenceClassification.from_pretrained(snapshot,local_files_only=True,trust_remote_code=False,use_safetensors=True,
                    num_labels=2,id2label={0:'REJECT',1:'KEEP'},label2id={'REJECT':0,'KEEP':1},ignore_mismatched_sizes=True).to('cuda')
                assert all(all_features[i][0] is not None for i in part['train']+part['validation'])
                counts=np.bincount([int(byid[i]['label']=='KEEP') for i in part['train']],minlength=2)
                weights=torch.tensor(len(part['train'])/(2*counts),dtype=torch.float32,device='cuda')
                opt=torch.optim.AdamW(model.parameters(),lr=modelcfg['learningRate'],weight_decay=cfg['weightDecay'],foreach=False)
                steps=math.ceil(math.ceil(len(part['train'])/cfg['microBatch'])/cfg['gradientAccumulation'])*modelcfg['epochs']
                schedule=get_linear_schedule_with_warmup(opt,int(steps*cfg['warmupFraction']),steps)
                checkpoint=a.output/tag
                best=float('inf');history=[]
                torch.cuda.reset_peak_memory_stats();started=time.perf_counter()

                def predictions(ids, features=all_features, device='cuda', batch=None):
                    batch=batch or cfg['inferenceBatch']
                    output=[]
                    model.eval()
                    with torch.inference_mode():
                        for offset in range(0,len(ids),batch):
                            selected=ids[offset:offset+batch]
                            active=[i for i in selected if features[i][0] is not None]
                            scores={i:[0.,1.] for i in selected}
                            if active:
                                tensors=tokenizer.pad([features[i][0] for i in active],padding=True,return_tensors='pt').to(device)
                                probs=model(**tensors).logits.softmax(-1).cpu().tolist()
                                scores.update(dict(zip(active,probs)))
                            output.extend(dict(id=i,keepScore=scores[i][1],failOpen=features[i][0] is None) for i in selected)
                    return output

                for epoch in range(modelcfg['epochs']):
                    model.train();order=list(part['train']);random.Random(seed+epoch).shuffle(order)
                    opt.zero_grad(set_to_none=True)
                    batches=[order[j:j+cfg['microBatch']] for j in range(0,len(order),cfg['microBatch'])]
                    for j,ids in enumerate(batches):
                        tensors=tokenizer.pad([all_features[i][0] for i in ids],padding=True,return_tensors='pt').to('cuda')
                        labels=torch.tensor([int(byid[i]['label']=='KEEP') for i in ids],device='cuda')
                        group_start=(j//cfg['gradientAccumulation'])*cfg['gradientAccumulation']
                        group_size=min(cfg['gradientAccumulation'],len(batches)-group_start)
                        loss=torch.nn.functional.cross_entropy(model(**tensors).logits,labels,weight=weights)/group_size
                        loss.backward()
                        if (j+1)%cfg['gradientAccumulation']==0 or j+1==len(batches):
                            torch.nn.utils.clip_grad_norm_(model.parameters(),cfg['gradientClip'])
                            opt.step();schedule.step();opt.zero_grad(set_to_none=True)
                    valpred=predictions(part['validation'])
                    valce=float(np.mean([-math.log(max(1e-7,p['keepScore'] if byid[p['id']]['label']=='KEEP' else 1-p['keepScore'])) for p in valpred]))
                    history.append(dict(epoch=epoch+1,validationCrossEntropy=valce))
                    if valce<best:
                        best=valce;best_epoch=epoch+1
                        model.save_pretrained(checkpoint,safe_serialization=True)
                        tokenizer.save_pretrained(checkpoint)
                    print(tag,'epoch',epoch+1,'validationCE',round(valce,5),flush=True)
                torch.cuda.synchronize()
                training=dict(seconds=time.perf_counter()-started,peakGpuAllocatedMiB=torch.cuda.max_memory_allocated()/2**20,peakGpuReservedMiB=torch.cuda.max_memory_reserved()/2**20)
                del opt,schedule,model;gc.collect();torch.cuda.empty_cache()
                model=AutoModelForSequenceClassification.from_pretrained(checkpoint,local_files_only=True,use_safetensors=True).to('cuda').eval()
                ids=[r['id'] for r in pool if splits['companyFold'].get(r['company'])==fold['fold']]
                assert set(part['evaluation'])<=set(ids)
                predictions(ids[:16]);torch.cuda.synchronize();torch.cuda.reset_peak_memory_stats()
                # End-to-end measurement includes fresh encoding, not cached features.
                started=time.perf_counter()
                fresh={i:encode(next(r for r in pool if r['id']==i),tokenizer,view,cfg['maxLength']) for i in ids}
                cachepred=predictions(ids,fresh);torch.cuda.synchronize();elapsed=time.perf_counter()-started
                benchrows=sorted([r for r in pool if r['id'] in set(ids)],key=lambda r:hashlib.sha256(('bench|'+r['id']).encode()).hexdigest())[:20]
                single=[]
                for r in benchrows:
                    torch.cuda.synchronize();t=time.perf_counter()
                    predictions([r['id']],{r['id']:encode(r,tokenizer,view,cfg['maxLength'])},batch=1)
                    torch.cuda.synchronize();single.append((time.perf_counter()-t)*1000)
                infermem=dict(peakGpuAllocatedMiB=torch.cuda.max_memory_allocated()/2**20,peakGpuReservedMiB=torch.cuda.max_memory_reserved()/2**20,processPeakRssMiB=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024)
                diagnostics={}
                for mode in ['company_masked','body_tail']:
                    features={i:encode(mask_company(byid[i],splits['companyFold']) if mode=='company_masked' else byid[i],tokenizer,view,cfg['maxLength'],tail=mode=='body_tail') for i in part['evaluation']}
                    diagnostics[mode]=predictions(part['evaluation'],features)
                valpred=predictions(part['validation'])
                synthetic=predictions([r['id'] for r in fixtures])
                cpu=None
                if fold['fold']==2:
                    model.to('cpu');torch.cuda.empty_cache()
                    predictions([benchrows[0]['id']],device='cpu',batch=1)
                    times=[]
                    for r in benchrows:
                        t=time.perf_counter();predictions([r['id']],{r['id']:encode(r,tokenizer,view,cfg['maxLength'])},device='cpu',batch=1);times.append((time.perf_counter()-t)*1000)
                    cpu=dict(singleMedianMs=float(np.median(times)),singleP95Ms=float(np.percentile(times,95)),sequentialPerSecond=len(times)/(sum(times)/1000),sample=len(times),threads=4,processPeakRssMiB=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024)
                record=dict(tag=tag,model=key,view=view,fold=fold['fold'],baseRevision=modelcfg['revision'],baseWeightSha256=weightsha,
                    checkpointPath=str(checkpoint),checkpointSha256=sha(checkpoint/'model.safetensors'),parameters=sum(p.numel() for p in model.parameters()),
                    selectedEpoch=best_epoch,history=history,training=training,validation=valpred,cache=cachepred,synthetic=synthetic,diagnostics=diagnostics,
                    truncation=dict(cacheN=len(ids),bodyTruncated=sum(fresh[i][1].get('truncated',False) for i in ids),titleOverflows=sum(fresh[i][0] is None for i in ids)),
                    gpuTiming=dict(totalSeconds=elapsed,n=len(ids),batch=cfg['inferenceBatch'],msPerPosting=elapsed*1000/len(ids),postingsPerSecond=len(ids)/elapsed,singleMedianMs=float(np.median(single)),singleP95Ms=float(np.percentile(single,95)),includesTokenization=True),inferenceMemory=infermem,cpuTiming=cpu)
                (a.output/(tag+'.json')).write_text(json.dumps(record,indent=2)+'\n')
                summary['runs'].append(record)
                (a.output/'predictions.json').write_text(json.dumps(summary,indent=2)+'\n')
                print('DONE',tag,'bestEpoch',best_epoch,'trainSeconds',round(training['seconds'],1),flush=True)
                del model;gc.collect();torch.cuda.empty_cache()
    print('ALL COMPLETE',flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    for name in ['root','pool','fixtures','output','cache']:p.add_argument('--'+name,type=Path,required=True)
    run(p.parse_args())
