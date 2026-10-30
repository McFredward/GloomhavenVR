# Build724 spatial UI hardware repair

## Hardware report and scope

On 2026-10-27 the maintainer reported two failures in Build723: a door
illustration remained flat, and live character previews translated with the
viewer's lateral head movement. The requested character presentation is a fixed
scene inside the window, viewed through glass: head movement changes perspective,
never the model's pose relative to the window.

Both supplied local logs identify ModBuild723, commit `5ddc94991`, built
2026-10-27 07:36:04 UTC. The remote log still identifies Build717 and cannot
certify this round. No new screenshot was supplied. Byte-pinned local inputs and
the exact Build723 source/assemblies are retained under ignored
`debug/menu-depth724/`.

The logs show four character source registrations, the complete 448-identity /
515-field catalogue loading, and bounded `MENU DEPTH DIRECT CAPTURE`
InvalidOperationException reports. They do not identify the pictured door by
themselves or demonstrate visible relief. A later loadout illustration is logged
as `CR_DF_03_Demon (VR-mip)`. Inspecting that exact native asset confirms a
stone door with candles, consistent with the report despite its asset name.

## Proven source gaps

- The original serialized GUI3D camera owns the character holder as its child.
  Build723 moves that camera transform while rendering each tracked eye, which
  also moves its native model and light hierarchy. The earlier character fixture
  used a separate holder root and therefore missed the real hierarchy.
- Illustration matching compares the displayed sprite name literally with the
  original asset name. The existing mip baker creates named replacements and
  maintains a read-only reverse map to the originals. Those replacements fail
  Build723's identity match. Event default comparisons and async old/new sprite
  retention need the same original identity. Reconstructed trimmed sprites also
  need their presentation UVs mapped back to the original texture coordinates.
- Direct-capture failures currently log the exception type without the message
  or source, preventing a reliable attribution from the supplied log alone.

## Implemented repair

Character eye rendering now changes only the original camera's view/projection
and culling matrices. Its original transform, child model, lights and native
animation pose remain fixed during rendering. Actual GPU eye-position uniforms
still follow the supplied eye matrix. Each window keeps its own view of that
same native scene; manual window movement/resizing carries the scene with it.

Illustration lookup resolves the actual `CardFaceMipBake.OriginalFor` reverse
map instead of interpreting a display-name suffix. Native/mip replacement during
async story transitions retains the visible original's own depth. Event defaults
and compendium GUID lookups use the same original provenance. Reconstructed region
copies receive an explicit displayed-UV to original-texture depth transform using
the producer's rounded row-copy coordinates. Native colour UVs, materials, input,
layout and dissolve animation stay original; prepared media files are unchanged.
Debug binding notes stop after 32 state changes and stable missing identities
receive at most 4 normal-level reports after a one-second observation window.

Direct head rendering now admits visible world-space consumers only; native
screen-space consumers retain the mapped original UI-camera path. Private capture
layer acquisition/release updates the direct exclusion mask immediately. Hidden
pooled consumers do not borrow the native model scene. Failure reports retain
their existing bounded schedule and add phase, source and exception message;
stack details remain guarded at Debug. The exact cause of the old type-only
exception entries remains unassigned.

## Focused evidence

- Character: 757 assertions and 15 compiling causal controls in one coherent
  Playmode invocation. The new proof independently reconstructs fixed 3D landmarks
  from actual eye rasters across full/cropped, rotated/scaled and native-screen
  consumers.180 eye/landmark samples have maximum error 0.715px for full viewports
  and 0.850px for native LDR viewports against the unchanged 1.05px bound.
  The independent reconstructed-depth bound remains 45mm. Native child/light/
  animation state and eye uniforms are observed during actual image-effect draws.
