using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chat.Client;
using Il2CppInutan;
using Il2CppPb;
using MelonLoader;
using mod;
using mod.st;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LocalHomeModule;

internal sealed partial class HomeRuntime : IDisposable
{
    private readonly NetChater net=NetChater.Instance;
    private readonly OriginMarker originMarker=new();
    private readonly Dictionary<int,Placement> local=new();
    private readonly Dictionary<string,Peer> peers=new(StringComparer.Ordinal);
    private readonly Dictionary<(string Owner,int Id),Visual> visuals=new();
    private const string LocalOwner="\0";
    private HomeSystem? home;
    private int scene=-1, revision=-1;
    private string character="", room="";
    private volatile bool enabled, disposed;
    private bool online, dirty;
    private int createBudget;
    private DateTime publishAt, quietUntil;
    private bool requestPeers, sendingObserved=true, forceSnapshot=true;
    private Placement[] published=Array.Empty<Placement>(), inFlight=Array.Empty<Placement>();
    private string publishedBatch="", inFlightBatch="";
    private int syncGeneration, inFlightGeneration;
    private void ResetSync()
    {
        syncGeneration++; published=Array.Empty<Placement>(); publishedBatch=""; forceSnapshot=true;
    }
    private void MarkDirty(bool editing=false)
    {
        dirty=true;
        if(editing) quietUntil=DateTime.UtcNow.AddSeconds(.8);
    }
    private Task<bool> sending=Task.FromResult(true);
    private static long nextId=-1000000000000L-DateTime.UtcNow.Ticks%1000000000L;
    private string SavePath => Path.Combine(AppContext.BaseDirectory,"UserData","LocalHome",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(character)))+".json");
    private sealed class Peer
    {
        internal readonly SnapshotReceiver Receiver=new();
        internal Placement[] Items=Array.Empty<Placement>();
        internal DateTime RequestedAt;
    }
    private sealed class Visual
    {
        internal long Id;
        internal Placement Placement=null!;
        internal HomeItem_Furniture? Handle;
        internal DateTime Created;
        internal bool Retired;
        internal UnityEngine.Vector3 BaseScale=UnityEngine.Vector3.one;
    }
    internal HomeRuntime() { net.OnMessageReceived+=Receive; net.OnChannelEvent+=ChannelEvent; }
    private bool InHome => SceneManager.GetActiveScene().name == "Home_Day_001_Modify" && IHomeSystem.sys != null;
    internal string EditorIdentity => character+":"+selectionEpoch;
    internal bool Enabled => enabled;
    internal bool Available => InHome && !string.IsNullOrEmpty(TipMsgCore.i.mainplayername);
    internal Placement[] Items => local.Values.OrderBy(p=>p.Id).ToArray();
    internal string Notice { get; private set; } = "点击开启，读取本地布局";
    internal string Status => !Available ? "请先进入家园" : (enabled ? "已开启" : "已关闭") + " · " + local.Count + " 件 · " + (net.ChannelReady ? "频道已连接" : "离线编辑") + " · 远端 " + peers.Count + " 人";
    internal void Execute(params string[] args)
    {
        try
        {
            string before=Notice;
            Command(new[]{"lh"}.Concat(args).ToArray());
            if(Notice==before && args.Length>0 && new[]{"move","del","clear","reload"}.Contains(args[0])) Say("操作完成，布局已更新");
        }
        catch(Exception e) { Say(e.Message); }
    }
    private void Say(string text) { Notice=text; MelonLogger.Msg("[LocalHome] "+text); }
    internal void Tick()
    {
        if(disposed) return;
        string who=TipMsgCore.i.mainplayername ?? "";
        int currentScene=SceneManager.GetActiveScene().handle;
        var current=InHome ? IHomeSystem.sys : null;
        string channel=net.DesiredChannel;
        if(home != current || scene!=currentScene || character!=who)
        {
            if(home!=null && enabled && (current==null || character!=who)) Withdraw();
            ClearVisuals(); peers.Clear(); home=current; scene=currentScene;
            ClearMechanisms();
            if(character!=who) { character=who; local.Clear(); enabled=false; ResetEditor(); ClearMechanisms(true); }
            ResetSync(); MarkDirty(); online=false;
        }
        if(revision!=net.ChannelRevision || room!=channel || (online && !net.ChannelReady))
        {
            revision=net.ChannelRevision; room=channel; peers.Clear(); remoteMechanisms.Clear(); mechanismChunks.Clear(); MarkMechanisms(); RemoveRemote(); online=false; ResetSync();
        }
        if(sending.IsCompleted && !sendingObserved)
        {
            sendingObserved=true; publishAt=DateTime.UtcNow.AddSeconds(3);
            if(sending.IsFaulted || sending.IsCanceled) { forceSnapshot=true; requestPeers=true; MarkDirty(); Say("同步失败，稍后重试："+sending.Exception?.GetBaseException().Message); }
            else if(!sending.Result) { forceSnapshot=true; requestPeers=true; MarkDirty(); }
            else if(inFlightGeneration==syncGeneration) { published=inFlight; publishedBatch=inFlightBatch; }
        }
        if(enabled && home!=null && net.ChannelReady && !online) { online=true; ResetSync(); requestPeers=true; MarkDirty(); }
        originMarker.Update(enabled && home!=null,Origin);
        if(!enabled || home==null) return;
        createBudget=4;
        TickMechanisms();
        long mechanismNow=UtcMs;
        Reconcile(LocalOwner,AnimatedLocal(mechanismNow));
        foreach(var peer in peers) Reconcile(peer.Key,AnimatedRemote(peer.Key,peer.Value.Items,mechanismNow));
        foreach(var remote in remoteMechanisms.Where(p=>!peers.ContainsKey(p.Key))) Reconcile(remote.Key,AnimatedRemote(remote.Key,Array.Empty<Placement>(),mechanismNow));
        foreach(var pair in visuals.ToArray())
        {
            var v=pair.Value;
            if(v.Handle==null && home.allFurnitures.TryGetValue(v.Id,out var found))
            {
                v.Handle=found;
                v.BaseScale=found.gameObject.transform.localScale;
                // Detach only our exact handle from the game's furniture registry.
                home.UnRegisterFurniture(found);
            }
            if(v.Handle!=null)
            {
                if(v.Retired) { v.Handle.Destroy(); visuals.Remove(pair.Key); }
                else
                {
                    var p=v.Placement;
                    v.Handle.SetPosRot(new UnityEngine.Vector3(p.X,p.Y,p.Z),p.Yaw);
                    v.Handle.gameObject.transform.rotation=Quaternion.Euler(p.Pitch,p.Yaw,p.Roll);
                    v.Handle.gameObject.transform.localScale=v.BaseScale*p.Scale;
                }
            }
            else if(!v.Retired && DateTime.UtcNow-v.Created>TimeSpan.FromSeconds(20))
            { v.Retired=true; Say("家具加载超时："+v.Placement.Item+"；可用 lh reload 重试"); }
        }
        var now=DateTime.UtcNow;
        if(online) foreach(var peer in peers.Values)
        {
            peer.Receiver.Accept(null,now);
            if(peer.Receiver.NeedsSnapshot && now-peer.RequestedAt>=TimeSpan.FromSeconds(10))
            { peer.RequestedAt=now; requestPeers=true; MarkDirty(); }
        }
        if(online && sendingObserved && sending.IsCompleted && dirty && DateTime.UtcNow>=publishAt && DateTime.UtcNow>=quietUntil)
        {
            var snapshot=Items;
            var frames=forceSnapshot || publishedBatch.Length==0 ? LayoutCodec.Encode(snapshot) : LayoutCodec.EncodeDelta(published,snapshot,publishedBatch);
            dirty=false; forceSnapshot=false;
            if(frames.Length==0 && !requestPeers) return;
            inFlight=snapshot; inFlightBatch=frames.Length==0 ? publishedBatch : LayoutCodec.Decode(frames[0])!.Batch; inFlightGeneration=syncGeneration;
            if(requestPeers) frames=new[]{LayoutCodec.Prefix+JsonSerializer.Serialize(new Frame("request",Guid.NewGuid().ToString("N")))}.Concat(frames).ToArray();
            requestPeers=false; sendingObserved=false;
            sending=Publish(frames,net.Client!,revision,room);
        }
    }
    private async Task<bool> PublishAfter(Task previous,string[] frames,ChatClient client,int rev,string channel,bool withdrawal=false)
    {
        try { await previous.ConfigureAwait(false); } catch { /* Empty snapshot supersedes a failed transfer. */ }
        for(int attempt=0; ; attempt++)
        {
            try
            {
                if(await Publish(frames,client,rev,channel,withdrawal).ConfigureAwait(false)) return true;
                if(!withdrawal || attempt>=2 || rev!=net.ChannelRevision || channel!=net.DesiredChannel || net.Client!=client) return false;
            }
            catch when(withdrawal && attempt<2) { }
            await Task.Delay(3000).ConfigureAwait(false);
        }
    }
    private async Task<bool> Publish(string[] frames, ChatClient client,int rev,string channel,bool withdrawal=false)
    {
        // Borrow the host connection; serialize with its room transitions.
        await net.ChannelGate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach(var frame in frames)
            {
                if(((disposed || !enabled) && !withdrawal) || !net.ChannelReady || rev!=net.ChannelRevision || net.Client!=client || channel!=net.DesiredChannel) return false;
                await client.SendAsync(channel,frame).ConfigureAwait(false);
            }
            return true;
        }
        finally { net.ChannelGate.Release(); }
    }
    private void Receive(ChatMessage message)
    {
        if(!enabled || home==null || !net.ChannelReady || message.Channel!=room || message.From==net.Nick) return;
        if(ReceiveMechanisms(message.From,message.Text)) return;
        var frame=LayoutCodec.Decode(message.Text); if(frame==null) return;
        if(frame.Kind=="request") { forceSnapshot=true; MarkDirty(); return; }
        if(!peers.TryGetValue(message.From,out var peer))
        {
            if(peers.Count>=16)
            {
                var inactive=peers.FirstOrDefault(p=>p.Value.Items.Length==0);
                if(inactive.Key==null) return;
                peers.Remove(inactive.Key);
            }
            peers.Add(message.From,peer=new Peer());
        }
        var result=peer.Receiver.Accept(frame,DateTime.UtcNow);
        if(result!=null)
        {
            peer.Items=result;
            if(result.Length==0) { remoteMechanisms.Remove(message.From); mechanismChunks.Remove(message.From); RemoveOwner(message.From); }
        }
        if(peer.Receiver.NeedsSnapshot && DateTime.UtcNow-peer.RequestedAt>=TimeSpan.FromSeconds(10))
        { peer.RequestedAt=DateTime.UtcNow; requestPeers=true; MarkDirty(); }
    }
    private void ChannelEvent(ChannelEventInfo evt)
    {
        if(evt.Channel!=room) return;
        if(evt.Type=="leave") { peers.Remove(evt.Nick); remoteMechanisms.Remove(evt.Nick); mechanismChunks.Remove(evt.Nick); RemoveOwner(evt.Nick); }
        else if(evt.Type=="join") { forceSnapshot=true; MarkDirty(); MarkMechanisms(); }
    }
    private void Reconcile(string owner,IEnumerable<Placement> values)
    {
        var desired=values.ToDictionary(p=>(Owner:owner,Id:p.Id));
        foreach(var pair in visuals.Where(p=>p.Key.Owner==owner).ToArray())
            if(!desired.ContainsKey(pair.Key) || desired[pair.Key].Item!=pair.Value.Placement.Item) Retire(pair.Key,pair.Value);
        foreach(var pair in desired)
        {
            if(visuals.TryGetValue(pair.Key,out var exists)) { exists.Placement=pair.Value; continue; }
            if(createBudget<=0) break;
            createBudget--;
            long id;
            do { id=--nextId; } while(home!.allFurnitures.ContainsKey(id));
            var p=pair.Value;
            var visual=new Visual { Id=id,Placement=p,Created=DateTime.UtcNow };
            visuals.Add(pair.Key,visual);
            try
            {
                home!.CreateAndPutDown(new FurnitureDetailsInfo {
                    FurnitureId=id,FurnitureItemId=p.Item,
                    Pos=ObjectHelper.ToNewPBVector3(new UnityEngine.Vector3(p.X,p.Y,p.Z)),
                    Rotation=ObjectHelper.ToNewPBVector3(new UnityEngine.Vector3(p.Pitch,p.Yaw,p.Roll))
                },0);
            }
            catch(Exception e) { visual.Retired=true; Say("创建失败："+e.Message); }
        }
    }
    private void Retire((string Owner,int Id) key,Visual v)
    {
        v.Retired=true;
        if(v.Handle==null && home!=null && home.allFurnitures.TryGetValue(v.Id,out var found))
        { v.Handle=found; home.UnRegisterFurniture(found); }
        if(v.Handle!=null) { v.Handle.Destroy(); visuals.Remove(key); }
    }
    private void RemoveOwner(string owner)
    { foreach(var pair in visuals.Where(p=>p.Key.Owner==owner).ToArray()) Retire(pair.Key,pair.Value); }
    private void RemoveRemote()
    { foreach(var pair in visuals.Where(p=>p.Key.Owner!=LocalOwner).ToArray()) Retire(pair.Key,pair.Value); }
    private void ClearVisuals()
    {
        var previous=home;
        foreach(var pair in visuals.ToArray()) Retire(pair.Key,pair.Value);
        var pending=visuals.Values.Where(v=>v.Handle==null).Select(v=>v.Id).ToArray();
        visuals.Clear();
        if(previous!=null && pending.Length>0) MelonCoroutines.Start(CleanupLate(previous,pending));
    }
    private static System.Collections.IEnumerator CleanupLate(HomeSystem previous,long[] ids)
    {
        var remaining=new HashSet<long>(ids);
        while(previous!=null && remaining.Count>0)
        {
            foreach(long id in remaining.ToArray())
                if(previous.allFurnitures.TryGetValue(id,out var item))
                { previous.UnRegisterFurniture(item); item.Destroy(); remaining.Remove(id); }
            yield return null;
        }
    }
    private void Save(IEnumerable<Placement> values) => SaveEditor(new EditorState(values.ToArray(),Origin,LastGroup));
    private void Load()
    {
        var state=ReadEditor();
        ApplyEditor(state); selectionEpoch++; undo.Clear(); redo.Clear(); LoadMechanisms();
    }
    private void Change(Action<Dictionary<int,Placement>> change)
    {
        var next=new Dictionary<int,Placement>(local); change(next);
        CommitEditor(new EditorState(next.Values.ToArray(),Origin,LastGroup.Where(next.ContainsKey).ToArray()));
    }
    internal void Command(string[] a)
    {
        string verb=a.Length>1 ? a[1] : "help";
        if(verb=="help") { Say("lh ui on/off | lh on/off | add 配置ID [x y z yaw 或 x y z rx ry rz] | move 实例ID x y z yaw 或 x y z rx ry rz [scale] | scale 实例ID 倍率(0.01~100) | del 实例ID | clear yes | list | save | reload | sync。省略 add 坐标时放在原点。undo/redo 撤销/重做。mechanism start [ID]/end/reset/debug on或off/trigger ID。修改自动保存。日志查看结果。"); return; }
        Tick();
        if(verb=="off")
        {
            if(enabled) Withdraw();
            enabled=false; online=false; dirty=false; ClearMechanisms(); ClearVisuals(); peers.Clear(); Say("本地家园已关闭，已安排撤回远端布局"); return;
        }
        if(home==null || string.IsNullOrEmpty(character)) throw new InvalidOperationException("请先进入家园场景");
        if(verb=="on") { if(!enabled) { Load(); enabled=true; online=false; MarkDirty(); } Say("本地家园已开启，布局 "+local.Count+" 件"); return; }
        if(!enabled) throw new InvalidOperationException("请先 lh on");
        float F(int i)=>float.Parse(a[i],NumberStyles.Float,CultureInfo.InvariantCulture);
        int Id(int i)=>int.Parse(a[i],CultureInfo.InvariantCulture);
        switch(verb)
        {
            case "add" when a.Length==3 || a.Length==7 || a.Length==9:
                int id=local.Count==0?1:checked(local.Keys.Max()+1);
                var p=new Placement(id,uint.Parse(a[2],CultureInfo.InvariantCulture),a.Length>=7?F(3):Origin.X,a.Length>=7?F(4):Origin.Y,a.Length>=7?F(5):Origin.Z,a.Length==9?F(7):a.Length==7?F(6):Origin.Yaw,a.Length==9?F(6):a.Length==7?0:Origin.Pitch,a.Length==9?F(8):a.Length==7?0:Origin.Roll);
                Change(d=>d.Add(id,p)); Say("新增实例 "+id); break;
            case "move" when a.Length==7 || a.Length==9 || a.Length==10:
                int moving=Id(2); var old=local[moving]; Change(d=>d[moving]=old with {X=F(3),Y=F(4),Z=F(5),Pitch=a.Length>=9?F(6):old.Pitch,Yaw=a.Length>=9?F(7):F(6),Roll=a.Length>=9?F(8):old.Roll,Scale=a.Length==10?F(9):old.Scale}); break;
            case "scale" when a.Length==4:
                int scaling=Id(2); Change(d=>d[scaling]=local[scaling] with {Scale=F(3)}); Say("缩放已更新"); break;
            case "del" when a.Length==3: Change(d=> { if(!d.Remove(Id(2))) throw new ArgumentException("实例不存在"); }); break;
            case "clear" when a.Length==3 && a[2]=="yes": Change(d=>d.Clear()); break;
            case "list": foreach(var item in local.Values.OrderBy(p=>p.Id)) Say(JsonSerializer.Serialize(item)); Say("共 "+local.Count+" 件，远端 "+peers.Count+" 人"); break;
            case "mechanism" when a.Length>=3:
                switch(a[2])
                {
                    case "start": StartMechanisms(a.Length>3?a[3]:null); break;
                    case "end": EndMechanisms(); break;
                    case "reset": ResetMechanisms(); break;
                    case "debug" when a.Length==4: SetMechanismDebug(a[3]=="on"); break;
                    case "trigger" when a.Length==4: FireTrigger(a[3]); break;
                    default: throw new ArgumentException("lh mechanism start [ID] | end | reset | debug on/off | trigger ID");
                }
                break;
            case "undo": Undo(); break;
            case "redo": Redo(); break;
            case "save": Save(local.Values); Say("已保存："+SavePath); break;
            case "reload":
                GuardMechanismEdit(); var before=Items; Load(); ClearVisuals();
                if(!before.SequenceEqual(Items)) MarkDirty(editing:true);
                forceSnapshot=true; requestPeers=true; MarkDirty(); break;
            case "sync": forceSnapshot=true; MarkDirty(); Say(net.ChannelReady?"已安排同步（连续操作合并，发送至少间隔 3 秒）":"网络频道未就绪，本地编辑仍可用"); break;
            default: throw new ArgumentException("命令参数不正确，lh help 查看格式");
        }
    }
    private void Withdraw()
    {
        if(net.Client==null || revision!=net.ChannelRevision || room!=net.DesiredChannel) return;
        ResetSync();
        sending=PublishAfter(Task.WhenAll(sending,mechanismSending),LayoutCodec.Encode(Array.Empty<Placement>()),net.Client!,revision,room,withdrawal:true);
        sendingObserved=false;
    }
    public void Dispose()
    {
        if(enabled) Withdraw();
        disposed=true; originMarker.Dispose(); net.OnMessageReceived-=Receive; net.OnChannelEvent-=ChannelEvent;
        ClearVisuals(); peers.Clear();
        _=sending.ContinueWith(t=>MelonLogger.Warning("[LocalHome] "+t.Exception?.GetBaseException().Message),TaskContinuationOptions.OnlyOnFaulted);
    }
}

