# Home editor verification

This folder has two independent consoles. `Verify.csproj` retains the older optional HomeEditorModule metadata/live checks. The new `HomeEditorVerification.csproj` verifies the local-home placement editor. Always supply the full project path because this directory contains two projects.

```powershell
dotnet run --project tools/HomeEditorVerification/HomeEditorVerification.csproj -c Release -p:UseSharedCompilation=false
```

The new console targets .NET 6 and links the production `LocalHomeModule/Layout.cs`, `Geometry.cs`, and `HomeEditor.cs` directly. No game, Unity, network, or external package references are used. `Stubs.cs` provides only the host dependencies and test-only partial helpers that reach private editor state. Snapshot validation, editing, saving, migration, undo, and redo all execute production methods.

Latest result: **12/12 scenarios passed, 0 failed** with .NET SDK 9.0.308. The expected NETSDK1138 SDK notice is emitted for the net6.0 target.

Covered scenarios:

- Pattern generation is exactly one undo operation; redo restores the entire generated group.
- Origin changes are one operation and do not move furniture or request layout sync.
- Group translation, rotation, scaling, and optional origin translation are one operation.
- A second generation replaces the active generated group; undo and redo restore both group selections correctly.
- Version 2 save/reload preserves origin, group, item placement, and scale; loading starts with empty history.
- Legacy array saves default scale to one, initialize origin from the player, remain unchanged on read, and migrate on the next successful save.
- Thirteen invalid operations preserve memory, undo/redo, save bytes, and dirty count.
- A real filesystem save failure is induced by a directory at the temporary save-file path. Generation, origin edits, group transforms, undo, and redo all preserve state, history, save bytes, and dirty count after failure.
- Exact and rounded no-op changes preserve redo and do not attempt a save, even when the temp path is blocked.
- Sixty-five edits retain exactly the latest fifty operations; undo/redo replay all retained states.
- Player and furniture origin selection copies the complete position and rotation and can be undone.
- Disabled/unavailable editors reject mutations.

Fixtures use a unique process-temporary directory printed in the result. The console returns nonzero on any failed scenario. These checks do not verify Unity rendering, UI input, or multiplayer behavior.
