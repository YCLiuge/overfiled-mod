import csv, hashlib, io, json, zipfile
from pathlib import Path
root=Path(__file__).resolve().parents[2]
export=root/'artifacts/model-export-20261122'
results=json.loads((export/'results.json').read_text(encoding='utf-8-sig'))
validation=json.loads((export/'validation.json').read_text(encoding='utf-8-sig'))
verified={r['file']:r for r in validation if r['status']=='ok'}
valid=sorted([r for r in results if r['status']=='ok' and r['file'] in verified],key=lambda r:(r['name'],r['gameObject']))
if not valid:raise RuntimeError('No validated rigged models')
for r in valid:
    r.setdefault('skinning','skinned')
    r['sha256']=hashlib.sha256((export/r['file']).read_bytes()).hexdigest()
fields=['name','gameObject','skinning','file','vertices','triangles','meshes','uniqueBones','boneBindings','weightedVertices','invalidWeights','invalidBones','materials','textures','morphs','bytes','sha256']
s=io.StringIO(newline='');w=csv.DictWriter(s,fieldnames=fields,extrasaction='ignore');w.writeheader();w.writerows(valid)
(export/'model-list.csv').write_bytes(s.getvalue().encode('utf-8-sig'))
readme=f'''游戏角色模型与骨骼导出

{len(valid)} 个经过验证的 FBX 文件；角色/资源分组数 {len(set(r['name'] for r in valid))}。

skinned 类型 FBX 含网格、骨骼层级、蒙皮权重和绑定矩阵。少数无有效蒙皮权重的附件标为 rigid，导出为静态网格，使用时需自行挂接。skeleton.json 为骨骼/节点层级及局部变换，model-info.json 为模型统计。
同一角色的身体、头饰等原始资源可能分别导出，名称沿用原始资源名。
贴图沿用导出器能够识别的材质关联。游戏使用的自定义 Shader、描边、光照与特效需要在目标软件中重新设置。
本次目标为模型和骨骼，未导出动画片段。没有在 Blender/Maya 中做人工绑定或动画测试。
验证包括转换前的骨骼索引/非零蒙皮权重检查，以及生成FBX内的网格、骨骼节点、Skin、Cluster、权重数组和绑定矩阵检查。
请保持每个模型目录完整，避免丢失相对路径关联的贴图。

model-list.csv 可查看每个模型的顶点、骨骼、贴图数量与 SHA256。
'''
(export/'README.txt').write_text(readme,encoding='utf-8-sig')
archive=root/'artifacts/game-character-models-20261122.zip'
paths=set()
for r in valid:
    paths.update(p for p in (export/r['file']).parent.rglob('*') if p.is_file())
with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=5,allowZip64=True) as z:
    for p in sorted(paths):z.write(p,p.relative_to(export).as_posix())
    for name in ['model-list.csv','README.txt','validation.json']:z.write(export/name,name)
sample=root/'artifacts/character-06-rigged-sample.zip'
with zipfile.ZipFile(sample,'w',zipfile.ZIP_DEFLATED,compresslevel=5) as z:
    for p in sorted(paths):
        if p.relative_to(export).parts[:2]==('models','character_06_skin'):
            z.write(p,p.relative_to(export).as_posix())
summary={'validated_fbx':len(valid),'skinned_fbx':sum(r['skinning']=='skinned' for r in valid),'rigid_fbx':sum(r['skinning']=='rigid' for r in valid),'groups':len(set(r['name'] for r in valid)),'vertices':sum(r['vertices'] for r in valid),'weighted_vertices':sum(r['weightedVertices'] for r in valid),'invalid_weight_sums':sum(r['invalidWeights'] for r in valid),'invalid_bone_indices':sum(r['invalidBones'] for r in valid),'textures':sum(r['textures'] for r in valid),'bone_range':[min(r['uniqueBones'] for r in valid),max(r['uniqueBones'] for r in valid)],'failed_or_empty':[r for r in results if r['status']!='ok'],'zip_bytes':archive.stat().st_size}
(export/'summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False,indent=2))
