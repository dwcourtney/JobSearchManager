"""Time the unchanged frozen rules on curiosity after training completes."""
import argparse,hashlib,json,statistics,sys,time
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--root',type=Path,required=True);p.add_argument('--evaluation-root',type=Path,required=True);a=p.parse_args()
base=json.loads((a.root/'baseline.json').read_text())
for n,d in base['sourceHashes'].items():assert hashlib.sha256((a.evaluation_root/n).read_bytes()).hexdigest()==d
sys.path.insert(0,str(a.evaluation_root));import leakage
rows=[json.loads(x) for n in ['corpus.jsonl','cache.jsonl'] for x in (a.root/n).read_text().splitlines()]
for r in rows:assert leakage.decide(r,'electrical-safe')[0]==base['decisions'][r['id']]['keep']
times=[]
for _ in range(5):
    start=time.perf_counter()
    for r in rows:leakage.decide(r,'electrical-safe')
    times.append(time.perf_counter()-start)
t=statistics.median(times)
(a.root/'output/rules-benchmark.json').write_text(json.dumps(dict(host='curiosity',n=len(rows),passes=5,medianSeconds=t,msPerPosting=1000*t/len(rows),postingsPerSecond=len(rows)/t,sourceHashes=base['sourceHashes']),indent=2)+'\n')
print('Rules parity and Linux timing PASS',flush=True)