- Capture: 56 assertions and 14 controls. Final physical-glass rendering adds 171
  assertions and 8 controls: original camera child scene, offset/cropped RawImage,
  two-camera native UI stack, moved/scaled glass and perspective head raster.
  Three camera-effect profiles each cover seven poses by two eyes. Maximum
  fixed-world landmark error 0.519px against an unchanged 3.5px limit. Pass-through
  image effects actually run once per prepared eye; this proves callback and
  mapping retention, not native Beautify/Bloom pixel parity.
- Story: the final integrated 17-case proof passes 582 assertions and 14 controls,
  including whole-atlas/GUID assertions. The actual fractional-trim native
  door sprite, complete unchanged mip producer and its real SHA/CRC-checked depth
  field execute together. Correct rendering has 11819 changed parallax pixels;
  disabling eye projection gives zero despite the same nonflat mesh. Atlas
  original-versus-mip alignment is 0/12800 mismatched pixels; dropping the depth
  UV transform produces 2844 mismatches. Native dissolve and async/default cases
  remain covered.

These are focused repairs. Existing Build723 complete/scoped evidence is inherited
only for unchanged areas; no new complete local-gate invocation is claimed.
Integrated final checks and the ordinary Debug archive identity are recorded below
when completed. Headset appearance, comfort, performance and actual multiplayer
parity remain hardware outcomes, separate from these explicit-eye Unity/GL proofs.

## Native viewport and test-run boundaries

An initially plausible full-RenderTexture viewport override was rejected by actual
native-profile testing. Unity2021.3.5 GL with HDR enabled ignored stored rects in
the fixture, while native LDR cameras retained them. All exact GUI3D source cameras
and six audited story/event UI-camera references have HDR disabled. Both candidate
projection overrides were retracted before integration; the shipped projection
files remain byte-identical to723. No HDR heuristic or effect-specific mapping
policy ships. Failed assumptions, clipped/underresolved diagnostic marker attempts
and their corrections remain preserved alongside passing results.

Initial editor attempts stalled before the test update because a shared Android
device property query did not return. A task-private ADB socket restored startup
in 4.64s without changing the default server or device connection. Those hung
attempts establish no GPU outcome. Unity also escaped its launcher process group;
the exact-project pidfd editor/descendant reaper now precedes compiler and cache
cleanup. Its real-process control passes 9 assertions; the earlier compiler
teardown control remains 11. Premature old cleanup attempts and subsequent exact
process-stop/corrective receipts are retained honestly.

Main ignored evidence: `debug/menu-depth724/`, including byte-pinned723 hardware
inputs and assemblies, verified story/capture worker handoffs, native camera
census, and editor startup intervention records.

## Integrated checks and delivery

The integrated focused invocation passes all 14 selected local suites in 227.6s:
all seven menu-depth suites, original desktop/movie rendering, native story
continuation/lifecycle and shared window geometry/pose/reward placement. All 16
source checks and all 306431 unfiltered golden assertions pass. Strict real-reference
Debug and Release builds each report zero warnings and zero errors. The exact-project
editor/compiler process controls also pass independently in the integration checkout.

Compiled comparison against the retained actual723 DLL finds six intended behavior
changes: catalogue, character source, typed image driver, graphic mesh, source
registry and panel supersampling. Other changed types contain only the724 build
constant or assembly commit stamp; no production type is added or removed. The
first comparison accidentally included adjacent XML docs; that instrument failure
is retained, and the comparable DLL-only snapshots establish this final scope.
Configuration, patch registration, native camera projection files, renderer bundle,
prepared fields and movies remain unchanged.

The normal Debug delivery uses `scripts/package-release.sh` and the existing
`dist/GloomhavenVR-1.2.2.zip` destination. Final clean-commit DLL/resource/asset checks
and archive identity are retained in ignored `debug/menu-depth724/` delivery receipts.
Completed worker worktrees were removed only after independent full archive/member
byte verification; source/assembly/capture evidence, hardware inputs, pins, local
worker branches and main-checkout deliverables remain available.
