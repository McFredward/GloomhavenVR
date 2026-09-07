# R3 — FLIGHT ANIMATIONS: identity, sense, destination

Base `origin/dev` @ `9f646c86`, ModBuild 479. Read-only review. Every `file:line` below was opened on
2026-09-07 against that commit; game ground truth is `/home/claw/gloomhaven_vr/decompiled/`.

Maintainer's question: *"Fluganimationen: Sind die Fluganimation beim remote board und lokal
identisch? Machen sie an der Stelle Sinn? Gehen sie in den richtigen Pile?"*

The used-card → pile flight is ACCEPTED and is not questioned here. What follows is whether it, and
its 16 siblings, are right.

---

## HEADLINE

The **curve, the duration, the arc height, the rotation, the size ramp and the anchors** of every
mirrored *pile* flight are genuinely 1:1 with `VRCard.FlyToPile` — the 2026-09-07 `RemoteFlightCurve`
consolidation landed and I could not falsify it. Destinations are right: no flight lands in a pile
the game's model does not name.

The defects are **not in the arcs**. They are in (1) **what the flying and settled card is WEARING** —
two independent expressions read a game field that the burn paths this project cares about most never
write, and a third maps the game's own char to the grey — and (2) **two transitions that animate on
the owner's board and pop on every mirror**.

---

## FINDINGS, ranked

### F1 — CONFIRMED. `LostMode` is the CHAR, and the mod maps it to the GREY. Reachable this round.

`src/GloomhavenVR/Net/Remote/UsedCardLook.cs:145-148` collapses two of the game's three FX tasks:

```csharp
if (fx.HasEffect(CardEffects.FXTask.BurnCard))   return CardFxLook.Burn;
if (fx.HasEffect(CardEffects.FXTask.DiscardMode)
    || fx.HasEffect(CardEffects.FXTask.LostMode)) return CardFxLook.Ghost;
```

The game dispatches those tasks at `decompiled/GH.Runtime/CardEffects.cs:417-428`:

```
419:  case FXTask.DiscardMode: GhostOutOn(ghostAnim: true);                 break;   // the grey
422:  case FXTask.LostMode:    BurnCard(burnAnim: true, playOnDisabled);    break;   // the CHAR
425:  case FXTask.BurnCard:    BurnCard(burnAnim: true, playOnDisabled);    break;   // the CHAR
```

