"""First-stage leakage audit and explicitly versioned offline rule refinements.
No production imports, inference, network requests or holdout input.
"""
import argparse,collections,hashlib,json,re,statistics,time
from pathlib import Path
import evaluate as base

HERE=Path(__file__).resolve().parent
SEED='jsm-leakage-audit-v1'
VARIANTS=('baseline','safety','families','body-context','civil-context','procurement-context','aggressive-commercial','electrical-safe')
# Survival-only additions. Generic field/simulator/electronic support remains
# ambiguous; a physical title without a technical function is not enough.
SUPPORT_TITLE=base.rx(r'\b(?:field(?: service)?|training device|pc support|electronic maintenance) technician\b')
ERP_OBJECT=base.rx(r'\b(?:sql|sap|erp|s/?4hana|financial system(?:s)? modules)\b')
ERP_DUTY=base.rx(r'\b(?:configur\w*|troubleshoot\w*|systems? (?:issues|modules)|data mapping|functional specifications?|integration testing)\b')
EXTRA={
 'marketing-communications':(r'\b(?:marketing (?:specialist|manager)|public relations)\b',r'\b(?:campaigns?|press releases|media plans|marketing|messaging|go.to.market)\b'),
 'environment-field':(r'\b(?:environmental (?:project|program) manager|archaeolog\w*|omm\b.*technician)\b',r'\b(?:groundwater|soil|remediation|nepa|wetlands|environmental sampling|archaeolog\w*)\b'),
 'cost-scheduling-payroll':(r'\b(?:program schedule analyst|project controls? specialist|time & pay)\b',r'\b(?:earned value|cost and schedule|budget performance|payroll|timekeeping|garnishments)\b'),
 'supplier-administration':(r'\bsupplier (?:quality|development)\b',r'\b(?:supplier performance|supplier quality|supplier health|on.site at supplier|supplier.*deliveries|quality issues)\b'),
 'aircraft-maintenance-training':(r'\bmaintenance customer technical training\b',r'\b(?:aircraft mechanic|military aircraft maintainers|aircraft maintenance training)\b'),
}
EXTRA_COMPILED={k:(base.rx(t),base.rx(b)) for k,(t,b) in EXTRA.items()}
# Filter individual veto matches, never delete a whole posting or all vetoes.
# Genuine alternative matches continue to protect the posting.
NOISE=base.rx(r'\b(?:talent pipelines?|recruit top.tier|professional network|practice network|cad software|consistent application|development and application|program controls|programmatic safety|programs within ms office|training application)\b')
CIVIL_TITLE=base.rx(r'\b(?:civil|roadway|bridge|geotechnical|drainage|structural)\b')
CIVIL_OBJECT=base.rx(r'\b(?:bridges?|roadways?|highways?|stormwater|storm sewers|culverts?|foundations?|concrete panels|steel structures|steel framing|landfills|excavations|erosion|sediment)\b')
CIVIL_WORK=base.rx(r'\b(?:design\w*|construction|drawings?|calculations?|grading|hydraulic|geotechnical)\b')
CORE_TITLE=base.rx(r'\b(?:software|developer\w*|programmer|devops|devsecops|full[- ]stack|back[- ]end|cloud|aws|azure|systems?|integration|automation|ai|ml|nlp|machine learning|network\w*|cyber\w*|database|gis|sysadmin|administrator|solution consultant)\b|(?<!\w)(?:\.net|c#)(?!\w)')
PROCUREMENT_ROLE=base.rx(r'\bprocurement (?:agent|specialist)\b')
ROLE_TECH=base.rx(r'\b(?:engineer\w*|developer\w*|programmer|architect|sysadmin|administrator|integration|devops|devsecops|automation|solution consultant)\b')
ELECTRICAL_TITLE=base.rx(r'\b(?:lighting|electrical|protection|controls|signals?)\b')
COMMERCIAL=base.rx(r'\b(?:business development|bd director|product marketing|procurement agent|hr business partner)\b')


