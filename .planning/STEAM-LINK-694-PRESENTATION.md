# Steam Link MR loading continuity and window coverage — Build694

## Evidence and scope

The maintainer confirms693 now enables native Steam Link passthrough. The new
reported defects are soft window contours and intermittent green output while
loading. The files presently in the main hardware debug folder are still691;
they do not independently establish the successful693 image or its loading
sequence. The three earlier passthrough screenshots are historical visual input,
not new693 hardware captures.

## Loading cause and repair

SteamLinkConnectionLifecycle previously invalidated a positively identified HMD
when VRCameraPolicy.AllowedHead changed and passed camera activity as session
eligibility. Loading can replace, disable or temporarily remove that camera
without ending the active SteamVR/OpenXR session. This discarded Ready and
selected the ordinary saved green chromakey while querying the same HMD again.

Admission now follows the running eligible XR session and actual HMD events.
Start its asynchronous lookup before a scene camera exists; retain a proven
identity across camera changes. Device connect/disconnect/configuration changes
still invalidate, including removal after a device loses its characteristics.
Runtime/session/platform/standalone changes still cancel/reset, and old-epoch
reader results cannot reauthorize a new HMD. No persisted identity is trusted.

While an actual identity check is pending, preserve the ordinary image instead
of showing a green key or publishing an unadmitted carrier. The scene-output
owner also recreates resources after a normal OnDisable closure; genuine
nonempty preparation/render failures keep their existing failed-head refusal.

## Window edges: measured improvement, report still open

The actual Unity2021.3.5 UI/Default uses premultiplied RGB and correct alpha
blending over the fully opaque MR plate. The historical PanelSupersample blend
comment was not proof of a defect. Do not ship an unsupported material rewrite.
The coverage atlas instead stores1024² opacity against2048² scene RGB and adds
spatial interpolation before video/native filtering. Replace its additional
linear interpolation weights with a continuous bounded C1 kernel. This never
thresholds opacity itself and adds no texture reads, RTs, cameras or CPU pixels.
The rebuilt Windows bank is173185 bytes, SHA256
`a3f34fab8a68947970e8a8852b5811a9f899cf657e9ae2d395a346561c33019d`.

The actual-bank Vulkan comparison passes571 checks in20 cases. Six UI/opaque
plate edges average3.282→3.101 source pixels at10–90% (5.52% less). Constant glass
opacities are unchanged; gradient/fades differ by at most1/255;500 RGB probes
remain byte-identical. Actual materialisation remains monotonic and reaches zero.
Clipping/fade delta adds126 passing checks. The old linear bank fails the exact
edge assertion after valid compilation/raster, rather than a generic crash.

This is explicitly NOT a complete fix for the reported soft window edges.
Nearest/hybrid and two clamped unsharp candidates were measured under32 moving
MSAA poses; they narrowed some edges further but increased silhouette movement.
Unsharp also tripled alpha reads. None is production code. The fixture retains
these offline comparisons and their reports; no further filter experiments are
needed until the visible symptom is classified.

The maintainer has been asked whether the complaint is a narrow blurred outer
contour or a broad transparent strip inside the window. A broad strip cannot
be assigned to this few-pixel reconstruction footprint without new evidence.
New693 hardware logs are not currently present. A separate author-window mask
or higher-resolution receiver reconstruction would be a larger follow-up, not
something established by this limited filter change. Keep this item open.

## Actual stock UI effect footprint repair

A further source defect is now reproduced in real Unity: Image/RawImage
DrawingRect alone excludes the stock Shadow/Outline-modified vertices. Their
visible pixels may extend beyond the opaque backing's8px margin and remain
partially transparent. The production measurement now includes their actual
stock translations in component order with native byte-alpha rules, including
zero-alpha intermediate geometry that a later modifier can reveal. Merge equal
alpha pieces into256 bounded bins; never populate or rebuild a native game mesh.
New types are explicitly qualified as UnityEngine.UI.Shadow/Outline to avoid a
different game/test binding. TMP/custom graphics/effects retain their old paths.

The no-effect drawing rectangle, inherited/native hide gates, image aspect and
sprite padding, external capture and ancestor clipping retain their existing
contracts. There is no new sampling cadence, watcher, RT or camera, and a fresh
measurement starts from current artwork rather than an accumulating union.

Actual Unity stock ModifyMesh and rendered pixels independently reproduce this
defect:16 geometry cases/15,200 visible pixels,4,032 outside the previous backing
in the large-shadow cases. Corrected geometry includes them. A narrow aspect and
byte-zero delta passes25 assertions/four cases; the old drawing-rectangle path
fails the exact active-shadow xMax comparison after six successful checks.
The existing PanelInk suite passes499 assertions plus30 runtime and one placement
mutation. All owned processes/caches are reaped/cleaned; full and limited results
remain under `.planning/debug/steamlink-window-shadow/`.

This repairs a real window-opacity source path; it does not prove that the
specific693 Story window had this modifier. Final headset appearance remains a
hardware boundary, particularly the mask's residual few-pixel filtering.

## Focused checks

- Source-linked lifecycle plus actual asynchronous Monitor:111 assertions. The
  old camera-dependent lifecycle compiles, then fails the exact loading-regression
  assertion. Unity/XR/session/reader inputs are explicitly controlled ports.
- Production MixedReality.Tick and camera restoration in actual Unity2021.3.5:
  42 assertions, including pending initial identity and actual revalidation with
  no green fallback. The old pending-key path fails its semantic counterexample.
- Complete SceneHeadOutput/SteamLinkSceneRuntime with actual coverage, packing,
  material callbacks and shipping shader bank:436 assertions,11 real renders,
  five resumes and96 guard cells. Real GameObject.OnDisable, OnPreCull and
  OnRenderImage execute. The old owner fails specifically at same-camera resume;
  actual HDR/unknown-eye faults remain refused. XR metadata and host ownership
  are explicit ports, not an actual XR session. This lifecycle evidence used the
  unchanged prior bank; the updated coverage bank has its separate raster proof.
- Focused source instrumentation:1/1 passes. Do not repeat the unchanged full
  gate merely because the bounded tests were registered with the suite runner.

## Delivery and verification boundary

Final composed694 strict Release build passes0 warnings/0 errors. The affected
source instrumentation gate passes again after the UI-effect addition (1/1).
The existing scene-output Gamma/Vulkan edge-witness suite passes against the
new shipping bank and updated independent mask reference. The final compiled
plugin and source/bank hashes are preserved in
`.planning/debug/steamlink-694-stability/final/`; helper source SHA256 is
`3f1b701a802bf0d6575b85693a38f39ba1bb4a4e8ee7c04b43f518574a898808`.
No unchanged whole suite was repeated.

This is a focused source/presentation repair, not a new complete local gate.
Unchanged693 classifier/helper/cache/Proton/SDK/native checks are inherited.
Native receiver layout, guard, installation and Frame service remain unchanged.
The independent unfinished stereo catalogue remains local; publish only this
bounded repair on public693, then merge into shared dev preserving that work.
The complete stereo handover advances to695 or later.

Focused receipts and cleanup records live under `.planning/debug/steamlink-694*`.
The final headset contours and uninterrupted loading must still be assessed in
hardware; source-linked and actual Unity raster controls are not a headset image.
