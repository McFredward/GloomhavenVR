# Build764 character-creation presentation repair

Base: `fb03d1e64` / Build763. The integrator owns the class-C final union and
dev push. This lane changes only the permanent character window's fixed-fit
presentation and adds its exact original-layout/coordinator regression.

## Hardware and original authoring

All three supplied JPGs were inspected. `115027` shows the party rail and creator
but an incomplete right-side figure/aperture; `115042` places the creator over
almost all of the left rail. `115035` is the cancelled creator, with a conspicuous
gap between party rail and mercenary list. Both paired logs identify Build763,
commit `fb03d1e64`; the local log is the screenshot author's SteamVR run.

The local fixed-fit readings state the mechanism directly:

- line 45513: the campaign selector is seated at 1.000, natural width 1627 px.
- line 46061: the same frozen root now needs 2308 px, overlaps the rail by 303 px,
  and spills 357 px beyond the physical frame.
- line 46780: re-seating that changing union trades the overlap for 637 px spill.
- line 47163: another 23 px overlap develops without an open-set identity change.
- line 47488: cancellation returns the measured width to 1627 px.

The creator is nested under the still-shown campaign selector. The collector
correctly avoids placing both ancestor and descendant, but the resulting frozen
solution only remembers the ancestor identity. Opening/cancelling creation
therefore changed its contents without changing the fit's presentation epoch.

Read-only original `level9` inspection identifies root 7924, party rail 11014,
selector 8177, creator 10672 and preview 11189 / RawImage 13202. Native show animator
14088 gives the preview MOVE OriginalValue 600 / ToValue 600 with zero duration;
the roster's -300 to -20 values are SIZE_DELTA insets, not positions. The 0.1 s
roster tween completes before AdventureCharacterCreator.OnShown builds/shows
the creator. The original RawImage already references RenderTexture
`Character 3D assembly wide render texture` (external 2 / path 31), UV 0..1; loading
fills that original target, rather than assigning a late presentation rectangle.
The helper nevertheless measures original RawImage apertures independently of
their current texture or artwork readiness.

Original `UICharacterCreatorWindow.IsShown` remains true through Hide's fade via
`isHidding`, which is cleared at transition completion. The new frame therefore
outlives all painted closing frames. The native Name step intentionally hides
the party rail; this is retained. The user's always-visible left rail request
is applied to class/mercenary selection and its cancellation endpoint, without
inventing a fake rail or changing native Hide/Show/refcounts/callbacks.

## Bounded repair

`CharacterCreationFrame` captures the native creator rectangle and original
preview apertures once per nested opening. This is not a current ink, mesh,
head-position or asynchronous texture measurement. The actual fixed-fit
collector tracks that opening independently of its ancestor, uses its complete
frame for seating, and releases the old solution on cancellation. Only the
transparent host frame gains enough native-pixel room for its complete right
edge. The target retains its authored 1920x1080 frame and all actors keep their
native origin and existing projection metric.

The existing level-up frame-extension protection also protects this extension:
an accepted world pose is not replaced, and the user's world resize is untouched.
Native transitions remain authored transitions. Capture quality, raster dimensions, projection, normal capture cadence, gameplay,
original assets and networking remain unchanged. The first-frame handback repair
described below adds two bounded coordinator methods to this lane.

Exact production scope: new `Conversion/CharacterCreationFrame.cs`, and the
character-specific FixedFitState/ApplyFixedFitCore/CollectActiveSubViews/
MeasureFixedFitParts/SolveSubViewPlacement in `CanvasConversion.3.Fit.cs`.
Generic measurement, clipping, typography, native model source/projection and
level-up branches retain their original bodies. The only shared coordinator
change is the source-empty handback below; the final union therefore uses class C.

## Verification boundary and retained failures

The desktop regression imports 1,475 original hierarchy rectangles and 86 original
sprites from the party/selector/creator authoring. Native lifecycle/page states,
animation endpoints and a cyan mesh witness are explicit inputs. Exact production
fixed-fit kernels and the complete existing converted-window/head coordinator
execute in actual Unity with final camera pixels. The original IsShown getter
also executes; no native game/save/character callback runs.

Original material fragment programs and dynamic population are not reconstructed
by this layout fixture. White blur panels, template text and the cyan body are
declared fixture witnesses, not a game-ready visual acceptance render. Native
font data feeds a common original SDF font; this does not claim all original
font/material pixels or populated campaign content are identical. Actual XR,
Windows D3D11 and multiplayer hardware remain unverified.

Setup failures are retained: the source binder first duplicated extracted fields,
then lacked its explicit Alert sink. A 101-assertion early pass did not assert
cancel pixels; manual PNG review found a black cancel capture. That proof is
insufficient and is not the final acceptance result. Stronger native page/fade/
late-texture/reopen and explicit cancellation-pixel checks supersede it. All
standalone runs use canonical completion cleanup after process reaping.