`LostMode` and `BurnCard` are the *same call*. `DiscardMode` is the odd one out. The doc block one
line above the mapping asserts the opposite and **cites the exact lines that falsify it**
(`UsedCardLook.cs:121-125`: *"`DiscardMode` and `LostMode` both run `GhostOutOnTimeline`
(CardEffects.cs:422-423)"* — 422-423 is the `LostMode → BurnCard` arm).

This is not the rare path, it is the **ordinary** one. `FullAbilityCard.SetPile` raises `LostMode`,
never `BurnCard`, for every card the model routes into a burnt pile
(`decompiled/GH.Runtime/FullAbilityCard.cs:321-324`). The only site in the whole game that raises
`FXTask.BurnCard` is the short rest (`decompiled/GH.Runtime/CardsHandUI.cs:955`). So a short-rest
sacrifice is the one burn the mod gets right, and every other burn is mapped to the grey.

Consumer: `src/GloomhavenVR/Net/Remote/RemoteHandFan.cs:5173` drives the mirrored hand-fan card's
look off `FromWidget`, ramped over `UsedCardLook.RampSeconds` (2 s) at `:5185-5190`.

**FAILURE SCENARIO.** A peer plays a card carrying a "lost" symbol, or burns a hand card to negate
damage. On his own headset the game chars the card warm brown-black over 2 s
(`BurnCardTimeline`, `CardEffects.cs:508-619`). On the maintainer's headset the same card in that
peer's mirrored hand fan washes **cold blue-grey** over the same 2 s. Same card, same instant, two
different burns. 1:1 covers ANIMATION; this is the wrong animation, played correctly.

### F2 — CONFIRMED. A damage-negation burn never writes `CurrentCardPile`, and three mirrored surfaces read exactly that field. Reachable this round.

`Cards/Art/BurnLookPolicy.cs:441-467` (`ForCard`) switches on `card.CurrentCardPile`, and
`:317-328` (`IsLost`) reads the same field.

The damage-negation burn does not go through the writer:

```
decompiled/.../GameState.cs:1503-1507  Lose1HandCardToAvoidAttack     -> MoveAbilityCard(Hand      -> Lost)
decompiled/.../GameState.cs:1509-1515  Lose2DiscardCardsToAvoidAttack -> MoveAbilityCard(Discarded -> Lost)
```

`CCharacterClass.MoveAbilityCard` (`decompiled/.../CCharacterClass.cs:273-309`) moves the card
between two `List<CAbilityCard>` and **never touches `CurrentCardPile`**. Grepped: the only writers
of that property in the whole rule library are `MoveAbilityCardToPile` (`:452`),
`RestoreCachedAugmentOrSongAbilityCard` (`:548`) and `Reset` (`:1198`, `:1207`). The
avoid-damage paths call `MoveAbilityCard` **directly**, bypassing `MoveAbilityCardToPile` entirely.

So after a damage-negation burn the field is stale: `Hand` for the 1-hand-card variant (last written
by `Reset:1198`), `Discarded` for the 2-discard variant.

Consumers, all via `UsedCardLook.FromState` → `BurnLookPolicy.ForCard`:
* `Net/Remote/RemoteCardFx.cs:637` — the look on the **flying slab**;
* `Net/Remote/RemotePileFronts.cs:726` — the look in the **mirrored burnt-pile fan**;
* `Net/Remote/RemoteHeldCardFace.cs:725` — the look on a **card in the peer's fist**.

**FAILURE SCENARIO.** A peer burns a hand card to avoid damage. `ForCard` reads `Hand`, falls to
`default:`, returns `Look.None`. The maintainer watches a **pristine, unmarked** card arc into that
peer's burnt stack and then lie in the burnt fan pristine — while its owner's own card is charred by
the game's own `LostMode` timeline. In the 2-discard variant it reads `Discarded` → `Look.Ghost` →
grey, not charred. This is the exact picture behind the standing ruling *"Beim Verbrennen EGAL AUS
WELCHEM GRUND muss die Karte immer mit der Vorderseite sichtbar sein"* — the FACE is correct here
(`RevealGate.IsPubliclyRevealedCard` walks `LostAbilityCards`, a **list**, so it is immune), but the
LOOK is not.

Second-order, same root: `BurnLookPolicy.IsLost` is false for these cards, so `EnforceLost` — the
policy's own **rule 2, "a lost card is fully burnt"** — never runs on the *owner's* board either.

### F3 — CONFIRMED. Hand → round recess is a glide locally and a pop on every mirror. Reachable every turn.

Owner: `Cards/Tray/PlayTray.4.Slots.cs:1193-1209` (`PlaceCard`, `instant` defaults **false**) →
`VRCard.SetHome` (`Cards/VRCard.cs:879-887`, reparents `worldPositionStays:true`) → the exponential
home glide at `Cards/VRCard.cs:2291` (`t = 1 - exp(-CardLerpSpeed·dt)`, default 14/s). The card
visibly sails out of the fan into the recess. `PlacePickCard` (`:1222-1240`) is the same call.

Mirror: `Net/Remote/RemoteBoardCard.cs:153-159` writes `localPosition` **once, in the constructor**.
The recess slabs never move — a card "arriving" is `Set(card, front, actor)`, a content change on a
stationary slab. The only caller of `RemoteBoardCard.Move` in the entire tree is
`RemoteActiveCards.cs:579`.

There is **no wire event for it**, and there never has been. I enumerated every announcement site —
7 `ReportCardFx` (`6.Flows:3590`, `5.Interactions:1490`, `4.Rebuild:2692/:2999/:3337/:3915/:3965`)
plus 2 bare `NetCardFx.Report` (`5.Interactions:1731/:1853`) — and **not one has `from ==
CardFxAnchor.HandFan`**. Consequently `RemoteCardFx.ResolveFace`'s `from == HandFan` arm
(`RemoteCardFx.cs:426-428`) is unreachable code: the receiver was built for a flight no sender emits.

**FAILURE SCENARIO.** Every single card the maintainer's team-mates play. They watch their card fly
from their hand into the recess; he watches it blink into existence there. This is the most
frequently-executed 1:1 breach in the card stack.

### F4 — CONFIRMED. The active column's *resident* re-centre glide has no mirror. Reachable this round.

The arrival is now correct on both sides and I want to say so plainly, because the ledger's own
table (`CardFlightLedger.cs:163-168`) reads as though it were still open: `LaunchActiveFlights`
(`6.Flows:3565-3655`) flies the card AND announces it (`ReportCardFx(origin, Active)` at `:3590`),
`RemoteCardFx` flies the mirror to the re-resolved **cell** (`RemoteCardFx.cs:714-716`), and
`ActivePileViewer` seats arrivals instantly on both ends (`ActivePileViewer.cs:296-299`). Closed.

What is not closed is the other half of that same line. `ActivePileViewer.cs:296-299`:

```csharp
bool arriving = _seated.Add(card);
card.SetHome(_root, pos, Quaternion.identity, cardScale, instant || arriving);
```

`arriving == false` ⇒ `instant:false` ⇒ the **resident glides**. The mirror's counterpart is
`RemoteActiveCards.cs:578-579` → `RemoteBoardCard.cs:220`, a bare `localPosition` write, on the
0.25 s content cadence.

**FAILURE SCENARIO.** A peer activates a SECOND persistent card. On his board the first card slides
smoothly sideways to make room for it. On the maintainer's mirror it jumps to its new cell, up to
one cadence tick late. Owner glides, viewer snaps — the same shape as the row-10 defect this build
fixed for arrivals, still standing for residents.

### F5 — CONFIRMED (mechanism) / PLAUSIBLE (picture). `RemoteBurnFx` claim tokens are fungible and can leak.

`Net/Remote/RemoteBurnFx.cs:273` — `private int _claims;`, a bare counter. `ConsumesWireEvent`
(`:302-341`) tests only `NetCardFx.To(endpoints) != CardFxAnchor.Burnt` and then decrements. **No
term binds a token to a card**; `From` is never inspected. The doc at `:268-272` claims *"each
presented burn eats exactly its own event, and an unpresented one always falls through"* — nothing in
the code makes that true.

Two ways a token outlives its event:
* `Acquire`'s recycle-at-cap path (`:1027-1039`) discards a presentation that may never have handed
  over and **does not decrement `_claims`** — verified, the block does `HideFront` / `HasFace=false`
  / `SetFrontFace` and returns.
* The stale-token sweep (`:333-337`) is gated on `!AnyHoldPending()`, and since the 2.0 s ceiling was
  retired a hold has **no upper bound** — so while any burn is holding, no token can ever expire.

**FAILURE SCENARIO (plausible).** The owner's `→ Burnt` extras event for burn A is dropped — the
stream is unreliable by contract and this mod's own `CARD FX LOST` line measured 1 loss in 4 this
session. A's token is stranded. Burn B is then skipped by `Present` (`:490-494`, viewer's board
gate). B's event arrives, the stranded token eats it, and `RemoteBurnFx` draws nothing for B because
it never presented it: **B's burn flight is missing entirely** on that viewer. I rank this PLAUSIBLE
rather than CONFIRMED because I cannot establish the arrival ordering off the source alone; the
falsifier is a `BURN MIRROR SKIPPED` line and a `CARD FX LOST` count in the same window.

