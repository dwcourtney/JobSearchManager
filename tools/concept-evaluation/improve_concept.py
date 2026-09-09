"""Offline, one-concept rounds. Production rules are never written by this command."""
import argparse
import collections
import copy
import json
from pathlib import Path
import re
import shutil
import subprocess
import evaluate as ev


def keys(rows):
    return {p['id'] for p in rows}, {p['descriptionHash'] for p in rows}


def exclude(rows, seen):
    ids, hashes = keys(seen)
    return [p for p in rows if p['id'] not in ids and p['descriptionHash'] not in hashes]


def random_rows(rows, count, seed):
    if count < 1 or not rows:
        raise ValueError('No eligible fresh postings or invalid sample count')
    return sorted(rows, key=lambda p: (ev.digest((seed + '\0' + p['id'] + '\0' + p['descriptionHash']).encode()), p['id']))[:count]


def snapshot(run, role, rows, seed, requested, taxonomy, cid, extra=None):
    folder = run / role
    folder.mkdir(exist_ok=False)
    full = ev.read(taxonomy)
    selected = [c for c in full['concepts'] if c['id'] == cid]
    if len(selected) != 1:
        raise ValueError('Unknown or duplicate target concept')
    ev.freeze(folder/'sample.json', dict(schemaVersion=2, postings=rows))
    ev.freeze(folder/'taxonomy.json', dict(version=full['version'], concepts=selected))
    (folder/'full-taxonomy.json').write_bytes(Path(taxonomy).read_bytes())
    ev.freeze(folder/'sample-manifest.json', dict(schemaVersion=2, role=role, runId=run.name, conceptId=cid,
        seed=seed, requested=requested, actual=len(rows), createdUtc=ev.now(),
        method='sha256-seeded-simple-random-without-replacement-v1',
        selected=[dict(id=p['id'], descriptionHash=p['descriptionHash']) for p in rows],
        files={f:ev.digest((folder/f).read_bytes()) for f in ['sample.json','taxonomy.json','full-taxonomy.json']}, **(extra or {})))
    return folder


def start(args):
    run = Path(args.run)
    if run.exists():
        raise ValueError('Round already exists; use a new round ID')
    taxonomy = ev.read(args.taxonomy)
    if sum(c['id']==args.concept for c in taxonomy['concepts']) != 1:
        raise ValueError('Unknown concept ID')
    rows, stats, sources = ev.population(Path(args.cache_root))
    previous = []
    history = []
    if args.history_root:
        for m in sorted(Path(args.history_root).glob('**/sample-manifest.json')):
            data=ev.read(m)
            if data.get('conceptId') == args.concept:
                ev.verify(m.parent)
                prior=ev.read(m.parent/'sample.json')['postings']
                previous.extend(prior)
                history.append(dict(path=str(m),role=data['role'],sha256=ev.digest(m.read_bytes())))
    exposed=[]
    for path in args.exposed_sample:
        exposed.extend(ev.read(path)['postings'])
    fresh=exclude(rows,previous+exposed)
    # Keep old/fixed corpora available for development mining, never fresh validation.
    # Fail closed if no disjoint validation population can remain.
    if len(fresh) < 2:
        raise ValueError('Insufficient never-exposed postings for discovery and fresh validation')
    count=min(args.count, len(fresh)-1)
    discovery=random_rows(fresh,count,args.seed)
    reserved=exclude(fresh,discovery)
    mining=exclude(rows,discovery+reserved)
    run.mkdir(parents=True,exist_ok=False)
    for source,name in [(args.taxonomy,'full-taxonomy.json'),(args.rules,'baseline-rules.json'),
                        (str(Path(args.rules).with_suffix('.schema.json')),'baseline-rules.schema.json')]:
        shutil.copyfile(source,run/name)
    ev.freeze(run/'population.json',dict(postings=rows))
    ev.freeze(run/'validation-pool.json',dict(postings=reserved))
    ev.freeze(run/'mining-population.json',dict(postings=mining))
    ev.freeze(run/'round.json',dict(schemaVersion=1,runId=run.name,conceptId=args.concept,createdUtc=ev.now(),
        seed=args.seed,requested=args.count,population=stats,sources=sources,priorBatches=history,
        priorExposure=[dict(path=str(p),sha256=ev.digest(Path(p).read_bytes())) for p in args.exposed_sample],
        discovery=len(discovery),reservedValidationPopulation=len(reserved),broaderMiningPopulation=len(mining),
        policy=dict(minimumF1Gain=.05,minimumPrecision=.80,maximumPrecisionLoss=.03,minimumAPGain=0,
                    minimumPositiveSupport=10,minimumNegativeSupport=10),
        limitations='Available-cache sample; exact ID/description deduplication, near duplicates may remain. Prior global/round exposure excluded from fresh validation. No fallback reuse.',
        files={f:ev.digest((run/f).read_bytes()) for f in ['full-taxonomy.json','baseline-rules.json','baseline-rules.schema.json','population.json','validation-pool.json','mining-population.json']}))
    snapshot(run,'discovery',discovery,args.seed,args.count,run/'full-taxonomy.json',args.concept)
    print(json.dumps({k:ev.read(run/'round.json')[k] for k in ['conceptId','population','discovery','reservedValidationPopulation','broaderMiningPopulation']}))


