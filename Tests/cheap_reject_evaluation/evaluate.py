"""Offline development experiment. Never reads holdout labels or invokes an LLM.

Run: python evaluate.py --source-root REPO [--shadow-pool eligible.json]
Requires numpy/scikit-learn only for classical candidates. Output is development
 evidence, not a production switch or an independent performance certification.
"""
from __future__ import annotations
import argparse, collections, hashlib, html, json, platform, re, statistics, time
from pathlib import Path
import numpy as np
import sklearn
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.pipeline import FeatureUnion, Pipeline
from sklearn.preprocessing import FunctionTransformer
from sklearn.linear_model import LogisticRegression
from sklearn.model_selection import StratifiedGroupKFold

HERE = Path(__file__).resolve().parent
THRESHOLDS = (0.05, 0.1, 0.2, 0.5)  # Declared before scoring; no test-driven search.
SEED = 20260905

def normalize(text):
    return re.sub(r'\s+', ' ', html.unescape(re.sub(r'<[^>]+>', ' ', text or ''))).strip().lower()

def rx(pattern):
    return re.compile(pattern, re.I)

# Deliberately broad unguarded comparator; title keyword != primary occupation.
FAMILIES = {
    'clinical': (r'\b(?:nurse|clinical|physician|therapist|dental|dietitian|psychiatric|mental health|social worker)\b', r'\b(?:patient|patients|counseling|therapy|rehabilitation|medications|teeth)\b'),
    'driving': (r'\b(?:driver|truck|chauffeur)\b', r'\b(?:cdl|freight|deliver|delivery routes|tractor trailer|driving license)\b'),
    'trades-construction': (r'\b(?:construction|civil|bridge|roadway|structural|electrician|plumber|welder|machinist|mechanic|inspector|cad operator)\b', r'\b(?:construction|concrete|welding|weld|pipes|plumbing|wiring|machining|engines|aircraft|structural steel|bridge design)\b'),
    'finance': (r'\b(?:accountant|accounting|finance|financial|tax|payroll|auditor)\b', r'\b(?:accounting|financial statements|journal entries|tax|payroll|cost control|cost analysis|earned value|audits|audit|portfolios)\b'),
    'sales': (r'\b(?:sales|account executive|business development|capture|pursuit lead)\b', r'\b(?:sales|revenue|quota|quotas|capture|pipeline|prospect|commercial|contracts)\b'),
    'hr': (r'\b(?:hr|human resources|recruiter|recruiting|talent acquisition|compensation|staffing)\b', r'\b(?:hiring|recruitment|recruiting|candidates|employee relations|staffing|compensation|benefit policies)\b'),
    'hospitality': (r'\b(?:cook|chef|housekeep\w*|bartender|barista|restaurant server|food service|hotel front desk)\b', r'\b(?:food|kitchen|meals|guest rooms|linens|drinks|beverages|tables|cooking)\b'),
    'physical-security': (r'\b(?:security guard|security officer|police|armed security|protective officer)\b', r'\b(?:patrol|patrolling|firearm|gates|guarding|premises|physical access)\b'),
    'operator-technician': (r'\b(?:technician|operator|assembler|warehouse|material handler)\b', r'\b(?:manufacturing|production parts|machinery|pallets|forklift|repair|repairs|aircraft|assembly|assemble|materials)\b'),
    'business-other': (r'\b(?:procurement|supply chain programs|product marketing|go.to.market|gtm|estimator|cost estimating|port captain)\b', r'\b(?:procurement|supplier|suppliers|purchase|marketing|positioning|estimat\w*|vessels|crewing|commercial)\b'),
}
COMPILED = {k: (rx(t), rx(b)) for k, (t, b) in FAMILIES.items()}
# Broad technical/engineering survival is intentional. A few civil engineers will
# pass too; absent a demonstrated safe distinction, false keeps are acceptable.
PROTECTED = rx(r'\b(?:engineer\w*|developer\w*|programmer|software|devops|devsecops|full[- ]stack|back[- ]end|front[- ]end|platform|cloud|aws|azure|systems?|integration|automation|ai|ml|nlp|machine learning|deep learning|network\w*|cyber\w*|database|data warehouse|gis|architect|sysadmin|administrator|technical program|solution consultant)\b|(?<!\w)(?:\.net|c#)(?!\w)')
# Require an implementation/admin verb close to a specific technical object.
# A blanket 'software', 'application' or 'AI tools' mention is insufficient.
TECH_DUTIES = rx(r'\b(?:develop\w*|build\w*|implement\w*|program\w*|administer\w*|configur\w*|debug\w*|deploy\w*|integrat\w*|troubleshoot\w*|maintain\w*)\b[^.!?;]{0,80}\b(?:software|apis?|code|applications?|linux|windows server|servers?|networks?|kubernetes|sql|database|plc|cloud|python|csharp|c#|ci/cd)\b')

def title_matches(title):
    return [name for name, (t, _) in COMPILED.items() if t.search(title)]

