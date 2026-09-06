"""Offline regression and artifact integrity checks; no model downloads."""
import gzip
import hashlib
import json
import math
import unittest
from pathlib import Path
from score_experiment import metrics, retains
from frontier import frontier

ROOT = Path(__file__).resolve().parent


class Decisions(unittest.TestCase):
    def test_threshold_equality_keeps(self):
        p = dict(failOpen=False, keepScore=.9)
        self.assertTrue(retains(p, 'paired', .9))
        self.assertFalse(retains(p, 'paired', .91))

    def test_neutral_is_not_negative_evidence(self):
        p = dict(failOpen=False, keepScore=.01, keepNli=[.01, .01, .98], rejectNli=[.01, .01, .98])
        self.assertTrue(retains(p, 'entailment-gate', .9))
        p['rejectNli'] = [.01, .98, .01]
        self.assertFalse(retains(p, 'entailment-gate', .9))
        p['keepNli'] = [.1, .8, .1]
        self.assertTrue(retains(p, 'entailment-gate', .9))

    def test_overflow_always_keeps(self):
        self.assertTrue(retains(dict(failOpen=True), 'paired', 1))
        self.assertTrue(retains(dict(failOpen=True), 'entailment-gate', .7))

    def test_false_reject_metrics(self):
        m = metrics([dict(label=x) for x in ['KEEP', 'KEEP', 'REJECT']], [True, False, False])
        self.assertEqual(m['keepRecall'], .5)
        self.assertEqual(m['falseRejects'], 1)
        self.assertEqual(m['rejectPrecision'], .5)
        with self.assertRaises(ValueError):
            metrics([], [True])


class Artifacts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.raw = json.loads((ROOT/'predictions.json').read_text())
        cls.scored = json.loads((ROOT/'metrics-extended.json').read_text())

    def test_fingerprints_and_label_free_inputs(self):
        raw_input = gzip.decompress((ROOT/'input.json.gz').read_bytes())
        self.assertEqual(hashlib.sha256(raw_input).hexdigest(), self.raw['inputSha256'])
        self.assertEqual(hashlib.sha256((ROOT/'run_deberta.py').read_bytes()).hexdigest(), self.raw['scriptSha256'])
        self.assertEqual(hashlib.sha256((ROOT/'score_experiment.py').read_bytes()).hexdigest(), self.scored['scorerSha256'])
        self.assertEqual(hashlib.sha256((ROOT/'predictions.json').read_bytes()).hexdigest(), self.scored['predictionSha256'])
        self.assertEqual(self.raw['modelWeightSha256'], self.raw['protocol']['expected_weight_sha256'])
        rows = json.loads(raw_input)
        self.assertEqual(len(rows), 1563)
        self.assertTrue(all(set(r) == {'id', 'title', 'body'} for r in rows))
        ids = {r['id'] for r in rows}
        for data in self.raw['views'].values():
            self.assertEqual({p['id'] for p in data['predictions']}, ids)
            for p in data['predictions']:
                self.assertTrue(math.isfinite(p['keepScore']))
                self.assertLessEqual(p['pairTokens'], 512)

    def test_baseline_and_selected_results(self):
        self.assertEqual(self.scored['baseline']['cache']['rejected'], 208)
        self.assertEqual(len(self.scored['configurations']), 72)
        for choice in self.scored['choices'].values():
            self.assertEqual(choice['cache']['rejected'], 61)
            self.assertEqual(choice['reviewed']['falseRejects'], 0)
        gates = [c for c in self.scored['configurations'] if c['mode']=='entailment-gate']
        self.assertEqual(len(gates), 15)
        self.assertTrue(all(c['cache']['rejected']==0 for c in gates))

    def test_frontier_replay(self):
        self.assertEqual(frontier(self.raw, ROOT.parent), json.loads((ROOT/'frontier.json').read_text()))


if __name__ == '__main__':
    unittest.main()
