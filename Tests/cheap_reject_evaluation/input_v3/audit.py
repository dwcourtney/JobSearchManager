"""Gold evidence is inspected only here, after deterministic inputs are built."""
import argparse
import json
from pathlib import Path
from huggingface_hub import snapshot_download
from transformers import AutoTokenizer
from inputs import encode, merge_ranges


def summarize(records):
    def rate(field):
        return sum(r[field] for r in records)/len(records) if records else None
    return dict(n=len(records), anyEvidence=rate('anyEvidence'),
                halfEvidence=rate('halfEvidence'), fullEvidence=rate('fullEvidence'),
                meanFraction=rate('fraction'), truncated=rate('truncated'))


def main(root, cache):
    cfg=json.loads((root/'protocol.json').read_text())
    rows=[json.loads(x) for x in (root/'corpus.jsonl').read_text().splitlines()]
    model=cfg['models']['deberta-small']
    snapshot=snapshot_download(model['name'], revision=model['revision'], cache_dir=str(cache), local_files_only=True)
    tok=AutoTokenizer.from_pretrained(snapshot, local_files_only=True)
    result={}
    for view in cfg['views']:
        records=[]
        for r in rows:
            if not r['bodyAvailable']:
                continue
            _, info=encode(r,tok,view)
            gold=merge_ranges([(s['start'],s['end']) for s in r['reviewedEvidence']])
            total=sum(e-s for s,e in gold)
            overlap=sum(max(0,min(b,d)-max(a,c)) for a,b in gold for c,d in info['sourceRanges'])
            assert 0<=overlap<=total
            fraction=overlap/total if total else 0
            records.append(dict(id=r['id'],label=r['label'],confidence=r['confidence'],
                                sourceRanges=info['sourceRanges'],tokens=info['tokens'],
                                evidenceCharacters=total,overlapCharacters=overlap,
                                anyEvidence=overlap>0,halfEvidence=fraction>=.5,
                                fullEvidence=fraction==1,fraction=fraction,truncated=info['truncated']))
        binary=[r for r in records if r['label']!='AMBIGUOUS']
        result[view]=dict(binary=summarize(binary),allDescribed=summarize(records),
                          byLabel={label:summarize([r for r in records if r['label']==label]) for label in ['KEEP','REJECT','AMBIGUOUS']},records=records)
        print(view,result[view]['binary'],flush=True)
    (root/'evidence-coverage.json').write_text(json.dumps(result,indent=2)+'\n')


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--cache',type=Path,required=True)
    a=p.parse_args();main(a.root,a.cache)
