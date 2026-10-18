# Build704 — direct MR comparison and source admission

## Current hardware evidence

The supplied PC run is Build703, clean source d86736508, SteamVR/OpenXR2.17.10,
NVIDIA RTX4090/D3D11. The maintainer directly compares sharp MR Off with soft MR
On, and reports roughly one second of the previous blurred edge appearance
before a short flash and the corrected edges. This is a headset observation,
not something inferred from nominal video settings.

Actual XR diagnostics now show4040->5050->6060 and back, full viewports and
matching per-eye render-pass dimensions. The Build703 live-allocation repair
therefore changes real targets in this run. Existing Frame logs were fetched
read-only; no device write, setting change, restart or capture occurred.

The actual decoded MR video remains3264x6400 even while requested widths rise
to5056 and6080. Off restores the ordinary foveated1152x4608 format. Those two
formats must not be compared as if they were simple ordinary per-eye widths:
foveated reconstruction concentrates samples in the view centre. The v3 source
additionally maps RGB into only2048/3168 of each carrier axis. The confirmed A/B
and dimensions locate the remaining loss in our MR transport rather than a
nonworking resolution slider. Another larger width request is not a full fix.

The retained private PC driver has a concrete proportional format-limit path:
layout2 caps the complete format at3200x6400, layout1 at6400x6400 and the other
layout at3200x12800. This explains the observed format quantitatively, including
a possible coded/buffer alignment margin. It is NOT a new measurement of a
universal decoder maximum: that retained PC binary was collected11October and
has no exact version receipt proving it is the current user's PC file. Private
snippets and exact SHA pins remain ignored, never redistributed with the mod.

## Bounded source correction

MixedReality.Tick previously launched acquisition and source preparation in
parallel. No authority related the source to the current settings lease. The
source can therefore emit an atlas under the previous encoder policy. Existing
log order is consistent with this missing boundary but does not time actual
native encoder changes; the worker's informational message is outside its lock.

Require the actual current host lease's epoch, matching normalized extent,
uncompleted stop task and uncompleted helper task. Check the same authority at
cold preparation, render ownership and the final actual-source extent. Off,
resize, Reset and helper completion revoke it immediately. Pending/refused
acquisition retains the ordinary scene and can recover without resetting the
player's preference. Explicit edge-quality opt-out remains functional.

No sleep, fixed warm-up delay, off-mode policy prewarming, focus suspension,
camera allocation, FOV, shader, atlas profile or receiver change is introduced.
The host-ready receipt does not prove that native video reconfiguration has
finished. Do not claim the whole one-second visible transition is solved.

## Native menu review

The first current episode09:25:17 is confounded: Frame tracking enters IMU
fallback at09:25:16.836, the menu opens with reason=remote, and PC XR temporarily
becomes Synchronized. Tracking relocalizes at09:25:19.685. The clean local
controller toggle is09:25:25.127 through09:25:32.650, with Focused->Visible and
back, and no decoder-format change. Do not attribute tracking loss to every menu.

The logs do not establish whether a different native pipeline bypasses decoding,
current header validation fails, or another opaque local background hides valid
coverage. The known layered-ND vertex shader cannot simply be allowlisted: its
locations/types and q-to-R1 coordinate convention differ. The user still requires
game + local menu + camera together; hiding the game is not that result.

The smallest proposed observation is six bounded numeric records for one
closed/open/closed transition and both original eye selectors. The old whole
image probe exceeds its64MiB budget at the current3264x6400 and must not be reused
by merely raising the limit. No such new probe was installed in this round.

## Full-quality path remains open

Preserve ordinary RGB/foveation and transport genuine coverage without spending
that RGB raster on an entire opacity plane. A separate mask requires exact
current RGB-frame/session/eye AND pose/warp association; a timestamp or latest
mask is insufficient. Fine interleaved tiles are not a proven shortcut: the PC
compositor can reproject the carrier before native decoding, moving tile borders.

Public OpenVR projection overlays support an eye-specific frustum and alpha.
Whether Steam Link carries their RGBA independently or flattens them into the
same opaque video is not established by those API declarations. A bounded
explicitly started transport prototype is being prepared independently; it is
not a production renderer or an automatically enabled feature. Official API:
https://github.com/ValveSoftware/openvr/blob/master/headers/openvr.h

## Focused validation and limits

- Complete production host monitor/protocol:114 assertions, strict net472,
  three compiled causal controls for pending, wrong width and finished helper.
- Complete production scene callbacks:138 assertions, strict real Unity/net472
  typechecking, three controls for initial admission, pre-cull revocation and
  late source-size changes. Runtime Unity/XR boundaries are explicit in this test.
- Real Unity presentation:47 assertions on extracted verbatim MixedReality.Tick
  and camera save/restore, including actual ordinary RGBA while pending and live
  recovery with preferences retained. Four affected semantic controls were
  rerun, including the pending-host control. They fail the intended assertion.
- Later cleanup-only Python runner changes were AST/diff checked; earlier
  unchanged tests are inherited, not reported as a new complete gate. Final
  integrated source/build/resource receipts remain in the scoped evidence folder.

Evidence lives under `.planning/debug/steamlink-704-review/`: pinned hardware,
read-only Frame logs, `quality-lifecycle/`, independent architecture and native
reviews. All current image-quality, full first-frame edge behavior and native
dashboard outcomes remain hardware-unverified. No new headset test is requested
for a purported full-quality repair in this source-only correction.

## Dashboard diagnostic attachment on2026-10-18

The isolated diagnostic was explicitly authorized and actually loaded on the
Frame. Native SteamVR2.18.2 startup succeeded. Its cold queue-profile admission
refused before recording (`ready:false`, reason3, zero submissions/draws); no
stream action or menu comparison followed, and no sparse record/image capture
was taken. This is diagnostic failure evidence, not a dashboard repair result.
The remaining rejected predicate was not recorded: queue flags, queue extension
chain or missing observed graphics capability. In particular, family0 is the
old unassigned field default on this branch. Do not treat it as a measured
family, or earlier count1/family0 receipts as proof of null extension chains.

The maintainer approved manual rollback, all original public activation hashes
and links were restored, the public receiver was mapped without the diagnostic,
and the maintainer confirmed a normal visible/usable Frame surface. A transient
owned user runtime mask serialized the swap against the Frame's competing
restart; it was removed before starting, with no timer or system-unit change.
No GloomhavenVR production source, renderer, PC sender or public receiver binary
changed in this attachment. Native source review found the isolated probe's
blanket queue-extension rejection diverged from the earlier successful typed
global-priority scheduling contract; that is a correction target, not yet the
proved cause of this device's refusal.

Exact private receipts: `native/dashboard704-20261018-083233-950bad/` beside the
existing Build704 evidence, including `manual-rollback-result.json`,
`rollback-verification.json` and `completion-cleanup.json`. All small metadata is
saved locally. Under1MiB of inactive Frame staging is retained because six
same-user maps were inaccessible, preventing a complete unmapped-library proof;
no diagnostic manifest, drop-in or runtime mask remains active.
