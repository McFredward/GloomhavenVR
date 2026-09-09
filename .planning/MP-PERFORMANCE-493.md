# Build 493: multiplayer presentation performance

## Scope and evidence

The user requested CPU/memory optimization for up to four simultaneous remote-board presentations, with no lost features, visual shortcuts or animation degradation. Workers started from dev `baaf9871` in separate `mp493-capture`, `mp493-playback` and `mp493-board` worktrees. The integrator reviewed and combined their disjoint changes and the native-send optimization.

Existing hardware evidence is build 491 on both multiplayer clients and build 492 in the new singleplayer log. Near 4,000 visible renderers with both rooms open, the singleplayer 30-second window averages 16.50 ms/frame; four multiplayer windows average 22.37 ms, frame-count weighted. The latter contain 3.07 ms/frame in `Net.Board`, including 1.48 ms in native revision scanning. These are nested scopes, not additive costs. Matching graphics settings do not control camera pose, activity, session age or build differences. The singleplayer log contains only 90 seconds of completed steady windows. These readings support optimizing multiplayer CPU work but are not a build 493 benchmark or proof of a memory leak.

Detailed input selection is in ignored `.planning/debug/sp492-vs-mp491.md`. The older second_logs description in MP-492-PERF.md predates replacement of those logs.

## Implemented changes

### Owner card capture

`CardAppearanceBindings` keeps private scratch nodes, group lists and shader-support masks. It still reads every native channel and scans the complete hierarchy each sample. It lazily sorts the group addresses only for capture, so receiver binding refresh does not acquire sorting work. Unchanged captured nodes/arrays are immutable and reused; changed nodes are copied. `CardAppearanceCapture` keeps address/provenance candidate state separate from published state and validates all changed output. Session reset and disappearance remove cached owner bindings as before. Required deep node copies also avoid allocating a default value array which was immediately discarded by the copy initializer; their values remain independent.

Shader replacement on the same material invalidates support immediately. Scratch channels are cleared before every read, preventing an unsupported property from retaining an old spent/burn value. More than eight CanvasGroups, detached fallback-flight roots and original asynchronous artwork handling remain supported. All existing Capture APIs retain their semantics; no published array is reused as mutable scratch.

### Native send queue

Native send calls pass the immutable snapshot they just successfully encoded as optional local queue metadata. The existing scheduler already accepts that identity, so it no longer needs to decode another full graph merely to preserve clear/reopen and model boundaries. Byte-only callers retain the original decoding path. Receivers still decode and validate every message normally. Payloads are still copied before the writer buffer can change, and identity histories remain bounded.

No packet bytes, TLVs, cadence, scheduler weights, fragmentation/compression, resend, connection or gameplay behavior changed. Appearance, native board, native prompt, bonus animation and auxiliary use-bar send sites all pass their matching snapshot.

### Remote native playback

Exact comparisons read actual target output before avoiding redundant UI/material writes. This preserves tiny intermediate changes and repairs another writer's changes, even when the received snapshot object is unchanged. Shader-property support is cached per bounded card role; shader changes invalidate it. Native card enumeration uses a stack-based range, avoiding iterator allocation for every nested node lookup, including supplemental groups.

Original element playback owns only the material field of each affected clone graphic. The generic mirror continues to copy its other fields. This removes viewer-material/owner-material ping-pong. Claims are released on non-effect, missing/rejected frame and teardown paths. Replacing the original material or its shader recreates the correct clone-owned effect material. Original game materials are never modified.

### Board invalidation

Objective/rule, element, initiative and actor/furniture sections have independent content revisions and recovery clocks. Native board animation traffic no longer increments the broad actor presence revision. Every existing per-frame native scan and live animation pass remains active; source-root scans retain their existing sharing across observers. Owner extras and auxiliary use-bar changes still refresh actor/furniture immediately.

Native geometry/content changes refresh their affected section. Previously built but destroyed clones, new element generations and the first valid native frame after hidden playback receive targeted recovery. Pooled initiative rows additionally track Actor/Avatar identity even when their visible question-mark image is unchanged. Local element playback readiness retries the validated fit/show path immediately, without requiring another network frame; a failed fit keeps that retry pending. Creation/teardown reset all section state. Cached immutable text hashes, stable component/instance bindings and local sprite/texture reads reduce repeated native access without skipping source activity or hierarchy changes.

