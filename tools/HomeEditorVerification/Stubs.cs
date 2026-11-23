using System.Text.Json;

namespace mod
{
    internal sealed class TipMsgCore
    {
        internal static TipMsgCore i = new();
        internal Player? mainplayer;
    }
    internal sealed class Player { internal Transform transform = new(); }
    internal sealed class Transform { internal Vector position, eulerAngles; }
    internal struct Vector { internal float x, y, z; internal Vector(float x,float y,float z) { this.x=x;this.y=y;this.z=z; } }
}

namespace LocalHomeModule
{
    // Only host dependencies are stubbed. All editing, persistence and history methods
    // execute the linked production HomeEditor.cs, never a reimplementation.
    internal sealed partial class HomeRuntime
    {
        private readonly Dictionary<int,Placement> local=new();
        private bool enabled=true;
        internal bool Available { get; set; }=true;
        internal string SavePath { get; set; }
        internal Placement[] Items => local.Values.OrderBy(p=>p.Id).ToArray();
        internal int DirtyCalls { get; private set; }
        internal string LastMessage { get; private set; }="";
        internal HomeRuntime(string path) { SavePath=path; }
        private void MarkDirty(bool editing=false) { DirtyCalls++; }
        private void Say(string text) { LastMessage=text; }

        // Test-only partial helpers reach production private methods and state.
        internal long SelectionEpochForTest => selectionEpoch;
        internal int UndoCount => undo.Count;
        internal int RedoCount => redo.Count;
        internal void ChangeForTest(Action<Dictionary<int,Placement>> mutation)
        {
            var next=local.ToDictionary(p=>p.Key,p=>p.Value);
            mutation(next);
            CommitEditor(new(next.Values.OrderBy(p=>p.Id).ToArray(),Origin,LastGroup));
        }
        internal void SaveForTest() => SaveEditor(EditorSnapshot());
        internal void LoadForTest() { var state=ReadEditor(); ApplyEditor(state); undo.Clear(); redo.Clear(); }
        internal void ResetForTest() => ResetEditor();
        internal void EnableForTest(bool value) => enabled=value;
        internal string StateForTest() => JsonSerializer.Serialize(EditorSnapshot());
        internal string HistoryForTest() => JsonSerializer.Serialize(new { Undo=undo, Redo=redo });
    }
}
