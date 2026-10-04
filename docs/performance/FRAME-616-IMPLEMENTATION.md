# Build 616: original residents and animated walls

The supplied Frame capture is **Build615 / 64db88dc9**. The seven fully loaded
scenario windows improve from Build612's 53.20ms mean to **49.60ms**; this is
an observed 6.8% change across different natural VR sessions, not an isolated
per-setting saving or headset/compositor FPS. Loading and partial windows are
excluded. See [the complete hardware audit](FRAME-616-PERF-AUDIT.md).

## Original NPC geometry and Frame defaults

The immersive resident body-detail slider and all twelve NPC derivatives are
removed, including their dedicated twelve parts: **53,762,395 bytes**. NPCs use
their original meshes at every distance. The scenario's 1,594 figure derivatives
and 66 remaining parts are byte-identical. Optional per-renderer skinning quality
remains independent; it does not replace an NPC mesh.

Fresh or missing-key standalone profiles default to
`WorldUI/ImmersiveTownServices=false` in
`BepInEx/config/dev.gloomhavenvr.worldui.cfg`. PC profiles default true. An explicit
saved preference remains intact, so an existing Frame profile with true must
disable **Immersive NPC interactions** once under **Environment**. The old
`Optimize/TownNpcDetailPercent` key remains inert and hidden in both curated and
advanced options. Original 2D-map and non-NPC service windows retain their paths.
See [native geometry and packaging evidence](FRAME-616-NPC-ORIGINALS.md).

This is a full installation rather than a DLL-only drop: the indexed part set
shrinks. An archive overlay may leave obsolete files on disk, but the new index
and NPC runtime never consult the removed parts. Native town/voice/main bundles
are unchanged; no new mesh or voice generation occurs.

## Wall presentation

The pop is a defect, not an intended performance compromise. An authored floor
material with a live native wall-fade gate was admitted to the simpler shader;
its retained properties advertised a dissolve that the substitute did not render.
The log positively includes that cheap shader in Wall25's fade set. Structural
chunks were zero, so active chunk replacement is not the demonstrated cause.

Live native fade channels veto simpler shading on floors as well as structural
surfaces. Later native material/property changes restore original materials before
drawing. A separate wall-only visual timestep bound prevents a long application
frame from jumping straight to the fade endpoint. Shared UI/card fade math and
native gameplay timing are unchanged. See [source and rendered causal controls](FRAME-616-WALLS.md)
for the original shader boundary and hardware limits.

The attached environment driver also used `OnPreCull(Camera)` and
`OnPostRender(Camera)` as explicit event handler names. Unity mistook these for
automatic MonoBehaviour messages with invalid parameter signatures. Renamed
handlers retain their explicit camera subscriptions and stop those startup script
errors. This is not evidence that they caused the long frames.

## Complete incremental diagnostics

The capture contains no completed SCENE/SIM/GFX inventory: display refresh changes
repeatedly cancel its slow sliced traversal. Refresh-only changes now retain the
same job while still closing the old FRAME measurement window before adopting
the new rate. Scene/load/graphics/configuration and Debug lifecycle invalidation
remain. Existing bounded Debug progress also reports work units, visited nodes,
slices, CPU cost and elapsed capture span. Normal logging gains no per-frame trace.

Actual Unity runtime tests exercise the production refresh/mark methods and native
hierarchy across repeated 72/24Hz boundaries. Three effective controls independently
prove job retention, true graphics invalidation and pacing-window closure. This
restores missing evidence; it is not a claim that the unidentified hitches are fixed.

## Remaining hardware work

The loaded wall counters skip 24/24 atomic publications and health bars skip
18.5% of bone checks. The scene already uses authored Animator culling; the new
idle option causes zero additional culling here. All 17 structural candidates are
unreadable, so no chunks are applied. These distinctions prevent attributing the
observed improvement to inactive settings.

Cold figure pickup/ghost and stat-panel preview bursts are specifically measured.
Other frames around one second, and an unfinished extreme tail, contain little
named mod work. CPU engine work, driver waits and OS scheduling remain unresolved;
the logs supply no application GPU busy measurement. A next capture should verify
native wall appearance and use the repaired completed inventories to identify the
remaining work. Automated checks do not certify headset smoothness or visual quality.

Integration receipts, immutable Build615 compiled baseline and original hardware
inputs live under `.planning/debug/frame616-review/`. The final validation counts
are recorded in `.planning/STATE.md` after the integrated gate completes.
