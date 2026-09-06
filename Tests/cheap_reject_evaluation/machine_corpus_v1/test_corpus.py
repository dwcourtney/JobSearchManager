import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from make_exclusions import digest,normalize,make
from export_sources import export

ROOT=Path(__file__).resolve().parent

class ExclusionTests(unittest.TestCase):
    def test_holdout_index_exports_no_text_or_labels(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'holdout.json'
            p.write_text(json.dumps(dict(examples=[dict(title='Hidden job title',descriptionHtml='Hidden duty text',expectedPresent=True,companyId='company',requisitionId='123',sourceUrl='https://example.test/123')])))
            result=make([p]);serialized=json.dumps(result)
            self.assertNotIn('Hidden job title',serialized)
            self.assertNotIn('Hidden duty text',serialized)
            self.assertNotIn('expectedPresent',serialized)
            self.assertIn(digest('company\n123'),result['requisitionDigests'])

    def test_changed_title_still_excluded_by_requisition(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);holdout=root/'holdout.json';cache=root/'cache.json';index=root/'index.json';inventory=root/'inventory.json'
            holdout.write_text(json.dumps(dict(examples=[dict(title='Old title',companyId='company',requisitionId='123')])) )
            index.write_text(json.dumps(make([holdout])))
            cache.write_text(json.dumps(dict(jobs=[dict(title='Changed title',companyId='company',requisitionId='123',descriptionHtml='new text')])) )
            inventory.write_text(json.dumps([dict(path=str(cache),kind='job-cache',sha256=hashlib.sha256(cache.read_bytes()).hexdigest())]))
            rows,audit=export([inventory],[index]);self.assertEqual(rows,[])
            self.assertEqual(audit['counts']['excludedByIdentityOrTitle'],1)

class FrozenSampleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.rows=[json.loads(line) for line in (ROOT/'sample-unlabeled.jsonl').read_text(encoding='utf-8').splitlines()]
        cls.manifest=json.loads((ROOT/'sampling-manifest.json').read_text())

    def test_size_identity_and_missing_body_counts(self):
        self.assertEqual(len(self.rows),3198)
        self.assertEqual(len({r['id'] for r in self.rows}),3198)
        self.assertEqual(sum(r['bodyAvailable'] for r in self.rows),1312)
        self.assertTrue(all(r['selectionProbability']==1 for r in self.rows))

    def test_sample_hash_and_no_invented_labels(self):
        self.assertEqual(hashlib.sha256((ROOT/'sample-unlabeled.jsonl').read_bytes()).hexdigest(),self.manifest['sampleSha256'])
        self.assertTrue(all('label' not in r and 'confidence' not in r for r in self.rows))

    def test_exclusion_intersections_empty(self):
        indices=[json.loads((ROOT/name).read_text()) for name in ['production-holdout-exclusions.json','prior-evaluation-exclusions.json']]
        for index in indices:
            for row in self.rows:
                self.assertNotIn(digest(normalize(row['title'])),index['titleDigests'])
                self.assertNotIn(row['contentSha256'],index['contentDigests'])
                self.assertNotIn(digest(row['employer']+'\n'+str(row['requisitionId'])),index['requisitionDigests'])
                self.assertNotIn(digest(row['sourceUrl']),index['urlDigests'])
                if row['body']:
                    self.assertNotIn(digest(normalize(row['body'])),index['bodyDigests'])

if __name__=='__main__':unittest.main()
