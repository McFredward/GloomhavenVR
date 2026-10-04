# Frame 617: wall delivery and maintenance attribution

The Build 616 headset report remains unresolved visually: native wall materials
received intermediate numerical fades, but the maintainer still saw walls pop.
The previous GL fixture executed the original runtime delivery against an explicit
LOW, above-foundation fragment surrogate. It did not execute the game's compiled
HIGH shader on Steam Frame. Passing that fixture did not establish native HIGH
pixel continuity. See [the Build 616 evidence review](FRAME-617-WALLS.md).

This change adds bounded evidence at the real camera boundary. It does **not**
replace the native shader, adjust the ramp, change renderer ownership, or claim
that wall popping is fixed.

## Actual renderer delivery

`WallSegmentFade.DrawTrace.cs` records only native wall renderers after the real
`Apply` MPB setter. The driver subscribes to the existing head camera's
`Camera.onPreRender` event, which occurs after pre-cull and before submission. It
can therefore expose changes between the late wall write and camera submission,
including another pre-cull subscriber's modifications.

At that boundary, each selected sample reports:

- Renderer identity, fade, enabled and `forceRenderingOff` state.
- Actual renderer-wide map, cutoff, integer toggle and both material-gate floats.
- Original native material identities, shader names, keywords and authored gates
  for at most eight slots. Snapshot comparison includes material replacement,
  shader replacement, keyword and material-gate changes.
- Nonempty material-index MPBs, which take precedence over renderer-wide blocks.
  Their cutoff, map and gates are reported separately.
- Whether the observed renderer-wide/material state changed after the real late
  setter. The shader and all observed slot inputs are named rather than inferred
  from the scalar fade ledger.

Capture is Debug-only. It opens on the actual early intermediate native write,
selects at most six segment episodes and at most three distinct native material
routes per segment, and emits at most twelve phase samples per selected renderer.
The route selection avoids spending all samples on consecutive HIGH renderers
while missing a segment's toggle-native branch. Additional eyes in the same frame
cannot duplicate a sample. Scene/lifecycle reset releases references; an exception
disables the diagnostic for that activation without interrupting the native wall
effect or camera continuation.

The diagnostic does not query the scene, add a camera, alter a renderer/material,
read pixels, or claim GPU pixel verification. Command buffers and native fragment
execution still follow the event. Material slots after the eight-slot bound are
explicitly outside the sample. Ordinary logs gain no recurring diagnostic stream.

## Staleness safety and recurring commit cost

The periodic staleness ceiling still forces a commit after 30 skipped cycles.
The latest capture attributes approximately 140 ms stalls to those ceiling
commits; **maintenance reuse is not enabled** in this patch.

The existing signatures are useful gates, but they do not establish the full
native collector dependency closure. They intentionally omit `renderer.enabled`,
retain cached material-family classification, and survey wall subtree identity
without all native eligibility state. The round-robin wall-segment AABB probe
also does not cover every mounted/stacked/body member, membership change or
hierarchy/component derivation. The collector additionally consumes native tile,
room, door and prop data and mutates driver ledgers across 25 ordered phases.

A renderer-only snapshot cannot safely remove the guard. Spreading the present
mutating collector across frames would publish partially rebuilt ownership and
restoration state. Either approach requires a complete audited closure or a
separate private collector with deferred native writes and atomic publication.
The old diagnostic's claim that unchanged signatures prove completeness has
been corrected.

All 25 phases already have literal `WallFade.Commit.*` scopes and per-cycle
measurements. A forced ceiling commit now emits one Debug `CEILING COMMIT` report
ranking its four most expensive phases, their total/remainder, completion state
and exact native/reused bounds-read counts. It reuses those existing accumulators:
there is no additional stopwatch and no per-phase log stream. At most eight
reports are emitted per scene activation. Normal budget reporting is unchanged.

## Focused verification and evidence limits

`check-environment-budget-runtime.py` now binds the complete production
`Apply`/`EnsureTextures`, original enable/disable camera subscription methods and
the complete new draw sampler. Only its native frame-count read is explicitly
aliased to a deterministic fixture clock: `Camera.Render` cannot advance Unity's
player loop in a synchronous Editor fixture. Actual Unity camera events, MPBs,
materials, indexed overrides, keywords and renderer masks execute.

The focused run passed 359 production assertions and five targeted negative
controls. It verifies disabled normal logging, actual head-camera selection,
intervening native MPB reads, indexed overrides and the scene episode bound.
Production assertions also cover native keyword/gate/mask changes, state
preservation, additional-eye deduplication, diagnostic failure containment and
callback removal. A failed initial negative-control expectation and its original
output were retained; the control's reported expected assertion was corrected
and that case was rerun before the final focused pass.

`check-wall-maintenance-trace.py` binds all 25 actual native phase boundaries and
executes the exact new summary helper against explicit phase-array/logger/math
boundaries. It verifies top-four per-cycle ranking, read counts, the Debug gate,
scene cap/reset and read-only accumulator handling, with four causal controls.
It does not execute the native collector or measure its cost. The production
project also compiles with zero warnings and errors.

Preserved results are under
`.planning/debug/frame617-implementation/walls/` in the main checkout. No full
unrelated worker suite was repeated; final integration gates belong to the
integrator.

## Next headset interpretation

Use Debug and reproduce a native wall fade/return shortly after entering the
scenario. `DRAW DELIVERY` distinguishes a late state overwrite/disable/indexed
override from correct delivered noise/cutoff/keyword inputs. If those inputs
remain correct while pixels still pop, the investigation must target the actual
HIGH/toggle fragment branch on the Frame backend; the LOW surrogate is not proof.
`CEILING COMMIT` identifies the expensive native collector phase before any
bounded staging design is chosen. Both visual wall continuity and removal of the
maintenance stall remain hardware targets.
