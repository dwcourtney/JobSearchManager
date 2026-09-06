import hashlib,json,unittest
from pathlib import Path
import evaluate as base
from leakage import HERE,VARIANTS,decide,audit_estimates,sample

class LeakageTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.audit=base.load_rows(HERE/'leakage-audit.jsonl');cls.delta=base.load_rows(HERE/'leakage-delta-audit.jsonl')
        cls.dev=base.load_rows(HERE/'development.jsonl');cls.fixtures=base.load_rows(HERE/'fixtures.jsonl')
        cls.by_id={r['id']:r for r in cls.audit+cls.delta}
        cls.results=json.loads((HERE/'leakage-results.json').read_text(encoding='utf-8'))

    def test_audit_is_frozen_and_delta_disjoint(self):
        m=json.loads((HERE/'leakage-manifest.json').read_text(encoding='utf-8'))
        for name in ['leakage-audit.jsonl','leakage-delta-audit.jsonl']:
            self.assertEqual(hashlib.sha256((HERE/name).read_bytes()).hexdigest(),m[name+'Sha256'])
        self.assertFalse({r['id'] for r in self.audit}&{r['id'] for r in self.delta})
        self.assertEqual(len(self.audit),196);self.assertEqual(len(self.delta),41)

    def test_financial_system_support_and_field_engineering_rescued(self):
        for identity in ['d44a1bd2c6c20832','a641029d359d4837']:
            row=self.by_id[identity];self.assertFalse(decide(row)[0]);self.assertTrue(decide(row,'electrical-safe')[0])

    def test_safety_only_never_discards_baseline_keep(self):
        for r in self.audit+self.delta+self.dev+self.fixtures:
            if decide(r)[0]:self.assertTrue(decide(r,'safety')[0],r['title'])

    def test_noise_match_removed_but_second_real_match_survives(self):
        row=dict(title='Recruiter',body='Build strong talent pipelines and recruit top-tier cloud software candidates. Recruit candidates and conduct hiring interviews.')
        self.assertTrue(decide(row)[0]);self.assertFalse(decide(row,'body-context')[0])
        row['body']+=' Develop software APIs and deploy applications.'
        self.assertTrue(decide(row,'body-context')[0])

    def test_civil_exception_requires_specific_body_and_preserves_software(self):
        row=dict(title='Civil Engineer',body='Responsibilities are unspecified.')
        self.assertTrue(decide(row,'electrical-safe')[0])
        row['body']='Design bridge foundations and construction drawings.'
        self.assertFalse(decide(row,'electrical-safe')[0])
        row['body']+=' Develop software for bridge modeling.'
        self.assertTrue(decide(row,'electrical-safe')[0])

    def test_electrical_lighting_control_boundaries(self):
        for title in ['Roadway Lighting Engineer','Civil Electrical Engineer','Structural Controls Engineer','Roadway Signal Engineer','Bridge Automation Engineer']:
            self.assertTrue(decide(dict(title=title,body='Design bridge foundations and electrical controls.'),'electrical-safe')[0],title)
        row=next(r for r in self.delta if r['label']=='KEEP')
        self.assertFalse(decide(row,'civil-context')[0]);self.assertTrue(decide(row,'electrical-safe')[0])

    def test_company_descriptor_does_not_change_procurement_into_systems_job(self):
        row=dict(title='Procurement Agent - Millennium Space Systems',body='Negotiate procurement contracts and supplier purchase orders.')
        self.assertTrue(decide(row)[0]);self.assertFalse(decide(row,'electrical-safe')[0])
        row['title']='Procurement Systems Analyst';row['body']+=' Configure ERP modules and troubleshoot SQL systems.'
        self.assertTrue(decide(row,'electrical-safe')[0])

    def test_rejected_aggressive_variant_records_real_error(self):
        row=self.by_id['c28c937c0128cf79']
        self.assertTrue(decide(row,'electrical-safe')[0]);self.assertFalse(decide(row,'aggressive-commercial')[0])

    def test_same_frozen_development_and_contrast_cases(self):
        for row in self.dev:
            if row['label']=='KEEP':self.assertTrue(decide(row,'electrical-safe')[0],row['title'])
        for row in self.fixtures:self.assertEqual(decide(row,'electrical-safe')[0],row['label']=='KEEP',row['title'])

    def test_report_metrics_replay_for_every_variant(self):
        for variant in VARIANTS:
            for name,rows in [('development',self.dev),('audit',self.audit),('deltaAudit',self.delta),('synthetic',self.fixtures)]:
                self.assertEqual(base.metrics(rows,[decide(r,variant)[0] for r in rows]),self.results['variants'][variant][name])

    def test_design_weights_not_raw_oversampled_counts(self):
        e=audit_estimates(self.audit)
        self.assertAlmostEqual(e['estimatedUnrelatedKept'],331.25714285714287)
        self.assertAlmostEqual(e['estimatedFalseRejects'],130*2/60)
        for s in e['strata']:
            rows=[r for r in self.audit if r['auditStratum']==s]
            self.assertAlmostEqual(sum(r['weight'] for r in rows),rows[0]['stratumPopulation'])

    def test_every_new_reject_has_a_review_and_no_known_false_reject(self):
        flips=[r for r in self.results['transitions'] if r['variant']=='electrical-safe' and r['baselineKeep'] and not r['keep']]
        self.assertEqual(len(flips),81)
        for r in flips:self.assertEqual(self.by_id[r['id']]['label'],'REJECT',r['title'])
        for r in self.audit+self.delta:
            if r['label']=='KEEP':self.assertTrue(decide(r,'electrical-safe')[0],r['title'])

    def test_label_disagreement_is_visible_not_silently_rewritten(self):
        old=next(r for r in self.dev if r['id']=='d44a1bd2c6c20832')
        self.assertEqual(old['label'],'REJECT');self.assertEqual(self.by_id[old['id']]['label'],'KEEP')
        self.assertEqual(len(self.results['labelDisagreements']),1)

if __name__=='__main__':unittest.main()