### F6 — CONFIRMED. `RemotePileFronts._wearsBack` is seeded against a premise one caller breaks.

`Net/Remote/RemotePileFronts.cs:146-147` seeds `_wearsBack = true` *"because that is what
`RemoteBrowserFan` and `RemoteItemFan` build their slabs with."* `RemoteBrowserFan` does destroy all
its slabs (`RemoteBrowserFan.cs:1338-1345`), so the premise holds there. `RemoteItemFan` does **not**:
`RemoteItemFan.cs:1799-1806` deliberately lifts the detached clip "survivor" out of the destroy
sweep, re-inserts it at `:1862-1883`, and only then calls `_fronts.Rebuild(...)` at `:1889`.

The survivor may be wearing the edge/front material from a previous `SetFrontFace(i,
showsBack:false)`. `RemoteCardArt.Destroy` undoes the *mesh* hosting but not that *material* write.
The edge gate at `:376-379` (`if (_wearsBack[index] == showsBack) return;`) then makes every later
"show a back" a no-op for that index.

**FAILURE SCENARIO.** A peer's item arc re-lays out while the clip chip is detached; the surviving
chip keeps a card-edge frame around what should be a plain back on every `ShowBacksEverywhere` path
(`:402-404`, `:526-527`, `:554-557`, `:613-615`) — the inverse of the user-item-10 defect this field
exists to prevent. Narrow (the item population is phase-exempt), but real.

