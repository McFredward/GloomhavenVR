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

- line45513: the campaign selector is seated at 1.000, natural width1627px.
- line46061: the same frozen root now needs2308px, overlaps the rail by303px,
  and spills357px beyond the physical frame.
- line46780: re-seating that changing union trades the overlap for637px spill.
- line47163: another23px overlap develops without an open-set identity change.
- line47488: cancellation returns the measured width to1627px.

The creator is nested under the still-shown campaign selector. The collector
correctly avoids placing both ancestor and descendant, but the resulting frozen
solution only remembers the ancestor identity. Opening/cancelling creation
therefore changed its contents without changing the fit's presentation epoch.

Read-only original `level9` inspection identifies root7924, party rail11014,
selector8177, creator10672 and preview11189/RawImage13202. Native show animator
14088 gives the preview MOVE OriginalValue600/ToValue600 with zero duration;
the roster's -300 to -20 values are SIZE_DELTA insets, not positions. The0.1s
roster tween completes before AdventureCharacterCreator.OnShown builds/shows
the creator. The original RawImage already references RenderTexture
`Character 3D assembly wide render texture` (external2/path31), UV0..1; loading
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
edge. The target retains its authored1920x1080 frame and all actors keep their
native origin and existing projection metric.

The existing level-up frame-extension protection also protects this extension:
an accepted world pose is not replaced, and the user's world resize is untouched.
Native transitions remain authored transitions. No camera/raster/RT quality,
capture cadence, gameplay, original asset or networking code is changed.

Exact production scope: new `Conversion/CharacterCreationFrame.cs`, and the
character-specific FixedFitState/ApplyFixedFitCore/CollectActiveSubViews/
MeasureFixedFitParts/SolveSubViewPlacement in `CanvasConversion.3.Fit.cs`.
Generic measurement, clipping, typography, native model source/projection,
level-up branches and unrelated window policies retain their original bodies.

## Verification boundary and retained failures

The desktop regression imports1,475 original hierarchy rectangles and86 original
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
then lacked its explicit Alert sink. A101-assertion early pass did not assert
cancel pixels; manual PNG review found a black cancel capture. That proof is
insufficient and is not the final acceptance result. Stronger native page/fade/
late-texture/reopen and explicit cancellation-pixel checks supersede it. All
standalone runs use canonical completion cleanup after process reaping.

Final targeted proof, causal controls, strict build, source bindings and archive
hashes are recorded below once complete. The integrator runs the complete
containing desktop suite once on the final union; this lane never claims that
its focused selection is the complete gate.