def decision(row, candidate):
    title = normalize(row['title'])
    families = title_matches(title)
    if not families:
        return True, 'KEEP: no explicit unrelated title family'
    if candidate != 'title-unguarded' and PROTECTED.search(title):
        return True, 'KEEP: protected technical/engineering title'
    if candidate == 'title-body-confirmed':
        body = normalize(row['body'])
        if TECH_DUTIES.search(body):
            return True, 'KEEP: explicit technical implementation/support evidence'
        families = [f for f in families if COMPILED[f][1].search(body)]
        if not families:
            return True, 'KEEP: missing corroborating occupational body evidence'
    return False, 'REJECT: ' + ', '.join(families)


def legacy_patterns(source):
    """Semantic Python port of ONLY the current C# first-stage predicate.

    Extract literal regexes from source so this does not silently drift. Python
    regex timing is not a benchmark of the .NET implementation/timeouts.
    """
    text = source.read_text(encoding='utf-8')
    result = {}
    for name in ('HardConflictSignals', 'PhysicalTitleSignals', 'Buckets'):
        section = re.search(r'private static readonly (?:Signal|Bucket)\[\] '+name+r'\s*=\s*\[(.*?)\];', text, re.S)
        if not section:
            raise ValueError('Cannot extract legacy first stage: ' + name)
        result[name] = [rx(p.replace('""', '"')) for p in re.findall(r'@"((?:[^"]|"")*)"', section.group(1))]
        if not result[name]:
            raise ValueError('Empty legacy patterns: ' + name)
    return result


def legacy_decision(row, patterns):
    title = normalize(row['title']); combined = title + ' ' + normalize(row['body'])
    conflict = any(p.search(combined) for p in patterns['HardConflictSignals'])
    physical = any(p.search(title) for p in patterns['PhysicalTitleSignals'])
    technical = any(p.search(combined) for p in patterns['Buckets'])
    return not (conflict or (physical and not technical)), 'Legacy first-stage predicate'


def load_rows(path):
    rows = [json.loads(line) for line in path.read_text(encoding='utf-8').splitlines() if line]
    if len({r['id'] for r in rows}) != len(rows):
        raise ValueError('Duplicate ids')
    if any(r['label'] not in ('KEEP', 'REJECT') for r in rows):
        raise ValueError('Invalid label')
    return rows


def metrics(rows, keep):
    if len(rows) != len(keep):
        raise ValueError('Prediction count mismatch')
    positives = sum(r['label'] == 'KEEP' for r in rows)
    fn = sum(r['label'] == 'KEEP' and not p for r, p in zip(rows, keep))
    rejected = sum(not p for p in keep)
    return dict(n=len(rows), keeps=positives, falseRejects=fn, rejected=rejected,
                keepRecall=(positives-fn)/positives if positives else None,
                rejectionRate=rejected/len(rows) if rows else None,
                rejectPrecision=(rejected-fn)/rejected if rejected else None,
                negativeRecall=(rejected-fn)/(len(rows)-positives) if len(rows)>positives else None)


def texts(rows):
    return [normalize(r['title']) + ' ' + normalize(r['body']) for r in rows]

def titles(rows):
    return [normalize(r['title']) for r in rows]

def bodies(rows):
    return [normalize(r['body']) for r in rows]


def model(weighted=False):
    def vector():
        return TfidfVectorizer(ngram_range=(1, 2), min_df=1, max_features=30000, sublinear_tf=True)
    if weighted:
        features = FeatureUnion([
            ('title', Pipeline([('select', FunctionTransformer(titles)), ('tfidf', vector())])),
            ('body', Pipeline([('select', FunctionTransformer(bodies)), ('tfidf', vector())])),
        ], transformer_weights={'title': 3.0, 'body': 1.0})
    else:
        features = Pipeline([('select', FunctionTransformer(texts)), ('tfidf', vector())])
    return Pipeline([('features', features), ('lr', LogisticRegression(C=1.0, class_weight='balanced', solver='liblinear', random_state=SEED, max_iter=1000))])


def benchmark(predict, rows, rounds=3):
    predict(rows)  # Warm compilation/caches, excluded from results.
    single = []; batch = []
    for _ in range(rounds):
        start = time.perf_counter_ns(); predict(rows); batch.append((time.perf_counter_ns()-start)/1e9)
        for row in rows:
            start = time.perf_counter_ns(); predict([row]); single.append((time.perf_counter_ns()-start)/1000)
    return dict(singleMedianUs=statistics.median(single), singleP95Us=float(np.percentile(single,95)),
                batchPostingsPerSecond=len(rows)/statistics.median(batch), rounds=rounds,
                normalizationIncluded=True, trainingExcluded=True)


