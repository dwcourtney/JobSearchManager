"""Offline invariants, leakage checks and scoring boundary tests."""
import hashlib,json,unittest
from pathlib import Path
from score import choose,metrics
ROOT=Path(__file__).resolve().parent
def read(n):return json.loads((ROOT/n).read_text(encoding='utf8'))
class ExperimentTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.rows=[json.loads(x) for x in (ROOT/'corpus.jsonl').read_text(encoding='utf8').splitlines()];cls.by={r['id']:r for r in cls.rows};cls.split=read('splits.json')
    def test_frozen_corpus_and_binary_counts(self):
        self.assertEqual(hashlib.sha256((ROOT/'corpus.jsonl').read_bytes()).hexdigest(),self.split['corpusSha256'])
        self.assertEqual(len(self.by),3198);binary=[r for r in self.rows if r['label']!='AMBIGUOUS'];self.assertEqual(len(binary),2669)
        self.assertTrue(all(r['confidence'] in ['high','medium'] for r in binary));self.assertEqual(sum(r['bodyAvailable'] for r in binary),1159)
    def test_employer_family_and_identity_disjoint(self):
        for f in self.split['folds']:
            p=f['partitions']
            for a,b in [('train','validation'),('train','evaluation'),('validation','evaluation')]:
                for field in ['id','employer','splitGroup','duplicateCluster','titleFamilyCluster','titleGroup']:
                    self.assertFalse({self.by[i][field] for i in p[a]} & {self.by[i][field] for i in p[b]},(f['fold'],field))
                self.assertFalse({self.by[i]['body'] for i in p[a] if self.by[i]['body']} & {self.by[i]['body'] for i in p[b] if self.by[i]['body']})
    def test_each_binary_row_evaluated_once_and_no_ambiguous_fit(self):
        ids=[i for f in self.split['folds'] for i in f['partitions']['evaluation']]
        self.assertEqual(len(ids),2669);self.assertEqual(len(set(ids)),2669)
        for f in self.split['folds']:
            for part in f['partitions'].values():self.assertTrue(all(self.by[i]['label']!='AMBIGUOUS' for i in part))
    def test_threshold_ties_and_recall_resolution(self):
        rows=[dict(id=str(i),label='KEEP') for i in range(100)];scores={str(i):i/100 for i in range(100)}
        self.assertEqual(choose(rows,scores,.99),.01)
        ts={i:.01 for i in scores};self.assertEqual(metrics(rows,scores,ts)['falseRejects'],1)
        self.assertEqual(choose(rows[:42],scores,.99),0);self.assertEqual(choose(rows[:42],scores,.98),0)
    def test_rule_baseline_historical_cache(self):
        b=read('baseline.json')['decisions'];self.assertEqual(sum(not r['keep'] for i,r in b.items() if i.startswith('legacy:')),208)
    def test_complete_runs_if_present(self):
        paths=list((ROOT/'output').glob('*-fold[012].json'))
        if not paths:self.skipTest('GPU results not transferred yet')
        self.assertEqual(len(paths),36)
        archive=read('checkpoint-manifest.json')['checkpoints'];self.assertEqual(len(archive),36)
        archived={r['tag']:r['sha256'] for r in archive}
        cache=[json.loads(x) for x in (ROOT/'cache.jsonl').read_text(encoding='utf8').splitlines()]
        allrows=self.rows+cache
        self.assertEqual(len(list((ROOT/'output').glob('*-fold2-benchmark.json'))),12)
        rb=read('output/rules-benchmark.json');self.assertEqual(rb['sourceHashes'],read('baseline.json')['sourceHashes'])
        for path in paths:
            r=json.loads(path.read_text());f=self.split['folds'][r['fold']];self.assertEqual({p['id'] for p in r['validation']},set(f['partitions']['validation']))
            self.assertEqual(r['checkpointSha256'],archived[r['tag']])
            expected={x['id'] for x in allrows if self.split['companyFold'][x['employer']]==r['fold']}
            self.assertEqual({p['id'] for p in r['predictions']},expected)
            self.assertEqual(len(r['predictions']),len(expected))
            self.assertEqual(r['selectedEpoch'],min(r['history'],key=lambda x:x['validationCrossEntropy'])['epoch'])
            self.assertEqual(r['protocolSha256'],hashlib.sha256((ROOT/'protocol.json').read_bytes()).hexdigest())
            self.assertEqual(r['scriptSha256'],hashlib.sha256((ROOT/'train.py').read_bytes()).hexdigest())
            self.assertEqual(r['truncation']['titleOverflows'],0)
            self.assertTrue(all(0<=p['keepScore']<=1 for p in r['predictions']))
            for p in r['predictions']:
                if p['id'] in self.by:self.assertEqual(self.split['companyFold'][self.by[p['id']]['employer']],r['fold'])
if __name__=='__main__':unittest.main()
