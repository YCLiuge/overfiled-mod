using System.Globalization;
using System.Text.Json;

namespace LocalHomeModule;

internal sealed record CatalogItem(uint Id,string Name);
internal sealed class FurnitureCatalog
{
    private Dictionary<uint,CatalogItem> items=new();
    private readonly string path=Path.Combine(AppContext.BaseDirectory,"UserData","LocalHome","catalog.json");
    internal CatalogItem[] Items => items.Values.OrderBy(i=>i.Id).ToArray();
    internal FurnitureCatalog()
    {
        if(!File.Exists(path)) return;
        var loaded=JsonSerializer.Deserialize<CatalogItem[]>(File.ReadAllText(path)) ?? throw new InvalidDataException("配置表为空或损坏");
        foreach(var item in loaded)
        {
            if(item==null) throw new InvalidDataException("配置表条目无效");
            Validate(item.Id,item.Name);
            if(!items.TryAdd(item.Id,item)) throw new InvalidDataException("配置表 ID 重复");
        }
    }
    private static void Validate(uint id,string name)
    {
        if(id==0 || id>int.MaxValue) throw new ArgumentException("配置 ID 必须为正整数，且不超过 int.MaxValue");
        if(string.IsNullOrWhiteSpace(name) || name.Length>80 || name.Any(char.IsControl)) throw new ArgumentException("名称需要 1~80 个字符，不能包含控制字符");
    }
    internal void Put(string id,string name)
    {
        uint value=uint.Parse(id,NumberStyles.None,CultureInfo.InvariantCulture); name=name.Trim(); Validate(value,name);
        var next=new Dictionary<uint,CatalogItem>(items); next[value]=new CatalogItem(value,name); Save(next);
    }
    internal void Remove(uint id)
    {
        var next=new Dictionary<uint,CatalogItem>(items); next.Remove(id); Save(next);
    }
    private void Save(Dictionary<uint,CatalogItem> next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(next.Values.OrderBy(i=>i.Id),new JsonSerializerOptions { WriteIndented=true }));
        File.Move(path+".tmp",path,true); items=next;
    }
}
