using System.Text;
using System.Text.Json;
using LocalHomeModule;
static class Program {
 static int ok,fail; static void Check(bool x,string msg){if(!x)throw new Exception(msg);}
 static void Test(string s,Action test){try{test();Console.WriteLine("PASS "+s);ok++;}catch(Exception e){Console.WriteLine("FAIL "+s+": "+e.Message);fail++;}}
 static MechanismDefinition Node(string id="A",int furniture=1,string? parent=null)=>new(id,id,"track",new(),parent,new[]{furniture},new[]{new MechanismPoint(),new MechanismPoint(100,0,0)},Duration:10);
 static HomeRuntime New(){mod.TipMsgCore.i.mainplayer=null;var h=new HomeRuntime();h.UpsertMechanism(Node());h.TickForTest();return h;}
 static long Now=>DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
 static string Data(long rev,MechanismDefinition[]? nodes=null,Placement[]? items=null,Dictionary<string,long>? starts=null,long frozen=0)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {Revision=rev,Nodes=nodes??new[]{Node()},Items=items??new[]{new Placement(1,12,0,0,0,0)},Starts=starts??new(),Frozen=frozen})));
 static string Frame(string batch,int index,int count,string data)=>"LHM1:"+JsonSerializer.Serialize(new{Batch=batch,Index=index,Count=count,Data=data});
 static void Send(HomeRuntime h,long rev,string owner="other",Dictionary<string,long>? starts=null)=>h.ReceiveForTest(owner,Frame(Guid.NewGuid().ToString("N"),0,1,Data(rev,starts:starts)));
 static int Main(){
 Test("config disk roundtrip restores nodes/triggers but no runtime starts",()=>{var h=New();h.UpsertTrigger(new("T","T",new(),2,new[]{"A"}));h.StartMechanisms();var r=new HomeRuntime{SavePath=h.SavePath};r.LoadForTest();Check(r.Mechanisms.Length==1&&r.Triggers.Length==1&&r.Starts.Count==0,"roundtrip");});
 Test("start separate nodes and descendants; end prohibits restart; reset",()=>{var h=New();h.UpsertMechanism(Node("B",2));h.StartMechanisms("A");Check(h.Starts.Count==1,"A");h.StartMechanisms("B");Check(h.Starts.Count==2,"B");var a=h.Starts["A"];h.StartMechanisms("A");Check(h.Starts["A"]>=a,"explicit restart moved backwards");h.EndMechanisms();Check(h.Frozen>0,"end");bool threw=false;try{h.StartMechanisms();}catch(InvalidOperationException){threw=true;}Check(threw,"start after end");h.ResetMechanisms();Check(h.Starts.Count==0&&h.Frozen==0,"reset");});
 Test("debug start/end/reset retains network starts and frozen",()=>{var h=New();h.StartMechanisms();var start=h.Starts["A"];h.EndMechanisms();long frozen=h.Frozen;h.SetMechanismDebug(true);h.StartMechanisms();h.EndMechanisms();h.ResetMechanisms();Check(h.Starts["A"]==start&&h.Frozen==frozen,"network modified");h.SetMechanismDebug(false);Check(h.Starts["A"]==start,"exit debug");});
 Test("debug baseline edit must retain normal network state",()=>{var h=New();h.StartMechanisms();var start=h.Starts["A"];h.SetMechanismDebug(true);bool blocked=false;try{h.EditForTest(h.Items.Select(p=>p with{X=p.X+1}).ToArray());}catch(InvalidOperationException){blocked=true;}Check(blocked,"baseline editing not guarded");h.TickForTest();Check(h.Starts.TryGetValue("A",out long retained)&&retained==start,"network starts cleared in debug by baseline change");});
 Test("trigger stays edge based; baseline reset must rearm occupied trigger",()=>{var h=New();h.UpsertTrigger(new("T","T",new(),2,new[]{"A"}));mod.TipMsgCore.i.mainplayer=new();h.TickForTest();Check(h.Starts.Count==1,"initial enter");long first=h.Starts["A"];h.TickForTest();Check(h.Starts["A"]==first,"repeated enter");h.ResetMechanisms();h.EditForTest(h.Items.Select(p=>p with{X=p.X+1}).ToArray());h.TickForTest();Check(h.Starts.Count==1,"baseline resets starts but trigger remains latched");});
 Test("saved trigger must detach input targets",()=>{var h=New();var targets=new[]{"A"};h.UpsertTrigger(new("T","T",new(),2,targets));targets[0]="MISSING";Check(h.Triggers[0].Targets[0]=="A","mutable Targets alias changed saved in-memory config");});
 Test("chunks reordered complete and stale revisions ignored",()=>{var h=New();string data=Data(2),batch=Guid.NewGuid().ToString("N");int cut=data.Length/2;h.ReceiveForTest("x",Frame(batch,1,2,data[cut..]));Check(h.RemoteCount==0,"partial commit");h.ReceiveForTest("x",Frame(batch,0,2,data[..cut]));Check(h.RemoteRevision("x")==2,"did not commit");Send(h,1,"x");Check(h.RemoteRevision("x")==2,"stale accepted");});
 Test("expired chunk batch must not resurrect",()=>{var h=New();string data=Data(2),batch=Guid.NewGuid().ToString("N");int cut=data.Length/2;h.ReceiveForTest("x",Frame(batch,0,2,data[..cut]));h.AgeChunk("x");h.ReceiveForTest("x",Frame(batch,1,2,data[cut..]));h.ReceiveForTest("x",Frame(batch,0,2,data[..cut]));Check(h.RemoteCount==0,"expired batch resurrected by replaying old header fragment");});
 Test("null malformed packet is discarded; future poisoned revision rejected",()=>{var h=New();h.ReceiveForTest("x",Frame(Guid.NewGuid().ToString("N"),0,1,"%%%%"));Check(h.RemoteCount==0,"bad base64 accepted");Send(h,long.MaxValue,"x");Send(h,DateTime.UtcNow.Ticks,"x");Check(h.RemoteRevision("x")!=long.MaxValue,"long.MaxValue revision pins owner forever");});
 Test("expired incomplete owners do not exhaust lifetime sender slots",()=>{var h=New();for(int i=0;i<16;i++){h.ReceiveForTest("old"+i,Frame(Guid.NewGuid().ToString("N"),0,2,"AA"));h.AgeChunk("old"+i,61);}Send(h,1,"fresh");Check(h.RemoteCount==1,"16 expired owners permanently block fresh owner");});
 Test("remote state takes mechanism packet baseline independent of fallback",()=>{var h=New();Send(h,1);var items=h.RemoteForTest("other");Check(items.Single().X==0,"packet baseline");});
 Test("bound furniture removal is guarded against persisted ID reuse",()=>{var h=New();bool blocked=false;try{h.EditForTest(h.Items.Where(p=>p.Id!=1).ToArray());}catch(InvalidOperationException){blocked=true;}Check(blocked,"bound furniture removal must be guarded");});
 Test("async pending prevents parallel send and failure retries latest state",()=>{var h=New();var pending=new TaskCompletionSource<bool>();h.NextPublish=pending.Task;h.StartMechanisms();h.ReadyPublish();h.TickForTest();int count=h.Published.Count;h.ResetMechanisms();h.ReadyPublish();h.TickForTest();Check(h.Published.Count==count,"concurrent send");pending.SetException(new IOException("fake failure"));h.NextPublish=Task.FromResult(true);h.ReadyPublish();h.TickForTest();Check(h.Published.Count==count+1,"did not retry");var wire=JsonSerializer.Deserialize<JsonElement>(h.Published[^1][0][5..]);var packet=JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(Convert.FromBase64String(wire.GetProperty("Data").GetString()!)));Check(!packet.GetProperty("Starts").EnumerateObject().Any(),"retry used stale starts");});
 Test("parent start includes descendants and unrelated node stays inactive",()=>{var h=New();h.UpsertMechanism(Node("B",2));h.UpsertMechanism(Node("C",1,"A") with{FurnitureIds=Array.Empty<int>()});h.StartMechanisms("A");Check(h.Starts.ContainsKey("A")&&h.Starts.ContainsKey("C")&&!h.Starts.ContainsKey("B"),"subtree start scope");Check(h.Starts["A"]==h.Starts["C"],"subtree not same UTC start");});
 Test("new LH2 baseline takes priority until mechanism packet matches",()=>{var h=New();Send(h,1,starts:new(){["A"]=Now-1000});var fallback=new[]{new Placement(1,12,25,0,0,0)};Check(h.RemoteForTest("other",fallback).Single().X==25,"old packet overrode newer baseline");});
 Test("global skips trigger child subtree but preserves ancestor space transform",()=>{
   var h=New();h.UpsertMechanism(Node("B",2,"A"));h.UpsertMechanism(Node("C",1,"B") with{FurnitureIds=Array.Empty<int>()});h.UpsertMechanism(Node("D") with{FurnitureIds=Array.Empty<int>()});
   h.UpsertTrigger(new("T","T",new(),2,new[]{"B"}));h.StartMechanisms();
   Check(h.Starts.Keys.ToHashSet().SetEquals(new[]{"A","D"}),"global entered trigger subtree through ordinary parent");
   var items=MechanismEngine.Evaluate(h.Mechanisms,h.Items,h.Starts,h.Starts["A"]+5000);
   Check(Math.Abs(items.Single(p=>p.Id==2).X-6)<.001,"inactive child must inherit ancestor motion without running its own track");
   h.StartMechanisms();Check(!h.Starts.ContainsKey("B")&&!h.Starts.ContainsKey("C"),"repeat global entered reserved subtree");
   h.FireTrigger("T");Check(h.Starts.Keys.ToHashSet().SetEquals(new[]{"A","B","C","D"}),"trigger did not start reserved descendants");Check(h.Starts["B"]==h.Starts["C"],"trigger child subtree timestamps");
   h.EndMechanisms();Check(h.Frozen>0&&h.Starts.Count==4,"global end lost reserved started nodes");h.ResetMechanisms();Check(h.Starts.Count==0&&h.Frozen==0,"global reset did not reset trigger nodes");
  });
  Test("global excludes multiple targets including disabled trigger targets",()=>{
   var h=New();h.UpsertMechanism(Node("B",2));h.UpsertMechanism(Node("C",1,"B") with{FurnitureIds=Array.Empty<int>()});h.UpsertMechanism(Node("D") with{FurnitureIds=Array.Empty<int>()});h.UpsertMechanism(Node("E") with{FurnitureIds=Array.Empty<int>()});
   h.UpsertTrigger(new("T","T",new(),2,new[]{"B","D"}));h.UpsertTrigger(new("OFF","OFF",new(),2,new[]{"E"},false));h.StartMechanisms();
   Check(h.Starts.Keys.ToHashSet().SetEquals(new[]{"A"}),"multiple/disabled targets globally started");h.FireTrigger("T");Check(h.Starts.ContainsKey("B")&&h.Starts.ContainsKey("C")&&h.Starts.ContainsKey("D")&&!h.Starts.ContainsKey("E"),"explicit trigger scope wrong");
   h.StartMechanisms("E");Check(h.Starts.ContainsKey("E"),"manual debug start must permit disabled trigger target");
   var starts=h.Starts;h.StartMechanisms();Check(h.Starts.Count==starts.Count&&starts.All(p=>h.Starts[p.Key]==p.Value),"global altered already-triggered clocks");
  });
  Test("debug global uses same trigger exclusions and explicit starts still work",()=>{
   var h=New();h.UpsertMechanism(Node("B",2,"A"));h.UpsertMechanism(Node("C",1,"B") with{FurnitureIds=Array.Empty<int>()});h.UpsertMechanism(Node("D") with{FurnitureIds=Array.Empty<int>()});h.UpsertTrigger(new("T","T",new(),2,new[]{"B","D"},false));
   h.StartMechanisms("B");var network=h.Starts;h.SetMechanismDebug(true);h.StartMechanisms();
   Check(h.DebugStarts.Keys.ToHashSet().SetEquals(new[]{"A"}),"debug global activated reserved subtree/disabled targets");Check(h.Starts.Count==network.Count&&network.All(p=>h.Starts[p.Key]==p.Value),"debug global touched network starts");
   h.FireTrigger("T");Check(h.DebugStarts.Count==4&&h.DebugStarts["B"]==h.DebugStarts["C"],"debug trigger failed");h.EndMechanisms();Check(h.DebugFrozen>0,"debug global end");h.ResetMechanisms();Check(h.DebugStarts.Count==0&&h.DebugFrozen==0,"debug global reset");
   h.StartMechanisms("B");Check(h.DebugStarts.Keys.ToHashSet().SetEquals(new[]{"B","C"}),"explicit debug start must include subtree");
  });
  Console.WriteLine($"RESULT {ok} pass, {fail} fail");Test("draft preview does not modify saved nodes or network clocks",()=>{
    var h=New();h.StartMechanisms();var clocks=h.Starts.ToDictionary(p=>p.Key,p=>p.Value);var saved=JsonSerializer.Serialize(h.Mechanisms);
    var draft=h.Mechanisms[0] with{Kind="rotate",RotationInput="angle",RotationY=90,Duration=2};
    h.SetRotationPreview(draft,1);Check(h.RotationPreviewActive,"preview inactive");
    Check(Math.Abs(h.LocalForTest().Single(p=>p.Id==1).Yaw-90)<.01,"draft did not render at endpoint");
    h.SetRotationPreview(draft,.5f);Check(Math.Abs(h.LocalForTest().Single(p=>p.Id==1).Yaw-45)<.01,"progress did not update");
    Check(clocks.All(p=>h.Starts[p.Key]==p.Value),"preview changed starts");
    Check(JsonSerializer.Serialize(h.Mechanisms)==saved,"preview saved draft");
    h.ClearRotationPreview();Check(!h.RotationPreviewActive,"preview did not clear");
  });
  Test("vector clipboard parses complete triples and rejects malformed values",()=>{
    foreach(string text in new[]{"1, 2, 3","(1 2 3)","[1；2；3]","1\t2\t3","1，2，3"}) Check(VectorText.Parse(text)==new MechanismPoint(1,2,3),"format "+text);
    foreach(string text in new[]{"1,2","1,2,3,4","NaN,2,3","Infinity 2 3","100001,2,3","bad 2 3",""}) { bool rejected=false;try{VectorText.Parse(text);}catch(ArgumentException){rejected=true;}Check(rejected,"accepted "+text); }
    var p=new MechanismPoint(.12345679f,-99999.99f,1e-5f);Check(VectorText.Parse(VectorText.Format(p))==p,"precision roundtrip");
  });
  Console.WriteLine($"FINAL RESULT {ok} pass, {fail} fail");
  Test("new rotation settings persist and pass through mechanism packet",()=>{
    var h=New();var d=h.Mechanisms[0] with{Kind="rotate",RotationInput="angle",RotationStyle="orbit",RotationSpace="local",RotationReference=new(20,35,10),RotationY=90};
    h.UpsertMechanism(d);var restored=new HomeRuntime{SavePath=h.SavePath};restored.LoadForTest();
    var copy=restored.Mechanisms.Single();Check(copy.RotationStyle=="orbit"&&copy.RotationInput=="angle"&&copy.RotationSpace=="local"&&copy.RotationReference==d.RotationReference,"disk lost settings");
    h.ReceiveForTest("new-version",Frame(Guid.NewGuid().ToString("N"),0,1,Data(1,new[]{d},h.Items,new(){["A"]=Now-1000})));
    var actual=h.RemoteForTest("new-version",h.Items);Check(actual.Single(p=>p.Id==1).Yaw==h.Items.Single(p=>p.Id==1).Yaw,"orbit remote changed orientation");Check(h.RemoteCount==1,"new definition rejected");
  });
  Console.WriteLine($"ALL RESULT {ok} pass, {fail} fail");
  EventChecks.Run(Test,Check);
   Console.WriteLine($"EVENTS INCLUDED RESULT {ok} pass, {fail} fail");
   return fail==0?0:1;
 }
}
