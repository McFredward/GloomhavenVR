# Card and board progression review (Build758 integration)

Base dev `cd827d688`, 2026-11-04. Worker ownership is the entire Cards and Board
production subtrees, one dedicated focused replay and the existing forced-rest
fixture's API adaptation. The integration is class C; the parent owns the final
strict builds, all source suites, complete local/golden checkpoint and remote
consumer/localization integration. No worker push or release is authorized.

## Scope and review model

The recorded inventory includes all 160 original C# files in those subtrees plus
the two added production files. The ignored `review-inventory.json` records each
path/hash/category; it is accounting, not a claim of executed line coverage.

| Scope | Current files | Progression boundaries reviewed |
|---|---:|---|
| Driver, native API and serialized actions | 14 | Selection, required rest, readiness/revoke, half actions, native Ready/Undo/Skip, damage/lose/recover, queue ownership, scene admission, hand changes and teardown |
| Native callback/isolation patches | 19 | Tutorial gates, original callback ownership, pick open/end/owner changes, inactive-hand burn admission, native card pooling and scene-child destruction |
| Physical affordances and piles | 26 | Pending slots, swaps/take-back, pick pages/reopen, item use/subchoice, cancel/undo, rest controls, dead/foreign focus, hidden native-control replacement and board recovery |
| Native card art/widget lifetimes | 28 | Borrowed-face return, original layout preservation, asynchronous artwork, burn/flight sequencing, canceled iterator ownership and presentation barriers |
| Figure/prop lifetimes | 41 | Local/remote held release before native action, busy/idle and FlowControl guards, animator admission, clone/render-only ownership, restoration and existing narrowly scoped attack-flow watchdog |
| Focus, picking, configuration and pure support | 34 | Decision-owner pin, local ownership fallback, read-only remote focus, tile/waypoint/actor click admission, tutorial targeting, AoE, independent camera-arrival pause release and reset paths |

The audit follows native entry and exit events and separates an automatic wait
from a human decision. A yellow initiative frame or missing card overlay cannot
identify either on its own. Renderer/material, mesh/contour, geometry and
photometer support is checked as an indirect dependency: it may not synthesize
rules, consume a native choice or retain a global input blocker. No speculative
replacement of already working paths is introduced.

## Confirmed defects and fixes

### C1: canceled queue work retained permanent admission flags

`CardsDriver.ReleaseCardsBeforeSceneLoad` calls `CardActionQueue.Clear`. Previously
Clear discarded every completion without notifying its owner. Four paths set a
busy flag before enqueue and clear it only in completion: long-rest pumping,
selection-hand recovery, pick-confirm reopen and mandatory pick commit. An
aborted native load can preserve that same driver, leaving any such flag true
forever. Rebuilding native faces did not release those flags.

The queue now has a separate optional cancellation notification. Clear snapshots
and removes pending work before notifying; it executes neither the native action
nor its ordinary completion. Each of the four callers releases only its own mod
flag and requests a fresh authoritative rebuild. Callback failures cannot starve
other owners; a callback's new enqueue survives for the next ordinary pump.
Currently executing work still finishes normally. Module shutdown and driver
teardown retain the same cancellation semantics. No native actor, phase or
choice is changed by cancellation.

### C2: successful native animation cancellation never released its UI flag

Fresh original `CardsHandUI.CancelAnimateCardLost` clears animatedLosingCard,
cancels/clears its tweens, stops the coroutine, restores layout, releases the UI
lock and invokes its original completion callback. It omits the coroutine's last
`AnimatingLostCards=false`. The original
`Choreographer.WaitTriggerAnyOnLongRestAddActiveBonuses` waits on that exact flag;
it can therefore retain a completed/canceled UI lifetime indefinitely.

A postfix completes only that successful native cleanup's missing flag write.
The prefix must have observed an actually running animation. A weak per-hand
request generation excludes replacements, including reentrant unstarted or
already finished requests. Original exceptions bypass the repair and propagate;
no timeout, callback replay, phase advancement or network acknowledgment occurs.
A bounded normal-level anomaly records successful repairs. The read-only burn
watch now describes remaining exceptions/replacements without claiming that its
flag alone proves callback delivery.

This corrects presentation-liveness bookkeeping, not a rule-engine property.
The historical blanket claim that a private UI setter could never be repaired
was incorrect. It remains forbidden to fabricate a native continuation.

### C3: forced-rest cue counted the fan but ignored already selected cards

A last valid pair leaves zero cards in the hand, or one remains after selecting
the first card. The old cue read only HandAbilityCards.Count and incorrectly
advertised a forced rest while Hand+Round still met the original two-card rule.
The cue now receives the actual `MustRestInsteadOfPlay` result. That rule also
excludes extra-turn picks and non-selection phases. A shared actor overload lets
the existing remote furniture use exactly the same synchronized model; root
owns that consumer change. Rest availability and selected flags remain native.

### C4: an ordinary empty decision had no concise next-action explanation

The existing mirrored board banner gains a final fallback for ordinary two-card
selection, genuine required rest, final confirmation, remaining owned characters,
already-ready waiting, foreign ownership, own card actions and laser targeting.
It also consumes root's passive delayed automatic-wait notice, so a stalled
synchronization is not mislabeled as a card/rest choice.

Existing mandatory item, floating panel, pick and exhausted status retain
priority. Blocking modals/overlays and hidden/missing boards do not publish a
fallback. Ordinary input instructions defer incomplete burn layout; an actual
automatic-wait notice can still explain a visible board with no hand at all.
The card-action instruction requires actual docked cards and no live native
End Turn/Continue control, so an empty long-rest dock never advertises an action.
Messages are change-gated, contain no private card identity and use existing
owner-to-peer banner synchronization. No additional interaction, choice or
presentation timer is introduced. Root owns English/German localization.

