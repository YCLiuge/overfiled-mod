import json,collections
m=json.load(open('artifacts/model-export-20261122/manifest-records.json',encoding='utf8'))
a=[x for x in m['assets'] if any(s in x['asset_path'].lower() for s in ['emoji','emotion','emoticon','sticker'])]
c=collections.Counter('/'.join(x['asset_path'].split('/')[:-1]) for x in a)
for p,n in c.items():print(n,p)
print('EMOJI ASSETS')
for x in m['assets']:
 if x['asset_path'].startswith('Assets/ResBundle/Emoji/'):print(x['asset_path'])
print('CHAT')
for x in m['assets']:
 if 'chat' in x['asset_path'].lower() and x['asset_path'].endswith('.png'):print(x['asset_path'])
