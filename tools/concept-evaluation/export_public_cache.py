"""Read-only public posting export. No account/workflow/classification/model data is copied."""
import argparse
import json
from pathlib import Path

FIELDS = ('stableId', 'requisitionId', 'title', 'companyId', 'primaryLocation', 'additionalLocations',
          'descriptionHtml', 'compressedDescriptionHtml', 'sourceUrl', 'isSourceAvailable', 'detailCachedAtUtc')


def export(source, destination):
    destination = Path(destination)
    destination.mkdir(parents=True, exist_ok=False)
    output = destination / 'job-caches'
    output.mkdir()
    count = 0
    for index, path in enumerate(sorted(Path(source).glob('**/job-caches/**/*.json'))):
        jobs = json.loads(path.read_bytes()).get('jobs', [])
        public = [{k: j[k] for k in FIELDS if k in j} for j in jobs]
        with (output / f'cache-{index:03d}.json').open('x', encoding='utf-8', newline='\n') as stream:
            json.dump({'jobs': public}, stream, ensure_ascii=False)
        count += len(jobs)
    return count


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--cache-root', required=True)
    parser.add_argument('--destination', required=True)
    args = parser.parse_args()
    print(json.dumps({'exportedCachedCopies': export(args.cache_root, args.destination)}))