### F7 — CONFIRMED (mechanism), cosmetic. `RemoteBurnFx` freezes its arc height at discovery.

`RemoteBurnFx.cs:549` computes `b.Arc` once, inside `Present`. `b.From`, `b.To`, `DrawnBoardScale`
and the size ramp are all re-read per frame (`:699-705`, `:927-928`); `b.Arc` is not. With the hold
ceiling retired, discovery can precede launch by a whole turn. The owner's `FlyToPile` computes his
arc **at launch** (`CardsDriver.4.Rebuild.cs:2986`).

**FAILURE SCENARIO.** A peer drags or zooms his board during the hold. His own burn arcs at the
height his board is now; the mirror arcs at the height it was a turn ago. No instrument can see it —
the `BURN FLIGHT CURVE` line prints `b.Arc`, so the log agrees with itself.

---

## THE FLIGHT-BY-FLIGHT TABLE

Curve/duration for every row marked "same" is: `VRCard.SmootherStep` on the chord AND on the
`VRCard.FlyArcOffset` bow AND on the scale ramp; `FlyToPileSeconds` = `NetProtocol.CardFxSeconds` =
0.4 s; arc `max(boardScale·CardHeight·1.5, dist·0.55)`; rotation locked to the owner's board.
Mirrors reach the identical functions through `Net/Remote/RemoteFlightCurve.cs`.

| # | transition | LOCAL producer | MIRROR | same? | lands where? |
|---|---|---|---|---|---|
| 1 | round recess → Discard/Burnt (turn clear) | `4.Rebuild:2696` | `RemoteCardFx` via `ReportCardFx@2692` | **yes** | `DiscardedAbilityCards` / `LostAbilityCards` — correct |
| 2 | recess → Burnt (park sweep) | `4.Rebuild:3003` | `RemoteBurnFx` or `RemoteCardFx` @`:2999` | **yes** | `LostAbilityCards` — correct |
| 3 | recess → Burnt (pile watcher) | `4.Rebuild:3918` | ditto @`:3915` | **yes** | `LostAbilityCards` — correct |
| 4 | → Burnt, no live card (slab fallback) | `4.Rebuild:3956` | ditto @`:3965`, origin `Board` | duration/curve yes; **origin degrades to board centre** | `LostAbilityCards` — correct |
| 5 | pick field → Discard (tray CONFIRM) | `5.Interactions:1493` | `RemoteCardFx` @`:1490` | **yes** | `DiscardedAbilityCards` — correct |
| 6 | Discard → Slot0 (sacrifice presented) | `5.Interactions:1728` | `RemoteCardFx` @`:1731` | **yes** | card is still IN `DiscardedAbilityCards` — correct |
| 7 | Slot0 → Discard (sacrifice redraw) | `5.Interactions:1856` | `RemoteCardFx` @`:1853` | **yes** | still `DiscardedAbilityCards` — correct |
| 8 | Discard → hand (pick restart cancel) | `4.Rebuild:3336` | `RemoteCardFx` @`:3337` | **yes** | `HandAbilityCards` — correct |
| 9 | recess → active column | `6.Flows:3581` | `RemoteCardFx` @`:3590`, cell re-resolved per frame | **yes** | `m_ActivatedCards` — correct |
| 10 | active column re-centre (residents) | `ActivePileViewer:296-299` glide | `RemoteActiveCards:578-579` **snap** | **NO — F4** | n/a (layout) |
| 11 | card leaves the active column | burn/discard flight | mirror cell `Set(null)`, one frame | **NO** — vanishes, no arc | `LostAbilityCards`/`DiscardedAbilityCards` — correct |
| 12 | browse arc → its stack (collapse) | `6.Flows:3011` | `RemoteBrowserFan.BeginCollapse` (state-driven) | **yes** | the browsed pile — correct |
| 13 | pile loan released, arc shut | `6.Flows:3083` | **none** — declined at `6.Flows:3058-3060` | **NO** — owner arcs, mirror pops | correct pile |
| 14 | stack → browse arc (emerge) | `PileBrowser:283` | `RemoteBrowserFan.BeginEmerge` | **yes** (both `1-exp(-14·dt)`) | n/a |
| 15 | **hand → round recess (dock)** | `PlayTray.4.Slots:1204/:1231` glide | **none — no wire event exists** | **NO — F3** | `RoundAbilityCards` — correct |
| 16 | hand fan open/close/character swap | `CardFan` | `RemoteHandFan.BeginSwap` | **yes** | n/a |
| 17 | item fan open/close/solo | `ItemsPile:5642/:5675` | `RemoteItemFan:994/:1565/:1382` | **yes** | items stack — correct |

