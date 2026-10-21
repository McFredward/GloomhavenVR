# Current XR display GPU timing binding

The B716 Quest performance investigation exposed an existing shared measurement
bug: PerfMonitor resolved GPU samples only through the legacy XRStats class,
although the actual Unity2021.3 provider exposes TryGetAppGPUTimeLastFrame on
XRDisplaySubsystem. Its output is seconds; window GPU samples are milliseconds.

Bind the current display API first through the existing cached open-delegate
mechanism. Convert successful finite, nonnegative provider seconds to milliseconds.
A false result, invalid value or overflowing conversion remains unavailable and
can use the established legacy millisecond fallback. Do not relabel unavailable
as a measured zero. Leave refresh, dropped/presented counters and their existing
semantics unchanged; this repair is deliberately limited to GPU sampling.

Primary reference and matching installed SDK XML:
https://docs.unity3d.com/2021.3/Documentation/ScriptReference/XR.XRDisplaySubsystem.TryGetAppGPUTimeLastFrame.html

Focused proof compiles the actual nested production XrProbe with controlled
provider availability/values, not a copied measurement implementation. Twelve
assertions and four compiling causal controls cover binding, units, priority,
false/invalid values, fallback and genuine zero. Four additional actual owned SDK
metadata assertions qualify the provider method shapes without invoking native
engine callbacks. The complete original prototype and final source are retained;
no physical XR provider or GPU result is claimed from those host tests.

Shared integration passes strict Release0warnings/errors and source16/16.
The new portable fixture is registered in the maintained local/CI inventory.
Unchanged gameplay/network evidence is inherited from current dev; this is not
a new complete local gameplay gate. No new PC hardware artifact is handed out
by this source-only repair. The separate Quest update contains its own new build
number and separately gated presentation/capture changes.

Ignored evidence:
`/home/claw/quest3-local/build/evidence/quest717-review-20261021/shared-xr/`.
All test children are reaped before the common completion cleanup; source, logs,
results and compiler/input identities remain. Live provider timing availability
still depends on the XR plugin/hardware and must be checked in the next capture.
