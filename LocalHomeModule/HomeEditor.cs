using System.Text.Json;
using mod;

namespace LocalHomeModule;

internal sealed partial class HomeRuntime
{
    private sealed record EditorState(Placement[] Items, EditorOrigin Origin, int[] Group, int Version=2);
    private readonly List<EditorState> undo=new(), redo=new();
    private long selectionEpoch;
    internal EditorOrigin Origin { get; private set; }=new();
    private int[] lastGroup=Array.Empty<int>();
    internal int[] LastGroup => (int[])lastGroup.Clone();
    internal bool CanUndo => enabled && undo.Count>0;
    internal bool CanRedo => enabled && redo.Count>0;
    private EditorState EditorSnapshot() => new(Items,Origin,LastGroup);
    private void ResetEditor() { selectionEpoch++; Origin=new(); lastGroup=Array.Empty<int>(); undo.Clear(); redo.Clear(); }
    partial void GuardMechanismEdit();
    private void RequireEditor()
    {
        if(!Available || !enabled) throw new InvalidOperationException("请先进入家园并开启本地家园");
        GuardMechanismEdit();
    }
    private static EditorState ValidateEditor(EditorState state)
    {
        if(state.Version!=2 || state.Origin==null || !state.Origin.Valid || state.Group==null) throw new InvalidDataException("编辑器存档或原点无效");
        var items=LayoutCodec.Validate(state.Items).OrderBy(p=>p.Id).ToArray();
        var ids=items.Select(p=>p.Id).ToHashSet();
        return new(items,state.Origin.Rounded(),state.Group.Where(ids.Contains).Distinct().ToArray());
    }
    private void SaveEditor(EditorState state)
    {
        var validated=ValidateEditor(state);
        string path=SavePath; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp=path+".tmp";
        File.WriteAllText(tmp,JsonSerializer.Serialize(validated,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(tmp,path,true);
    }
    private EditorState ReadEditor()
    {
        var t=TipMsgCore.i.mainplayer?.transform;
        var initial=t==null ? new EditorOrigin() : new EditorOrigin(t.position.x,t.position.y,t.position.z,0,t.eulerAngles.y,0).Rounded();
        if(!File.Exists(SavePath)) return ValidateEditor(new(Array.Empty<Placement>(),initial,Array.Empty<int>()));
        using var json=JsonDocument.Parse(File.ReadAllText(SavePath));
        // Legacy saves are placement arrays; migrate only on a successful edit/save.
        var state=json.RootElement.ValueKind==JsonValueKind.Array
            ? new EditorState(json.RootElement.Deserialize<Placement[]>()!,initial,Array.Empty<int>())
            : json.RootElement.Deserialize<EditorState>() ?? throw new InvalidDataException("编辑器存档为空");
        return ValidateEditor(state);
    }
    private void ApplyEditor(EditorState state)
    {
        var remaining=state.Items.Select(p=>p.Id).ToHashSet();
        if(local.Keys.Any(id=>!remaining.Contains(id))) selectionEpoch++;
        local.Clear(); foreach(var p in state.Items) local.Add(p.Id,p);
        Origin=state.Origin; lastGroup=(int[])state.Group.Clone();
    }
    private static bool SameEditor(EditorState a,EditorState b) => a.Origin==b.Origin && a.Items.SequenceEqual(b.Items) && a.Group.SequenceEqual(b.Group);
    private static void Remember(List<EditorState> stack,EditorState state)
    {
        stack.Add(state); if(stack.Count>50) stack.RemoveAt(0);
    }
    partial void CheckMechanismItems(Placement[] items);
    private void CommitEditor(EditorState next)
    {
        RequireEditor(); next=ValidateEditor(next); CheckMechanismItems(next.Items); var before=EditorSnapshot();
        if(SameEditor(before,next)) return;
        SaveEditor(next); // History and memory change only after a successful atomic save.
        Remember(undo,before); redo.Clear(); ApplyEditor(next);
        if(!before.Items.SequenceEqual(next.Items)) MarkDirty(editing:true);
    }
    internal void SetOrigin(EditorOrigin origin)
    {
        CommitEditor(new(Items,origin,LastGroup)); Say("放置原点已保存（家具保持原位）");
    }
    internal void MoveOrigin(float dx,float dy,float dz)
    {
        RequireEditor();
        if(new[]{dx,dy,dz}.Any(v=>!float.IsFinite(v) || Math.Abs(v)>100000))
            throw new ArgumentOutOfRangeException(nameof(dx),"原点位移须为有限数值且不超过 100000");
        SetOrigin(Origin with { X=Origin.X+dx,Y=Origin.Y+dy,Z=Origin.Z+dz });
    }
    internal void DeleteGroup(int[] ids)
    {
        RequireEditor(); ArgumentNullException.ThrowIfNull(ids);
        var remove=ids.ToHashSet();
        if(remove.Count==0 || remove.Any(id=>!local.ContainsKey(id))) throw new InvalidOperationException("请选择仍存在的本地家具");
        CommitEditor(new(Items.Where(p=>!remove.Contains(p.Id)).ToArray(),Origin,LastGroup.Where(id=>!remove.Contains(id)).ToArray()));
        Say("已删除选中的 "+remove.Count+" 件家具，可一次撤销");
    }
    internal void OriginAtPlayer()
    {
        var t=TipMsgCore.i.mainplayer?.transform ?? throw new InvalidOperationException("角色尚未就绪");
        SetOrigin(new(t.position.x,t.position.y,t.position.z,t.eulerAngles.x,t.eulerAngles.y,t.eulerAngles.z));
    }
    internal void OriginAtFurniture(int id)
    {
        var p=local.TryGetValue(id,out var found) ? found : throw new InvalidOperationException("请先选中一件家具");
        SetOrigin(new(p.X,p.Y,p.Z,p.Pitch,p.Yaw,p.Roll));
    }
    internal int[] GeneratePattern(string shape,uint item,int count,float spacing,float radius)
    {
        RequireEditor();
        var points=HomeGeometry.Generate(shape,count,spacing,radius,Origin);
        int first=local.Count==0?1:checked(local.Keys.Max()+1);
        var added=points.Select((v,i)=>HomeGeometry.OrientPattern(shape,i,count,new Placement(checked(first+i),item,v.X,v.Y,v.Z,Origin.Yaw,Origin.Pitch,Origin.Roll).Rounded(),Origin)).ToArray();
        var group=added.Select(p=>p.Id).ToArray();
        CommitEditor(new(Items.Concat(added).ToArray(),Origin,group)); Say("已生成 "+group.Length+" 件家具，可整体变换或一次撤销");
        return group;
    }
    internal void TransformGroup(int[] ids,float dx,float dy,float dz,float rx,float ry,float rz,float scale,bool moveOrigin)
    {
        RequireEditor();
        var unique=ids.Distinct().ToArray();
        if(unique.Length==0) throw new InvalidOperationException("操作范围没有家具，请先生成或选择家具");
        if(unique.Any(id=>!local.ContainsKey(id))) throw new InvalidOperationException("操作范围包含已删除的家具，请重新选择");
        var transformed=HomeGeometry.Transform(unique.Select(id=>local[id]).ToArray(),Origin,dx,dy,dz,rx,ry,rz,scale).ToDictionary(p=>p.Id);
        var origin=moveOrigin ? Origin with {X=Origin.X+dx,Y=Origin.Y+dy,Z=Origin.Z+dz} : Origin;
        CommitEditor(new(Items.Select(p=>transformed.TryGetValue(p.Id,out var updated)?updated:p).ToArray(),origin,LastGroup));
        Say("已围绕原点变换 "+unique.Length+" 件家具");
    }
    private void RestoreHistory(List<EditorState> source,List<EditorState> destination,string message)
    {
        RequireEditor(); if(source.Count==0) throw new InvalidOperationException("没有可"+message+"的操作");
        var next=source[^1]; CheckMechanismItems(next.Items); var before=EditorSnapshot();
        SaveEditor(next); Remember(destination,before); source.RemoveAt(source.Count-1); ApplyEditor(next);
        if(!before.Items.SequenceEqual(next.Items)) MarkDirty(editing:true);
        Say("已"+message);
    }
    internal void Undo() => RestoreHistory(undo,redo,"撤销");
    internal void Redo() => RestoreHistory(redo,undo,"重做");
}
