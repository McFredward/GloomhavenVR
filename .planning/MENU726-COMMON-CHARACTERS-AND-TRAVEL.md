# Build726 common character units, rear views and travel sound

## Hardware review and inputs

The maintainer accepts the direction of Build725 but requests consistent character
alignment and a common scale, with intrinsically shorter bodies remaining shorter.
Some previews were smaller/right-shifted while others were larger/centered. The
back of a physical window must show the actual character's back with tracked 3D
perspective. Independently, road-event continuation sometimes produced overlapping
travel sound until arrival. The delivery remains the normal Debug installer ZIP.

Both fresh local logs identify725 /6ee56ff48, built2026-10-27 12:30:47 UTC. This is
the same source commit as the earlier delivered725, with a different compile time.
Remote717 logs are stale. No new screenshot or audio recording was supplied. Inputs
are byte-pinned under ignored debug/menu726/hardware725, not modified in place.
The new log records sparse New Party display fallbacks naming the rejected
front-facing portal at lines5777 onward. This directly supports the rear-eye
admission defect; log rows alone do not establish the count of playing AudioObjects.

## Source causes and implementation boundaries

Build725 fits each completed actor/equipment/companion envelope independently to
82% width/75% height, places its bounds center on the aperture and compresses depth
to a per-envelope limit. A weapon or companion therefore changes the main actor's
scale/center, while short/tall bodies lose their intrinsic size relationship.
Use a single native-unit metric and the authored main actor's ground/axis instead.
Native model/camera/holder transforms, original animation, skins, companions and
authored child scale remain original. No first head pose or animation phase may
establish the reference frame, and no recurring renderer census may return.

The native audit covers34 forms,21 configs and12 exact native camera-to-RT-to-image
apertures. Manager roots have zero local position/unit scale; native child scale
still varies intentionally. Imported skinned bounds and equipment extrema are not
ground corrections or per-body fit rules. Actual mesh coordinates, authored TRS,
GUID/path identities and source hashes remain reproducible read-only evidence.

Rear cabinet eyes need a reflected off-axis view through the same fixed pane UVs,
with finally-restored culling state. Native-screen eye mapping must also admit
the rear half-space. Preserve the725 stable direct-target priming, sample budget,
hysteresis, capture ownership and all original native effects/restoration. The
accepted story/video resources and native input authority remain unchanged.

Native MapTimedMovementFlow starts StartMoving followed by a scheduled Moving item
on the encounter leg. Its continuation starts Moving again while the first loop
remains alive. MapPointsMovementFlow has the same producer asymmetry. The serialized
moving item is a loop with unlimited instances and no stop events; the encounter
item has no stop event either. The repair owns concrete native AudioObjects for
the exact flow's configured moving item, not all sounds sharing an ID. Before the
next native leg it retires that previous request and lets every original statement,
callback and new native layer execute. Pending starts and pool reuse must be
qualified against the actual ClockStone lifecycle. Flat calls stay native.

## Integration and verification plan

Three disjoint worktrees start at currentdev6ee56ff48: character math/native-scene
projection, native travel-audio ownership, and a read-only native authoring audit.
The primary owns registration, ModBuild726, manifests/inventories, final review,
dev integration/push and normal archive delivery. Focused affected checks and full
golden/source/build checks will reuse the passing725 evidence for unchanged areas;
they must not be described as a new complete local-suite invocation. Source/GPU/PCM
proof and new headset/audible outcomes remain separate. Final results follow the
integrated checkpoints.

## Integrated implementation and review

CharacterSource no longer reconstructs renderer/bind envelopes. It caches the
completed Character3D.models[0] placement root in the original camera frame, with
bounded identity storage and no renderer census. Projection uses one 2.4-unit span
and the existing common aperture proportions; XYZ share a uniform metric. Native
main roots, poses, original body scales, equipment and companion offsets stay
untouched. This aligns the ground/axis, rather than normalizing every body to the
same height or centering the combined equipment envelope. The 2.4 span avoids
clipping the tall base bodies under the original square/wide apertures. Imported
Mech rest-arm width is not a reason to shrink every character globally; imported
rest bounds do not establish an animated silhouette.

Rear capture reverses forward while retaining the fixed pane's UV right/up axes.
It borrows GL.invertCulling by XOR with the prior state and restores it in finally,
including failure. Camera/holder/model transforms never move with the eye. The
desktop screen mapper admits signed distances on either side; exactly coplanar
and invalid eyes retain the original fallback. Generic projection, display,
shaders, accepted media, lifecycle discovery and adaptive sample ownership do not
change.

