"""Synthetic workflow tests; never invokes an LLM or uses fabricated real-run labels."""
import argparse
import copy
import json
from pathlib import Path
import tempfile
import unittest
import evaluate as ev
import improve_concept as imp


def posting(i):
    return dict(id=str(i),title='Engineer',company='example',primaryLocation='',additionalLocations=[],
                descriptionText='Automated software release pipelines '+str(i),descriptionHtml='Automated software release pipelines '+str(i),descriptionHash='hash-'+str(i))


def rule(rid,cid,order,pattern='release'):
    return dict(ruleId=rid,conceptId=cid,executionOrder=order,kind='positive-evidence',scope='both',pattern=pattern,description='fixture',provenance='fixture')


class ImprovementTests(unittest.TestCase):
    def test_deterministic_random_and_id_description_exclusion(self):
        rows=[posting(i) for i in range(20)]
        self.assertEqual(imp.random_rows(rows,10,'seed'),imp.random_rows(list(reversed(rows)),10,'seed'))
        self.assertNotEqual(imp.random_rows(rows,10,'seed'),imp.random_rows(rows,10,'new-seed'))
        duplicate=posting(99);duplicate['descriptionHash']=rows[0]['descriptionHash']
        self.assertNotIn(duplicate,imp.exclude(rows+[duplicate],[rows[0]]))
        self.assertEqual(len(imp.random_rows(rows,500,'seed')),20)

    def test_candidate_scope_ids_authority_and_canonical_order(self):
        base=dict(taxonomy='frozen',rules=[rule('a','a',0),rule('b','b',1)])
        cand=copy.deepcopy(base);cand['rules'][0]['pattern']='pipeline'
        self.assertEqual(imp.check_candidate(base,cand,'a'),['a'])
        for change in ['other','authority','remove','reassign','order','duplicate']:
            bad=copy.deepcopy(cand)
            if change=='other':bad['rules'][1]['pattern']='other'
            if change=='authority':bad['taxonomy']='changed'
            if change=='remove':bad['rules'].pop()
            if change=='reassign':bad['rules'][0]['conceptId']='b'
            if change=='order':bad['rules'][1]['executionOrder']=0
            if change=='duplicate':bad['rules'].append(copy.deepcopy(bad['rules'][0]))
            with self.subTest(change=change),self.assertRaises(ValueError):imp.check_candidate(base,bad,'a')
        added=copy.deepcopy(base);added['rules'].insert(1,rule('aa','a',1,'build'));added['rules'][2]['executionOrder']=2
        self.assertEqual(imp.check_candidate(base,added,'a'),['aa'])

    def test_concept_only_batches_and_frozen_inputs(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);taxonomy=root/'catalog.json'
            ev.freeze(taxonomy,dict(version=9,concepts=[dict(id='a',displayName='A',definition='Automated release pipelines'),dict(id='b',displayName='B',definition='Other')]))
            folder=imp.snapshot(root,'discovery',[posting(1)],'seed',500,taxonomy,'a')
            ev.export(argparse.Namespace(run=str(folder),phase='a',batch_size=10))
            batch=ev.read(next((folder/'batches/a').glob('*.json')))
            self.assertEqual([c['id'] for c in batch['concepts']],['a'])
            self.assertEqual(batch['postings'][0]['conceptIds'],['a'])
            self.assertNotIn('prediction',batch['postings'][0])
            bad=copy.deepcopy(batch);bad['postings'][0]['prediction']=True
            with self.assertRaises(ValueError):ev.verify_batch(folder,bad,'a')
            (folder/'full-taxonomy.json').write_text('{}')
            with self.assertRaises(ValueError):ev.verify(folder)

    def test_result_counts_unresolved_scores_and_ap(self):
        sample=dict(postings=[posting(i) for i in range(5)])
        refs=dict(conceptIds=['a'],postings=[dict(id=str(i),labels=[y]) for i,y in enumerate([True,True,False,False,None])])
        pred=dict(rules=[rule('a','a',0)],postings=[dict(id=str(i),concepts=[dict(conceptId='a',evidence='release')] if yes else [],matchedRuleIds={'a':['a']} if yes else {}) for i,yes in enumerate([True,False,True,False,True])])
        result=imp.result_for(sample,refs,pred,'a',dict(units={'positive-evidence':1}))
        self.assertEqual(result['resolved'],4);self.assertEqual(result['unresolved'],1)
        self.assertEqual([result['metrics'][k] for k in ['tp','fp','fn','tn']],[1,1,1,1])
        self.assertEqual(result['metrics']['f1'],.5);self.assertEqual(result['curve']['averagePrecision'],.5)
        self.assertEqual(result['scoreDistribution'],{'0.5':2,'0':2})
        pred['postings'][0]['matchedRuleIds']={}
        with self.assertRaises(ValueError):imp.result_for(sample,refs,pred,'a',dict(units={'positive-evidence':1}))

    def test_candidate_seal_and_validation_disjoint_and_one_time(self):
        with tempfile.TemporaryDirectory() as tmp:
            run=Path(tmp);taxonomy=run/'full-taxonomy.json';ev.freeze(taxonomy,dict(version=9,concepts=[dict(id='a',displayName='A',definition='Automated release pipelines')]))
            base=dict(rules=[rule('a','a',0)]);ev.freeze(run/'baseline-rules.json',base);(run/'baseline-rules.schema.json').write_text('{}')
            ev.freeze(run/'validation-pool.json',dict(postings=[posting(2),posting(3)]));ev.freeze(run/'mining-population.json',dict(postings=[]))
            ev.freeze(run/'round.json',dict(conceptId='a',files={f:ev.digest((run/f).read_bytes()) for f in ['full-taxonomy.json','baseline-rules.json','validation-pool.json','mining-population.json']}))
            imp.snapshot(run,'discovery',[posting(1)],'discovery',1,taxonomy,'a')
            with self.assertRaises(FileNotFoundError):imp.validation(argparse.Namespace(run=str(run),seed='validation',count=500))
            candidate=copy.deepcopy(base);candidate['rules'][0]['pattern']='pipelines'
            ev.freeze(run/'proposal.json',candidate)
            ev.freeze(run/'rationale.json',{'a':dict(sourcePhrases=['release pipelines'],supportingPostingIds=['1'],falsePositiveRisk='Generic pipeline ambiguity')})
            imp.seal(argparse.Namespace(run=str(run),candidate=str(run/'proposal.json'),rationale=str(run/'rationale.json')))
            ev.freeze(run/'discovery-candidate-results.json',dict(developmentOnly=True))
            imp.validation(argparse.Namespace(run=str(run),seed='validation',count=500))
            self.assertEqual(len(ev.read(run/'validation/sample.json')['postings']),2)
            self.assertFalse(set(p['id'] for p in ev.read(run/'validation/sample.json')['postings']) & {'1'})
            with self.assertRaises(FileExistsError):imp.validation(argparse.Namespace(run=str(run),seed='other',count=500))
            with self.assertRaises(ValueError):imp.seal(argparse.Namespace(run=str(run),candidate=str(run/'proposal.json'),rationale=str(run/'rationale.json')))
            (run/'candidate-rules.json').write_text('{}')
            with self.assertRaises(ValueError):imp.candidate_seal(run)


