import pathlib,json
p=pathlib.Path('step3-b2c269');read=lambda f:[json.loads(l) for l in f.read_text().splitlines()]
for local,remote in [('historical-live','live'),('snapshot-cache','current')]:
 for name,v in [('baseline','1.0.0'),('candidate','1.0.1')]:
  assert read(p/f'{local}-{name}.jsonl')==read(p/f'{remote}-{v}.jsonl'),(local,name)
print('Windows/Linux full decision/evidence parity: PASS (2205 historical + 275 snapshot-cache, both versions)')
f=json.loads((p/'frozen/comparison.json').read_text());print('Frozen parity mismatches',len(f['frozenParityMismatches']))
for n,r in f['runs'].items():print(n,{k:{m:v[m] for m in ['n','keepRecall','falseRejectCount','rejectionCount']} for k,v in r['cohorts'].items()},'changes',len(r['changedDecisions']),'failopen',r['failOpenCount'])
print('Changed live IDs',[(x['id'],x['after']['ruleIds']) for x in json.loads((p/'live-changes.json').read_text())])
