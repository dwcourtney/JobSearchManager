"""Offline verifier/export regression tests. Optional real archive path in PHASE1_ARCHIVE."""
import copy, importlib.util, json, os, pathlib, unittest
spec=importlib.util.spec_from_file_location('exporter',pathlib.Path(__file__).with_name('export.py'))
exporter=importlib.util.module_from_spec(spec);spec.loader.exec_module(exporter)
class ExportTests(unittest.TestCase):
    def setUp(self):
        root=pathlib.Path(__file__).parent
        self.seed=exporter.seed_rules(exporter.load(root.parent.parent/'LegacyJobConceptRules.json'))
        self.live=[{k:r[k[0].lower()+k[1:]] for k in exporter.FIELDS} for r in exporter.load(root/'sqlite-effective-rules.json')['rules']]
    def test_all_matching_fields_and_ids_exact(self):
        self.assertEqual(exporter.compare(self.seed,self.live)['effectiveRules'],288)
    def test_each_field_change_stops_export(self):
        for key in exporter.FIELDS:
            live=copy.deepcopy(self.live);live[0][key]=(live[0][key] or '')+'changed'
            with self.assertRaises(ValueError,msg=key):exporter.compare(self.seed,live)
    def test_missing_added_duplicate_stop(self):
        for live in [self.live[:-1],self.live+self.live[:1]]:
            with self.assertRaises((ValueError,AssertionError)):exporter.compare(self.seed,live)
    def test_matcher_types_and_execution_order(self):
        d=exporter.load(pathlib.Path(__file__).with_name('sqlite-effective-rules.json'));rules=d['rules']
        self.assertEqual([r['executionIndex'] for r in rules],list(range(288)))
        self.assertEqual(rules,sorted(rules,key=lambda r:(r['conceptId'],r['ruleType'],r['ruleId'])))
        self.assertEqual(sum(r['matcher']['kind']=='regex' for r in rules),263)
        self.assertEqual(sum(r['matcher']['kind']=='parsed-fact' for r in rules),25)
        for r in rules:
            if r['matcher']['kind']=='regex':self.assertEqual(r['pattern'],r['matcher']['pattern'])
            elif r['ruleType']!='remote-designation':self.assertEqual(r['pattern'],r['matcher']['category'])
        self.assertEqual(sorted(map(len,d['contextGroups'].values())),[2,2,2])
        self.assertTrue(all('matchCountLifetime' not in r and 'status' not in r for r in rules))
    @unittest.skipUnless(os.environ.get('PHASE1_ARCHIVE'),'private archive not provided')
    def test_complete_archive_integrity_and_history_roundtrip(self):
        m=exporter.verify(pathlib.Path(os.environ['PHASE1_ARCHIVE']))
        self.assertEqual(len(m['tableCounts']),8)
if __name__=='__main__':unittest.main()
