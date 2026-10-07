# Flat/VR crossplay quest readiness review, 2026-10-08

Worker base: `dev` at `819a9a9ee`. Scope: `MapQuestReadyUp`,
`ReadyToggleParkClaim`, `MapQuestDepartureValidation` and their causal native-source fixtures. This lane
does not modify wire fields, authoritative game state, the native all-ready
handshake, scene travel, or the native readiness participant population.

## Source-proven failures

1. `TickPendingClientPrompt` discarded the exact native quest-preview callback
   whenever `UIReadyToggle.IsVisible` was true. Native
   `UIReadyToggle.Initialize(show:false)` retains `_requestVisible`; its
   subsequent native `SetInteractable(true)` can reveal a prior or early VR
   request before the new native preview callback runs. The dispatcher therefore
   inferred an answered prompt from a visible reused singleton. That inference
   skipped the native quest selection/card and cancellation control.
2. `MapQuestReadyUp.Reset` erased pending native callbacks on VR room teardown.
   The native map controller may survive a room rebuild, and its hidden-HUD
   prompt can be captured before the next room activation. Reentering that room
   had no remaining prompt to answer; reconnecting happened to raise a fresh one.
3. The desktop presenter's native installed click is a wrapper:
   `HideMultiplayerQuestPreview(); onConfirmCallback?.Invoke()`. Capturing the
   bare argument bypassed the native hover-preview close, allowing a second
   stale native quest card after accepting the proposal.
4. A park claim trusted any parked object even when it differed from the
   current native singleton. Claims also remained good for a destroyed target
   until their clock expired. Both now require the actual current live target.
5. Native quest initialization disables `validateReadyUpOnPlayerLeft`. After
   three participants become two with all remaining participants ready,
   `OnPlayerLeft` removes the departed member but returns before the native
   controllable-state ACK coroutine. The full ready list also refuses an
   ordinary `ReadyUp(false)` while the original Cancel may still be visible.
   A subsequent all-ready event can hide it. This is a separate native
   departure corner, beyond the initially reported missing quest prompt.

## Resulting behavior

Each captured native proposal is driven once through its original installed
click continuation on an online client in the active VR room. The callback is
validated against the original native controller and stable quest identity,
the Quests toggle state, and the existing point-of-no-return guard. Missing
native initialization waits; cancelled/replaced scenes and committed/other
readiness decisions refuse the callback. Visibility no longer proves that the
proposal has been answered.

A pending prompt survives room presentation reset and is revalidated before
use. Native desktop `OnClicked` and console `Confirm` postfixes consume the exact
pending callback when the player answers it normally in 2D. Consequently room
entry cannot replay an already answered native prompt or disturb its readiness.
The desktop dispatcher captures the actual native installed wrapper; optional
metadata fallback performs native public hover-preview cleanup before the
original supplied continuation.

Native ready-up remains entirely manual and uses the original `ReadyUp` entry
point. Unready on actual quest replacement still uses the game's own validated
local-player withdrawal, with `autoValidateUnreadying:true` for a full ready set.
Visibility blocks, participant assignment, all-ready hiding, controller/quest
state, and the native host's controllable-state ACK/`ReadyProceed` remain native.

During a running online VR session, native `Initialize` now receives its own
`validateReadyUpOnPlayerLeft:true` option only for Participant/Quests in MapHQ
or MapAtLinkedScenario. The original host departure handler evaluates its own
current participant/awaited-player rules and continues through its unchanged
controllable-state ACK coroutine, timeout and `ReadyProceed` callback. No native
list, quorum, ACK or coroutine state is written by the mod.

An unmodified Flat host still owns its original initialization and host barrier.
A VR client cannot start that host's departure coroutine. Instead, a real native
departure may preserve the existing local Cancel when it is visibly enabled,
the local player is ready, and the current native participant count is already
full. Only an actual explicit press of that same Cancel can pass the native
`autoValidateUnreadying:true` option. Original InputToggle and, when configured,
its asynchronous progress completion retain this one-shot consent. Cancelled
progress, Initialize/Reset, controller/quest/phase/session changes and native
PointOfNoReturn end authorization. Arbitrary `ReadyUp(false)` callers remain
blocked, including while that progress animation runs. The significant actual
withdrawal emits one normal-level note; no routine roster/frame traces are added.

The marker measures the post-departure blocked state rather than classifying
the departed player's former participant role. Native Detach has already
released controllables before this event; ready, unready or spectator departures
qualify only when the original handler leaves the exact real Cancel available
in that state. This path never sends an automatic vote. On a Flat host, the user
can withdraw and explicitly Accept again; the original receiver then reevaluates
readiness and starts its own ACK/continuation. No automatic Flat-host progression
from the departure option is claimed.

## Integration requirements outside this lane

- Register `MapQuestReadyUp.Install()` at WorldUI startup, before the first
  native map proposal. The former registration only on the first room Engage
  could miss proposals raised before that activation. Root owns this wiring.
