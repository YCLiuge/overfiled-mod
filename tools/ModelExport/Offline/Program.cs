using System.Text.Json;
using AssetStudio;

var root=Path.GetFullPath(args[0]);
Directory.CreateDirectory(root);
var rows=JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Path.Combine(root,"model-map.json")));
bool retryRigid=args.Contains("--retry-rigid");
int limit=args.Length>1&&!args[1].StartsWith("--")?int.Parse(args[1]):int.MaxValue;
var previous=retryRigid?JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Path.Combine(root,"results.json"))):Array.Empty<JsonElement>();
var retries=previous.Where(r=>r.GetProperty("status").GetString()=="failed"&&r.TryGetProperty("gameObject",out _)).Select(r=>r.GetProperty("name").GetString()+"|"+r.GetProperty("gameObject").GetString()).ToHashSet();
var results=new List<object>(previous.Where(r=>r.GetProperty("status").GetString()!="failed").Cast<object>());
var json=new JsonSerializerOptions{WriteIndented=true,IncludeFields=true};
foreach(var row in rows.Where(r=>System.Text.RegularExpressions.Regex.IsMatch(r.GetProperty("name").GetString(),@"^character_\d+_skin$")).Take(limit)) {
 string name=row.GetProperty("name").GetString();
 if(retryRigid&&!retries.Any(k=>k.StartsWith(name+"|")))continue;
 string path=row.GetProperty("path").GetString();
 var manager=new AssetsManager();
 try {
  string[] files=new[]{path}.Concat(row.GetProperty("dependencies").EnumerateArray().Select(p=>p.GetString())).Distinct().ToArray();
  var stdout=Console.Out;
  try{Console.SetOut(TextWriter.Null);manager.LoadFiles(files);}finally{Console.SetOut(stdout);}
  var objects=manager.assetsFileList.SelectMany(f=>f.Objects).ToArray();
  var roots=manager.assetsFileList.First().Objects.OfType<GameObject>().Where(g=>g.m_Transform!=null&&!g.m_Transform.m_Father.TryGet(out var _)).ToArray();
  Console.WriteLine($"{name}: loaded {manager.assetsFileList.Count} serialized files; {objects.Length} objects, {roots.Length} roots");
  int exported=0;
  foreach(var go in roots) {
   if(retryRigid&&!retries.Contains(name+"|"+go.m_Name))continue;
   // Ignore unrelated dependency roots; only hierarchies containing skinned meshes are candidates.
   if(!HasSkin(go.m_Transform))continue;
   string safe=string.Join("_",go.m_Name.Split(Path.GetInvalidFileNameChars()));
   string dir=Path.Combine(root,"models",name,safe);Directory.CreateDirectory(dir);
   try {
    var converted=new ModelConverter(go,ImageFormat.Png,Array.Empty<AnimationClip>());
    var meshes=converted.MeshList;
    if(meshes.Count==0||(!retryRigid&&meshes.Sum(m=>m.BoneList?.Count??0)==0))throw new Exception("No skinned mesh/bones in converted model");
    long weighted=0;int invalidWeights=0,invalidBones=0;
    foreach(var mesh in meshes)foreach(var v in mesh.VertexList) {
     if(v.Weights==null)continue;
     if(v.Weights.Any(w=>w>0))weighted++;
     float sum=v.Weights.Sum();if(sum>0&&Math.Abs(sum-1)>0.02)invalidWeights++;
     for(int i=0;i<v.Weights.Length;i++)if(v.Weights[i]>0&&(v.BoneIndices[i]<0||v.BoneIndices[i]>=(mesh.BoneList?.Count??0)))invalidBones++;
    }
    if((weighted==0&&!retryRigid)||invalidBones>0)throw new Exception($"Invalid skinning: weighted={weighted} invalidBoneIndices={invalidBones}");
    string fbx=Path.Combine(dir,safe+".fbx");
    ModelExporter.ExportFbx(fbx,converted,false,0.25f,true,weighted>0,false,true,false,1f,false,1f,3,false);
    if(!File.Exists(fbx)||new FileInfo(fbx).Length<1000)throw new Exception("FBX file absent or empty");
    var skeleton=Frame(converted.RootFrame);
    File.WriteAllText(Path.Combine(dir,"skeleton.json"),JsonSerializer.Serialize(skeleton,json));
    var stats=new{name,gameObject=go.m_Name,status="ok",skinning=weighted>0?"skinned":"rigid",file=Path.GetRelativePath(root,fbx).Replace('\\','/'),meshes=meshes.Count,vertices=meshes.Sum(m=>m.VertexList.Count),triangles=meshes.Sum(m=>m.SubmeshList.Sum(s=>s.FaceList.Count)),boneBindings=meshes.Sum(m=>m.BoneList?.Count??0),uniqueBones=meshes.SelectMany(m=>m.BoneList??new()).Select(b=>b.Path).Distinct().Count(),weightedVertices=weighted,invalidWeights,invalidBones,textures=converted.TextureList.Count,materials=converted.MaterialList.Count,morphs=converted.MorphList.Count,bytes=new FileInfo(fbx).Length};
    File.WriteAllText(Path.Combine(dir,"model-info.json"),JsonSerializer.Serialize(stats,json));
    results.Add(stats);exported++;Console.WriteLine($"OK {name}/{safe}: {stats.vertices} vertices, {stats.uniqueBones} bones, {stats.textures} textures");
   }catch(Exception ex){results.Add(new{name,gameObject=go.m_Name,status="failed",error=ex.ToString()});Console.WriteLine($"FAILED {name}/{go.m_Name}: {ex.Message}");}
  }
  if(exported==0)results.Add(new{name,status="empty",roots=roots.Select(g=>g.m_Name).ToArray(),types=objects.GroupBy(o=>o.GetType().Name).ToDictionary(g=>g.Key,g=>g.Count())});
 }catch(Exception ex){results.Add(new{name,status="failed",error=ex.ToString()});Console.WriteLine($"FAILED {name}: {ex.Message}");}
 finally{manager.Clear();GC.Collect();}
 File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(results,json));
}
static bool HasSkin(Transform t){if(t.m_GameObject.TryGet(out var g)&&g.m_SkinnedMeshRenderer!=null)return true;foreach(var c in t.m_Children)if(c.TryGet(out var child)&&HasSkin(child))return true;return false;}
static object Frame(ImportedFrame f)=>new{name=f.Name,path=f.Path,position=f.LocalPosition,rotationEuler=f.LocalRotation,scale=f.LocalScale,children=Enumerable.Range(0,f.Count).Select(i=>Frame(f[i])).ToArray()};
