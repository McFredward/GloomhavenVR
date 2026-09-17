# Animated map-window room making — build 519

## Request and evidence

The user reported an encounter opening behind the dialog. Moving only the new window
sideways had previously put it outside the view. On 2026-09-17 the user explicitly
requested necessary-only animated movement of existing visible windows to make room.
This authorizes opening-time overlap correction, not continuous head-following or recall.

The supplied main-checkout logs still identify local build 515 and remote build 500.
Local `.planning/debug/LogOutput.log:325` and `:1294` measure the encounter overlapping
the map story by 22 and 29 degrees. The encounter spans 48 degrees. At `:332` and
`:1301`, reveal reaches its 600 ms deadline without a first content fit. These are
historical evidence of the geometry and lifecycle, not a hardware test of build 519.

The existing local packer seats new windows while leaving their neighbors fixed. Shared
windows bypass that search and use deterministic identity-based homes. Neither route
jointly makes room by moving the existing story. Together the measured native windows
can exceed the horizontal view, so lateral translation alone cannot always separate them.

## Presentation and multiplayer contracts

Room making is an opening-time, finite animation. The solver considers visible geometry,
retains native content/scale and upright yaw-only facing, and never waits indefinitely for
first fit. The runtime scope is the three shared-map identities: story, encounter and quest,
also when playing offline. Private and corner-anchored windows stay fixed as obstacles;
a fixed incoming quest may move its blocking story. Unrelated clear windows stay where
they were. Native reveal deadlines, buttons,
story completion and encounter game actions do not depend on the layout succeeding.

The tween lasts 0.26 seconds. Horizontal bounds use the measured usable HMD cone with
35 degrees per side as the comfort cap; actual pitched-camera viewport bounds gate both
admission and destinations. Full host/hit footprints and noncentral pivots participate.
The maximum reading distance uses the author's physical rig scale, not the stable
parchment serialization scale. The supplied zoomed 515 geometry demonstrates why: the
same acceptable 1.73 m physical layout exceeded an incorrectly table-scaled distance cap.
At impossible layouts beyond 3.5 physical metres or behind fixed barriers, the ordinary
interactive window and grab bar remain. This is bounded packing, not a general curved
trajectory planner; existing overlaps resolve during the short transition.

A participating native host authors shared motion; with a flat/nonparticipating host,
the lowest fresh VR map participant does. Observers receive the same intermediate poses
through record 21. Shared appearance never independently follows each observer's head.
Manual local and remote grips take precedence, including a stationary remote grip.
Additive record 77 publishes held/automatic masks for story, quest and encounter;
explicit zero differs from absent metadata. Wire version remains 3.

The sticky, already completed story retains FINISHED while publishing its visible frame's
pose, including the final endpoint beyond the ordinary completion linger. No OPEN/page
resurrection is introduced. Automatic movement uses the existing bounded fast cadence;
its endpoint/ownership edge is sent immediately and the idle cadence resumes afterwards.
Cancelling on a peer grab baselines the unsent local tween remainder so it cannot echo
back as a fictitious new local drag.

## Validation

Focused checks pass: 1,247 production layout/pivot assertions, 15 source bindings and two
runtime negative controls; 74 shared-authority/finished-story assertions, six bindings and
four runtime negative controls. Golden wire coverage grows by 546 assertions for TLV 77,
including independent reader fixtures, explicit release and malformed/truncated records.
Final integration passes all 17 source checkers, every production harness and 254,565
wire assertions. The guard exits 1 solely because the compiled behavior changed, as intended.
Strict Release: zero warnings/errors. English/German docs, shell syntax and whitespace pass.
Patch inventory remains 130 classes / 197 methods; all registrations occur exactly once.
Surfaces: 625 configuration keys, 172 patch signatures, 4,730 log tokens (one new token:
`WINDOW ROOM MAKING`; none removed). Bundle unchanged at 74,942,975 bytes.

The retained build-502 snapshot compares as 84 changed / 53 added / zero removed. A private
comparison against the actual build-518 compiled snapshot has 14 changed / three added /
zero removed: five changes only propagate 518 to 519; one only propagates the four-byte
presence-buffer increase. The remainder is the reviewed layout, pose attribution, codec,
authority/cancellation and send-cadence work. Adding the explicit bridge constructor moves
RemoteMapStory's static initializers into its compiled constructor; the partial-order
checker confirms no cross-part initializer dependency. No baseline was overwritten.

Artifacts: `/tmp/gvr-519-guard.log`, `/tmp/gvr-519-release-final.log`,
`/tmp/gvr-519-incremental.diff` and `/tmp/gvr-519-layout-tests.log`.
A source/test result does not establish that both headset views are visually correct.
Hardware follow-up: open an encounter with the dialog centered, already clear, manually
held locally/remotely, and alongside the character/quest windows. Check intermediate
motion, readable endpoints, grab interruption and unchanged encounter continuation.
