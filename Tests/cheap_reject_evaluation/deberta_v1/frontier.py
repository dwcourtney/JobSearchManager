"""Optimistic, label-informed paired-threshold frontier; not held-out validation."""
import argparse
import json
import math
from pathlib import Path
from score_experiment import load_rows, metrics, retains


def frontier(raw, root):
    dev = load_rows(root / 'development.jsonl')
    merged = {r['id']: r for r in dev}
    merged.update({r['id']: r for r in load_rows(root / 'leakage-audit.jsonl') + load_rows(root / 'leakage-delta-audit.jsonl')})
    datasets = {'development': dev, 'developmentCorrected': [merged[r['id']] for r in dev], 'reviewedUnique': list(merged.values())}
    synthetic = {r['id'] for r in load_rows(root / 'fixtures.jsonl')}
    result = []
    for view, data in raw['views'].items():
        predictions = {r['id']: r for r in data['predictions']}
        for recall in [.98, .99]:
            # Equality is KEEP: the (allowed false rejects + 1)th KEEP score is
            # the largest feasible threshold, including score ties.
            bounds = []
            for rows in datasets.values():
                scores = sorted(predictions[r['id']]['keepScore'] for r in rows if r['label'] == 'KEEP')
                bounds.append(scores[math.floor((1-recall)*len(scores)+1e-9)])
            threshold = min(bounds)
            cache = [p for p in predictions.values() if p['id'] not in synthetic]
            entry = dict(view=view, targetRecall=recall, threshold=threshold,
                         datasets={name: metrics(rows, [retains(predictions[r['id']], 'paired', threshold) for r in rows]) for name, rows in datasets.items()},
                         cacheRejected=sum(not retains(p, 'paired', threshold) for p in cache), cacheN=len(cache))
            entry['cacheRejectionRate'] = entry['cacheRejected']/len(cache)
            result.append(entry)
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--predictions', type=Path, required=True)
    parser.add_argument('--evaluation-root', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    results = frontier(json.loads(args.predictions.read_text(encoding='utf-8')), args.evaluation_root)
    args.output.write_text(json.dumps(results, indent=2)+'\n', encoding='utf-8')
    for r in results:
        print(r['view'], r['targetRecall'], r['threshold'], r['cacheRejected'], r['cacheRejectionRate'])
