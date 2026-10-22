# Shared multiplayer windows and latejoin selection, Build717

## Requested behavior and evidence

The maintainer's 2026-10-22 four-player review requires one shared spatial window,
identical absolute world geometry despite independent viewer zoom, host-only
initial placement and convergent browsing dismissal. Existing synchronized manual
move/resize and original gameplay/continuation authority remain. A flat native host
has no VR head; one compatible VR presenter supplies placement in that combination.

All four supplied clients ran release Build715. Main hardware inputs remain in
`.planning/debug/`; the other three are in `remote/{1,2,3}`. Original logs and the
42m20.6s recording under `trailer/gameplay/` are preserved. Local watch extraction
used twelve overview frames, six map/story-focused frames and four later-session
frames, without Whisper,
network uploads or audio transcription. Sparse samples show the map quest window
and subsequent encounter/story windows at the far side of the table. They do not
establish every player's simultaneous picture or precise network delay.

The paired logs and source expose three distinct failures:

* The old design-frame/content-fit repair normalized pixels correctly, but
  GrabbableModal captured and repeatedly applied each viewer's zoom. A comparable
  Story window had actual widths approximately14.06,8.32 and26.92 world units.
  Its old audit divided by the same local zoom, producing apparently equal
  millimetres. Equal normalized dimensions did not prove equal world objects.
* Legacy scenario poses were divided by the sender's seat/zoom, then reconstructed
  with the receiver's different seat/zoom. Matching legacy bytes were insufficient.
  The parchment frame itself is common in this evidence; fallback seats were not.
* Main LogOutput19033 publishes selection-clear key0/stamp1. Remote3 LogOutput8725
  consumes it while native readiness still masks map interaction. Remote3 had
  already readied (Player74384). The ordinary selection-change observer can unready
  only after selection changes, but the mask prevents that change: circular waiting.
  Native Deselect can also refuse, so clearing mod staging before observing its
  outcome could hide only one local copy.

The fourth participant is remote1/player4. Its ownership was already correct while
its native current card presenter remained Berserker rather than its newly assigned
Elementalist. Only a later native figure click switched the presenter and enabled
placement. See `MP717-LATEJOIN.md` for exact source/log evidence and callback limits.
No per-line wall-clock timing is invented from these logs.

## Implementation

Shared rendered scale comes from common parchment/scenario base geometry, excluding
saved and live pinch zoom. The native Hex width is read once and cached independently
of menu rigs and scene teardown, including an exact read-only pre-initialization
resource fallback. Private windows retain their captured local scale.
One host gaze supplies the fitted spawn. Followers retain hidden, noninteractive
frames until they adopt the current shared pose; reveal waits do not alter native
continuation. Initial receipt adopts the movement baseline and native identity too,
so it cannot echo as a new human drag. A manual shared endpoint outranks repeated
host initialization, including for latejoiners. Legacy averaged-seat reseating no
longer moves these standing host-placed windows.

Additive TLV119 carries source originXYZ/yaw/scale and a six-kind initial-role mask
atomically with unchanged legacy19/21/72/73 records. The receiver reconstructs the
author's actual game-world pose before election/interpolation. Existing legacy
record grammar/coordinate meaning, wire version3 and native controls are preserved.
Missing/invalid context cannot become a competing legacy pose. Scenario/map story
openings retain their existing native opening ledgers. Quest/encounter first reveal
also rejects cached poses from before the exact new local grab lifetime and accepts
the next ordinary author snapshot; this is a cache lifetime fence, not a new native
encounter identity protocol.

Quest browsing uses a subject/surface/native-generation/input-revision-qualified
pending delivery. It invokes the original quest-unready seam once and waits for
the actual native acknowledgement to release its own mask. It never unlocks unrelated
masks or invents cancellation of a committed host proposal. Incidental table/X input
therefore retains the native committed window and its original ready/decline controls.

Additive TLV120 pairs each author's stable browsing event with its full-width native
sender identity, nonwrapping counter and exact parent opening. The opening-based
register converges under reordered selections, clears and reopens, including missing
intermediate snapshots and same-content reuse. Explicit read-only selected-state
relays preserve the original counter/full author after its source leaves, without
minting an adopted click. Remote adoption and native quest
retirement do not mint fake local clicks. Counter exhaustion fails closed. The final
maximum extras sum is7747; allocation8004 retains the required257-byte spare record,
while unchanged8000-byte transport reassembly has253 bytes of actual headroom.

Video playback source election is separate from physical spawn authority. Rewards
retain explicit capability/decline and continuation handshakes; compatible session
membership remains stable while a scene load temporarily removes its avatar. These
paths must retain original movie/end/reward callbacks and manual movement priority.

## Validation and remaining hardware checks

Integrated validation is in progress. Focused evidence currently includes actual
Unity geometry writers, production transport/first-pose kernels, byte-exact119/120
vectors, causal mutation controls, native quest-ready/departure/close behavior and
the latejoin native SwitchHand queue. A fixture's explicit native/tracking/renderer
boundaries remain limitations. Full-gate and final integration receipts will be
recorded after completion; no headset acceptance is implied by green automated tests.

Required hardware review: four players at different zooms/seats; host opens each
shared kind; late native copies inherit the same endpoint; manual move/resize is
shared; browsing clear/reselect and a committed proposal remain consistent; player4
can place owned selection cards immediately after its normal native hand switch.