def evaluate(source_root, shadow_pool=None):
    rows = load_rows(HERE/'development.jsonl'); fixtures = load_rows(HERE/'fixtures.jsonl')
    manifest = json.loads((HERE/'manifest.json').read_text(encoding='utf-8'))
    for name in ('development.jsonl','fixtures.jsonl'):
        if hashlib.sha256((HERE/name).read_bytes()).hexdigest() != manifest[name+'Sha256']:
            raise ValueError('Frozen development artifact changed: ' + name)
    pool = json.loads(shadow_pool.read_text(encoding='utf-8')) if shadow_pool else None
    patterns = legacy_patterns(source_root/'TriageEvaluation.cs')
    report = dict(candidateSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),legacySourceSha256=hashlib.sha256((source_root/'TriageEvaluation.cs').read_bytes()).hexdigest(),thresholds=THRESHOLDS,modelConfiguration='TF-IDF word 1-2 grams, min_df=1, max_features=30000, sublinear_tf; LR C=1 balanced liblinear; weighted title:body=3:1; seed=20260905',protocol='Provisional development evidence; no holdout inputs, no LLM calls. Classical results are 5-fold out-of-company predictions; synthetic tests never train models.', environment=dict(python=platform.python_version(),sklearn=sklearn.__version__,numpy=np.__version__,os=platform.platform(),processor=platform.processor()),dataset=manifest,candidates={},folds=[],predictions=[])
    def record(name, keep, fixture_keep, predictor, reasons):
        entry = dict(development=metrics(rows,keep),synthetic=metrics(fixtures,fixture_keep),latency=benchmark(predictor,rows),
                     falseRejectExamples=[dict(id=r['id'],title=r['title'],basis=r['labelReason']) for r,p in zip(rows,keep) if not p and r['label']=='KEEP'],
                     rejectExamples=[dict(id=r['id'],title=r['title'],basis=r['labelReason']) for r,p in zip(rows,keep) if not p and r['label']=='REJECT'][:8],
                     syntheticFalseRejects=[r['title'] for r,p in zip(fixtures,fixture_keep) if not p and r['label']=='KEEP'])
        if pool is not None:
            out=predictor(pool);entry['unlabeledShadow']=dict(n=len(pool),rejected=sum(not bool(p) for p in out),rejectionRate=sum(not bool(p) for p in out)/len(pool),warning='No recall/precision estimate; purposive employer cache, not independent deployment evidence.')
        report['candidates'][name]=entry
        for row,p,reason in zip(rows,keep,reasons):
            report['predictions'].append(dict(id=row['id'],candidate=name,label=row['label'],predicted='KEEP' if p else 'REJECT',reason=reason))
    for name in ('legacy-stage1-port','title-unguarded','title-guarded','title-body-confirmed'):
        one = (lambda r: legacy_decision(r,patterns)) if name=='legacy-stage1-port' else (lambda r,n=name: decision(r,n))
        predictor = lambda data,f=one: [f(r)[0] for r in data]
        record(name,predictor(rows),predictor(fixtures),predictor,[one(r)[1] for r in rows])
    y=np.array([int(r['label']=='KEEP') for r in rows]); groups=np.array([r['company'] for r in rows])
    splits=list(StratifiedGroupKFold(n_splits=5,shuffle=True,random_state=SEED).split(np.zeros(len(rows)),y,groups))
    for train,test in splits:
        assert not set(groups[train]) & set(groups[test])
        report['folds'].append(dict(train=len(train),test=len(test),trainKeep=int(y[train].sum()),testKeep=int(y[test].sum()),testCompanies=sorted(set(groups[test]))))
    for weighted in (False,True):
        name='weighted-title-body-lr' if weighted else 'tfidf-lr'
        scores=np.zeros(len(rows));training_seconds=0.0
        for train,test in splits:
            fitted=model(weighted);start=time.perf_counter();fitted.fit([rows[i] for i in train],y[train]);training_seconds+=time.perf_counter()-start
            scores[test]=fitted.predict_proba([rows[i] for i in test])[:,1]
        fitted=model(weighted).fit(rows,y)  # Only for runtime, fixtures and unlabeled shadow, never empirical recall.
        fixture_scores=fitted.predict_proba(fixtures)[:,1]
        for threshold in THRESHOLDS:
            predictor=lambda data,m=fitted,t=threshold: m.predict_proba(data)[:,1]>=t
            candidate=f'{name}@{threshold:g}'
            record(candidate,scores>=threshold,fixture_scores>=threshold,predictor,[f'Out-of-company KEEP score {s:.6f}; reject below {threshold:g} (uncalibrated)' for s in scores])
            report['candidates'][candidate]['trainingSecondsFiveFolds']=training_seconds
    return report

if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,required=True)
    parser.add_argument('--shadow-pool',type=Path)
    parser.add_argument('--output',type=Path,default=HERE/'results.json')
    args=parser.parse_args()
    result=evaluate(args.source_root,args.shadow_pool)
    args.output.write_text(json.dumps(result,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    for name,entry in result['candidates'].items():
        print(name,json.dumps(entry['development']),json.dumps(entry['latency']), 'shadow',entry.get('unlabeledShadow',{}).get('rejectionRate'))
