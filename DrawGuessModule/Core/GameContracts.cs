using System;
using System.Collections.Generic;

namespace DrawGuessModule.Core
{
    /// <summary>Identity is supplied separately by the authenticated server transport, never in this DTO.</summary>
    public sealed class GameCommand
    {
        public string Type { get; set; } = "";
        public string SessionId { get; set; } = "";
        public long RoundId { get; set; }
        public long ExpectedVersion { get; set; } = -1;
        public string Name { get; set; } = "";
        /// <summary>For Type="word", the current authenticated drawer submits a trimmed 1..16 character answer in the choosing phase, bound to RoundId.</summary>
        public string Text { get; set; } = "";
        public int TurnsPerPlayer { get; set; } = 1;
        public int RoundSeconds { get; set; } = 60;
        public int RevealSeconds { get; set; } = 5;
        public DrawingOperation? Operation { get; set; }
    }

    public sealed class DrawingOperation
    {
        public string Tool { get; set; } = "line";
        public int X1 { get; set; }
        public int Y1 { get; set; }
        public int X2 { get; set; }
        public int Y2 { get; set; }
        public string Color { get; set; } = "#202020";
        public int Size { get; set; } = 4;
        public bool NewStroke { get; set; }
        /// <summary>Starts at 1 each round; strictly increases per attempted operation. Gaps up to 4096 tolerate rejected commands; old IDs deduplicate.</summary>
        public long OpId { get; set; }
        public long RoundId { get; set; }
        public long Sequence { get; set; }
        public long StrokeId { get; set; }
        public DrawingOperation Copy() => (DrawingOperation)MemberwiseClone();
    }

    public sealed class CommandResult
    {
        public bool Success { get; set; }
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public bool Changed { get; set; }
        public long Version { get; set; }
        public DrawingOperation? Operation { get; set; }
    }

    public sealed class PlayerSnapshot
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Score { get; set; }
        public bool HasGuessed { get; set; }
    }

    public sealed class GameSnapshot
    {
        public string SessionId { get; set; } = "";
        public string HostId { get; set; } = "";
        public string Phase { get; set; } = "lobby";
        public long Version { get; set; }
        public long DrawingVersion { get; set; }
        public long RoundId { get; set; }
        public int RoundNumber { get; set; }
        public int TotalRounds { get; set; }
        public string DrawerId { get; set; } = "";
        /// <summary>Seconds until the current phase deadline: choosing allows 30 seconds to submit, playing starts its full round timer only after submission.</summary>
        public double RemainingSeconds { get; set; }
        /// <summary>Null until reveal except in the current drawer's personally addressed snapshot.</summary>
        public string? Word { get; set; }
        public int WordLength { get; set; }
        public string LastEvent { get; set; } = "";
        public int ReplayCount { get; set; }
        public List<PlayerSnapshot> Players { get; set; } = new List<PlayerSnapshot>();
    }

    public sealed class DrawingChunk
    {
        public string SessionId { get; set; } = "";
        public long RoundId { get; set; }
        public long DrawingVersion { get; set; }
        public int Index { get; set; }
        public int Count { get; set; }
        public List<DrawingOperation> Operations { get; set; } = new List<DrawingOperation>();
    }
}
