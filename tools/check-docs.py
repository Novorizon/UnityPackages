#!/usr/bin/env python3
"""Check local documentation links and published sample declarations."""
import json
from pathlib import Path
import re
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]


def main():
    errors = []
    documents = [ROOT / 'README.md', *(ROOT / 'Documentation').glob('*.md'),
                 *(ROOT / 'Packages').rglob('*.md'), *(ROOT / 'tools').rglob('README.md')]
    for document in documents:
        if any(part in {'bin', 'obj', 'Fixtures', 'SourceGenerators~'} for part in document.relative_to(ROOT).parts):
            continue
        for number, line in enumerate(document.read_text().splitlines(), 1):
            for target in re.findall(r'\]\(([^)]+)\)', line):
                target = target.strip().split(' "', 1)[0].strip('<>')
                parsed = urlsplit(target)
                if parsed.scheme or parsed.netloc or not parsed.path:
                    continue
                if not (document.parent / unquote(parsed.path)).exists():
                    errors.append(f'{document.relative_to(ROOT)}:{number}: missing {target}')
    samples = 0
    for manifest in (ROOT / 'Packages').glob('*/package.json'):
        for sample in json.loads(manifest.read_text()).get('samples', []):
            path = manifest.parent / sample['path']
            if not path.is_dir() or not (path / 'README.md').is_file():
                errors.append(f'{manifest.relative_to(ROOT)}: incomplete sample {sample["path"]}')
            samples += 1
    if errors:
        raise SystemExit('\n'.join(errors))
    print(f'PASS documentation links and {samples} declared samples')


if __name__ == '__main__':
    main()
