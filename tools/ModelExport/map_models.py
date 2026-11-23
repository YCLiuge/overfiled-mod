"""Offline YooAsset 2.0.0 manifest parser and character model dependency mapper.
Only Python standard library; never loads game code or modifies game files.
"""
from __future__ import annotations
import argparse
import csv
import hashlib
import json
from pathlib import Path
import re
import struct
import zlib

DEFAULT_GAME = Path('D:/TapTap/PC Games/176228/launcher_Data')
WORKSPACE = Path(__file__).resolve().parents[2]

class Reader:
    def __init__(self, data):
        self.data, self.pos = data, 0
    def number(self, fmt):
        size = struct.calcsize('<' + fmt)
        result = struct.unpack_from('<' + fmt, self.data, self.pos)[0]
        self.pos += size
        return result
    def string(self):
        length = self.number('H')
        end = self.pos + length
        if end > len(self.data):
            raise ValueError('Truncated string')
        result = self.data[self.pos:end].decode('utf-8')
        self.pos = end
        return result
    def strings(self):
        return [self.string() for _ in range(self.number('H'))]
    def ints(self):
        return [self.number('i') for _ in range(self.number('H'))]

def parse_manifest(path):
    r = Reader(path.read_bytes())
    if r.number('I') != 0x594f4f:
        raise ValueError(f'Unexpected YooAsset manifest signature: {path}')
    version = r.string()
    if version != '2.0.0':
        raise ValueError(f'Unsupported manifest version: {version}')
    # Header: enable-addressable(bool), location-to-lower(bool),
    # include-asset-guid(bool), output-name-style(int32), then three strings.
    flags = [r.number('B') for _ in range(3)]
    output_style = r.number('i')
    pipeline, package, package_version = r.string(), r.string(), r.string()
    assets = []
    for index in range(r.number('i')):
        try:
            assets.append(dict(id=index, address=r.string(), asset_path=r.string(),
                               guid=r.string(), tags=r.strings(), bundle_id=r.number('i'), dependency_ids=[]))
        except Exception as exc:
            raise ValueError(f'Asset {index}, offset {r.pos}, previous {assets[-1:]}') from exc
    bundles = []
    for index in range(r.number('i')):
        bundles.append(dict(id=index, name=r.string(), raw_name_id=r.number('I'),
                            hash=r.string(), crc=r.string(), size=r.number('q'),
                            encrypted=r.number('B'), tags=r.strings(), dependency_ids=r.ints()))
    if r.pos != len(r.data):
        raise ValueError(f'Unparsed bytes: {len(r.data)-r.pos}, offset {r.pos}')
    for a in assets:
        if not 0 <= a['bundle_id'] < len(bundles):
            raise ValueError(f'Invalid asset bundle ID: {a}')
        if any(not 0 <= i < len(bundles) for i in a['dependency_ids']):
            raise ValueError(f'Invalid asset dependency ID: {a}')
    for b in bundles:
        if any(not 0 <= i < len(bundles) for i in b['dependency_ids']):
            raise ValueError(f'Invalid bundle dependency ID: {b}')
        if not re.fullmatch('[0-9a-f]{32}', b['hash']):
            raise ValueError(f'Invalid hash: {b}')
    return dict(path=path.as_posix(), format=version, flags=flags,
                output_style=output_style, pipeline=pipeline, package=package,
                version=package_version, assets=assets, bundles=bundles)

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--game-data', type=Path, default=DEFAULT_GAME)
    p.add_argument('--manifest', type=Path)
    p.add_argument('--output', type=Path, default=WORKSPACE/'artifacts/model-export-20261122')
    p.add_argument('--verify', action='store_true', help='Verify mapped files with MD5 and CRC32')
    args = p.parse_args()
    manifests = list((args.game_data/'yoo/DefaultPackage/ManifestFiles').glob('*.bytes'))
    manifest = args.manifest or max(manifests, key=lambda f: f.name)
    m = parse_manifest(manifest)
    print(f"Parsed {len(m['assets'])} assets, {len(m['bundles'])} bundles; consumed entire manifest")
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output/'manifest-records.json').write_text(json.dumps(m, ensure_ascii=False, indent=2), encoding='utf-8')
    bundles = m['bundles']
    builtin = args.game_data/'StreamingAssets/yoo/DefaultPackage'
    cache = args.game_data/'yoo/DefaultPackage/CacheFiles'
    verified = {}
    def resolve(b):
        if b['id'] in verified:
            return verified[b['id']]
        h = b['hash']
        candidates = [builtin/f'{h}.bundle', cache/h[:2]/h/'__data']
        found = None
        for path in candidates:
            if not path.is_file() or path.stat().st_size != b['size']:
                continue
            if args.verify:
                md5, crc = hashlib.md5(), 0
                with path.open('rb') as stream:
                    for block in iter(lambda: stream.read(1024*1024), b''):
                        md5.update(block)
                        crc = zlib.crc32(block, crc)
                if md5.hexdigest() != h or crc != int.from_bytes(bytes.fromhex(b['crc']), 'little'):
                    continue
            found = path.resolve().as_posix()
            break
        verified[b['id']] = found
        return found
    def closure(index):
        seen, pending = set(), list(bundles[index]['dependency_ids'])
        while pending:
            item = pending.pop()
            if item in seen or item == index:
                continue
            seen.add(item)
            pending.extend(bundles[item]['dependency_ids'])
        return sorted(seen)
    assets_by_bundle = {}
    for a in m['assets']:
        assets_by_bundle.setdefault(a['bundle_id'], []).append(a['asset_path'])
    models = []
    for b in bundles:
        name = b['name']
        # Restrict primaries to actual character 3D skin/model bundles, not UI portraits.
        if not re.fullmatch(r'assets_development_arts_model_character_(?:character|fashion|npc|emoji).*\.bundle', name):
            continue
        ids = closure(b['id'])
        dep_records = [dict(id=i, bundle=bundles[i]['name'], path=resolve(bundles[i])) for i in ids]
        models.append(dict(name=name.removeprefix('assets_development_arts_model_character_').removesuffix('.bundle'),
                           bundle=name, bundle_id=b['id'], path=resolve(b),
                           dependencies=[d['path'] for d in dep_records if d['path']],
                           dependency_ids=ids, dependency_records=dep_records,
                           missing_dependencies=[d['bundle'] for d in dep_records if not d['path']],
                           assets=assets_by_bundle.get(b['id'], []),
                           verified='size+MD5+CRC32' if args.verify else 'size'))
    models.sort(key=lambda x: (x['name'] != 'character_06_skin', x['name']))
    (args.output/'model-map.json').write_text(json.dumps(models, ensure_ascii=False, indent=2), encoding='utf-8')
    prefab_bundles = []
    for b in bundles:
        if b['name'].startswith('assets_resbundle_model_character_'):
            ids = closure(b['id'])
            prefab_bundles.append(dict(bundle=b['name'], bundle_id=b['id'], path=resolve(b),
                dependency_ids=ids, dependencies=[resolve(bundles[i]) for i in ids],
                assets=assets_by_bundle.get(b['id'], [])))
    (args.output/'character-prefabs.json').write_text(json.dumps(prefab_bundles, ensure_ascii=False, indent=2), encoding='utf-8')
    with (args.output/'model-candidates.csv').open('w', encoding='utf-8-sig', newline='') as f:
        writer = csv.writer(f)
        writer.writerow(['name','bundle_id','bundle','path','dependency_count','missing_dependencies','asset_count'])
        for model in models:
            writer.writerow([model['name'],model['bundle_id'],model['bundle'],model['path'],len(model['dependency_ids']),len(model['missing_dependencies']),len(model['assets'])])
    print(f"Mapped {len(models)} model candidates, {len(prefab_bundles)} character prefab bundles")
    print(f"Local primaries: {sum(bool(m['path']) for m in models)}; complete local sets: {sum(bool(m['path']) and not m['missing_dependencies'] for m in models)}")
    print(json.dumps(models[0], ensure_ascii=True, indent=2))
    return 0

if __name__ == '__main__':
    raise SystemExit(main())
