"""Fresh-process CPU/GPU inference measurements, one fold-2 checkpoint per candidate."""
import argparse, hashlib, json, resource, statistics, time
from pathlib import Path
import torch
from transformers import AutoModelForSequenceClassification, AutoTokenizer
from train import encode

def main(root,tag):
    torch.set_num_threads(4);run=json.loads((root/'output'/(tag+'.json')).read_text());cfg=json.loads((root/'protocol.json').read_text())
    rows={r['id']:r for r in map(json.loads,(root/'corpus.jsonl').read_text().splitlines())};splits=json.loads((root/'splits.json').read_text());ids=splits['folds'][2]['partitions']['evaluation']
    sample=sorted(ids,key=lambda i:hashlib.sha256(('bench2'+i).encode()).hexdigest())[:40]
    tok=AutoTokenizer.from_pretrained(run['checkpointPath'],local_files_only=True)
    model=AutoModelForSequenceClassification.from_pretrained(run['checkpointPath'],local_files_only=True,use_safetensors=True).eval().to('cuda')
    assert model.config.id2label=={0:'REJECT',1:'KEEP'}
    assert encode(dict(title='word '*1000,body=''),tok,run['view'],cfg['maxLength'])[0] is None
    assert encode(dict(title='Software Engineer',body='Build and test APIs.'),tok,run['view'],cfg['maxLength'])[0] is not None
    def infer(ids,device):
        features=[encode(rows[i],tok,run['view'],cfg['maxLength'])[0] for i in ids]
        tensors=tok.pad(features,padding=True,return_tensors='pt').to(device)
        with torch.inference_mode():return model(**tensors).logits.softmax(-1)[:,1].cpu().tolist()
    infer(sample[:16],'cuda');torch.cuda.synchronize();torch.cuda.reset_peak_memory_stats();start=time.perf_counter()
    for j in range(0,len(sample),16):infer(sample[j:j+16],'cuda')
    torch.cuda.synchronize();elapsed=time.perf_counter()-start;times=[]
    for i in sample:
        torch.cuda.synchronize();start=time.perf_counter();infer([i],'cuda');torch.cuda.synchronize();times.append(1000*(time.perf_counter()-start))
    result=dict(tag=tag,n=len(sample),gpuBatchMsPerPosting=1000*elapsed/len(sample),gpuThroughput=len(sample)/elapsed,gpuSingleMedianMs=statistics.median(times),gpuPeakAllocatedMiB=torch.cuda.max_memory_allocated()/2**20,gpuPeakReservedMiB=torch.cuda.max_memory_reserved()/2**20,processPeakRssMiB=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024,includesTokenization=True,cpuThreads=4)
    model.to('cpu');torch.cuda.empty_cache();infer(sample[:1],'cpu');times=[]
    for i in sample:
        start=time.perf_counter();infer([i],'cpu');times.append(1000*(time.perf_counter()-start))
    result.update(cpuSingleMedianMs=statistics.median(times),cpuThroughput=1000*len(times)/sum(times),processPeakRssAfterCpuMiB=resource.getrusage(resource.RUSAGE_SELF).ru_maxrss/1024)
    (root/'output'/(tag+'-benchmark.json')).write_text(json.dumps(result,indent=2)+'\n');print(tag,'benchmark PASS',flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--tag',required=True);a=p.parse_args();main(a.root,a.tag)
