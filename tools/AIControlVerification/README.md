# AIControlHttpServer standalone verification

This .NET 6 console project links the actual transport source from [AIControlHttpServer.cs](../../Logic/管理器/AIControlHttpServer.cs) into the test assembly. It references only framework assemblies and the workspace's [Newtonsoft.Json.dll](../../libs/Newtonsoft.Json.dll), with `Private=true` so the DLL is copied beside the runnable assembly. There is no test-framework package or game-library reference. [NuGet.Config](NuGet.Config) clears package feeds, so restore does not require network packages.

All harness sources, configuration, build output, and result files live in this directory. The suite starts its own loopback listeners and never contacts the running game. It also launches the existing [PowerShell client](../../Invoke-GameCommand.ps1) against the fixture's own ephemeral port.

## Run

From the repository root on Windows:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\AIControlVerification\run.ps1
```

The runner sets both required environment variables, builds the project, writes [verification-results.txt](verification-results.txt), and returns the console suite's exit code. A passing run exits 0; a failed check or startup failure exits 1. The suite takes roughly 18 seconds because it checks the real 15-second queue-expiration interval. The intentional callback-failure test prints PowerShell error output and checks that the client exits nonzero; that output is expected in an otherwise passing suite.

You can select an installed runtime explicitly:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\AIControlVerification\run.ps1 -RuntimeVersion 6.0.25
```

To build/run manually from this directory:

```powershell
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet build .\AIControlVerification.csproj --configfile .\NuGet.Config
dotnet run --project .\AIControlVerification.csproj --no-build
```

## Checks

The suite reports 12 groups:

1. Health before and after Tick, version/session metadata, queue count, and command catalog.
2. Queued command acceptance, no HTTP-thread execution, queryable receipts, same-ID replay before and after completion, exactly one execution, payload/target conflicts (409), and missing receipt (404).
3. Waiting HTTP request completes after queue acceptance is observed and the designated Tick thread runs.
4. Six concurrent state requests and six concurrent history requests retain distinct per-call data, receipt correlation, and the history endpoint's successful array shape.
5. Chat queuing, idempotency, and safe truncation at a UTF-16 surrogate boundary.
6. Malformed/non-object JSON (400), incorrect method (405), unsupported/missing content type (415), unknown route (404), internal/edge newlines (400), invalid fields, Origin rejection (403), and 16 KiB limits (413) for both known-length and chunked requests. A chunked multi-byte UTF-8 probe crosses the byte limit while remaining below the character count.
7. Callback exception returns 422, retains a failed receipt/error, logs the failure, and never reexecutes on same-ID retry.
8. Actual PowerShell client health, waiting command, same-ID replay, and nonzero exit on callback failure, using inherited stdout/stderr without redirected child-process pipes.
9. A queued request expires after an asynchronous 15.1-second wait, receipt replay returns 408, stale health reports no active main thread, and a later Tick never executes the expired callback.
10. All game-facing execute/state/history/chat callbacks use the actual recorded dedicated Tick thread. Catalog is immutable metadata and intentionally runs directly on the HTTP thread.
11. Forced listener closure clears `IsRunning`, prevents queued execution, and permits a new session on the exact same port.
12. Dispose cancels queued work; a cancelled receipt can be inspected after reopening; cancelled replay returns 503; a fresh core uses a new session, has no old receipt, and accepts the same client ID independently.

## Scheduling and isolation

A named background thread owns every Tick call. Async test continuations may use different pool threads; callback assertions compare against the thread ID recorded immediately before the actual Tick call. Tick occurs only when the test explicitly requests it. Tests wait for the health pending count before pumping instead of assuming that issuing an HTTP request already enqueued it.

Each new fixture reserves an ephemeral port with `TcpListener(IPAddress.Loopback, 0)` and releases that reservation with `Stop()` before the transport binds. (`TcpListener` is not `IDisposable` on .NET 6.) The unavoidable reservation-to-bind race is limited to those two immediate operations. The listener recovery check deliberately reuses the prior fixture's port.

## Observed results and launch diagnostics

The full 12-group suite passed on .NET 6.0.25 and .NET 6.0.36, x64. Build completed with zero warnings and zero errors. In the main fixture, all 18 queued callbacks ran on its designated Tick thread. The suite includes regression checks for edge-newline validation, chunked UTF-8 byte counting, and listener-fault state handling.

The generated apphost executable produced Windows HttpListener native error 6 (`invalid handle`) on this host, including with a raw listener unrelated to the transport. Running the same assembly with `dotnet` passed both raw listener and transport checks. The project therefore sets `UseAppHost=false`, so the ordinary runner uses the verified DLL launch path. This observation does not establish a .NET runtime incompatibility.

For startup isolation after building:

```powershell
dotnet .\bin\Debug\net6.0\AIControlVerification.dll --listener-smoke
dotnet .\bin\Debug\net6.0\AIControlVerification.dll --core-smoke
```

These modes start/close an own ephemeral listener without touching the game. Runtime/architecture and any startup stack are printed for diagnosis.
