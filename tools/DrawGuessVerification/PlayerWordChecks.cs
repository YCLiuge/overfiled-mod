using DrawGuessModule.Core;

static class PlayerWordChecks
{
    public static void Run(Action<bool,string> check)
    {
        var g = NewGame();
        GameCommand Cmd(string type, string? text = "", long? round = null) => new()
        { Type = type, SessionId = g.SessionId, RoundId = round ?? g.SnapshotFor("h", 1000).RoundId, Text = text! };
        var initial = g.SnapshotFor("h",0);
        check(initial.Phase == "choosing" && initial.DrawerId == "h" && initial.RoundId == 1, "default runtime waits for current drawer word");
        check(initial.RemainingSeconds == AuthoritativeGame.ChoosingSeconds && initial.Word == null && initial.WordLength == 0, "choosing exposes deadline but no default answer");
        check(g.SnapshotFor("a",0).Word == null && g.SnapshotFor("unknown",0).Word == null, "choosing snapshots never contain answer");
        check(!g.Handle("x",Cmd("word","secret"),0).Success, "unauthenticated outsider cannot submit word");
        check(g.Handle("a",Cmd("word","secret"),0).Code == "drawer_only", "authenticated guesser cannot submit word");
        check(g.Handle("h",Cmd("word","secret",0),0).Code == "stale_round", "old round word rejected");
        check(!g.Handle("h",Cmd("draw"),0).Success && !g.Handle("a",Cmd("guess","apple"),0).Success, "draw and guess forbidden while choosing");
        foreach (var invalid in new string?[] { null, "", "   ", new string('a',17), "apple\n", "\tapple", "a\0b" })
            check(g.Handle("h",Cmd("word",invalid),0).Code == "invalid_word", "invalid word rejected without leaving choosing");
        check(g.SnapshotFor("h",0).Phase == "choosing" && g.SnapshotFor("h",0).Word == null, "invalid submissions do not start timer or expose answer");
        check(!g.Tick(20) && g.SnapshotFor("h",20).Phase == "choosing", "elapsed choice time does not consume drawing time");
        check(g.Handle("h",Cmd("word","  苹果  "),20).Success, "current drawer submits trimmed word");
        var playing = g.SnapshotFor("h",20);
        check(playing.Phase == "playing" && playing.RemainingSeconds == 60 && playing.Word == "苹果" && playing.RoundId == initial.RoundId, "submission starts full round timer and retains round identity");
        check(g.SnapshotFor("a",20).Word == null && g.SnapshotFor("x",20).Word == null && g.SnapshotFor("a",20).WordLength == 2, "playing public snapshots give hint without answer");
        check(g.Handle("h",Cmd("word","覆盖答案"),21).Code == "invalid_phase" && g.SnapshotFor("h",21).Word == "苹果" && g.SnapshotFor("h",21).RemainingSeconds == 59, "duplicate word cannot replace answer or restart timer");
        check(g.AddPlayer("a","重连甲",21).Success && g.SnapshotFor("a",21).Word == null, "guesser reconnect does not receive private answer");
        check(g.SnapshotFor("h",21).Word == "苹果", "drawer reconnect snapshot retains own answer");
        check(g.AddPlayer("late","后来者",21).Success && g.SnapshotFor("late",21).Word == null, "late join cannot read answer");
        check(!g.Tick(79) && g.SnapshotFor("a",79).Phase == "playing", "drawing persists through full configured round duration");
        check(g.Tick(80) && g.SnapshotFor("a",80).Word == "苹果" && g.SnapshotFor("x",80).Word == "苹果", "answer becomes public only at reveal");
        check(g.Tick(85) && g.SnapshotFor("h",85).Phase == "choosing" && g.SnapshotFor("h",85).DrawerId == "a", "next drawer must author a new word");
        check(g.SnapshotFor("h",85).Word == null && g.SnapshotFor("a",85).Word == null && g.SnapshotFor("a",85).WordLength == 0 && g.Operations.Count == 0, "new round clears old secret and drawing replay");
        check(g.Handle("h",Cmd("word","苹果",initial.RoundId),85).Code == "stale_round", "late previous drawer submission rejected by RoundId");
        check(g.Handle("h",Cmd("word","越权"),85).Code == "drawer_only", "host cannot author another drawer round");
        check(g.Handle("a",Cmd("word",new string('字',16)),85).Success, "sixteen character answer accepted for next drawer");
        check(g.SnapshotFor("h",85).Word == null && g.SnapshotFor("a",85).Word == new string('字',16), "private answer follows current drawer only");

        var timeout = NewGame();
        check(timeout.Tick(30) && timeout.SnapshotFor("h",30).Phase == "reveal" && timeout.SnapshotFor("a",30).LastEvent == "word_timeout", "nonresponsive drawer times out after thirty seconds");
        check(timeout.SnapshotFor("a",30).Word == null && timeout.SnapshotFor("a",30).WordLength == 0 && timeout.SnapshotFor("a",30).Players.All(p=>p.Score==0), "skipped choice reveals no invented answer and awards no points");
        var lateWord = new GameCommand {Type="word", SessionId=timeout.SessionId, RoundId=1, Text="太晚"};
        check(!timeout.Handle("h",lateWord,30).Success, "submission at exact deadline loses to timeout");
        check(timeout.Tick(35) && timeout.SnapshotFor("a",35).DrawerId == "a" && timeout.SnapshotFor("a",35).Phase == "choosing", "choice timeout advances to next drawer after reveal delay");
        timeout.Tick(65); timeout.Tick(70);
        check(timeout.SnapshotFor("h",70).Phase == "finished" && timeout.SnapshotFor("h",70).Word == null, "all unresponsive drawers cannot stall game permanently");
        var skip = NewGame();
        check(skip.Handle("h",new GameCommand{Type="next",SessionId=skip.SessionId,RoundId=1},1).Success && skip.SnapshotFor("h",1).Phase == "reveal", "host may skip stalled choosing phase");
        var disconnected = NewGame();
        disconnected.Handle("h",new GameCommand{Type="next",SessionId=disconnected.SessionId,RoundId=1},0);
        disconnected.Tick(5);
        check(disconnected.RemovePlayer("a",6).Success && disconnected.SnapshotFor("h",6).Phase == "reveal", "drawer disconnection also ends choosing phase");
        var legacy = new AuthoritativeGame("h","legacy",0,_=>"测试");
        legacy.AddPlayer("a","甲",0); legacy.Handle("h",new GameCommand{Type="start",SessionId="legacy"},0);
        check(legacy.SnapshotFor("h",0).Phase == "playing" && legacy.SnapshotFor("a",0).Word == null, "explicit injected selector remains an opt-in compatibility seam");
    }
    private static AuthoritativeGame NewGame()
    {
        var game = new AuthoritativeGame("h","manual",0);
        game.AddPlayer("a","甲",0);
        var result = game.Handle("h",new GameCommand{Type="start",SessionId="manual"},0);
        if(!result.Success) throw new Exception(result.Code);
        return game;
    }
}
