# Why Gloomhaven Digital desynchronises in multiplayer — and what the mod can do

> Evidence-only. Every claim below cites a decompiled file and line. Read this before
> touching anything under `src/GloomhavenVR/Net/`.
> Method note: the finding that matters most (§2) was reached by enumerating **every**
> call site of `FFSNetwork.HandleDesync` — 31 of them — rather than by reasoning about
> what "desync" usually means. It does not mean here what it usually means.

## 0. The headline

**"Desynchronization occurred" is not, in most cases, a report that two clients hold
different game state. It is the game's catch-all for _any exception thrown while a
network message is being applied_ — and a majority of those messages are applied by
dereferencing a UI singleton with no null check.** The detector asserts a cause it
never observed.

There is a second, entirely separate mechanism (§3) that is a pure **impatience timer**:
if the local client cannot reach the phase an incoming action belongs to within
**5 seconds**, the session is declared desynchronised and shut down. Again: no state was
compared.

Both are terminal by design (§6). There is no resync, no retry, no rejoin — one dialog,
one button, main menu. That is why it means "reload".

## 1. The architecture, in one paragraph

`FFSNet` (`decompiled/GH.Runtime/FFSNet/`) is a thin custom layer over Photon Bolt.
Player input becomes a `GameAction` (`Synchronizer.SendGameAction`); a client sends it to
the host, the host processes it and forwards it to the other clients
(`Synchronizer.ForwardGameActionToClients`, Synchronizer.cs:236). Every client then
**executes the same action locally** against its own copy of the rule library. It is
action replication, close to lockstep — not state broadcast. `ActionProcessor` owns a
queue and a phase state machine; an action carries the `ActionPhaseType` it is meant to
be played in, and is only executed when the local phase matches.

## 2. Mechanism A — the receive path IS the presentation layer

`GameAction.Execute()` (GameAction.cs:1047) looks the action up in a static dispatch
table of ~121 entries (GameAction.cs:11). Counting the receivers in that table:

| receiver | actions dispatched to it |
|---|---|
| `Choreographer.s_Choreographer` | 27 |
| `CardsHandManager.Instance` | 13 |
| `Singleton<MapChoreographer>.Instance` | 11 |
| `NewPartyDisplayUI.PartyDisplay` | 10 |
| `Singleton<UIReadyToggle>.Instance` | 8 |
| 26 further `Singleton<UI…>.Instance` types | 1–4 each |

**60 of ~121 entries dereference `Singleton<T>.Instance` at the dispatch site with no null
check.** And `Singleton<T>` (Singleton.cs) is not lazy:

```csharp
public static T Instance => _instance;          // null before Awake…
protected virtual void Awake()    { _instance = this as T; }
protected virtual void OnDestroy(){ _instance = null; }   // …and null again after
```

So an action that arrives while its target window has not yet Awoken — or has just been
destroyed by a scene change — throws a `NullReferenceException` inside
`ActionProcessor.ProcessAction`. That call sits inside:

```csharp
// ActionProcessor.cs:401-404, the catch of TryProcessNextAction
catch (Exception ex) { FFSNetwork.HandleDesync(ex); return false; }
```

**A missing window is reported to the player as a desynchronisation and ends the
session.** This is the mechanism that best matches the field reports: the failures
cluster at transitions (loadout ↔ map ↔ scenario, window open/close, a player joining),
because that is exactly when a receiver can be null while its actions are in flight.

## 3. Mechanism B — the 5-second phase deadline

`ActionProcessor.TryProcessNextAction`, the branch that fires when the head of the queue
does not belong to the current phase:

```csharp
if (action.TargetPhaseID != (int)currentState.PhaseType && action.TargetPhaseID != 0)
{
    incorrectActionsDetectedCounter++;
    …
    if (incorrectActionsDetectedCounter >= MaxConsecutiveIncorrectActionsAllowed)
        throw new Exception("Error processing action. Timed out after … seconds waiting
                             for a suitable phase to play the action in …");
    return false;
}
incorrectActionsDetectedCounter = 0;
```