The audio repair scopes the six original timed/point travel producers and records
actual PlayAudioSubItem results. Each native flow owns only its concrete moving
request, including layered and DSP-scheduled objects. Resume cancels that old
request with zero fade before native playback continues. Receipts qualify object,
subitem, timestamp, item and a weak per-object playback generation. ClockStone's
system time advances once per Update: a real pooled replay can reuse exactly the
same timestamp/subitem/ID in one frame, so timestamp matching alone was rejected.
The existing native play-result observer invalidates already tracked receipts on
unscoped reuse without global scans or entries for unrelated objects. Destruction
clears pending owned requests even after MapRoom stands down. Flat travel, native
arrival/destruction, native exceptions and all gameplay callbacks remain original.

The fresh logs identify encounter leg1, event and resumed leg2 at local lines7957,
7981,8092,8113 and Player16546,16593,16915,16953. They do not enumerate sources.
The actual native/Unity fixture reproduces two simultaneous moving AudioObjects
with advancing PCM in its original-defect control, and one after repair while
encounter/completion sounds remain. Synthetic probe clips and supplied movement/
bootstrap/arrival endpoints isolate the native audio lifecycle; full gameplay,
network choreography and physical headset sound are not emulated.

## Final focused verification scope

Integrated runtime is frozen at 3cb9a2a95; later 26d051871 updates only the fixture
oracles for the explicitly requested common scale/rear behavior. Root validation:

- 17 affected local suites pass: story-completion, map-story-lifecycle, map-flow,
  desktop-render-runtime, stereo-movie-render, shared-window-geometry-runtime,
  shared-window-pose-runtime, shared-reward-placement-runtime, menu-depth-media,
  menu-depth-data, menu-depth-character, menu-depth-capture, menu-depth-desktop,
  menu-depth-graphic, menu-depth-material, menu-depth-performance and
  travel-audio-runtime. Receipts: debug/menu726/{affected,character-desktop}.
- Character 1500 assertions +31 causal controls; Desktop255 +11; native audio36
  +10. Common bodies use independently supplied native short/tall dimensions,
  actual final GPU pixels, front/rear surfaces, opposite oblique eyes and64mm IPD.
  Rear failure also proves culling/texture restoration. Performance863 +10 keeps
  event-driven discovery and first-picture priming. Fixtures use Unity2021.3.5,
  llvmpipe/OpenGLCore, explicit XR eyes and pass-through native effects, not the
  maintainer's headset/D3D11/Beautify rendering.
- All 16 source suites, all306431 unfiltered wire/golden assertions, strict real
  Debug/Release and private RefAsm compiles pass; each compile has0 warnings/errors.
  Five bilingual document pairs pass. Suite inventory has250 local entries; its
  19 unit tests pass with2 expected skips. There is no new complete250-suite run:
  unchanged coverage is inherited from725's qualified249-suite evidence.
- Original-asset audit and exact metadata comparison pass for34 forms/21 configs/
 243 skinned renderers/12 apertures/65 source pins. The falsified-root-scale
  control is rejected. This audit is read-only and retains scalar metadata only.
- Compiled Release review against the exact delivered725 Release DLL finds three
  changed menu behavior types, WorldUI's audio registration and two new audio
  helper types. Nine other files differ only by inlined ModBuild725-to726. No
  top-level types disappear; the obsolete nested SkinBinding is deliberately
  removed with bound fitting. Normalize only recorded commit/time identities.
  An initial current decompile omitted the game reference path and was invalid;
  it is retained separately and replaced by equal-reference-path comparison.
- Source surface retains all669 config keys,248 literal Harmony entries (+4) and
 4839 log tokens (+1), with no removals. Generated inventory is218 patch classes /
 318 methods, all registered once; new patch rationale is in PATCH-NOTES.md.

Every completed fixture stops/reaps its owned processes before the shared cleanup
API removes project caches. Logs, results, actual executed assemblies, native
inputs, source pins, captures and .keep protections remain. All three workers'
evidence is copied and byte-verified under debug/menu726/workers before their
worktrees are removed. Private ADB isolates these Editors from the unrelated
default Android-device server. Final Debug archive identity, byte/media checks
and clean dev delivery are recorded under debug/menu726/delivery. No headset
acceptance is inferred from these automated passes.
