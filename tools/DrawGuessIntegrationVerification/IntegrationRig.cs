using System.Diagnostics;
using System.Reflection;
using Chat.Client;
using DrawGuessModule;
using DrawGuessModule.Core;

internal sealed class IntegrationRig : IDisposable
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    readonly mod.st.NetChater previousHost=mod.st.NetChater.Instance;
    readonly Panel previousPanel=PaintingSystem.Current;
    readonly Il2CppInutan.IHomeSystem? previousHome=Il2CppInutan.IHomeSystem.sys;
    readonly string previousName=mod.TipMsgCore.i.mainplayername;
    public readonly mod.st.NetChater[] Hosts=Enumerable.Range(0,3).Select(_=>new mod.st.NetChater()).ToArray();
    public readonly MainLogic[] Logic=new MainLogic[3];
    public readonly Panel[] Panels=Enumerable.Range(0,3).Select(i=>new Panel{InitialBoardPlacement=new(){X=10+i*20,Y=2,Z=30+i*10,Yaw=i*45}}).ToArray();
    public readonly int?[] Channels={701,701,701};
    public readonly string Prefix="runtime"+Guid.NewGuid().ToString("N")[..8];
    public int Checks;
    public IntegrationRig(){for(int i=0;i<3;i++){HostContext.Select(Hosts[i]);Logic[i]=new MainLogic();}}
    public void Run(int i,Action action){HostContext.Select(Hosts[i]);PaintingSystem.Current=Panels[i];mod.TipMsgCore.i.mainplayername=Prefix+i;Il2CppInutan.IHomeSystem.sys=Channels[i].HasValue?new(){_channelId_k__BackingField=Channels[i]!.Value}:null;Hosts[i].Tick();action();}
    public void Command(int i,string text)=>Run(i,()=>Logic[i].Command(text));
    public T? Field<T>(int i,string name) where T:class=>(T?)typeof(MainLogic).GetField(name,Flags)!.GetValue(Logic[i]);
    public bool Ready(int i)=>(bool)typeof(MainLogic).GetProperty("ChannelReady",Flags)!.GetValue(Logic[i])!;
    public bool Busy(int i)=>(bool)typeof(MainLogic).GetField("_busy",Flags)!.GetValue(Logic[i])!;
    public GameRoomInfo? Room(int i)=>Field<GameRoomInfo>(i,"_room");
    public GameSnapshot? View(int i)=>Field<GameSnapshot>(i,"_view");
    public ChatClient Client(int i)=>Hosts[i].Client!;
    public void Check(bool value,string name){if(!value)throw new Exception("BOARD RUNTIME FAIL: "+name);Console.WriteLine("BOARD RUNTIME PASS: "+name);Checks++;}
    public async Task Until(Func<bool> condition,string label,int seconds=12){var clock=Stopwatch.StartNew();while(clock.Elapsed.TotalSeconds<seconds){for(int i=0;i<3;i++){int id=i;Run(id,()=>Logic[id].Update());}if(condition())return;await Task.Delay(10);}throw new Exception("Timeout: "+label+" | "+string.Join(" / ",Panels.Select(p=>p.Status+" "+p.Detail)));}
    public async Task Settle(){var clock=Stopwatch.StartNew();await Until(()=>clock.Elapsed.TotalMilliseconds>=250,"settle network queues");}
    public async Task Connect(int port){for(int i=0;i<3;i++){int id=i;Run(i,()=>Logic[id].Start());Command(i,$"dg connect 127.0.0.1 {port}");}await Until(()=>Enumerable.Range(0,3).All(Ready),"connect production runtimes");}
    public void Dispose(){for(int i=0;i<3;i++){int id=i;Run(id,()=>Logic[id].Dispose());Hosts[i].Dispose();}HostContext.Select(previousHost);PaintingSystem.Current=previousPanel;Il2CppInutan.IHomeSystem.sys=previousHome;mod.TipMsgCore.i.mainplayername=previousName;}
}
