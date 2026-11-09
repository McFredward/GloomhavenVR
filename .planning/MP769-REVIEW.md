# Results follow-up review: Build769

The final production delta from Build768 changes one cancellation condition in
`ExtrasSendQueue.EnqueueResults`, adds bounded DTO-only progress receipts in the
existing publisher/receiver/painter, and increments the compatibility build.
Private decompilation confirms three existing behavior types, one new internal
receipt type, unchanged32embedded resources and no removed types. The remaining
differences are inspected build/commit literals. No capture, shader, codec,
native rule or continuation implementation changed.

## Findings and decisions

1. **Indefinite incomplete full pictures: fixed.** Every newer full revision
   formerly cancelled the active same-opening full, even when the older picture
   was merely halfway through fragmentation. Exact Unity sampler output plus
   the real publisher/transport/receiver reproduces a permanent completed
   heading. Retain the active visible full, then service the latest waiting full
   and its dependent cumulative delta. Default tests include the old condition
   as a compiling negative control; the complete picture suite additionally
   passes its own actual captured samples through the production transport.
2. **New opening/author and close: preserved.** They supersede the old in-flight
   picture immediately. Waiting fulls coalesce to one, or two with the exact
   latest delta dependency. Duplicate fulls cannot erase that newer delta.
   Finishing an older full cannot rewind final receiver state. Explicit tests
   cover these boundaries, bounded memory and eventual drainage.
3. **Other multiplayer streams: unchanged policy.** No event-size/rate, native
   reliable-delivery flag, scheduler weight, finite reserve, sequence namespace,
   compression or wire bytes changed. The production mutation is confined to
   the Results-only enqueue method. Complete integration qualification remains
   required because this is the shared queue implementation.
4. **Original appearance and clipping: no speculative rewrite.** Native guest
   inactive panes, Graphic flags, CanvasGroup alpha, cull/clip state, dynamic
   source Canvas and heading-to-statistics frame growth are exercised by the
   real original sampler/codec/inert consumer. These tests do not establish a
   new rendering cause. The previous asset-catalogue repair is retained.
5. **Actual camera path: now covered explicitly.** The complete picture suite
   must also execute the built mod's real conversion/reveal, supersample
   allocator, layer walk, resolve/display and final head rendering over real
   Unity frames. It is no longer enough to port those coordinators and compare
   a raw UI camera. A stale-resolve mutation proves that the final head check
   can reject a header-only display. Read `scripts/results-head-runtime/README.md`
   for the substantial native bootstrap/content/shader/audio ports and visible
   limitations of those diagnostic images; pixel equality does not certify
   correct native final-game layout or headset appearance.
6. **Animation and sounds: original lifecycle retained.** Header/statistics
   controller callbacks still run on each peer. Native Victory/Defeat and
   readiness sounds are not suppressed or replayed by this change. Inactive
   observer construction strips duplicate audio/animation controllers before
   activation. Intermediate alpha/position samples remain shared, not filtered
   to a final static table. Tests inspect source component identity/enabled
   state and inert copies, but do not measure audible timing between headsets.
7. **Inputs and actor authority: retained.** Shared scroll ordering, source
   eligibility, the reserved continuation footer and native callback permissions
   are unchanged. Scroll requests do not constitute proof that a picture
   assembled; the new bounded `queued`/`assembled`/`displayed` receipts separate
   those stages without leaking text or emitting per-frame Info traces.

## Evidence boundaries

Both supplied logs genuinely identify768/d18f3cb7c. They contain no missing
original or Results sample/apply warnings, but also no per-Results completion
receipts. The exact property transition in that hardware run cannot be recovered
from them. The reproduced starvation failure is source-proven and matches the
reported symptom; it must not be described as a confirmed packet trace of that
run or as fresh HMD acceptance.

Independent queue/receipt review is frozen in
`validation/build769/transport-lane/final-runtime-review.json`. See the lane
reports and final integration record for all runs, negative controls, input
bindings, failures, cleanup and the complete checkpoint result.