**No flight lands in the wrong pile.** `RemoteCardFx.NearestNamedStack` (`:836-859`) is a genuine
independent check — a nearest-neighbour search over the sibling anchors, not an equality test
against the expression that produced the point — and the Discard/Burnt anchors are `PileSpacing`
apart by construction on both ends. Item 4c ("beide Karten gingen in den Verbrannt Stapel") has no
mechanism in the current code; if it recurs, the `AIMED AT` clause on the `PILE FLIGHT` line names it
in one grep.

---

## WHAT I CHECKED AND FOUND CLEAN

* **The curve consolidation is real.** All three mirrored flight surfaces call
  `RemoteFlightCurve.Ease`/`.Pose` — `RemoteCardFx.cs:724-725`, `RemoteBurnFx.cs:920-921`,
  `RemoteBrowserFan.cs:1139/:1146` — which call `VRCard.SmootherStep` / `VRCard.FlyArcOffset`
  directly. I grepped `Net/` for surviving hand-written `t²(3−2t)` and `sin(πt)`: the only hits are
  `RemoteHandFan.cs:5390/:5447`, both weight blends, neither a flight. `scripts/check-mirrors.sh`
  guards ease, bow and rotation for these files.
* **Durations agree.** `FlyToPileSeconds = 0.4f` (`CardsDriver.1.Core.cs:143`),
  `CardFxSeconds = 0.4f` (`NetProtocol.cs:25876`). Both plain `const float`, neither a config key —
  so the brief's "constant on one side, config on the other" case does **not** occur.
* **Arc terms agree term for term.** `ArcFraction 0.55f` (`RemoteCardFx.cs:81`) ==
  `VRCard.FlyArcHeightFraction` (`VRCard.cs:1538`); `MinArcCardHeights 1.5f` (`:85`) ==
  `BoardArcMin`'s 1.5 (`4.Rebuild:3454`); both floors feed the OWNER's synced `CardWidth`.
* **World-up bow on both sides.** `VRCard.cs:1603` hard-sets `_flyArcUp = Vector3.up` and discards
  the caller's board-up; `RemoteCardFx.cs:247` and `RemoteBurnFx.cs:921` pass `Vector3.up`. A tilted
  board cannot lean either arch.
* **Rotation is the owner's, never the viewer's.** `RemoteCardFx.SlabRotation` (`:1025-1026`) is the
  peer's drawn board rotation for faced AND faceless slabs; the `Camera.main` billboard arm is gone
  and the checker fails the build if it returns.
* **`RemoteCardFx`'s pool is clean.** I walked all 12 `Flight` fields against `Play`: `ActiveCardId`
  reset at `:226`, `ToAnchor` `:237`, `From/To/ArcUp/Arc/Elapsed/Active` `:244-254`,
  `FromWidth/ToWidth` `:288-289`, `HasFace` `:424`, `WearsBack` via `:305`. The recycle-at-cap path
  (`:1046-1050`) additionally clears the face. No unreset field.
