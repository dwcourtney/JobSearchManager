"""Offline reporting only; never classifies jobs, contacts providers, or writes caches.

Build: python -B build-report.py --raw-dir PATH --output NEW_DIRECTORY
Verify portable artifacts: python -B build-report.py --validate-only DIRECTORY
The raw directory is the frozen collector archive plus adjudications.tsv/scores.json.
"""
import argparse
import collections
import csv
import hashlib
import json
from pathlib import Path

RELEASE = 'e0a28a10bfb158d5ea2c86f4718ca79f3fb99309'
RULE_HASH = '269be7264e56f641723c254910d026d20687f5d61aaa39d967c5d52e4bc51983'
TARGETED = {65, 227, 480, 496, 611, 849, 1983}

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def write_json(path, value):
    path.write_bytes((json.dumps(value, indent=2, ensure_ascii=False) + '\n').encode('utf-8'))

def deduplicate(rows):
    groups = collections.defaultdict(list)
    for row in rows:
        groups[row['stableId']].append(row)
    selected = [max(group, key=lambda x: (x['sourceRefreshed'] or '', x['descriptionBacked'], x['cache']))
                for group in groups.values()]
    return sorted(selected, key=lambda x: (x['employer'], x['stableId']))

def sample(rows):
    rank = lambda x: hashlib.sha256(('jsm-live-shadow-audit-v1|' + x['stableId']).encode()).hexdigest()
    chosen = []
    for employer in sorted({x['employer'] for x in rows}):
        buckets = collections.defaultdict(list)
        for row in rows:
            if row['employer'] == employer and row['observation']['decision'] == 'REJECT':
                buckets[row['observation']['result']['category']].append(row)
        for bucket in buckets.values():
            bucket.sort(key=rank)
        picked = []
        while len(picked) < 12 and any(buckets.values()):
            for key in sorted(buckets):
                if buckets[key] and len(picked) < 12:
                    picked.append(buckets[key].pop(0))
        chosen += picked
        chosen += sorted([x for x in rows if x['employer'] == employer and x['observation']['decision'] == 'KEEP'], key=rank)[:5]
        chosen += [x for x in rows if x['employer'] == employer and x['observation']['decision'] == 'UNDETERMINED']
    return {x['stableId'] for x in chosen}

def metrics(rows):
    counts = collections.Counter(x['observation']['decision'] for x in rows)
    return dict(total=len(rows), keep=counts['KEEP'], reject=counts['REJECT'], undetermined=counts['UNDETERMINED'],
                titleOnly=sum(not x['descriptionBacked'] for x in rows),
                descriptionBacked=sum(x['descriptionBacked'] for x in rows),
                rejectionRate=counts['REJECT']/len(rows) if rows else None)

def summarize(rows, audits):
    unique = deduplicate(rows)
    groups = collections.defaultdict(list)
    for row in rows:
        groups[row['stableId']].append(row)
    employers = {e: metrics([x for x in unique if x['employer'] == e]) for e in sorted({x['employer'] for x in unique})}
    providers = {p: metrics([x for x in unique if x['provider'] == p]) for p in sorted({x['provider'] for x in unique})}
    return dict(aggregate=metrics(unique), employers=employers, providers=providers,
        sourceAvailable=metrics([x for x in unique if x['sourceAvailable'] is True]),
        retainedUnavailable=metrics([x for x in unique if x['sourceAvailable'] is False]),
        described=metrics([x for x in unique if x['descriptionBacked']]),
        titleOnly=metrics([x for x in unique if not x['descriptionBacked']]),
        rawCacheInstances=len(rows), duplicateInstancesRemoved=len(rows)-len(unique),
        overlappingStableIds=sum(len(g)>1 for g in groups.values()),
        overlappingDecisionDisagreements=sum(len({x['observation']['decision'] for x in g})>1 for g in groups.values()),
        overlappingInputDisagreements=sum(len({x['observation']['result']['postingFingerprint'] for x in g})>1 for g in groups.values()),
        provenance=dict(collections.Counter(x['provenance'] for x in unique)),
        keepReasons=dict(collections.Counter(x['observation']['result']['reason'] for x in unique if x['observation']['decision']=='KEEP')),
        rejectCategories=dict(collections.Counter(x['observation']['result']['category'] for x in unique if x['observation']['decision']=='REJECT')),
        auditJudgments=dict(collections.Counter(x['judgment'] for x in audits)),
        coreAuditJudgments=dict(collections.Counter(x['judgment'] for x in audits if x['sampling']=='core')),
        employerRejectAudit={e: dict(collections.Counter(x['judgment'] for x in audits if x['employer']==e and x['decision']=='REJECT')) for e in employers})

