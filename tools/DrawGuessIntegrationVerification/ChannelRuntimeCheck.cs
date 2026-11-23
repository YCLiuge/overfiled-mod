using Newtonsoft.Json;

internal static class ChannelRuntimeCheck
{
    public static async Task<int> Run(int port)
    {
        using var r=new IntegrationRig();await r.Connect(port);
        r.Command(0,"dg board");string owner=r.Client(0).SessionId;
        await r.Until(()=>r.Panels[1].Boards.ContainsKey(owner),"initial same-channel board");
        var original=r.Client(1);r.Channels[1]=703;
        await r.Until(()=>r.Ready(1)&&r.Client(1).Channels.SequenceEqual(new[]{"703"}),"channel switch");
        r.Check(ReferenceEquals(original,r.Client(1))&&r.Panels[1].Boards.Count==0&&r.Room(1)==null,"channel switch retains socket while removing old boards and room authority");
        r.Command(0,"dg draw line 40 40 50 50 EA4335 4 1");await r.Settle();
        r.Check(r.Panels[1].Boards.Count==0,"former channel does not deliver new owner drawing");
        r.Command(1,"dg mode guess");r.Command(1,"dg join undiscovered-room");await r.Settle();
        r.Check(r.Room(1)==null&&!r.Busy(1),"ordinary join refuses undiscovered unbound room");
        r.Channels[1]=null;await r.Until(()=>!r.Ready(1),"missing channel");
        r.Command(1,"dg board");r.Check(r.Panels[1].Boards.Count==0,"missing game channel cannot create a broadcast board");
        r.Channels[1]=701;await r.Until(()=>r.Ready(1)&&r.Panels[1].Boards.TryGetValue(owner,out var b)&&b.Operations.Count==1,"return channel discovers current replay");
        r.Check(r.Panels[1].Boards[owner].Operations[0].StartsWith("line:40:"),"returning channel replays accepted owner state");
        // Malformed/incomplete transfer must never partially replace canonical replay.
        string attacker=r.Client(2).SessionId;
        var placement=new DrawGuessModule.Core.SceneBoardPlacement{X=1,Y=2,Z=3};
        await r.Client(2).BroadcastBoardAsync("701",JsonConvert.SerializeObject(new{Kind="snapshot",Name="ignored",Placement=placement,Revision=100,Transfer="partial",Index=1,Count=2,Operations=new[]{new DrawGuessModule.Core.DrawingOperation{Tool="line",X1=99,Y1=1,X2=100,Y2=2,Color="#EA4335",Size=4}}}));
        await r.Settle();
        r.Check(!r.Panels[1].Boards.TryGetValue(attacker,out var incomplete)||incomplete.Operations.Count==0,"out-of-order snapshot chunks do not become visible drawing");
        r.Check(r.Panels[1].Boards[owner].Operations.Count==1,"incomplete attacker snapshot leaves other creator replay intact");
        return r.Checks;
    }
}
