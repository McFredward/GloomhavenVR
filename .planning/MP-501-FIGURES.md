# Build 501: held figures yield to native actions

Base: `7745d90c` (`dev`, ModBuild 500). This lane changes cosmetic figure ownership,
not authoritative movement, animation selection, message processing or network actions.
Version/build publication and complete integration gates belong to the integrator.

## Evidence and cause

Both supplied September multiplayer logs identify ModBuild 500 / `7745d90c`.
The paired Player logs show movement setup before the holder's release:

- Main Player.log: action 55 (player 2), MoveTileSelection at 49813;
  ActorHasMoved 49840, LocoTarget 49845, WaitingForMoveAnim 49847;
  local Brute release/heal at 49900–49901.
- Remote Player.log: action 104 (player 1), MoveTileSelection at 102874;
  ActorHasMoved 102902, LocoTarget 102907, WaitingForMoveAnim 102909;
  local Mindthief release/heal at 102950–102951.
- Remote action 77 also releases the held Mindthief after movement setup.

The logs do not sample the remote miniature's exact pose at that boundary. The
causal source path is explicit: `ActorBehaviour.SetLocoTarget` reads the current
root position into its starting distance, origin and target vector (native lines
272–274). `NetFigures` previously kept suppressing native transforms and
interpolating toward the peer's hand until a later network release. Dropping the
hold at the next Update cannot undo the origin already sampled by native movement.

Native attacks similarly read source/target transforms before their first clip.
A native animation callback alone therefore cannot cover the early movement and
facing reads. The older local forced-release glide could also overlap a native
action; the latest explicit immediate-return instruction supersedes that older
August ruling. Ordinary voluntary idle releases retain their glide.

## Changes

- Existing ActorBehaviour transform prefixes now yield to the same per-actor busy
  rule locally and remotely. Native movement, push/pull and teleport setters
  relinquish cosmetic ownership before their original bodies read transforms.
- A scoped, main-thread-only Choreographer presentation prefix returns the exact
  movement, attack, hit and death participants, plus native source actors whose
  message branches perform an early LookAt. It does not consume, replace or
  re-dispatch the message. Human wait messages and unrelated idle figures remain
  untouched. Summon/revive use their explicit native actor fields.
- The game's `MF.AnimatorPlay` seam releases the actual held actor only for an
  available non-idle clip, before playback. This also covers aura-originator or
  receiver animation selection, awakening and other non-movement animations.
  Missing clips and the native idle vocabulary do not end a hold.
- Native locomotion flags supplement animation names: walking can occur inside
  `Idle-Run`, so that name alone cannot prove a miniature is stationary.
- Remote action handover restores the captured board-local position, rotation,
  scale and animated-root offset. It never reparents the native figure or writes
  movement/choreographer/rule fields. Multiple claims share the original home
  pose; one peer's ordinary release cannot reset another active peer's scale.
- Interrupted actor identities remain quarantined in their advertised slots until
  an owner release or switch. Delayed samples cannot re-grab the actor after idle,
  including slot migration, two peers, destroyed/unresolved instances and recreation.
  Teardown removes the peer's records. No new packet grammar or cadence is needed.
- Forced local releases and action-interrupted release glides restore immediately.
  The normal idle release glide continues to be transmitted.
- Home ghosts stop drawing immediately and native selection rings recover when
  action ownership changes, including after the normal ghost tick has already run.
- Default-visible `REMOTE FIGURE ACTION RELEASE` records the handover for hardware
  correlation; existing diagnostic grep tokens remain present.

The source actor may still face an unrelated idle held target during healing or
similar effects because native LookAt reads that target's actual transform. This
lane does not force unrelated idle targets out of a hand: the explicit standing
instruction permits them to remain held while other figures act. Actual target
attack/damage/death and non-idle animation handovers are covered.

## Validation

`bash scripts/figure-hold-tests.sh`: **1,440 assertions** and **six runtime
negative controls** pass. It compiles production `NetFigures`, held membership,
transform/message/clip prefixes and ring restoration, extracts the actual local
release/glide policies, per-figure busy predicate and ghost-retirement methods,
and uses small Unity/game stand-ins. Native local Restore integration is also
checked against production source. Controls remove pre-origin restoration,
revive a stale held lease, restore the old forced glide, omit locomotion flags,
omit the attacking actor, and let a cosmetic animator probe exception escape the native
entry guard; each reaches its intended runtime failure. Injected participant-lookup,
animator-probe and local-restore failures are contained while the native prefix returns
normally. Unity scene-read checks are inside the guards as well.

Strict Release build: **0 warnings / 0 errors**. Surface comparison against the
fresh build-500 baseline: config **625 unchanged**, patch census **152 → 160**, log
markers **4,718 → 4,719**, no removals. `git diff --check` passes.

The harness does not run Unity's Animator, native locomotion, rendering or actual
network latency. Headset confirmation is still required: hold a figure through
walk/jump, attack, damage/death, teleport and push/pull on both machines; inspect
its immediate board origin, the lack of a duplicate ghost, and absence of a
late re-grab. Also test a normal release glide, an unrelated idle held figure,
two hands, a new grab after action, and peer departure during a hold.

## Integrator actions

Register `Choreographer_HeldFigureAction_Patch` and
`MF_HeldFigureAnimation_Patch` next to the existing figure transform patch in
`BoardModule`. Wire the harness into local/hosted checks, regenerate the patch
inventory, and run the complete gates on the integrated build. Keep 1.0.0 and
bump ModBuild once with the card-flight lane; do not publish a worker branch.