## Measured automated evidence

These are production-code harness results with controlled Unity API substitutes or pure .NET scheduling. They measure eliminated work, not headset FPS or complete Unity allocation.

- Card binding/capture: 18,206 assertions against a frozen build 492 capture oracle. Covers all float/color channels, shader/texture/low-material replacement, every group property, hierarchy reorder/reparent/rename, detached roots, invalid-hierarchy recovery and retained snapshots. Four negative controls actually fail: the old eight-group limit, mutable published scratch, stale shader support and restored warmed allocation.
- Warmed capture: 1,000 unchanged iterations with 12 graphics and 16 groups allocate 0 B in the harness, versus 7,640,000 B in the original capture oracle. Unity native name/readback allocations and the full game sampler are outside this measurement.
- Native playback: 466 assertions and three failing runtime controls cover exact actual-target writes, material ownership, shader changes, iterator allocation, supplemental groups, moving burn bounds and four independent board outputs.
- Board refresh: 1,216 production-gate assertions and three failing runtime controls cover independent sections, pooled row identities and local readiness recovery. Four simulated boards over 270 frames retain all 1,080 owner actor updates while global recovery runs only 44 times. This is a gate scheduling fixture, not a complete Unity board benchmark.
- Native send: four independent production schedulers emit identical bytes at identical turns across clear/reopen/retarget bursts. Payload-buffer mutation cannot corrupt queued output. A 32-card fixture with one native node per card allocates 4,883,200 B over 200 legacy enqueue calls versus 448,000 B with snapshot reuse (90.8% less in this step). Restoring production decode makes the allocation assertion fail. This excludes capture, serialization, rendering and compression work. A separate 200-node deep-copy fixture drops from 72,000 B to 40,000 B; restoring the discarded default array fails its allocation assertion. Earlier pre-copy-optimization enqueue baseline was 5,907,200 B for the same fixture.

## Hardware validation and limits

Full original widgets, face/privacy rules, native intermediate motion/effects and interaction features are retained. No lowered sampling cadence, simplified remote widgets, disabled effects, staggered visible construction or lower remote graphics settings were introduced.

New scopes `Net.Presentation.NativeSend`, `Net.Presentation.BonusSend`, `Net.Presentation.Transport`, `Net.Board.ContentRefresh`, `Net.Board.NativeRefresh` and `Net.Board.ActorRefresh` expose remaining costs. Build 492's narrow card scopes remain available. Broader instrumentation now accounts for work previously outside named mod scopes, so a change in the summed mod total alone is not a valid before/after result.

Compare same-build singleplayer and multiplayer with the same room state, camera pose and actions; then increase the actual connected peer/board population and repeat a long session. Inspect local and remote first-visible fronts, active/spent/burn overlays, short/long rest, hand/fan/flight transitions, native decisions, element animation, actor switching and reconnect. Automated four-state tests do not replace a four-board Unity/headset run. No numerical FPS gain or eliminated long-session leak is claimed before that evidence exists.

Build 493 is DLL-only after the full 483 installation. All multiplayer peers require 493. GVR1/v3, all TLVs and the asset bundle remain unchanged.

## Integration checks

All 17 guard checkers pass. Wire assertions increase from 251,797 to 253,055 (+1,258 send/copy
assertions); none of the existing vectors were removed. Production capture 18,206, playback 466
and board refresh 1,216 pass, together with 12 actual failing runtime negative controls. Strict
Release has 0 warnings / 0 errors; docs i18n and 16 reference assemblies pass. Config 625, patch
surface 152, registration 109 classes / 167 methods, log tokens 4,716 and bundle 74,943,763 bytes remain
unchanged. Compiled diff against baaf9871 contains 21 intended changed types and nine additions,
with no removals. Plugin, RemoteHandFan, VersionGuard and DesyncWatch change only because the
ModBuild constant is embedded; their behavior is otherwise untouched.
