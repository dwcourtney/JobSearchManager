"""Build identity-only exclusion digests. No holdout text or labels are exported."""
import argparse
import hashlib
import html
import json
import re
from pathlib import Path

def normalize(text):
    return re.sub(r'\s+',' ',html.unescape(re.sub(r'<[^>]+>',' ',text or ''))).strip().lower()

def digest(text):return hashlib.sha256(text.encode()).hexdigest()

def make(paths):
    ids=set();titles=set();contents=set();requisitions=set();urls=set();bodies=set();audits=[]
    for path in paths:
        raw=path.read_bytes()
        if path.suffix=='.jsonl':rows=[json.loads(line) for line in raw.decode('utf-8-sig').splitlines()]
        else:rows=json.loads(raw).get('examples',[])
        if not rows:raise ValueError('No exclusion examples in '+str(path))
        audits.append(dict(path=str(path),sha256=hashlib.sha256(raw).hexdigest(),records=len(rows)))
        for row in rows:
            if row.get('title'):titles.add(digest(normalize(row['title'])))
            for key in ['id','stableId']:
                if row.get(key):ids.add(digest(str(row[key])))
            for key in ['postingContentHash','contentSha256']:
                if row.get(key):contents.add(row[key])
            company=row.get('companyId',row.get('company',''))
            if company and row.get('requisitionId'):requisitions.add(digest(company+'\n'+str(row['requisitionId'])))
            if row.get('sourceUrl'):urls.add(digest(row['sourceUrl']))
            body=row.get('descriptionHtml',row.get('body',''))
            if body:bodies.add(digest(normalize(body)))
    return dict(schemaVersion=1,identityDigests=sorted(ids),titleDigests=sorted(titles),contentDigests=sorted(contents),requisitionDigests=sorted(requisitions),urlDigests=sorted(urls),bodyDigests=sorted(bodies),sources=audits)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--manifest',type=Path,action='append',required=True);p.add_argument('--output',type=Path,required=True)
    a=p.parse_args();result=make(a.manifest);a.output.write_bytes((json.dumps(result,indent=2)+'\n').encode())
    print(json.dumps({k:len(v) for k,v in result.items() if isinstance(v,list)}))