def validate(folder):
    manifest = json.loads((folder/'manifest.json').read_text(encoding='utf-8'))
    assert manifest['release'] == RELEASE
    for name, sha in manifest['artifactSha256'].items():
        assert digest(folder/name) == sha, name
    rows = [json.loads(line) for line in (folder/'observations.jsonl').read_text(encoding='utf-8').splitlines()]
    audits = [json.loads(line) for line in (folder/'review.jsonl').read_text(encoding='utf-8').splitlines()]
    unique = deduplicate(rows)
    by_id = {x['stableId']: x for x in unique}
    assert len({(x['cache'],x['stableId']) for x in rows}) == len(rows)
    assert len({x['stableId'] for x in audits}) == len(audits)
    for c in manifest['caches']:
        subset = [x for x in rows if x['cache']==c['cache']]
        assert len(subset)==c['count']
        assert dict(collections.Counter(x['observation']['decision'] for x in subset))==c['counts']
        assert c['network']=='none'
    for row in rows:
        o = row['observation']; r = o['result']
        assert o['analysisVersion']==1 and r['rulesetVersion']=='1.0.0' and r['rulesetFingerprint']==RULE_HASH
        assert len(r['postingFingerprint'])==64
        assert o['descriptionAvailable']==row['descriptionBacked']
        assert o['decision']==('UNDETERMINED' if r['failOpen'] else r['decision'])
        assert row['originalObservationUnchanged'] if row['hadOriginalObservation'] else True
    core = sample(unique)
    assert {x['stableId'] for x in audits if x['sampling']=='core'} == core
    assert {x['auditIndex'] for x in audits if x['sampling']=='targeted'} == TARGETED
    allowed = {'REJECT': {'clearly-correct','questionable','likely-wrong'},
               'KEEP': {'appropriate-keep','borderline-keep','obvious-leak'},
               'UNDETERMINED': {'undetermined-timeout'}}
    for row in audits:
        source = by_id[row['stableId']]
        assert row['auditIndex']==unique.index(source)
        assert row['decision']==source['observation']['decision']
        assert row['judgment'] in allowed[row['decision']] and row['note']
        assert row['ruleIds']==source['observation']['result']['ruleIds']
    summary = summarize(rows, audits)
    assert summary == json.loads((folder/'summary.json').read_text(encoding='utf-8'))
    assert summary['aggregate']==dict(total=2205,keep=1967,reject=236,undetermined=2,titleOnly=450,descriptionBacked=1755,rejectionRate=236/2205)
    assert len(audits)==157 and sum(x['decision']=='REJECT' for x in audits)==100
    print('PASS: source totals, deduplication, identities, preserved observations, deterministic sample, audit joins, metrics and artifact hashes')

def build(raw, output):
    output.mkdir(parents=True, exist_ok=False)
    records = json.loads((raw/'records.json').read_text(encoding='utf-8-sig'))
    original = json.loads((raw/'manifest.json').read_text(encoding='utf-8-sig'))
    rows = []
    for x in records:
        rows.append({k:x[k] for k in ('cache','sourceRefreshed','employer','stableId','title','observation','sourceAvailable')} | {
            'provider':'SmartRecruiters' if x['employer'] in ('aecom','servicenow') else 'Workday',
            'descriptionBacked':bool(x['body'].strip()),
            'bodySha256':hashlib.sha256(x['body'].encode()).hexdigest(),
            'hadOriginalObservation':x['previousObservation'] is not None,
            'originalObservationUnchanged':x['previousObservation']==x['observation'],
            'provenance':'production-recorded' if x['previousObservation']==x['observation'] else 'isolated-runtime-reconciliation'})
    unique = deduplicate(rows); core=sample(unique)
    notes = list(csv.DictReader((raw/'adjudications.tsv').open(encoding='utf-8-sig'),delimiter='\t'))
    assert len({int(x['index']) for x in notes})==len(notes)
    scores = json.loads((raw/'scores.json').read_text())
    audits=[]
    for note in notes:
        i=int(note['index']); x=unique[i]; r=x['observation']['result']
        assert x['stableId'] in core or i in TARGETED
        audits.append(dict(auditIndex=i,stableId=x['stableId'],employer=x['employer'],title=x['title'],
            decision=x['observation']['decision'], category=r['category'],reason=r['reason'],ruleIds=r['ruleIds'],
            matchedEvidence=r['evidence'],descriptionBacked=x['descriptionBacked'],
            sampling='core' if x['stableId'] in core else 'targeted',
            judgment=note['judgment'],note=note['note'],
            adjudicator='Codex live-audit interpretation; not human ground truth or a historical relabel',
            jobFitScore=scores.get(x['stableId']),
            jobFitAvailability='already rendered in signed-in Parsons list; profile-dependent, not safety truth' if x['stableId'] in scores else 'not captured; no score computed or detail/provider fetch requested',
            cache=x['cache'],postingFingerprint=r['postingFingerprint']))
    for name, data in [('observations.jsonl',rows),('review.jsonl',audits)]:
        (output/name).write_bytes(''.join(json.dumps(x,ensure_ascii=False)+'\n' for x in data).encode('utf-8'))
    write_json(output/'summary.json',summarize(rows,audits))
    manifest=original | dict(schemaVersion=1,rulesetVersion='1.0.0',rulesetSha256=RULE_HASH,
        remoteArchive='/home/codex/jsm-lab/experiments/live-shadow-audit-20260906',
        rawRecordsSha256=digest(raw/'records.json'),rawManifestSha256=digest(raw/'manifest.json'),
        originalCollectorSha256=digest(raw/'collect.py'),
        scope='Existing employer/query caches; production observations preserved; actual deployed app in network-disabled isolated containers for other decisions. Not new normal-browser traffic.',
        duplicatePolicy='Stable posting ID; newest source refresh, then described input, then descending cache key. Distinct near-duplicate requisitions retained.',
        samplePolicy='SHA256 jsm-live-shadow-audit-v1|stableId order; category round-robin up to 12 rejects/employer, five keeps/employer, all undetermined; seven additional title-census risk cases.',
        truthLimitations='Codex audit judgments only; no independent human ground truth, no recall/precision certification, no edits to historical labels.',
        artifactSha256={n:digest(output/n) for n in ('observations.jsonl','review.jsonl','summary.json')})
    write_json(output/'manifest.json',manifest)
    validate(output)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--raw-dir',type=Path);p.add_argument('--output',type=Path);p.add_argument('--validate-only',type=Path)
    args=p.parse_args()
    if args.validate_only: validate(args.validate_only)
    elif args.raw_dir and args.output: build(args.raw_dir,args.output)
    else: p.error('Use --validate-only DIRECTORY or --raw-dir PATH --output NEW_DIRECTORY')
