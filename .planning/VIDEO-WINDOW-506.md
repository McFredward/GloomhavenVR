# Native movie window lifecycle — build 506

## Evidence

The new local LogOutput.log (line 18) and Player.log (line 41) identify ModBuild 505,
assembly 1.0.1.0. The remote logs still identify build 500 and are not evidence for
this retest. No screenshot accompanied the movie-window report.

The user confirms the movie is visible but sinks into the table and has no grab bar.
LogOutput.log:147 records its opening. Lines 148, 151, 155, 159 and 164 repeatedly
report a *first* grab-bar transition to hidden for `Native video`, followed by empty
ink measurements. That repeated initialization is consistent with rebuilt holders.
The reported ~130.76 world-unit bar width is not 130 physical metres: this map rig
uses scaled game coordinates, as the other windows' placement logs also show.

Two source-proven defects explain the behavior:

- `ModalFallback.SweepOrphanChrome` runs every five seconds. Build 505 registered
  the video handle in `GrabbableModal.LiveHolders` but never added its ownership to
  the sweep, which recognized only `ModalFallback.Converted`. The sweep destroys
  the handle. The next `GrabbableModal.Tick` recreates a frame at the world origin
  through `EnsureFrame`; it does not rerun the initial `Build` placement. The
  surviving movie host then follows that origin. The orphan warning is Debug-level
  (`VRLog.Warn`), so its absence from this Info-level log does not falsify this path.
- `PanelInkBounds` treats a full-frame Graphic as a backdrop and excludes it from
  the content union. The movie's sole RawImage fills the entire frame, so even a
  stable movie has no accepted ink to keep the handle visible. The log's zero plate
  count during the churn does not independently prove that classification; the
  production walker regression does.

The native VoiceChat shutdown NullReferenceException in Player.log:3536 is unrelated
and occurs during teardown.

## Changes

The live video window now claims its exact handle in the ordinary orphan sweep.
An unrelated handle with the same name still gets removed. Video chrome and canvas
share a persistent lifetime, and module teardown closes both before the sweep.

`ConvertedPanel.ContentGraphic` identifies the exact full-frame movie content. The
ink walker still applies its visibility, alpha, clipping and transient-content gates;
neighboring background plates retain their previous classification. Videos use the
normal modal draw tier, the existing grab/resize controls and the existing shared
pose path. Opaque scene depth still behaves like other windows; this fix does not
force videos to render through hands or other foreground objects.

## Validation

The production movie owner and actual orphan-sweep method are tested together,
including repeated sweeps, ordinary modal ownership, a same-name orphan, remote
ownership, content binding, modal ordering and persistent-lifetime enrollment.
The production ink walker tests full-frame content, other backdrop exclusion,
visibility, clipping and repeated measurements. Deliberately reintroduced defects
must fail both suites. Final full-gate counts are recorded in STATE.md.

Headset confirmation is still required: replay the same savegame for longer than
30 seconds, grab with laser and hand, resize, release, skip/finish, and repeat with
a second player. Also replay a movie spanning a scene transition; the desktop
harness verifies persistence enrollment, not Unity's scene-unload renderer behavior.

Version remains 1.0.1; ModBuild 506 is DLL-only relative to the build 483 bundle.
All VR peers need build 506. No main push, release or tag change is part of this fix.
