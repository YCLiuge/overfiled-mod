using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Chat.Client;
using DrawGuessModule;
using mod.st;

internal static class HostSharingCheck
{
    public static async Task<int> Run(int port)
    {
        var oldHost=NetChater.Instance;var oldPanel=PaintingSystem.Current;var oldHome=Il2CppInutan.IHomeSystem.sys;
        string oldName=mod.TipMsgCore.i.mainplayername;
        int baseline=mod.TipMsgCore.Subscriptions,checks=0,channel=505;
        string prefix="shared"+Guid.NewGuid().ToString("N")[..8];
        var hosts=new[]{new NetChater(),new NetChater()};
        var panels=new[]{new Panel(),new Panel()};
        var runtimes=new MainLogic[2];
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        void Context(int id){HostContext.Select(hosts[id]);PaintingSystem.Current=panels[id];mod.TipMsgCore.i.mainplayername=prefix+id;Il2CppInutan.IHomeSystem.sys=new(){_channelId_k__BackingField=channel};}
        void Run(int id,Action action){Context(id);hosts[id].Tick();action();}
        void Command(int id,string text)=>Run(id,()=>runtimes[id].Command(text));
        GameRoomInfo? Room(int id)=>(GameRoomInfo?)typeof(MainLogic).GetField("_room",flags)!.GetValue(runtimes[id]);
        ChatClient? Borrowed(int id)=>(ChatClient?)typeof(MainLogic).GetField("_client",flags)!.GetValue(runtimes[id]);
        void Check(bool value,string label){if(!value)throw new Exception("SHARED HOST FAIL: "+label);Console.WriteLine("SHARED HOST PASS: "+label);checks++;}
        async Task Until(Func<bool> predicate,string label)
        {
            var timer=Stopwatch.StartNew();
            while(timer.Elapsed.TotalSeconds<12)
            {
                for(int i=0;i<2;i++){int id=i;Run(id,()=>runtimes[id]?.Update());}
                if(predicate())return;
                await Task.Delay(10);
            }
            throw new Exception("SHARED HOST timeout: "+label);
        }
        async Task JoinGuest()
        {
            await Until(()=>
            {
                var boards=(System.Collections.IDictionary)typeof(MainLogic).GetField("_boards",flags)!.GetValue(runtimes[1])!;
                var board=Room(0)==null?null:boards[Room(0)!.HostId];
                return board!=null&&(string)board.GetType().GetField("RoomInstance")!.GetValue(board)! == Room(0)!.InstanceId;
            },"guest discovers current bound board");
            Command(1,"dg boardjoin "+Room(0)!.HostId+" "+Room(0)!.InstanceId);await Until(()=>Room(1)?.Members.Count==2,"shared guest joins discovered board");
        }
        async Task StartRound()
        {
            Command(0,"dg start 1 60");await Until(()=>panels[0].TopicPhase=="choosing","drawer chooses");
            Command(0,"dg word shared-topic");await Until(()=>panels[0].CanDraw,"drawer submitted topic");
        }
        try
        {
            checks += await NicknameRetryCheck.Run(port);
            NetChater.ParseEndpoint("localhost:9001",out var parsedHost,out int parsedPort);
            Check(parsedHost=="localhost"&&parsedPort==9001,"net endpoint parser accepts hostname and port");
            NetChater.ParseEndpoint("[::1]:9002",out parsedHost,out parsedPort);
            Check(parsedHost=="::1"&&parsedPort==9002,"net endpoint parser accepts bracketed IPv6");
            bool invalid=false;try{NetChater.ParseEndpoint("localhost:99999",out _,out _);}catch(ArgumentException){invalid=true;}
            Check(invalid,"net endpoint parser rejects invalid port");
            // Hold the connection gate so cancellation certainly happens before handshake.
            Context(0);await hosts[0].ChannelGate.WaitAsync();
            using(var waiter=new CancellationTokenSource())
            {
                var pending=hosts[0].ConnectAsync("127.0.0.1",port,prefix+0,waiter.Token);
                waiter.Cancel();bool cancelled=false;
                try{await pending;}catch(OperationCanceledException){cancelled=true;}
                hosts[0].ChannelGate.Release();
                await hosts[0].ConnectAsync("127.0.0.1",port,prefix+0);
                Check(cancelled&&hosts[0].IsConnected,"canceling module connection wait does not cancel host connection lifetime");
            }
            Context(1);await hosts[1].ConnectAsync("127.0.0.1",port,prefix+1);
            await Until(()=>hosts.All(h=>h.ChannelReady),"host channels ready without modules");
            var original=hosts.Select(h=>h.Client!).ToArray();
            var sessions=original.Select(c=>c.SessionId).ToArray();
            for(int i=0;i<2;i++){Context(i);runtimes[i]=new MainLogic();runtimes[i].Start();Command(i,"dg mode guess");}
            await Until(()=>Enumerable.Range(0,2).All(i=>ReferenceEquals(Borrowed(i),original[i])),"modules borrow existing host socket");
            Check(hosts.All(h=>h.IsConnected)&&Enumerable.Range(0,2).All(i=>ReferenceEquals(Borrowed(i),original[i])),"loading modules borrows existing host clients without reconnecting");
            Command(0,"dg connect");
            await Until(()=>ReferenceEquals(Borrowed(0),original[0])&&!(bool)typeof(MainLogic).GetField("_busy",flags)!.GetValue(runtimes[0])!,"default connect reused");
            Check(ReferenceEquals(hosts[0].Client,original[0])&&hosts[0].Client!.SessionId==sessions[0],"dg connect retains configured local endpoint and host session");
            var chats=new ConcurrentQueue<ChatMessage>();hosts[1].OnMessageReceived+=chats.Enqueue;
            Command(0,"dg board");await Until(()=>!((bool)typeof(MainLogic).GetField("_busy",flags)!.GetValue(runtimes[0])!),"board created");Command(0,"dg mode guess");Command(0,"dg host "+prefix);await Until(()=>Room(0)!=null,"shared host room");
            await JoinGuest();await StartRound();
            Run(0,()=>hosts[0].Send("socket-chat-one"));
            await Until(()=>chats.Any(m=>m.Text=="socket-chat-one"),"host chat on game socket");
            Check(ReferenceEquals(Borrowed(0),hosts[0].Client)&&Room(0)!.HostId==sessions[0]&&chats.Any(m=>m.Text=="socket-chat-one"&&m.From==prefix+0),"chat and painting use the same authenticated TCP session");
            Command(0,"dg draw line 460 10 470 20 EA4335 4 1");
            await Until(()=>panels[1].Operations.Count==1,"shared drawing delivered");
            Check(panels[0].Operations.SequenceEqual(panels[1].Operations),"shared socket continues carrying accepted game traffic after chat");
            var roster=await original[0].ListAsync();
            Check(roster.Single(c=>c.Name=="505").Members.OrderBy(n=>n).SequenceEqual(new[]{prefix+0,prefix+1}.OrderBy(n=>n)),"module loading creates no duplicate relay nickname or chat connection");
            Run(0,()=>runtimes[0].Dispose());
            await Until(()=>Room(1)==null,"module unload explicitly leaves and closes room");
            Check(hosts[0].ChannelReady&&ReferenceEquals(hosts[0].Client,original[0])&&original[0].SessionId==sessions[0],"unloading module leaves host socket and channel alive");
            Run(0,()=>hosts[0].Send("chat-after-unload"));await Until(()=>chats.Any(m=>m.Text=="chat-after-unload"),"chat after module unload");
            Check(chats.Any(m=>m.Text=="chat-after-unload"),"host chat works after painting module unload");
            channel=506;
            await Until(()=>hosts.All(h=>h.ChannelReady&&h.DesiredChannel=="506"),"host changes channel while module absent");
            Check(ReferenceEquals(hosts[0].Client,original[0])&&hosts[0].Channels.SequenceEqual(new[]{"506"}),"host follows channel changes after module unload without replacing its socket");
            channel=505;await Until(()=>hosts.All(h=>h.ChannelReady&&h.DesiredChannel=="505"),"host channel returns before reload");
            Context(0);runtimes[0]=new MainLogic();runtimes[0].Start();Command(0,"dg mode guess");
            await Until(()=>ReferenceEquals(Borrowed(0),original[0]),"reload rebinds host");
            Check(ReferenceEquals(hosts[0].Client,original[0])&&Borrowed(0)!.SessionId==sessions[0],"module reload reuses original session without a new TCP connection");
            Command(0,"dg board");await Until(()=>!((bool)typeof(MainLogic).GetField("_busy",flags)!.GetValue(runtimes[0])!),"board created");Command(0,"dg mode guess");Command(0,"dg host "+prefix);await Until(()=>Room(0)!=null,"reload room");
            string priorInstance=Room(0)!.InstanceId;
            await JoinGuest();
            Run(0,()=>hosts[0].Disconnect());
            await Until(()=>Room(0)==null&&Room(1)==null&&Borrowed(0)==null,"host disconnect clears borrowed room");
            Check(!hosts[0].IsConnected&&Room(0)==null&&Room(1)==null,"host disconnect clears module room and closes guest room");
            Context(0);await hosts[0].ConnectAsync("127.0.0.1",port,prefix+"reconnected");
            await Until(()=>hosts[0].ChannelReady&&ReferenceEquals(Borrowed(0),hosts[0].Client),"module follows replacement socket");
            Check(!ReferenceEquals(hosts[0].Client,original[0])&&hosts[0].Client!.SessionId!=sessions[0]&&Room(0)==null,"host reconnect attaches new client with no stale room authority");
            Command(0,"dg board");await Until(()=>!((bool)typeof(MainLogic).GetField("_busy",flags)!.GetValue(runtimes[0])!),"board created");Command(0,"dg mode guess");Command(0,"dg host "+prefix);await Until(()=>Room(0)!=null,"reconnected host room");
            await JoinGuest();await StartRound();
            Check(Room(0)!.InstanceId!=priorInstance&&Room(0)!.HostId==hosts[0].Client!.SessionId,"new room binds replacement session and new incarnation");
            Command(0,"dg draw line 480 10 490 20 4285F4 4 2");await Until(()=>panels[1].Operations.Count==1,"reconnected drawing");
            Check(panels[1].Operations.Single().StartsWith("line:480:")&&panels[0].Operations.SequenceEqual(panels[1].Operations),"reconnected module accepts new game traffic without stale replay");
            Run(0,()=>hosts[0].Send("chat-after-reconnect"));await Until(()=>chats.Any(m=>m.Text=="chat-after-reconnect"),"chat on replacement socket");
            Check(chats.Last().From==prefix+"reconnected","host chat and painting follow replacement socket together");
        }
        finally
        {
            for(int i=0;i<2;i++){int id=i;Run(id,()=>runtimes[id]?.Dispose());}
            foreach(var host in hosts)host.Dispose();
            HostContext.Select(oldHost);PaintingSystem.Current=oldPanel;Il2CppInutan.IHomeSystem.sys=oldHome;mod.TipMsgCore.i.mainplayername=oldName;
        }
        Check(mod.TipMsgCore.Subscriptions==baseline,"shared host verification releases module command handlers");
        Console.WriteLine($"SHARED HOST INTEGRATION: {checks} checks passed");return checks;
    }
}