The numbers (NetworkManager.cs:37, :42, :124):

```
actionQueueProcessingInterval        = 0.3 s
incorrectActionDetectedTimeOutDuration = 5.0 s
MaxConsecutiveIncorrectActionsAllowed = round(5.0 / 0.3) = 17 retries
```

**Five seconds.** No state is compared. The client is simply given 5 s to arrive at the
phase, and anything that stalls it locally spends that budget: a long animation, an asset
load, a hitch, a modal the local player has not dismissed, a slow frame. `throw` →
the same catch → `HandleDesync`.

Note what the game itself does for exactly one action type — `ConfirmAction` (45):

```csharp
if (action.ActionTypeID == 45 && (ScenarioRuleClient.IsProcessingOrMessagesQueued … ))
{
    Console.LogWarning("Unable to process ConfirmAction until SRL and choreographer are done");
    return false;      // <- returns WITHOUT incrementing the counter
}
```

So the game already has an idiom for "the local client is busy, ask me again" that is
exempt from the deadline. It applies it to one action out of 121.

## 4. Mechanism C — eight Bolt state callbacks with the same shape

`GHNetworkControllable` has eight `On…Changed` handlers (`OnControllerIDChanged`,
`OnLevelChanged`, `OnPerkPointsChanged`, `OnActivePerksChanged`, `OnCardInventoryChanged`,
`OnItemInventoryChanged`, `OnStartingTileChanged`, `OnStartRoundCardsChanged`,
GHNetworkControllable.cs:352–595). Each one wraps presentation calls —
`NewPartyDisplayUI.PartyDisplay.Proxy*`, `CardsHandManager.Instance.GetHand(…)`,
`Choreographer.s_Choreographer.ProxyUpdateCharacterStartingTile(…)`,
`Singleton<UIResetLevelUpWindow>.Instance.MPResetCharacter(…)` — in

```csharp
try { … } catch (Exception ex) { FFSNetwork.HandleDesync(ex); }
```

Same false-positive class as §2, on the state channel instead of the action channel.

## 5. Mechanism D — two ACK timeouts, and one bad log line

**The ready-up handshake** (UIReadyToggle.cs:66–70):

```
stateSyncCheckInterval        = 0.2 s
clientStateSyncTimeoutDuration = 20 s   // client: WaitUntilRevisionsMatch
globalStateSyncTimeoutDuration = 30 s   // host:   WaitForStateSyncBeforeProceeding
```

The client-side wait is:

```csharp
while (!latestRevision.IsSameRevision(targetPlayer) || ActionProcessor.IsProcessing)
```

— so a client stuck **mid-action** for 20 s desyncs even when its state is identical.

**The bad log line** (NetworkCallbacks.cs:182):

```csharp
Console.LogCoreInfo("[[Received GameAction #" + evnt.ActionID + … +
    PlayerRegistry.GetPlayer(evnt.PlayerID).Username + …);   // <- unguarded
ActionProcessor.QueueUpAction(new GameAction(evnt));
```

`PlayerRegistry.GetPlayer(int)` is `AllPlayers.FirstOrDefault(…)` (PlayerRegistry.cs:261)
— it returns **null** for a player who has just left or has not been registered yet. The
NRE aborts the Bolt callback *before* `QueueUpAction`, so **the action is silently
dropped**. Nothing reports it. The queue then never advances past the next action, and
§3 fires ~5 s later with a message that names the wrong action. Racy with join/leave.

## 6. Why it is terminal

```csharp
public static void HandleDesync(Exception ex)          // FFSNetwork.cs:68
{
    if (IsClient) Synchronizer.SendSideAction(GameActionType.ClientDesync, …);
    HasDesynchronized = true;                          // gates EVERY send in Synchronizer
    Console.LogError("ERROR_MULTIPLAYER_00022", …);
    OnDesyncDetected?.Invoke(ex);
    Manager.OnDisconnected?.Invoke(DisconnectionErrorCode.Desynchronization);
    if (Manager.AutoShutdownUponDesynchronization) Shutdown();   // serialized true
}
```

