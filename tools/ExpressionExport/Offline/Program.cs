using AssetStudio;
using System.Text.Json;
var dir=Path.GetFullPath(args[0]);var rows=JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Path.Combine(dir,"bundle-map.json")));
var results=new List<object>();
foreach(var row in rows){var manager=new AssetsManager();
try{var stdout=Console.Out;try{Console.SetOut(TextWriter.Null);manager.LoadFiles(row.GetProperty("files").EnumerateArray().Select(x=>x.GetString()).ToArray());}finally{Console.SetOut(stdout);}
var objects=manager.assetsFileList.First().Objects;
var roots=objects.OfType<GameObject>().Where(g=>g.m_Transform!=null&&!g.m_Transform.m_Father.TryGet(out var _)).Select(g=>g.m_Name).ToArray();
var clips=objects.OfType<AnimationClip>().Select(c=>c.m_Name).ToArray();
if(row.GetProperty("name").GetString().Contains("emoji_skin")){
 foreach(var go in objects.OfType<GameObject>().Where(g=>g.m_Transform!=null&&!g.m_Transform.m_Father.TryGet(out var _))){
 try{
 if(args.Contains("--rigid-only")&&!go.m_Name.StartsWith("面片@"))continue;
 var name=go.m_Name;var clipName=name.Split('@').Last();var selected=objects.OfType<AnimationClip>().Where(c=>c.m_Name==clipName).ToArray();
 var model=new ModelConverter(go,ImageFormat.Png,selected);var target=Path.Combine(dir,"models",name);Directory.CreateDirectory(target);
 bool skinned=model.MeshList.Any(m=>m.VertexList.Any(v=>v.Weights!=null&&v.Weights.Any(w=>w>0)));
 ModelExporter.ExportFbx(Path.Combine(target,name+".fbx"),model,false,0.25f,true,skinned,true,true,false,1f,false,1f,3,false);
 var info=new{name,skinning=skinned?"skinned":"rigid",clips=selected.Select(c=>c.m_Name).ToArray(),meshes=model.MeshList.Count,vertices=model.MeshList.Sum(m=>m.VertexList.Count),textures=model.TextureList.Count,animations=model.AnimationList.Select(a=>new{name=a.Name,tracks=a.TrackList.Count}).ToArray()};
 File.WriteAllText(Path.Combine(target,"info.json"),JsonSerializer.Serialize(info,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"EXPORTED {name}: {model.AnimationList.Count} animations");
 }catch(Exception e){Console.WriteLine($"FAILED {go.m_Name}: {e}");}
 }
}

results.Add(new{bundle=row.GetProperty("name").GetString(),roots,clips,types=objects.GroupBy(o=>o.GetType().Name).ToDictionary(g=>g.Key,g=>g.Count())});Console.WriteLine($"{row.GetProperty("name").GetString()}: roots={roots.Length} clips={clips.Length}");
}catch(Exception e){Console.WriteLine(e);results.Add(new{error=e.ToString()});}finally{manager.Clear();GC.Collect();}
}
File.WriteAllText(Path.Combine(dir,"inventory.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
