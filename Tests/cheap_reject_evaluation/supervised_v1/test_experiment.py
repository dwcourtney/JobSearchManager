import hashlib
import gzip
import json
import math
from pathlib import Path
import unittest
from score import choose_threshold, metrics

ROOT=Path(__file__).resolve().parent

class Decisions(unittest.TestCase):
    def test_equality_keeps(self):
        rows=[dict(id='k',label='KEEP'),dict(id='r',label='REJECT')]
        self.assertEqual(metrics(rows,{'k':.2,'r':.1},.2)['falseRejects'],0)
        self.assertEqual(metrics(rows,{'k':.2,'r':.1},.2)['rejectPrecision'],1)

    def test_validation_only_boundary(self):
        rows=[dict(id=str(i),label='KEEP') for i in range(100)]
        scores={str(i):i/100 for i in range(100)}
        self.assertEqual(choose_threshold(rows,scores,.99),.01)
        self.assertEqual(choose_threshold(rows,scores,.98),.02)
        self.assertEqual(choose_threshold(rows,scores,1),0)

class FrozenData(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.split=json.loads((ROOT/'splits.json').read_text())
        cls.rows={r['id']:r for r in map(json.loads,(ROOT/'corrected.jsonl').read_text(encoding='utf-8').splitlines())}

    def test_one_explicit_correction(self):
        self.assertEqual(len(self.rows),347)
        self.assertEqual(sum(r['label']=='KEEP' for r in self.rows.values()),150)
        self.assertEqual(self.rows['d44a1bd2c6c20832']['label'],'KEEP')
        self.assertEqual(len(self.split['corrections']),1)

    def test_source_and_corrected_hashes(self):
        self.assertEqual(hashlib.sha256((ROOT/'corrected.jsonl').read_bytes()).hexdigest(),self.split['correctedSha256'])
        for name,digest in self.split['sourceHashes'].items():
            self.assertEqual(hashlib.sha256((ROOT.parent/name).read_bytes()).hexdigest(),digest)

    def test_split_isolation(self):
        evaluated=[]
        for f in self.split['folds']:
            part=f['partitions'];evaluated.extend(part['evaluation'])
            for a,b in [('train','validation'),('train','evaluation'),('validation','evaluation')]:
                self.assertFalse(set(part[a])&set(part[b]))
                self.assertFalse(set(f['companies'][a])&set(f['companies'][b]))
                self.assertFalse({self.rows[i]['titleGroup'].casefold() for i in part[a]}&{self.rows[i]['titleGroup'].casefold() for i in part[b]})
        self.assertEqual(len(evaluated),len(set(evaluated)))
        self.assertEqual(set(evaluated),set(self.rows))

    def test_prediction_provenance(self):
        raw=json.loads((ROOT/'predictions.json').read_text())
        self.assertEqual(len(raw['runs']),12)
        self.assertEqual(raw['scriptSha256'],hashlib.sha256((ROOT/'train.py').read_bytes()).hexdigest())
        self.assertEqual(raw['splitSha256'],hashlib.sha256((ROOT/'splits.json').read_bytes()).hexdigest())
        for r in raw['runs']:
            f=self.split['folds'][r['fold']]
            cache={p['id']:p for p in r['cache']}
            self.assertTrue(set(f['partitions']['evaluation'])<=set(cache))
            self.assertFalse(set(f['partitions']['train'])&set(cache))
            self.assertFalse(set(f['partitions']['validation'])&set(cache))
            self.assertEqual({p['id'] for p in r['validation']},set(f['partitions']['validation']))
            self.assertTrue(all(math.isfinite(p['keepScore']) and 0<=p['keepScore']<=1 for p in cache.values()))
            self.assertEqual(r['selectedEpoch'],min(r['history'],key=lambda h:h['validationCrossEntropy'])['epoch'])

    def test_all_cache_predictions_exclude_employer(self):
        raw=json.loads((ROOT/'predictions.json').read_text())
        pool={r['id']:r for r in json.loads(gzip.decompress((ROOT.parent/'deberta_v1/eligible.json.gz').read_bytes()))}
        for model in raw['protocol']['models']:
            for view in raw['protocol']['views']:
                seen=[]
                for run in [r for r in raw['runs'] if r['model']==model and r['view']==view]:
                    f=self.split['folds'][run['fold']]
                    for prediction in run['cache']:
                        seen.append(prediction['id'])
                        company=pool[prediction['id']]['company']
                        self.assertIn(company,f['companies']['evaluation'])
                        self.assertNotIn(company,f['companies']['train']+f['companies']['validation'])
                self.assertEqual(len(seen),1503)
                self.assertEqual(set(seen),set(pool))

    def test_threshold_and_archive_manifest(self):
        raw=json.loads((ROOT/'predictions.json').read_text())
        scored=json.loads((ROOT/'results.json').read_text())
        self.assertEqual(scored['scorerSha256'],hashlib.sha256((ROOT/'score.py').read_bytes()).hexdigest())
        self.assertEqual(scored['predictionSha256'],hashlib.sha256((ROOT/'predictions.json').read_bytes()).hexdigest())
        archive=json.loads((ROOT/'checkpoint-manifest.json').read_text())
        self.assertEqual(len(archive['checkpoints']),12)
        for run in raw['runs']:
            saved=next(c for c in archive['checkpoints'] if c['tag']==run['tag'])
            self.assertEqual(saved['sha256'],run['checkpointSha256'])
            candidate=next(c for c in scored['candidates'] if c['model']==run['model'] and c['view']==run['view'])
            val=[self.rows[p['id']] for p in run['validation']]
            probabilities={p['id']:p['keepScore'] for p in run['validation']}
            for o in candidate['operatingPoints']:
                self.assertEqual(o['thresholds'][str(run['fold'])],choose_threshold(val,probabilities,o['validationTargetRecall']))

if __name__=='__main__':unittest.main()