def decide(row,variant='baseline'):
    if variant not in VARIANTS:raise ValueError('Unknown variant')
    if variant=='baseline':return base.decision(row,'title-body-confirmed')
    level=VARIANTS.index(variant);t=base.normalize(row['title'])
    if SUPPORT_TITLE.search(t):return True,'KEEP: ambiguous technical support title'
    # Body evidence can only rescue an otherwise possible reject. Avoid scanning
    # descriptions for titles that cannot reach a rejection branch.
    possible=bool(base.title_matches(t)) or (level>=2 and any(p.search(t) for p,_ in EXTRA_COMPILED.values()))
    possible=possible or (level>=4 and bool(CIVIL_TITLE.search(t))) or (variant=='aggressive-commercial' and bool(COMMERCIAL.search(t)))
    if not possible:return True,'KEEP: no explicit unrelated title family'
    may_override=(level>=4 and bool(CIVIL_TITLE.search(t))) or (level>=5 and bool(PROCUREMENT_ROLE.search(t))) or (variant=='aggressive-commercial' and bool(COMMERCIAL.search(t)))
    if base.PROTECTED.search(t) and not may_override:return True,'KEEP: protected technical/engineering title'
    b=base.normalize(row['body'])
    if ERP_OBJECT.search(b) and ERP_DUTY.search(b):return True,'KEEP: explicit ERP/SQL/configuration/support evidence'
    if level==1:return base.decision(row,'title-body-confirmed')
    patterns=dict(base.COMPILED);patterns.update(EXTRA_COMPILED)
    families=[f for f,(title,_) in patterns.items() if title.search(t)]
    civil=level>=4 and CIVIL_TITLE.search(t) and CIVIL_OBJECT.search(b) and CIVIL_WORK.search(b) and not CORE_TITLE.search(t)
    if variant=='electrical-safe' and ELECTRICAL_TITLE.search(t):civil=False
    procurement=level>=5 and PROCUREMENT_ROLE.search(t) and not ROLE_TECH.search(t) and not CORE_TITLE.search(t.split(' - ')[0])
    aggressive=variant=='aggressive-commercial' and COMMERCIAL.search(t) and not ROLE_TECH.search(t)
    if civil:families.append('civil-domain')
    if aggressive and not families:families.append('commercial-role')
    if not families:return True,'KEEP: no explicit unrelated title family'
    if base.PROTECTED.search(t) and not (civil or procurement or aggressive):return True,'KEEP: protected technical/engineering title'
    matches=list(base.TECH_DUTIES.finditer(b))
    genuine=[m for m in matches if level<3 or not NOISE.search(m.group())]
    if genuine:return True,'KEEP: technical-duty veto: '+genuine[0].group()
    confirmed=[f for f in families if f=='civil-domain' or f=='commercial-role' and base.COMPILED['sales'][1].search(b) or f in patterns and patterns[f][1].search(b)]
    if not confirmed:return True,'KEEP: missing corroborating occupational body evidence'
    return False,'REJECT: '+', '.join(confirmed)


def sample(pool):
    groups=collections.defaultdict(list)
    for row in pool:
        keep,reason=decide(row);groups[reason if keep else 'REJECT'].append(row)
    rows=[]
    for s,items in sorted(groups.items()):
        n=min(len(items),60 if s=='REJECT' else 70 if 'no explicit' in s else 35)
        for row in sorted(items,key=lambda r:hashlib.sha256((SEED+'|'+r['id']).encode()).hexdigest())[:n]:
            rows.append(dict(row,auditIndex=len(rows),auditStratum=s,stratumPopulation=len(items),stratumSample=n,weight=len(items)/n))
    return rows


