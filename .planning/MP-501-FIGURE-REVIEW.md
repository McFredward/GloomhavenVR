# Independent review: native figure action handover

Review date: 2026-09-14. Implementation lane: `codex/mp501-figures`, based on
`7745d90c8`. This supplements `MP-501-EVIDENCE.md`; hardware output remains unverified.

## Findings corrected during review

1. **Interrupted leases were lost when an actor temporarily stopped resolving.**
   The first implementation resolved the Unity actor before checking an interrupted
   same-id lease, and the tick pruned missing roots before checking interruption.
   A stale held sample could therefore revive a recreated actor after a missing-instance
   interval. The revised code checks the same-id lease first, retains interrupted
   records before root checks, and retains a shifted unresolved alias by stable id.
   Tombstones remain bounded by peer slots and end on the sender's clear/switch or
   peer teardown.

2. **Releasing one duplicate remote owner could overwrite another owner's scale.**
   `RestoreHomeScale` initially searched only the first active record; that could be
   the outgoing record itself even when another active alias existed. It now searches
   every other active record, excluding the outgoing one. This prevents a temporary
   board-scale write while another peer still owns the miniature's held presentation.

3. **Generic message ownership was too broad for non-movement animation.**
   The first scoped message list included Sleep/Awake and other branches whose actual
   animated actor can differ from `m_ActorSpawningMessage`. Native aura animation
   can target the originator, receiver, both or neither. The revised approach retains
   early message handling for concrete movement, attack participants and direct
   facing operations, and handles actual available non-idle clips at `MF.AnimatorPlay`.
   SleepIdle and failed state lookup remain no-ops at that seam.

4. **Initial tests stubbed the local release implementation.**
   The initial remote harness could validate network lease release but its local
   `FigureGrabbable.ReleaseForNativeAction` stub simply wrote the origin. The lane
   was asked to test extracted production local methods, including voluntary glide,
   forced immediate return, interruption of an existing glide and late release callbacks.
   The integrator must read the final harness results rather than treating the initial
   stubbed local assertions as evidence about production local release.

## Source checks

- `ActorBehaviour.SetLocoTarget` reads the current transform into the movement start
  before setting movement flags. The new entry prefix restores presentation before
  this read. Merely unblocking the next native Update would be too late.
- Native `MF.AnimatorPlay` (decompiled line 77) checks animator, controller and
  layer-zero state availability, then enables and plays the animator. The added
  prefix uses those same availability conditions, excludes the game's idle state
  vocabulary and resolves the actual held actor through animator ancestry. It does
  not skip or replace native playback.
- The message prefix preserves native main-thread/error/phase eligibility; it does
  not consume messages, alter parameters or advance the rule engine.
- Movement participants include carried actors; swap participants include both
  actors; attacks include attacker and actual target list. An unrelated held actor
  remains held.
- Native Summon/Revive use dedicated actor fields. Their actual message producers
  (`CAbilitySummon` lines 450/474 and `CAbilityRevive` line 230) set these equal to
  the spawning actor, supporting the current direct-facing fallback for those cases.
- Interrupted aliases are excluded from the native-transform suppression registry,
  so preserving a tombstone does not continue suppressing native animation.

## Remaining review boundary

Some direct facing branches such as healing read another actor's transform while
that target may still be held and idle (`Choreographer` lines 5704–5708; damage
origin lines 6272–6276). This was flagged to the implementation lane. Releasing
every nearby/targeted idle actor indiscriminately would conflict with the standing
rule that unrelated idle figures remain inspectable. The final implementation and
report should distinguish the affected actor's movement/animation handover from
any broader redesign of native facing toward an otherwise idle held target.

All checks here are source review. They establish the placement of the handover
and identify lifecycle failures; they cannot certify a headset frame or a native
Unity root-motion trajectory from stubbed tests alone.
