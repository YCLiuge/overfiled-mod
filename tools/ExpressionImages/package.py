import pathlib,zipfile,hashlib,json
r=pathlib.Path('artifacts/expression-images-20261122')
paths=sorted(p for p in r.rglob('*') if p.is_file() and p.suffix not in ['.zip'] and p.name!='package-info.json')
z=r/'expression-images.zip'
with zipfile.ZipFile(z,'w',zipfile.ZIP_DEFLATED) as f:
 for p in paths:f.write(p,p.relative_to(r).as_posix())
with zipfile.ZipFile(z) as f:
 assert f.testzip() is None
 pngs=[n for n in f.namelist() if n.endswith('.png')];assert len(pngs)==40
report=dict(zip=z.name,bytes=z.stat().st_size,sha256=hashlib.sha256(z.read_bytes()).hexdigest(),archive_files=len(paths),png_files=len(pngs),chat_expressions=12,additional_ui_sprites=28)
(r/'package-info.json').write_text(json.dumps(report,indent=2),encoding='utf8');print(report)
