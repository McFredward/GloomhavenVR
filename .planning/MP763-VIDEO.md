# Build763 shared native/review movie pose repair

## Scope and evidence

Class B: a video-specific readiness guard. The shared wire, opening/gesture
arbitration, placement authority, native progression, decoder election and generic
frame writers are unchanged. Reuse the qualified complete 292-ID Build762 proof
(`4536289ed`, `.planning/validation/build762`) for unchanged production closures.
The integrator owns final union/source checks, build number and push.

Both supplied hardware runs identify Build762 / `4536289ed` / version 1.2.4. The
main log starts the deliberate review at lines 13995/13998, creates its shared physical
window at line 14005 and reports a real local Video pose stamp 1 at line 14440. Scenario ESC,
objectives, elements and initiative coexist at lines 14003–14004, so this happened in a
scenario, not a 3D-map-only context. The remote starts the matching review at line 556,
creates its window at line 558 and reveals at line 561; both selected
`Review/CP_Intro/GH_CP_Intro.sbs-v2.mov`. It closes at line 583. Absence of a pose Info
line alone does not establish the cause; the source/consumer replay below does.

## Cause

`RemoteVideoPlayback.Resolve` calls `EnsureMirror` before `ResolveVideoPose` on
every observer frame. `EnsureMirror` unconditionally called
`RemoteMapStory.TryVideoSharedInitialPose`. This initializer also adopts the
shared resolver's baseline, following-peer/stamp and interpolation track. It
therefore recorded the latest incoming move as already applied on every mirror
tick. `NativeVideoWindow.SetRemotePose` only stores a legacy pending pose for a
shared movie; it does not move its actual grab frame. The immediately following
generic resolver then matched that newly written baseline and skipped its actual
`GrabbableModal.PlaceFrameAt` write. Initial reveal/content worked while subsequent
physical movement and resizing remained at the old pose.

Publication and receiving are already scene independent: stream 72 carries the
movie and physical pose, 119 the source frame, 121 the captured render reference,
and 123 the existing gesture order. The actual `SharedWindowMoves.Observe` precedes
the video receiver. No replacement transport or map-only eligibility workaround
is needed.

## Repair

Only call the initializer while the current physical movie panel is absent or
unrevealed (`!NativeVideoWindow.SharedInitialPoseReady`). Once revealed, the
existing shared resolver exclusively owns subsequent peer movement and resizing.
This is not a decoder-created one-shot: late first metadata still gets repeated
opportunities to reveal. `NativeVideoWindow.Tick` replaces a changed selected
source, and `PrepareSharedPlacement` gives that new physical panel its own reveal
lease. The complete coordinator retires/recreates the cosmetic decoder when its
author or token changes, including same-clip replays.

Deliberate movie actions remain under Advanced > Cheats and the default-off
Cheats gate, including stale-click rechecks. Native authored movies remain
independent. No movie key, clock, audio, click or native continuation changed.

## Why the previous proof missed it

The portable pose fixture previously ran the receiver's exact pose-forwarding
tail and separately exercised the initializer and resolver. It never executed
the full `Resolve -> EnsureMirror -> initial-reader -> ResolveVideoPose` order on
each observer frame. Its decoder/content harness replaced `RemoteMapStory` with
an explicit no-op port, so neither existing proof crossed this boundary. The new
portable scenario runs the complete production playback coordinator plus actual
72/119/121/123 codecs, receipt, initial adoption and generic pose consumer.

The Unity companion additionally runs the original
`NativeVideoWindow.TryRevealSharedPlacement`, actual readiness property,
`GrabbableModal.PlaceFrameAt/Tick/LateSyncHost` and original host/scale writers
against real Unity transforms, Canvas and camera pixels. Native window discovery,
roster, the cosmetic decoder and grab chrome remain explicit ports. The decoder
port supplies test ink; this does not claim native decoding or headset acceptance.
The repeating visual ease helper pins to the sampled real pose in this fixture;
the original generic shared-pose interpolation executes unchanged.

Both proofs cover native/review movies, scenario/map, host/client movers,
initially missing opening metadata, resize, release endpoint, independent observer
zoom and a replacement generation. A compiling mutation that removes just the
new guard must fail the physical-frame/pixel movement assertion. Existing
shared-window movement controls remain present.

## Validation record

Final counts, durations, archived member hashes and cleanup are recorded in the
worker's final evidence manifest under
`.planning/validation/build763/video-lane`. The strict worker build uses the real
game references and returns 0 warnings/errors. No local release archive or device
modification is part of this work.

Fixture setup failures are retained, not counted as passes: the new ready port
initially needed an explicit false initializer for strict CS0649; the Unity port
needed its real Unity imports and disabled documentation generation; a synthetic
reopen first incorrectly allocated a new Opening 1 publisher instead of retaining
its actual Opening 2 counter. A deliberately cold register schedule required the
next fresh complete metadata heartbeat after local identity discovery, as in the
actual periodic receive path. The final proof asserts pending-before-discovery
and successful later reveal rather than bypassing that boundary.


Fresh worker validation at the unchanged Build762 constant:

| Scope | Result |
| --- | --- |
| Complete shared-window-pose-runtime | Production 5,142 assertions; all 51 compiling negative controls rejected, 52 cases total |
| Complete video-playback | Production 83 assertions; all 17 original compiling controls rejected |
| Registered video-window-pose-runtime (`--unity-video`) | Unity 2021.3.5f1, 148 assertions; original-reader mutation rejected by final pixel movement; 32 production PNG views |
| Strict real-reference Release | 0 warnings, 0 errors; build reported 11.03 s |
| Native-video, unnecessarily broad invocation | Interrupted, exit 130: native runtime 488 assertions passed; packaging only partly executed; no containing-scope pass claimed |

The Unity observations in every schedule move the green decoded-ink centroid
from x=255.5 to 324.0 pixels and resize its area from 2,640 to 5,201 pixels. This
is physical coordinate/picture evidence, not the original game's movie artwork
or a headset visual acceptance claim. The production and control captures have
separate assembly-named folders to prevent a failed mutant overwriting production
pictures.

The native-video descriptor, its entire tests directory and every production
source imported by that descriptor are byte-identical to the qualified Build762
inputs. It does not import the changed coordinator or the changed
VideoPlaybackTests stub. Its full native/movie-packaging coverage therefore
inherits the separately audited existing qualification rather than repeating
unchanged installer work. The unnecessary live invocation was stopped with
SIGINT, its PowerShell child group killed/reaped by the existing finally handler,
and its canonical completion cleanup retained all partial/failure evidence.
The independent reviewer confirmed this consumer closure. The inherited
qualification itself identifies the native scope as audited prior exact scope
(via the Build762 aggregate), not a fresh worker full-suite result.
