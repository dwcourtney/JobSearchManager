"""Run clean-process diagnostics after all twelve fixed training runs complete."""
import json
import subprocess
import shutil
import sys
from pathlib import Path

root=Path(__file__).resolve().parent
raw=json.loads((root/'output/predictions.json').read_text())
assert len(raw['runs'])==12
diagnostics=[]
for run in raw['runs']:
    subprocess.run([sys.executable,str(root/'diagnose.py'),'--root',str(root),'--tag',run['tag']],check=True)
    diagnostics.append(json.loads((root/'output'/(run['tag']+'-diagnostics.json')).read_text()))
(root/'output/diagnostics.json').write_text(json.dumps(diagnostics,indent=2)+'\n')
shutil.copy2(root/'output/predictions.json',root/'predictions.json')
subprocess.run([sys.executable,str(root/'score.py'),'--root',str(root),'--pool','/tmp/jsm-cheap-reject-audit-20260905/eligible.json','--evaluation-root','/tmp/jsm-deberta.dxVPsh/evaluation','--output',str(root/'output/results.json')],check=True)
subprocess.run([sys.executable,'-m','pip','freeze'],stdout=(root/'requirements-lock.txt').open('w'),check=True)
print('All clean-process diagnostics complete',flush=True)
