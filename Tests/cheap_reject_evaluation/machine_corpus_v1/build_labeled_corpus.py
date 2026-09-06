"""Assemble frozen Codex decisions and reports. No model calls or label inference."""
import csv
import hashlib
import json
from collections import Counter, defaultdict
from pathlib import Path

ROOT=Path(__file__).resolve().parent
def read(name):return [json.loads(x) for x in (ROOT/name).read_text(encoding='utf-8').splitlines()]
def sha(name):return hashlib.sha256((ROOT/name).read_bytes()).hexdigest()
def writejson(name,obj):
    (ROOT/name).write_text(json.dumps(obj,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
def writejsonl(name,rows):
    (ROOT/name).write_text(''.join(json.dumps(r,ensure_ascii=False)+'\n' for r in rows),encoding='utf-8',newline='\n')
def count(rows,key):return dict(sorted(Counter(r[key] for r in rows).items()))

def build():
    source=read('sample-unlabeled.jsonl'); decisions=read('codex-decisions.jsonl'); reviews=read('codex-quality-reviews.jsonl')
    assert len(source)==len(decisions)==3198
    assert {r['id'] for r in source}=={r['id'] for r in decisions}
    assert len({r['id'] for r in decisions})==3198
    byid={r['id']:r for r in decisions}; overrides={r['id']:r for r in reviews}
    assert len(overrides)==len(reviews)
    result=[];changes=[]
    for s in source:
        d=json.loads(json.dumps(byid[s['id']]))
        assert d['inputContentSha256']==s['contentSha256']
        d['flags']['assessmentScope']='Reviewed evidence only; false does not certify the unseen body.'
        d['flags']['descriptionInsufficient']=False
        if d['flags']['titleBodyConflict']:d['basis']='duties'
        if s['id'] in overrides:
            v=overrides[s['id']]
            changes.append(dict(id=s['id'],title=s['title'],beforeLabel=d['label'],afterLabel=v['label'],beforeConfidence=d['confidence'],afterConfidence=v['confidence']))
            d['initialDecision']={k:d[k] for k in ['label','confidence','category','reason','basis']}
            for k in ['label','confidence','category','reason','basis']:d[k]=v[k]
            d['reviewedEvidence']+=v['reviewedEvidence']
            for k in ['titleBodyConflict','technicalJargonDutiesConflict','descriptionInsufficient']:d['flags'][k]=v[k]
            d['qualityReviewed']=True
        else:d['qualityReviewed']=False
        for e in d['reviewedEvidence']:
            assert s['body'][e['start']:e['end']]==e['text']
        d['labelStatus']='provisional-machine-adjudicated-not-human-gold'
        d['trainingEligibility']='unapproved; no training performed'
        result.append({**s,**d})
    writejsonl('machine-labeled.jsonl',result)
    # Review selection is deterministic and coverage-oriented, NOT a random
    # accuracy/prevalence sample. Surface all observed conflicts plus 100
    # ambiguous examples per description partition where available.
    queue={r['id']:r for r in result if r['flags']['titleBodyConflict'] or r['flags']['technicalJargonDutiesConflict'] or r['flags']['descriptionInsufficient']}
    for available in [False,True]:
        groups=defaultdict(list)
        for r in result:
            if r['bodyAvailable']==available and r['label']=='AMBIGUOUS':groups[(r['employer'],r['category'])].append(r)
        for group in groups.values():group.sort(key=lambda r:hashlib.sha256(('review-v1'+r['id']).encode()).hexdigest())
        selected=[]
        while len(selected)<100 and any(groups.values()):
            for k in sorted(groups):
                if groups[k] and len(selected)<100:selected.append(groups[k].pop(0))
        for r in selected:queue[r['id']]=r
    review_rows=sorted(queue.values(),key=lambda r:(r['bodyAvailable'],r['employer'],r['category'],r['id']))
    writejsonl('human-review.jsonl',review_rows)
    columns=['id','employer','title','label','confidence','category','reason','basis','bodyAvailable','titleFamilyCluster','sourceUrl']
    with (ROOT/'human-review.csv').open('w',encoding='utf-8-sig',newline='') as f:
        w=csv.DictWriter(f,fieldnames=columns);w.writeheader();w.writerows({k:r[k] for k in columns} for r in review_rows)
    tg=defaultdict(list);bg=defaultdict(list);cross=defaultdict(list)
    for r in result:
        cross[r['titleGroup']].append(r)
        if not r['bodyAvailable']:tg[r['titleGroup']].append(r)
        else:bg[hashlib.sha256(r['body'].encode()).hexdigest()].append(r)
    repeated=[v for v in tg.values() if len(v)>1]
    body_repeated=[v for v in bg.values() if len(v)>1]
    cross_disagreements=[dict(title=k,postings=[{f:r[f] for f in ['id','label','bodyAvailable']} for r in v]) for k,v in cross.items() if len({r['bodyAvailable'] for r in v})>1 and len({r['label'] for r in v})>1]
    quality=dict(reviewed=len(reviews),labelChanges=[c for c in changes if c['beforeLabel']!=c['afterLabel']],
                 repeatedTitleOnlyGroups=len(repeated),repeatedTitleOnlyRows=sum(map(len,repeated)),
                 inconsistentTitleOnlyGroups=sum(len({r['label'] for r in v})>1 for v in repeated),
                 repeatedBodyGroups=len(body_repeated),inconsistentIdenticalBodyGroups=sum(len({r['label'] for r in v})>1 for v in body_repeated),
                 crossPartitionTitleDisagreements=cross_disagreements,
                 interpretation='Internal consistency only. Same Codex reviewer; no independent human accuracy, recall, or false-reject estimate.')
    writejson('quality-checks.json',quality)
    summary=dict(total=len(result),labels=count(result,'label'),confidence=count(result,'confidence'),
                 byDescription={str(b):dict(total=sum(r['bodyAvailable']==b for r in result),labels=count([r for r in result if r['bodyAvailable']==b],'label'),confidence=count([r for r in result if r['bodyAvailable']==b],'confidence')) for b in [False,True]},
                 titleOnlyMissingDescription=sum(not r['bodyAvailable'] and r['basis']=='title' for r in result),
                 titleOnlyDespiteAvailableDescription=sum(r['bodyAvailable'] and r['basis']=='title' for r in result),
                 employerCounts=count(result,'employer'),distinctTitles=len(cross),lexicalTitleFamilies=len({r['titleFamilyCluster'] for r in result}),
                 reviewQueue=len(review_rows),reviewQueueLowConfidence=sum(r['confidence']=='low' for r in review_rows),
                 titleBodyConflicts=sum(r['flags']['titleBodyConflict'] is True for r in result),
                 jargonDutiesConflicts=sum(r['flags']['technicalJargonDutiesConflict'] is True for r in result))
    writejson('label-summary.json',summary)
    manifest=dict(corpusVersion='jsm-codex-adjudication-v1-20260905',adjudicator='Codex in this task; no external/local model API or inference service',
                  checkpoint='Exact backend model revision not exposed in task; no checkpoint claim is made.',
                  definitions=dict(KEEP="Plausibly within the user's realistic technical search space; continue downstream.",REJECT='Clearly unrelated primary occupational duties; safe cheap-triage rejection.',AMBIGUOUS='Insufficient, mixed or unclear evidence; never force into a binary training class.'),
                  evidencePolicy='Every title reviewed. Available bodies reviewed through recorded deterministic excerpts, with targeted additional spans. Not an exhaustive full-description review. Missing descriptions: medium confidence at most; uncertain duties: AMBIGUOUS/low.',
                  scope='Occupational plausibility, not final personal eligibility, clearance, seniority, location, or Job Fit scoring.',
                  samplingManifest='sampling-manifest.json',preservedDistribution='Census of deduplicated eligible cache frame, no class balancing. Not representative of the entire job market; missingness and employer selection bias remain.',
                  excludedData='Production holdout and prior evaluation examples excluded by frozen hashed indexes; their labels not used for adjudication.',
                  reproducibility='Frozen explicit decision ledger + targeted-review ledger reproduce corpus exactly. Scripts never infer new labels. A fresh Codex adjudication is not guaranteed identical.',
                  sha256={n:sha(n) for n in ['sample-unlabeled.jsonl','sampling-manifest.json','production-holdout-exclusions.json','prior-evaluation-exclusions.json','codex-decisions.jsonl','codex-quality-reviews.jsonl','machine-labeled.jsonl','human-review.jsonl','adjudicate.py','quality_reviews.py','build_labeled_corpus.py','test_adjudication.py']})
    writejson('adjudication-manifest.json',manifest)
    lines=['# JSM Codex cheap-triage corpus adjudication','',
           'Completed 3,198 provisional machine decisions. No Qwen, local/generative labeling service, training, production changes, rule changes, or commits.', '',
           'Codex directly reviewed every title and recorded explicit decisions. The helper scripts present evidence and serialize those decisions; they do not classify text. Bodies were reviewed via recorded excerpts and targeted additional spans, **not exhaustively in full**. Confidence describes the reviewed evidence, not calibrated probability.', '',
           '## Distribution','', '| Evidence availability | Total | KEEP | REJECT | AMBIGUOUS |','|---|---:|---:|---:|---:|']
    for b in [False,True]:
        x=summary['byDescription'][str(b)];lines.append(f"| {'Description available' if b else 'No description'} | {x['total']} | {x['labels'].get('KEEP',0)} | {x['labels'].get('REJECT',0)} | {x['labels'].get('AMBIGUOUS',0)} |")
    lines+=['',f"Overall labels: {summary['labels']}. Confidence: {summary['confidence']}.",f"Missing-description title-only decisions: {summary['titleOnlyMissingDescription']}; available-but-insufficient description title-only decisions: {summary['titleOnlyDespiteAvailableDescription']}.",
            '', '## Label definition and evidence limits','',
            'KEEP means plausibly relevant technical or adjacent duties, including software, infrastructure/support, cybersecurity, engineering analysis, controls/electronics and technical integration. REJECT means clearly unrelated primary work. AMBIGUOUS means insufficient or mixed evidence and must not be silently mapped to REJECT or forced into binary training truth. Employer/product names and generic technology terms are not sufficient evidence. Technical presales can KEEP when hands-on architecture, deployment or debugging duties are present. Personal eligibility and final application fit were not assessed.',
            '', 'The 1,886 description-missing rows are 58.97% of this corpus. All decisive labels in that partition are medium confidence, and its AMBIGUOUS labels are low. Treat them as weak title labels, separately from description-supported data. Do not use them as body-classifier ground truth or as a human-gold evaluation set. Even high-confidence machine labels require human validation before drawing 98–99% recall conclusions.',
            '', 'A targeted second read changed Extended Workforce Solutions HR Project Manager from REJECT to KEEP: integration monitoring and root-cause resolution across VNDLY, Workday and ServiceNow are substantive technical duties. This illustrates missing-body risk; it is not an estimated error rate for all title-only rows.',
            '', '## Sampling and coverage','',
            f"{summary['distinctTitles']} normalized titles, {summary['lexicalTitleFamilies']} lexical title-family clusters, {len(summary['employerCounts'])} employers. The eligible frame had 3,198 representatives, so all were retained rather than artificially balancing labels. Employer counts: {summary['employerCounts']}.",
            'The preparation manifest preserves source paths/hashes, identity versions, duplicate clusters, seed, exclusion indexes and sampling rules. Title families are lexical connected components, not validated occupational categories. Missing bodies limit near-duplicate detection. This is a selected JSM cache distribution, strongly dominated by Leidos, not a random job-market sample.',
            '', '## Quality and consistency','',
            f"{quality['reviewed']} targeted second reads; {len(quality['labelChanges'])} class changes, retained in quality-checks.json and the final records' initialDecision fields.",
            f"Repeated title-only groups: {quality['repeatedTitleOnlyGroups']} ({quality['repeatedTitleOnlyRows']} rows); conflicting class labels: {quality['inconsistentTitleOnlyGroups']}. Identical nonempty-body groups: {quality['repeatedBodyGroups']}; conflicting classes: {quality['inconsistentIdenticalBodyGroups']}.",
            f"{len(cross_disagreements)} identical-title groups differ across description availability. Examples include Senior Program Manager (title ambiguous, network-delivery duties KEEP), Mid Cartographic Analyst (title ambiguous, GIS duties KEEP), and Material Project Manager (title ambiguous, procurement duties REJECT). Different postings are not paired counterfactual labels.",
            f"Observed title/body conflicts: {summary['titleBodyConflicts']}; technical-jargon/duty conflicts: {summary['jargonDutiesConflicts']}. Flags apply only to reviewed evidence; missing-body conflict fields remain unknown.",
            'Internal repeated-evidence agreement supports consistency but does not measure correctness. No independent reviewer, random blinded re-adjudication, human accuracy, KEEP recall, or false-reject rate was measured. Do not translate zero duplicate disagreements into 100% labeling accuracy.',
            '', '## Human review','',
            f"human-review.jsonl and human-review.csv contain {summary['reviewQueue']} cases, including {summary['reviewQueueLowConfidence']} low-confidence cases. Selection includes all flagged conflicts plus up to 100 ambiguous cases per description partition with employer/category coverage. It is an intentionally enriched review queue, not a population-risk estimate.",
            'Difficult title-only examples include technical writers, systems/engineering technicians, program managers, intelligence analysts, product owners and integration-adjacent support roles. Missing descriptions prevent a reliable distinction between hands-on technical duties, operational use of systems and administrative coordination.',
            '', '## Examples','']
    for label in ['KEEP','REJECT','AMBIGUOUS']:
        examples=[r for r in result if r['label']==label and r['bodyAvailable']]
        examples=sorted(examples,key=lambda r:r['reviewOrdinal'])[:5]
        lines.append(f'**{label}**')
        lines.append('')
        for r in examples:lines.append(f"- {r['title']} ({r['employer']}, {r['id']}): {r['reason']} [{r['confidence']}; {r['basis']}]")
        lines.append('')
    lines+=['## Reproduce and validate','',
            'From this directory, run `python -X utf8 quality_reviews.py`, `python -X utf8 build_labeled_corpus.py`, and `python -X utf8 -m unittest discover -s . -p "test_*.py" -v`. Frozen decision ledgers are the semantic input. Original sample and exclusions remain immutable.',
            '', 'See validation-results.md for the actual checks run. Stop here: no classifier has been trained or enabled.']
    (ROOT/'adjudication-report.md').write_text('\n'.join(lines)+'\n',encoding='utf-8',newline='\n')
    print(json.dumps(summary,indent=2));print('Quality class changes:',len(quality['labelChanges']))

if __name__=='__main__':build()
