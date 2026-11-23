import csv
import io
import json
import re
import zipfile
from collections import Counter
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[2]
export = root / 'artifacts' / 'portrait-export-20261122'
files = sorted(export.glob('character_*/Texture2D/*.png'))
if not files:
    raise RuntimeError('No exported PNG files')
rows = []
cols, cw, ch = 6, 280, 330
sheet = Image.new('RGB', (cols*cw, ((len(files)+cols-1)//cols)*ch), '#e6e8ed')
draw = ImageDraw.Draw(sheet)
archive = root / 'artifacts' / 'game-portraits-20261122.zip'
with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_STORED) as z:
    for i, path in enumerate(files):
        name = re.search(r'ui_character_\d+_quanshen_\d+', path.name).group(0)
        with Image.open(path) as im:
            im.load()
            alpha = im.getchannel('A').getextrema() if 'A' in im.getbands() else None
            rows.append({'name':name+'.png', 'width':im.width, 'height':im.height,
                         'mode':im.mode, 'alpha_min':alpha[0] if alpha else None,
                         'alpha_max':alpha[1] if alpha else None, 'bytes':path.stat().st_size,
                         'source':path.relative_to(export).as_posix()})
            thumb = im.convert('RGBA')
            thumb = thumb.crop(thumb.getbbox())
            thumb.thumbnail((cw-20,ch-42))
            x, y = (i%cols)*cw, (i//cols)*ch
            sheet.paste(thumb, (x+(cw-thumb.width)//2,y+8), thumb)
            draw.text((x+8,y+ch-26), name.replace('ui_character_', ''), fill='#172033')
        z.write(path, name+'.png')
    buf=io.StringIO(newline='')
    writer=csv.DictWriter(buf, fieldnames=list(rows[0]))
    writer.writeheader()
    writer.writerows(rows)
    z.writestr('manifest.csv',buf.getvalue().encode('utf-8-sig'))
    z.writestr('README.txt', 'Exported from loaded character AssetBundles via the bundle exporter.\nFilter: quanshen (full-body illustrations).\nPNG pixels and alpha are preserved; filenames are shortened in this archive.\nThe bundle exporter dispatch was fixed to use the native IL2CPP type.\n'.encode())
preview=root/'artifacts'/'game-portraits-preview.jpg'
sheet.save(preview, quality=91)
summary={'images':len(rows),'characters':len({p.parts[-3] for p in files}),
         'dimensions':dict(Counter(f"{r['width']}x{r['height']}" for r in rows)),
         'transparent_images':sum(r['alpha_min']==0 and r['alpha_max']==255 for r in rows),
         'total_png_bytes':sum(r['bytes'] for r in rows),'zip_bytes':archive.stat().st_size,
         'files':rows}
(export/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in summary.items() if k!='files'},ensure_ascii=False,indent=2))
print(archive)
print(preview)
