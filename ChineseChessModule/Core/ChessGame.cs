#nullable enable
using System;

namespace ChineseChessModule.Core
{
    public enum Side { Red, Black }

    public sealed class Piece
    {
        public Side Side;
        public char Kind;
    }

    /// <summary>Pure .NET Xiangqi rules. Index = y * 9 + x; Red starts at y=9.</summary>
    public sealed class ChessGame
    {
        public Piece?[] Board = new Piece?[90];
        public Side Turn;
        public Side? Winner;
        public bool Finished;
        public long Revision;
        public string Status = string.Empty;

        public ChessGame() { Reset(); }

        public void Reset()
        {
            Board = new Piece?[90];
            const string back = "RNBAKABNR";
            for (int x = 0; x < 9; x++)
            {
                Put(x, 0, Side.Black, back[x]);
                Put(x, 9, Side.Red, back[x]);
            }
            Put(1, 2, Side.Black, 'C'); Put(7, 2, Side.Black, 'C');
            Put(1, 7, Side.Red, 'C'); Put(7, 7, Side.Red, 'C');
            for (int x = 0; x < 9; x += 2)
            {
                Put(x, 3, Side.Black, 'P'); Put(x, 6, Side.Red, 'P');
            }
            Turn = Side.Red;
            Winner = null;
            Finished = false;
            Revision++;
            UpdateStatus();
        }

        /// <summary>Restore an authoritative snapshot without incrementing its revision.</summary>
        public void LoadSnapshot(Piece?[] board, Side turn, Side? winner, bool finished, long revision)
        {
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (board.Length != 90) throw new ArgumentException("Board must contain 90 squares.", nameof(board));
            if (!ValidSide(turn) || (winner.HasValue && !ValidSide(winner.Value)))
                throw new ArgumentOutOfRangeException(nameof(turn));
            if (revision < 0) throw new ArgumentOutOfRangeException(nameof(revision));
            var copy = new Piece?[90];
            for (int i = 0; i < 90; i++)
            {
                Piece? p = board[i];
                if (p == null) continue;
                if (!ValidSide(p.Side) || "KABNRCP".IndexOf(p.Kind) < 0)
                    throw new ArgumentException("Snapshot contains an invalid piece.", nameof(board));
                copy[i] = new Piece { Side = p.Side, Kind = p.Kind };
            }
            Board = copy;
            Turn = turn; Winner = winner; Finished = finished; Revision = revision;
            UpdateStatus();
        }

        public bool TryMove(int from, int to, out string error)
        {
            error = string.Empty;
            if (Finished) { error = "棋局已结束"; return false; }
            if (!ValidBoard() || !Inside(from) || !Inside(to)) { error = "棋盘或位置无效"; return false; }
            Piece? piece = Board[from];
            if (piece == null) { error = "起点没有棋子"; return false; }
            if (piece.Side != Turn) { error = "尚未轮到该方走棋"; return false; }
            if (!PseudoLegal(from, to)) { error = "不符合棋子走法"; return false; }
            if (!SafeMove(from, to, Turn)) { error = "走棋后己方将帅受将军"; return false; }

            Side mover = Turn;
            Piece? captured = Board[to];
            Board[to] = piece; Board[from] = null;
            Turn = Opponent(Turn);
            if ((captured != null && captured.Kind == 'K') || !HasLegalMove(Turn))
            {
                Finished = true;
                Winner = mover;
            }
            Revision++;
            UpdateStatus();
            return true;
        }

        public bool InCheck(Side side)
        {
            if (!ValidSide(side)) throw new ArgumentOutOfRangeException(nameof(side));
            if (!ValidBoard()) return true;
            int king = -1;
            for (int i = 0; i < 90; i++)
                if (Board[i]?.Side == side && Board[i]?.Kind == 'K') { king = i; break; }
            if (king < 0) return true;
            for (int i = 0; i < 90; i++)
                if (Board[i] != null && Board[i]!.Side != side && PseudoLegal(i, king)) return true;
            return false;
        }

