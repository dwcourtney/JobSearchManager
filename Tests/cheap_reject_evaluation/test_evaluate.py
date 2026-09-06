import hashlib,json,tempfile,unittest
from pathlib import Path
import numpy as np
from sklearn.model_selection import StratifiedGroupKFold
from evaluate import HERE,SEED,decision,legacy_decision,legacy_patterns,load_rows,metrics,model
from export_pool import export,normalize
from sample import select

class EvaluationTests(unittest.TestCase):
    def test_metrics_count_false_rejects(self):
        rows=[{'label':x} for x in ['KEEP','KEEP','REJECT','REJECT']]
        self.assertEqual(metrics(rows,[True,False,False,True]),dict(n=4,keeps=2,falseRejects=1,rejected=2,keepRecall=.5,rejectionRate=.5,rejectPrecision=.5,negativeRecall=.5))
        self.assertIsNone(metrics(rows,[True]*4)['rejectPrecision'])
        self.assertIsNone(metrics([],[])['keepRecall'])
        with self.assertRaises(ValueError):metrics(rows,[])

    def test_protected_titles_survive_without_body(self):
        for title in ['Software Engineer','Developer','.NET Developer','C# Backend Engineer','Full Stack Developer','DevOps','DevSecOps','Platform Engineer','Cloud Administrator','AWS Engineer','Azure Administrator','Systems Administrator','Integration Engineer','AI Engineer','ML Engineer','NLP Engineer','Automation Technician','Network Technician','Infrastructure Engineer','Information Systems Security Officer','Server Administrator']:
            with self.subTest(title=title):self.assertTrue(decision(dict(title=title,body=''),'title-body-confirmed')[0])

    def test_body_support_and_uncertainty(self):
        self.assertTrue(decision(dict(title='Technician',body=''),'title-body-confirmed')[0])
        self.assertTrue(decision(dict(title='Technician',body='Repair equipment and configure Linux servers.'),'title-body-confirmed')[0])
        self.assertFalse(decision(dict(title='Recruiter',body='Source candidates for software teams.'),'title-body-confirmed')[0])
        self.assertFalse(decision(dict(title='Account Executive',body='Sell cloud software and meet revenue quotas.'),'title-body-confirmed')[0])

    def test_frozen_dataset_and_real_negative_coverage(self):
        manifest=json.loads((HERE/'manifest.json').read_text(encoding='utf-8'))
        for name in ['development.jsonl','fixtures.jsonl']:
            self.assertEqual(hashlib.sha256((HERE/name).read_bytes()).hexdigest(),manifest[name+'Sha256'])
        rows=load_rows(HERE/'development.jsonl')
        self.assertEqual(len(rows),166)
        self.assertEqual(len({r['titleGroup'] for r in rows}),166)
        self.assertEqual(sum(r['label']=='REJECT' for r in rows),81)
        self.assertTrue(all(r['labelReason'] and r['sourceKind']=='cached-public-posting' for r in rows))

    def test_company_folds_are_disjoint_and_predict_every_row_once(self):
        rows=load_rows(HERE/'development.jsonl'); y=np.array([r['label']=='KEEP' for r in rows]);groups=np.array([r['company'] for r in rows]);seen=[]
        for train,test in StratifiedGroupKFold(n_splits=5,shuffle=True,random_state=SEED).split(np.zeros(len(rows)),y,groups):
            self.assertFalse(set(groups[train]) & set(groups[test]));self.assertEqual(len(set(y[train])),2);seen.extend(test)
        self.assertEqual(sorted(seen),list(range(len(rows))))

    def test_vectorizer_fits_training_only(self):
        train=[dict(title='Software Engineer',body='Write software code.'),dict(title='Nurse',body='Treat patients.')]
        fitted=model().fit(train,[1,0]);fitted.predict_proba([dict(title='zqxheldouttoken',body='zqxheldouttoken')])
        self.assertNotIn('zqxheldouttoken',fitted.named_steps['features'].named_steps['tfidf'].vocabulary_)

    def test_contrastive_fixtures(self):
        for row in load_rows(HERE/'fixtures.jsonl'):
            with self.subTest(title=row['title']):self.assertEqual(decision(row,'title-body-confirmed')[0],row['label']=='KEEP')

    def test_exclusion_export_dedup_and_allowlisted_fields(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);cache=root/'workspaces/w/shared/job-caches/c';cache.mkdir(parents=True)
            jobs=[dict(title='Hidden title',descriptionHtml='secret holdout text',stableId='a'),dict(title='HIDDEN  TITLE',descriptionHtml='different body'),dict(title='Other title',descriptionHtml='body',stableId='a'),dict(title='Hash match',descriptionHtml='body',postingContentHash='hash'),dict(title='Visible',descriptionHtml='<p>Public &amp; text</p>',companyId='x',privateModelResult='never export')]
            (cache/'a.json').write_text(json.dumps(dict(jobs=jobs+jobs[-1:])),encoding='utf-8')
            manifest=root/'holdout.json';manifest.write_text(json.dumps(dict(examples=[dict(title='Hidden title',stableId='a',postingContentHash='hash')])),encoding='utf-8')
            rows,audit=export(root,[manifest]);self.assertEqual(len(rows),1);self.assertEqual(audit['excludedRecords'],4)
            self.assertEqual(rows[0]['body'],'public & text');self.assertNotIn('privateModelResult',rows[0]);self.assertNotIn('secret',json.dumps(rows))

    def test_selection_ignores_labels_and_is_order_independent(self):
        rows=[dict(id=str(i),title='Nurse',titleGroup='nurse',label='KEEP') for i in range(3)]
        rows.append(dict(id='4',title='Developer',titleGroup='developer',label='REJECT'))
        manifest=dict(seed='test',samplingFamilies={'clinical':'nurse','core-technical':'developer'})
        first=select(rows,manifest);second=select(list(reversed(rows)),manifest)
        self.assertEqual([r['id'] for r in first],[r['id'] for r in second]);self.assertEqual(len(first),2)

    def test_legacy_stage_one_port_boundary(self):
        import os
        source=Path(os.environ.get('JSM_SOURCE_ROOT',str(HERE.parents[1])))/'TriageEvaluation.cs'
        patterns=legacy_patterns(source)
        self.assertEqual([len(patterns[k]) for k in ['HardConflictSignals','PhysicalTitleSignals','Buckets']],[4,10,10])
        self.assertFalse(legacy_decision(dict(title='Systems Integration Engineer',body='Build shipboard software.'),patterns)[0])
        self.assertTrue(legacy_decision(dict(title='Registered Nurse',body='Treat patients.'),patterns)[0]) # stage two intentionally absent

if __name__=='__main__':unittest.main()
