"""Validate completion, then benchmark in separate processes. No refits."""
import json, subprocess, sys
from pathlib import Path
root=Path(__file__).resolve().parent
runs=list((root/'output').glob('*-fold[012].json'));assert len(runs)==36,len(runs)
subprocess.run([sys.executable,str(root/'benchmark_rules.py'),'--root',str(root),'--evaluation-root','/tmp/jsm-deberta.dxVPsh/evaluation'],check=True)
for path in sorted(runs):
    r=json.loads(path.read_text())
    if r['fold']==2:
        subprocess.run([sys.executable,str(root/'benchmark.py'),'--root',str(root),'--tag',r['tag']],check=True)
with (root/'requirements-lock.txt').open('w') as f:subprocess.run([sys.executable,'-m','pip','freeze'],stdout=f,check=True)
print('BENCHMARKS COMPLETE',flush=True)
