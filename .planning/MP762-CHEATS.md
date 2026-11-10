# Build762 multiplayer cheats

The maintainer now explicitly permits cheats while testing multiplayer. This
supersedes the historical2026-09-04 blanket online prohibition. The config stays
default-off; movie reviews remain on Advanced > Cheats and retain their existing
spoiler opt-in. The normal native game and rule assemblies are unchanged.

## User-visible behavior

- The host can request a normal scenario win. The original
  `DebugMenu.WinNoToggle` already sends native `DebugWinScenario` to clients,
  including unmodded clients. Keep its loading/story/damage/ending checks, exact
  scenario confirmation, menu closure and post-closure host revalidation.
- Gold, campaign unlocks, room reveal and requirements overrides are host-only
  while online. Every real native roster member must advertise this build from
  its current native player object. Pending joins/character switches, incompatible
  peers, a busy native processor, loading, story/damage and outstanding rules or
  visual messages refuse new commands with a localized explanation.
- Gold still uses the pre-existing **3D map selection** UI contract. The backend
  uses native map phases independently of map rendering, but this change does not
  expand the Gold button's existing UI eligibility to the2D map.
- The exact selected character is addressed by `CharacterID + CharacterName`;
  ID alone denotes the class prototype and is insufficient. Shared-gold mode
  instead credits the native party purse. Only the host writes its campaign save.
- Offline actions retain the original native APIs and confirmation behavior.
  A replicated online requirements enable never turns into an unrequested
  offline override after leaving multiplayer or loading another campaign.

## Native ordering and compatibility

These are intentional test mutations, not cosmetic presentation. They do not
execute in the GVR presence receive callback or a new side channel. The host
calls the original `Synchronizer.AutoExecuteServerAuthGameAction`, which places
one command in the original reliable ordered `GameAction` queue. Native
`GameActionEvent` compresses its action type to7bits; the native enum is0..121.
The command uses native type127 only while `Enum.IsDefined` says it is unused.
This is a **different namespace** from GVR extension IDs; no GVR TLV127 is added.

The bounded payload has an independent fixed byte golden. It includes sequence,
roster, map seed/mode, exact scenario seed/ID for reveal, operation and exact gold
recipient. UTF8 identities, enum, booleans, length and exhaustion are validated.
Its512-byte maximum fits the original event channel's600-byte binary attachment
limit (`NetworkManager` packetSize1200, `EventChannel` packetSize/2). The existing
native event serializer carries that length-prefixed BinaryData attachment.

The host checks its actual pending payload, original ExecuteLocallyFirst marker,
current opt-in, real AllPlayers roster, pending connections/joins and exact world
again immediately before mutation. An invalid host command marks the ORIGINAL
GameAction invalid, so native FinishProcessingAction never forwards it. A new
unmodded player cannot interleave between validation and forwarding on the
synchronous original Execute → Finish → Forward main-thread path. New joins
receive the native updated state. No additional external replay consumer of
GetProcessedActions was found in the inspected GH.Runtime; an actual network
late-join/rejoin run is still required, rather than claiming universal replay
acceptance from source alone.

`NoteVersionReceipt` binds the existing version advertisement to a weak reference
of the current native NetworkPlayer, avoiding stale build trust after a reused
native player ID. Host pending and requirements leases also bind a weak native
cohort. They retain no actor/world graph. A transient receiver join flag can
settle after an accepted requirements command: it temporarily returns original
verdicts without forgetting that accepted grant. A real map/session/cohort
replacement retires it. Receiver execution never consults private Cheats or
FlatNet/UI settings; a valid host-authorized command must have the same effect.

Unlock invokes the same native LockQuest/UnlockQuest pass as the offline cheat,
including Blocked→Locked→Unlocked, skipping Job quests. Its online visual refresh
uses original `RefreshAllMapLocations(null, save:false)`. It deliberately avoids
DebugShowAllScenarios, whose persistent local ShowAllScenariosMode would also
bypass requirements and survive a later mixed roster.

## Native one-action permits and failures

Native ProcessAction consumes ProcessOneAndHalt before Execute, and native
FinishProcessingAction restores ProcessOneAndSwitchBack. A refused test action
must not spend either user-choice permit. The prefix therefore withdraws only
an exact issued, still-unforwarded host head when its permit changes. One bounded
issued-byte receipt survives private context retirement until that exact head
executes or is withdrawn; it contains no actor reference.

An accepted receiver command remains in the native order. Under the original
lock=false, ready=true, processing=false gates, its exact head executes through
original patched GameAction.Execute without invoking native ProcessAction/Finish
permit changes. A scoped synchronous guard blocks *all* reentrant queue attempts
until finally; no phase/ready/ACK/ActionID is written. The head retires before its
model invocation, as native ProcessAction does, so a failure cannot reapply it on
the next frame. Genuine model/world failures use original HandleDesync. There is
no fabricated rollback, acknowledgment, choice, phase progression or recovery.

Reveal can wait before dequeue using the original ConfirmAction SRL/message
busy condition, including its damage-response exemption. This uses the original
retry loop while processingAction stays false; no mod timer or automatic choice.
Ordinary actions and native locks/Halted/readiness remain untouched.

Backup happens before enqueue. Optional visual refresh, save and diagnostic
failures cannot stop forwarding after an admitted deterministic model change.
Actual quest/gold/reveal rule failures remain visible to original native error
handling; no all-or-nothing rule rollback is claimed.

## Verification and limits

Worker source freezes at05ac21999 after native Win checkpointd8b6d3345,
ordered command checkpointd3adff6bb and weak-context checkpoint7b7f55bb0.
The final complete `multiplayer-cheats-runtime` pass has252 assertions and all16
compiling causal controls,52.634s, with unchanged source pins. It executes exact
native excerpts for ProcessorState(struct), SetState, QueueUpAction,
TryProcessNextAction, ProcessAction, FinishProcessingAction,
AutoExecuteServerAuthGameAction, ForwardGameActionToClients and Execute.
Native enum/source/binary hashes are pinned in the fixture's manifest. Model
setters, sockets/event attachment, coroutine scheduler and singleton fields are
explicit ports. This is not Photon, actual rule-library execution or headset proof.

Cases cover both one-action modes, real native Halted/readiness/switch-back,
following ordinary choices, native locks, model-callback reentrancy, exact native
ID replacement and fresh advertisements, context reset before queued withdrawal,
receiver local opt-in off, transient join flags, MP→SP requirements retirement,
party purse, optional IO/logger errors and original native model-error reporting.
The source-bound failures were retained: missing fixture imports, malformed
truncation escaping InvalidDataException, and outdated causal-control labels.
Earlier incomplete one-slot fixture ports were replaced with the actual native
struct/state kernel; their narrower passes are not promoted to final coverage.

The native Win helper passes70 assertions and both original controls. Movie
options pass915 assertions and its existing controls with the new localized
scope dependency; final containing-suite receipts are recorded separately.
Strict final worker Release is0W0E (4.76s). Parent owns final union, source gates,
wire goldens, complete risk-classC aggregate and dev push. No hardware action,
release, local ZIP or worker-branch push is performed. Canonical cleanup retains
logs/results/source/capture evidence while removing generated project caches.