def audit_estimates(audit):
    strata={};families=collections.Counter();leaks=0.;keeps=0.;false_rejects=0.
    for s in dict.fromkeys(r['auditStratum'] for r in audit):
        rs=[r for r in audit if r['auditStratum']==s];neg=sum(r['label']=='REJECT' for r in rs);weight=rs[0]['weight']
        strata[s]=dict(population=rs[0]['stratumPopulation'],sample=len(rs),reviewedReject=neg,reviewedKeep=len(rs)-neg,estimatedReject=neg*weight)
        keeps+=sum(r['label']=='KEEP' for r in rs)*weight
        if s!='REJECT':
            leaks+=neg*weight
            for r in rs:
                if r['label']=='REJECT':families[r['leakageFamily']]+=weight
        else:false_rejects+=(len(rs)-neg)*weight
    return dict(strata=strata,estimatedUnrelatedKept=leaks,estimatedKeeps=keeps,estimatedFalseRejects=false_rejects,
                estimatedKeepRecall=1-false_rejects/keeps,leakageFamilies=dict(families),warning='Design-weighted editorial sample estimates, not independent human-validated accuracy. Do not use raw oversampled counts as prevalence.')


def benchmark(rows,variant):
    timings=[];batches=[]
    for _ in range(3):
        start=time.perf_counter()
        for r in rows:decide(r,variant)
        batches.append(time.perf_counter()-start)
        for r in rows:
            start=time.perf_counter_ns();decide(r,variant);timings.append((time.perf_counter_ns()-start)/1000)
    return dict(medianUs=statistics.median(timings),p95Us=sorted(timings)[int(.95*(len(timings)-1))],batchPerSecond=len(rows)/statistics.median(batches))


def run(pool):
    audit=base.load_rows(HERE/'leakage-audit.jsonl');delta=base.load_rows(HERE/'leakage-delta-audit.jsonl');dev=base.load_rows(HERE/'development.jsonl');fixtures=base.load_rows(HERE/'fixtures.jsonl')
    canonical=hashlib.sha256(json.dumps(pool,sort_keys=True,ensure_ascii=False,separators=(',',':')).encode()).hexdigest()
    manifest=json.loads((HERE/'manifest.json').read_text(encoding='utf-8'))
    if canonical!=manifest['eligiblePoolCanonicalSha256']:raise ValueError('Wrong cache snapshot')
    if [r['id'] for r in sample(pool)]!=[r['id'] for r in audit]:raise ValueError('Audit sampling does not replay')
    result=dict(poolCount=len(pool),poolSha256=canonical,auditSha256=hashlib.sha256((HERE/'leakage-audit.jsonl').read_bytes()).hexdigest(),candidateSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),estimates=audit_estimates(audit),variants={},transitions=[],labelDisagreements=[])
    devby={r['id']:r for r in dev}
    for row in audit:
        if row['id'] in devby and row['label']!=devby[row['id']]['label']:
            result['labelDisagreements'].append(dict(id=row['id'],title=row['title'],frozenLabel=devby[row['id']]['label'],auditLabel=row['label'],reason=row['labelReason']))
    for v in VARIANTS:
        e={}
        for name,rows in [('development',dev),('audit',audit),('deltaAudit',delta),('synthetic',fixtures)]:
            predictions=[decide(r,v)[0] for r in rows];e[name]=base.metrics(rows,predictions)
            e[name+'FalseRejects']=[dict(id=r['id'],title=r['title'],reason=r['labelReason']) for r,k in zip(rows,predictions) if not k and r['label']=='KEEP']
        preds=[decide(r,v)[0] for r in pool];e['cache']=dict(rejected=sum(not k for k in preds),rejectionRate=sum(not k for k in preds)/len(pool))
        e['latency']=benchmark(dev,v)
        result['variants'][v]=e
        for row,k in zip(pool,preds):
            old=decide(row)[0]
            if old!=k:result['transitions'].append(dict(id=row['id'],title=row['title'],variant=v,baselineKeep=old,keep=k,reason=decide(row,v)[1]))
    return result

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--pool',type=Path,required=True);p.add_argument('--output',type=Path,default=HERE/'leakage-results.json');a=p.parse_args()
    result=run(json.loads(a.pool.read_text(encoding='utf-8')));a.output.write_text(json.dumps(result,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    for v,e in result['variants'].items():print(v,'dev',e['development'],'audit',e['audit'],'cache',e['cache'],'FN',e['auditFalseRejects'])
