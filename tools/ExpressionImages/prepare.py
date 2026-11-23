import json,pathlib,hashlib,sys
chat='--chat' in sys.argv
root=pathlib.Path('artifacts/expression-images-20261122') / ('chat' if chat else '.');root.mkdir(parents=True,exist_ok=True)
m=json.load(open('artifacts/model-export-20261122/manifest-records.json',encoding='utf8'))
a=[x for x in m['assets'] if ('/Texture/Chat/Expression_' in x['asset_path'] if chat else 'UI_Root_Emotion/Processed/' in x['asset_path'])]
json.dump(a,open(root/'source-assets.json','w',encoding='utf8'),ensure_ascii=False,indent=2)
bs={b['id']:b for b in m['bundles']}; seen=set(); pending=[x['bundle_id'] for x in a]
while pending:
 i=pending.pop()
 if i in seen:continue
 seen.add(i);pending+=bs[i]['dependency_ids']
game=pathlib.Path('D:/TapTap/PC Games/176228/launcher_Data');rows=[]
for i in sorted(seen):
 b=bs[i];h=b['hash'];candidates=[game/'StreamingAssets/yoo/DefaultPackage'/f'{h}.bundle',game/'yoo/DefaultPackage/CacheFiles'/h[:2]/h/'__data']
 found=None
 for p in candidates:
  if p.is_file() and p.stat().st_size==b['size'] and hashlib.md5(p.read_bytes()).hexdigest()==h:found=p;break
 if found is None:raise RuntimeError(f'Missing or corrupt bundle {b}')
 rows.append(dict(b,path=found.as_posix(),verified_size=found.stat().st_size,verified_md5=h))
json.dump(rows,open(root/'bundle-verification.json','w',encoding='utf8'),ensure_ascii=False,indent=2)
print('assets',len(a),'png',sum(x['asset_path'].endswith('.png') for x in a),'bundles',len(rows))
print('\n'.join(x['asset_path'] for x in a));print('bundles',[(r['id'],r['name'],r['size']) for r in rows])
