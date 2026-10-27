# Issue 24: initiative original, dock and input ownership

## Evidence and limits

[Issue 24](https://github.com/McFredward/GloomhavenVR/issues/24) reports a very
small/distant initiative row, or a row just behind its board whose portraits no
longer switch character boards. The attached 3840x2160 image shows a normal-sized
board and a much smaller initiative strip above it. It does not identify a mod
build or the transform which moved. No issue-specific paired logs accompany it.
The current maintainer logs are not attributed to this screenshot.

Two production defects are reproduced with real Unity 2021.3.5f1 geometry and the
actual initiative/WorldSurface lifetime coordinator:

1. `WorldSurface.Tick` retains any live native target. It does not compare a new
   `FindTarget()` against that retained target. Initiative rendering/fit ownership
   could therefore remain on a previous original while row animations and the
   depth picker read the current `InitiativeTrack.Instance`. The same stale
   reference survives a framework-retired host because `ConvertedPanel.IsAlive`
   tests **only the native target**, not the conversion host. The latter state is
   reproduced by the actual native-target/host lifetime contract. A previous
   native object need not be destroyed when a presentation conversion is retired.
2. `TryIntersectRect` normalizes the cross product of the two **physical** edges.
   Unity returns a zero normalized vector below 1e-5 magnitude. At board scale
   0.04 a valid 150x130-pixel portrait has edges about 2.5x2.17 mm and area normal
   about 5.42e-6, so the depth pick always rejects it. Normalizing the independent
   non-degenerate edges first makes the normal scale-independent. Physical world
   corners, scale, plane orientation and front-side-only permission stay identical.

These reproductions establish defects and repair their entire input/lifetime
classes. They do **not** prove which transition caused the user's screenshot.
Native `InitiativeTrack.Awake`/`OnDestroy` writes its singleton, but the ordinary
`SceneController` transition awaits scene unload before the next additive load.
Do not claim that every ordinary scene load overlaps two initiative singletons.
Passive `RemoteWidgetMirror` clones are built inactive and their gameplay
components are removed before activation; they are not assumed to overwrite the
singleton. No blanket root flattening or native animation cancellation was added.

## Repair

`InitiativeTrackSurface` binds the exact native component during conversion.
Before ordinary or late placement it checks that the native singleton, target,
live host and ancestor ownership still agree. A valid nested content frame under
the host is accepted; no direct-parent assumption forces repeated conversion.

An invalid binding releases the old ownership and adopts the current original in
the same tick. Every exit uses one `ReleasePanel` handoff: old reorder/pointer
state, authored depth, popup treatment and mip originals are restored against the
**old component**, and its host picker is unregistered before a new one is bound.
If the framework already retired the conversion, its old native-home snapshot is
not replayed over newer native layout. Releasing an already dead original is safe.
The ordinary log records only actual binding transitions with old/current IDs.

The portrait picker refuses to resolve a different singleton through an older
host. Its only geometry change is scale-independent plane-normal calculation.
No board pose, zoom, card state, native button callback, native interaction
permission, shared tuning or wire record changes. Remote mirrors continue to use
the current local native original, their existing per-owner tuning and callbacks;
flat/unmodded gameplay is untouched.

## Validation

Run locally:

```sh
python3 scripts/check-initiative-binding-runtime.py
```

The real Unity harness compiles the complete production `WorldSurface` and
`SlotPanelSurface`/`TrayMountedPanelSurface`, plus exact production initiative
Tick, LateTick, OnConverted, ReleasePanel, ownership and portrait-pick methods.
`CanvasConversion` adoption/release and unrelated animation/mip consumers are
explicit modeled boundaries and spies. It tests retained live/hidden native
roots, current-original replacement in Update and LateUpdate, a null singleton
gap, an externally retired host, newer native layout, a valid nested frame,
actual native destruction, board/mount rebuild, scalar and nonuniform/rotated
zoom, VR-off/re-enable and shutdown. Actual uGUI `Button.OnPointerClick` receives
the selected original, while non-interactable buttons, back faces, outside rays
and degenerate portraits remain refused. Original child animation is untouched.

Seven compiling runtime mutations must fail at their specific assertion:
retaining a live old target, omitting the late handoff, restoring the new mip
source, retaining the old picker, the original area-normal calculation, treating
a retired host as live, and restoring an already released native snapshot twice.
The old/new normal cases also export a numeric witness proving identical world
corners/poses and a different actual hit result. All generated project caches are
cleaned through `cleanup_completed_run` after the Unity process is reaped; source,
logs, manifests, results and numeric captures remain.

This suite requires the installed Unity editor and is local-only. Strict real and
CI-style metadata compilation separately validate production references. This is
focused regression evidence, not a new complete gameplay-gate pass or a headset
acceptance. Paired four-player hardware still needs confirmation of the formerly
intermittent report; the new binding transition log identifies a future occurrence.
