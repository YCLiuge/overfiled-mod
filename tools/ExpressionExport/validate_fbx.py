"""Check binary FBX geometry, skeleton nodes and skin deformers without a GUI."""
import json, struct, zlib
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]/'artifacts/expression-export-20261122'

def inspect(path):
    data=path.read_bytes()
    if not data.startswith(b'Kaydara FBX Binary  \x00\x1a\x00'):raise ValueError('Not binary FBX')
    version=struct.unpack_from('<I',data,23)[0]
    wide=version>=7500
    fmt='<QQQB' if wide else '<IIIB'
    header=25 if wide else 13
    objects=[]
    def prop(p):
        code=chr(data[p]);p+=1
        scalar={'Y':'h','C':'?','I':'i','F':'f','D':'d','L':'q'}
        if code in scalar:
            f='<'+scalar[code];return struct.unpack_from(f,data,p)[0],p+struct.calcsize(f)
        if code in 'SR':
            n=struct.unpack_from('<I',data,p)[0];p+=4
            raw=data[p:p+n]
            return (raw.decode('utf-8',errors='replace') if code=='S' else {'bytes':n}),p+n
        if code in 'fdilbc':
            n,encoding,size=struct.unpack_from('<III',data,p);p+=12
            raw=data[p:p+size]
            if encoding==1:raw=zlib.decompress(raw)
            width={'f':4,'d':8,'i':4,'l':8,'b':1,'c':1}[code]
            if len(raw)!=n*width:raise ValueError('Corrupt FBX array')
            return {'array':code,'count':n},p+size
        raise ValueError(f'Unsupported FBX property {code}')
    def node(p):
        end,nprops,proplen,namelen=struct.unpack_from(fmt,data,p)
        if end==0:return None,p+header
        if end>len(data):raise ValueError('Node extends beyond FBX')
        p+=header;name=data[p:p+namelen].decode();p+=namelen
        props=[]
        for _ in range(nprops):v,p=prop(p);props.append(v)
        children=[]
        while p<end:
            child,p=node(p)
            if child is None:break
            children.append(child)
        result={'name':name,'props':props,'children':children}
        if name=='Objects':objects.extend(children)
        return result,end
    p=27
    while p+header<=len(data):
        n,p=node(p)
        if n is None:break
    texture_refs=[]
    for o in objects:
        if o['name']!='Texture':continue
        refs={c['name']:c['props'][0] for c in o['children'] if c['name'] in ('RelativeFilename','FileName') and c['props']}
        relative=refs.get('RelativeFilename')
        if relative:
            target=path.parent/relative.replace('\\','/')
            if not target.is_file():raise ValueError(f'Missing referenced texture: {relative}')
            texture_refs.append(relative)
    geometries=[o for o in objects if o['name']=='Geometry' and o['props'][-1]=='Mesh']
    bones=[o for o in objects if o['name']=='Model' and o['props'][-1]=='LimbNode']
    skins=[o for o in objects if o['name']=='Deformer' and o['props'][-1]=='Skin']
    clusters=[o for o in objects if o['name']=='Deformer' and o['props'][-1]=='Cluster']
    def count(o,name):
        return sum(c['props'][0].get('count',0) for c in o['children'] if c['name']==name)
    vertices=sum(count(o,'Vertices')//3 for o in geometries)
    weights=sum(count(o,'Weights') for o in clusters)
    indexes=sum(count(o,'Indexes') for o in clusters)
    links=sum(count(o,'TransformLink')==16 for o in clusters)
    info=json.loads((path.parent/'info.json').read_text(encoding='utf-8-sig'))
    kind='skinned' if skins else 'rigid'
    stacks=[o for o in objects if o['name']=='AnimationStack']
    curves=[o for o in objects if o['name']=='AnimationCurve']
    keys=sum(count(o,'KeyTime') for o in curves)
    if len(stacks)!=1 or keys==0:raise ValueError('Missing animation stack or keyframes')
    if vertices!=info['vertices']:raise ValueError('Vertex count differs from source conversion')
    if vertices<=0:raise ValueError('Empty FBX geometry')
    if kind=='skinned' and not (bones and skins and clusters and weights>0 and weights==indexes and links==len(clusters)):
        raise ValueError(f'Incomplete geometry/rig: {vertices=}, bones={len(bones)}, skins={len(skins)}, clusters={len(clusters)}, {weights=}, {indexes=}, {links=}')
    return {'file':path.relative_to(ROOT).as_posix(),'version':version,'animation_stacks':len(stacks),'animation_curves':len(curves),'keyframes':keys,'skinning':kind,'referenced_textures':len(set(texture_refs)),'mesh_geometries':len(geometries),'vertices':vertices,'skeleton_nodes':len(bones),'skins':len(skins),'clusters':len(clusters),'weight_entries':weights,'bind_matrices':links,'bytes':len(data),'status':'ok'}

if __name__=='__main__':
    output=[]
    for path in sorted((ROOT/'models').rglob('*.fbx')):
        try:output.append(inspect(path))
        except Exception as e:output.append({'file':path.relative_to(ROOT).as_posix(),'status':'failed','error':str(e)})
    (ROOT/'validation.json').write_text(json.dumps(output,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({'files':len(output),'passed':sum(r['status']=='ok' for r in output),'failures':[r for r in output if r['status']!='ok']},ensure_ascii=False,indent=2))
    if not output or any(r['status']!='ok' for r in output):raise SystemExit(1)
