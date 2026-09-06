"""Read-only live progress for the running Linux experiment."""
import time
from pathlib import Path
root=Path(__file__).resolve().parent
last=''
while True:
 text=(root/'pipeline.log').read_text(errors='replace')
 lines=[x for x in text.splitlines() if x.startswith(('START ','DONE ','SKIP ','ALL COMPLETE','ARCHIVED','PIPELINE COMPLETE')) or ' epoch ' in x or 'benchmark PASS' in x or 'Error:' in x]
 current=lines[-1] if lines else 'GPU feasibility / evidence audit'
 print(time.strftime('%H:%M:%S'),current,flush=True)
 if 'PIPELINE COMPLETE' in text or 'Traceback (most recent call last)' in text:
  break
 time.sleep(45)
