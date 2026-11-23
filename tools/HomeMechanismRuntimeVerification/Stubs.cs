using System.Text.Json;
namespace mod {
 internal struct Vec { public float x,y,z; public Vec(float a,float b,float c) {x=a;y=b;z=c;} }
 internal class Transform { internal Vec position; }
 internal class Player { internal Transform transform=new(); }
 internal class TipMsgCore { internal static TipMsgCore i=new(); internal Player? mainplayer; }
}
namespace LocalHomeModule {
 internal sealed partial class HomeRuntime {
  partial void GuardMechanismEdit();
  partial void CheckMechanismItems(Placement[] items);
  internal bool Available=true; private bool enabled=true,online=true; private int revision=1; private string room="test";
  private class Network { internal object? Client=new(); } private Network net=new();
  internal Placement[] Items {get;set;}=new[]{new Placement(1,12,0,0,0,0),new Placement(2,12,1,0,0,0)};
  internal string SavePath=Path.Combine(Path.GetTempPath(),"MechanismRuntimeTests",Guid.NewGuid().ToString("N"),"layout.json");
  internal List<string[]> Published=new(); internal string Notice=""; private void Say(string s)=>Notice=s;
  internal Task<bool> NextPublish=Task.FromResult(true);
  private Task<bool> Publish(string[] frames,object? client,int revision,string room) {Published.Add(frames);return NextPublish;}
  internal void EditForTest(Placement[] items) { GuardMechanismEdit(); CheckMechanismItems(items); Items=items; }
  internal void TickForTest()=>TickMechanisms(); internal void LoadForTest()=>LoadMechanisms();
  internal bool ReceiveForTest(string owner,string text)=>ReceiveMechanisms(owner,text);
  internal Dictionary<string,long> Starts=>new(mechanismStarts); internal long Frozen=>mechanismFrozen;
   internal Dictionary<string,long> DebugStarts=>new(debugStarts); internal long DebugFrozen=>debugFrozen;
  internal MechanismDefinition[] RemoteNodesForTest(string owner)=>remoteMechanisms[owner].Nodes;
   internal int RemoteCount=>remoteMechanisms.Count; internal long RemoteRevision(string owner)=>remoteMechanisms[owner].Revision;
  internal Placement[] RemoteForTest(string owner,Placement[]? fallback=null)=>AnimatedRemote(owner,fallback??Array.Empty<Placement>(),UtcMs);
  internal Placement[] LocalForTest()=>AnimatedLocal(UtcMs);
  internal string ConfigPath=>MechanismPath;
  internal void ReadyPublish()=>mechanismPublishAt=DateTime.MinValue;
  internal void AgeChunk(string owner,int seconds=31)=>mechanismChunks[owner].At=DateTime.UtcNow.AddSeconds(-seconds);
  internal void EventsForTest(long now)=>TickMechanismEvents(now);
   internal void ClockForTest(string id,long start) { ActiveStarts[id]=start; ActiveEventProgress.Remove(id); }
   internal int EventCountForTest=>ActiveEventProgress.Count;
    internal long MechanismRevisionForTest=>mechanismRevision;
   internal int ChunkCount=>mechanismChunks.Count;
  internal void ChannelResetForTest(){remoteMechanisms.Clear();mechanismChunks.Clear();MarkMechanisms();online=false;}
 }
}
