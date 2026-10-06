# Build630: expensive terrain admission in the Frame628 capture

The immutable hardware capture identifies Build628/176beb741, Steam Frame,
Turnip Adreno 750, Direct3D11 and MultiPass with 3408x3408 eye targets. The exact
inputs and SHA receipt are retained in the main checkout under
`.planning/debug/frame630-review-20261006/inputs/`.

Completed post-native-loading windows before the open-all-doors action report
68--83ms mean frames and 37--45ms of measured mod work. Terrain pre-cull alone
costs 16--17ms per frame, usually across two eye callbacks. These are **not**
three-room settled averages. After all doors open, surviving spike samples
record terrain pre-cull at 40--72ms and environment pre-cull at 8--10ms; native
frame totals reach hundreds of milliseconds without a coincident GC collection.
Nested WallFade scope costs cannot be added to their enclosing scope.

Prepared terrain membership reaches 299 surfaces, including unopened/native
inactive rooms. Original admission repeated twelve native component lookups for
each distinct ancestor and revalidated the same original mesh bank metadata for
every repeated wall. Cheap shading and lower geometry do take effect, but that
does not establish a net saving against their CPU cost.

The implementation now reads each ancestor's component list once, evaluates the
same exclusions and native owners in managed code, and reads repeated source
mesh admission once per synchronous Update/camera invocation. Native inactive or
disabled renderers are rejected before bank/material/proxy work. There is no
cross-frame admission cache: each later camera rechecks current component,
parent, mesh and visibility state. Existing native writer/clone restoration,
command-buffer refusal and pre-native-culling boundary remain intact.

Focused real Unity camera evidence passes 293 assertions plus causal actor,
wall-owner, redundant mesh-read and stale-admission controls. The full final gate
must retain every original terrain/shader/native source control as well. This is
proof of source work reduction and tested rendering boundaries, not a headset
FPS guarantee.

The diagnostic window also had a blind spot: reported rate changes every5--10s
reset windows holding fewer than120 frames. At severe low FPS this discarded
all completed three-room statistics. Windows lasting at least5s now retain
at least2 genuine frames with their actual sample count; quick slider edits
still only reset. Optimize setting changes close Debug measurement windows, and
end-of-window configured values include terrain, batching, shading, cache and
cloth/FX controls. Configured snapshots are explicitly not latched A/B evidence.
Reported refresh changes no longer claim reprojection as an established fact.

## Further steps ordered by remaining evidence

1. Measure the same parked three-room view with cheap walls and geometry budgets
   independently switched off/on, using the new settings boundaries. Price the
   net effect of substitutions instead of assuming fewer triangles mean lower CPU.
2. If private proxy synchronization still dominates, develop an optional coherent
   static renderer snapshot with explicit native mutation invalidation and a
   synchronous original-renderer fallback before native culling. Prove late
   reparenting, component changes, native cloning, room visibility and both eyes
   before enabling it. A timer-only stale cache is insufficient.
3. Reduce WallFade maintenance on stable boards by making reclaimed visibility
   work incremental and sharing exact reads within one pass. Preserve continuous
   visible dissolution and native held/interaction exclusions. The captured
   pipeline/fast-reclaim cost makes this more relevant than loading-time pauses.
4. Extend exact coarse mesh coverage only where the remaining visible native
   renderer census proves a gap. Prefer coarse three-dimensional walls/floors with
   original collision and tactical contents; no room hiding or 2.5D board.
5. If actual render submission/GPU cost remains dominant after CPU cleanup, offer
   independent native shadow/material/effect compromises, with measured surviving
   work. Keep common PC/Frame code/assets and separate unset-entry defaults.

No remote peer participates in this capture, so none of its counters certifies
multiplayer capacity. The next paired run needs the same parked three-room view
with native input and shared presentation active. Idle visual degradation is
withdrawn by the maintainer and is not a fallback strategy.