* **`RemoteBurnFx`'s pool is clean** on the shipped-defect pattern — all 19 `Burn` fields are
  rewritten in `Present` (`:527-592`).
* **The size ramp is right at both ends.** `WidthForAnchor` (`RemoteCardFx.cs:893-925`) ends a pile
  flight at `CardWidth · PileViewer.PileStack.SlabFactor` (0.62), the same product this client
  already draws its mirrored stack at.
* **`RemoteCardPlume` is not a flight** — instantiated at identity on the slab
  (`RemoteCardPlume.cs:170-176`), no travel. Its `[Cards] GameCardParticles` dial ships **false**
  (`Defaults/Defaults.Cards.cs:274`) and suppresses the plume on the OWNER's board too
  (`Compat/CardParticlesOff.cs`), so "no plume anywhere" is parity, not a gap.
* **`Cards/Art/BurnCardFx.cs` is not a flight producer** — it reparents `_smokeEffect` and calls
  `BurnLookPolicy.Enforce`. Confirmed.
* **`Net/NetCardFx.cs` is outbox-only** — packs two nibbles, no animation. Confirmed.
* **The short-rest destination is right, for the reason the brief gives.**
  `CardsHandUI.PerformShortRest` (`decompiled:750-754`) indexes `DiscardedAbilityCards` and removes
  nothing; `PerformFinalShortRest` (`:882-883`) removes only from a throwaway local copy. The model
  move happens later, on the SRL worker thread (`GameState.PlayerShortRested:2615-2619`). Rows 6 and
  7 fly to and from the Discard stack, which is where the card genuinely still is. Correct.

---

## NOTES (no failure scenario, or corrections to the brief)

**N1 — the `Selectable` place-rule is INERT, and I could not reproduce the defect the brief names.**
`RemoteCardFx.cs:504` asks under `PeerCardPopulation.Selectable` where the sibling arrival path
(`DressFace`, `:576`) asks under `BoardPickSeat`. The two differ in exactly one term —
`RevealGate.PileFrontsReach` (`RevealGate.cs:883-885`) refuses the discard-pile exemption to
`BoardPickSeat` and grants it to `Selectable` — so `Selectable` is the wider permission and it *is*
a place rule applied to a card that has left its place. But it cannot leak, because the departure
path can only ever act on `_departedFace[slot]`, and that memory is filled from `_latchedFaces[i]`
(`RemoteControlBoard.cs:2538`), which is written **only under `front`** (`:2745-2749`) and is
explicitly nulled for a sacrifice recess (`:2598`). Every card the gate is re-asked about is a card
the recess already legitimately drew face-up. It is an inconsistency worth fixing for legibility;
I found no state in which it changes a pixel. If last round's lane had a concrete scenario, it is not
derivable from the code as it stands today.

**N2 — the brief's `SetPile(Activated) → RestoreCard()` is NOT unconditional.**
`decompiled/GH.Runtime/FullAbilityCard.cs:313-329`: the three FX arms sit inside
`if (cardPile != newCardPile && cardEffects != null)` at `:315`. `RestoreCard()` fires only on a
**change** into `Hand` or `Activated`, measured against the value `SetPile` itself last received.
`UpdateCard()` (`AbilityCardUI.cs:768-775`) re-passes the *same* `cardType`, so on an unchanged card
it is a complete no-op on the FX layer. Four places in this repo state the unconditional version as
fact — `Cards/Art/BurnLookPolicy.cs:576-580` and `:635-637`, and
`Net/Remote/UsedCardLook.cs` (the ModBuild 479 header block). The guard is a change-detector on the
**UI's** last value, not on the model, and `AbilityCardUI.ToggleHighlight` mutates `cardType` behind
its back (`:1098`, `:1186`) — so the erasure is real but its *timing* is not what those comments say.
The remedies built on it are still needed; the story told about them is wrong.