## First-frame cancellation handback

The stronger final-camera check exposed a real additional coordinator defect.
The native mono RT already contained the current party rail/list, while the
first head frame was completely black. The head route disables its own private
photograph Canvas while rendering the stereo carrier. Releasing that carrier
and re-enabling the photograph inside head pre-cull is too late for uGUI's
same-frame batching. A following ordinary frame looked correct, which is why a
test with an extra yield would have concealed the defect. The normal service
had no earlier source-empty retirement; this was not a missing native mono
bootstrap in the fixture. Initial ordinary capture and the actual geometry
and visibility kernels are now explicit parts of the regression.

An experiment with RawImage.SetAllDirty plus Canvas.ForceUpdateCanvases inside
carrier disposal still failed the first-frame pixel assertion. That experiment
was removed. The actual repair adds RetireEmptyMenuDepth to ServiceEntry,
between SyncGeometry and SyncVisibility. WorldUIModule first updates the source
registry, then calls CanvasConversion.LateTick and this service. The private
Canvas is restored before normal batching; SyncVisibility resumes the ordinary
mono capture and the existing disposal releases both head RTs and reservations.

If a native source disappears after that LateTick, the already-established
carrier renders the CURRENT original plain UI for that handoff frame. It never
reuses the prior creator picture. The next ordinary LateTick releases it. An
ordinary window without an established pair never acquires one. This changes
only the source-empty handback branch of CaptureMenuDepth, plus the exact new
helper/caller above. No head material, projection, filtering, sampling density,
allocation budget or permanent two-eye policy changes.

## Final owned verification

Final focused run: `.planning/debug/menu-depth-desktop/run-hsgdwu5_`:

- `head-creation-layout764`: **191 assertions passed**.
- Five causal controls rejected the intended defective behavior: original
  nested-opening collector, missing transparent host extension, current-ink
  envelope, omitted early handback, and original pre-cull late-loss release.
- Actual geometry -> retirement -> SyncVisibility caller statements are
  extracted from production ServiceEntry; production SyncVisibility and the
  complete existing `.6` head coordinator execute unchanged in the fixture.
- Two opening epochs cover initial delayed preview texture, authored roster
  animation, class/name/personal-quest stages, original Hide fade and cancellation.
  Both head eyes assert original party-rail art and roster-grid art inside their
  physical rectangles. All creator/preview and opposite quest bounds fit.
- The source-empty transition asserts the very first cancellation picture,
  then post-LateTick source loss with automatic mono camera disabled and enabled.
  Its first handoff picture has current UI and zero cyan character pixels;
  the next service has no retained head pair. A following foreign camera retains
  its target/projection and the global eye stack is balanced.
- Accepted host pose, original model origin and user resize are unchanged.
  The unchanged dedicated character-rotation/actual XR/multiplayer consumers
  belong to the integrator's final complete gate and hardware review.

This is **six focused cases, not a complete container/gate**. The integrator
explicitly owns the complete `menu-depth-desktop` container on the final union,
including the combat-log-specific frame classifier. No complete local gate or
full native-UI container was redundantly launched by this worker.

Unity: 2021.3.5f1, OpenGLCore / Mesa llvmpipe. This run took 53.34 seconds including
generation/compilation/cleanup, with 26.75 seconds inside the six rendered cases.
All 633 pinned inputs remained unchanged; retained executed fixture DLL hashes
bind the actual assemblies. The runner reaped its editor/compiler and performed
canonical cache cleanup; no owned process remained. Slot 2 used private ADB 5083.

Final strict Release build passed **0 warnings / 0 errors**, elapsed 9.63 seconds:
`.planning/debug/mp764-creation/validation-mp764-creation-final-build.log`.
Plugin SHA256: `87de0ab17e13b932e9e0ad733a9834305c406da880f5e7dcc638e0fb08d398af`;
preloader SHA256: `36dbcbe11d0341cdc7f9ac4159126be22533cdb4ff821107b487f8e1fd00c058`.
No DEBUG-only/preprocessor behavior was introduced. The integrator performs the
all-source and class-C complete final checks after merging all workers.

The retained prior runs include initial missing-source binder compile failures,
the insufficient 101-assertion pass, strengthened first-frame failures, the
failed dirty/ForceUpdate experiment, and the intermediate 175-assertion pass.
Final left/right class, both quest choices, closing-fade, cancellation and
handoff PNGs were manually inspected. White panels/template text and cyan body
remain declared fixture ports; these are not fabricated in-game screenshots or
a claim of HMD acceptance.

Before worker retirement, complete evidence and all failures are archived with
per-file hashes under the main checkout's
`.planning/validation/build764/creation-lane/`. The archive verification receipt
is the authority for retained members; the source checkout's generated caches
can then be removed without erasing captures, manifests, pins or logs.