`Shutdown()` disconnects every peer with a `Desynchronization` token and calls
`BoltNetwork.Shutdown()`. `SceneController.ShowDesyncDetectionError` (SceneController.cs:2545)
then shows one `GlobalErrorMessage` with a single **"Main Menu"** button.

There is no partial recovery anywhere in the layer. `SavePointReachedEvent` /
`PlayerRegistry.JoiningPlayers` exist for players joining at a save point, but nothing
routes a desynchronised session into them.

## 7. Ruled out (named so nobody re-chases it)

**RNG divergence.** The scenario RNGs are explicit shared state
(`ScenarioState.ScenarioRNGState`, `EnemyIDRNGState`, `EnemyAbilityCardRNGState`,
`GuidRNGState`), serialized with `BinaryFormatter` and carried in the save
(Extensions.cs:53–76). Genuine divergence there is possible in principle, but it would
surface as **different outcomes**, not as a caught exception — and every mechanism above
is a caught exception or a timer. Do not chase this without an artefact that shows it.

**The modded-ruleset hash checks** (GHClientCallbacks.cs:727–783) are UGC transfer, not
this mod, and produce their own distinct messages.

---

# What the mod can do

## R0 (obligation) — do no harm, and prove it

The mod already patches `Choreographer`, `CardsHandManager`, `NewPartyDisplayUI`,
`UIReadyToggle`, `TakeDamagePanel` and `PingManager` — **the five heaviest receivers in
the §2 table plus two more.** A throw in any of those patch bodies, on a network dispatch,
lands in the same catch and is shown to the player as *the game's* desynchronisation
dialog, with our stack inside it.

So the first measure is a **checker**, not a feature: enumerate every mod patch whose
target type appears in the `GameAction` dispatch table or in `GHNetworkControllable`, and
require each body to carry a recorded verdict. Costs nothing at runtime, and it is the
difference between "the mod is innocent" being a belief and being a measurement.

**DONE, ModBuild 334.** `scripts/check-desync-surface.py` + `docs/NET-ACTION-SURFACE.md`,
wired into `refactor-guard.sh check`. Twelve patch classes sit on a receiver type; a
thirteenth fails the gate until somebody reads the body.

**The measured result, and a correction.** The first pass counted the token `try` inside
each patch body and reported "11 of 12 unguarded". That is not the same question as "can
this body throw into the dispatch", and reading all twelve gives a much smaller answer:
2 already carry their own `try/catch`, 4 do nothing but call `VREvents.Raise` — which has
been guarded from the start, with a doc comment saying exactly why — and 3 are a constant
expression or a single bool write. **Three** genuinely read game state unguarded, and
those are now wrapped in `Net.Desync.DispatchGuard`: `Placement_Click_Diagnostics` (a
*diagnostic* prefix on the heaviest receiver in the game), `CardsHandManager_ShowHands_Patch`
(the one Show postfix that reads `ActivePlayer` before reaching the guarded `Raise`), and
`PartyPanelStackingHide` (a skip prefix; on a throw it returns `true`, so vanilla runs).

Worth recording as method, not just as result: the bad first number came from an
instrument that measured **one term** of what the question asked — the same failure this
document attributes to the game's own desync detector, one directory over.

## R1 — the desync recorder (read-only, event-driven)

