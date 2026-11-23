using ChineseChessModule.Core;

static class Program
{
    static int assertions;
    static int I(int x, int y) => y * 9 + x;
    static void Assert(bool value, string name)
    {
        assertions++;
        if (!value) throw new InvalidOperationException("FAIL: " + name);
    }
    static void Put(ChessGame g, int x, int y, Side side, char kind) => g.Board[I(x, y)] = new Piece { Side = side, Kind = kind };
    static ChessGame Empty(Side turn = Side.Red)
    {
        var g = new ChessGame { Board = new Piece?[90], Turn = turn };
        Put(g, 3, 9, Side.Red, 'K'); Put(g, 5, 0, Side.Black, 'K');
        return g;
    }
    static bool Has(ChessGame g, int x, int y, int tx, int ty) => g.LegalTargets(I(x, y)).Contains(I(tx, ty));
    static void Main()
    {
        var g = new ChessGame();
        Assert(g.Board.Length == 90 && g.Board.Count(p => p != null) == 32, "initial 32 pieces");
        Assert(g.Turn == Side.Red && !g.Finished && g.Winner == null && g.Revision == 1, "initial metadata");
        Assert(g.Board[I(4, 9)]?.Kind == 'K' && g.Board[I(4, 0)]?.Side == Side.Black, "initial kings");
        Assert(g.Board[I(1, 7)]?.Kind == 'C' && g.Board[I(0, 6)]?.Kind == 'P', "initial cannons pawns");
        Assert(!g.InCheck(Side.Red) && !g.InCheck(Side.Black), "initial safety");
        Assert(g.LegalTargets(I(1, 9)).OrderBy(x => x).SequenceEqual(new[] { I(0, 7), I(2, 7) }), "initial knight targets");
        long rev = g.Revision;
        Assert(!g.TryMove(-1, 90, out var error) && error.Length > 0 && g.Revision == rev, "invalid index rejected");
        Assert(!g.TryMove(I(0, 3), I(0, 4), out _), "wrong turn rejected");
        Assert(!g.TryMove(I(4, 4), I(4, 3), out _), "empty source rejected");
        Assert(!g.TryMove(I(0, 9), I(1, 9), out _), "friendly capture rejected");
        Assert(!g.TryMove(I(0, 6), I(0, 6), out _), "null move rejected");
        Assert(g.TryMove(I(0, 6), I(0, 5), out error) && error == "" && g.Turn == Side.Black && g.Revision == rev + 1, "red move increments revision");
        Assert(g.TryMove(I(0, 3), I(0, 4), out _) && g.Turn == Side.Red, "black forward direction");
        g.Reset(); Assert(g.Revision == rev + 3 && g.Board.Count(p => p != null) == 32, "reset increments and restores");

        g = Empty(); Put(g, 4, 5, Side.Red, 'N');
        Assert(Has(g, 4, 5, 6, 4) && Has(g, 4, 5, 5, 3), "knight L moves");
        Put(g, 5, 5, Side.Red, 'P');
        Assert(!Has(g, 4, 5, 6, 4) && !Has(g, 4, 5, 6, 6) && Has(g, 4, 5, 5, 3), "horizontal knight leg");
        Put(g, 4, 4, Side.Black, 'P'); Assert(!Has(g, 4, 5, 5, 3), "vertical knight leg");
        Assert(!Has(g, 4, 5, 5, 4), "knight diagonal rejected");

        g = Empty(); Put(g, 4, 7, Side.Red, 'B');
        Assert(Has(g, 4, 7, 6, 5), "red elephant move");
        Put(g, 5, 6, Side.Black, 'P'); Assert(!Has(g, 4, 7, 6, 5), "elephant eye");
        g = Empty(); Put(g, 2, 5, Side.Red, 'B'); Assert(!Has(g, 2, 5, 4, 3), "red elephant river");
        g = Empty(Side.Black); Put(g, 2, 4, Side.Black, 'B');
        Assert(!Has(g, 2, 4, 4, 6) && Has(g, 2, 4, 0, 2), "black elephant river and move");

        g = Empty(); Put(g, 4, 8, Side.Red, 'A');
        Assert(Has(g, 4, 8, 5, 7) && !Has(g, 4, 8, 4, 7), "advisor diagonal");
        g.Board[I(4, 8)] = null; Put(g, 3, 7, Side.Red, 'A'); Assert(!Has(g, 3, 7, 2, 6), "advisor palace");
        Assert(Has(g, 3, 9, 4, 9) && !Has(g, 3, 9, 2, 9) && !Has(g, 3, 9, 4, 8), "king palace and orthogonal");
        g = Empty(Side.Black); Assert(Has(g, 5, 0, 4, 0) && !Has(g, 5, 0, 6, 0), "black king palace");

        g = Empty(); Put(g, 0, 5, Side.Red, 'R');
        Assert(Has(g, 0, 5, 0, 1) && !Has(g, 0, 5, 1, 4), "rook straight only");
        Put(g, 0, 3, Side.Black, 'P');
        Assert(Has(g, 0, 5, 0, 3) && !Has(g, 0, 5, 0, 2), "rook capture and blocking");
        g = Empty(); Put(g, 0, 5, Side.Red, 'C'); Put(g, 0, 1, Side.Black, 'R');
        Assert(Has(g, 0, 5, 0, 2) && !Has(g, 0, 5, 0, 1), "cannon quiet and no-screen capture");
        Put(g, 0, 3, Side.Red, 'P');
        Assert(Has(g, 0, 5, 0, 1) && !Has(g, 0, 5, 0, 2), "cannon one screen capture only");
        Put(g, 0, 2, Side.Black, 'P'); Assert(!Has(g, 0, 5, 0, 1), "cannon two screens rejected");

        g = Empty(); Put(g, 0, 6, Side.Red, 'P');
        Assert(Has(g, 0, 6, 0, 5) && !Has(g, 0, 6, 1, 6) && !Has(g, 0, 6, 0, 7), "red pawn before river");
        Put(g, 2, 4, Side.Red, 'P');
        Assert(Has(g, 2, 4, 1, 4) && Has(g, 2, 4, 3, 4) && !Has(g, 2, 4, 2, 5), "red crossed pawn");
        Put(g, 0, 0, Side.Red, 'P'); Assert(Has(g, 0, 0, 1, 0), "pawn at last rank moves sideways");
        g = Empty(Side.Black); Put(g, 0, 3, Side.Black, 'P'); Put(g, 2, 5, Side.Black, 'P');
        Assert(Has(g, 0, 3, 0, 4) && !Has(g, 0, 3, 1, 3) && Has(g, 2, 5, 3, 5) && !Has(g, 2, 5, 2, 4), "black pawn river and direction");

        g = Empty(); g.Board[I(3, 9)] = null; Put(g, 5, 9, Side.Red, 'K');
        Assert(g.InCheck(Side.Red) && g.InCheck(Side.Black), "facing kings both checked");
        Assert(Has(g, 5, 9, 5, 0), "flying king capture");
        Put(g, 5, 5, Side.Red, 'R'); Assert(!g.InCheck(Side.Red), "king file blocked");
        var rook = g.Board[I(5, 5)]; rev = g.Revision;
        Assert(!g.TryMove(I(5, 5), I(4, 5), out error) && error.Length > 0, "facing kings exposure rejected");
        Assert(g.Board[I(5, 5)] == rook && g.Board[I(4, 5)] == null && g.Revision == rev && g.Turn == Side.Red, "rejected move atomic");
        Assert(!Has(g, 5, 5, 4, 5), "legal targets filter self check");

        g = Empty(); Put(g, 3, 0, Side.Black, 'R'); Put(g, 3, 5, Side.Red, 'R');
        Assert(!Has(g, 3, 5, 4, 5) && Has(g, 3, 5, 3, 0), "pinned rook cannot expose king but can capture attacker");
        g.Board[I(3, 5)] = null; Assert(g.InCheck(Side.Red), "rook check");
        Put(g, 0, 6, Side.Red, 'P'); Assert(!g.TryMove(I(0, 6), I(0, 5), out _), "must answer existing check");
        Assert(g.TryMove(I(3, 9), I(4, 9), out _), "king escapes check");

        g = Empty(); Put(g, 4, 7, Side.Black, 'N'); Assert(g.InCheck(Side.Red), "knight check");
        Put(g, 4, 8, Side.Red, 'P'); Assert(!g.InCheck(Side.Red), "blocked knight does not check");
        g = Empty(); Put(g, 3, 0, Side.Black, 'C'); Assert(!g.InCheck(Side.Red), "cannon without screen no check");
        Put(g, 3, 5, Side.Red, 'P'); Assert(g.InCheck(Side.Red), "cannon screen check");
        g = Empty(); Put(g, 3, 8, Side.Black, 'P'); Assert(g.InCheck(Side.Red), "black pawn forward check");
        g = Empty(); Put(g, 4, 9, Side.Black, 'P'); Assert(g.InCheck(Side.Red), "crossed pawn sideways check");

        g = Ending(); Put(g, 3, 1, Side.Red, 'R'); Put(g, 5, 1, Side.Red, 'R'); Put(g, 4, 2, Side.Red, 'R');
        rev = g.Revision;
        Assert(g.TryMove(I(4, 2), I(4, 1), out _), "mating move accepted");
        Assert(g.Finished && g.Winner == Side.Red && g.InCheck(Side.Black) && g.Revision == rev + 1, "checkmate loses");
        Assert(!g.TryMove(I(4, 0), I(3, 0), out _) && g.LegalTargets(I(4, 0)).Length == 0 && !g.Resign(Side.Red), "finished rejects actions");
        g = Ending(); Put(g, 3, 3, Side.Red, 'R'); Put(g, 5, 3, Side.Red, 'R'); Put(g, 0, 1, Side.Red, 'R');
        Assert(g.TryMove(I(0, 1), I(1, 1), out _) && g.Finished && g.Winner == Side.Red && !g.InCheck(Side.Black), "stalemate loses without check");
        g = Ending(); Put(g, 4, 1, Side.Red, 'R');
        Assert(g.TryMove(I(4, 1), I(4, 0), out _) && g.Finished && g.Winner == Side.Red, "king capture ends game");

        g = new ChessGame(); rev = g.Revision;
        Assert(g.Resign(Side.Black) && g.Winner == Side.Red && g.Finished && g.Revision == rev + 1, "either side resignation");
        g.Reset(); Assert(!g.Finished && g.Winner == null && g.Status.Contains("红方"), "reset after finish");
        rev = g.Revision; var snapshot = g.Board;
        g.LoadSnapshot(snapshot, Side.Black, null, false, 42);
        Assert(g.Turn == Side.Black && g.Revision == 42 && g.Status.Contains("黑方"), "snapshot metadata");
        Assert(!ReferenceEquals(g.Board, snapshot) && !ReferenceEquals(g.Board[0], snapshot[0]), "snapshot deep copy");
        Assert(g.LegalTargets(-1).Length == 0 && g.LegalTargets(90).Length == 0 && g.LegalTargets(I(0, 6)).Length == 0, "invalid or opponent legal query");
        var before = g.Board.ToArray(); rev = g.Revision; string status = g.Status;
        for (int i = 0; i < 90; i++) g.LegalTargets(i);
        Assert(g.Board.SequenceEqual(before) && g.Revision == rev && g.Status == status && g.Turn == Side.Black, "queries preserve all state");
        Console.WriteLine($"PASS: {assertions} assertions covering initialization, all seven pieces, blocking, river/palace, checks, self-check, mate/stalemate, finish, revision, resignation, snapshots and query purity.");
    }
    static ChessGame Ending()
    {
        var g = Empty(); g.Board[I(5, 0)] = null; g.Board[I(3, 9)] = null;
        Put(g, 4, 0, Side.Black, 'K'); Put(g, 4, 9, Side.Red, 'K'); Put(g, 4, 5, Side.Red, 'P');
        return g;
    }
}
