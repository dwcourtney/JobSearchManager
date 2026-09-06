"""Allowlisted public-posting extraction; exclude evaluation identities first."""
import argparse
import base64
import collections
import gzip
import hashlib
import json
from pathlib import Path
from make_exclusions import normalize,digest

def export(inventories, exclusion_paths):
    exclusions=[json.loads(p.read_text()) for p in exclusion_paths]
    title_blocks={x for e in exclusions for x in e['titleDigests']}
    id_blocks={x for e in exclusions for x in e['identityDigests']}
    content_blocks={x for e in exclusions for x in e['contentDigests']}
    requisition_blocks={x for e in exclusions for x in e['requisitionDigests']}
    url_blocks={x for e in exclusions for x in e['urlDigests']}
    body_blocks={x for e in exclusions for x in e['bodyDigests']}
    paths={p['path']:p for inventory in inventories for p in json.loads(inventory.read_text()) if p.get('kind')=='job-cache'}
    out=[];counts=collections.Counter();files=[]
    for value,info in sorted(paths.items()):
        path=Path(value);raw=path.read_bytes()
        assert hashlib.sha256(raw).hexdigest()==info['sha256'],'Cache changed since inventory: '+str(path)
        doc=json.loads(raw)
        files.append(dict(path=str(path),sha256=info['sha256'],savedAtUtc=doc.get('savedAtUtc'),records=len(doc['jobs'])))
        for job in doc['jobs']:
            counts['inputRecords']+=1
            title=job.get('title','').strip();stable=str(job.get('stableId',''))
            req_digest=digest(job.get('companyId','unknown')+'\n'+str(job.get('requisitionId','')))
            if digest(normalize(title)) in title_blocks or (stable and digest(stable) in id_blocks) or job.get('postingContentHash') in content_blocks or req_digest in requisition_blocks or digest(job.get('sourceUrl','')) in url_blocks:
                counts['excludedByIdentityOrTitle']+=1;continue
            body=job.get('descriptionHtml','')
            if not body and job.get('compressedDescriptionHtml'):
                body=gzip.decompress(base64.b64decode(job['compressedDescriptionHtml'])).decode('utf-8')
            body=normalize(body)
            content=digest(normalize(title)+'\n'+body)
            if content in content_blocks or digest(content[:16]) in id_blocks or (body and digest(body) in body_blocks):
                counts['excludedByContent']+=1;continue
            if not title:
                counts['missingTitle']+=1;continue
            employer=job.get('companyId','unknown')
            source=job.get('sourceUrl','')
            requisition=job.get('requisitionId','')
            identity=digest(employer+'\n'+(str(requisition) or stable or source or normalize(title)+'\n'+body))
            out.append(dict(id=identity[:24],identitySha256=identity,originalStableId=stable,employer=employer,title=title,body=body,
                            sourceUrl=source,requisitionId=requisition,bodyAvailable=bool(body),contentSha256=content,
                            titleGroup=normalize(title),snapshotSavedAt=doc.get('savedAtUtc'),detailCachedAt=job.get('detailCachedAtUtc'),
                            sourceFile=str(path),sourceFileSha256=info['sha256']))
            counts['exportedRecords']+=1
            counts['exportedWithBody']+=bool(body)
    return out,dict(counts=dict(counts),sources=files,exclusionFiles=[dict(path=str(p),sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in exclusion_paths])

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--inventory',type=Path,action='append',required=True);p.add_argument('--exclusions',type=Path,action='append',required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--audit',type=Path,required=True)
    a=p.parse_args();rows,audit=export(a.inventory,a.exclusions)
    a.output.write_bytes(('\n'.join(json.dumps(r,ensure_ascii=False) for r in rows)+'\n').encode())
    a.audit.write_bytes((json.dumps(audit,indent=2)+'\n').encode())
    print(json.dumps(audit['counts']))