**N3 — `UsedCardLook`'s "only two writers REMOVE from `toggledEffects`" is half right.**
The removal sites are `ToggleAdditiveEffect(active:false)` (`CardEffects.cs:432`) and `RestoreCard()`
(`:468`) — `:432` is inside `ToggleAdditiveEffect`, not `ToggleEffect`, and the doc names the wrong
method for the right line. It matters: `ToggleEffect` calls `RestoreCard()` **first, always**
(`:362`), so by the time `:432` runs the set is already empty and the `Remove` is dead. `HasEffect`
is therefore reading a single-slot register, not a set.

**N4 — two short-rest sites bypass the read-only-focus wire guard, but are unreachable.**
`5.Interactions:1731` and `:1853` call `Net.NetCardFx.Report` **directly**, skipping
`CardsDriver.ReportCardFx` (`4.Rebuild:3429-3434`) and its `Board.CharacterFocus.ReadOnlyView` early
return. That would announce a Slot0↔Discard flight on our own board while we are only *looking at* a
team-mate. It cannot fire today: `4.Rebuild:1240` reads
`shortRested = pick || readOnly ? null : ...`, so `PresentShortRestCard` — the only caller of
`FlyShortRestCardToDiscard` (`:1679`) — never runs under a focus. A defence-in-depth gap, not a live
defect.

**N5 — `CardFlightLedger`'s citations have rotted.** Five checked, five wrong:
`LaunchActiveFlights` is `6.Flows:3565` (doc says 2745), `StartBrowseCollapse` `:2968` (2162),
`ReturnCardToPile` `:3062` (~2245), `DrainPickReturnFlight` `4.Rebuild:3288` (3075),
`ActivePileViewer.Relayout` `:243` (195). Every method exists and behaves as described. The table's
*substance* held up under audit — it is a genuinely good instrument — but its line numbers should not
be quoted without re-grepping.

**N6 — `RemoteBurnFx`'s pile walk is 12.5 Hz, not 4 Hz.** `WatchSeconds = 0.08f`
(`RemoteBurnFx.cs:135`). The 4 Hz figure in the brief and in `CardFlightLedger.cs:97` is the retired
`RemoteBoardContent.DefaultRefreshSeconds = 0.25f`.

**N7 — the two 0.4 s literals are unbound.** `FlyToPileSeconds` and `CardFxSeconds` are independent
`const float`s in two files. `scripts/check-mirrors.sh` guards the ease, the bow and the rotation,
but nothing guards the DURATION. A retune of one is a silent 1:1 breach.

**N8 — stale comment.** `RemoteBrowserFan.cs:52` still describes its collapse as *"smoothstep along
the chord + a sine bow"*. The code at `:1139-1146` calls `RemoteFlightCurve`. Documentation only.

**N9 — `ActivePileViewer`'s instant-seat introduces a one-frame pose jump.** `SetHome(instant:true)`
reparents with `worldPositionStays:false` (`VRCard.cs:882`), so the world pose jumps until
`_instantNext` is consumed (`VRCard.cs:2281-2288`). Neither `CardsDriver.Update` nor `VRCard.Update`
carries a `DefaultExecutionOrder`, so a card whose `Update` already ran renders one frame at the
garbage pose. Harmless for a *flown* arrival (`FlyFromPile` seeds the transform synchronously,
`VRCard.cs:1682-1684`); it bites only activation from the hand fan / pick field / focus switch.

**N10 — reference retention, not a picture.** `RemoteBoardCard._plumeCard` / `_plumeOwner` (`:312-313`)
are latched by every `Set()` and cleared only by `ResetPlume`, which `RemoteActiveCards` never calls
(`TickPlume`'s only caller is `RemoteControlBoard.cs:2815`). An active cell holds a `CAbilityCard`
and a `CPlayerActor` for every card that has passed through it, until `Blank`/`Destroy`.

---

## WHAT WOULD SETTLE THE OPEN ONES ON HARDWARE

* **F1/F2** need no new instrument: one burn of each kind, and a screenshot of the peer's fan and
  burnt stack beside the owner's. Grep `RECESS CARD FX` and `REMOTE ACTIVE WASH` for the look each
  surface chose.
* **F5** needs `BURN MIRROR SKIPPED` and `CARD FX LOST` counted in the same window, anchored on
  `GloomhavenVR] ` per the tree's standing rule.
* **F3/F4** are visible without instruments — they happen every turn.
