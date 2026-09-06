"""Export public posting text while excluding blinded holdout identities.

Run on the established Linux host. Exclusion manifests are used only for
identity matching; no holdout text, labels or predictions leave this process.
No services, containers, inference or production caches are modified.
"""
import argparse,base64,gzip,hashlib,html,json,re
from pathlib import Path

def normalize(text):
    return re.sub(r'\s+',' ',html.unescape(re.sub(r'<[^>]+>',' ',text or ''))).strip().lower()

def sha(text):return hashlib.sha256(text.encode()).hexdigest()

def export(root,manifests):
    titles=set();hashes=set();ids=set();audit=[]
    for p in manifests:
        raw=p.read_bytes();audit.append({'name':p.name,'sha256':hashlib.sha256(raw).hexdigest()})
        for e in json.loads(raw).get('examples',[]):
            if e.get('title'):titles.add(normalize(e['title']))
            if e.get('postingContentHash'):hashes.add(e['postingContentHash'])
            if e.get('stableId'):ids.add(e['stableId'])
    rows={};excluded=0
    for p in sorted(root.glob('workspaces/*/shared/job-caches/*/*.json')):
        for j in json.loads(p.read_text(encoding='utf-8')).get('jobs',[]):
            title=j.get('title','')
            if normalize(title) in titles or j.get('stableId') in ids or j.get('postingContentHash') in hashes:
                excluded+=1;continue
            body=j.get('descriptionHtml','')
            if not body and j.get('compressedDescriptionHtml'):
                body=gzip.decompress(base64.b64decode(j['compressedDescriptionHtml'])).decode('utf-8')
            body=normalize(body)
            if not body:continue
            key=sha(normalize(title)+'\n'+body)
            rows[key]=dict(id=key[:16],contentSha256=key,title=title,body=body,company=j.get('companyId','unknown'),titleGroup=normalize(title))
    return sorted(rows.values(),key=lambda r:r['id']),dict(excludedRecords=excluded,excludedTitleGroups=len(titles),exclusionManifests=audit)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--cache-root',type=Path,required=True)
    p.add_argument('--exclude-manifest',type=Path,action='append',required=True)
    p.add_argument('--output-dir',type=Path,required=True)
    a=p.parse_args();rows,audit=export(a.cache_root,a.exclude_manifest)
    a.output_dir.mkdir(parents=True,exist_ok=True)
    (a.output_dir/'eligible.json').write_text(json.dumps(rows,ensure_ascii=False)+'\n',encoding='utf-8')
    (a.output_dir/'exclusion-audit.json').write_text(json.dumps(audit,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(dict(eligible=len(rows),**audit)))
