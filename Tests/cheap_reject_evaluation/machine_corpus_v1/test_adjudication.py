import hashlib
import json
from pathlib import Path
import unittest

ROOT=Path(__file__).resolve().parent
def read(name):return [json.loads(s) for s in (ROOT/name).read_text(encoding='utf-8').splitlines()]

class AdjudicationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source=read('sample-unlabeled.jsonl')
        cls.rows=read('machine-labeled.jsonl')
        cls.byid={r['id']:r for r in cls.rows}

    def test_exact_coverage_and_source_preservation(self):
        self.assertEqual(len(self.rows),3198)
        self.assertEqual(len(self.byid),3198)
        self.assertEqual(set(self.byid),{r['id'] for r in self.source})
        for s in self.source:
            for k,v in s.items():self.assertEqual(self.byid[s['id']][k],v)

    def test_decision_schema_and_confidence_boundaries(self):
        for r in self.rows:
            self.assertIn(r['label'],['KEEP','REJECT','AMBIGUOUS'])
            self.assertIn(r['confidence'],['high','medium','low'])
            self.assertIn(r['basis'],['title','duties','both'])
            self.assertTrue(r['reason'] and r['category'])
            self.assertEqual(r['inputContentSha256'],r['contentSha256'])
            if not r['bodyAvailable']:
                self.assertEqual(r['basis'],'title')
                self.assertNotEqual(r['confidence'],'high')
                self.assertIsNone(r['flags']['titleBodyConflict'])
            if r['label']=='AMBIGUOUS':self.assertEqual(r['confidence'],'low')

    def test_evidence_offsets_are_real_source_text(self):
        for r in self.rows:
            for e in r['reviewedEvidence']:
                self.assertTrue(0<=e['start']<=e['end']<=len(r['body']))
                self.assertEqual(r['body'][e['start']:e['end']],e['text'])

    def test_manifest_hashes(self):
        m=json.loads((ROOT/'adjudication-manifest.json').read_text(encoding='utf-8'))
        for name,digest in m['sha256'].items():
            self.assertEqual(hashlib.sha256((ROOT/name).read_bytes()).hexdigest(),digest,name)

    def test_review_queue_contains_borderlines_and_all_conflicts(self):
        q=read('human-review.jsonl');qids={r['id'] for r in q}
        self.assertGreaterEqual(sum(r['confidence']=='low' for r in q),100)
        self.assertEqual(len(qids),len(q))
        for r in self.rows:
            if r['flags']['titleBodyConflict'] or r['flags']['technicalJargonDutiesConflict'] or r['flags']['descriptionInsufficient']:
                self.assertIn(r['id'],qids)
        for r in q:self.assertEqual(r,self.byid[r['id']])

    def test_targeted_correction_preserves_audit(self):
        r=next(r for r in self.rows if r['title']=='Extended Workforce Solutions HR Project Manager')
        self.assertEqual(r['initialDecision']['label'],'REJECT')
        self.assertEqual(r['label'],'KEEP')
        self.assertEqual(r['basis'],'duties')
        self.assertTrue(r['flags']['titleBodyConflict'])
        r=next(r for r in self.rows if r['reviewPartition']=='body' and r['reviewOrdinal']==1258)
        self.assertEqual(r['label'],'REJECT')
        self.assertEqual(r['category'],'finance-accounting')

    def test_summary_reconciles(self):
        s=json.loads((ROOT/'label-summary.json').read_text())
        self.assertEqual(sum(s['labels'].values()),3198)
        self.assertEqual(sum(s['confidence'].values()),3198)
        self.assertEqual(s['titleOnlyMissingDescription'],1886)
        for label,n in s['labels'].items():self.assertEqual(sum(r['label']==label for r in self.rows),n)
        for available in [False,True]:
            group=[r for r in self.rows if r['bodyAvailable']==available]
            self.assertEqual(s['byDescription'][str(available)]['total'],len(group))
            for label,n in s['byDescription'][str(available)]['labels'].items():
                self.assertEqual(sum(r['label']==label for r in group),n)

    def test_review_ordinals_address_frozen_evidence(self):
        for partition,available in [('title',False),('body',True)]:
            ordered=sorted([r for r in self.source if r['bodyAvailable']==available],key=lambda r:(r['titleGroup'],r['employer'],r['id']))
            for d in read('codex-decisions.jsonl'):
                if d['reviewPartition']==partition:self.assertEqual(ordered[d['reviewOrdinal']]['id'],d['id'])

if __name__=='__main__':unittest.main()
