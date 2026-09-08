from pathlib import Path
import hashlib,json,sys
import argparse
p=argparse.ArgumentParser();p.add_argument('comparison',type=Path);p.add_argument('--repository',type=Path,default=Path(__file__).resolve().parents[3]);a=p.parse_args();r=a.repository;e=a.comparison
summary=json.loads((e/'comparison-summary.json').read_text(encoding='utf-8'));authority=summary['candidate']
assert summary['curated']['exact'] and summary['holdout']['exact'] and not summary['modelsInvoked']
sourcefiles=['ConceptRuleSnapshot.cs','RegexSemanticClassifier.cs','SemanticClassificationService.cs','RegexEvaluation.cs','AiHoldoutEvaluation.cs']
sources={p:hashlib.sha256((r/p).read_text(encoding='utf-8').encode()).hexdigest() for p in sourcefiles}
source='uncommitted-concept-validation:'+hashlib.sha256(json.dumps(sources,sort_keys=True,separators=(',',':')).encode()).hexdigest()
metric=hashlib.sha256(''.join((r/p).read_text(encoding='utf-8') for p in ['RegexEvaluation.cs','AiHoldoutEvaluation.cs']).encode()).hexdigest()
out=r/'evaluation/concept-detection';out.mkdir(parents=True,exist_ok=True)
(out/'.gitattributes').write_text('*.json -text\n',encoding='utf-8')
entries=[]
for role,file in [('curated',e/'curated-json.json'),('holdout',e/'holdout-json/ai-holdout-report-latest.json')]:
    raw=json.loads(file.read_text(encoding='utf-8'));assert raw['rulesetFingerprint']==authority['pipelineFingerprint']
    dataset=raw.get('datasetFingerprint',raw.get('validationCorpusFingerprint'));reference=raw.get('referenceLabelFingerprint',dataset)
    report=dict(schemaVersion=1,role=role,sourceIdentity=source,sourceFiles=sources,authority=authority,metricImplementationHash=metric,
        datasetFingerprint=dataset,referenceFingerprint=reference,runId=raw['evaluationRunId'],evaluatedUtc=raw['evaluatedUtc'],
        datasetId=raw['datasetId'],labelProvenance=raw['labelProvenance'],postingCount=raw['postingCount'],
        eligibleDecisions=raw.get('eligibleConceptDecisions',raw.get('conceptDecisionCount')),unresolvedCount=raw.get('unresolvedExcludedDecisions',0),
        macro=raw['macro'],micro=raw['micro'],concepts=raw['concepts'])
    if role=='curated':report['ruleAttribution']=raw['rules']
    data=(json.dumps(report,ensure_ascii=False,indent=2)+'\n').encode();name=role+'-'+raw['evaluationRunId']+'.json'
    target=out/name
    if target.exists():assert target.read_bytes()==data,'Never overwrite an immutable report'
    else:target.write_bytes(data)
    entries.append(dict(role=role,file=name,sha256=hashlib.sha256(data).hexdigest(),datasetFingerprint=dataset,referenceFingerprint=reference))
(out/'index-v1.json').write_text(json.dumps(dict(schemaVersion=1,reports=entries),indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps(dict(sourceIdentity=source,metricImplementationHash=metric,reports=entries),indent=2))
