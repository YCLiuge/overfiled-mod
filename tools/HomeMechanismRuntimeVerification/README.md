# Home mechanism runtime integration verification

Run:

```powershell
dotnet run --project tools/HomeMechanismRuntimeVerification/Verify.csproj -c Release -p:UseSharedCompilation=false
```

Links the real production HomeMechanisms.cs, HomeMechanismEvents.cs, HomeRotationPreview.cs, Mechanisms.cs and Layout.cs. Stubs only host/player, baseline Items, deterministic event clocks and Publish transport; never opens a socket or calls the actual network.

Event semantics: once completes naturally after delay + endpoint; loop emits after each endpoint period; pingpong emits after a complete outward/return period. Event targets and descendants restart at observation tick + 300ms, using the same subtree/start helper as explicit starts. Global starts reserve all configured event target subtrees. A snapshot queues and deduplicates at most 128 target nodes before application; missed cycles advance the cursor and emit once, with no catch-up queue. Mutual event graphs are allowed, direct self targets are rejected. Repeating zero-length tracks have no finite cycle boundary; zero-length once tracks complete after delay. Explicit starts restart their selected subtree; global starts preserve active starts. Normal and debug cursors are independent. Every tick always advances the normal timeline, including while debug is open; debug adds a second independent pass. Each pass uses its own starts, frozen value and cursors, without switching the runtime mode. Normal event starts mark the network state for synchronization; debug event starts never do. Freezing either timeline does not freeze the other. Manual end freezes without events; reset/baseline/load clear cursors. Preview drafts never supply event definitions, while saved timeline events keep processing.

Event coverage includes exact once/loop/pingpong boundaries and delays; long frame gaps; manual freeze, reset and restart epochs; debug isolation; global reservations; deferred zero-length chains and bounded mutual graphs; delete cleanup; persistence, old JSON defaults, clone isolation and invalid network targets; remote render isolation and preview draft isolation. Event clock injection avoids wall-clock sleeps.

Latest run: 40 passed, 0 failed, exit 0 (including 19 event scenarios). NETSDK1138 is the existing .NET 6 target warning; CS0649 is the deliberate default player-position stub.

Coverage: config save/load without restoring starts; independently starting nodes and complete descendant subtrees with one UTC start; end/reset; debug start/end/reset preserving normal starts/frozen; baseline edit guards; trigger edge and reset rearming; detached trigger targets; reordered chunks, malformed base64, old revisions, excessive future revision rejection; expired batch rejection; expired-owner slot recovery; protection of bindings from deletion/ID reuse; async pending-send exclusion and failed-send retry using latest reset state; newer LH2 fallback priority over mismatched mechanism baseline.

The async test uses a TaskCompletionSource transport and does not test the actual HomeRuntime Publish ChannelGate implementation. HomeRuntime scene/channel transitions were reviewed statically, not executed here. Actual OS clock changes, Unity and two-client behavior are not exercised. Guard tests call the production partial guards from the stub edit entry point; they do not link HomeEditor.cs or execute game commands.

Earlier runs reproduced expired batch resurrection, expired owner slot exhaustion and unbounded revision poisoning. The root agent fixed production during this review; final tests verify the updated contracts. Initial baseline-mutation injections bypassed the new production guards and were replaced with guarded edits, so those injections are not current findings.