def verify_round(run):
    meta=ev.read(run/'round.json')
    for f,h in meta['files'].items():
        if ev.digest((run/f).read_bytes()) != h:
            raise ValueError('Frozen round input changed: '+f)
    return meta


def check_candidate(baseline,candidate,cid):
    if {k:v for k,v in baseline.items() if k!='rules'} != {k:v for k,v in candidate.items() if k!='rules'}:
        raise ValueError('Non-rule authority/engine metadata changed')
    def core(rule):return {k:v for k,v in rule.items() if k!='executionOrder'}
    old={r['ruleId']:r for r in baseline['rules']}; new={r['ruleId']:r for r in candidate['rules']}
    if len(new)!=len(candidate['rules']) or len(old)!=len(baseline['rules']):
        raise ValueError('Duplicate rule IDs')
    if not set(old)<=set(new):
        raise ValueError('Existing stable rule IDs must be retained')
    for rid,r in old.items():
        if r['conceptId']!=cid and core(r)!=core(new[rid]):
            raise ValueError('Untouched concept changed: '+rid)
        if new[rid]['conceptId']!=r['conceptId']:
            raise ValueError('Existing rule concept changed')
    if any(r['conceptId']!=cid for rid,r in new.items() if rid not in old):
        raise ValueError('New rule belongs to another concept')
    ordered=sorted(candidate['rules'],key=lambda r:(r['conceptId'],r['kind'],r['ruleId']))
    if any(r['executionOrder']!=i for i,r in enumerate(ordered)):
        raise ValueError('Candidate must retain canonical execution ordering')
    changed=[rid for rid,r in new.items() if rid not in old or core(r)!=core(old[rid])]
    if not changed:
        raise ValueError('No target rule change')
    return changed


