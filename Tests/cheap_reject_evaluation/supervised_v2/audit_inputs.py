"""Token-budget/evidence audit using frozen tokenizers only; no model inference."""
import argparse,json
from pathlib import Path
from huggingface_hub import snapshot_download
from transformers import AutoTokenizer
def main(root,cache):
    cfg=json.loads((root/'protocol.json').read_text());rows=[json.loads(x) for x in (root/'corpus.jsonl').read_text().splitlines()];result={}
    for key,m in cfg['models'].items():
        path=snapshot_download(m['name'],revision=m['revision'],cache_dir=str(cache),local_files_only=True)
        tok=AutoTokenizer.from_pretrained(path,local_files_only=True,trust_remote_code=False);records=[]
        for r in rows:
            if not r['bodyAvailable']:continue
            title=tok.encode('Title: '+r['title'],add_special_tokens=False);prefix=tok.encode('\nBody: ',add_special_tokens=False)
            remaining=max(0,cfg['maxLength']-tok.num_special_tokens_to_add(pair=False)-len(title)-len(prefix))
            offsets=tok(r['body'],add_special_tokens=False,return_offsets_mapping=True)['offset_mapping']
            cutoff=offsets[min(remaining,len(offsets))-1][1] if remaining and offsets else 0
            spans=r['reviewedEvidence'];overlap=sum(max(0,min(cutoff,s['end'])-s['start']) for s in spans)
            records.append(dict(id=r['id'],label=r['label'],cutoffCharacter=cutoff,bodyCharacters=len(r['body']),truncated=len(offsets)>remaining,reviewedSpanCharacters=sum(s['end']-s['start'] for s in spans),reviewedSpanOverlapCharacters=overlap,allReviewedEvidenceOutside=bool(spans) and overlap==0))
        binary=[r for r in records if r['label']!='AMBIGUOUS']
        result[key]=dict(describedBinary=len(binary),truncated=sum(r['truncated'] for r in binary),allReviewedEvidenceOutside=sum(r['allReviewedEvidenceOutside'] for r in binary),records=records)
    (root/'input-audit.json').write_text(json.dumps(result,indent=2)+'\n');print({k:{f:v for f,v in x.items() if f!='records'} for k,x in result.items()})
if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--cache',type=Path,required=True);a=p.parse_args();main(a.root,a.cache)
