# Build 600 wall read-fact candidate

Worker base: current `dev` commit `fc89ba75` (Build 599). This lane changes only
wall read work and its focused regression harness; it does not change wall
ownership, native fade toggles, renderer availability, room-reveal rules or
committed-table publication.

## Matching hardware evidence

The main checkout's `.planning/debug/steam_frame/LogOutput.log` identifies ModBuild
599 at line 17. The final BUDGET line at line 2978 reports a 292.41 ms atomic
commit, with WallCache 121.23 ms, Mounted 55.72 ms and PropUnits 46.30 ms. Its
prepare phase already warmed 4,631 wall-cache renderers and 1,359 unit roots in
75 frames, costing 121.1 ms in total. It reports zero rejected room/board warms,
zero lost standing scopes and zero dropped anchor sets. The prewarm is therefore
already retained on this run; deleting invalidation safeguards would not address
those measurements.

## Removed work

1. `CollectWallFadeInfo` now derives shader name/variant, authored live toggle,
   authored cutoff and the toggle-native suffix once per shared Material inside
   the synchronous WallCache phase. The original admission predicate remains the
   authority. Each renderer still takes its current native shared-material list
   in the same order. Standing-prop restitution precedes all material reads;
   native-toggle counts, template donation, variant writes and first-cutoff
   selection remain per occurrence. A replacement material gets an independent
   entry even when it uses the same shader.
2. The standing figure root walk now shares its exact nearest ActorBehaviour /
   CInteractableActor / Animator result across common ancestor nodes within the
   existing synchronous figure memo window. The old live loop remains outside
   the window. The root check retains inactive-node semantics and all three
   component arms; the renderer guard's held-prop veto is untouched.

Both new caches are controlled by `PerfConfig.SharedWallReadCacheOn` (the shared
`[Optimize] SharedWallReadCache` setting, enabled by default on all platforms).
The setting is sampled once at `BeginWallCacheMaterialFacts` / `BeginFigureMemo`,
never per renderer. Disabled mode takes live material and nearest-root reads and
keeps both new dictionaries empty. It does not disable or change the older
`FigureAncestryMemo` window. A changed setting takes effect at the next synchronous
window; no cache is kept across a frame while waiting for that change.

Both dictionaries close in the production phase/pass lifecycle and drop their
Unity object references. Material facts never span a prepare slice or a frame.
The WallCache phase can change renderer material lists and property blocks, but
none of its callees writes properties, keywords or shaders of existing shared
Material instances. Native changes between commits remain live; new materials
are sampled by their identity. No wall phase moved and no atomic table state was
moved into a sliced builder.

## Verification and limits

`scripts/wall-read-facts-tests.sh` compiles the production read-fact file and
extracts the actual production material consumer, live-toggle predicate, shader
name facts and figure-memo lifecycle. Counted Unity doubles make avoided native
queries observable rather than asserting the new implementation's own cache
count. The focused suite passes **4,525 assertions** and eight negative controls:
disabled material cache, retained material references, disabled figure cache,
retained figure references, inverted native gate and missing production
material-scope `finally`, ignored material-cache bypass and ignored figure-root
bypass. Disabled-mode coverage also verifies live authored-toggle changes, zero
memo entries, one config read per window, and preservation of the old figure
ancestry window.

In the focused shared-material population, 4,000 subsequent renderer consumers
repeat zero native shader/property probes, while every renderer still reads its
live material list. The common figure ancestry is probed only once per node per
pass. These counts establish removed work, not milliseconds on Steam Frame.
The model does not emulate Unity native timing, actual scene geometry, or a
headset picture. Existing standing/floor/actor/light and held-prop rules are
unchanged by source diff; no full wall picture parity claim follows from this
focused harness.

Release build: zero errors / zero warnings. Source checks: 11 frame-order locks,
61 load-bearing diagnostic writes, 15 enum arrays. The worker does not run the
full gate; the integrator runs it once on the final merged tree.

Read WallCache and worst-commit timings in the next identical large-scenario
Frame run. Further costs in Standing, Mounted, PropUnits and scene/native
rendering remain open; no headset improvement is claimed by this lane.
