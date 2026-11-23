namespace LocalHomeModule;

internal static class MechanismTree
{
    internal sealed record Node(string Id,string Name,string? ParentId);
    internal sealed record Row(string Id,string Name,string? ParentId,int Depth,bool HasChildren,bool Expanded);
    internal static Row[] Flatten(IEnumerable<Node> source,ISet<string> collapsed)
    {
        var nodes=source.Take(128).GroupBy(n=>n.Id,StringComparer.Ordinal).Select(g=>g.First()).ToArray();
        var ids=nodes.Select(n=>n.Id).ToHashSet(StringComparer.Ordinal);
        var children=nodes.GroupBy(n=>n.ParentId!=null && ids.Contains(n.ParentId)?n.ParentId:"").ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
        var visited=new HashSet<string>(StringComparer.Ordinal); var result=new List<Row>();
        void Visit(Node n,int depth)
        {
            if(!visited.Add(n.Id)) return;
            bool has=children.TryGetValue(n.Id,out var kids); bool open=!collapsed.Contains(n.Id);
            result.Add(new(n.Id,n.Name,n.ParentId,depth,has,open));
            if(has && open) foreach(var child in kids!) Visit(child,depth+1);
        }
        if(children.TryGetValue("",out var roots)) foreach(var n in roots) Visit(n,0);
        return result.ToArray();
    }
}