def seal(args):
    run=Path(args.run);meta=verify_round(run)
    if (run/'validation').exists():raise ValueError('Candidate cannot change after validation is assigned')
    baseline=ev.read(run/'baseline-rules.json'); candidate=ev.read(args.candidate)
    changed=check_candidate(baseline,candidate,meta['conceptId'])
    rationale=ev.read(args.rationale)
    if set(rationale)!=set(changed) or any(not all(rationale[k].get(f) for f in ['sourcePhrases','supportingPostingIds','falsePositiveRisk']) for k in changed):
        raise ValueError('Each changed rule requires source phrases, supporting posting IDs and false-positive risk')
    allowed={p['id']:p for p in ev.read(run/'discovery/sample.json')['postings']+ev.read(run/'mining-population.json')['postings']}
    normalize=lambda s:' '.join(s.lower().split())
    for detail in rationale.values():
        if not set(detail['supportingPostingIds'])<=set(allowed):raise ValueError('Rule rationale uses held-out/unknown postings')
        texts=[normalize(allowed[i]['title']+' '+allowed[i]['descriptionText']) for i in detail['supportingPostingIds']]
        if any(not any(normalize(phrase) in text for text in texts) for phrase in detail['sourcePhrases']):
            raise ValueError('Rule rationale phrase is absent from supporting corpus')
    ev.freeze(run/'candidate-rules.json',candidate)
    shutil.copyfile(run/'baseline-rules.schema.json',run/'candidate-rules.schema.json')
    ev.freeze(run/'candidate-rationale.json',rationale)
    ev.freeze(run/'candidate-seal.json',dict(sealedUtc=ev.now(),conceptId=meta['conceptId'],changedRuleIds=changed,
        files={f:ev.digest((run/f).read_bytes()) for f in ['candidate-rules.json','candidate-rules.schema.json','candidate-rationale.json']}))
    print(json.dumps(dict(changedRuleIds=changed,candidateHash=ev.digest((run/'candidate-rules.json').read_bytes()))))


def candidate_seal(run):
    seal=ev.read(run/'candidate-seal.json')
    for f,h in seal['files'].items():
        if ev.digest((run/f).read_bytes())!=h:raise ValueError('Sealed candidate changed: '+f)
    return seal


def validation(args):
    run=Path(args.run);meta=verify_round(run);seal=candidate_seal(run)
    # Also require a completed development evaluation before allocating validation.
    ev.read(run/'discovery-candidate-results.json')
    rows=random_rows(ev.read(run/'validation-pool.json')['postings'],args.count,args.seed)
    if len(exclude(rows,ev.read(run/'discovery/sample.json')['postings']))!=len(rows):
        raise ValueError('Discovery/validation overlap')
    snapshot(run,'validation',rows,args.seed,args.count,run/'full-taxonomy.json',meta['conceptId'],
             dict(candidateSealHash=ev.digest((run/'candidate-seal.json').read_bytes()),candidateSealedUtc=seal['sealedUtc']))
    print(json.dumps(dict(validation=len(rows),seed=args.seed,overlap=0)))


def result_for(sample, reference, predictions, cid, policy):
    byid={p['id']:p for p in predictions['postings']};refs={p['id']:p for p in reference['postings']}
    if len(byid)!=len(predictions['postings']) or set(byid)!=set(refs) or set(refs)!={p['id'] for p in sample['postings']}:
        raise ValueError('Incomplete prediction/reference matrix')
    i=reference['conceptIds'].index(cid);rules={r['ruleId']:r for r in predictions['rules']}
    rows=[];decisions=[]
    for p in sample['postings']:
        pred=byid[p['id']];ids=pred['matchedRuleIds'].get(cid,[])
        concepts={c['conceptId']:c for c in pred['concepts']};yes=cid in concepts
        if bool(ids)!=yes:raise ValueError('Accepted-rule/presence mismatch')
        y=refs[p['id']]['labels'][i];score=ev.score(ids,rules,policy)
        if y is not None:rows.append((y,yes,score))
        decisions.append(dict(id=p['id'],title=p['title'],company=p['company'],descriptionHash=p['descriptionHash'],
            reference=y,prediction=yes,score=score,ruleIds=ids,evidence=concepts.get(cid,{}).get('evidence',''),
            referenceEvidence=refs[p['id']].get('evidence',{}).get(cid,''),
            outcome='unresolved' if y is None else 'TP' if y and yes else 'FN' if y else 'FP' if yes else 'TN'))
    return dict(conceptId=cid,postings=len(decisions),resolved=len(rows),unresolved=len(decisions)-len(rows),
        metrics=ev.metrics(rows),curve=ev.curve(rows),scoreDistribution=dict(collections.Counter(str(r[2]) for r in rows)),decisions=decisions)


