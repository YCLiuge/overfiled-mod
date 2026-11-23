namespace Il2CppInutan
{
    public sealed class IHomeSystem
    {
        public static IHomeSystem? sys = new();
        public int _channelId_k__BackingField = 101;
    }
}
namespace MelonLoader
{
    public static class MelonLogger { public static void Msg(string message) => Console.WriteLine(message); }
}
namespace mod
{
    public sealed class TipMsgCore
    {
        public static TipMsgCore i = new();
        public string mainplayername = "local";
        public static event Action<string, string>? Tip_actions;
        public static void Emit(string message) => Tip_actions?.Invoke(i.mainplayername, message);
        public static int Subscriptions => Tip_actions?.GetInvocationList().Length ?? 0;
    }
}
namespace DrawGuessModule
{
    public sealed class StubBoard
    {
        public Core.SceneBoardPlacement Placement = new();
        public string Label = "";
        public List<string> Operations = new();
    }
    public sealed class Panel
    {
        public readonly Dictionary<string,StubBoard> Boards = new(StringComparer.Ordinal);
        public Action<string>? CommandCallback;
        public void SelectBoard(string id) => CommandCallback?.Invoke("dg boardselect " + id);
        public void JoinBoard(string id,string roomInstance) => CommandCallback?.Invoke("dg boardjoin " + id + " " + roomInstance);
        public bool CanDraw;
        public string Secret = "", Score = "", Status = "", Detail = "";
        public string Mode = "guess", PlayerName = "";
        public string TopicPhase = "";
        public bool IsTopicDrawer;
        public int SaveCount;
        public string? SavedName;
        public string SavedMode = "";
        public List<string> SavedOperations = new();
        public List<string> Operations = new();
        public Core.SceneBoardPlacement InitialBoardPlacement = new() { X = 1, Y = 2, Z = 3, Yaw = 180 };
        public Core.SceneBoardPlacement? Board;
        public bool AutomaticBoardPlacement = true;
        public int AutomaticBoardCreations;
    }
    // A separate UI context per client lets the production runtime be exercised without Unity native calls.
    public static class PaintingSystem
    {
        public static Panel Current = new();
        public static void Initialize(Action<string> action) { Current.CommandCallback = action; }
        public static void SetChannelBoard(string id,Core.SceneBoardPlacement placement,string label,List<Core.DrawingOperation> operations,bool reset)
        {
            if(!Current.Boards.TryGetValue(id,out var board)) Current.Boards.Add(id,board=new StubBoard());
            board.Placement=placement.Copy();board.Label=label;
            if(reset) board.Operations.Clear();
            foreach(var op in operations) Apply(board.Operations,op.Tool,op.X1,op.Y1,op.X2,op.Y2,op.Color,op.Size,op.NewStroke);
        }
        public static void RemoveChannelBoard(string id)=>Current.Boards.Remove(id);
        public static void ClearChannelBoards()=>Current.Boards.Clear();
        private static void Apply(List<string> operations,string tool,int x1,int y1,int x2,int y2,string color,int size,bool newStroke)
        {
            if(tool=="clear")operations.Clear();
            else operations.Add($"{tool}:{x1}:{y1}:{x2}:{y2}:{color}:{size}:{newStroke}");
        }
        public static void Tick() { }
        public static void Dispose() { Current.CanDraw = false; RemoveSceneBoard(); }
        public static void Show() { GetSceneBoardPlacement(); }
        public static void Hide() { }
        public static void SetMode(string mode) => Current.Mode = mode;
        public static void SetPlayerName(string name) => Current.PlayerName = name;
        public static void SetTopicState(string phase,bool isDrawer) {Current.TopicPhase=phase;Current.IsTopicDrawer=isDrawer;}
        public static string SaveLocalPainting(string? name)
        {
            Current.SaveCount++;
            Current.SavedName = name;
            Current.SavedMode = Current.Mode;
            Current.SavedOperations = Current.Operations.ToList();
            return "stub-export.png";
        }
        public static Core.SceneBoardPlacement? GetSceneBoardPlacement()
        {
            if (Current.Board == null && Current.AutomaticBoardPlacement)
            {
                Current.AutomaticBoardCreations++;
                SetSceneBoardPlacement(Current.InitialBoardPlacement);
            }
            return Current.Board?.Copy();
        }
        public static void SetSceneBoardPlacement(Core.SceneBoardPlacement placement)
        {
            if (placement?.IsValid == true) Current.Board = placement.Copy();
        }
        public static void SetSceneBoardAutomaticPlacement(bool enabled)
        {
            Current.AutomaticBoardPlacement = enabled;
            if (!enabled) RemoveSceneBoard();
        }
        public static void PlaceSceneBoard() => SetSceneBoardPlacement(Current.InitialBoardPlacement);
        public static void RemoveSceneBoard() => Current.Board = null;
        public static string DescribeSceneBoard() => Current.Board == null ? "场景画架：尚未生成" :
            FormattableString.Invariant($"场景画架：({Current.Board.X:F2}, {Current.Board.Y:F2}, {Current.Board.Z:F2}) | 共享画布 True");
        public static void ResetCanvas() => Current.Operations.Clear();
        public static void SetState(string status,string detail,bool canDraw) { Current.Status=status;Current.Detail=detail;Current.CanDraw=canDraw; }
        public static void SetRoomState(string score,string drawer,string secret) {Current.Score=score;Current.Secret=secret;}
        public static void ApplyOperation(string tool,int x1,int y1,int x2,int y2,string color,int size,bool newStroke)
        {
            if(tool=="clear") Current.Operations.Clear();
            else Current.Operations.Add($"{tool}:{x1}:{y1}:{x2}:{y2}:{color}:{size}:{newStroke}");
        }
    }
}
