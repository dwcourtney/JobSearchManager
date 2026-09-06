"""One-shot completion helper for this experiment, not a scheduled automation."""
import argparse,subprocess,sys,time
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--pid',type=int,required=True);a=p.parse_args()
root=Path(__file__).resolve().parent
while True:
    cmd=Path(f'/proc/{a.pid}/cmdline')
    if not cmd.exists() or b'/tmp/jsm-supervised-corpus-v2' not in cmd.read_bytes():break
    time.sleep(15)
subprocess.run([sys.executable,str(root/'finish.py')],check=True)
subprocess.run([sys.executable,str(root/'archive.py'),'--root',str(root),'--destination','/home/codex/jsm-lab/experiments/supervised-corpus-v2-20260905'],check=True)
print('VALIDATION AND ARCHIVE COMPLETE',flush=True)