def evaluate_round(args):
    run=Path(args.run);meta=verify_round(run);folder=run/args.role
    sample,taxonomy=ev.verify(folder);refs=ev.read(folder/'reference-labels.json');rs=ev.read(folder/'reference-seal.json')
    if rs != dict(referenceHash=ev.digest((folder/'reference-labels.json').read_bytes()),sampleHash=ev.digest((folder/'sample.json').read_bytes())):
        raise ValueError('Reference seal changed')
    for p,h in refs['sourceHashes'].items():
        if ev.digest((folder/p).read_bytes())!=h:raise ValueError('Reference source label changed')
    candidate=args.variant=='candidate'
    if candidate:candidate_seal(run)
    if args.role=='validation':
        candidate_seal(run)
        if ev.read(folder/'sample-manifest.json')['candidateSealHash']!=ev.digest((run/'candidate-seal.json').read_bytes()):
            raise ValueError('Validation candidate identity changed')
    predictions=ev.read(args.predictions)
    rulefile=run/('candidate-rules.json' if candidate else 'baseline-rules.json')
    if predictions['authority']['byteHash']!=ev.digest(rulefile.read_bytes()) or predictions['sampleHash']!=rs['sampleHash']:
        raise ValueError('Wrong rules/sample for evaluation')
    if predictions['taxonomyHash']!=ev.digest((folder/'taxonomy.json').read_bytes().replace(b'\r\n',b'\n')):
        raise ValueError('Wrong frozen concept taxonomy')
    policy=ev.read(args.policy)
    result=result_for(sample,refs,predictions,meta['conceptId'],policy)
    result.update(role=args.role,variant=args.variant,createdUtc=ev.now(),
        rulesHash=ev.digest(rulefile.read_bytes()),referenceHash=rs['referenceHash'],
        predictionsHash=ev.digest(Path(args.predictions).read_bytes()),scorePolicyHash=ev.digest(Path(args.policy).read_bytes()))
    ev.freeze(run/f'{args.role}-{args.variant}-results.json',result)
    print(json.dumps({k:v for k,v in result.items() if k not in ['decisions','curve']}|dict(ap=result['curve']['averagePrecision'])))


def mine(args):
    run=Path(args.run);meta=verify_round(run)
    results=ev.read(run/'discovery-baseline-results.json')
    posts=ev.read(run/'discovery/sample.json')['postings'];broader=ev.read(run/'mining-population.json')['postings']
    # Generic phrase seeds derive from the target definition and actual reference-positive quotes.
    target=next(c for c in ev.read(run/'full-taxonomy.json')['concepts'] if c['id']==meta['conceptId'])
    positive=' '.join(d['referenceEvidence'] for d in results['decisions'] if d['reference'] is True)
    stop=set('a an and are as at be been by can for from has have in into is it its of on or our that the their this to use using was we will with you your'.split())
    def words(text):return [w for w in re.findall(r'[a-z][a-z0-9/+.-]*',text.lower()) if w not in stop and len(w)>2]
    seeds=set(words(target['definition']+' '+target['displayName']))
    quote_terms=collections.Counter(words(positive))
    seeds.update(w for w,n in quote_terms.most_common(20) if n>=2)
    patterns={};failures={}
    byid={p['id']:p for p in posts}
    for outcome in ['FN','FP']:
        relevant=[d for d in results['decisions'] if d['outcome']==outcome]
        counts=collections.defaultdict(set);examples=[]
        for d in relevant:
            p=byid[d['id']];sentences=[s for s in re.split(r'\n|(?<=[.!?])\s+',p['descriptionText']) if seeds.intersection(words(s))]
            quote=d['referenceEvidence'] or d['evidence'];examples.append(dict(**d,contexts=sentences[:8]))
            for line in [quote]+sentences:
                tokens=words(line)
                for size in [2,3,4]:
                    for i in range(len(tokens)-size+1):counts[' '.join(tokens[i:i+size])].add(d['id'])
        failures[outcome]=dict(count=len(relevant),examples=examples,
            phrases=[dict(phrase=p,count=len(ids),postingIds=sorted(ids)) for p,ids in sorted(counts.items(),key=lambda x:(-len(x[1]),x[0]))[:100]])
    contexts=[];counts=collections.defaultdict(set)
    for p in broader:
        for line in re.split(r'\n|(?<=[.!?])\s+',p['descriptionText']):
            tokens=words(line)
            if not seeds.intersection(tokens):continue
            for size in [2,3,4]:
                for i in range(len(tokens)-size+1):
                    gram=tokens[i:i+size]
                    if seeds.intersection(gram):counts[' '.join(gram)].add(p['id'])
            contexts.append(dict(id=p['id'],title=p['title'],text=line))
    ev.freeze(run/'failure-analysis.json',dict(concept=target,derivedSeeds=sorted(seeds),failures=failures,
        broaderPopulation=len(broader),broaderPhrases=[dict(phrase=p,count=len(ids),postingIds=sorted(ids)) for p,ids in sorted(counts.items(),key=lambda x:(-len(x[1]),x[0]))[:250]],
        broaderContexts=contexts,validationUsed=False))
    print(json.dumps(dict(fn=failures['FN']['count'],fp=failures['FP']['count'],broaderPopulation=len(broader))))


