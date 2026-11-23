import json,hashlib,zlib
from pathlib import Path
root=Path(__file__).resolve().parents[2]
out=root/'artifacts/expression-export-20261122';out.mkdir(parents=True,exist_ok=True)
m=json.loads((root/'artifacts/model-export-20261122/manifest-records.json').read_text(encoding='utf-8'))
b=m['bundles'];game=Path('D:/TapTap/PC Games/176228/launcher_Data')
ids={r['id'] for r in b if r['name']=='assets_development_arts_model_character_emoji_skin.bundle' or ('emoji' in r['name'] and 'animations' in r['name'])}
# Also capture motion bundles explicitly used by the game's expression action prefabs.
actions=[r for r in b if r['name'].startswith('assets_resbundle_action_emoji_')]
def closure(i):
 seen={i};pending=[i]
 while pending:
  for n in b[pending.pop()]['dependency_ids']:
   if n not in seen:seen.add(n);pending.append(n)
 return seen
all_deps=set().union(*(closure(r['id']) for r in actions))
ids.update(i for i in all_deps if 'animations' in b[i]['name'] and ('表情' in b[i]['name'] or 'emoji' in b[i]['name']))
records=[]
for i in sorted(ids):
 r=b[i];files=[]
 for j in sorted(closure(i),key=lambda j:j!=i):
  x=b[j];h=x['hash'];p=game/'StreamingAssets/yoo/DefaultPackage'/f'{h}.bundle'
  if not p.is_file():p=game/'yoo/DefaultPackage/CacheFiles'/h[:2]/h/'__data'
  data=p.read_bytes()
  if len(data)!=x['size'] or hashlib.md5(data).hexdigest()!=h or zlib.crc32(data)!=int.from_bytes(bytes.fromhex(x['crc']),'little'):raise ValueError(x['name'])
  files.append(str(p))
 records.append({'name':r['name'],'files':files})
(out/'bundle-map.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'selected':len(records),'names':[r['name'] for r in records]},ensure_ascii=False,indent=2))
