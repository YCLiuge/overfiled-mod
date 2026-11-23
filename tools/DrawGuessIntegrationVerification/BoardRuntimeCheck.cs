using DrawGuessModule.Core;
using Newtonsoft.Json;

internal static class BoardRuntimeCheck
{
    public static async Task<int> Run(int port)
    {
        using var r=new IntegrationRig();r.Channels[2]=702;await r.Connect(port);
        string room=r.Prefix+"game";string owner=r.Client(0).SessionId, visitor=r.Client(1).SessionId;
        r.Command(0,"dg host "+room);await r.Settle();
        r.Check(r.Room(0)==null,"host requires creator's own board");
        r.Command(0,"dg board");await r.Until(()=>r.Panels[1].Boards.ContainsKey(owner),"ordinary board discovery");
        r.Check(r.Panels[0].Boards.ContainsKey(owner)&&!r.Panels[2].Boards.ContainsKey(owner),"ordinary board visible to same channel only");
        r.Command(0,"dg draw line 10 20 30 40 EA4335 4 1");await r.Until(()=>r.Panels[1].Boards[owner].Operations.Count==1,"owner drawing");
        r.Run(1,()=>r.Panels[1].SelectBoard(owner));
        r.Check(!r.Panels[1].CanDraw&&r.Panels[1].Operations.Count==1,"ordinary board click opens readonly owner's artwork");
        r.Command(1,"dg draw line 99 99 100 100 4285F4 4 1");await r.Settle();
        r.Check(r.Panels[0].Boards[owner].Operations.Count==1&&r.Panels[1].Operations.Count==1,"visitor cannot draw into selected ordinary board");
        r.Command(1,"dg mode guess");r.Command(1,"dg host stolen");await r.Settle();
        r.Check(r.Room(1)==null,"selecting another creator's board does not grant hosting authority");
        var oldPlacement=r.Panels[0].Boards[owner].Placement.Copy();
        r.Panels[0].InitialBoardPlacement.X+=100;r.Command(0,"dg board");
        await r.Until(()=>r.Panels[1].Boards[owner].Placement.X==r.Panels[0].InitialBoardPlacement.X,"owner board reposition");
        r.Check(r.Panels[1].Boards.Count==1&&r.Panels[1].Boards[owner].Placement.X!=oldPlacement.X,"repeated dg board moves same session board");
        r.Command(1,"dg board");await r.Until(()=>r.Panels[0].Boards.ContainsKey(visitor),"second personal board");
        r.Command(1,"dg draw line 70 70 80 80 4285F4 4 1");await r.Until(()=>r.Panels[0].Boards[visitor].Operations.Count==1,"independent creator artwork");
        r.Check(r.Panels[0].Boards[owner].Operations.Count==1&&r.Panels[0].Boards[visitor].Operations[0].StartsWith("line:70:"),"per-board operation buffers remain independent");
        r.Command(0,"dg save personal sample");
        r.Check(r.Panels[0].SaveCount==1&&r.Panels[0].SavedName=="personal sample"&&r.Panels[0].SavedOperations.Count==1,"PNG export receives selected creator painting");
        r.Command(1,"dg host free-forbidden");await r.Settle();r.Check(r.Room(1)==null,"ordinary free boards do not create legacy multiplayer write rooms");
        // The relay owns sender identity; a fabricated payload Owner cannot overwrite the victim board.
        await r.Client(1).BroadcastBoardAsync("701",JsonConvert.SerializeObject(new{Kind="snapshot",Owner=owner,Name="forged",Placement=new SceneBoardPlacement{X=999,Y=1,Z=1},Revision=10,Transfer="forged",Index=0,Count=1,Operations=new[]{new DrawingOperation{Tool="line",X1=55,Y1=55,X2=56,Y2=56,Color="#EA4335",Size=4,NewStroke=true}}}));
        await r.Until(()=>r.Panels[0].Boards[visitor].Placement.X==999,"authenticated spoof processed as attacker's board");
        r.Check(r.Panels[0].Boards[owner].Placement.X!=999&&r.Panels[0].Boards[owner].Operations[0].StartsWith("line:10:"),"payload owner spoof cannot move or write another session's board");
        // Late entrant must obtain complete canonical artwork, including multiple 48-operation chunks.
        for(int i=2;i<=65;i++)
        {
            r.Command(0,$"dg draw line {i} 20 {i+1} 21 EA4335 4 {i}");
            if(i%16==0){await r.Settle();await r.Settle();}
        }
        await r.Until(()=>r.Panels[1].Boards[owner].Operations.Count==65,"multi-chunk owner snapshot");
        r.Channels[2]=701;await r.Until(()=>r.Ready(2)&&r.Panels[2].Boards.TryGetValue(owner,out var b)&&b.Operations.Count==65,"late channel entrant receives full board replay");
        r.Check(r.Panels[2].Boards[owner].Operations.SequenceEqual(r.Panels[0].Boards[owner].Operations),"late join reconstructs canonical multichunk replay");
        r.Command(0,"dg mode guess");r.Command(0,"dg host "+room);await r.Until(()=>r.Room(0)!=null,"bound game host");
        string epoch=r.Room(0)!.InstanceId;
        await r.Until(()=>r.Panels[1].Boards[owner].Label.Contains(room),"room binding advertisement");
        r.Run(1,()=>r.Panels[1].JoinBoard(owner,"stale-instance"));await r.Settle();
        r.Check(r.Room(1)==null,"board click rejects stale room incarnation");
        r.Run(1,()=>r.Panels[1].JoinBoard(owner,epoch));await r.Until(()=>r.View(1)?.Players.Count==2,"bound board click joins room");
        r.Check(r.Room(1)!.InstanceId==epoch&&r.Room(1)!.HostId==owner,"board click pins authenticated creator and room instance");
        r.Command(2,"dg mode guess");r.Command(2,"dg join "+room);await r.Until(()=>r.View(2)?.Players.Count==3,"ordinary join resolves discovered bound board");
        r.Command(1,"dg start");await r.Settle();r.Check(r.View(0)!.Phase=="lobby","nonhost cannot start game");
        r.Command(0,"dg start 1 60");await r.Until(()=>r.View(2)?.Phase=="choosing","initial topic selection");
        r.Check(r.View(0)!.RemainingSeconds>20&&r.View(0)!.RemainingSeconds<=30&&r.Panels.All(p=>!p.CanDraw),"choosing uses separate 30-second deadline and disables drawing");
        r.Command(1,"dg word stolen-topic");r.Command(0,"dg draw line 1 1 2 2 EA4335 4 1");await r.Settle();
        r.Check(r.View(0)!.Phase=="choosing"&&r.Panels[0].Operations.Count==0,"only current drawer may submit topic and no one draws while choosing");
        r.Command(0,"dg word secret-apple");await r.Until(()=>r.View(2)?.Phase=="playing"&&r.Panels[0].CanDraw,"topic starts playing");
        r.Check(r.View(0)!.RemainingSeconds>55&&r.Panels[0].Secret=="secret-apple"&&r.Panels.Skip(1).All(p=>string.IsNullOrEmpty(p.Secret)),"submitted topic starts full playing duration and stays drawer-private");
        r.Command(0,"dg draw line 100 110 120 130 EA4335 4 1");await r.Until(()=>r.Panels[2].Operations.Count==1,"shared game drawing");
        r.Command(1,"dg draw clear 0 0 0 0 EA4335 4 1");await r.Settle();
        r.Check(r.Panels.All(p=>p.Operations.Count==1)&&!r.Panels[1].CanDraw,"shared host board writable only by current drawer");
        r.Command(1,"dg guess secret-apple");await r.Until(()=>r.View(0)!.Players.Any(p=>p.Name==r.Prefix+1&&p.Score>0),"authenticated guess scoring");
        int score=r.View(0)!.Players.First(p=>p.Name==r.Prefix+1).Score;r.Command(1,"dg guess secret-apple");await r.Settle();
        r.Check(r.View(0)!.Players.First(p=>p.Name==r.Prefix+1).Score==score,"duplicate guess cannot score twice");
        r.Command(0,"dg next");await r.Until(()=>r.View(2)?.Phase=="reveal","reveal");r.Check(r.View(2)!.Word=="secret-apple","topic becomes public only in reveal");
        r.Command(0,"dg next");await r.Until(()=>r.View(1)?.Phase=="choosing"&&r.View(1)?.DrawerId==visitor,"drawer rotation");
        r.Command(0,"dg word old-drawer");await r.Settle();r.Check(r.View(0)!.Phase=="choosing","previous drawer loses topic authority after rotation");
        r.Command(1,"dg word second-topic");await r.Until(()=>r.Panels[1].CanDraw&&r.View(0)?.Phase=="playing","rotated drawer topic");
        r.Check(!r.Panels[0].CanDraw&&string.IsNullOrEmpty(r.Panels[0].Secret)&&r.Panels[1].Secret=="second-topic","rotation transfers drawing and secret access off host");
        r.Command(1,"dg draw line 200 210 220 230 4285F4 4 1");await r.Until(()=>r.Panels[0].Operations.Count==1,"rotated drawer operation");
        r.Command(0,"dg leave");await r.Until(()=>Enumerable.Range(0,3).All(i=>r.Room(i)==null),"host closes room");
        r.Command(0,"dg mode guess");r.Command(0,"dg host "+room);await r.Until(()=>r.Room(0)!=null,"room recreation");
        string newEpoch=r.Room(0)!.InstanceId;r.Check(newEpoch!=epoch,"recreated room has fresh incarnation");
        await r.Until(()=>r.Panels[1].Boards[owner].Label.Contains(room),"recreated board binding");await r.Settle();
        r.Run(1,()=>r.Panels[1].JoinBoard(owner,epoch));await r.Settle();r.Check(r.Room(1)==null,"old board click cannot replay into recreated room");
        r.Run(1,()=>r.Panels[1].JoinBoard(owner,newEpoch));await r.Until(()=>r.View(1)?.Phase=="lobby","fresh instance join");
        var replay=JsonConvert.DeserializeObject<GameSnapshot>(JsonConvert.SerializeObject(r.View(0)))!;
        replay.SessionId=epoch;replay.Phase="playing";replay.Version+=100;
        await r.Client(0).SendGameAsync(room,JsonConvert.SerializeObject(new{Protocol=1,Kind="snapshot",SessionId=epoch,Snapshot=replay}));await r.Settle();
        r.Check(r.Room(1)?.InstanceId==newEpoch&&r.View(1)?.Phase=="lobby","authenticated host's old room snapshot cannot mutate fresh incarnation");
        Console.WriteLine($"BOARD RUNTIME INTEGRATION: {r.Checks} checks passed");return r.Checks;
    }
}
