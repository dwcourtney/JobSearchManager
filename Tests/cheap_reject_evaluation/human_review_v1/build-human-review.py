"""Prepare a frozen, unadjudicated human queue. No rule/model execution or network.
python -B build-human-review.py --raw unique.json --audit review.jsonl --workflow review-workflow.json --output NEW_DIR
"""
import argparse, collections, csv, hashlib, io, json
from pathlib import Path

# Literal excerpt anchors selected by the investigator, not classification rules.
# Index is the prior audit's frozen index; output is joined and validated by stable ID.
SPEC = {
95:('civil-structural-power','Technical expertise in highway',700),
96:('civil-structural-power','Desired skillsets',700),
15:('civil-structural-power','We are seeking a Senior Structural',750),
859:('civil-structural-power','We are seeking a Substation',1100),
860:('civil-structural-power','We are seeking a Licensed',1100),
998:('civil-structural-power','In this role',1200),
1669:('civil-structural-power','Key Responsibilities:',1000),
1642:('civil-structural-power','Responsibilities:',1000),
2046:('electronics-test-lab','What You Will Do',1250),
227:('electronics-test-lab','In this role',850),
480:('electronics-test-lab','Responsibilities:',1200),
496:('electronics-test-lab','Provide support in analysis',1200),
849:('electronics-test-lab','Primary Responsibilities:',1250),
1108:('enterprise-application-support','Responsibilities:',1400),
33:('field-technicians-training','The candidate will ideally',1200),
619:('field-technicians-training','Position Responsibilities:',1300),
885:('field-technicians-training','The ASO will be required',1100),
914:('field-technicians-training','The ASO will be required',1100),
468:('field-technicians-training','Responsibilities:',1200),
105:('technical-project-leadership','Assess current-state',1300),
1661:('technical-project-leadership',"What You'll Be Doing",850),
2003:('technical-project-leadership','What You Will Do',1400),
65:('technical-project-leadership','AECOM is seeking a Civil OSP',1400),
663:('manufacturing-quality-analysis','Apprentices will learn',900),
2000:('manufacturing-quality-analysis','What Will You Do',1300),
2014:('manufacturing-quality-analysis','What You Will Do',1400),
611:('manufacturing-quality-analysis','Position Responsibilities:',1400),
2151:('technical-presales-product','About the Role',1400),
2136:('technical-presales-product','ROLE SUMMARY',1400),
}
SECOND={
1661:('Serve as Design Manager',650),
496:('Experience inspecting',430),
2136:('Coordinate with professional services',430),
}
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def text(path,s):path.write_bytes(s.encode('utf-8'))
def main():
 p=argparse.ArgumentParser(description=__doc__)
 for name in ('raw','audit','workflow','output'):p.add_argument('--'+name,required=True,type=Path)
 a=p.parse_args();a.output.mkdir(parents=True,exist_ok=False)
 raw=json.loads(a.raw.read_text(encoding='utf-8-sig'))
 audits=[json.loads(x) for x in a.audit.read_text(encoding='utf-8').splitlines()]
 selected=[x for x in audits if x['judgment'] in ('questionable','likely-wrong')]
 assert len(selected)==29 and {x['auditIndex'] for x in selected}==set(SPEC)
 assert collections.Counter(x['judgment'] for x in selected)=={'questionable':24,'likely-wrong':5}
 workflow=json.loads(a.workflow.read_text(encoding='utf-8-sig'))
 rows=[]; proofs=[]
 for num,x in enumerate(sorted(selected,key=lambda x:(SPEC[x['auditIndex']][0],x['employer'],x['stableId'])),1):
  i=x['auditIndex'];r=raw[i];assert r['stableId']==x['stableId'] and r['observation']['decision']=='REJECT'
  assert r['observation']['result']['ruleIds']==x['ruleIds']
  assert r['observation']['result']['evidence']==x['matchedEvidence']
  assert r['observation']['result']['postingFingerprint']==x['postingFingerprint']
  group,anchor,n=SPEC[i]; body=r['plainBody'];parts=[];spans=[]
  for start_text,length in [(anchor,n)]+([SECOND[i]] if i in SECOND else []):
   start=body.casefold().find(start_text.casefold());assert start>=0,(i,start_text)
   end=min(start+length,len(body));parts.append(body[start:end]);spans.append({'start':start,'end':end,'text':body[start:end]})
  histories=[{'workspace':h['workspaceAlias'],**h['jobs'][x['stableId']]} for h in workflow['histories']]
  row=dict(review_id=f'Q{num:02}',stable_job_id=x['stableId'],employer=x['employer'],title=x['title'],
   family=group,current_decision='REJECT',category=x['category'],reason=x['reason'],
   matched_rule_ids='; '.join(x['ruleIds']),matched_evidence=json.dumps(x['matchedEvidence'],ensure_ascii=False),
   job_fit_score='' if x['jobFitScore'] is None else f"{x['jobFitScore']}/10",job_fit_availability=x['jobFitAvailability'],
   workflow_state='; '.join(h['workspace']+'='+h['state']+' ('+h['provenance']+')' for h in histories),
   description_available=r['observation']['descriptionAvailable'],duty_excerpt_1=parts[0],duty_excerpt_2=parts[1] if len(parts)>1 else '',
   audit_classification='LIKELY WRONG' if x['judgment']=='likely-wrong' else 'QUESTIONABLE',review_reason=x['note'],
   human_decision='',human_confidence='',human_reason='',reviewer='',reviewed_at_utc='')
  rows.append(row)
  proofs.append(dict(reviewId=row['review_id'],stableId=x['stableId'],auditIndex=i,cache=x['cache'],
   inputFingerprint=x['postingFingerprint'],normalizedBodySha256=hashlib.sha256(body.encode()).hexdigest(),excerpts=spans))
 csv_text=io.StringIO(newline='');writer=csv.DictWriter(csv_text,fieldnames=list(rows[0]),lineterminator='\n');writer.writeheader();writer.writerows(rows)
 text(a.output/'queue.csv',csv_text.getvalue())
 md=['# Human review: 29 Shadow rejects','',
 'Decisions are **blank**. Enter KEEP, REJECT or AMBIGUOUS in `queue.csv`, plus confidence, reason, reviewer and UTC date. AMBIGUOUS means continue downstream; it is not the runtime timeout state.',
 '','Read duties before the prior audit assessment. Judge broad technical/engineering occupational scope, not personal fit. Prior audit judgments are machine interpretations, not human truth.',
 '','All 29 have cached descriptions. Workflow is a later read-only snapshot of both existing workspaces, not a global state or a suitability label. A missing history entry defaults to normal and is marked. Blank Job Fit means not captured, not zero or TBD.',
 '',f"Workflow capture: {workflow['capturedAtUtc']}. Posting/evidence snapshot: live_shadow_v1, 2026-09-06. No posting refresh was performed.",
 '','| Review | Family | Employer | Title |','|---|---|---|---|']
 for row in rows:md.append(f"| [{row['review_id']}](#{row['review_id'].lower()}) | {row['family']} | {row['employer']} | {row['title'].replace('|','/')} |")
 for row in rows:
  md+=['',f"<a id=\"{row['review_id'].lower()}\"></a>",f"## {row['review_id']} — {row['employer']}: {row['title'].strip()}",'',
   f"ID: `{row['stable_job_id']}` · Family: `{row['family']}` · Description: available",'',
   '**Primary-duty excerpts** (literal normalized cached text; excerpt boundaries omit surrounding material):','',
   '> '+row['duty_excerpt_1']+' …']
  if row['duty_excerpt_2']:md+=['','> '+row['duty_excerpt_2']+' …']
  md+=['',f"**Current:** {row['current_decision']} — {row['category']}. {row['reason']}",
   f"**Rules:** `{row['matched_rule_ids']}`",'','**Matched evidence:**','']
  for e in json.loads(row['matched_evidence']):md.append(f"- `{e['predicateId']}` / {e['scope']}: {e['text']}")
  md+=['',f"**Job Fit:** {row['job_fit_score'] or 'Not captured'}; {row['job_fit_availability']}",
   f"**Workflow:** {row['workflow_state']}",'',
   f"**Prior audit: {row['audit_classification']}** — {row['review_reason']}",'',
   '**Human decision:** _____  **Confidence:** _____  **Reason / decisive duties:** _____','',
   '**Reviewer / UTC date:** _____']
 text(a.output/'queue.md','\n'.join(md)+'\n')
 text(a.output/'excerpt-provenance.json',json.dumps(proofs,ensure_ascii=False,indent=2)+'\n')
 text(a.output/'workflow-snapshot.json',json.dumps(workflow,indent=2)+'\n')
 manifest=dict(scope='Unadjudicated preparation only; immutable prior audit preserved',rows=29,
  families=dict(collections.Counter(x['family'] for x in rows)),priorJudgments=dict(collections.Counter(x['audit_classification'] for x in rows)),
  rawSha256=sha(a.raw),auditSha256=sha(a.audit),workflowInputSha256=sha(a.workflow),
  rulesetVersion='1.0.0',rulesetSha256='269be7264e56f641723c254910d026d20687f5d61aaa39d967c5d52e4bc51983',
  artifactSha256={n:sha(a.output/n) for n in ['queue.csv','queue.md','excerpt-provenance.json','workflow-snapshot.json']})
 text(a.output/'manifest.json',json.dumps(manifest,indent=2)+'\n')
 # Semantic validation: exact selected IDs, unchanged machine evidence, empty human decisions,
 # lossless multiline/Unicode CSV, and literal excerpts from the frozen normalized bodies.
 loaded=list(csv.DictReader(io.StringIO((a.output/'queue.csv').read_text(encoding='utf-8'))))
 assert len(loaded)==len({x['stable_job_id'] for x in loaded})==29
 assert {x['stable_job_id'] for x in loaded}=={x['stableId'] for x in selected}
 assert all(not x[k] for x in loaded for k in ['human_decision','human_confidence','human_reason','reviewer','reviewed_at_utc'])
 for row,proof in zip(loaded,proofs):
  assert row['duty_excerpt_1']==proof['excerpts'][0]['text']
  assert all(raw[proof['auditIndex']]['plainBody'][s['start']:s['end']]==s['text'] for s in proof['excerpts'])
 print('PASS: exact 29 IDs (24 questionable / 5 likely wrong), blank human decisions, CSV round-trip and source-exact excerpts')
 print(json.dumps(manifest['families']))
if __name__=='__main__':main()
