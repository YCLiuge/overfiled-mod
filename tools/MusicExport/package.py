import csv
import io
import json
import struct
import zipfile
from pathlib import Path

root = Path(__file__).resolve().parents[2]
export = root / 'artifacts/music-export-20261122'
rows = json.loads((export/'results.json').read_text(encoding='utf-8-sig'))
catalog = json.loads((export/'catalog.json').read_text(encoding='utf-8-sig'))
if len(rows) != len(catalog):
    raise RuntimeError(f'Export incomplete: {len(rows)}/{len(catalog)}')
valid=[]
for r in rows:
    if r['status']!='ok':
        continue
    p=export/'tracks'/r['file']
    with p.open('rb') as f:
        if f.read(4)!=b'fLaC': raise ValueError(f'Invalid FLAC: {p}')
        head=f.read(4)
        if head[0]&127!=0 or int.from_bytes(head[1:],'big')!=34:
            raise ValueError('Missing STREAMINFO')
        info=f.read(34)
    flags=int.from_bytes(info[10:18],'big')
    rate=flags>>44
    channels=((flags>>41)&7)+1
    bits=((flags>>36)&31)+1
    samples=flags&((1<<36)-1)
    if (rate,channels,bits)!=(r['rate'],r['channels'],r['bits']): raise ValueError('Audio format mismatch')
    if abs(samples/rate-r['seconds'])>1/rate: raise ValueError('Audio duration mismatch')
    if r['nonzero']<=0 or r['rms']<=0 or p.stat().st_size<=42: raise ValueError('Empty audio')
    r['streaminfo_md5']=info[18:34].hex()
    r['sha256']=__import__('hashlib').sha256(p.read_bytes()).hexdigest()
    valid.append(r)
fields=['index','name','file','seconds','rate','channels','bits','bytes','peak','rms','nonzero','sourceCompression','sha256','streaminfo_md5','bundle']
s=io.StringIO(newline='')
w=csv.DictWriter(s,fieldnames=fields,extrasaction='ignore');w.writeheader();w.writerows(valid)
(export/'music-list.csv').write_bytes(s.getvalue().encode('utf-8-sig'))
playlist='#EXTM3U\n'+''.join(f"#EXTINF:{r['seconds']:.3f},{r['name']}\ntracks/{r['file']}\n" for r in valid)
(export/'playlist.m3u8').write_text(playlist,encoding='utf-8')
readme=(f'游戏背景音乐导出\n\n共 {len(valid)}/{len(catalog)} 首。FLAC，16-bit PCM。\n'
        '从游戏当前加载的 BGM bundle 清单定位本地资源，AssetStudio/FMOD 解码后使用 FLAC 无损压缩。\n'
        'FLAC 无损指保留本次解码得到的采样；游戏内部原音频可能采用有损压缩。\n'
        '每首验证了非静音 PCM、FLAC 采样率/声道/位深与完整时长；CSV 附带 SHA256。\n'
        'playlist.m3u8 可在支持 FLAC 的播放器中打开，music-list.csv 是歌曲清单。\n'
        '仅覆盖本次已加载音乐包，不保证包含游戏全部音乐。\n')
(export/'README.txt').write_text(readme,encoding='utf-8-sig')
archive=root/'artifacts/game-music-20261122.zip'
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_STORED,allowZip64=True) as z:
    for r in valid:z.write(export/'tracks'/r['file'],'tracks/'+r['file'])
    for name in ['music-list.csv','playlist.m3u8','README.txt']:
        z.write(export/name,name)
summary={'total_bundles':len(catalog),'exported_tracks':len(valid),'failed':[r for r in rows if r['status']!='ok'],
         'total_seconds':sum(r['seconds'] for r in valid),'bytes':sum(r['bytes'] for r in valid),
         'sample_rates':sorted(set(r['rate'] for r in valid)), 'channels':sorted(set(r['channels'] for r in valid)),
         'zip_bytes':archive.stat().st_size}
(export/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False,indent=2))
