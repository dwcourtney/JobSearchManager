"""Read-only public-posting cache inventory. Never print posting/holdout text."""
import argparse
import collections
import hashlib
import json
from pathlib import Path

def inventory(paths):
    report=[]
    for path in sorted(set(paths)):
        try:
            raw=path.read_bytes(); document=json.loads(raw)
            jobs=document.get('jobs') if isinstance(document,dict) else None
            sources=document.get('sources') if isinstance(document,dict) else None
            if isinstance(jobs,list):
                hydrated=[j for j in jobs if j.get('descriptionHtml') or j.get('compressedDescriptionHtml')]
                report.append(dict(path=str(path),sha256=hashlib.sha256(raw).hexdigest(),bytes=len(raw),kind='job-cache',records=len(jobs),withBody=len(hydrated),
                                   companies=dict(collections.Counter(j.get('companyId','unknown') for j in jobs))))
            elif isinstance(sources,dict):
                report.append(dict(path=str(path),sha256=hashlib.sha256(raw).hexdigest(),bytes=len(raw),kind='source-corpus',records=len(sources),
                                   withBody=sum(bool(s.get('fullPosting')) for s in sources.values()),companies=dict(collections.Counter(s.get('companyId','unknown') for s in sources.values()))))
        except (OSError,ValueError) as error:
            report.append(dict(path=str(path),error=type(error).__name__))
    return report

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--root',type=Path,action='append',default=[])
    p.add_argument('--file',type=Path,action='append',default=[])
    p.add_argument('--output',type=Path,required=True)
    a=p.parse_args()
    paths=list(a.file)
    for root in a.root:
        paths.extend(p for p in root.rglob('*.json') if 'job-caches' in p.parts)
    report=inventory(paths)
    a.output.write_bytes((json.dumps(report,indent=2)+'\n').encode())
    print(json.dumps(dict(files=len(report),records=sum(r.get('records',0) for r in report),withBody=sum(r.get('withBody',0) for r in report))))
    for r in report:print(r.get('records',0),r.get('withBody',0),r['path'])
