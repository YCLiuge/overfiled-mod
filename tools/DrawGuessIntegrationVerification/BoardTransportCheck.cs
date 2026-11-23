using System.Collections.Concurrent;
using System.Diagnostics;
using Chat.Client;
using Chat.Protocol;

internal static class BoardTransportCheck
{
    public static async Task<int> Run(int port)
    {
        int checks=0;
        void Check(bool value,string label){if(!value)throw new Exception("BOARD TRANSPORT FAIL: "+label);Console.WriteLine("BOARD TRANSPORT PASS: "+label);checks++;}
        using var a=new ChatClient();using var b=new ChatClient();using var outsider=new ChatClient();
        string prefix="board"+Guid.NewGuid().ToString("N")[..8], channel=prefix+"channel";
        var seen=new ConcurrentQueue<BoardChannelMessage>();var foreign=new ConcurrentQueue<BoardChannelMessage>();
        b.BoardMessageReceived+=seen.Enqueue;outsider.BoardMessageReceived+=foreign.Enqueue;
        await a.ConnectAsync("127.0.0.1",port,prefix+"a");await b.ConnectAsync("127.0.0.1",port,prefix+"b");await outsider.ConnectAsync("127.0.0.1",port,prefix+"x");
        foreach(var client in new[]{a,b})foreach(string old in client.Channels.ToArray())await client.LeaveAsync(old);
        await a.JoinAsync(channel);await b.JoinAsync(channel);
        string text="{\"OwnerId\":\"forged\",\"Kind\":\"probe\"}";
        await a.BroadcastBoardAsync(channel,text);
        var timer=Stopwatch.StartNew();while(!seen.Any()&&timer.Elapsed.TotalSeconds<5)await Task.Delay(10);
        Check(seen.TryPeek(out var received)&&received.AuthenticatedId==a.SessionId&&received.Name==a.Nick&&received.Channel==channel&&received.Text==text,"relay supplies authenticated identity independently of spoofed payload");
        Check(foreign.IsEmpty,"board broadcast does not reach clients outside channel");
        bool rejected=false;try{await outsider.BroadcastBoardAsync(channel,"outsider");}catch(ChatException){rejected=true;}
        Check(rejected,"nonmember cannot publish into board channel");
        rejected=false;try{await a.BroadcastBoardAsync(channel,new string('x',25000));}catch(ChatException){rejected=true;}
        Check(rejected,"oversized board payload is rejected");
        return checks;
    }
}
