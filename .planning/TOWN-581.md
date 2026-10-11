# Town service and window input follow-up — ModBuild 581

## Hardware evidence and scope

The supplied local `Player.log` and `LogOutput.log` identify NPC ModBuild 580.
The available remote logs are older and cannot establish this run's peer view.
The maintainer observed three failures: the merchant looked at a visitor before
speaking, the enchantress could extend her hand without a card-placement cue,
and the laser passed through scenario story and combat-log close controls.
The latter blocked native continuation. No headset result exists yet for 581.

## Merchant greeting

The face chooses a visible visitor before the merchant's body enters attention.
Body entry is deliberately held until a gripped coin finishes its transfer.
Build 580 moved the greeting from native shop entry to the body-attention edge,
so the head could already look at a more distant visitor while speech still
waited. The face author's exact visitor decision now cues the shared line.
Later body motion or native shop entry cannot queue a duplicate. Leaving before
the line starts cancels it. Event-bounded Debug records expose queue and start.

## Enchantress approach

The second approach in the Build 580 log occurred while Merchant remained the
native guildmaster destination. The enhancement handoff retained intent but
required destination `None`, so it never opened the native enchantress service.
The physical hand approaching the enchantress switches the local native
destination through the existing path, preserving the selected character and
deferring to a live modal confirmation. Head proximity alone does not switch
away from another stand. The map hand changes from merchant items or temple
purse to owned ability cards only when that physical focus moves to the
enchantress. The merchant cabinet and all three resident bodies remain active.

The same rule applies to a visitor standing between two NPCs or to different
players visiting different NPCs: proximity at one station never reserves another
station. Each NPC's private transaction reservation begins when a card or purse
is physically parked. Multiple visitors can inspect one NPC before that edge;
after it, only the claimant has the local controls until the offer is withdrawn,
confirmed, the visitor leaves, or the session expires. A single local player
still has one native guildmaster destination at a time. Physical hand focus
selects which station's original controller supplies that player's hand, while
the other stations remain present. This is a native game constraint, not a
shared NPC proximity lock.

The visual transaction bit in TLV92 only selects the shared presentation author.
The native host independently grants a short reservation through ReliableOrdered
message 25; the original merchant, temple and enchantress callbacks wait for that
grant. A late or reordered reply cannot extend an expired reservation. A second
visitor at the same NPC receives a busy response and recovers their parked item,
while transactions at either other NPC remain independent. An unreachable,
unmodded or deliberately flat-network host does not authorize the immersive
transaction; the original native VR window is restored for that opening.

The enchantress's offered-hand pose follows the visible local or replicated
original placement cue, or a parked card. A remote visit manifest alone no
longer extends her hand before the overlay is ready.

The priestess covers the bowl only when every currently known temple visitor's
original controller reports donation unavailable. An unknown/late sample leaves
it open; each player's own controller independently denies an invalid purse.
The blessing revision now advances only from native donation success, not a
character switch or affordability change.

## Transparent controls

The NPC branch's visible-ink ray filter discarded alpha-zero graphics before
the normal uGUI raycast. The native story box uses a transparent full-area
click target to advance; mod close controls, including combat log X, use a
transparent hit plane under their painted button. A transparent graphic now
counts as a laser surface only when it is an active raycast target with a live
pointer handler. Passive transparent layout, hidden CanvasGroups and disabled
controls remain non-interactive. This applies to every window using the shared
ray path rather than a story-only exception.

## Verification

- Merchant schedule regression: a gaze with body attention still zero queues and
  starts one greeting; subsequent body attention/native entry does not repeat it;
  departure cancels a pending greeting. Town service codec suite passes.
- Transparent-control Unity 2021.3.5f1 harness: 101 assertions and eight
  negative controls pass, including story/close hit planes and hidden/decorative
  geometry.
- Enchantress and temple Unity runtimes cover cross-service hand focus, native
  callback gating, overlapping visitors and modal safeguards. The grant codec
  and ledger have 145 assertions; the town-service codec has 50,241.
- The shared-interaction Unity mirror suite passes after its fixture was updated
  for TLV92 and the native-only blessing revision. The resident runtime checks
  the visible enhancement cue rather than inferring readiness from proximity.
- Full Release build succeeds with zero warnings and errors. The remaining
  repository-wide source/wire guard is recorded separately after integration.

The merchant's visible hand-to-belly gap remains open. Its geometry findings
are documented in [TOWN-580.md](TOWN-580.md); this input/voice build does not
claim that the asset was fixed.
