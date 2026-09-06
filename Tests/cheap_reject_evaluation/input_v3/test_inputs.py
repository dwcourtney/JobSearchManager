"""Representation boundaries and label isolation, no model dependency."""
import random
import unittest
from inputs import encode, encode_text, first_heading, merge_ranges, select_ranges


class CharacterTokenizer:
    def encode(self,text,add_special_tokens=False):
        return [ord(c)+3 for c in text]

    def __call__(self,text,**kwargs):
        return dict(input_ids=self.encode(text),offset_mapping=[(i,i+1) for i in range(len(text))])

    def num_special_tokens_to_add(self,pair=False):
        return 2

    def build_inputs_with_special_tokens(self,ids):
        return [1]+ids+[2]


class InputTests(unittest.TestCase):
    def test_budget_title_preservation_and_unique_chronological_spans(self):
        tok=CharacterTokenizer()
        for view in ['prefix256','headtail256','headmidtail256','prefix384','prefix512','sections256']:
            for length in [0,1,10,100,250,256,300,500,2000]:
                body=('responsibilities: create things. qualifications: knowledge. '*100)[:length]
                feature,info=encode_text('Technical Support',body,tok,view)
                self.assertLessEqual(len(feature['input_ids']),int(view[-3:]))
                title=tok.encode('Title: Technical Support')
                self.assertEqual(feature['input_ids'][1:1+len(title)],title)
                spans=info['sourceRanges']
                self.assertEqual(spans,merge_ranges(spans))
                self.assertTrue(all(0<=s<e<=len(body) for s,e in spans))

    def test_gold_label_employer_invariance(self):
        tok=CharacterTokenizer();base=dict(title='Job',body='intro '*80+'responsibilities: duties '*80)
        for view in ['prefix256','headtail256','headmidtail256','prefix384','prefix512','sections256']:
            expected=encode(base,tok,view)
            poisoned={**base,'label':'REJECT','employer':'Software Company','reviewedEvidence':[dict(start=999,end=1000)],'reason':'anything'}
            self.assertEqual(expected,encode(poisoned,tok,view))

    def test_missing_body_and_title_overflow(self):
        tok=CharacterTokenizer()
        missing=[]
        for view in ['prefix256','headtail256','headmidtail256','prefix384','prefix512','sections256']:
            missing.append(encode_text('Job','',tok,view)[0])
            self.assertIsNone(encode_text('x'*600,'body',tok,view)[0])
        self.assertTrue(all(x==missing[0] for x in missing))

    def test_generic_heading_boundaries_and_fallback(self):
        self.assertEqual(first_heading('DUTIES: analyze',['duties']),0)
        self.assertIsNone(first_heading('predutiesXYZ',['duties']))
        offsets=[(i,i+1) for i in range(1000)]
        self.assertEqual(select_ranges('x'*1000,offsets,100,'sections',3),[(0,23),(476,523),(976,1000)])

    def test_randomized_span_budget(self):
        rng=random.Random(42)
        for _ in range(500):
            n=rng.randrange(1,3000);capacity=rng.randrange(1,600);gap=rng.randrange(1,10)
            body=('x'*rng.randrange(n)+' responsibilities qualifications '+'y'*n)[:n]
            offsets=[(i,i+1) for i in range(n)]
            for strategy in ['prefix','headtail','headmidtail','sections']:
                ranges=select_ranges(body,offsets,capacity,strategy,gap)
                self.assertLessEqual(sum(e-s for s,e in ranges)+max(0,len(ranges)-1)*gap,capacity)
                self.assertEqual(ranges,merge_ranges(ranges))


if __name__=='__main__':
    unittest.main()