def decision(args):
    run=Path(args.run);meta=verify_round(run);candidate_seal(run)
    b=ev.read(run/'validation-baseline-results.json');c=ev.read(run/'validation-candidate-results.json');p=meta['policy']
    checks=dict(f1Gain=c['metrics']['f1']-b['metrics']['f1']>=p['minimumF1Gain'],
        precisionFloor=c['metrics']['precision']>=p['minimumPrecision'],
        precisionLoss=c['metrics']['precision']>=b['metrics']['precision']-p['maximumPrecisionLoss'],
        apMaintained=c['curve']['averagePrecision'] is not None and b['curve']['averagePrecision'] is not None and c['curve']['averagePrecision']>=b['curve']['averagePrecision'],
        support=c['metrics']['support']>=p['minimumPositiveSupport'] and c['metrics']['negativeSupport']>=p['minimumNegativeSupport'])
    review=ev.read(args.review)
    regressions=ev.read(args.regression)
    checks['untouchedParity']=regressions.get('untouchedConceptChanges')==0
    checks['reviewedFamilies']=review.get('noUnexplainedRegression') is True and review.get('noBroadFalsePositiveFamily') is True
    label='ACCEPTABLE FOR RELEASE REVIEW' if all(checks.values()) else 'REJECT' if not all(checks[k] for k in ['f1Gain','precisionFloor','precisionLoss','apMaintained','untouchedParity']) else 'NEEDS REVIEW'
    changes=[dict(id=x['id'],before=x,after=y) for x,y in zip(b['decisions'],c['decisions']) if x['id']==y['id'] and x['prediction']!=y['prediction']]
    result=dict(decision=label,checks=checks,criteria=p,createdUtc=ev.now(),productionSwitch=False,
        validationBaseline=b['metrics'],validationCandidate=c['metrics'],baselineAP=b['curve']['averagePrecision'],candidateAP=c['curve']['averagePrecision'],
        changedValidationDecisions=changes,review=review,regressionHash=ev.digest(Path(args.regression).read_bytes()),
        candidateSealHash=ev.digest((run/'candidate-seal.json').read_bytes()))
    ev.freeze(run/'decision.json',result)
    print(json.dumps({k:v for k,v in result.items() if k not in ['changedValidationDecisions','review']}))


