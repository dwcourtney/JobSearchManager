"""Codex review worksheet and explicit decision recorder. No model API or classifier.

This script presents source evidence and records decisions supplied by Codex.
It does not infer occupational labels from text, patterns, or existing models.
"""
import argparse
import hashlib
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parent
SAMPLE=ROOT/'sample-unlabeled.jsonl'
CODES={
 'S':('KEEP','software','Explicit software development or software engineering work.'),
 'I':('KEEP','infrastructure-support','Technical computing, infrastructure, networks, systems or application support.'),
 'C':('KEEP','cybersecurity','Cybersecurity or technical information-security work.'),
 'D':('KEEP','data-ai','Data engineering, analytics, AI or machine-learning technical work.'),
 'E':('KEEP','engineering','Plausibly adjacent engineering, controls, electronics, integration or technical analysis.'),
 'T':('KEEP','technical-leadership','Technical architecture, product/program leadership or technical solution work.'),
 'H':('REJECT','clinical','Primary occupation is clinical care or healthcare service.'),
 'O':('REJECT','physical-operations','Primary occupation is physical operations, driving, trades or nontechnical production.'),
 'F':('REJECT','finance-accounting','Primary duties are financial/accounting rather than technical system work.'),
 'R':('REJECT','hr-recruiting','Primary duties are recruiting, human resources or personnel administration.'),
 'B':('REJECT','business-administration','Primary duties are nontechnical business administration, clerical or procurement work.'),
 'M':('REJECT','sales-marketing','Primary duties are selling, marketing or commercial relationship work without substantive technical duties.'),
 'P':('REJECT','physical-security','Primary duties are physical guarding or security operations rather than cybersecurity.'),
 'L':('REJECT','language-services','Primary occupation is translation, interpreting or language instruction rather than technical NLP work.'),
 'N':('REJECT','nontechnical-training','Primary occupation is nontechnical instruction or training administration.'),
 'W':('REJECT','personal-services','Primary occupation is nontechnical personal, fitness, customer or hospitality service.'),
 'A':('AMBIGUOUS','unclear-primary-duties','Available evidence does not resolve whether primary duties are meaningfully technical.'),
 'J':('AMBIGUOUS','adjacent-role-boundary','Adjacent role could involve technical work, but the available evidence is insufficient to decide safely.'),
 'X':('AMBIGUOUS','mixed-or-conflicting-evidence','Technical title/context and primary-duty evidence are mixed or conflicting.'),
}

def rows():return [json.loads(line) for line in SAMPLE.read_text(encoding='utf-8').splitlines()]
def ordered(kind):return sorted([r for r in rows() if r['bodyAvailable']==(kind=='body')],key=lambda r:(r['titleGroup'],r['employer'],r['id']))

def excerpts(row):
    body=row['body']
    if not body:return []
    # Layout anchors choose a review excerpt, never a class decision.
    anchors=["primary responsibilities","position responsibilities","what you'll be doing","what you will do","what you'll get to do","key responsibilities","essential duties","responsibilities include","job responsibilities","what you get to do","in this role","job description:","job description","job summary"]
    anchors.insert(3, 'what you will be doing')
    search_body=body.replace('\u2019', "'").replace('\u2018', "'")
    positions=[search_body.find(a) for a in anchors if search_body.find(a)>=0]
    start=positions[0] if positions else 0
    return [dict(start=start,end=min(len(body),start+620),text=body[start:start+620])]

def show(kind,start,count):
    selected=ordered(kind)
    for i in range(start,min(start+count,len(selected))):
        r=selected[i]
        print(f"{i}|{r['title']}"+(f" | {excerpts(r)[0]['text']}" if kind=='body' else ''))
    print('TOTAL',len(selected))

def record(kind,start,codes):
    selected=ordered(kind); existing=[]
    path=ROOT/'codex-decisions.jsonl'
    if path.exists():existing=[json.loads(line) for line in path.read_text(encoding='utf-8').splitlines()]
    seen={r['id'] for r in existing}
    for offset,code in enumerate(codes.split()):
        ordinal=start+offset;r=selected[ordinal]
        assert r['id'] not in seen,'Already adjudicated'
        base=code[0];assert base in CODES,code
        label,category,reason=CODES[base]
        if kind=='title':
            reason=('Title alone does not establish the technical nature of primary duties; description unavailable.' if label=='AMBIGUOUS' else 'Title indicates '+category.replace('-',' ')+' work; duties cannot be verified because the description is unavailable.')
        confidence='low' if label=='AMBIGUOUS' else ('medium' if kind=='title' else 'high')
        if 'm' in code:confidence='medium'
        if 'l' in code:confidence='low'
        evidence=excerpts(r)
        existing.append(dict(id=r['id'],reviewOrdinal=ordinal,reviewPartition=kind,label=label,confidence=confidence,category=category,reason=reason,
                             basis='title' if kind=='title' else 'both',reviewedEvidence=evidence,
                             bodyReviewScope='unavailable' if kind=='title' else 'excerpt',
                             flags=dict(descriptionUnavailable=kind=='title',titleBodyConflict=None if kind=='title' else ('x' in code),
                                        technicalJargonDutiesConflict=None if kind=='title' else ('j' in code)),
                             adjudicator='Codex direct adjudication in this task; no external/local model calls',
                             inputContentSha256=r['contentSha256']))
        seen.add(r['id'])
    path.write_bytes(('\n'.join(json.dumps(r,ensure_ascii=False) for r in existing)+'\n').encode())
    print('Recorded',len(codes.split()),'decisions; total',len(existing))

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('action',choices=['show','record']);p.add_argument('kind',choices=['title','body']);p.add_argument('start',type=int);p.add_argument('--count',type=int,default=150);p.add_argument('--codes');p.add_argument('--spec')
    a=p.parse_args()
    if a.action=='show':show(a.kind,a.start,a.count)
    else:
        if a.spec:
            explicit={}
            for item in a.spec.split(','):
                address,code=item.strip().split(':');ends=list(map(int,address.split('-')))
                for i in range(ends[0],ends[-1]+1):
                    assert i not in explicit,'Duplicate explicit decision'
                    explicit[i]=code
            assert set(explicit)==set(range(a.start,a.start+a.count)),'Decision gap or extra address'
            a.codes=' '.join(explicit[i] for i in sorted(explicit))
        record(a.kind,a.start,a.codes)
