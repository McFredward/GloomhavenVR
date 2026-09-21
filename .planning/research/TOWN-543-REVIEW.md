# Independent resident facial-runtime review — build 543

Review base: `dev` integration `97f15705`, 2026-09-21. Runtime draft inspected
read-only in `/home/claw/gvr-town543-gaze`; this lane owns this document only.
The review covers authority, clocks, stale/reordered packets, transforms, lifecycle
and bounded runtime work. It does not establish headset appearance or auditory quality.

## Findings reported to the implementation worker

1. **Authoritative expression clock could diverge permanently.** The original draft
   incremented a local clock and followed with `Max(local, received)`. A viewer whose
   local history was already 500 seconds would ignore an elected author's 20-second
   clock, so blink, smile and brow phases would differ indefinitely. Teardown also
   retained that accumulated clock. The implementation worker changed following to
   the received clock (already advanced for elapsed reception time), resets on
   teardown, and reserves clock inheritance for authority handover. A source-bound
   population regression explicitly starts with local 500 / remote 20. This is a
   source-proven defect and correction, not a hardware reproduction.

2. **Empty/blocked attention searches bypassed the cadence limit.** The early return
   checked the deadline only while a valid target existed. With no eligible target,
   every render frame rescanned peers and could raycast for all candidates. The
   four-player, three-NPC upper bound is 1,080 casts/second at 90 Hz. This is a
   worst-case code-path bound, not a measured workload. A bounded short retry for
   unsuccessful searches was requested, preserving cheap per-frame tracking of an
   existing target and prompt invalidation of departed players.

3. **A retired epoch could replace a new epoch through recovery presence.** The fast
   stream rejects mismatched epochs, but the original `ObservePresence` accepted
   every changed epoch. A direct A→B→retired-A sequence therefore caused subsequent
   fast B packets to be rejected. Normal `FfsNetTransport` presence envelopes are
   already sequenced by `ExtrasFragments`, which rejects older completed snapshots;
   ordinary in-connection reordering is protected. The requested retired-epoch guard
   is additional lifecycle/reconnect hardening, not evidence of a recorded transport
   failure. Regression requested: A/99→B/1→A/100→B-fast/2.

## Boundaries checked

- Record 80 is additive; existing record 79 remains unchanged. Active payload is
  94 bytes, complete small packet 102 bytes, worst presence 7,082 bytes below the
  unchanged 7,168-byte assembly cap. The 7,339-byte writer allocation retains the
  prior 257-byte largest-record spare margin.
- Only the elected resident authority publishes facial motion. Followers do not
  choose a viewer-local head target. Presence establishes stream lifetime;
  high-rate samples cannot independently elect another author.
- Angle, clock, speech-age and flag validation is bounded and rejects non-finite
  input. Sequence comparison uses wrap-aware serial arithmetic. Peer storage is
  capped at eight entries, and departure/reset paths clear facial state.
- Before each body sample, the previous facial head delta is removed and eye rest
  rotations are restored. Gaze then applies after the body clip using the authored
  optical frame, including inward-facing stations. This avoids accumulating the
  previous frame's head rotation into the next pose.
- Binding discovery is constructor-only. Runtime interpolation uses value types;
  facial weights use cached bindings, update all facial LODs, and skip unchanged
  values. The new fast stream has a fixed 15 Hz upper bound for all three residents
  together, rather than three streams. No per-frame mesh, material or hierarchy
  reconstruction was introduced by these files.
- Cosmetic face failures are isolated from native service/visit continuation and
  emit a bounded per-service warning. Missing face assets are not presented as
  successful facial animation.
- The speech adapter remains unbound. The user chose original recordings, and the
  separate native-voice audit found the named NPC dialogue unvoiced. No generated
  greeting or unsolicited narrator playback belongs in this candidate.

The review did not rerun the implementation worker's concurrently running suites.
Final test evidence and exact integrated commits must be recorded by the integrator;
source inspection alone does not establish networking behavior on actual hardware.
