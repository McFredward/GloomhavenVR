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

## Delivery and verification boundary

This is a focused source/presentation repair, not a new complete local gate.
Unchanged693 classifier/helper/cache/Proton/SDK/native checks are inherited.
Native receiver layout, guard, installation and Frame service remain unchanged.
The independent unfinished stereo catalogue remains local; publish only this
bounded repair on public693, then merge into shared dev preserving that work.
The complete stereo handover advances to695 or later.

Focused receipts and cleanup records live under `.planning/debug/steamlink-694*`.
The final headset contours and uninterrupted loading must still be assessed in
hardware; source-linked and actual Unity raster controls are not a headset image.
