import csv,hashlib,json,zipfile
from pathlib import Path
from PIL import Image
root=Path(__file__).resolve().parents[2];out=root/'artifacts/expression-export-20261122'
report=json.loads((out/'validation.json').read_text(encoding='utf-8'))
assert len(report)==32 and all(r['status']=='ok' for r in report)
rows=[]
for r in report:
 p=out/r['file'];info=json.loads((p.parent/'info.json').read_text(encoding='utf-8'));images=list(p.parent.glob('*.png'))
 for png in images:
  with Image.open(png) as im:
   im.load();assert im.width>0 and im.height>0
 rows.append(dict(name=info['name'],file=r['file'],vertices=r['vertices'],skeleton_nodes=r['skeleton_nodes'],skinning=r['skinning'],animation_curves=r['animation_curves'],keyframes=r['keyframes'],textures=len(images),sha256=hashlib.sha256(p.read_bytes()).hexdigest()))
with (out/'expression-list.csv').open('w',encoding='utf-8-sig',newline='') as f:
 w=csv.DictWriter(f,fieldnames=list(rows[0]));w.writeheader();w.writerows(rows)
(out/'README.txt').write_text('''游戏面部表情导出

32 个 FBX，每个包含对应的一段动画以及可识别的关联 PNG 贴图。
包括左右眨眼、双眼睁闭、微笑眨眼、生气、眼睛下垂、打瞌睡、嘴部/全部缩点，以及面片出现/循环/消失。
“开始、循环、恢复”是不同片段，不代表32种独立情绪。

Blender 使用：
1. 解压，保持每个目录中的 FBX 和 PNG 在一起。
2. 在新场景导入一个 FBX，例如 models/表情@双眼生气_开始/表情@双眼生气_开始.fbx。
3. 开启 FBX 导入器中的动画导入选项（若有），导入后选择骨架，切到动画/动作编辑器查看动作。播放时间轴。
4. 若时间轴范围不适合，在动作编辑器按 Home 查看关键帧，并把播放起止范围设到关键帧范围。
5. 这是独立表情脸部网格，可能只有眼、眉、嘴或面片，不包含完整身体和头发。游戏使用自定义材质，显示需调整。

与角色模型结合：
这些是游戏的表情模板，不能仅导入到角色旁边就自动控制角色。需要在目标软件中匹配脸部网格、骨骼名称/路径、位置与材质；本次没有完成对39个角色的逐个绑定或动画重定向。
29个FBX包含蒙皮；3个面片FBX没有有效源蒙皮权重，按刚性网格及节点动画导出。

验证：全部32个FBX有动画栈及非空关键帧，网格顶点数与转换结果一致；有蒙皮的文件核验骨骼、权重索引与绑定矩阵；贴图引用与PNG可解码性通过。
未进行Blender人工播放或游戏材质复现测试。身体表情动作包仅调查，未包含本包。
''',encoding='utf-8-sig')
paths=list((out/'models').rglob('*'))+[out/'README.txt',out/'expression-list.csv',out/'validation.json']
zip_path=root/'artifacts/game-facial-expressions-20261122.zip'
with zipfile.ZipFile(zip_path,'w',zipfile.ZIP_DEFLATED,compresslevel=5) as z:
 for p in sorted(paths):
  if p.is_file():z.write(p,p.relative_to(out))
with zipfile.ZipFile(zip_path) as z:assert z.testzip() is None
summary={'fbx':len(rows),'skinned':sum(r['skinning']=='skinned' for r in rows),'rigid':sum(r['skinning']=='rigid' for r in rows),'keyframes':sum(r['keyframes'] for r in rows),'zip_bytes':zip_path.stat().st_size}
(out/'summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8');print(summary)
