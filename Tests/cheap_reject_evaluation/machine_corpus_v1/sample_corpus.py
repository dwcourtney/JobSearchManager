"""Deterministic identity/near-copy deduplication and unbalanced random sampling."""
import argparse
import collections
import hashlib
import json
from pathlib import Path
from sklearn.feature_extraction.text import TfidfVectorizer

def sha(value):return hashlib.sha256(value.encode()).hexdigest()

class Union:
    def __init__(self,n):self.parents=list(range(n))
    def find(self,i):
        while self.parents[i]!=i:
            self.parents[i]=self.parents[self.parents[i]];i=self.parents[i]
        return i
    def join(self,i,j):
        a,b=self.find(i),self.find(j)
        self.parents[max(a,b)]=min(a,b)

def sample(paths,target,seed):
    identities=collections.defaultdict(list)
    for path in paths:
        for line in path.read_text(encoding='utf-8').splitlines():
            row=json.loads(line);identities[row['identitySha256']].append(row)
    rows=[];identity_clusters=[]
    for identity,versions in sorted(identities.items()):
        versions.sort(key=lambda r:(r['bodyAvailable'],r.get('detailCachedAt') or r.get('snapshotSavedAt') or '',r['contentSha256']),reverse=True)
        row=dict(versions[0],sourceVersions=len(versions),sourceFiles=sorted({r['sourceFile'] for r in versions}))
        rows.append(row)
        identity_clusters.append(dict(id=row['id'],versions=len(versions),distinctContents=len({r['contentSha256'] for r in versions}),sourceFiles=row['sourceFiles']))
    titles=TfidfVectorizer(analyzer='char_wb',ngram_range=(3,5),min_df=1).fit_transform([r['titleGroup'] for r in rows])
    similarities=(titles@titles.T).tocoo()
    near=Union(len(rows));families=Union(len(rows))
    exact={};shingles={};near_edges=[]
    for i,row in enumerate(rows):
        if row['bodyAvailable']:
            key=row['contentSha256']
            if key in exact:near.join(i,exact[key])
            else:exact[key]=i
            words=row['body'].split()
            shingles[i]={tuple(words[j:j+5]) for j in range(len(words)-4)}
    for i,j,similarity in zip(similarities.row,similarities.col,similarities.data):
        if i>=j:continue
        if similarity>=.80:families.join(int(i),int(j))
        if similarity>=.90 and rows[i]['employer']==rows[j]['employer'] and i in shingles and j in shingles:
            left,right=shingles[i],shingles[j]
            if not left or not right:continue
            overlap=len(left&right)/len(left|right)
            if overlap>=.95:
                near.join(int(i),int(j));near_edges.append(dict(left=rows[i]['id'],right=rows[j]['id'],titleCosine=float(similarity),bodyShingleJaccard=overlap))
    groups=collections.defaultdict(list);family_members=collections.defaultdict(list)
    for i,row in enumerate(rows):
        groups[near.find(i)].append(i);family_members[families.find(i)].append(row['id'])
    family_ids={k:sha('|'.join(sorted(ids)))[:20] for k,ids in family_members.items()}
    frame=[];clusters=[]
    for indices in groups.values():
        indices.sort(key=lambda i:(not rows[i]['bodyAvailable'],sha(seed+'|representative|'+rows[i]['id'])))
        rep=indices[0];members=[rows[i]['id'] for i in indices]
        cluster_id=sha('|'.join(sorted(members)))[:20]
        row=dict(rows[rep],duplicateCluster=cluster_id,duplicateClusterSize=len(indices),titleFamilyCluster=family_ids[families.find(rep)])
        frame.append(row)
        clusters.append(dict(cluster=cluster_id,representative=row['id'],members=sorted(members)))
    frame.sort(key=lambda r:sha(seed+'|sample|'+r['id']))
    selected=frame[:target]
    for rank,row in enumerate(selected):row['sampleRank']=rank;row['selectionProbability']=min(1,target/len(frame))
    return selected,dict(seed=seed,target=target,exportedOccurrences=sum(len(v) for v in identities.values()),identityUnique=len(rows),deduplicatedFrame=len(frame),sampled=len(selected),
                         bodyAvailable=sum(r['bodyAvailable'] for r in selected),bodyMissing=sum(not r['bodyAvailable'] for r in selected),
                         employerCounts=dict(collections.Counter(r['employer'] for r in selected)),titleFamilyCount=len({r['titleFamilyCluster'] for r in selected}),
                         distinctTitles=len({r['titleGroup'] for r in selected}),frameEmployerCounts=dict(collections.Counter(r['employer'] for r in frame)),
                         sampling='SHA-256 seeded simple random ordering after identity and strict near-copy deduplication; census if frame below target. No label balancing or employer quotas.',
                         deduplication='Prefer hydrated then most recently cached whole version per employer/requisition identity; exact title/body digest duplicates; same-employer title character-TFIDF cosine >=0.90 AND five-word body-shingle Jaccard >=0.95. Transitive clusters, seeded representative. Missing-body near copies cannot be established.',
                         titleFamilies='Unsupervised connected components of title character-TFIDF cosine >=0.80 across employers. Lexical grouping, not guaranteed semantic occupation families.',
                         identityClusters=identity_clusters,duplicateClusters=clusters,nearDuplicateEdges=near_edges,
                         titleFamilyMembers={family_ids[k]:sorted(ids) for k,ids in family_members.items()},
                         inputHashes={str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths})

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--input',type=Path,action='append',required=True);p.add_argument('--target',type=int,default=3500);p.add_argument('--seed',default='jsm-machine-corpus-v1-20260905');p.add_argument('--output',type=Path,required=True);p.add_argument('--manifest',type=Path,required=True)
    a=p.parse_args();rows,manifest=sample(a.input,a.target,a.seed)
    a.output.write_bytes(('\n'.join(json.dumps(r,ensure_ascii=False) for r in rows)+'\n').encode())
    manifest['sampleSha256']=hashlib.sha256(a.output.read_bytes()).hexdigest()
    a.manifest.write_bytes((json.dumps(manifest,indent=2)+'\n').encode())
    print(json.dumps({k:v for k,v in manifest.items() if k not in ['identityClusters','duplicateClusters','nearDuplicateEdges','titleFamilyMembers']},indent=2))