def regression(args):
    run=Path(args.run);meta=verify_round(run);candidate_seal(run);cid=meta['conceptId']
    datasets=ev.read(args.datasets);results=[];unrelated=0
    policy=ev.read(args.policy)
    for item in datasets:
        folder=Path(item['sample']);sample,taxonomy=ev.verify(folder)
        refs=ev.read(folder/'reference-labels.json')
        if ev.read(folder/'reference-seal.json')!=dict(referenceHash=ev.digest((folder/'reference-labels.json').read_bytes()),sampleHash=ev.digest((folder/'sample.json').read_bytes())):
            raise ValueError('Regression reference seal changed')
        baseline=ev.read(item['baseline']);candidate=ev.read(item['candidate'])
        for variant,pred in [('baseline',baseline),('candidate',candidate)]:
            if pred['sampleHash']!=ev.digest((folder/'sample.json').read_bytes()) or pred['authority']['byteHash']!=ev.digest((run/(variant+'-rules.json')).read_bytes()):
                raise ValueError('Regression prediction identity mismatch')
        b=result_for(sample,refs,baseline,cid,policy);c=result_for(sample,refs,candidate,cid,policy)
        changes=[];other=[]
        bm={p['id']:p for p in baseline['postings']};cm={p['id']:p for p in candidate['postings']}
        for ident in bm:
            bp=bm[ident];cp=cm[ident]
            bc={x['conceptId']:x for x in bp['concepts'] if x['conceptId']!=cid};cc={x['conceptId']:x for x in cp['concepts'] if x['conceptId']!=cid}
            for otherId in set(bc)|set(cc):
                if bc.get(otherId)!=cc.get(otherId) or bp['matchedRuleIds'].get(otherId,[])!=cp['matchedRuleIds'].get(otherId,[]):other.append(dict(id=ident,conceptId=otherId))
        bd={d['id']:d for d in b['decisions']};cd={d['id']:d for d in c['decisions']}
        for ident,d in bd.items():
            if d['prediction']!=cd[ident]['prediction']:
                changes.append(dict(id=ident,before=d,after=cd[ident]))
        unrelated+=len(other)
        results.append(dict(name=item['name'],role='later-regression',freshValidation=False,postings=len(sample['postings']),
            baseline=b['metrics'],candidate=c['metrics'],resolved=b['resolved'],unresolved=b['unresolved'],
            baselineAP=b['curve']['averagePrecision'],candidateAP=c['curve']['averagePrecision'],
            changedTargetDecisions=changes,untouchedConceptChanges=other,
            sourceHashes={k:ev.digest(Path(item[k]).read_bytes()) for k in ['baseline','candidate']}))
    result=dict(conceptId=cid,createdUtc=ev.now(),untouchedConceptChanges=unrelated,datasets=results,
                note='Null/unlabeled historical decisions are unscored. Prior discovery remains development/regression, never fresh validation.')
    ev.freeze(run/'regression-results.json',result)
    print(json.dumps(dict(untouchedConceptChanges=unrelated,datasets=[dict(name=r['name'],postings=r['postings'],changed=len(r['changedTargetDecisions'])) for r in results])))


def main():
    parser=argparse.ArgumentParser(description=__doc__);commands=parser.add_subparsers(dest='command',required=True)
    p=commands.add_parser('start');p.add_argument('--concept',required=True);p.add_argument('--cache-root',required=True);p.add_argument('--taxonomy',required=True);p.add_argument('--rules',required=True);p.add_argument('--history-root');p.add_argument('--exposed-sample',action='append',default=[]);p.add_argument('--count',type=int,default=500);p.add_argument('--seed',required=True)
    p=commands.add_parser('seal-candidate');p.add_argument('--candidate',required=True);p.add_argument('--rationale',required=True)
    p=commands.add_parser('validation');p.add_argument('--count',type=int,default=500);p.add_argument('--seed',required=True)
    p=commands.add_parser('evaluate');p.add_argument('--role',choices=['discovery','validation'],required=True);p.add_argument('--variant',choices=['baseline','candidate'],required=True);p.add_argument('--predictions',required=True);p.add_argument('--policy',required=True)
    commands.add_parser('mine')
    p=commands.add_parser('regression');p.add_argument('--datasets',required=True);p.add_argument('--policy',required=True)
    p=commands.add_parser('decide');p.add_argument('--review',required=True);p.add_argument('--regression',required=True)
    for p in commands.choices.values():p.add_argument('--run',required=True)
    args=parser.parse_args();{'start':start,'seal-candidate':seal,'validation':validation,'evaluate':evaluate_round,'mine':mine,'regression':regression,'decide':decision}[args.command](args)


if __name__=='__main__':main()
