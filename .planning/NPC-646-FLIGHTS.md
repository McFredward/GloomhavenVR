# NPC646 native return-clock review

## Hardware and source scope

Both frozen paired `LogOutput.log` files name Build645 at line17. Frozen receipts
under the main checkout's `.planning/debug/npc646/inputs` bind the current logs
to dev `2d77ffb65`. No new flight movie or picture was supplied; older October4
NPC pictures do not measure the new run's return timing. The logs do not contain
per-render flight positions or a causal artwork/motion packet chronology.

The enchantress return uses the original `VRCard` fly-in curve: unscaled elapsed
time, native smootherstep, locked rotation, parabolic arc and original relative
face/body geometry. `TownCardReturnMotion` already captures those endpoints and
evaluates that curve on every observer render. Merchant item returns use the
same evaluator with their original exponential or collapse curves. No new
flight easing or viewer-local flight phase is needed or authorized.

## Reproduced cause and repair

`ApplyRemoteMotion` removes an independent kind8 return clock when its source
sample predates newer `LastFrame` artwork; kind9 offered registration already
survives that check. The per-render return also requires an admitted kind1 root,
which the same newer artwork can filter. Artwork arriving between sparse motion
events hand the picture back to generic sampled root interpolation during the
actual short flight. The exact old production source reproduces this at the
first caption refresh: frame10 deviates by **0.03254788 m**, after frames1–9
follow the native curve. This is a source-proven defect, not an inferred exact
packet chronology from the hardware logs.

The bounded repair is confined to `TownServiceMirror.Motion.cs`:

- A validated kind8 clock remains eligible across newer artwork only during
  its existing duration plus 0.25-second completion margin. Matching peer/lane,
  session, service, module census and native structure remain required.
- The current visible original can consume that clock without an unchanged
  compact root surviving the newer artwork timestamp. A present newer root must
  still have the same hand affinity; regrab and actual removal take precedence.

The existing `TownCardReturnMotion` renderer is reused. Source endpoints, elapsed
time, easing, arc, child offsets, local fan behavior and protocol are unchanged.
There is no local flight-phase exception or second approximation of the tween.

Existing prepared-return checks verify only four renders between packets, with
no newer caption/artwork during those samples. The new focused fixture compiles
the actual `VRCard.TryTownReturnMotion`, source fly-update curve, capture, codecs,
receiver and final return writer. Its source sampler and active source fly-update
body are extracted from the current `VRCard.cs`; only a small flight initializer,
the unscaled engine-clock boundary and external card construction are fixtures.
Native gameplay completion callbacks are outside this picture test and are not
claimed to run. It introduces newer actual caption
frames between sparse return events and compares every render against the exact
published curve, including original child offsets and a moving approved rig
holder comparison. The merchant comparison supplies an explicit exponential
return receipt to the existing evaluator; it is a rig-holder/metadata replay
test, not a claim to run the entire merchant gameplay outcome controller.
Existing real-clock merchant/cabinet/prepared-return coverage is run separately.
Old-source and terminal-removal controls are retained.

## Focused evidence

Worker-local receipts retain source hashes, exact compiled inputs, CSV curves,
failures and results:

- `npc-return646-old/run-wjflr1e0`: unchanged old production fails at the first
  newer native caption frame with 32.55 mm curve error. A preceding setup run
  failed the unrelated common liveness fixture after the deterministic time
  binding; that setup failure is retained and not attributed to hardware.
- `npc-return646-final/run-hetxc1jj`: ability route **500 assertions** and
  moving approved holder route **324 assertions** pass, including all54 renders,
  three newer artwork arrivals, the exact endpoint, fresh opposite-hand takeover
  and immediate actual terminal removal. Measured maximum positional error is
  **0 m** in both deterministic routes; four encoded clocks are admitted. The
  exact Build645 `TownServiceMirror.Motion.cs` control still fails at frame10.
- `npc-return646-existing/run-pzoofsev`: the existing real-clock `card-return`
  production subset passes **480 assertions**, retaining merchant, cabinet,
  prepared ability and service-switch returns. Unchanged other suites are not
  repeated and this is not a new complete-gate pass.

The bounded receipt's production `TownServiceMirror.Motion.cs` SHA256 is
`7246db80bc901352c08a6f2b46ad9d9496cb5c2345437bf09e8e4b80d298cbcc`,
independently matched to the reviewed source. The old control compiles the exact
base file from `2d77ffb65` against the same fixture and clock boundary; it does not
corrupt a packet or remove art to manufacture a failure.

At every newer caption arrival the fixture checks that the actual artwork's
source sample is later than the retained published flight clock, and verifies the
unchanged compact root is absent from the eligible motion set. Thus the positive
route exercises the missing-root case rather than masking it with a fresh root.
For ability returns the replay is additionally compared against the actual
source smootherstep/arc, both original face/body world positions, locked child
orientation and face dimensions, rather than only a second call to the observer
evaluator. Rig-holder motion uses the same continuous transform
boundary as approved scenario/merchant holding; the actual tracker adapter is
covered by its inherited atomic-hand tests.

No extra packet, property format, send rate, artwork work or per-frame log was
introduced. Network transit jitter and exact hardware per-render output remain
outside this deterministic local proof. This accepts the reproduced cause and
bounded repair without claiming an HMD smoothness or WAN-latency guarantee.
