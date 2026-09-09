import argparse
import copy
import json
from pathlib import Path
import tempfile
import unittest
import evaluate as e
import label_codex
import export_public_cache


class EvaluationTests(unittest.TestCase):
    def test_confusion_and_micro(self):
        rows = [(True, True, .75), (False, True, .5), (True, False, 0), (False, False, 0)]
        m = e.metrics(rows)
        self.assertEqual([m[k] for k in ('tp', 'fp', 'fn', 'tn')], [1, 1, 1, 1])
        self.assertEqual([m[k] for k in ('precision', 'recall', 'f1')], [.5, .5, .5])
        self.assertEqual(e.metrics([])['f1'], 0)

    def test_threshold_ties_and_ap(self):
        result = e.curve([(True, True, .75), (False, True, .5), (True, True, .5), (False, False, 0)])
        self.assertEqual([p['threshold'] for p in result['points']], [None, .75, .5, 0])
        self.assertAlmostEqual(result['averagePrecision'], .5 + .5 * 2 / 3)
        self.assertEqual(result['points'][2]['tp'], 2)
        self.assertIsNone(e.curve([(False, False, 0)])['averagePrecision'])
        self.assertEqual(e.curve([(True, False, 0)])['averagePrecision'], 1)

    def test_score_dedup_context_and_range(self):
        policy = {'units': {'positive-evidence': 1, 'title-evidence': 2, 'required-context': 2}}
        rules = {str(i): dict(kind='positive-evidence', scope='both', pattern=str(i)) for i in range(100)}
        self.assertEqual(e.score([], rules, policy), 0)
        self.assertEqual(e.score(['0'], rules, policy), .5)
        self.assertEqual(e.score(['0', '0'], rules, policy), .5)
        rules['title'] = dict(kind='title-evidence', scope='title', pattern='title')
        self.assertEqual(e.score(['title'], rules, policy), .75)
        rules['c1'] = rules['c2'] = dict(kind='required-context', contextGroupId='one')
        self.assertEqual(e.score(['c1', 'c2'], rules, policy), .75)
        self.assertTrue(0 <= e.score(list(rules), rules, policy) <= 1)
        self.assertEqual(e.score(list(rules), rules, policy), e.score(list(reversed(rules)), rules, policy))

    def test_sampling_order_independent(self):
        rows = [dict(id=str(i), title='Engineer' if i % 2 else 'Analyst', company=str(i % 3), descriptionText='Remote travel software business') for i in range(100)]
        selected = e.sample_rows(rows, 30, 'fixed')
        self.assertEqual(selected, e.sample_rows(list(reversed(rows)), 30, 'fixed'))
        self.assertEqual(len({p['id'] for p in selected}), 30)
        self.assertNotEqual(selected, e.sample_rows(rows, 30, 'different'))

    def test_dedup_and_no_prediction_leakage(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            jobs = [dict(stableId='one', title='Engineer', companyId='test', descriptionHtml='<p>' + 'public text ' * 30 + '</p>', semanticClassification={'secret': 'MUST NOT LEAK'}, workflow='saved')]
            e.freeze(root / 'job-caches' / 'a.json', {'jobs': jobs})
            e.freeze(root / 'job-caches' / 'b.json', {'jobs': jobs + [{**jobs[0], 'stableId': 'two'}]})
            rows, stats, sources = e.population(root)
            self.assertEqual(len(rows), 1)
            self.assertEqual(stats['duplicateDescription'], 1)
            self.assertEqual(stats['duplicateIdOrWorkspaceCopy'], 1)
            self.assertNotIn('MUST NOT LEAK', json.dumps(rows))
            self.assertNotIn('workflow', json.dumps(e.payload(rows[0])))
            before = {p: p.read_bytes() for p in root.glob('job-caches/*.json')}
            public = root / 'public-export'
            self.assertEqual(export_public_cache.export(root / 'job-caches' / '..', public), 3)
            self.assertNotIn('MUST NOT LEAK', ''.join(p.read_text() for p in public.glob('job-caches/*.json')))
            self.assertEqual(before, {p: p.read_bytes() for p in before})

    def test_complete_blinded_workflow_and_freeze(self):
        with tempfile.TemporaryDirectory() as directory:
            run = Path(directory) / 'run'
            posting = dict(id='p', title='Engineer', company='c', primaryLocation='', additionalLocations=[], descriptionText='Remote work available')
            e.freeze(run / 'sample.json', {'postings': [posting]})
            e.freeze(run / 'taxonomy.json', {'concepts': [dict(id='c1', displayName='Remote', definition='Remote work'), dict(id='c2', displayName='Other', definition='Other work')]})
            e.freeze(run / 'sample-manifest.json', {'files': {f: e.digest((run / f).read_bytes()) for f in ('sample.json', 'taxonomy.json')}})
            for phase, labels in [('a', [True, False]), ('b', [False, False])]:
                e.export(argparse.Namespace(run=str(run), phase=phase, batch_size=5))
                batch_path = next((run / 'batches' / phase).glob('*.json'))
                batch = e.read(batch_path)
                self.assertNotIn('prediction', json.dumps(batch['postings']))
                response = dict(batchId=batch['batchId'], inputHash=batch['inputHash'], decisions=[dict(id='p', labels=labels, evidence={'c1': 'Remote work available'})])
                identity = dict(model='test-only', tool='fixture', sessionId=phase, startedUtc=e.now(), completedUtc=e.now())
                e.freeze(run / ('response-' + phase + '.json'), dict(identity=identity, response=response))
                e.import_labels(argparse.Namespace(run=str(run), phase=phase, batch=str(batch_path), response=str(run / ('response-' + phase + '.json'))))
                invalid = copy.deepcopy(response); invalid['decisions'][0]['labels'] = [True]
                with self.assertRaises(ValueError): e.validate_response(batch, invalid)
            with self.assertRaises(ValueError): e.reference(argparse.Namespace(run=str(run)))
            e.export(argparse.Namespace(run=str(run), phase='adjudication', batch_size=5))
            batch_path = next((run / 'batches' / 'adjudication').glob('*.json'))
            batch = e.read(batch_path)
            self.assertEqual(batch['postings'][0]['conceptIds'], ['c1'])
            self.assertNotIn('labels', batch['postings'][0])
            response = dict(batchId=batch['batchId'], inputHash=batch['inputHash'], decisions=[dict(id='p', labels=[None], evidence={})])
            identity = {**identity, 'sessionId': 'adjudication'}
            e.freeze(run / 'c.json', dict(identity=identity, response=response))
            e.import_labels(argparse.Namespace(run=str(run), phase='adjudication', batch=str(batch_path), response=str(run / 'c.json')))
            e.reference(argparse.Namespace(run=str(run)))
            self.assertEqual(e.read(run / 'reference-labels.json')['postings'][0]['labels'], [None, False])
            self.assertEqual(e.read(run / 'reference-labels.json')['unresolved'], 1)
            with self.assertRaises(FileExistsError): e.reference(argparse.Namespace(run=str(run)))
            with self.assertRaises(ValueError): e.export(argparse.Namespace(run=str(run), phase='a', batch_size=5))
            (run / 'sample.json').write_text('{}')
            with self.assertRaises(ValueError): e.verify(run)

    def test_tool_use_rejected(self):
        valid = '\n'.join(json.dumps(x) for x in [{'type': 'thread.started', 'thread_id': 's'}, {'type': 'item.completed', 'item': {'type': 'agent_message'}}, {'type': 'turn.completed'}])
        self.assertEqual(label_codex.audit(valid), 's')
        with self.assertRaises(ValueError): label_codex.audit(valid.replace('agent_message', 'command_execution'))

    def test_evaluate_publish_exact_metrics_and_immutable_history(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); run = root / 'fixture-only'
            sample = {'postings': [dict(id=str(i), title='Fixture', company='Test', descriptionText='Test fixture only') for i in range(3)]}
            taxonomy = {'concepts': [dict(id=c, displayName=c) for c in ['one', 'two']]}
            e.freeze(run / 'sample.json', sample); e.freeze(run / 'taxonomy.json', taxonomy)
            e.freeze(run / 'sample-manifest.json', {'files': {f: e.digest((run / f).read_bytes()) for f in ['sample.json', 'taxonomy.json']}})
            e.freeze(run / 'reference-labels.json', {'conceptIds': ['one', 'two'], 'unresolved': 1, 'sourceHashes': {}, 'postings': [dict(id=str(i), labels=v) for i, v in enumerate([[True, False], [False, True], [None, False]])]})
            e.freeze(run / 'reference-seal.json', {'referenceHash': e.digest((run / 'reference-labels.json').read_bytes()), 'sampleHash': e.digest((run / 'sample.json').read_bytes())})
            e.freeze(run / 'predictions.json', {'sampleHash': e.digest((run / 'sample.json').read_bytes()), 'taxonomyHash': e.digest((run / 'taxonomy.json').read_bytes()), 'authority': {'pipelineFingerprint': 'test-only'},
                'rules': [dict(ruleId='r', kind='positive-evidence', scope='both', pattern='x')],
                'postings': [dict(id=str(i), concepts=[dict(conceptId='one', evidence='x')], matchedRuleIds={'one': ['r']}) for i in range(3)]})
            policy = root / 'score.json'; metric = root / 'metric.json'
            e.freeze(policy, {'version': 'concept-evaluation-score-v1', 'formula': '1 - 2^(-units)', 'units': {'positive-evidence': 1, 'title-evidence': 2, 'required-context': 2, 'remote-designation': 2, 'remote-signal': 2, 'extended-location-signal': 2}, 'minimumPositiveSupport': 5, 'minimumNegativeSupport': 5})
            e.freeze(metric, {'version': 'concept-evaluation-metrics-v2', 'zeroDivision': 0})
            e.evaluate(argparse.Namespace(run=str(run), policy=str(policy), metric_policy=str(metric)))
            report = e.read(run / 'report.json'); summary = report['summary']
            self.assertEqual([summary[k] for k in ['totalPossible', 'resolved', 'unresolved']], [6, 5, 1])
            self.assertEqual(summary['micro']['f1'], .5)
            self.assertAlmostEqual(summary['macro']['f1'], 1/3)
            self.assertAlmostEqual(report['microCurve']['averagePrecision'], .45)
            self.assertTrue(all(c['curve'] is None for c in report['perConcept']))
            self.assertEqual(len(report['disagreements']), 3)
            destination = root / 'published'
            e.freeze(destination / 'index-v1.json', {'historical': 'preserved'})
            e.publish(argparse.Namespace(run=str(run), destination=str(destination)))
            self.assertEqual(e.read(destination / 'index-v1.json'), {'historical': 'preserved'})
            with self.assertRaises(ValueError): e.publish(argparse.Namespace(run=str(run), destination=str(destination)))
            (run / 'report.json').write_text('{}')
            with self.assertRaises(ValueError): e.publish(argparse.Namespace(run=str(run), destination=str(root / 'other')))


if __name__ == '__main__':
    unittest.main()
