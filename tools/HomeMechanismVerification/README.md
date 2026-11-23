# Pure .NET mechanism verification

Run:

```powershell
dotnet run --project tools/HomeMechanismVerification/HomeMechanismVerification.csproj -c Release -p:UseSharedCompilation=false
```

The project links the production Layout.cs and Mechanisms.cs directly; no game, Unity runtime or stub implementation is used.

## Contract and timing

- `Validate` checks the full graph, baseline placements and direct furniture bindings, then returns detached definition/track/binding arrays. IDs are ordinal, case-sensitive; names need not be unique. Maximum 128 nodes, 256 points per track. A track needs at least two finite points; repeated/stationary points are accepted.
- Coordinates, gravity, velocity and Euler angular velocity components are finite and bounded to ±100000. Speed is `(0,100000]`; duration `(0,86400]` seconds, delay `[0,86400]`; target scale `[.01,100]`. All definition fields are validated even when unused by the current kind.
- `start=0` means inactive. Earlier than start is `启动前`; delay is `启动`; elapsed active time is `运行`; a completed once animation is `结束`. At the exact end, once holds its final value. Zero-length once tracks end immediately after delay. Zero-length looping tracks remain stationary and `运行`.
- Track points are absolute world-space baseline coordinates. The displacement is the sampled point minus the first track point, and completion is path length / speed, independent of Duration. Zero-length segments are skipped. Loop wraps at the endpoint; pingpong travels back and forth continuously. For the other kinds, Duration defines completion and loop/pingpong phase.
- Rotation defaults to `RotationInput="speed"`: Euler angular velocity times phase time, using Unity Z then X then Y. `angle` multiplies the supplied total Euler angle by `phaseTime/Duration`. `RotationSpace="local"` applies the immutable Euler reference snapshot as `qReference*qDelta*inverse(qReference)`; absent reference is identity. `world` uses the delta directly. Scale linearly interpolates from 1 to Scale. Gravity uses `velocity*t + gravity*t²/2`, defaulting to `(0,-9.8,0)` and zero velocity.
- Per-node starts overload accepts `IReadOnlyDictionary<string,long>`. Missing, zero or future starts leave that node's own transform at identity; an inactive child still inherits active ancestors. The original shared-start overload remains available.

## Composition and safety

A local node is `T(-pivot)*Scale*Rotation*T(pivot+displacement)` in row-vector notation. Children apply their own baseline-space transform, then the parent transform. Position denotes a baseline world pivot, so the child pivot follows its parent. Position and orientation rotations compose independently as parent * child; uniform scales multiply. Default `group` rotates both position about the pivot and orientation. `orbit` rotates position only, preserving baseline furniture angles unless an ancestor/descendant changes orientation. `spin` rotates orientation only, preserving position except for other ancestor/descendant position transforms. A spin parent passes orientation to its descendants without moving them; an orbit parent moves descendants without rotating their orientation. Each Evaluate starts independently from the given baseline and never updates definitions or items.

System.Numerics Quaternion and Matrix4x4 supply Unity-order rotation and matrix coefficients. Double translation/scale composition keeps up to 128 nested 100x scales finite before final coordinate and scale clamping. Final positions clamp to ±100000 and scale to .01–100, with standard two-decimal placement rounding. Unbound and inactive furniture retain the baseline record values.

`Preview(defs, items, id, progress)` accepts a selected rotate node and finite progress `[0,1]`. It activates only that node and its descendants at `progress*selected.Duration` seconds, ignoring delay and playback modes. Each descendant clamps at its individual end time (track length/speed, otherwise duration). Ancestors and unrelated nodes remain identity. Zero and one are exact baseline/endpoint samples, even for loop or pingpong nodes; inputs and runtime start dictionaries are not modified. Non-rotation selections, missing IDs and invalid progress are rejected.

Event fields `OnCompleteTargets` and `OnCycleTargets` default to null for old JSON. Non-null arrays are cloned and must contain unique existing IDs, without direct self references. Mutual event chains are permitted; runtime bounds their processing per tick. `CycleDuration` uses track path length / speed or Duration and doubles the result for pingpong.

Current result: **9631 checks passed**, including 100 deterministic random Euler/reference samples across all six space/style combinations, nine parent/child style pairings, old JSON defaults, reference clone isolation, invalid enum/reference rejection, angle/speed timing, loop preview endpoint, actual-second descendant preview and baseline immutability.

Coverage includes speed, absolute paths, corners and repeated points; delay/inactive/exact endpoints; loop/pingpong; XYZ rotations, pivot scale and gravity; noncommuting parent/child operations and delayed/inactive child inheritance; independently started nodes; gimbal orientations; 1000 identical evaluations without drift; clone isolation, cycles, duplicate and missing bindings, finite boundaries and 128-deep scale saturation.
