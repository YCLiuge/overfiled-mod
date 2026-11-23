using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

if(args.FirstOrDefault()=="--live")
{
    if(Environment.GetEnvironmentVariable("DRAWGUESS_ALLOW_UPDATED_LIVE")!="1")
        throw new InvalidOperationException("--live requires updated native board/word protocol and explicit DRAWGUESS_ALLOW_UPDATED_LIVE=1. Never run against an older game module.");
    await LiveGameCheck.Run();return;
}
var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
string server=Path.GetFullPath(args.Length>0?args[0]:"服务器/Chat.Server/bin/Release/net9.0/Chat.Server.dll");
var start=new ProcessStartInfo("dotnet"){UseShellExecute=false};
foreach(var arg in new[]{server,"--host","127.0.0.1","--port",port.ToString(),"--heartbeat","0"})start.ArgumentList.Add(arg);
using var process=Process.Start(start)??throw new Exception("Could not start isolated relay");
try
{
    var clock=Stopwatch.StartNew();bool ready=false;
    while(clock.Elapsed.TotalSeconds<12){try{using var c=new TcpClient();await c.ConnectAsync("127.0.0.1",port);ready=true;break;}catch{await Task.Delay(20);}}
    if(!ready)throw new Exception("Isolated relay did not start");
    int checks=await BoardTransportCheck.Run(port);
    checks+=await BoardRuntimeCheck.Run(port);
    checks+=await ChannelRuntimeCheck.Run(port);
    checks+=await HostSharingCheck.Run(port);
    Console.WriteLine($"RUNTIME INTEGRATION: {checks} checks passed");
}
finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
