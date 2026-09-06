"""Validate completed research outputs and deterministically replay their reports."""
import ast
import hashlib
import json
import subprocess
import sys
from pathlib import Path

root=Path(__file__).resolve().parent


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(script):
    subprocess.run([sys.executable,'-B','-X','utf8',script,'--root',str(root)],cwd=root,check=True)


def main():
    for path in root.glob('*.py'):
        ast.parse(path.read_text(encoding='utf8'),filename=str(path))
    subprocess.run([sys.executable,'-B','-X','utf8','-m','unittest','test_inputs','test_experiment','-v'],cwd=root,check=True)
    # First normalize generated output to this runtime; then require byte-identical replay.
    run('score.py');run('report.py')
    names=['results.json','report.md','paired-error-analysis.json','selected-thresholds.csv',
           'diagnostic-frontiers.csv','fixed-threshold-sweep.csv','rule-matched-diagnostic.json']
    before={name:digest(root/name) for name in names}
    run('score.py');run('report.py')
    assert before=={name:digest(root/name) for name in names}
    failures=[]
    for path in root.rglob('*'):
        if path.is_file() and path.suffix in ['.py','.md','.json','.jsonl','.csv','.txt','.sh','.log']:
            for number,line in enumerate(path.read_text(encoding='utf8').splitlines(),1):
                if line.rstrip(' \t')!=line:
                    failures.append(f'{path.relative_to(root)}:{number}')
    assert not failures,failures[:20]
    result=dict(pythonSyntax='PASS',unitTests=13,reportReplay='byte-identical',
                reportHashes=before,whitespace='PASS',tokenizerValidation=json.loads((root/'tokenizer-validation.json').read_text()))
    (root/'artifact-validation.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf8')
    print('ARTIFACT VALIDATION PASS')


if __name__=='__main__':
    main()
