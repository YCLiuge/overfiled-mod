using System.Reflection;
using System.Text;
using System.Text.Json;

namespace FurnitureExport;

// Called on the game's main thread through the existing local call endpoint.
// Reads configuration only. It does not create items or send chat/network messages.
public static class ExporterV3
{
    private const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
    private static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(t=>t!=null) ?? throw new TypeLoadException(name);
    private static object? Get(object obj,string name) => obj.GetType().GetProperty(name,Flags)?.GetValue(obj) ?? obj.GetType().GetField(name,Flags)?.GetValue(obj);
    private static object? Static(Type type,string name) => type.GetProperty(name,Flags)?.GetValue(null) ?? type.GetField(name,Flags)?.GetValue(null);
    private static IEnumerable<object> Values(object obj)
    {
        if(obj is System.Collections.IEnumerable e) { foreach(var value in e) if(value!=null)yield return value; yield break; }
        var indexer=obj.GetType().GetProperties(Flags).FirstOrDefault(p=>p.GetIndexParameters().Length==1 && p.GetIndexParameters()[0].ParameterType==typeof(int));
        var size=Get(obj,"Count") ?? Get(obj,"Length");
        if(indexer!=null && size!=null)
        {
            for(int i=0;i<Convert.ToInt32(size);i++) { var value=indexer.GetValue(obj,new object[]{i}); if(value!=null)yield return value; }
            yield break;
        }
        object iterator=obj.GetType().GetMethod("GetEnumerator",Type.EmptyTypes)!.Invoke(obj,null)!;
        try
        {
            var move=iterator.GetType().GetMethod("MoveNext",Type.EmptyTypes)!;
            while((bool)move.Invoke(iterator,null)!) { var value=Get(iterator,"Current"); if(value!=null)yield return value; }
        }
        finally { (iterator as IDisposable)?.Dispose(); }
    }
    private static void Save(string path,object value) => File.WriteAllText(path,JsonSerializer.Serialize(value,new JsonSerializerOptions { WriteIndented=true }),new UTF8Encoding(false));
    private static Dictionary<string,object?> Scalars(object obj) => obj.GetType().GetProperties(Flags).Where(p=>p.GetMethod?.IsStatic==false && p.PropertyType!=typeof(IntPtr) && p.PropertyType!=typeof(UIntPtr) && p.GetIndexParameters().Length==0 && (p.PropertyType.IsPrimitive || p.PropertyType.IsEnum || p.PropertyType==typeof(string)) && !p.Name.EndsWith("_")).ToDictionary(p=>p.Name,p=>p.PropertyType.IsEnum ? p.GetValue(obj)?.ToString() : p.GetValue(obj));
    private static int Int(object obj,string name) => Convert.ToInt32(Get(obj,name));
    private static string Csv(object? value) => "\""+(value?.ToString()??"").Replace("\"","\"\"")+"\"";
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            var homes=Values(Static(Find("Il2CppProtocol.HomeItemConfigureManager"),"HomeItem") ?? throw new InvalidOperationException("HomeItem not loaded")).ToArray();
            var items=Values(Static(Find("Il2CppProtocol.ItemConfigureManager"),"Item") ?? throw new InvalidOperationException("Item not loaded")).ToArray();
            var groups=Values(Static(Find("Il2CppProtocol.FurnitureConfigureManager"),"Furniture") ?? throw new InvalidOperationException("Furniture not loaded")).ToArray();
            Save(Path.Combine(output,"home-items-raw.json"),homes.Select(Scalars).ToArray());
            Save(Path.Combine(output,"furniture-groups-raw.json"),groups.Select(g=>new { Fields=Scalars(g), Items=Values(Get(g,"Items")!).Select(Scalars).ToArray() }).ToArray());
            var furnitureIds=homes.Concat(groups).Select(h=>Int(h,"ItemID")).Where(id=>id>0).Concat(items.Where(i=>Int(i,"NewBagItemTag")==34 || Int(i,"NewBagItemTag")==39).Select(i=>Int(i,"ID"))).Distinct().OrderBy(id=>id).ToArray();
            var itemMap=items.ToDictionary(i=>Int(i,"ID"));
            var homeMap=homes.ToLookup(h=>Int(h,"ItemID"));
            var groupMap=groups.ToLookup(g=>Int(g,"ItemID"));
            var names=Find("Il2Cpp.IExcelSystem+Item").GetMethod("GetItemString",Flags,null,new[]{typeof(int)},null)!;
            var rows=new List<Dictionary<string,object?>>();
            int missingName=0;
            foreach(int id in furnitureIds)
            {
                itemMap.TryGetValue(id,out var item);
                var text=names.Invoke(null,new object[]{id});
                var name=text==null ? "" : Values(Get(text,"text_")!).Select(v=>v.ToString()).FirstOrDefault() ?? "";
                if(string.IsNullOrWhiteSpace(name)) { missingName++; name="未命名家具 #"+id; }
                rows.Add(new() { ["Id"]=id,["Name"]=name,["BagItemTag"]=item==null ? null : Get(item,"BagItemTag")?.ToString(),["TagId"]=item==null ? null : Get(item,"NewBagItemTag"),["ItemExists"]=item!=null,["RelateID"]=item==null ? null : Get(item,"RelateID"),["HomeIds"]=homeMap[id].Select(h=>Int(h,"ID")).ToArray(),["Paths"]=homeMap[id].Select(h=>Get(h,"Path")?.ToString()).Distinct().ToArray(),["GroupIds"]=groupMap[id].Select(g=>Int(g,"ID")).ToArray() });
            }
            Save(Path.Combine(output,"furniture-details.json"),rows);
            Save(Path.Combine(output,"catalog.json"),rows.Select(r=>new { Id=r["Id"],Name=r["Name"] }).ToArray());
            File.WriteAllLines(Path.Combine(output,"furniture-ids.csv"),new[]{"配置ID,名称,分类,家园表ID,家具定义表ID,模型路径"}.Concat(rows.Select(r=>string.Join(",",Csv(r["Id"]),Csv(r["Name"]),Csv(r["BagItemTag"]),Csv(string.Join(";",(int[])r["HomeIds"]!)),Csv(string.Join(";",(int[])r["GroupIds"]!)),Csv(string.Join(";",(string?[])r["Paths"]!))))),new UTF8Encoding(true));
            Save(Path.Combine(output,"summary.json"),new { TimestampUtc=DateTime.UtcNow, HomeRows=homes.Length, GroupRows=groups.Length, ItemRows=items.Length, Count=rows.Count, MissingName=missingName, MissingItem=rows.Count(r=>!(bool)r["ItemExists"]!), ByTag=rows.GroupBy(r=>r["BagItemTag"]?.ToString()??"MissingItem").ToDictionary(g=>g.Key,g=>g.Count()), Rule="Distinct positive HomeItem.ItemID union Furniture.ItemID union Item.ID with BagItemTag Furniture(34)/WallPaper(39). Read-only runtime configuration export." });
        }
        catch(Exception e) { Save(Path.Combine(output,"error.json"),new { Error=e.ToString() }); throw; }
    }
    public static void Inspect(string output)
    {
        Directory.CreateDirectory(output);
        var type=Find("Il2Cpp.IExcelSystem");
        var tables=type.GetProperties(Flags).Where(p=>p.GetMethod?.IsStatic==true).Select(p=>new { Name=p.Name, Type=p.PropertyType.FullName, Members=p.PropertyType.GetMembers(Flags).Select(m=>m.ToString()).ToArray() }).ToArray();
        Save(Path.Combine(output,"excel-interfaces.json"),tables);
    }
}
