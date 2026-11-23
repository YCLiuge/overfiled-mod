# DrawGuess runtime integration verification

Run from the repository root:

```powershell
dotnet build .\服务器\Chat.Server\Chat.Server.csproj -c Release
dotnet run --project .\tools\DrawGuessIntegrationVerification\Verify.csproj -c Release
```

The runner starts a real relay on an ephemeral loopback port, connects production ChatClient/NetChater/MainLogic/Core implementations, and terminates only the relay it starts. It uses stub Unity panel/scene rendering and does not contact the native game, production relay, or game directory.

The latest completed run reported **62 checks passed**:

| Coverage | Checks |
| --- | ---: |
| BOARD relay authentication, channel visibility, membership and size limit | 4 |
| Ordinary board ownership, per-session movement, independent canvases, late chunk replay, room binding, game topic/drawing authority, secrecy, scoring and old room-instance replay | 25 |
| Channel switching, missing channel, undiscovered join refusal and incomplete/out-of-order board replay | 7 |
| Nickname collision retry and connection cancellation | 7 |
| Endpoint parsing, shared host socket, chat coexistence, unload/reload and replacement-session reconnect | 19 |

The game cases verify a separate 30-second choosing deadline, no drawing during choosing, authenticated current-drawer word submission, a fresh full drawing timer after submission, drawer-private word access, public reveal, and authority transfer to a nonhost drawer. They do not wait 30 seconds for a real timeout; deterministic timeout rules are covered by the separate core verification suite.

The scene stub stores independent operation lists, positions and captions per authenticated owner, and exposes the same board selection callback commands as production. PNG export here verifies the selected painting passed to the save API. Actual PNG decoding/pixel assertions remain in the optional native checks and were not executed in this isolated run.

Host reconnect tests deliberately change the relay nickname while retaining the game character name. They wait for the new authenticated owner and room instance to be discovered, then click that exact board. This avoids interpreting a still-visible old board with the same room name as the new host. Ordinary unambiguous `dg join` and stale board-instance rejection have separate coverage.

## Optional native checks

The `--live` path requires an updated native module with BOARD and choosing/word support, plus explicit `DRAWGUESS_ALLOW_UPDATED_LIVE=1`. It can call the native game HTTP interface and create PNGs. It was **not run** during this work and must not be used against an older module.

Native checks now create an owner board before hosting, switch from the resulting free mode back to guess mode, submit topics before drawing, and verify ordinary owner paintings rather than legacy cooperative free rooms. Optional native shared-connection checks also create/bind a fresh owner board after reload/reconnect.

The project includes production board runtime partials while excluding Unity-dependent board rendering, which is replaced by the scene stub.