        public int[] LegalTargets(int from)
        {
            if (Finished || !ValidBoard() || !Inside(from) || Board[from] == null || Board[from]!.Side != Turn)
                return Array.Empty<int>();
            var result = new System.Collections.Generic.List<int>();
            for (int to = 0; to < 90; to++)
                if (PseudoLegal(from, to) && SafeMove(from, to, Turn)) result.Add(to);
            return result.ToArray();
        }

        public bool Resign(Side side)
        {
            if (Finished || !ValidSide(side)) return false;
            Finished = true; Winner = Opponent(side); Revision++;
            UpdateStatus();
            return true;
        }

        private bool HasLegalMove(Side side)
        {
            for (int from = 0; from < 90; from++)
                if (Board[from]?.Side == side)
                    for (int to = 0; to < 90; to++)
                        if (PseudoLegal(from, to) && SafeMove(from, to, side)) return true;
            return false;
        }

        private bool SafeMove(int from, int to, Side side)
        {
            Piece? moving = Board[from], captured = Board[to];
            Board[to] = moving; Board[from] = null;
            try { return !InCheck(side); }
            finally { Board[from] = moving; Board[to] = captured; }
        }

        private bool PseudoLegal(int from, int to)
        {
            if (from == to) return false;
            Piece? p = Board[from], target = Board[to];
            if (p == null || (target != null && target.Side == p.Side)) return false;
            int x = from % 9, y = from / 9, tx = to % 9, ty = to / 9;
            int dx = tx - x, dy = ty - y, ax = Math.Abs(dx), ay = Math.Abs(dy);
            switch (p.Kind)
            {
                case 'K':
                    if (target?.Kind == 'K' && dx == 0 && Between(from, to) == 0) return true;
                    return ax + ay == 1 && Palace(tx, ty, p.Side);
                case 'A': return ax == 1 && ay == 1 && Palace(tx, ty, p.Side);
                case 'B':
                    return ax == 2 && ay == 2 && (p.Side == Side.Red ? ty >= 5 : ty <= 4)
                        && Board[(y + dy / 2) * 9 + x + dx / 2] == null;
                case 'N':
                    if (ax == 2 && ay == 1) return Board[y * 9 + x + dx / 2] == null;
                    if (ax == 1 && ay == 2) return Board[(y + dy / 2) * 9 + x] == null;
                    return false;
                case 'R': return (dx == 0 || dy == 0) && Between(from, to) == 0;
                case 'C': return (dx == 0 || dy == 0) && Between(from, to) == (target == null ? 0 : 1);
                case 'P':
                    int forward = p.Side == Side.Red ? -1 : 1;
                    bool crossed = p.Side == Side.Red ? y <= 4 : y >= 5;
                    return (dx == 0 && dy == forward) || (crossed && ay == 0 && ax == 1);
                default: return false;
            }
        }

        private int Between(int from, int to)
        {
            int step = from % 9 == to % 9 ? (to > from ? 9 : -9) : (to > from ? 1 : -1);
            int count = 0;
            for (int i = from + step; i != to; i += step) if (Board[i] != null) count++;
            return count;
        }

        private static bool Palace(int x, int y, Side side) => x >= 3 && x <= 5 && (side == Side.Red ? y >= 7 && y <= 9 : y >= 0 && y <= 2);
        private static bool Inside(int i) => i >= 0 && i < 90;
        private static bool ValidSide(Side side) => side == Side.Red || side == Side.Black;
        private bool ValidBoard() => Board != null && Board.Length == 90;
        private static Side Opponent(Side side) => side == Side.Red ? Side.Black : Side.Red;
        private void Put(int x, int y, Side side, char kind) => Board[y * 9 + x] = new Piece { Side = side, Kind = kind };
        private void UpdateStatus()
        {
            if (Finished) Status = Winner.HasValue ? (Winner == Side.Red ? "红方获胜" : "黑方获胜") : "棋局结束";
            else Status = (Turn == Side.Red ? "红方走棋" : "黑方走棋") + (InCheck(Turn) ? "（将军）" : "");
        }
    }
}
