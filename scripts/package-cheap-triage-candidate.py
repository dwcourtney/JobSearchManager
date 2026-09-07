"""Package existing evaluation results for the Admin handoff; no evaluation or activation."""
import argparse,json,pathlib,hashlib
p=argparse.ArgumentParser();p.add_argument('--snapshot',required=True,type=pathlib.Path);p.add_argument('--comparison',required=True,type=pathlib.Path);p.add_argument('--changes',required=True,type=pathlib.Path);p.add_argument('--human-comparison',required=True,type=pathlib.Path);p.add_argument('--rules',required=True,type=pathlib.Path);p.add_argument('--validation',required=True,choices=['PASS','FAIL','INCOMPLETE']);p.add_argument('--output',required=True,type=pathlib.Path);p.add_argument('--result-type',choices=['CANDIDATE','NO_UPDATE_NEEDED'],default='CANDIDATE');a=p.parse_args()
sha=lambda path:hashlib.sha256(path.read_bytes()).hexdigest()
manifest=json.loads((a.snapshot/'manifest.json').read_text());bundle=sha(a.snapshot/'manifest.json');assert bundle==a.snapshot.name
for n,h in manifest['artifacts'].items():assert sha(a.snapshot/n)==h
review=json.loads((a.snapshot/'human-review.json').read_text());f=json.loads(a.comparison.read_text());changes=json.loads(a.changes.read_text());human=json.loads(a.human_comparison.read_text());rules=a.rules.read_bytes();rh=hashlib.sha256(rules).hexdigest()
assert f['runs']['baseline']['rulesetSha256']==manifest['rulesetFingerprint'];assert f['runs']['candidate']['rulesetSha256']==rh
hm={x['posting']['stableJobId']:x['human'] for x in review['decisions']};assert set(hm)=={x['id'] for x in human}
for x in human:assert hm[x['id']]==x['human']
assert {x['id'] for x in f['runs']['candidate']['changedDecisions']} <= {x['id'] for x in changes}
def metrics(n):
 c=f['runs'][n]['cohorts'];return dict(keepRecall=c['binary']['keepRecall'],describedKeepRecall=c['described']['keepRecall'],titleOnlyKeepRecall=c['titleOnly']['keepRecall'],rejectionRate=c['binary']['rejectionCount']/c['binary']['n'],oldCacheRejectionRate=c['oldCache']['rejectionCount']/c['oldCache']['n'],falseRejectCount=c['binary']['falseRejectCount'])
before,after=metrics('baseline'),metrics('candidate');failures=[]
if f['frozenParityMismatches']:failures.append('Frozen baseline parity mismatch')
if any(x['before']['decision']=='KEEP' and x['after']['decision']=='REJECT' for x in changes):failures.append('New REJECT decisions require safety investigation')
if any(after[k]<before[k] for k in ['keepRecall','describedKeepRecall','titleOnlyKeepRecall']) or after['falseRejectCount']>before['falseRejectCount']:failures.append('KEEP safety regression')
result=dict(snapshotBundle=bundle,baselineVersion=manifest['rulesetVersion'],baselineHash=manifest['rulesetFingerprint'],candidateVersion=json.loads(rules)['rulesetVersion'],candidateHash=rh,rulesetJson=rules.decode('utf-8'),humanCorrections=sum(x['baseline']['decision']!=hm[x['id']]['decision'] and x['candidate']['decision']==hm[x['id']]['decision'] for x in human),before=before,after=after,changedDecisions=changes,warnings=['Frozen labels are provisional; the selected human review is development evidence, not independent population safety proof.','KEEP is continuation advice, not a Technology label. No production gating or workload savings are established.'],safetyFailures=failures,validationStatus=a.validation)
if a.result_type=='NO_UPDATE_NEEDED':
 assert rh==manifest['rulesetFingerprint'], 'No-update must retain exact current rules'
 assert before==after and not changes and not f['runs']['candidate']['changedDecisions'], 'No-update cannot change decisions or metrics'
 assert not failures and a.validation=='PASS', 'No-update requires successful validation'
 assert min(after['keepRecall'],after['describedKeepRecall'],after['titleOnlyKeepRecall'])>=.98, 'No-update does not meet safety floor'
 assert len(human)==len(hm), 'Duplicate human IDs'
 assert all(x['baseline']['decision']==x['candidate']['decision']==hm[x['id']]['decision'] and x['candidate']['decision'] in ('KEEP','REJECT') for x in human), 'Current rules must match every human decision'
 payload=dict(snapshotBundle=bundle,rulesetVersion=manifest['rulesetVersion'],rulesetHash=rh,
   queueFingerprint=review['queueFingerprint'],sourceManifestHash=review['sourceManifestHash'],
   humanMatches=[dict(id=x['id'],human=x['human'],decision=x['candidate']['decision']) for x in human],
   metrics=after,changedDecisionCount=0,validationStatus='PASS',warnings=result['warnings'],safetyFailures=[],evaluationArtifactHash=sha(a.comparison))
 result=dict(resultType='NO_UPDATE_NEEDED',noUpdate=payload)
else:
 assert result['candidateVersion']!=result['baselineVersion'], 'Same-version candidates are invalid; use explicit NO_UPDATE_NEEDED only when its evidence requirements are met'
 result=dict(resultType='CANDIDATE',candidate=result)
with a.output.open('x',encoding='utf-8',newline='\n') as out:json.dump(result,out,indent=2)
print(str(a.output),hashlib.sha256(a.output.read_bytes()).hexdigest())