- Select the actually shown native selected/multiplayer quest window for the
  confirm parker. The two native roots can share the QuestPopup ID and cannot
  be distinguished by that ID or client/host role alone. Root owns Modal/window
  routing and multiplayer presentation authority.
- Derive the effective quest decision from native host proposal/local native
  quest state as well as mod map-selection records. A Flat host has no mod
  selection broadcast. Initial native proposal observation must not withdraw
  existing readiness; replacement/cancellation may. The selection worker owns
  that observation contract.
- Update patch inventory for two new native click seams and register the new
  focused runtime script in the integrated suite registry. Root owns both.
- Install `MapQuestDepartureValidation` at the same early WorldUI startup and
  register its seven patch classes (ten handlers) in the integrated inventory.
  Root has integrated production checkpoint `910b8ad7d` and owns that wiring.

## Evidence and limits

`check-map-quest-ready-runtime.sh`: 287 assertions, 25 two-through-four-player
Flat/VR membership layouts, fourteen runtime causal controls. The original
prompt/claim proof remains 224 assertions/seven controls; the departure proof
adds 63 assertions/seven controls. It compiles all three full production
classes, ten unchanged native prompt bodies and 22 unchanged native departure
bodies. Departure proof executes original host/client `OnPlayerLeft`, input,
validated ready/unready, native Flat-host `ProxySetReadyState`, controllable ACK
and `Proceed`/`Reset`. Its explicit boundaries are documented in
`tests/map-quest-ready-runtime/README.md`. No automatic acceptance, gameplay
collection write, second handshake or mod packet is introduced.

Separate optional actual HarmonyX 2.7.0 proof under Unity Mono passes 72
assertions: all seven production target registrations, both scoped input and
progress prefix/finalizer pairs, then the same native scenarios with real
patch delivery. These assertions overlap the portable 63; they are not added
to the 287 normal-suite total. Hosted CI can explicitly skip this optional
mode when Mono is unavailable without claiming an actual registration pass.
Worker receipt: `.planning/debug/quest-ready-harmonyx-20261008.log`.

Final strict Release compilation passes with 0 errors/0 warnings. The initial
prompt/claim source checks passed 14/15; the expected remaining patch-inventory
failure required the integrator's generated update for the two click seams.
That receipt predates the seven departure target classes; the integrator owns
their final generated inventory and combined source gate.
Receipt: `.planning/debug/test-runs/20261008-000127-f52e571c/results.json`.
Focused existing suites also pass: map-flow 2,012 assertions/nine causal controls
and map-button 14 assertions/two controls, 2/2 partial scope.
Receipt: `.planning/debug/test-runs/20261008-000258-c5605c07/results.json`.
A mistakenly broad refactor command earlier completed the source group (15/15)
but then started the entire runtime group and was cancelled; no complete-gate
pass is claimed. Its unrelated town-service-mirror
fixture failed compilation on existing missing `SpriteKey`/`ScanPackedSprites`
extraction bindings at this worker base. The primary integrator runs the final
combined checks and records any unrelated base failures separately.

No supplied paired-log build banners or screenshots establish this user's
reported sequence, and no new headset/session result is claimed. This proves
the source causes and their dispatcher repairs. Native real multiplayer host
ACKs, loading, party changes during ready-up, campaign/Guildmaster picture,
and headset usability still need an actual integration hardware test. Native
participant/connecting-player refusals remain visible game rules rather than a
mod-created ready-up bypass.

## Portable native fixture follow-up

The runner compiles ten verbatim native bodies from the committed
`NativeFixture.cs`, so hosted CI runs the same 224 assertions and seven causal
controls without access to the game installation. A main-worktree reference
tree is located through `git rev-parse --git-common-dir`, and every body is
pinned against its read-only native source whenever that tree exists. An absent
tree uses the committed fixture; an existing but changed method fails before
runtime verification. No production source changes accompany this portability
follow-up.

The departure fixture follows the same portable policy. Its default .NET 8
proof needs neither installed game references nor Mono/Harmony packages. The
optional actual registration mode is a separate receipt and uses private build
outputs, preserving the regular CPU harness.

## Guarded native-state follow-up, 2026-10-08

Five departure seams now guard all native state probes. Unavailable or throwing
state cannot grant withdrawal permission or alter the original initialization
argument. Failed probes clear only mod authorization; finalizers return the exact
original input/progress exception. The pure CancelProgress/Reset seams only write
mod fields. The desync ledger records five SELF-GUARDED and two CANNOT-THROW rows.
Actual probe exceptions report once per Initialize/Reset lifetime through the
normal-level Alert; logger exceptions cannot change consent or continuation.

Focused worker execution passed 160 departure assertions (the original 63 plus
97 failure/logger/original-exception checks), all 15 causal production controls
and all 22 native source pins. Twelve native getter boundaries are fault-injected,
including reflection roster/player ID and progress state. Optional actual
HarmonyX 2.7.0 under Unity Mono passed 169 assertions through the production
seven-target registration without manual seam dispatch. Compiler: zero warnings
and errors. The unchanged prompt/claim receipt is 224 assertions, seven controls
and 25 layouts; this follow-up alone is not a new complete integration gate.