## Existing prevention retained and human-error review

| Potential apparent or technical block | Current owner and disposition |
|---|---|
| Too few ability cards to form a pair | CardsGameApi.MustRestInsteadOfPlay; corrected rim and explicit rest explanation, not fabricated slot offers |
| Another owned mercenary still needs cards | UpdateInitiativeTodo and SelectionReadyHighlighter plus fallback explanation; shared confirmation remains subject to original native readiness |
| Already ready, waiting for others | Native UIReadyToggle/IsConfirmed and root's attributed wait cue; explicit waiting text, readiness remains revocable |
| Long-rest sacrifice, damage burn, discard or recovery | UpdatePickStatus/ResolvePickOccasion state the actual occasion, required count, batch and confirmation/cancel; the same original commit handlers remain authoritative |
| More than two mandatory cards | Existing pick batch lock, retained selected prefix, undo/restart return flights and final native confirm; queue cancellation now releases reopen/commit admission |
| Item refresh/surrender or lose-reward choice | UpdateItemDemandStatus and ItemsPile show counts, legal originals and item-fan wayfinding; original picker controls confirm |
| Doom/distribution or element/target selection | UpdatePanelDecisionStatus retains the floating panel's native title; fallback targeting text is shown only on the actual controlling seat |
| Foreign or wrong-character view | CharacterFocus/decision pin and ConfirmCapsForeignView retain actor ownership; selection fallback and queue recovery preserve reachable return, foreign selection gets an ownership explanation |
| Exhausted character | UpdateExhaustedStatus names exhaustion; dead actor controls do not claim a live input offer |
| Native UI unavailable because its adopted parent was destroyed | Existing NativeCardSceneLifetime/NativeCardPoolLifetime return original child hierarchies before native unload; real scene admission is independent of generic IsLoading |
| Minimized/doffed/rebuilt rig or lost control board | Existing board recall, deferred tracked placement, finite/apparent-size guards and ordinary resume pose preservation; no unsolicited fixed-board follow |
| Held figure blocks native action/animation | Existing direct action and animator hooks release local/remote figures before native work; native busy/FlowControl gates remain unchanged |
| Original callback throws or original network/rule progression fails | Preserve the original exception and authority; diagnose it. Do not treat a timer as permission to skip an outcome or acknowledge another player |

### Configuration-off admission

There is no optional Cards/Board/HalfSelection module-disable setting in these
subtrees. The VR/dev module lifetime owns HandSuppression.Active and restores the
original window on shutdown. CardsConfig switches affect comfort, placement,
sound, particles and optional cues; they do not disable the original callbacks.
BoardConfig.TouchTilesWithFingertip=false excludes only near-touch admission;
BoardPick's ordinary far ray and BoardClickDriver.TickFar remain available.
HoverHaptics, SnapToHexCenter, AutoFocusOnTurn and SelectionReadyHighlighter affect
their named optional aid; half-card laser/poke and original Ready/Undo callbacks
remain. Parent owns global interactor-policy and window fallback review. No
configuration is silently re-enabled by this change.

## Focused evidence

`check-card-progress-lifetime.py` compiles the actual production queue, the four
queue-owning driver methods, actual scene-release body, actor/hand rest predicate,
actual progress-helper class and native cancellation hooks. It freshly decompiles
original GH.Runtime CancelAnimateCardLost, OnClearLeanTweens and OnDisable and
executes those bodies unchanged except the invocation adapter's method rename.
The original long-rest WaitWhile predicate is source-bound. Unity scheduling,
layout drawing and native rule/network endpoints are explicit ports; rule calls
are counted/refused, never simulated as a successful network session.

Final focused run `run-kwavzqlz`: **433 assertions and 16 compiling causal controls
pass in 58.52s**. Controls separately remove cancellation notification, substitute
success callbacks, lose reentrant work, strand each of four actual caller flags,
restore original missing animation flag cleanup, remove replacement/owned guards,
count only the hand, remove extra-turn exemption, misattribute targeting or
automatic-wait instructions, or wrongly advertise actions during an empty
long-rest dock or native End Turn/Continue. Normal completion, repeat cancel, native exception,
replacement and cross-hand cases retain their original outcomes.

Earlier retained attempts: first fixture compile failed on three uninitialized
port fields; second failed because its frame driver advanced once while the real
hand-switch recovery requires two frames. Those fixture-only errors were corrected,
not relabeled as passes. Intermediate runs passed 122/10, 339/12, 427/14,
430/14 and 431/14 as rest/notice/modal/empty-hand proof was extended. Only the
433/16 run binds the final functional production inputs. Subsequent Flows and
DamageFlowPatches edits correct diagnostic wording and stale comments; the
executed method bodies are unchanged. Parent compiler/source checks cover those
final nonfunctional differences.

The existing actual Unity multiplayer-progression suite requires its affected
cue consumer rerun after root integration; its fixture was adapted to the bool
contract here. Parent owns the complete class-C checkpoint, final source binding,
strict compile and native/remote consumer checks. No headset, Photon or reported
Issue27 reproduction is claimed. In particular, neither C1 nor C2 is established
as the cause of that unmatched historical video/log incident.

Every standalone run calls cleanup_completed_run after process reap. Source,
manifest, execution assemblies, original native body, controls, failed attempts
and logs remain; generated bin/obj caches are removed. Only this worker's caches
and eventual worktree may be retired.
