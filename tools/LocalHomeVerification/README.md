# LocalHomeVerification

Independent .NET 6 console verification for `LocalHomeModule/Layout.cs`. The project links the production source directly and has no project references, package references, game assemblies, or third-party test framework.

Run from the repository root:

```powershell
dotnet run --project tools/LocalHomeVerification/LocalHomeVerification.csproj --configuration Release
```

The process prints each case and returns 0 only when every case passes. Latest verification: **176/176 passed, 0 failed** with .NET SDK 9.0.308. The SDK reports NETSDK1138 because the project's net6.0 target matches the production module.

Additional v1.5 coverage includes LH2 delta transactions and legacy LH1 reading; default Scale=1 for old saves, finite 0.01–100 limits and two-decimal normalization; single-item modifications, additions and deletions; unchanged layouts producing no frames; empty-layout baselines, replay suppression, atomic commits and full-snapshot recovery after missing or damaged transactions. Idle polling detects transaction timeouts without requiring a subsequent edit.

Coverage:

- Complete atomic snapshots, sorted output, missing items, and empty snapshots. There is no furniture count cap: begin accepts any nonnegative Int32 count, including Int32.MaxValue, without allocating capacity from that count. Storage grows only as valid new items arrive.
- Identical duplicate items and begin frames are idempotent. Conflicting duplicates, changed counts, extra items, and invalid same-batch frames discard the pending snapshot.
- The inactivity deadline is inclusive at 30 seconds, initially measured from begin and refreshed only by a valid, previously unseen item in the active batch. Repeated begin/item frames, unrelated batches, and null frames do not extend it. Expiration and rollback before the last activity discard pending state. A simulated 1024-item batch receives one item every 20 seconds for over five hours and still commits.
- Different batch item/end frames cannot contaminate the active batch; a valid new begin replaces the old batch. Completion or rejection clears pending state, and a fresh batch can succeed.
- Encode/decode/receive round trips with 1024 items and all three rotation axes, independently generated batch IDs, null inputs, positive unique IDs and item IDs bounded by Int32.MaxValue.
- Placement retains Yaw for the Y axis and appends optional Pitch (X axis) and Roll (Z axis), both defaulting to zero. Old save JSON, old LH1 item frames, and existing six-argument constructors retain their previous behavior. New save JSON preserves all three axes; either optional wire field may also appear alone.
- Finite coordinates and all three angles within inclusive +/-100000; NaN, infinities, and out-of-range values are rejected. Optional Pitch/Roll fields, when present in a frame, must contain valid numbers; null, strings, booleans, arrays, objects, duplicate properties, overflow, and invalid ranges are rejected.
- `Placement.Rounded()` normalizes X/Y/Z and Yaw/Pitch/Roll with `Math.Round((double)v, 2, MidpointRounding.AwayFromZero)` and converts all zero results to positive zero. Exact positive/negative midpoints, ordinary decimals, float-to-double behavior, signed/tiny zeros, and idempotence are covered. `Validate` checks original values and unique IDs before returning a fresh rounded array without modifying its input; invalid ranges, IDs, NaN, and infinities stay rejected.
- Save JSON followed by validation, Encode/Decode/receive, and incoming raw item JSON converge on the same rounded layout. Old saves and optional rotation axes retain their compatibility.
- LH1 `request` frames round-trip through JSON serialization and Decode, require a valid batch ID, zero count, and no item. A receiver ignores valid requests without committing, discarding, or extending the inactivity deadline of a pending snapshot. Invalid request counts, items, and batches are rejected.
- JSON prefix, syntax, object shape, required fields, numeric types, overflow, duplicate properties, batch format, frame kind, counts, and the inclusive 1024-character frame size boundary.

Protocol validation rejects malformed wire frames by returning null. Direct receiver calls also validate frames. As with any decoder returning null for malformed input, callers should forward only successfully decoded frames; a discarded malformed string carries no trustworthy batch identity.
