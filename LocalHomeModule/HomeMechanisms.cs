using System.Text;
using System.Text.Json;
using mod;

namespace LocalHomeModule;

public record MechanismTrigger(string Id,string Name,MechanismPoint Position,float Radius,string[] Targets,bool Enabled=true);

internal sealed partial class HomeRuntime
{
    private sealed record MechanismConfig(MechanismDefinition[] Nodes,MechanismTrigger[] Triggers,int Version=1);
    private sealed record MechanismPacket(long Revision,MechanismDefinition[] Nodes,Placement[] Items,Dictionary<string,long> Starts,long Frozen=0);
    private sealed record MechanismChunk(string Batch,int Index,int Count,string Data);
    private sealed class ChunkBuffer { internal string Batch=""; internal string?[] Parts=Array.Empty<string?>(); internal DateTime At; }
    private MechanismConfig mechanismConfig=new(Array.Empty<MechanismDefinition>(),Array.Empty<MechanismTrigger>());
    private readonly Dictionary<string,long> mechanismStarts=new(), debugStarts=new();
    private readonly Dictionary<string,MechanismPacket> remoteMechanisms=new();
    private readonly Dictionary<string,ChunkBuffer> mechanismChunks=new();
    private readonly HashSet<string> triggerInside=new();
    private long mechanismFrozen,debugFrozen,mechanismRevision;
    private bool mechanismDebug,mechanismPending;
    private string mechanismItemsStamp="";
    private DateTime mechanismPublishAt;
    private Task<bool> mechanismSending=Task.FromResult(true);
    internal MechanismDefinition[] Mechanisms => JsonSerializer.Deserialize<MechanismDefinition[]>(JsonSerializer.Serialize(mechanismConfig.Nodes))!;
    internal MechanismTrigger[] Triggers => JsonSerializer.Deserialize<MechanismTrigger[]>(JsonSerializer.Serialize(mechanismConfig.Triggers))!;
    internal bool MechanismDebug => mechanismDebug;
    internal string MechanismStatus => (mechanismDebug?"本地独立调试（不联网）":"联网运行")+" · "+(ActiveFrozen>0?"已结束，保持状态":"系统 UTC 时间驱动")+" · "+mechanismConfig.Nodes.Length+" 个机关";
    private Dictionary<string,long> ActiveStarts => mechanismDebug?debugStarts:mechanismStarts;
    private long ActiveFrozen => mechanismDebug?debugFrozen:mechanismFrozen;
    private string MechanismPath => SavePath+".mechanisms.json";
    private static long UtcMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    internal string MechanismState(string id)
    {
        var node=mechanismConfig.Nodes.FirstOrDefault(n=>n.Id==id);
        if(node==null) return "启动前";
        if(ActiveFrozen>0 && ActiveStarts.ContainsKey(id)) return "结束";
        return MechanismEngine.State(node,ActiveStarts.GetValueOrDefault(id),UtcMs);
    }
    partial void CheckMechanismItems(Placement[] items)
    {
        var ids=items.Select(p=>p.Id).ToHashSet();
        if(mechanismConfig.Nodes.Any(n=>n.FurnitureIds.Any(id=>!ids.Contains(id))))
            throw new InvalidOperationException("请先在机关窗口解除家具挂载，再删除家具或撤销生成");
    }
    partial void GuardMechanismEdit()
    {
        if(mechanismDebug || mechanismStarts.Count>0 || debugStarts.Count>0)
            throw new InvalidOperationException("修改家具前请退出独立调试并重置机关");
    }
    private void RequireMechanisms()
    {
        if(!Available || !enabled) throw new InvalidOperationException("请先进入家园并开启本地家园");
    }
    private static MechanismTrigger[] ValidateTriggers(MechanismTrigger[] triggers,MechanismDefinition[] nodes)
    {
        if(triggers==null || triggers.Length>128) throw new ArgumentException("触发器数量上限128");
        var ids=new HashSet<string>(); var nodeIds=nodes.Select(n=>n.Id).ToHashSet();
        foreach(var t in triggers)
            if(t==null || string.IsNullOrWhiteSpace(t.Id) || t.Id.Length>80 || !ids.Add(t.Id) || t.Name==null || t.Name.Length>80 || t.Position==null ||
               new[]{t.Position.X,t.Position.Y,t.Position.Z,t.Radius}.Any(v=>!float.IsFinite(v)||Math.Abs(v)>100000) || t.Radius<=0 || t.Targets==null || t.Targets.Length>128 || t.Targets.Any(id=>!nodeIds.Contains(id)))
                throw new ArgumentException("触发器坐标、半径、编号或目标无效");
        return triggers;
    }
    private void SaveMechanisms(MechanismConfig config)
    {
        RequireMechanisms();
        if(mechanismStarts.Count>0 || debugStarts.Count>0) throw new InvalidOperationException("请先重置机关，再修改配置");
        var nodes=MechanismEngine.Validate(config.Nodes,Items); ValidateTriggers(config.Triggers,nodes);
        config=config with {Nodes=nodes,Triggers=config.Triggers.Select(t=>t with {Targets=(string[])t.Targets.Clone()}).ToArray()};
        string path=MechanismPath; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(config,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(path+".tmp",path,true); mechanismConfig=config; triggerInside.Clear(); MarkMechanisms();
    }
    internal void UpsertMechanism(MechanismDefinition node)
    { SaveMechanisms(mechanismConfig with {Nodes=mechanismConfig.Nodes.Where(n=>n.Id!=node.Id).Append(node).ToArray()}); }
    internal void DeleteMechanism(string id)
    {
        SaveMechanisms(new(mechanismConfig.Nodes.Where(n=>n.Id!=id).Select(n=>n with {ParentId=n.ParentId==id?null:n.ParentId,OnCompleteTargets=n.OnCompleteTargets?.Where(x=>x!=id).ToArray(),OnCycleTargets=n.OnCycleTargets?.Where(x=>x!=id).ToArray()}).ToArray(),
            mechanismConfig.Triggers.Select(t=>t with {Targets=t.Targets.Where(x=>x!=id).ToArray()}).ToArray()));
    }
    internal void UpsertTrigger(MechanismTrigger trigger)
    { SaveMechanisms(mechanismConfig with {Triggers=mechanismConfig.Triggers.Where(t=>t.Id!=trigger.Id).Append(trigger).ToArray()}); }
    internal void DeleteTrigger(string id)
    { SaveMechanisms(mechanismConfig with {Triggers=mechanismConfig.Triggers.Where(t=>t.Id!=id).ToArray()}); }
    internal void SetMechanismDebug(bool value)
    { RequireMechanisms(); mechanismDebug=value; debugStarts.Clear(); debugEventProgress.Clear(); debugFrozen=0; triggerInside.Clear(); Say(value?"独立调试已开启，操作不发送网络":"已返回联网机关状态"); }
    internal void StartMechanisms(string? id=null)
    {
        RequireMechanisms(); MechanismEngine.Validate(mechanismConfig.Nodes,Items);
        if(ActiveFrozen>0) throw new InvalidOperationException("请先重置，再重新启动");
        if(id!=null && !mechanismConfig.Nodes.Any(n=>n.Id==id)) throw new ArgumentException("机关不存在");
        // A trigger reserves its entire subtree, even while disabled. Global starts
        // must not activate it through an ordinary ancestor; spatial inheritance
        // still applies when that ancestor moves. Explicit/manual starts bypass this filter.
        var targets=id==null?mechanismConfig.Nodes.Select(n=>n.Id).ToHashSet():MechanismSubtree(new[]{id});
        if(id==null) targets.ExceptWith(MechanismSubtree(mechanismConfig.Triggers.SelectMany(t=>t.Targets).Concat(mechanismConfig.Nodes.SelectMany(n=>(n.OnCompleteTargets??Array.Empty<string>()).Concat(n.OnCycleTargets??Array.Empty<string>())))));
        StartMechanismTargets(targets,UtcMs+300,id!=null);
        Say(mechanismDebug?"调试启动":"已安排启动并同步，300ms后开始");
    }
    private HashSet<string> MechanismSubtree(IEnumerable<string> roots)
    {
        var targets=roots.ToHashSet();
        bool changed;
        do
        {
            changed=false;
            foreach(var node in mechanismConfig.Nodes)
                if(node.ParentId!=null && targets.Contains(node.ParentId)) changed|=targets.Add(node.Id);
        } while(changed);
        return targets;
    }
    internal void EndMechanisms()
    { RequireMechanisms(); if(mechanismDebug) { if(debugFrozen==0) debugFrozen=UtcMs; } else { if(mechanismFrozen==0) mechanismFrozen=UtcMs; MarkMechanisms(); } Say("所有机关已结束，保持当前状态"); }
    internal void ResetMechanisms()
    { RequireMechanisms(); ActiveStarts.Clear(); ActiveEventProgress.Clear(); if(mechanismDebug) debugFrozen=0; else { mechanismFrozen=0; MarkMechanisms(); } triggerInside.Clear(); Say("所有机关已恢复启动前"); }
    internal void FireTrigger(string id)
    { RequireMechanisms(); var t=mechanismConfig.Triggers.FirstOrDefault(t=>t.Id==id)??throw new ArgumentException("触发器不存在"); foreach(string target in t.Targets) StartMechanisms(target); }
    private void ClearMechanisms(bool config=false)
    {
        ClearRotationPreview();
        mechanismStarts.Clear(); debugStarts.Clear(); ClearMechanismEvents(); mechanismFrozen=debugFrozen=0; mechanismDebug=false;
        remoteMechanisms.Clear(); mechanismChunks.Clear(); triggerInside.Clear(); mechanismItemsStamp="";
        if(config) mechanismConfig=new(Array.Empty<MechanismDefinition>(),Array.Empty<MechanismTrigger>());
    }
    private void LoadMechanisms()
    {
        ClearMechanisms(true);
        try
        {
            if(File.Exists(MechanismPath))
            {
                var c=JsonSerializer.Deserialize<MechanismConfig>(File.ReadAllText(MechanismPath))??throw new InvalidDataException("机关存档为空");
                if(c.Version!=1) throw new InvalidDataException("机关存档版本无效");
                // Keep IDs safe when a layout has removed furniture since configuration was saved.
                var ids=Items.Select(p=>p.Id).ToHashSet();
                var nodes=c.Nodes.Select(n=>n with {FurnitureIds=n.FurnitureIds.Where(ids.Contains).ToArray()}).ToArray();
                mechanismConfig=new(MechanismEngine.Validate(nodes,Items),ValidateTriggers(c.Triggers,nodes));
            }
        }
        catch(Exception e) { Say("机关配置读取失败："+e.Message); }
        mechanismItemsStamp=JsonSerializer.Serialize(Items); MarkMechanisms();
    }
    private void MarkMechanisms() { mechanismRevision=Math.Max(mechanismRevision+1,DateTime.UtcNow.Ticks); mechanismPending=true; }
    private Placement[] AnimatedLocal(long now) => RotationPreviewFrame() ?? MechanismEngine.Evaluate(mechanismConfig.Nodes,Items,ActiveStarts,ActiveFrozen>0?ActiveFrozen:now);
    private Placement[] AnimatedRemote(string owner,Placement[] fallback,long now)
    {
        if(!remoteMechanisms.TryGetValue(owner,out var p)) return fallback;
        if(fallback.Length>0 && !p.Items.SequenceEqual(fallback)) return fallback;
        return MechanismEngine.Evaluate(p.Nodes,p.Items,p.Starts,p.Frozen>0?p.Frozen:now);
    }
    private void TickMechanisms()
    {
        long now=UtcMs;
        string stamp=JsonSerializer.Serialize(Items);
        if(stamp!=mechanismItemsStamp)
        {
            mechanismItemsStamp=stamp; var ids=Items.Select(p=>p.Id).ToHashSet();
            mechanismConfig=mechanismConfig with {Nodes=mechanismConfig.Nodes.Select(n=>n with {FurnitureIds=n.FurnitureIds.Where(ids.Contains).ToArray()}).ToArray()};
            mechanismStarts.Clear(); debugStarts.Clear(); ClearMechanismEvents(); mechanismFrozen=debugFrozen=0; triggerInside.Clear(); MarkMechanisms();
        }
        var pos=TipMsgCore.i.mainplayer?.transform.position;
        if(pos!=null && ActiveFrozen==0)
            foreach(var t in mechanismConfig.Triggers)
            {
                double dx=pos.Value.x-t.Position.X,dy=pos.Value.y-t.Position.Y,dz=pos.Value.z-t.Position.Z;
                bool inside=dx*dx+dy*dy+dz*dz<=(double)t.Radius*t.Radius;
                if(t.Enabled && inside && triggerInside.Add(t.Id)) FireTrigger(t.Id);
                if(!inside) triggerInside.Remove(t.Id);
            }
        TickMechanismEvents(now);
        if(!mechanismSending.IsCompleted) return;
        if(mechanismSending.IsFaulted || mechanismSending.IsCanceled || !mechanismSending.Result) { mechanismPending=true; mechanismSending=Task.FromResult(true); }
        if(!online || net.Client==null || DateTime.UtcNow<mechanismPublishAt || (!mechanismPending && DateTime.UtcNow<mechanismPublishAt.AddSeconds(10))) return;
        var packet=new MechanismPacket(mechanismRevision,mechanismConfig.Nodes,Items,new(mechanismStarts),mechanismFrozen);
        string data=Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(packet)));
        if(data.Length>2000000) { Say("机关同步配置过大"); mechanismPublishAt=DateTime.UtcNow.AddSeconds(10); return; }
        const int size=7000; int count=(data.Length+size-1)/size; string batch=Guid.NewGuid().ToString("N");
        var frames=Enumerable.Range(0,count).Select(i=>"LHM1:"+JsonSerializer.Serialize(new MechanismChunk(batch,i,count,data.Substring(i*size,Math.Min(size,data.Length-i*size))))).ToArray();
        mechanismSending=Publish(frames,net.Client,revision,room); mechanismPending=false; mechanismPublishAt=DateTime.UtcNow.AddSeconds(1);
    }
    private bool ReceiveMechanisms(string owner,string text)
    {
        if(!text.StartsWith("LHM1:",StringComparison.Ordinal)) return false;
        try
        {
            if(text.Length>12000) return true;
            var c=JsonSerializer.Deserialize<MechanismChunk>(text.Substring(5));
            if(c==null || c.Batch==null || c.Batch.Length!=32 || c.Count<1 || c.Count>286 || c.Index<0 || c.Index>=c.Count || c.Data==null || c.Data.Length>7000) return true;
            if(!mechanismChunks.TryGetValue(owner,out var b))
            {
                foreach(var expired in mechanismChunks.Where(p=>DateTime.UtcNow-p.Value.At>TimeSpan.FromSeconds(60)).Select(p=>p.Key).ToArray()) mechanismChunks.Remove(expired);
                if(mechanismChunks.Count>=16) return true; mechanismChunks[owner]=b=new();
            }
            if(b.Batch==c.Batch && DateTime.UtcNow-b.At>TimeSpan.FromSeconds(30)) return true;
            if(b.Batch!=c.Batch) { b.Batch=c.Batch; b.Parts=new string?[c.Count]; b.At=DateTime.UtcNow; }
            if(b.Parts.Length!=c.Count) return true;
            if(b.Parts[c.Index]!=null && b.Parts[c.Index]!=c.Data) { b.Batch=""; return true; }
            b.Parts[c.Index]=c.Data; if(b.Parts.Any(p=>p==null)) return true;
            var p=JsonSerializer.Deserialize<MechanismPacket>(Encoding.UTF8.GetString(Convert.FromBase64String(string.Concat(b.Parts))));
            if(p==null || p.Revision<=0 || p.Revision>DateTime.UtcNow.AddDays(1).Ticks || p.Starts==null || p.Frozen<0 || p.Frozen>UtcMs+86400000) return true;
            if(remoteMechanisms.TryGetValue(owner,out var old) && p.Revision<=old.Revision) return true;
            var items=LayoutCodec.Validate(p.Items); var nodes=MechanismEngine.Validate(p.Nodes,items); var ids=nodes.Select(n=>n.Id).ToHashSet();
            if(p.Starts.Count>128 || p.Starts.Any(s=>!ids.Contains(s.Key)||s.Value<=0||s.Value>UtcMs+86400000)) return true;
            remoteMechanisms[owner]=p with {Items=items,Nodes=nodes};
        }
        catch(Exception e) when(e is JsonException or ArgumentException or InvalidDataException or FormatException or OverflowException) { /* Discard malformed network state. */ }
        return true;
    }
}
