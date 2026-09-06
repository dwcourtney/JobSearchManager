"""Positional context for the evidence audit; never used by input selection."""
import json
import statistics
from pathlib import Path

root=Path(__file__).resolve().parent
rows=[json.loads(x) for x in (root/'corpus.jsonl').read_text(encoding='utf8').splitlines()]
rows=[r for r in rows if r['bodyAvailable'] and r['label']!='AMBIGUOUS']
ends=[max(s['end'] for s in r['reviewedEvidence'])/len(r['body']) for r in rows]
result=dict(n=len(rows),medianBodyCharacters=statistics.median(len(r['body']) for r in rows),
            medianLastEvidenceRelativePosition=statistics.median(ends),
            allEvidenceInFirstHalf=sum(e<=.5 for e in ends),
            allEvidenceInFirstQuarter=sum(e<=.25 for e in ends),
            note='Recorded adjudication evidence is not exhaustive relevant content. Positional summary is audit only.')
(root/'evidence-position-context.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf8')
