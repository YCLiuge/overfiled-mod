"""Verify selected model-map entries and dependencies without changing the mapping."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import re
import zlib
from map_models import DEFAULT_GAME, WORKSPACE, parse_manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--map', type=Path, default=WORKSPACE/'artifacts/model-export-20261122/model-map.json')
    parser.add_argument('--game-data', type=Path, default=DEFAULT_GAME)
    parser.add_argument('--pattern', default=r'character_(?:0[1-9]|[12]\d|3\d)_skin')
    parser.add_argument('--output', type=Path, default=WORKSPACE/'artifacts/model-export-20261122/base-character-verification.json')
    args = parser.parse_args()
    models = json.loads(args.map.read_text(encoding='utf-8'))
    selected = [m for m in models if re.fullmatch(args.pattern, m['name'])]
    if not selected:
        raise ValueError('No matching models')
    manifest_path = max((args.game_data/'yoo/DefaultPackage/ManifestFiles').glob('*.bytes'), key=lambda p:p.name)
    manifest = parse_manifest(manifest_path)
    bundles = manifest['bundles']
    wanted = {}
    for m in selected:
        wanted[m['bundle_id']] = m['path']
        for d in m['dependency_records']:
            wanted[d['id']] = d['path']
    results = []
    for index, (bundle_id, filename) in enumerate(sorted(wanted.items()), 1):
        b = bundles[bundle_id]
        row = dict(bundle_id=bundle_id, bundle=b['name'], path=filename, valid=False)
        if filename and Path(filename).is_file():
            path = Path(filename)
            digest, crc = hashlib.md5(), 0
            with path.open('rb') as stream:
                for block in iter(lambda: stream.read(1024*1024), b''):
                    digest.update(block)
                    crc = zlib.crc32(block, crc)
            row.update(size=path.stat().st_size, md5=digest.hexdigest(), crc32=f'{crc:08x}')
            row['valid'] = (row['size'] == b['size'] and row['md5'] == b['hash']
                            and crc == int.from_bytes(bytes.fromhex(b['crc']), 'little'))
        results.append(row)
        print(f"{index}/{len(wanted)} {'OK' if row['valid'] else 'FAIL'} {b['name']}", flush=True)
    report = dict(manifest=manifest_path.as_posix(), model_count=len(selected),
                  model_names=[m['name'] for m in selected], unique_file_count=len(results),
                  total_bytes=sum(r.get('size', 0) for r in results),
                  valid_count=sum(r['valid'] for r in results), files=results)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k != 'files'}, ensure_ascii=True, indent=2))
    return 0 if all(r['valid'] for r in results) else 1


if __name__ == '__main__':
    raise SystemExit(main())
