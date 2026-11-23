using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Chat.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Optional real-game verification. Requires the existing loopback command API and latest modules loaded.
internal static class LiveGameCheck
{
 public static async Task Run()
 {
  using var http=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:8080/api/"),Timeout=TimeSpan.FromSeconds(10)};
  async Task Command(string text)
  {
   using var content=new StringContent(JsonConvert.SerializeObject(new{command=text,target=text.StartsWith("net ",StringComparison.Ordinal)?"host":"logic",waitMs=2000}),Encoding.UTF8,"application/json");
   using var result=await http.PostAsync("command",content);result.EnsureSuccessStatusCode();
   var json=JObject.Parse(await result.Content.ReadAsStringAsync());
   if((string?)json["data"]?["status"]!="completed")throw new Exception("Game command failed: "+text+" "+json);
  }
  async Task Until(Func<bool> test,string name)
  {
   var time=Stopwatch.StartNew();while(time.Elapsed.TotalSeconds<15){if(test()){Console.WriteLine("LIVE PASS: "+name);return;}await Task.Delay(40);}throw new Exception("Live timeout: "+name);
  }
  var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
  var start=new ProcessStartInfo("dotnet"){UseShellExecute=false};
  foreach(var a in new[]{Path.GetFullPath("服务器/Chat.Server/bin/Release/net9.0/Chat.Server.dll"),"--host","127.0.0.1","--port",port.ToString()})start.ArgumentList.Add(a);
  using var server=Process.Start(start)??throw new Exception("No relay process");
  using var peer=new ChatClient();
  bool roomClosed=false; peer.GameRoomChanged+=e=>{if(e.Type=="closed")roomClosed=true;};
  var messages=new ConcurrentQueue<JObject>();GameRoomInfo? room=null;
  peer.GameMessageReceived+=m=>{try{var json=JObject.Parse(m.Text);foreach(var property in json.Properties().Where(p=>p.Value.Type==JTokenType.Null).ToArray())property.Remove();messages.Enqueue(json);}catch{}};
  string name="live"+Guid.NewGuid().ToString("N")[..8];
  try
  {
   await Until(()=>{try{using var c=new TcpClient();c.Connect("127.0.0.1",port);return true;}catch{return false;}},"relay ready");
   await peer.ConnectAsync("127.0.0.1",port,"LiveVerifier");
   await Command("dg leave"); await Command("dg mode guess");
   string gameRoot=Environment.GetEnvironmentVariable("DRAWGUESS_GAME_DIR") ??
    Path.GetDirectoryName(Process.GetProcessesByName("launcher").FirstOrDefault()?.MainModule?.FileName ?? throw new Exception("Set DRAWGUESS_GAME_DIR"))!;
   string logFile=Path.Combine(gameRoot,"MelonLoader","Latest.log");
   string ReadLog(){using var stream=new FileStream(logFile,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new StreamReader(stream);return reader.ReadToEnd();}
   int logStart=ReadLog().Length;string channel="";
   await Command($"net connect 127.0.0.1:{port}");
   await Until(()=>{var match=System.Text.RegularExpressions.Regex.Match(ReadLog()[logStart..],@"同步频道：([^\s]+)");if(!match.Success)return false;channel=match.Groups[1].Value;return true;},"native runtime joins only the current game channel");
   foreach(string old in peer.Channels.ToArray())await peer.LeaveAsync(old);
   await peer.JoinAsync(channel);
   // New protocol: a host must first create its own board; the runtime binds it to this room instance.
    await Command("dg board");
    await Command("dg mode guess");await Command("dg host "+name);
   await Until(()=>{try{room=peer.JoinGameAsync(name).GetAwaiter().GetResult();return true;}catch{return false;}},"actual game created room");
   if(room!.Channel!=channel)throw new Exception("Native room channel mismatch");
   Console.WriteLine("LIVE PASS: native room metadata matches game channel "+channel);
   using(var outsider=new ChatClient())
   {
    await outsider.ConnectAsync("127.0.0.1",port,"OtherChannelVerifier");
    foreach(string old in outsider.Channels.ToArray())await outsider.LeaveAsync(old);
    await outsider.JoinAsync("other."+channel);
    try{await outsider.JoinGameAsync(name);throw new Exception("Other-channel client joined native room");}
    catch(Chat.Protocol.ChatException e)when(e.Code=="game_not_found"){Console.WriteLine("LIVE PASS: other channel cannot join actual Unity host room");}
   }
   await Until(()=>messages.Any(m=>(m["Snapshot"] as JObject)?["Players"] is JArray p && p.Count==2),"actual game acknowledges joined player");
   await Command("paint");await Command("dg board");
   await Until(()=>messages.Any(m=>m["Board"] is JObject b && b["X"]?.Type==JTokenType.Float && b["Yaw"]!=null),"actual Unity scene board placement broadcast");
   await Command("dg start 1 60");await Command("dg hide");
    await Until(()=>messages.Any(m=>(string?)m["Snapshot"]?["Phase"]=="choosing"),"native drawer choosing with separate deadline");
    await Command("dg word native-topic");
   await Until(()=>messages.Any(m=>(string?)m["Snapshot"]?["Phase"]=="playing"),"actual Unity runtime starts round after topic submission");
   var snap=messages.Last(m=>(string?)m["Snapshot"]?["Phase"]=="playing")["Snapshot"]!;
   if(snap["Word"]?.Type!=JTokenType.Null&& !string.IsNullOrEmpty((string?)snap["Word"]))throw new Exception("Word leaked to real-game guest");
   Console.WriteLine("LIVE PASS: drawer word hidden from network guest");
   foreach(string cmd in new[]{"dg draw line 100 150 250 50 EA4335 5 1","dg draw line 250 50 400 150 EA4335 5 1","dg draw line 400 150 100 150 EA4335 5 1","dg draw line 125 150 125 400 4285F4 5 2","dg draw line 125 400 375 400 4285F4 5 2","dg draw line 375 400 375 150 4285F4 5 2"})await Command(cmd);
   await Until(()=>messages.Count(m=>(string?)m["Kind"]=="operation")>=6,"actual game raster operations relayed");
   await Task.Delay(200);await Command("dg status");
   await Command("dg next");
   await Until(()=>messages.Any(m=>(string?)m["Snapshot"]?["Phase"]=="reveal"&&!string.IsNullOrEmpty((string?)m["Snapshot"]?["Word"])),"actual game reveals answer");
   await Command("dg next");
   await Until(()=>messages.Any(m=>(string?)m["Snapshot"]?["Phase"]=="choosing"&&(string?)m["Snapshot"]?["DrawerId"]==peer.SessionId),"actual game rotates drawer to network guest choosing phase");
   var latest=messages.Last(m=>(string?)m["Snapshot"]?["DrawerId"]==peer.SessionId)["Snapshot"]!;
    await peer.SendGameAsync(name,JsonConvert.SerializeObject(new{Protocol=1,Kind="input",SessionId=room!.InstanceId,Command=new{Type="word",SessionId=room.InstanceId,RoundId=(long)latest["RoundId"]!,Text="peer-topic"}}));
    await Until(()=>messages.Any(m=>(string?)m["Snapshot"]?["Phase"]=="playing"&&(string?)m["Snapshot"]?["DrawerId"]==peer.SessionId),"network drawer submits private topic before drawing");
   var envelope=new{Protocol=1,Kind="input",SessionId=room!.InstanceId,Command=new{Type="draw",SessionId=room.InstanceId,RoundId=(long)latest["RoundId"]!,Operation=new{Tool="line",X1=1,Y1=1,X2=200,Y2=200,Color="#202020",Size=4,NewStroke=true,OpId=1,RoundId=(long)latest["RoundId"]!}}};
   await peer.SendGameAsync(name,JsonConvert.SerializeObject(envelope));
   await Until(()=>messages.Any(m=>(string?)m["Kind"]=="operation"&&(long?)m["Operation"]?["RoundId"]==(long)latest["RoundId"]!),"guest drawing validated by real game host");
   Console.WriteLine("LIVE GAME VERIFICATION PASSED");
   await Command("dg leave");
   await Until(()=>roomClosed,"game room fully closed before switching modes");
   await LiveFreePaintingCheck.Run(http,port,"free"+name,channel);
   if(Environment.GetEnvironmentVariable("LIVE_SHARED_CHECK")=="1") await LiveSharedConnectionCheck.Run(http,port,channel);
  }
  finally
  {
   try{await Command("dg leave");await Command("dg hide");}catch{}
   if(!server.HasExited){server.Kill(true);await server.WaitForExitAsync();}
  }
 }
}