class DecisionTests(unittest.TestCase):
    def test_frozen_multimetric_gates_reject_precision_loss_and_require_review(self):
        from unittest.mock import patch
        policy=dict(minimumF1Gain=.05,minimumPrecision=.8,maximumPrecisionLoss=.03,minimumAPGain=0,minimumPositiveSupport=10,minimumNegativeSupport=10)
        for name,gain,precision,review,expected in [('pass',.08,.98,True,'ACCEPTABLE FOR RELEASE REVIEW'),('precision',.08,.90,True,'REJECT'),('gain',.04,.98,True,'REJECT'),('review',.08,.98,False,'NEEDS REVIEW')]:
            with self.subTest(name=name),tempfile.TemporaryDirectory() as tmp:
                run=Path(tmp)
                ev.freeze(run/'candidate-seal.json',{})
                ev.freeze(run/'validation-baseline-results.json',dict(metrics=dict(f1=.8,precision=1,support=20,negativeSupport=80),curve=dict(averagePrecision=.8),decisions=[]))
                ev.freeze(run/'validation-candidate-results.json',dict(metrics=dict(f1=.8+gain,precision=precision,support=20,negativeSupport=80),curve=dict(averagePrecision=.9),decisions=[]))
                ev.freeze(run/'review.json',dict(noUnexplainedRegression=review,noBroadFalsePositiveFamily=review))
                ev.freeze(run/'regression.json',dict(untouchedConceptChanges=0))
                with patch.object(imp,'verify_round',return_value=dict(policy=policy)),patch.object(imp,'candidate_seal',return_value={}):
                    imp.decision(argparse.Namespace(run=str(run),review=str(run/'review.json'),regression=str(run/'regression.json')))
                self.assertEqual(ev.read(run/'decision.json')['decision'],expected)
                self.assertFalse(ev.read(run/'decision.json')['productionSwitch'])


if __name__=='__main__':unittest.main()
