using AssetStudio;
using System.Text.Json;
using SixLabors.ImageSharp;
var root=Path.GetFullPath(args.Length>0?args[0]:"artifacts/expression-images-20261122");
var bundles=JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Path.Combine(root,"bundle-verification.json")));
var sources=JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(Path.Combine(root,"source-assets.json")));
var wanted=sources.Where(s=>s.GetProperty("asset_path").GetString().EndsWith(".png")).ToDictionary(s=>Path.GetFileNameWithoutExtension(s.GetProperty("asset_path").GetString()),s=>s.GetProperty("asset_path").GetString());
var manager=new AssetsManager();var savedOut=Console.Out;try{Console.SetOut(TextWriter.Null);manager.LoadFiles(bundles.Select(b=>b.GetProperty("path").GetString()).Distinct().ToArray());}finally{Console.SetOut(savedOut);}
var objects=manager.assetsFileList.SelectMany(f=>f.Objects).ToArray();
Console.WriteLine("Types: "+string.Join(", ",objects.GroupBy(o=>o.type).Select(g=>$"{g.Key}={g.Count()}")));
Directory.CreateDirectory(Path.Combine(root,"png"));var results=new List<object>();var seen=new HashSet<string>();
foreach(var sprite in objects.OfType<Sprite>().Where(s=>wanted.ContainsKey(s.m_Name))){
 string identity=sprite.assetsFile.fileName+":"+sprite.m_PathID;if(!seen.Add(identity))continue;
 try{using var image=sprite.GetImage();if(image==null)throw new Exception("Sprite.GetImage returned null");string file="png/"+sprite.m_Name+".png";image.SaveAsPng(Path.Combine(root,file));
 results.Add(new{name=sprite.m_Name,source_asset=wanted[sprite.m_Name],serialized_file=sprite.assetsFile.fileName,path_id=sprite.m_PathID,file,width=image.Width,height=image.Height,rect=new{x=sprite.m_Rect.x,y=sprite.m_Rect.y,width=sprite.m_Rect.width,height=sprite.m_Rect.height},status="ok"});Console.WriteLine($"OK {sprite.m_Name} {image.Width}x{image.Height}");
 }catch(Exception e){results.Add(new{name=sprite.m_Name,status="failed",error=e.ToString()});Console.WriteLine(e);}
}
File.WriteAllText(Path.Combine(root,"sprite-export.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Exported {results.Count} records for {wanted.Count} PNG assets");
if(results.Count!=wanted.Count)Environment.ExitCode=1;
