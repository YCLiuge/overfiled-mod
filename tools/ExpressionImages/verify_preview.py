from PIL import Image, ImageDraw, ImageFont
import pathlib,json,hashlib,zipfile,math,csv,sys
chat='--chat' in sys.argv
r=pathlib.Path('artifacts/expression-images-20261122') / ('chat' if chat else '.')
exports=json.loads((r/'sprite-export.json').read_text());rows=[]
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15);small=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',12)
w,cellh,cols=260,310,5
sheet=Image.new('RGB',(w*cols,60+math.ceil(len(exports)/cols)*cellh),'#202632');d=ImageDraw.Draw(sheet);d.text((20,16),'Chat: 12 cropped expression Sprites' if chat else 'UI_Root_Emotion: 28 cropped Sprites | original pixel dimensions below',font=font,fill='white')
for i,x in enumerate(sorted(exports,key=lambda x:int(x['name'].split('_')[-1]) if chat else x['name'])):
 p=r/x['file'];im=Image.open(p);im.load();assert im.mode=='RGBA',(p,im.mode)
 alpha=im.getchannel('A');hist=alpha.histogram();visible=sum(hist[1:]);assert visible>0,p
 row=dict(x,index=i+1,mode=im.mode,alpha_min=alpha.getextrema()[0],alpha_max=alpha.getextrema()[1],visible_pixels=visible,transparent_pixels=hist[0],partially_transparent_pixels=sum(hist[1:255]),bytes=p.stat().st_size,sha256=hashlib.sha256(p.read_bytes()).hexdigest());rows.append(row)
 ox=(i%cols)*w;oy=60+(i//cols)*cellh
 for yy in range(0,250,16):
  for xx in range(0,240,16):d.rectangle((ox+10+xx,oy+yy,ox+min(250,xx+26),oy+min(250,yy+16)),fill='#dde2e8' if (xx//16+yy//16)%2 else '#f4f5f7')
 thumb=im.copy();thumb.thumbnail((228,232));sheet.paste(thumb,(ox+130-thumb.width//2,oy+125-thumb.height//2),thumb)
 d.text((ox+12,oy+257),f'{i+1:02d}  {im.width} x {im.height}',font=font,fill='white');d.text((ox+12,oy+281),x['name'] if chat else x['name'][:16]+'...',font=small,fill='#b6c4d4')
sheet.save(r/'contact-sheet.jpg',quality=94)
report=dict(total=len(rows),rgba_count=sum(x['mode']=='RGBA' for x in rows),nonempty_count=sum(x['visible_pixels']>0 for x in rows),with_transparent_pixels=sum(x['transparent_pixels']>0 for x in rows),unique_pixel_sha256=len(set(hashlib.sha256(Image.open(r/x['file']).tobytes()).hexdigest() for x in rows)),images=rows)
(r/'image-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
with (r/'image-list.csv').open('w',encoding='utf-8-sig',newline='') as f:
 writer=csv.DictWriter(f,fieldnames=['index','name','width','height','mode','visible_pixels','transparent_pixels','sha256','file']);writer.writeheader();writer.writerows({k:x[k] for k in writer.fieldnames} for x in rows)
print({k:v for k,v in report.items() if k!='images'})