`FFSNetwork.OnDesyncDetected` is a **public static multicast delegate**
(`DesyncDetectedEvent`, combined with `Delegate.Combine` in SceneController.cs:578) — the
mod can `+=` to it with **no patch at all**, and the game's own dialog still runs.
(The listener must be totally wrapped: a plain multicast delegate has no per-listener
catch — see the mod's own `a-thrown-listener-amputates-the-chain` lesson.)

On fire, write one self-contained report next to `Player.log`:

- the exception message + stack — and **whether `GloomhavenVR` appears in that stack**
  (the honest classifier: ours or theirs);
- `ActionProcessor.CurrentPhase`, `PreviousState`, `SavedState`, `IsProcessing`;
- the pending `ActionQueue`: each action's type and `TargetPhaseID` vs the current phase
  — this is what tells §3 apart from §2 at a glance;
- `IsHost`, the player list with `HasDesynched`, `PlayerRegistry.JoiningPlayers.Count`;
- the mod's ModBuild and the last N seconds of the mod's own log ring.

Rationale: half the `HandleDesync` sites construct `new Exception("…")` with **no stack at
all**, so for the timer class the game's artefact is a single line. Today a desync leaves
nothing worth reading.

## R2 — buy the local client patience (local-only, cannot affect peers)

```csharp
ActionProcessor.MaxConsecutiveIncorrectActionsAllowed   // public static { get; set; }
```

is settable without a patch. Raising 17 → ~50 turns the §3 deadline from 5 s into ~15 s.
This is **strictly local**: each client evaluates its own counter, the action is still
only executed when the phase matches, and nothing is applied early or twice. The only
cost of being wrong is that a genuinely stuck session takes 15 s instead of 5 s to say so.

VR earns this: the local client is measurably slower than the flat game (the mod's own
board rebuild is ~90 ms, the atomic commit 85 ms), so part of the 5 s budget is being
spent by us.

Caveat: `NetworkManager.OnEnable` recomputes the value from its serialized fields
(NetworkManager.cs:124), so it must be re-applied while online — cheap, one comparison
per tick. Do **not** patch `NetworkManager` (standing ruling); write the property.

## R3 — stop being the churn

Reduce *our* share of that budget: never start heavy mod work (board rebuild, bundle load,
asset instantiation) while `ActionProcessor.ActionQueue.Count > 0` and the head action's
phase does not match. Defer to the next quiet frame. Pure mod-side scheduling, no game
patch, and it directly follows the `we-were-the-churn` lesson: before widening the budget,
check whether the perturbation is us.

## R4 — warn before it dies

A §3 death is preceded by up to 5 s of a climbing counter. From the mod's own tick,
compare `ActionProcessor.CurrentPhase` with `ActionQueue.Peek().TargetPhaseID`; when they
disagree for ~1.5 s, show a small world-space notice ("waiting for the table…"). Two
gains: the player stops fiddling — and fiddling generates more actions, which makes it
worse — and if it does die, they know it was not instantaneous. Read-only observation of
public statics.

## R5 — verify the death is survivable in VR

The desync dialog is a `GlobalErrorMessage`, which the mod's modal fallback already
enrolls (`ModalFallback.10.CatchAll.cs:1484`). A desync fires during a forced shutdown,
often mid-transition — precisely the situation that could otherwise leave a VR player in
an empty room. This is a **verification** obligation against the standing rulings
("es darf niemals leere Fenster geben", "es MUSS immer möglich sein das Optionsmenü zu
öffnen"), not new code.

## NOT doing, and why

**Do not suppress.** Patching `HandleDesync` to swallow, or setting
`AutoShutdownUponDesynchronization = false`, would let a session continue past a *real*
state divergence and write a corrupt campaign save. At the throw site the mod cannot tell
a false positive from a real one, so the only honest lever on that axis is patience (R2),
never suppression.

**Candidate, held back pending evidence:** a pre-dispatch readiness check — before
`ProcessAction`, ask whether the receiver for this `GameActionType` is non-null, and if
not, `return false` (the game's own action-45 idiom, which is exempt from the counter).
It would convert most of §2 from a session kill into a wait. It also changes behaviour in
the network layer and can stall silently if the receiver never appears. Ship only once
R1's recorder has produced an artefact showing §2 actually occurring in his sessions —
otherwise it is a fix for a mechanism we have only read, and a fix gated behind the
instrument that was meant to test it never runs.

---

## 8. Mechanism E — the SILENT deadlock: no exception, no timer, no report

> Added ModBuild 351 from the ModBuild 348 two-player drop (2026-09-02). This is the
> mechanism that ended that session, and it is **not** any of §2–§5. Nothing threw,
> `HandleDesync` never fired on either machine, `DesyncWatch` printed one line on the
> host and nothing at all on the client, and the players sat there until they quit.

### 8.1 What the two logs say, aligned

`ARMA` (PlayerID 2, client, Barbar/Brute) took damage three times and each time chose to
burn a card to negate it. Actions #38, #39, #40, all `BurnAvailableCard @
TakeDamageConfirmation`.

| | remote (`remote/Player.log`) | local host (`Player.log`) |
|---|---|---|
| damage #1 | `260453 STATE: Halted @ TakeDamageConfirmation`<br>`262913 Switch hand Brute from Brute`<br>`272807 OnLoseCardClick` → #38 sent<br>`273021 STATE: Halted @ NONE` **advanced** | `328260 ProcessFreely @ TakeDamageConfirmation`<br>`343519 Processing #38` → `343523 Halted @ NONE` |
| damage #2 | `273363 STATE: Halted @ TakeDamageConfirmation`<br>**no `Switch hand` line at all**<br>`286699 OnLoseCardClick`<br>`286702 Coroutine couldn't be started because the the game object 'Player handBrute' is inactive!`<br>→ #39 sent, **and no further `STATE:` line for the rest of the session** | `344117 ProcessFreely @ TakeDamageConfirmation`<br>`359338 Processing #39` → `359342 Halted @ NONE` |
| retry | `295889 OnLoseCardClick`, same coroutine line, #40 sent | `369961 Received #40` … `370124 [Net] DESYNC STALL … never cleared` |

The whole difference between the burn that worked and the burn that killed the session is
the single Unity line at `286702` — and the missing `Switch hand` line above it.

### 8.2 The mechanism

`CardsHandUI.OnLoseCardClick` (CardsHandUI.cs:2340-2362) applies the rule change first and
then hands the rest of the flow to a coroutine **on itself**:

```csharp
GameState.Lose1HandCardToAvoidAttack(playerActor, selectedCardsUI[0].AbilityCard);  // card is gone
StartCoroutine(AnimateCardsLost(selectedCardsUI, delegate {
    GameState.PlayerAvoidingDamage(avoidDamageOption2);   // <- the ONLY thing that advances
    Hide();                                               // <- gameObject.SetActive(false)
}));
Synchronizer.SendGameAction(BurnAvailableCard, TakeDamageConfirmation, …);           // wire
…
Singleton<TakeDamagePanel>.Instance.ResetAndHide(stopActiveBonus: true);             // panel closes
```

`Hide()` → `ShowOrHideInternal(false)` → `base.gameObject.SetActive(false)`
(CardsHandUI.cs:460/514). **A successful burn therefore ends with the hand object
inactive**, and `MonoBehaviour.StartCoroutine` on an inactive GameObject is *refused* by
Unity — it logs, it does not throw. So on the next burn:

* the card is already moved to `LostAbilityCards`,
* `BurnAvailableCard` is already on the wire and every peer applies it correctly,
* the take-damage panel closes (`ResetAndHide` runs regardless),
* **`GameState.PlayerAvoidingDamage` never runs**, so the local rule engine stays in
  `TakeDamageConfirmation` for ever.

Every other client then queues its copy of the burn behind a phase that never opens.

### 8.3 Why nothing reported it — the §3 deadline does NOT apply

`ActionProcessor.TryProcessNextAction` (ActionProcessor.cs:279) opens with

```csharp
if (!lockProcessingAction && readyToProcessNextAction && actionQueue.Count > 0 && !processingAction)
```

The phase-mismatch branch that increments `incorrectActionsDetectedCounter` and eventually
throws lives **inside** that `if` (:370-394). A processor in state `Halted` has
`readyToProcessNextAction == false`, so it returns before the counter is ever touched:

> **A stall in the `Halted` state carries no deadline. The 5 s (or the mod's raised)
> budget never starts, `HandleDesync` never fires, and the action waits for ever.**

That is why the host log has exactly one `[Net] DESYNC STALL` line and no
`STALL CLEARED` and no desync: the host sat at `Halted @ NONE` with `BurnAvailableCard #40`
at the head of its queue. The session was already dead 90 seconds before the players
realised it. The host eventually quit; the client's only artefact is the disconnect box
(`Player 1 has left the room`).

### 8.4 Why the FIRST burn worked

In 2D the hand is put back on screen by `TakeDamagePanel.PreviewAvailableCards()` →
`CardsHandManager.Show(actor, LoseCard, …)` → `SwitchHand` → `hand.Show()`
(TakeDamagePanel.cs:530-537, CardsHandManager.cs:645/725). It is reached from the burn
toggle's **click** *or its mere mouse **hover*** (TakeDamagePanel.cs:466-512). Damage #1
went through it — `Switch hand Brute from Brute` is the proof. Damage #2 did not: the
player picked the card straight out of the VR fan.

**The mod is implicated.** `Cards/Patches/DamageFlowPatches.cs`
(`TakeDamagePanel_BurnHover_Skip`, ModBuild ~200) deliberately suppresses the hover half of
that path, because in VR the laser sweeps the docked row constantly and the whole hand
burst open and shut. That leaves exactly one route into `PreviewAvailableCards` in VR — a
deliberate press of the docked burn toggle — and the VR fan does not require it: the
`CardsHandUI`'s `currentMode`/`maxCardsSelected` persist from the previous prompt, so a
pick made without it still "works" right up to the coroutine.

The same missing `CardsHandManager.Show` also explains report item 10: with no
`UpdateView`, the previous prompt's selection was never cleared, which is why the log reads
`Toggle Select, deselecting a card ABILITY_CARD_OverwhelmingAssault` — a card that had
already been burned in damage #1 and was still sitting in `selectedCardsUI`.

### 8.5 What ModBuild 351 does

1. **`BurnCommitRescue`** (`Cards/Patches/DamageFlowPatches.cs`) — the existing
   `CardsHandUI.OnLoseCardClick` prefix now re-activates the hand's GameObject (and any
   inactive ancestor) when it is inactive, so the game's own `StartCoroutine` succeeds.
   Nothing about the pick, the mode, the selection or the card is touched; the game's own
   `Hide()` at the end of `AnimateCardsLost` puts the object back. One `VRLog.Note`
   (`BURN COMMIT RESCUE`, `// HW-VERIFY`) when it fires — which in a healthy flow is never.
2. **`BurnCommitWatch`** — the falsifier. Armed by every legal lose/burn commit, ticked
   from `HandSuppression.Tick`; if the client is still in `TakeDamageConfirmation` 8 s
   later it says so once at `Alert` (`BURN COMMIT HANG`). This is the line that will name
   the cause if the deadlock returns in a shape the rescue does not cover.
3. **`DesyncWatch`** — the `DESYNC STALL` line now prints the processor's own state word
   and distinguishes the two stalls. Through ModBuild 350 it always claimed a deadline;
   in the `Halted` case there is none, and it now says so and repeats (bounded) because a
   terminal stall that prints once is indistinguishable in a log from one that cleared.

### 8.6 Not fixed here — the recommended follow-up

The rescue makes the burn work; it does not restore the vanilla invariant. The clean fix
belongs in the VR pick flow (`Cards/Driver/**`, another lane): when a take-damage burn pick
begins and `CardsHandManager` never opened the burn step, drive the game's own
`Singleton<TakeDamagePanel>.Instance.BurnAvailableCard(true)` first. That runs
`OnSelectedToggle` + `PreviewAvailableCards` exactly as a toggle press does — which also
re-seeds the hand and clears the stale selection (report item 10). It must NOT be done at
commit time: `PreviewAvailableCards` → `UpdateView` would clear the selection the commit is
about to index.
