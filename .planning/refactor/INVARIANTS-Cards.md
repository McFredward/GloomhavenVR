# Invariant registry — `src/GloomhavenVR/Cards/`

> **Last verified 2026-09-08 against `49ceab21` (ModBuild 483).** Corrections carry
> **[verified 2026-09-08]**. Read the STALE SYMBOL INDEX below before trusting any `Where:` line.
>
> Subsystem: ~~25 files, ~23 900 lines~~ **67 files / 70 699 lines at `49ceab21`**. The largest and
> most heavily iterated part of the mod. **The registry has not grown with it**: it was written
> against the 25-file tree, so its silence about a file is not evidence that nothing load-bearing
> lives there. The 2026-09 round added entries for the two biggest uncovered types in
> `.planning/refactor-2026-09/REVIEW-cards.md` §3 (`CardsGameApi`, `CardFan`) rather than here —
> read those there.
>
> **What this document is.** Every entry below is a piece of logic that *looks*
> arbitrary, redundant or trivially simplifiable, and is in fact the residue of a bug
> that took one or more hardware test rounds on a Quest 3 to corner. There are no
> automated tests here and there cannot be: nearly every behaviour is a negotiation
> between a decompiled Unity game, a headset and a human's perception of it. If a
> refactor "cleans up" one of these, the bug comes back and the user has to
> rediscover it on hardware.
>
> **How to use it.** Before touching a symbol named in a **Where:** line, read its
> entry. `Breaks if:` names the specific edit that reintroduces the bug — these are
> written as the *tempting* edit, not a hypothetical one, because in almost every
> case the tempting edit is exactly what the code used to do.
>
> **Symbol names only, never line numbers** — the files are being edited concurrently.
>
> Confidence: **high** = the commit message or a code comment states the cause
> explicitly · **medium** = inferred from the diff or from surrounding code · **low** = guess.

---

## STALE SYMBOL INDEX — **[verified 2026-09-08 against `49ceab21`]**

Every identifier in every `- **Where:**` line of this file (575 distinct) was tested against
`src/`. **Twenty-one no longer exist anywhere in the tree.** They are listed here in one place
because the failure they cause is specific and expensive: a successor greps for the symbol, finds
nothing, and cannot tell whether the invariant was *deleted*, *renamed*, or *never existed*. Three
of these groups were deliberately removed, and one of them must never come back — so "cannot find
it, assume it is gone, re-add it" is exactly the wrong inference.

| gone symbol(s) | what took the job over |
|---|---|
| `CardsDriver.ContactStickyMargin`, `PileBrowser.ContactStickyMargin`, `ItemsPile.ContactStickyMargin`, bare `ContactStickyMargin`, `ContactPalmReach` | **MERGED into `Cards/FanSweep.cs`** — four copies of the tip reach became one. `FanSweep.TipReachMeters` (0.035), `FanSweep.PalmReachMeters` (0.13), `FanSweep.StickyMarginMeters` (0.02). **The mechanism also changed**: tip reach and the sticky margin are now scale-RELATIVE (`FanSweep.ResolveReach`, `FanReach.RelativeSize`, clamped 0.30–2.50), while the palm reach deliberately stays a real-metre constant because it mirrors `ProximityGrabber.ReachMeters` — candidacy must mean exactly "this hand could grab this card". |
| `CardFace.TryCaptureSilhouette`, `CardFace.ReadTexture`, `s_silhouetteTried`, `s_silhouetteAttempts` | `Cards/Art/CardFace.cs`: `TryCapture(RectTransform, CardBodyKind, SilhouetteState)`, `ReadSpriteRegion(Texture, Rect)`, and the per-kind `s_silhouette` array of `SilhouetteState` with `MaxSilhouetteAttempts` / `SilhouetteAttemptInterval`. |
| `PlayTray.WatchReachMeters`, `WatchFindableMeters`, `WatchViewMargin`, `WatchLostDwellSeconds`, `_lostSince` | **DELETED ON A USER RULING (2026-08-03) AND MUST NOT COME BACK.** See the corrected entries in §6 and the DO-NOT-RE-ADD block at the top of `Cards/Tray/PlayTray.2.Watchdog.cs`. |
| `_pinPoseVersion`, `_rigLocalPinValid`, `_rigLocalPinRot` | Moved out of `PlayTray` into the shared **`Core/FollowPinAnchor.cs`**, so the combat log's pin and the board's pin share one cache instead of two copies. `PlayTray` kept only what is its own: the freeze-sentinel announcement, the issue-C move label and the log. |
| `PlayTray.GenericCount`, `DockedLabelProud`, `_capTopLocalY` | The docked-label and generic-cluster geometry was reworked; `GenericClusterButtonSize`'s **cap auto-shrink is gone and `PlayTray.3.Pose.cs` says it "MUST NOT COME BACK"** (a third cluster member used to shrink every cap). |
| `VRCard.SetPopped` | `_popped` still exists and still means the hand-driven proximity pop; the setter is gone (it is written internally). `VRCard.SetLaserHover` / `_laserPopped` are unchanged, and the resting-vs-live distinction §1 protects is intact. |
| `_nextPressTime` | `PlayTray.BoardButton`'s poke cooldown bookkeeping; `ButtonTuning.PokePressCooldownSeconds` is still the dial. |

**What this index does NOT say.** It says nothing about entries whose symbols all still exist —
those were not re-derived, only checked for existence. A live symbol name is not proof the
sentence around it is still true.

## Contents

1. [Laser hit geometry — the resting/live rect split](#1-laser-hit-geometry--the-restinglive-rect-split)
2. [Lift priority and beam clamping](#2-lift-priority-and-beam-clamping)
3. [Hand-contact arbitration](#3-handcontact-arbitration)
4. [Fan geometry and presentation](#4-fan-geometry-and-presentation)
5. [Fan lifecycle and the palm gate](#5-fan-lifecycle-and-the-palm-gate)
6. [Board pose policy and the lost-board watchdog](#6-board-pose-policy-and-the-lostboard-watchdog)
7. [Buttons, presses and keycap geometry](#7-buttons-presses-and-keycap-geometry)
8. [Slots, drops and placement](#8-slots-drops-and-placement)
9. [Rebuild ordering and animation ownership](#9-rebuild-ordering-and-animation-ownership)
10. [Stale-state traps in the game API](#10-stalestate-traps-in-the-game-api)
11. [Modal gating and input blocking](#11-modal-gating-and-input-blocking)
12. [Item cards](#12-item-cards)
13. [Piles and the browse fan](#13-piles-and-the-browse-fan)
14. [Card faces, textures and render order](#14-card-faces-textures-and-render-order)
15. [Patches, suppression and the action queue](#15-patches-suppression-and-the-action-queue)
16. [Config, migrations and diagnostics](#16-config-migrations-and-diagnostics)
17. [Suspected vestigial](#suspected-vestigial)

---

## 1. Laser hit geometry — the resting/live rect split

### Resting rect for laser-driven pops
- **Where:** `VRCard.TryGetRestingLaserRect`, consumed by `CardFan.TryRaycast` (the pick this rule exists for), as the deliberate *second* accepted pose in `CardsDriver.TryHitLiftedCard`, and by two `VRCard` diagnostics (`LogLaserRectDecoupled`, `LogFanColliderFit`)
- **CORRECTED (phase 4):** this entry used to name `PlayTray.TryRaycastCards` as the second consumer. It is not one — `PlayTray.cs` has **zero** `TryGetRestingLaserRect` hits, and `TryRaycastCards` builds its plane from the **live** transform (`Dot(direction, t.forward)`, `t.position`, `InverseTransformPoint`), i.e. literally the construction this entry's `Breaks if:` forbids. `3b86e72` did add the docked twin there; `4b7ac8a` reverted it. The rule below is unchanged and still binds `CardFan.TryRaycast`, whose pop-feedback loop is what it exists for. Note the one place both rects are accepted at once — `TryHitLiftedCard` tries live first and resting second, on purpose, because neither pose alone covers the card mid-raise; that is not a violation of this rule, it is the rule's companion entry below.
- **Rule:** A pick whose *own* hover is what raises the card must intersect the card's **home** rect (`_homePos`/`_homeRot`/`_homeScale` under the current parent), never the live transform.
- **Why:** Positive feedback loop. `VRCard.Update`'s pop lifts a laser-hovered card toward the viewer (−Z) and up (+Y); a plane built from the live transform therefore *moves into the beam the instant the card pops*. Sweeping the beam up the card lifted its plane to meet the beam; sweeping off sideways left the hover latched at the raised height — "an invisible collider above the card". Because a live fan hover gates the board laser off entirely, the adjacent board button became unreachable and the trigger grabbed the card.
- **Established by:** `306e8ea` fix(cards): laser no longer sticks above a raised fan card (docked-card twin added in `3b86e72`, kept when the rest of that commit was reverted)
- **Breaks if:** "Simplifying" either raycaster to `transform.position` / `transform.forward` / `InverseTransformPoint`, which reads as the obvious way to intersect a card.
- **Confidence:** high

### Live rect for hand-driven pops
- **Where:** `VRCard.TryGetLiveLaserRect`, consumed by `CardsDriver.TryHitLiftedCard`
- **Rule:** A pick whose raise comes from somewhere **other** than the beam (the palm proximity highlight lifting a docked card) must intersect the card **where it visibly is**, pop and pop-grow included.
- **Why:** The feedback loop above does not exist on this path, and using the resting rect is then simply wrong — the card is visibly lifted toward the player, so the resting rect sits behind/below it. At a flat beam angle that gap projects far along the view direction, so the ray missed the rect entirely while the reticle sat dead centre on the card ("the laser goes straight through the card").
- **Established by:** `9d2a9b7` fix(cards): lift-priority tests the card where it VISIBLY is, not where it rested
- **Breaks if:** Unifying the two rect helpers into one "correct" version. They are near-identical by design and their difference is the whole point — the doc comments name which callers may use which.
- **Confidence:** high

### Lift priority accepts EITHER rect
- **Where:** `CardsDriver.TryHitLiftedCard` (live **or** resting), `CardsDriver.LiftHitMargin`
- **Rule:** The lift-priority branch tests both poses and widens each rect by `LiftHitMargin` (1.10).
- **Why:** During the raise animation neither pose alone covers the card. The margin exists because a card met edge-on subtends almost nothing, so a few millimetres of controller jitter is the difference between "dead centre" and "off the edge". It is safe here *because the branch already knows which card* — it only asks whether the beam is on it, so a wider rect cannot select a different card.
- **Established by:** `9d2a9b7`
- **Breaks if:** Dropping the margin to 1.0 as an unexplained fudge factor, or testing only one pose "since the live rect supersedes the resting one".
- **Confidence:** high

### Fan raycast accept margin + sticky incumbent
- **Where:** `CardFan.TryRaycast` (`acceptMargin` 1.10, `sticky` parameter)
- **Rule:** Each card's rect is widened ~10 %, and the currently hovered card **wins whenever the ray still touches its rect at all**, even if a neighbour is nearer along the ray.
- **Why:** Two separate hardware findings. (a) The trigger *pull itself* jerks the aim ray off the narrow fan strip — grabs missed "every 2nd–3rd try". (b) Overlapping fan cards plus the pop animation trade the "nearest hit" title mid-animation, so a plain nearest-wins rule flickers the highlight. Nearest-hit still arbitrates overlaps, so widening every card cannot flip the winner.
- **Established by:** `6c90126` (accept margin, T2), `321a619` fix(cards): roll-axis reveal, fan-hand exclusion… (sticky rule)
- **Breaks if:** Removing the sticky parameter as "state the raycaster shouldn't need", or normalising the margin away.
- **Confidence:** high

### Fan collider fits the visible face — no viewer-side apron
- **Where:** `VRCard.SetColliderRegion`
- **Rule:** While fan-seated the `BoxCollider` is `(strip width, full height, _fullColliderSize.z)` **centred at z = 0**. No −Z bias, no depth pad.
- **Why:** The earlier fan strip grew a 3 cm accept apron biased entirely onto the viewer side (`center.z = -0.015`, depth 0.02 → 0.05). Fan cards billboard at the head, so −Z points at the eye; from a steep look-down that eye is *above* the card, and the apron protruded ~3 cm (≈ half a card width) of invisible collider **above** the card's upper half. That volume caught the beam and the proximity highlight, floating the reticle over the card and eating the trigger meant for the board button beside it. Angle-dependent, which is why it survived three rounds.
- **Established by:** `6b36616` fix(cards): drop viewer-side fan collider apron that floated the laser above cards
- **Breaks if:** Re-adding a grab-reach pad here. `ProximityGrabber` already reaches 0.13 m off the collider surface (≈ 2× the card width at this scale), so the pad buys nothing and costs the laser.
- **Confidence:** high

### Collider-write idempotence guard
- **Where:** `VRCard.SetColliderRegion` (the `if (!_dockGrabPad && _box.size == size && _box.center == center) return;` early-out)
- **Rule:** Never assign an unchanged `BoxCollider` size/centre.
- **Why:** The fan re-lays out as the player's gaze moves, so this became a per-frame caller. Assigning an identical size still dirties the physics shape every frame for every fanned card. The `!_dockGrabPad` term is required so the first fan call after a dock still clears the dock flag.
- **Established by:** `ceef661` feat(perf): measure the mod's own frame cost… (perf pass), on top of `9e2b53d`
- **Breaks if:** Removing the `!_dockGrabPad` term as "obviously redundant with the size compare" — a docked card would keep its apron forever after entering a fan.
- **Confidence:** high

### Dock apron: sizes, hysteresis and the neighbour clamp
- **Where:** `VRCard.ResetColliderRegion`, `VRCard.DockPadSideFrac`/`DockPadUpFrac`/`DockPadDownFrac`/`DockPadDepth`/`DockPadStickyScale`, `VRCard.DockSlotPitchLocal`
- **Rule:** A slot-docked card grows an accept apron biased **downward**; while it holds the highlight (`_popped`) the whole apron and the bar-yield core grow ×1.3; the side pads are clamped against the **runtime** slot pitch to 95 % of the half-gap.
- **Why:** Three stacked findings. (1) The exact-fit collider is thin and tray-scaled ~0.5×, so a palm slightly *below* a docked card missed the proximity highlight and the trigger fell through to board actions. (2) The first apron (below +1.0×H, sides/above +0.35×H, depth 0.06) swallowed the tray's grab bar and caused accidental card grabs — hence the shrink to 0.10/0.15/0.35/0.04. (3) With zero hysteresis, palm jitter at the zone edge flickered the highlight off, and a trigger in a flicker-off frame fell through to board actions ("grabbing slightly off does nothing").
- **Established by:** `963bb96` (apron), `c9e01c5` (shrink + bar yield), `1a9b071` (hysteresis + pitch clamp)
- **Breaks if:** Rounding the three fractions to one value, making the sticky scale unconditional, or replacing `DockSlotPitchLocal()` with a constant — the clamp reads the *live* slot transforms precisely so it tracks every board scale and per-board config.
- **Confidence:** high

### `AllowsHand` yields to the tray grab bar
- **Where:** `VRCard.AllowsHand`, `VRCard.PalmClearlyAtTrayBar`, `PlayTray.HandleZone`
- **Rule:** An apron-extended docked card refuses a palm that is inside the tray handle's grab zone but outside the card's *core* box.
- **Why:** Belt-and-braces beside the apron shrink: `ProximityGrabber`'s nearest-wins pick could otherwise let an apron-extended card steal a palm clearly placed at the bar.
- **Established by:** `c9e01c5` fix(cards): shrink docked-card grab apron; tray grab bar beats apron overlap
- **Breaks if:** Deleting it as redundant with the smaller apron — it is deliberately a second, independent guard.
- **Confidence:** high

### `IsRooted` — a card that refuses the grab must not promise one
- **Where:** `VRCard.IsRooted` (`!IsHeld && !CanGrab`), consumed in `VRCard.UpdateBody` and `VRCard.OnPokeEnter` — **all three references live inside `VRCard.cs`**
- **CORRECTED (phase 4):** this entry used to add `CardsDriver.ScoreContact` and `CardsDriver.UpdateBoardLaser` as consumers, and to say `VRCard.OnPoke` where the use is `OnPokeEnter`. Those two driver sites inline `!card.CanGrab`, which is a **different predicate for a held card** (`IsRooted` is false while held, `!CanGrab` is true). The rule's substance is unaffected; do not "unify" the driver's `!CanGrab` tests with `IsRooted` on the strength of the old `Where:` line.
- **Rule:** A rooted card produces **zero** pop, scale change and haptic from *every* hover source — but the laser beam still clamps to it.
- **Why:** Every hover-pop is a grab-affordance promise. Popping a card that refuses the grab set up a pop↔drop oscillation the user reported as "the card pulses and vibrates constantly". The beam clamp stays so the trigger cannot fall through to a board click *behind* the card.
- **Established by:** `bce9a63` fix(interact/cards): … rooted-card hover gate + face hover FX neutralization (bug B)
- **Breaks if:** Merging the rooted branch into the normal hover branch, or returning early without the `UiHitOverride`.
- **Confidence:** high

### Game-side hover oscillator neutralised at adopt
- **Where:** `VRCard.NeutralizeFaceHoverFx` / `VRCard.RestoreFaceHoverFx`
- **Rule:** On adopting a face, the game's `ExtendedButtons` `highlightScaleFactor` is set to 1 and `hoverMovement` to 0; originals are restored on detach.
- **Why:** The game's pointer-enter LeanTween scales and shifts the button rect. Under the VR synthesized pointers the animated rect slides out from under the *stationary* pointer: enter → grow → exit → shrink → re-enter, with a haptic tick per change. This is the game-side half of the same "pulses and vibrates" report; the mod-side half is `IsRooted`.
- **Established by:** `bce9a63`
- **Breaks if:** Treating it as cosmetic tuning of the game's UI and dropping it. Both halves are needed; neither alone stops the oscillation.
- **Confidence:** high

---

## 2. Lift priority and beam clamping

### `SuppressFarClick` is not `UiHitOverride`
- **Where:** `CardsDriver.UpdateFanLaser` (rescue branch), `CardsDriver.UpdateBoardLaser` (lift fallback) — both call `dom.Ray.SuppressFarClick()`
- **Rule:** Claiming the trigger for the frame and moving the beam are **separate duties**. A branch that only wants the trigger must never publish a hit point.
- **Why:** The beam does not end *at* a `Ray.UiHitOverride` point — it ends at that point's **projection onto the aim ray** (`RayInteractor.UpdateVisuals`, deliberately, so a clamp can never bend the beam). Publishing an object's *centre* therefore parks the reticle on the plane through that centre **perpendicular to the beam**, at every aim direction: "an invisible wall drawn orthogonally through the MIDDLE of the card, and at too flat an angle the laser collides with that wall instead of with the card". Three rounds of collider surgery chased the wrong object because of this.
- **Established by:** `4330504` fix(cards): the phantom "wall through the middle of the card" was a beam clamp, not a collider
- **Breaks if:** Setting `UiHitOverride = card.transform.position` "so the beam telegraphs the winner" — the single most natural-looking edit in this file, and the exact bug.
- **Confidence:** high

### Lift priority pre-empts only on a real hit; the accept is a bail-out fallback
- **Where:** `CardsDriver.UpdateBoardLaser`
- **Rule:** A palm-highlighted docked card pre-empts the frame **only** when the ray genuinely crosses it (live or resting rect, `LiftHitMargin`). Otherwise the normal element scan runs first, and the lift accept applies as a fallback at the "hit nothing at all" bail-out — suppress-only, never a fabricated hit point.
- **Why:** The branch used to own the whole frame whenever a docked card was palm-highlighted, beam on the card or not: it cleared the board hover and returned before the element scan ran. The palm stays inside a docked card's grab volume for a long time (dock apron + `ProximityGrabber`'s 0.13 m reach + its sticky hysteresis), so the card kept the frame long after the beam had left it — "after grazing a raised card, sweeping the SAME flat beam onto the board buttons passed straight through them, and only ever after having been on a raised card". A near-miss deserves the card; a beam sitting squarely on a button is a deliberate aim and must win.
- **Established by:** `a546586` fix(cards): a lifted card no longer starves the board buttons beside it
- **Breaks if:** Hoisting the fallback back above the scan for readability, or restoring the unconditional pre-empt.
- **Confidence:** high

### Fan grab: proximity winner + 0.15 s hover grace, both under the uGUI test
- **Where:** `CardsDriver.UpdateFanLaser`, `CardsDriver.FanHoverGraceSeconds`
- **Rule:** Three independent ways to own the trigger on a fan card — the beam is on it, the proximity highlight is on it, or it was the last laser-hovered card within `FanHoverGraceSeconds` (0.15 s, **unscaled**). All of them yield to a live `RayUgui` hit.
- **Why:** The trigger pull jerks the aim ray off the narrow strip on the press frame, and the proximity fallback was itself deferred by the mod's own fan clamp raising `Ray.HasFreshUiHit` — so the proximity path never fired. Unscaled time because the card-selection phase pauses `timeScale`. The uGUI gate exists so a UI click can never double-fire with a grab.
- **AMENDED (fan double-highlight fix):** the first two of the three are now selected by the fan hover-owner arbitration rather than by "did the ray miss" — the beam owns the trigger only on frames it owns the *highlight*, and the proximity path is now reached whenever the HAND owns it (which includes, but is wider than, the old "ray missed the strip" condition). The grace is unchanged and still ranks below the proximity winner. See *The fan has exactly ONE hover owner* in §3.
- **Established by:** `6c90126` (T2, reliable fan grabs)
- **Breaks if:** Deleting the grace as "redundant with the proximity rescue", or switching to `Time.time`.
- **Confidence:** high

### Fan hover split resolves against `CardFan.Cards`, not the driver buffer
- **Where:** `CardsDriver.FanIndexOf` / `CardsDriver.UpdateFanHoverSplit`
- **Rule:** The hovered index is looked up in `CardFan.Cards` — the exact list `CardFan.SetHovered` indexes into.
- **Why:** After a mid-frame grab/return the driver's `_fanBuffer` and the fan's own list can disagree, and the split then opens around the wrong card.
- **Established by:** `91a1193` feat(cards): G6 — feed the fan hover split from finger proximity too
- **Breaks if:** Using `_fanBuffer.IndexOf` because it is already in hand.
- **Confidence:** high

### Laser wins the split; proximity only fills in
- **Where:** `CardsDriver.UpdateFanHoverSplit`
- **Rule:** Precedence is laser-first; the proximity highlight drives the split only when the laser hovers nothing.
- **Why:** The laser is the primary controller path. A symmetric "whichever is closer" rule makes the split jump between two sources as the hand passes the fan.
- **Established by:** `91a1193`
- **Breaks if:** Making the two sources equal partners.
- **Confidence:** medium

---

## 3. Hand-contact arbitration

### Exactly one winner per hand per tick, re-derived from scratch
- **Where:** `CardsDriver.UpdateHandContactArbitration`, `CardsDriver.SuppressContactLoser`, `VRCard.SetHandPopSuppressed`, `VRCard.AllowsHand`
- **Rule:** Among all cards in contact range of the free hand exactly one wins; every loser is pop-suppressed *and* refuses the hand in `AllowsHand`, so the `ProximityGrabber` highlight — and therefore the trigger grab — follows the same winner. The suppression set is cleared and refilled every tick.
- **Why:** A physical fan sweep used to lift several cards at once. Rebuilding the set each tick is stale-flag proof: a card that left the pools mid-frame cannot keep a permanent suppression.
- **Established by:** `1a9b071` fix(cards): fan single-lift arbitration, dock grab hysteresis, board never jumps
- **Breaks if:** Maintaining the set incrementally as an optimisation.
- **Confidence:** high

### Candidacy by palm, ranking by fingertip — for fan cards only
- **Where:** `CardsDriver.ScoreContact`, `CardsDriver.ContactTipReach` (0.035), `ContactPalmReach` (0.13)
- **Rule:** Palm reach *qualifies* a fan card as a candidate; the winner among candidates is ranked by **index-tip distance alone**. Dock/pick-field cards keep the legacy `min(tip, palm)` metric.
- **Why:** With the hand exactly between two cards, the wide 13 cm palm reach to two adjacent cards is near-equal and noisy, so mixing it into the winner choice let the lift flip-flop even though the index tip clearly favoured one card. The dock pool has no such midpoint case.
- **Established by:** `196a6c2` fix(cards): rank fan-sweep contact winner by index-fingertip distance
- **Breaks if:** Unifying the two pools onto one metric — the asymmetry is the fix.
- **Confidence:** high

### Incumbent hysteresis, scaled by world scale
- **Where:** `CardsDriver.ContactStickyMargin` (0.02 m × `WorldScale`), also `PileBrowser.ContactStickyMargin`, `ItemsPile.ContactStickyMargin`
- **Rule:** A rival must be this much *closer* than the incumbent to steal the lift, and the margin is multiplied by the diorama scale.
- **Why:** Without it the winner flutters at strip boundaries during a sweep. Without the scale factor an unscaled 2 cm is effectively zero at the ~20× diorama scale the hands live in.
- **Established by:** `1a9b071`
- **Breaks if:** Making it a plain "closest wins", or dropping the `* scale`.
- **Confidence:** high

### Arbitration runs after the laser paths; the laser-hovered card is exempt
- **Where:** `CardsDriver.TickInteractionsAndStatus` (call order), `CardsDriver.UpdateHandContactArbitration` (`_laserHover` / `_trayCardHover` exemption)
- **Rule:** Contact arbitration is invoked **after** the laser updates, and never suppresses the card the laser hovers this frame.
- **Why:** The laser path arbitrates itself and its pluck must keep working. Running arbitration first would let a palm near a neighbour suppress the card the player is aiming at.
- **Established by:** `1a9b071`
- **Breaks if:** Reordering the tick calls "since they are all independent".
- **Confidence:** high

### The fan has exactly ONE hover owner — sticky, first-engaged
- **Where:** `CardsDriver._fanHoverOwner` / `CardsDriver.ResolveFanHoverOwner` / `CardsDriver.HandOwnedFanCard`, consumed by `CardsDriver.UpdateFanLaser` (the non-owner branch) and by `CardsDriver.UpdateHandContactArbitration` (`fanWinner`)
- **Rule:** The laser and the reaching hand are arbitrated **against each other**, not just each against itself. Whichever engaged first owns the fan's single highlight until *it* disengages; the loser raises no pop **and** does not own the trigger. Both sources on the same card is not a conflict (the laser takes it, so the beam clamps to the card the trigger will grab); a fresh engagement with both live in one frame goes to the laser.
- **Why:** The two pop sources were independent by construction — `VRCard`'s pop is an OR of `_laserPopped` and the hand's `_popped`/`_pokeHover`, and the contact arbitration only ranked hand candidates against each other while *exempting* the laser card. Pulling a card out of the fan therefore lit two cards at once, and the trigger silently belonged to the laser's card (`Ray.HasFreshUiHit` defers `ProximityGrabber`), so the fan promised two things and honoured the one the player was not reaching for. Sticky rather than "laser always wins" because reaching in is exactly when the same controller's ray also sweeps the fan — an unconditional priority makes the highlight jump between sources as the hand moves, and flapping is worse than either choice.
- **Established by:** the fan double-highlight fix (laser vs. hand single-owner arbitration)
- **Breaks if:** Giving one source unconditional priority "to simplify"; re-electing the owner from scratch every frame (that *is* the flapping); or reading `Grabber.Highlighted` alone instead of `_handContactWinner` in `HandOwnedFanCard` — while the laser owns, every fan card refuses the hand in `AllowsHand`, so the highlight is null and the laser→hand handoff would cost a re-acquire frame with no highlight at all.
- **Confidence:** high

### Non-grabbable cards cannot win
- **Where:** `CardsDriver.ScoreContact` (`if (!card.CanGrab) return;`)
- **Rule:** A rooted card is not a candidate at all.
- **Why:** Otherwise a dead card wins the arbitration and suppresses the lift of a real candidate right next to it.
- **Established by:** `bce9a63`
- **Breaks if:** Treating `CanGrab` as a grab-time-only concern.
- **Confidence:** high

### The fan-owning hand is excluded from everything
- **Where:** `VRCard.InteractionBlockedHand`, `CardFan.UpdateFingertipHover` (`!ReferenceEquals(dom, _hand)`), `CardsDriver.UpdatePalmGate`
- **Rule:** The hand the fan sits on never hovers, highlights, grabs or pokes a card.
- **Why:** Its palm and fingers sit *inside* the fan; its own proximity hover made two cards flip-flop highlights forever.
- **Established by:** `321a619` fix(cards): roll-axis reveal, fan-hand exclusion, real 3D card body, in-hand held pose
- **Breaks if:** Relaxing it for left-handed parity without also moving the gate hand.
- **Confidence:** high

### Fingertip hover scans one hand directly, not the poke registry
- **Where:** `CardFan.UpdateFingertipHover`, `PileBrowser.Tick`, `ItemsPile` fan sweep
- **Rule:** These scan the dominant hand's `IndexTip` themselves and elect one nearest card within `FingertipHoverReach` (0.035 m × world scale).
- **Why:** The global poke registry has no per-hand filter and would buzz the fan-**owning** hand, whose fingers sit right by the cards.
- **Established by:** `65fd971` fix(cards): fingertip touch highlights fan cards; remove black card border
- **Breaks if:** "Consolidating" onto `VRInteractables`/`UguiPokeSurfaces` for consistency.
- **Confidence:** high

---

## 4. Fan geometry and presentation

### Gaze relief is multiplicative on a fixed resting bow
- **Where:** `CardFan.BowDepth`, `CardFan.RestBowDepth`, `CardFan.GazeApexIndex`, `CardFan.GazeReliefWidthFactor` (0.55), `CardFan.GazeReliefMinWidth` (1.2)
- **Rule:** `bow(i) = rest(i) · (1 − FanGazeApexFollow · exp(−((i − apex)/w)²))`, where `rest(i)` is the historic symmetric cup and **does not depend on the gaze at all**. Therefore `0 ≤ bow(i) ≤ rest(i)` for every card at every gaze angle.
- **Why:** The previous version moved the apex and re-normalised the bow by the **longer** side, `max(apex, n−1−apex)`. That produced three felt defects: (A) a corner in every card's response at the same gaze angle (the fan centre, where the `max` switches branch) — a hard velocity step the user could feel; (B) a mid-hand card's fraction *grows* when the apex slides to an end, so turning the head pushed cards 1–6 up to ~12 mm **further away** than the neutral shape — "it goes the wrong way first"; (C) the stacking clamp makes the response one-sided, so the two halves of a head sweep never felt alike. The relief form makes A impossible (no `max`, no branch; the Gaussian's derivative in the apex is continuous everywhere, including at `apex == i` where it is exactly 0), B impossible by construction, and works *with* C instead of against it.
- **Established by:** `5e79abe` wip: gaze relief replaces moving apex (superseding `9e2b53d` feat(cards): the fan presents cards to the viewer)
- **Breaks if:** "Simplifying" back to a moving apex, or normalising `rest(i)` by anything gaze-dependent. Monotonicity — one extremum per card, at "the gaze is on me" — is the requirement, not the specific curve.
- **Confidence:** high

### `FanGazeApexFollow = 0` must remain a byte-exact revert
- **Where:** `CardFan.BowDepth` (`if (follow <= 0f) return rest;`), `CardFan.GazeApexIndex` (returns `(n−1)/2`)
- **Rule:** At zero the relief term vanishes and the layout is the historic symmetric bow, unchanged.
- **Why:** It is the in-VR escape hatch: the user can revert the feature from the debug menu without a build.
- **Established by:** `5e79abe`
- **Breaks if:** Folding the early-out into the general expression — `exp(0)` at `apex == i` is 1, not 0, so the arithmetic is not equivalent at every index.
- **Confidence:** high

### The stacking clamp is the draw order, not a hack
- **Where:** `CardFan.ComposeDepths` (each card clamped to at least one `ZStagger` in front of its predecessor; skipped for a **negative** `FanSideDepthCurve`)
- **Rule:** The bow may curl the hand away but may never re-order it.
- **Why:** The bow is a translation and can out-run the 4 mm stagger: with the shipped defaults (10 cards, 35 mm bow) cards 7–9 ended up *behind* card 6, so the right half overlapped backwards and the last card — the one `Relayout` hands a **full-width** collider on the assumption that it is fully exposed — was actually half-covered. The skip for a negative curve is deliberate: a viewer-bulging bow is an opt-in whose whole point is the other stacking direction.
- **Established by:** `066f8ce` feat(cards): real-time fan-tuning preview + negative depth curvature, hardened in `5e79abe`
- **Breaks if:** Removing the clamp as "a leftover from the old bow", or applying it to both signs for symmetry.
- **Confidence:** high

### `ComposeDepths` is the single source of depth truth
- **Where:** `CardFan.ComposeDepths`, called by `CardFan.Relayout`, `CardFan.TickCollapse`, `CardFan.NearestGap`
- **Rule:** All three read their per-card Z from the same routine and the same `_depths` buffer.
- **Why:** So the steady layout, the close animation and the insertion-gap hit test can never drift apart. The collapse deliberately freezes the apex wherever the gaze left it and skips the toe-in (the cards are folding into a single pose by definition).
- **Established by:** `5e79abe`
- **Breaks if:** Inlining the depth maths into `Relayout` because the other two callers "only need an approximation".
- **Confidence:** high

### Presentation goes into the HOME pose, never the live transform
- **Where:** `CardFan.Relayout` → `VRCard.SetHome`; documented at `CardFan.BowDepth` and the region header
- **Rule:** Toe-in rotation and apex-shifted depth are part of the pose handed to `SetHome`, so `TryGetRestingLaserRect` (which reads `_homePos`/`_homeRot`) sees them.
- **Why:** Named in-source as "the recurring, expensive bug in this project": anything applied to the live transform behind the home's back desynchronises the pick geometry from what is drawn.
- **Established by:** `9e2b53d`
- **Breaks if:** Applying a presentation tweak directly to `card.transform` because it is "only visual".
- **Confidence:** high

### Gaze intersection fades smoothly at grazing angles
- **Where:** `CardFan.UpdateCardPresentation`, `CardFan.GazeGrazeMinZ` (0.05), `GazeGrazeFullZ` (0.35)
- **Rule:** The crossing point is weighted by a smoothstep over the fan-local gaze `+Z`, and the divisor is floored at `GazeGrazeMinZ`.
- **Why:** The old gate was a hard `gazeLocal.z > 0.2f`, which **snapped** the apex straight back to the fan centre the instant the gaze approached parallel — a felt jump of up to half a hand, i.e. a second threshold at a rarer angle. The grazing case is real (the intersection blows up as `z → 0`) but the cure has to be continuous.
- **Established by:** `5e79abe`
- **Breaks if:** Replacing the smoothstep with a plain clamp or an `if`.
- **Confidence:** high

### Gaze saturates at the ARC half-width, not the arc radius
- **Where:** `CardFan.ArcHalfWidth`, used to clamp `_gazeX`
- **Rule:** `_gazeX` is clamped to `sin(step·(n−1)/2)·radius`, the outermost card's |x|.
- **Why:** The outermost card sits well inside the radius, so clamping at the *radius* left `_gazeX` growing after the apex had already reached the end card — a dead band that made the effect feel like it "stopped gripping". Clamping here also makes `_gazeX` directly comparable to a card's x in the diagnostic.
- **Established by:** `5e79abe`
- **Breaks if:** Using `FanEffectiveRadius` directly because it is already to hand.
- **Confidence:** high

### Relayout gating epsilons are work gates only
- **Where:** `CardFan.GazeRelayoutEpsilon` (0.002 m), `CardFan.ToeInRelayoutEpsilon` (0.003 m), `Core.PerfConfig.FanRelayoutInterval` (default 0 = off)
- **Rule:** These decide **how often the target moves**, never how the cards travel — each card's own exponential home-lerp runs every frame regardless.
- **Why:** So a still head does no work and a turning head cannot look steppy. The perf interval ships defaulted to 0 deliberately: the relayout is not *known* to be expensive, so it is a lever to A/B on hardware, not a silent behaviour change. Only the gaze path is limited — card-set changes, hovers, plucks, insert gaps and the fan-out reveal call `Relayout` directly and are never delayed.
- **Established by:** `9e2b53d` (epsilons), `ceef661` feat(perf): … (interval lever)
- **Breaks if:** Defaulting the interval to a non-zero value "since it's obviously a win", or routing the non-gaze callers through the same limiter.
- **Confidence:** high

### Live tuning preview via a weighted parameter signature
- **Where:** `CardFan.FanParamSignature`, consumed in `CardFan.Tick`
- **Rule:** A single weighted float sum of every **steady-layout** Fan parameter is compared each frame; a change re-lays out the open fan. Animation durations, gaze bias, pop distance and palm/follow parameters are excluded on purpose. `NaN` seeds a baseline **without** relaying out.
- **Why:** The fan only re-laid out on open / card-set change, so a debug-menu stepper edit only showed after closing and reopening the fan. The excluded parameters are already read live every frame or only matter mid-animation. The `NaN` seed exists so the first steady frame after a reveal does not fire a spurious relayout.
- **Established by:** `066f8ce` feat(cards): real-time fan-tuning preview + negative depth curvature
- **Breaks if:** "Improving" the signature to include everything (the reveal then fights it every frame), or replacing it with a config `SettingChanged` subscription — the fan is also edited by hand in the cfg file and by the debug menu, both of which this covers with one float compare.
- **Confidence:** high

### The fan standoff uses `VRHand.WorldScale`, never `palm.lossyScale`
- **Where:** `CardFan.Tick`
- **Rule:** `FanPalmOffset` is multiplied by `_hand.WorldScale`.
- **Why:** For the procedural hand the palm anchor is a direct child of the hand root, so `palm.lossyScale == WorldScale` and the two were equivalent — but the **bundle glove** hangs its anchors under an armature authored at `localScale 100` (fbx cm→m). Scaling the standoff by that put the fan ~9 m up the palm normal instead of 0.09 m: the fan existed, bound and opened, but never rendered where the player looks.
- **Established by:** `ef85349` fix(cards): restore visible hand fan + kill 2D flash on card burn
- **Breaks if:** "Using the transform we already have" — this is the mod-wide 100× armature-scale trap (see also `EmptyFanHint`).
- **Confidence:** high

### Follow smoothing reparents to the rig root, not the palm
- **Where:** `CardFan.Tick` (`FanFollowSmoothing > 0` branch), `_followInit`
- **Rule:** When smoothing is on, the fan root parents to the **stable rig root** (same diorama scale, so card sizes are unchanged) and eases its world position toward the palm target, only once past `FanFollowDeadzone`. Reparenting happens only on a mode flip, with `worldPositionStays`. `_followInit` snaps on the first tick instead of easing in.
- **Why:** Dead-zoned easing needs the fan decoupled from the palm; a palm-parented root cannot hold still through sub-threshold jitter. `FanFollowSmoothing == 0` reproduces the exact previous rigid follow.
- **Established by:** `c29a0a9` feat(cards): G4 eased dead-zoned fan follow (Demeo parity)
- **Breaks if:** Parenting to the palm and smoothing the local offset — the diorama scale then rides the glove armature again.
- **Confidence:** high

### `ZStagger` is several times the card thickness
- **Where:** `CardFan.ZStagger` (0.004), mirrored in `PileBrowser.ZStagger`, `ItemsPile.ZStagger`, `ActivePileViewer.ZStagger`; `CardMesh.Thickness` is 0.0015
- **Rule:** Neighbouring fanned cards are separated by ~2.7× the backing thickness.
- **Why:** So the overlap is **pure render order** and cards can never interpenetrate visually. Cards kept their own z-stagger even while collapsed, so the draw order never flickers during the close animation.
- **Established by:** `4a32269` feat(cards): Demeo reveal modes, laser pluck… (fan de-clip)
- **Breaks if:** Deriving the stagger from the mesh thickness "to be exact".
- **Confidence:** high

### The hovered card's pop lives in `VRCard`, not in the layout
- **Where:** `CardFan.SetHovered` / `CardFan.Relayout` (split offsets only), `VRCard` `_popped` / `_laserPopped`
- **Rule:** The fan opens a **gap** around the hovered card; the forward pop is `VRCard`'s job alone.
- **Why:** `VRCard` already pops any hovered/highlighted card by `FanSelectedPopForward`; baking a pop into the home too would double it.
- **Established by:** `6fc47c3` feat(cards): G2 whole-fan hover split + public SetHovered (Demeo parity), `aa003fe` feat(cards): G2 — pop hovered card by [Cards] FanSelectedPopForward
- **Breaks if:** Moving the pop into the layout "so the pick geometry sees it" — that is precisely what the resting rect exists to prevent.
- **Confidence:** high

### Two separate pop sources that never stomp each other
- **Where:** `VRCard.SetPopped` (`_popped`) vs `VRCard.SetLaserHover` (`_laserPopped`)
- **Rule:** The card pops while **either** flag is set; the two are stored separately.
- **Why:** The dominant hand's ray and the proximity highlight would otherwise clear each other's state on alternating frames.
- **Established by:** `4a32269`
- **Breaks if:** Collapsing them into one `bool IsHovered`.
- **Confidence:** high

### A closed fan adopts stray cards
- **Where:** `CardFan.SetCards` (the `else if (_root != null)` branch)
- **Rule:** When the fan is **closed**, any card in the set not already parented under the (inactive) fan root is adopted there immediately.
- **Why:** `Relayout` only runs while open, so a card handed back to a closed fan kept its old parent and home *forever*. A slotted card the game deselected (short rest's `DeselectAllCards`, an undo, a rejected select) kept lying in the tray recess that the short-rest sacrifice display then docked into — the reported overlap glitch.
- **Established by:** `1034ab6` fix(cards): lift-priority tray-card grab + 2-card placement gate + rest collision safety (task #4b, collision safety)
- **Breaks if:** Guarding the whole method on `IsOpen` because "a closed fan has nothing to lay out".
- **Confidence:** high

### A card-set change finishes an in-flight collapse instantly
- **Where:** `CardFan.SetCards` (the `!IsOpen && _closeElapsed >= 0f` branch)
- **Rule:** A pending close animation is completed immediately (root deactivated) before the new set is installed.
- **Why:** Otherwise a stale collapsing stack lingers under the incoming set.
- **Established by:** `e89469a` feat(cards): Demeo-style fan reveal
- **Breaks if:** Letting the collapse tick finish on its own — it would then animate cards that are no longer in the fan.
- **Confidence:** medium

---

## 5. Fan lifecycle and the palm gate

### Roll gate v4: parallel transport, measured on the VISUAL hand frame
- **Where:** `Hands.PalmGate` (driven by `CardsDriver.UpdatePalmGate`), `CardsConfig.RevealEnterDegrees` / `RevealExitDegrees`
- **Rule:** The reveal measure parallel-transports a reference up-vector onto the plane perpendicular to the finger axis each frame, eases it back onto the pitch-neutral world anchor **only while `|dot(F, up)| < 0.7`**, and reads the signed angle about the finger axis — on the `HandRig.Root` **visual** frame, not the device pose.
- **Why:** Four generations of this gate failed on hardware. v2's projected-angle measure degenerated when the finger axis approached world-up (fired at "roll 97" on a pure pitch). v3 adopted Demeo's `dot(handRight, up)`, which is degenerate with the fingers vertical (wrist twist spins the right axis around world up, the dot pins near 0, the fan never opens). v4's easing gate kills holonomy drift while keeping the anchor well-conditioned. The visual frame is used so the gate matches the debug-menu seat offsets and per-style trims the user actually sees.
- **Established by:** `bb3502c` → `d7ec01c` → `6b5c38c` fix(cards,rig): roll gate v4 parallel-transport (pitch-invariant reveal)
- **Breaks if:** Reverting to any single dot product, or measuring on the device grip pose because it is "more direct".
- **Confidence:** high

### Exit threshold is clamped below enter
- **Where:** `Hands.PalmGate` (hysteresis clamp), `CardsConfig` reveal binds
- **Rule:** The gate clamps exit strictly below enter so hysteresis can never invert.
- **Why:** A user-tunable pair with two independent steppers can be dialled into an inverted state, which latches the fan.
- **Established by:** `e89469a`
- **Breaks if:** Trusting the config range alone.
- **Confidence:** high

### The fan stays open while the laser is on it
- **Where:** `CardsDriver.UpdatePalmGate` (`revealed = gate.Enabled && (gate.IsOpen || _laserHover != null)`)
- **Rule:** In tilt mode a live laser hover holds the fan open even after the palm rolls back.
- **Why:** Plucking must never collapse the fan mid-reach.
- **Established by:** `4a32269`, reinforced by `caafe5b` feat(cards): G5 — wire Demeo reveal preset + busy-hand gate into the fan
- **Breaks if:** Reducing to `gate.Enabled && gate.IsOpen`.
- **Confidence:** high

### Busy-hand gate
- **Where:** `CardsDriver.UpdatePalmGate` → `PalmGate.IgnoreWhenHandBusy` (`CardsConfig.RevealIgnoreWhenGrabbing`)
- **Rule:** While the dominant hand is grabbing, the gate holds its current state.
- **Why:** A pluck must never re-trigger the fan mid-reach.
- **Established by:** `caafe5b`
- **Confidence:** high

### Fan edge sounds are listener-anchored, card sounds are positional
- **Where:** `CardsDriver.PlayFanEdgeSound` (`AudioController.Play(id)`) vs `CardsDriver.PlayCardSound` (positional overload)
- **Rule:** Fan open/close must use the **non-positional** overload.
- **Why:** The mod never moves the `AudioListener` — it rides the game's 2D camera, not the VR head. The close default is a 2D UI item and ignores position, so it was audible; the open default is an in-world 3D item that attenuated against the far-away listener to **silence**, with `Play()` "succeeding" so nothing was logged. Two rounds were spent looking for a missing call.
- **Established by:** `385b39e` fix(cards,worldui): audible fan-open sound (listener-anchored) + live wrist HUD gold/XP
- **Breaks if:** Unifying both helpers onto one overload "since they're both card sounds".
- **Confidence:** high

### Mod card sounds only where the game is silent
- **Where:** `CardsDriver.PlayCardSound` call sites — grab, tray reorder, pick re-drop, reopen take-back **only**
- **Rule:** No mod sound on fan→slot play, fan→occupied swap or tray take-back.
- **Why:** `AbilityCardUI.ToggleSelect` plays the card's own serialized profile click on **every** queued Select/Unselect, so a mod thunk on top made two sounds per placement.
- **Established by:** `ae1f6e7` fix(cards): damage-burn flow crash gate + free swap, double place-sound… (task #5)
- **Breaks if:** "Adding the obviously missing place sound" for symmetry with the reorder branch.
- **Confidence:** high

### Empty-fan hint is edge-triggered and modal-gated
- **Where:** `CardsDriver.UpdatePalmGate` (`_gateWasRevealed`), `EmptyFanHint.Show`
- **Rule:** The ghost placard fires on the **rising edge** of `revealed`, and only when not modal-blocked, not the dev fake hand, a hand is bound, and the mode is `CardsSelection`.
- **Why:** Rolling the palm open with zero hand cards used to show nothing, which read as "the fan is broken". But pick flows and dialogs are cases where an empty fan is *expected* — flashing it there is noise, and a level-triggered version storms the placard every frame.
- **Established by:** `ae1f6e7` (task #9)
- **Breaks if:** Showing it whenever the fan would be empty.
- **Confidence:** high

### `EmptyFanHint` positions off `VRHand.WorldScale` too
- **Where:** `EmptyFanHint.Show`
- **Rule:** Same armature-scale rule as `CardFan.Tick` — never `palm.lossyScale`.
- **Why:** The bundle glove's 100× armature "would fling the placard out of view" (stated in-source).
- **Established by:** `ae1f6e7`
- **Confidence:** high

### The hint plate is deliberately not the shared glow material
- **Where:** `EmptyFanHint` (parchment plate build)
- **Rule:** A collider-free alpha-blended quad, explicitly **not** `CardGlow`'s additive recipe.
- **Why:** Stated in-source as deliberate; the additive glow reads as an interactive affordance, which this is not.
- **Established by:** `ae1f6e7`
- **Confidence:** medium

---

## 6. Board pose policy and the lost-board watchdog

> This section is the single most expensive lesson in the subsystem: the user's
> requirement is **absolute** — "fixed or follow mode, the board never moves without
> explicit user action". Several separate paths each violated it once.

### Only the user may recompute a board pose
- **Where:** `CardsDriver.OnModeChanged` (does **not** re-anchor the tray), `PlayTray.PlaceAtHead`, `PlayTray.RestorePose`
- **Rule:** The first placement or an explicit user action (tray grab, board-switch restore, settings orientation tuning, lost-pose recovery) computes a pose. **A game event never does.**
- **Why:** `OnModeChanged` used to call `InvalidatePlacement()` on `To == CardSelection/HalfSelection` from `TableIdle` — the exact transition every turn CONFIRM produces — forcing `PlaceAtHead` to recompute from the *current* head. The hardware log caught five "Control board placed" lines with yaw −9° → 42° across one confirm.
- **Established by:** `1a9b071` (issue C)
- **Breaks if:** Re-adding a "so the board is always reachable" re-anchor on any game transition.
- **Confidence:** high

### The pose watchdog exists to prove the policy holds
- **Where:** `CardsDriver.TickBoardPoseWatch`, `CardsDriver._expectedPoseChange`, `PlayTray.ConsumePinHousekeepingMove`
- **Rule:** Every sanctioned mover sets a one-frame token naming its trigger; the watch runs **last** in `Update` and warns on any unexplained parent-local pose change. The token is cleared unconditionally at the tail.
- **Why:** It is the instrument that turns "the board jumped again" into a log line naming the culprit. A token that survives frames would launder a *later* game-driven jump as user-sanctioned.
- **Established by:** `1a9b071`, extended by `fb2e6e3`/`f6d9725`
- **Breaks if:** Moving the watch up next to the placement calls, or clearing the token only inside the `moved` branch.
- **Confidence:** high

### The watchdog compares PARENT-LOCAL pose
- **Where:** `CardsDriver.TickBoardPoseWatch` (`_watchLocalPos`/`_watchLocalRot`/`_watchScale`, `_watchParent`)
- **Rule:** World-pose changes caused by the rig/anchor moving (recentre, diorama rescale) are not board moves. A parent change re-baselines **silently**.
- **Why:** Pin/follow toggles re-parent with `worldPositionStays`, and the rig moves constantly; a world-pose comparison warns on every one of them and drowns the one line the log exists to find.
- **Established by:** `1a9b071`
- **Breaks if:** Switching to `root.position`/`root.rotation` because it reads more naturally.
- **Confidence:** high

### Lost-board watchdog is UNCONDITIONAL and per-frame
- **Where:** `PlayTray.TickLostWatchdog`
- **Rule:** The lost check runs every frame, never driven by a presence / doff-don event.
- **Why:** The incident log has **no** `[Core] Session resumed` line anywhere and OpenXR sat in `XR_SESSION_STATE_FOCUSED` for the whole run — the runtime hid the doff/don from us entirely. A doff/don that produces no runtime signal is invisible to every event-driven recovery the mod has.
- **Established by:** `fb2e6e3` fix(board): lost-board watchdog — the control board can no longer end up unreachable
- **Breaks if:** Replacing the tick with a `SessionResumed` subscription "to save per-frame work". The absence of that signal is precisely the bug.
- **Confidence:** high

### THE AUTOMATIC RECALL IS GONE — the absence of the code IS the invariant
**[verified 2026-09-08. This entry REPLACES two entries — "Lost = neither reachable nor findable,
for 3 s, in REAL metres" and "A grabbed board is never recalled, and a missing head yields no
verdict" — which described the envelope-and-dwell recall as live law. It was deleted on a user
ruling on 2026-08-03. Both old entries are quoted below, because their reasoning is the argument
against the FIRST version of the feature and a successor needs it to understand why re-tuning the
envelope is not the answer either. `Cards/Tray/PlayTray.2.Watchdog.cs` cites "INVARIANTS-Cards §6"
as the holder of this invariant, and until this pass §6 did not hold it.]**

- **Where:** the DO-NOT-RE-ADD comment block at the top of `Cards/Tray/PlayTray.2.Watchdog.cs`. It has **no code under it, and that is the point.**
- **Rule:** **DO NOT RE-ADD A DISTANCE- OR VISIBILITY-BASED RECALL**, and do not delete the comment block because nothing follows it. `PlayTray.WatchReachMeters` (1.8), `WatchFindableMeters` (4), `WatchViewMargin` (0.35), `WatchLostDwellSeconds` (3) and `_lostSince` **no longer exist in `src/`**.
- **Why:** the feature did exactly what it was written to do, and *that* was the bug. USER RULING, 2026-08-03, after it fired while the options menu was open in a tutorial: *"das darf niemals passieren, das Controllboard muss immer wie angewurzelt an der Position sein — es darf niemals (egal was passiert) eine Position plötzlich wechseln (Respektiere natürlich nach wie vor fixed/Folgen)."* The hardware log of that run shows the mechanism exactly: reading a menu parks the head away from a PINNED board for longer than the dwell, so `CONTROL BOARD RECOVERED — out of reach AND out of view for 3.0s (2.64 m out, -1.12 m vertical, 69° off the view axis, mode PINNED, rig scale 8.1)` fired **four times in one session**, each time teleporting the board in front of the player. **No envelope tuning can fix that**: "the player is not looking at it and it is more than an arm away" is the NORMAL state of a pinned board, not evidence of a glitch. The same ruling deleted `WorldUI.ModalFallback`'s lost-menu recall at ModBuild 149, so the precedent the watchdog was modelled on is gone too — nothing in WorldUI recalls anything on a timer any more.
- **What survives:** the **non-finite verdict** (a NaN/Inf transform is not a position at all, nothing parented to it renders, and it can never heal by itself — see the entry below), plus the pose-PRESERVING pin housekeeping. The user-facing recovery is the **explicit** one, `CardsDriver.RequestBoardRecall` → `_recallBoard`, which is a deliberate action and therefore always allowed. **It has had a button since 2026-09-06** — the first row of *Brett & Karten ▸ Steuerbrett*, reached from the PAUSE menu and deliberately not from the board itself, which is the whole point when the board is what is missing.
- **Breaks if:** any per-frame test that moves the board because of where the player is or is looking. Re-read the ruling first; this is Tier 3 and it is settled.
- **Confidence:** high (source comment states the ruling verbatim; re-read at `49ceab21`)

**The two superseded entries, kept for their reasoning.** Both were true of the deleted feature
and both are the record of how carefully it was built — which is the point: it was carefully built
and still wrong, so a more careful envelope is not the fix.

> ~~**Lost = neither reachable nor findable, for 3 s, in REAL metres.** Within reach the board is
> never recalled *no matter where the head is looking*; a board that is far but inside the frustum
> (with viewport slack) is findable and must not be yanked back; the verdict must hold continuously
> for 3 s. A lectern below the chin legitimately leaves the frustum whenever the player looks up at
> the dungeon, so a frustum-only test recalls it constantly. The view margin exists because a board
> half off the view edge is findable by turning the head. The dwell is long enough that leaning away
> or turning around never moves it. Unscaled time because the pause menu freezes `timeScale`.
> Established by `fb2e6e3`.~~
>
> ~~**A grabbed board is never recalled, and a missing head yields no verdict.** Both cases reset
> the dwell timer rather than merely returning: yanking a panel out of the user's hand mid-carry is
> worse than losing it, and with no head there is no verdict possible. Returning without clearing
> lets the dwell accumulate across a long carry and fire the moment the user lets go.~~

### A non-finite transform bypasses the dwell
- **Where:** `PlayTray.TickLostWatchdog` (NaN/Inf branch returns `true` immediately)
- **Rule:** Corrupt poses recover at once, not after 3 s.
- **Why:** Everything parented to a NaN transform (cards, docked game canvases) renders undefined, and it can never heal by itself.
- **Established by:** `fb2e6e3`
- **Breaks if:** Folding it into the same dwell path "for uniformity".
- **Confidence:** high

### `SyncPinHolder` runs BEFORE the lost test
- **Where:** `PlayTray.TickLostWatchdog` (call order)
- **Rule:** Pin housekeeping is pose-preserving in the user's frame of reference, so it runs first and can stop the board from ever reading lost.
- **Why:** Otherwise a recentre strands the board for a frame and the dwell timer starts on a board that is about to be carried along anyway.
- **Established by:** `fb2e6e3`
- **Breaks if:** Moving the call after the reach test, or into the `lost` branch.
- **Confidence:** high

### The pinned board is carried ONLY on a rig SCALE change
- **Where:** `Cards/BoardZoomCarry.cs` (the arithmetic, Mathf-only and wire-tested), `Cards/BoardZoomCarryDriver.cs` (`LateUpdate`, `[DefaultExecutionOrder(20000)]`)
- **Rule:** On a frame where the rig's lossy scale changed by more than a **relative** epsilon — compared against the last **carried** scale, so sub-epsilon creep accumulates instead of escaping — the pinned board is re-derived from its rig-local pose. On every other frame nothing is written. The write goes to the **holder**, solved so the tray's parent-local transform is bit-identical.
- **Why:** `Rig/WorldGrab.cs:400` pivots the zoom on the glued **hand midpoint**, not the head, so a scale change moves the head through the world and a world-fixed object's angular size necessarily rides the zoom. Holding the rig-local pose makes the board invisible to the zoom; holding the world pose keeps it in the room. **Neither pure anchor is correct** — ModBuild 158 was world-static ("das controllboard mitgezoomed"), ModBuild 160 was rig-local ("geht immer mit"), and four builds moved back and forth between them. The scale gate is what separates the two behaviours.
- **Established by:** ModBuild 161, on the user's instruction of 2026-08-18 to revert the board to the ModBuild 158 state and fix from there.
- **Breaks if:** Carrying unconditionally (that is ModBuild 160 and report 3); sampling the rig in the Update phase (that is ModBuild 159's ±10 % breathing — `parent chain ×64.79 ÷ rig ×68.50`, the previous frame's scale, in 82 of 158 moving-zoom samples); writing the tray instead of the holder (`CardsDriver.TickBoardPoseWatch` then logs at frame rate); using an absolute epsilon (the rig scale spans ×19–×137); failing to advance the carried-scale baseline after a carry (the holder compounds to infinity in about a second — caught by mutation testing, not by the green suite).
- **Confidence:** high

### The pin holder scale is written ONCE and never re-asserted
- **Where:** `PlayTray.SyncPinHolder` (the explanatory comment block with **no code under it**), `PlayTray.ApplyFollowMode` (the single writer)
- **Rule:** `_pinRoot.localScale` is set at pin time and never touched per frame.
- **Why:** A per-frame re-assert from the live rig scale was added on the theory that a world-grab zoom would drift the pinned board. That theory was wrong twice over. It cannot drift: the holder sits at the world **origin** with identity rotation and is not parented under the rig, so rescaling the rig cannot move or resize anything underneath it. And the rescale *was* the reported bug — with the holder tracking the rig, the board's world **size** grows with the zoom while its world **position** is held, so it swells on screen exactly as the rest of the world shrinks.
- **Established by:** `f6d9725` fix(board): a pinned control board is no longer resized by world zoom (regression from `fb2e6e3`)
- **Breaks if:** Someone "fixes the obvious scale drift" by re-asserting the holder scale inside the tick. **The comment block with no code under it IS the invariant** — do not delete it as a dead comment.
- **AMENDED BY ModBuild 161** — and the amendment is exactly one word wide. The *unconditional* re-assert this entry forbids is still forbidden: ModBuild 159 shipped it and produced the "zoomed immer noch mit" report, because a holder tracking the rig while the world position is held makes the board swell as the world shrinks. But the reason given above ("it cannot drift") is **wrong**, and the log says so: the zoom pivots on the hand midpoint (`Rig/WorldGrab.cs:400`), not the head, so the player's eye moves through the world with every scale change and a world-static board's angular size rides it. The holder is now re-asserted on **scale-change frames only** — see *The pinned board is carried ONLY on a rig SCALE change*. On every other frame this entry still holds verbatim.
- **Confidence:** high

### Pin carry keys on `RigPoseVersion` and nothing else
**[verified 2026-09-08 — the rule holds; the state moved.]** `_pinPoseVersion`, `_rigLocalPinValid`
and `_rigLocalPinPos`/`Rot` are **no longer fields of `PlayTray`**: the origin version and the
rig-relative cache now live in the shared **`Core/FollowPinAnchor.cs`**, because the combat log's
pin needs the identical sentence and a second copy was the alternative. `PlayTray` kept the three
things that are its own — the freeze-sentinel announcement, the issue-C move label, and the log.
Everything below still describes the mechanism; read the fields at `FollowPinAnchor`.
- **Where:** `PlayTray.SyncPinHolder`, with the origin version and rig-local cache in `Core.FollowPinAnchor` (was `_pinPoseVersion`, `_rigLocalPinValid`, `_rigLocalPinPos`/`_rigLocalPinRot` on `PlayTray`)
- **Rule:** The pinned board is carried by its cached **rig-relative** pose exactly when `VRRigDriver.RigPoseVersion` changes; the rig-local cache is refreshed every frame while the origin is stable; the FOLLOW/no-pin early return still stamps the version first.
- **Why:** `RigPoseVersion` bumps on exactly two events — a rig (re)build and a deliberate recentre — and on nothing else: snap turns and world-grab deliberately do **not** bump it. It is therefore the only stable signal. The per-frame cache refresh exists so the next origin change carries the board from where the user last dragged it, not from where it was pinned. The early-return stamp prevents a stale version triggering a bogus carry the first frame after switching to PINNED.
- **Established by:** `fb2e6e3`
- **Breaks if:** Keying on "did the rig transform change since last frame" (carries on every snap turn), caching the rig-local pose only at pin time, or hoisting the early return above the stamp.
- **Confidence:** high

### The `BOARD ANCHOR` line accuses only an UNEXPLAINED move, and it asks the sentinel rather than inferring
- **Where:** `PlayTray.TickPinnedFreezeSentinel` (the tally: `_pinSanctionedMoveCount` / `_pinUnknownMoveCount`, incremented **outside** the 1 s log throttle), `PlayTray.TickBoardAnchorDiagnostics` (the deltas `sanctionedSince` / `unknownSince` and the five-branch verdict), `PlayTray.DefectVerdict` (the wording, byte-identical to what it has always been), `PlayTray.PinFreezePosEpsilon` / `PinFreezeScaleEpsilon` (one definition, read by both instruments)
- **Rule:** A move counts as *sanctioned* only when the sentinel observed **both** an announcement (`NotePinnedWrite`) **and** a real pose change in the same frame. The verdict order is: convicted unannounced writer → apparent-size push → sanctioned writer (named) → below the sentinel's own bands (float noise) → a real move nothing detected. `DefectVerdict` is reached by the first and the last of those and by nothing else.
- **Why:** "No push fired" was never the same question as "nothing wrote it". The push is one of *seven* sanctioned writers; the other six move the world **position** (tracking-origin carry, arrival-seat correction, first placement, lost-board recovery, settings live-apply, board-switch restore). So the line called eight legitimate pin carries a defect in the 2026-09-05 session (`BOARD ANCHOR … MOVED WITH NO PUSH` at 906 / 1243 / 6067 / 6079 / 6229 / 11370 / 11385 / 11567 — every one of them a `SyncPinHolder` carry or the first seat), and from the arrival-seat guard onwards it would have accused every arrival correction too, four lines under the line explaining the fix. Counting **announcements** would have been the wrong repair: several are world-pose-preserving by construction (`ApplyFollowMode`'s re-parent), so an announcement that moved nothing could have explained away a different move in the same window. What is counted is what the sentinel already measures.
- **Established by:** the 2026-09-05 arrival-seat round, on the coordinator's instruction not to soften the genuine verdict
- **Breaks if:** Counting in `NotePinnedWrite` instead of in the sentinel (announcements are claims, not observations); moving the tally inside the sentinel's 1 s throttle (five of the seven carries in that log were throttled — a term that goes silent under load agrees with every broken build); re-deriving either epsilon instead of calling `PinFreezePosEpsilon`/`PinFreezeScaleEpsilon`; or reordering the branches so `pushesSince` no longer outranks the generic sanctioned term.
- **Confidence:** high

### The arrival seat guard writes ONLY on a rig-origin change, and only inside a scenario arrival
- **Where:** `PlayTray.ObserveArrivalWindow` (arming, ahead of every early-out in `TickLostWatchdog`), `PlayTray.TickArrivalSeatGuard` (judging, immediately **after** `SyncPinHolder`), `PlayTray.EvaluateArrivalSeat`, `PlayTray.ReseatBesidePlayer`, `VRRigDriver.ScenarioArrivalPending`
- **Rule:** While a scenario ARRIVAL is pending, a verdict is taken at the first placement and at every change of `VRRigDriver.RigPoseVersion`; a board further than `[Cards] SpawnMaxReachMeters` from the head or more than `[Cards] SpawnMaxBearingDegrees` off the player's forward is re-seated at the configured first seat. On a frame where the origin did **not** change the guard is a **reading only** — it never writes. The player's own grab disarms it permanently, as does the arrival closing and a cap of 8 verdicts.
- **Why:** `_everPlaced` is cleared only by `PlayTray.Destroy`, i.e. by the **hands root** going away — not by arriving at a scenario. The tray instance and root survive a scenario change, so "beim ersten Spawnen" silently meant "the first scenario of the session": the hardware log of 2026-09-05 (`LogOutput.log`, three scenario starts at 785 / 5980 / 11282) holds exactly **one** `FIRST SEAT` line, at 896. Scenarios 2 and 3 kept the offset the previous scenario ended with, faithfully carried by `SyncPinHolder`, which put the board 28.55 world units / 2.05 m from the head in scenario 3 — out past the far edge of the play field, drifting to 41.08 wu / 2.95 m before the player found it by hand (`Board pose [user-grab]`, 12084). The ordering half is in the same log: recenter and every carry run **before** the footprint is measurable and **before** `Spawn ring: SEATED`, so any seat authored at one stage is authored against a rig pose the arrival is about to abandon. The invariant is therefore stated on the OUTCOME at every such stage, not on one stage.
- **Established by:** the 2026-09-05 report ("Das Controlboard muss zwingend immer neben einem spawnen, niemals weiter weg")
- **Breaks if:** Running the guard **before** `SyncPinHolder` (it would judge a pose about to be replaced, and the carry would then undo the correction from a rig-local cache taken before it — the exact "measured the wrong stage" defect); dropping the origin-change condition (that reinstates the automatic distance recall the 2026-08-03 ruling removed, and this file forbids two entries above); keying the arming on the tray being placed (the window opens while the placement is still deferred); or letting the correction skip `_pinPoseVersion`/`_rigLocalPin*` re-authoring (the next carry silently restores the stranded pose).
- **Confidence:** high — the mechanism is measured in the log, the fix is not yet hardware-confirmed (`// HW-VERIFY` on `CONTROL BOARD ARRIVAL SEAT`).

### Watchdog state is reset in `Destroy`
**[verified 2026-09-08 — the rule holds; three of the four fields are gone.]** `_lostSince` went
with the deleted recall (see *THE AUTOMATIC RECALL IS GONE*); `_pinPoseVersion` and
`_rigLocalPinValid` moved to `Core.FollowPinAnchor`. `_pinHousekeepingMove` is still a `PlayTray`
field. The invariant is unchanged and is the reason it must be re-checked after any such move:
**whatever pin/watchdog bookkeeping exists must be cleared on teardown, wherever it now lives.**
- **Where:** `PlayTray.Destroy` (was `_lostSince`, `_pinPoseVersion`, `_rigLocalPinValid`, `_pinHousekeepingMove`)
- **Rule:** All watchdog/pin bookkeeping is cleared on teardown.
- **Why:** The `PlayTray` **instance outlives its root** (board switch, rebuild), so a stale dwell timer could recover a board that was never lost.
- **Established by:** `fb2e6e3`
- **Breaks if:** Trimming the block because "the fields are re-initialised on build" — they are not; the instance is reused.
- **Confidence:** high

### HMD resume preserves the pose unless it is genuinely lost
- **Where:** `CardsDriver.ReassertBoardKeepingPose`, `PlayTray.ReassertPlacement`
- **Rule:** On session resume the pose is restored verbatim; a re-seat happens only for a non-finite pose, a horizontal distance beyond ~6 m (scaled), or a drop of more than 2 m (scaled). The root is always re-shown.
- **Why:** `PlayTray.ReassertPlacement` alone would re-seat a FOLLOW board at the head — a silent move, which the absolute requirement forbids. The scale factor is needed because the board lives in diorama space. The unconditional re-show exists so a presence blip can never leave the board hidden.
- **Established by:** `91afaf3` (item 3, presence heal), tightened by `1a9b071` and `fb2e6e3`
- **Breaks if:** Calling `ReassertPlacement` unconditionally on resume, or dropping the scale multipliers.
- **Confidence:** high

### `PlaceAtHead` defers on an untracked head — two independent tests
- **Where:** `PlayTray.PlaceAtHead` (`untrackedHmd` **and** `atWorldOrigin`), `PlayTray.TickPlacement`, `PlayTray.SetVisible` (`show = visible && _placed`)
- **Rule:** Placement aborts while the rig camera still sits at its local origin **or** the head reads world-origin with identity rotation; the root stays hidden meanwhile and the driver retries each frame.
- **Why:** In a session's first frames the "in front of the player" maths places the tray at a garbage pose — and a persisted PINNED mode then permanently pins it there ("the control board appeared far below the map"). The second test catches a fresh/stale `Camera.main` fallback that the first cannot see. Hiding is required because showing it would flash the tray at a stale pose.
- **Established by:** `2e73b6b` fix(cards): tray always spawns head-relative — pin re-engages after placement, untracked head defers; `2a97bae` (bogus head guard)
- **Breaks if:** Merging the two tests into one distance check, or dropping `atWorldOrigin` because "the camera is never exactly at origin".
- **Confidence:** high

### A persisted PINNED mode is applied only AFTER the first placement
- **Where:** `PlayTray.EnsureBuilt` (does not pin), `PlayTray.PlaceAtHead` (re-applies the persisted mode)
- **Rule:** The tray always spawns head-relative, pinned or not.
- **Why:** The pinned **world** pose is not persisted, so pinning a fresh root anchors it at a stale/default pose.
- **Established by:** `2e73b6b`
- **Breaks if:** "Restoring user settings early" by calling `ApplyFollowMode()` at the end of `EnsureBuilt`.
- **Confidence:** high

### Toggling INTO follow must not re-place
- **Where:** `PlayTray.ApplyFollowMode` (`if (!_placed) PlaceAtHead(); else _placed = true;`)
- **Rule:** The re-parent already preserves the world pose; only a never-placed tray gets seated.
- **Why:** The unconditional `PlaceAtHead()` was the one-time jump the user reported when flipping the pin.
- **Established by:** `cb62991` fix(cards): board HUD occlusion, follow/target re-seat jumps, drop slot captions (item 4)
- **Breaks if:** Collapsing the branch — the `else` looks like a no-op assignment of an already-true field.
- **Confidence:** high

### `ClampNearHead` is scaled and asymmetric
- **Where:** `PlayTray.ClampNearHead`
- **Rule:** ≤ 1.2 m horizontal, y in [−1.0, +0.2], **all multiplied by the rig scale**.
- **Why:** The belt-and-braces guard against every other cause of an absurd pose — a wildly mis-tuned `BoardPosOffset`, an odd rig scale, a frozen-then-restored head after a doff/don. The band is asymmetric because a lectern legitimately sits below the chin and never above the brow.
- **Established by:** `2a97bae` fix(cards): board spawns near player…, extracted in `91afaf3`
- **Breaks if:** Dropping the `* scale` (the clamp would pin the board to the player's nose) or symmetrising the band.
- **Confidence:** high

### Grab persistence divides out the per-board multipliers
- **Where:** `PlayTray.PersistPoseToConfig`
- **Rule:** Persisted `TrayYaw`/`TrayScale` are the raw pose **minus** `BoardYaw` and **divided by** `BoardScale`, and the pitch is undone with `Euler(-(90 − BoardTilt), 0, 0)`.
- **Why:** So `TrayScale` keeps its raw 0.5–2× grab semantics. Without the inverse, each grab-release compounds the per-board offsets into the global config.
- **Established by:** `17862bb` feat(cards): in-VR debug menu + predictable per-board tuning
- **Breaks if:** Writing `_root.rotation`/`localScale` straight to config as "the same thing".
- **Confidence:** high

### The board face frame is stored `_root`-LOCAL
- **Where:** `PlayTray.EnsureBuilt` (`_boardFaceFrame = Quaternion.Inverse(_root.rotation) * faceWorld`), applied as a child `localRotation`; bundle anchors get the **world** value
- **Rule:** The two must not be conflated.
- **Why:** `_root` is not identity in world — it is parented under the rig/hands root which carries the diorama rotation — so a world `LookRotation` applied as a child `localRotation` was double-rotated and put "Runde N" and the gear on the board **back**. The prior attempt at this fix "did not work" for exactly this reason.
- **Established by:** `880b8ed` fix(render): hands over menu screen, board readout/gear facing+depth, slot card fill (item 2)
- **Breaks if:** `_boardFaceFrame = faceWorld` — they look interchangeable at build time because the anchors two lines below use the world value.
- **Confidence:** high

### Anchor forward is `+nF`, verified by render
- **Where:** `PlayTray.EnsureBuilt` (`LookRotation(nF, vF)`)
- **Rule:** Do **not** flip to `−nF`.
- **Why:** An offscreen render of the board under the mod's real lectern tilt from the player POV shows `+nF` lands the cards squarely in the two slot recesses on the player-facing functional face; `−nF` hides them on the decorative back. The `−nF` flip was tried once, chasing a stale in-game report, and reverted.
- **Established by:** `0138eb6` → `3f91e79` (wrong) → `807c2fa` fix(board): revert anchor-facing to +nF — cards belong in the slot recesses (render-verified)
- **Breaks if:** "Correcting the sign" so the normal points at the viewer.
- **Confidence:** high

### Raycast auto-seating is GONE; fixed proud Z replaces it
- **Where:** `PlayTray.NewAnchor` / `PlayTray.FixedProudZ` (0.005)
- **CORRECTED (phase 4):** this entry used to add "`PlayTray.SeatOnBoardFace` / `ReseatProud` retained only for paths that need the true-surface projection". There were no such paths at HEAD — `ReseatProud` had **zero** callers, so `SeatOnBoardFace`'s only caller was itself dead, and the pair (plus `SeatStandoff`/`SeatProud`) is removed as Tier-0 dead code by Batch D of this refactor. Every widget seats at `FixedProudZ` through `NewAnchor`. The rule and the "no more −50 mm surprises" reason below are unchanged and are the whole point of the entry: they are what forbids re-adding a raycast seat.
- **Rule:** Widgets the debug menu does not expose seat at a predictable proud Z, not at a raycast hit.
- **Why:** The raycast reseat floated the gear −30…−50 mm off the Oak and Steel boards — "no more −50 mm surprises".
- **Established by:** `17862bb`
- **Breaks if:** Re-introducing a raycast seat (the removed `SeatOnBoardFace`) under `NewAnchor` because it is "more accurate", or replacing the XY preservation with the raw hit point.
- **Confidence:** high

### Board-extent measurement is local-space and clamped
- **Where:** `PlayTray.MeasureBoardLocalExtents`, `PlayTray.BoardExtentSanityMargin` (0.35)
- **Rule:** Extents come from per-renderer **mesh** bounds mapped through `worldToLocal × rendererLocalToWorld` (world AABB only as fallback), then clamped into `[authored, authored + 0.35]`.
- **Why:** The local mapping keeps a board **tilt** from inflating the extent the way a world AABB would. The clamp exists because a transient outlier — e.g. a card mid-flight still parented under the tray while it animates home — must never drag a board-anchored floater metres into the sky.
- **Established by:** `c188abf` fix(worldui): the enemy reveal is no longer planted behind the control board
- **Breaks if:** Using `mr.bounds` everywhere, or dropping the clamp as paranoid.
- **Confidence:** high

---

## 7. Buttons, presses and keycap geometry

### Depth-fire AND cooldown — both are required
- **Where:** `PlayTray.BoardButton.Update` (`_depthArmed`, `PressFireFraction` ~0.90, `PressRearmFraction` ~0.50), `PlayTray.BoardButton.Press` (`_nextPressTime`, `ButtonTuning.PokePressCooldownSeconds` 0.4 s)
- **Rule:** A poke fires only at ~90 % of travel, re-arms only after the cap rises past ~50 %, **and** must clear a 0.4 s cooldown. Laser presses skip the dwell but still go through the cooldown.
- **Why:** They cover different failure modes. Depth-fire makes a brush do nothing ("the press feels like actually pushing the key in"). The cooldown kills the retract/re-entry of one physical poke *and* the `PokeInteractor`'s hover flicker (exit+enter inside one poke), which the hysteresis alone could not. The laser goes through the cooldown to stop a cross-path poke+laser double-fire.
- **Established by:** `aba34a7` (depth-fire), `dd3dfd4` fix(buttons): debounce keycap double-trigger… (cooldown), ported from `4a75265` (pile stacks)
- **Breaks if:** Removing either as "covered by the other".
- **Confidence:** high

### `OnPoke` does not press an enabled button
- **Where:** `PlayTray.BoardButton.OnPoke` (sets `_hoverHand` only; disabled buttons still call `Press` for the log)
- **Rule:** Contact arms the follow, it does not fire.
- **Why:** So a brush does nothing — and the *rejected*-press gate log survives for disabled buttons, which is how a dead button is diagnosed from a hardware log.
- **Established by:** `aba34a7`, log contract from `32d3151`
- **Breaks if:** "Restoring" `Press(hand, "poke")` unconditionally.
- **Confidence:** high

### Press gate order and its logs
- **Where:** `PlayTray.BoardButton.Press`
- **Rule:** disabled → log REJECTED; `ActivationGuard > 0` → log SUPPRESSED; cooldown → log DEBOUNCED; then stamp the cooldown and fire. In that order.
- **Why:** "A silent dead button can no longer happen" — every attempt is logged with its source and, on rejection, the exact gate state. Stamping the cooldown before the guard check would debounce the legitimate retry.
- **Established by:** `32d3151` fix(cards): slot drops fire once per real release; CONFIRM/UNDO react again
- **Breaks if:** Hoisting the disabled check into `OnPoke` as a silent early-out.
- **Confidence:** high

### CONFIRM accident window, armed only by real gestures
- **Where:** `PlayTray.ConfirmGuardSeconds` (0.7), `PlayTray.NoteSlotActivity`, `PlayTray.ConfirmGuardRemaining` (also read by `WorldUI.TrayControlDockSurface`)
- **Rule:** For 0.7 s after a **real** drop or pluck, every CONFIRM activation — poke *and* laser, mod twin *and* docked native Continue — is suppressed. Never armed from `SyncFromGameState` placements.
- **Why:** CONFIRM sits right of the slots; the hand poked it while handling cards and the round started twice without a conscious confirm. Arming it inside `PlaceCard` would dead-lock CONFIRM, because the sync re-runs on every rebuild.
- **Established by:** `c78ce61` fix(cards): deliberate CONFIRM — poke dwell + slot-activity guard
- **Breaks if:** Applying it to poke only, or arming it from the placement path.
- **Confidence:** high

### Poke DWELL was removed; the accident window was kept
- **Where:** `PlayTray.BoardButton` dwell plumbing (`DwellHoldRange`, `_dwellHand`) — retained but defaulted to 0
- **Rule:** The dwell is off by user directive; the `ActivationGuard` window stays.
- **Why:** Explicitly separated in the commit subject: the user wanted instant fire but the accidental-CONFIRM protection kept.
- **Established by:** `239acb5` fix(cards): board gear/confirm/undo poke fires instantly — dwell removed per user directive (ActivationGuard accident window kept)
- **Breaks if:** Deleting the dwell code as dead (it is a configured-off feature, not dead code) **or** re-enabling it as "clearly intended".
- **Confidence:** high

### Keycap trigger collider spans the whole visible cap
- **Where:** `PlayTray.BoardButton.Create` (`boxFrontZ = min(capFrontZ, -0.006)` … `baseBackZ`; each cap shape sets its own `capFrontZ`)
- **Rule:** The box hugs the cap's actual frontmost local Z, not a constant.
- **Why:** The fixed box (`size.z` 0.02, centre −0.004 → front face −0.014) fell ~20 mm short of a boxy beveled keycap's front plateau (≈ −0.034). The frontmost 20 mm of the *visible* cap had no collider, so a laser aimed at the raised face passed over the box and never landed — gear/EINST and pin/FIXIERT were dead to the laser while poke worked from its 35 mm hover.
- **Established by:** `61ec005` fix(playtray): laser-hittable colliders for boxy caps + gear/pin opacity diag
- **Breaks if:** Restoring a constant depth, or changing cap geometry without updating `capFrontZ`.
- **Confidence:** high

### Keycap winding is outward, on a unit-scaled holder, with three submeshes
- **Where:** `CardMesh.BuildBeveledKeycap` (`AddQuad` winding), `PlayTray.BoardButton.Create` (unit-scale holder, three material instances), `PlayTray.BoardButton.LogCapDiagnostics` (`closedSolid`: 40 verts / 20 tris / 6-24-30 indices)
- **Rule:** Every face's right-hand normal points **outward**; the holder is uniformly scaled; top/bevel/wall are three separate material instances driven together by `SetCapColor`.
- **Why:** (a) The see-through "you can see the button's own underside through it" was an inside-out winding on a `Cull Back` material — every outward face was culled and only the far interior faces survived toward the viewer. (b) Uniform scale keeps the 45° bevel a true 45° in world space so `BoardLit` lights it. (c) The three-way value+hue split cures the "glassy" read — the cure was not "darker" but solid, warm, opaque colours on every face.
- **Established by:** `5b3ac2d` fix(cards): close see-through keycap — correct inside-out winding on BuildBeveledKeycap; `f85eda9`; `3848d00` fix(playtray): solid on-theme keycaps (item 1b)
- **Breaks if:** Non-uniformly scaling a holder to resize a cap, collapsing the three materials into one shared material, dropping the back cap quad, or refactoring the mesh without updating the diagnostic's expected counts (it then reports `CLOSED SOLID: NO` forever, silently).
- **Confidence:** high

### Button label sorting order beats its own face
- **Where:** `PlayTray.BoardButton.Create` (label `sortingOrder = 3`, native face `sortingOrder = 1`)
- **Rule:** The label's draw order, not its nearer Z, decides.
- **Why:** Both are transparent renderers with ZWrite off, so sorting — not depth — decides; without this the sprite drew over the text and every board button showed "4 black fields in a grid with a gold frame, no text".
- **Established by:** `70296c1` fix(worldui): native button frame (9-slice collapse + label z)…
- **Breaks if:** Removing it on the grounds that the label already sits proud in Z.
- **Confidence:** high

### The docked cluster label anchors off the cap TOP, not the centre
- **Where:** `PlayTray`/`WorldUI.ButtonCluster` docked label (`DockedLabelProud` 10 mm off `_capTopLocalY`)
- **Rule:** Both cap shapes anchor the label off the real cap top.
- **Why:** The round cylinder cap is taller than the shallow square/native cap, so a centre-relative offset put the round label nearly flush with its top face; at diorama scale the gap fell under a millimetre and the co-planar TMP and opaque cap face z-fought **per eye** under stereo.
- **Established by:** `dd3dfd4` (round label flicker)
- **Breaks if:** Re-expressing it as "centre + constant" for both shapes.
- **Confidence:** high

### Hide is logical-immediate, visual-deferred; the first frame pops silently
- **Where:** `PlayTray.BoardButton.SetVisible` (collider off, `_hoverHand = null`, `_depthArmed = true`, dwell cancelled immediately; `_ticked` / `_everShown` suppress the first settle)
- **Rule:** A dissolving button is not pressable, and the build-then-settle first frame produces no dust.
- **Why:** A still-collidable invisible button is pressable; and without the `_ticked` gate the board build storms dust bursts at scenario start.
- **Established by:** `aba34a7` (dust), `d8b2da7` (appear + storm guard)
- **Breaks if:** Deferring the logical hide to the end of the dissolve, or removing `_ticked` as an unnecessary flag.
- **Confidence:** high

### The item USE button does not resize the cluster
- **Where:** `PlayTray.SetItemUseConfirmVisible`, `PlayTray.GenericCount`, `PlayTray.GenericClusterY`, `PlayTray.BuildButtons` (cap size from `[BoardButtons]`); `PlayTray.GenericClusterButtonSize` retained but **unused**
- **Rule:** Adding or removing a cluster member changes only the member count and the **spacing**; every member keeps its tuned W×H at any count.
- **Why:** Cap size used to run through `GenericClusterButtonSize`, which shrank every cap once a third member joined — so the instant an item clipped into the use slot, Confirm and Undo silently resized and none of the three matched the dialled-in `[BoardButtons]` values.
- **Established by:** `fc34ae9` fix(items): use-button size, USE caption depth-fight, and the swimming clipped card
- **Breaks if:** Wiring `GenericClusterButtonSize` back in — it is right there and looks like dead code.
- **Confidence:** high

### Rest-button tuning rebuilds REST ONLY
- **Where:** `CardsDriver.ApplyBoardTuning` (`else if (_restTuningVersion != …)` → `_rest.Destroy()` + `_tray.PurgeDeadLaserTargets()` + `_rest.EnsureBuilt`)
- **Rule:** The `[RestButtons]` live-apply must **not** route through `PlayTray.RebuildAttachedControls`, and it must be an `else if` after the full control rebuild.
- **Why:** `RebuildAttachedControls` bumps the tray's own `_tuningVersion`, which would starve `PlayTray.ApplyButtonTuningIfChanged` of the gear/follow dashboard live-apply. `PurgeDeadLaserTargets` drops the destroyed caps' stale laser entries.
- **Established by:** `89fa5f7` feat(rest): wire short/long rest keycaps to [RestButtons] geometry binds
- **Breaks if:** Unifying the two rebuild paths "for consistency" — the dashboard tuning then silently stops applying and dead colliders keep receiving laser picks.
- **Confidence:** high

### Round caps wear the shared keycap material
- **Where:** `PlayTray.BoardButton.Create` (round branch → `NewKeycapMaterial(BoxCapShader())`), `CardMesh.GetRoundCap` (`RoundCapSegments` 64, cached by (diameter, thickness, segments))
- **Rule:** Round rest caps route through the same material helper as the square caps, and the disc is a 64-segment generated mesh, cached.
- **Why:** The round branch used a bare Standard material with a flat state colour — a plain plastic puck next to grain-textured square caps. And Unity's `PrimitiveType.Cylinder` is ~20-sided, which visibly facets at cap size. The cache exists so no per-button/per-frame rebuild happens.
- **Established by:** `253b40d` fix(cards): round rest caps wear the shared carved-grain keycap surface; `e17366a` fix(board): smooth round keycaps + materialize-from-dust appear
- **Breaks if:** Reverting to `PrimitiveType.Cylinder`, or dropping the mesh cache key.
- **Confidence:** high

### Bundled shaders must be loaded from the bundle, not `Shader.Find`
- **Where:** `PlayTray.OverlayShader`, `PlayTray.BoardLitShader`
- **Rule:** After `Shader.Find` fails, probe every loaded `AssetBundle` by explicit asset path.
- **Why:** A bundled shader is not discoverable via `Shader.Find` until something loads it into memory. `BoardLit` resolves only because a bundle prefab's material references it; `GloomhavenVR/Overlay` is referenced **only** by runtime C#, so it was never loaded and `Shader.Find` returned null — the root cause of the still-invisible gear/glows/cluster after the "fix" that set `_ZTest`.
- **Established by:** `cefee9c` fix(cards): actually LOAD the bundled Overlay shader + Oak button fit
- **Breaks if:** Deleting the bundle probe as dead code because `Shader.Find` "works for BoardLit".
- **Confidence:** high

### A forced-ZTest helper uses per-renderer instances and two ZTest property names
- **Where:** `WorldUI/NativeButtonSkin.cs` and `WorldUI/ActorBars.cs` — the two **live** implementations, both of which set `_ZTestMode` under a `HasProperty` guard
- **CORRECTED (phase 4):** this entry's `Where:` used to be `PlayTray.RenderOnTop`, which had **zero** call sites at HEAD (19 repo-wide `RenderOnTop` hits: 1 declaration, 18 comments, all of them saying the widget in question no longer uses it) and is removed as Tier-0 dead code by Batch D of this refactor. The lesson is not dead — it is implemented in the two files named above — so the entry now points at them. Whoever writes the *next* draw-over-the-board helper is the audience.
- **Rule:** `.materials` (instances, never `sharedMaterial`), and set `_ZTest` **and** `_ZTestMode` under `HasProperty` guards.
- **Why:** Instances so no shared bundle material is mutated globally. Two names because the quad/Tint (Standard) path exposes `_ZTest` while TextMeshPro's distance-field material exposes `_ZTestMode` — an earlier version set only `_ZTest` under a `HasProperty` guard and was therefore a **silent no-op** on every non-TMP widget.
- **Established by:** `cb62991`, corrected in `d56e4c8` fix(cards): Overlay-shader board HUD, Oak button sizing, dock gating
- **Breaks if:** Dropping either property name, or switching to `sharedMaterial`.
- **Confidence:** high

---

## 8. Slots, drops and placement

### "What glows is what drops" — the highlight is the authoritative accept rule
- **Where:** `CardsDriver.OnCardReleased` (rule 1: the slot that was glowing for **this** card, tracked per card via `_snapHighlightCard`), `PlayTray.SlotCaptureRadius` (0.25 m, rule 2 fallback)
- **Rule:** The glowing slot is accepted directly; the radius is only a fallback; the highlight is tracked per card so a two-hand release cannot consume the other card's highlight.
- **Why:** Hardware logs showed **every** real drop bouncing — releases consistently landed 14–16 cm real from the slot centre against a 12 cm radius, because the release gesture itself moves the hand, while the slot hover glow *had* triggered moments before.
- **Established by:** `413042f` fix(cards): drop accepts the glowing slot; radius fallback widened to 0.25 m
- **Breaks if:** Tightening `SlotCaptureRadius` to the slot's visual size to stop "cross-slot capture" — the glow already disambiguates, and tightening reintroduces dropped placements. (Explicitly re-affirmed at the 1.3× slot resize.)
- **Confidence:** high

### `SlotNear` samples both the card centre and the hand
- **Where:** `PlayTray.SlotNear`, `PlayTray.PickFieldNear` (`min(cardPos→slot, handPos→slot)`, scaled by `_root.lossyScale.x`)
- **Rule:** Either sample accepts.
- **Why:** The pinch-grip held pose offsets the card centre away from the palm, so "hand over the slot" and "card over the slot" must both work.
- **Established by:** `4b627f0` feat(cards): generous slot capture, snap-preview glow, snap haptic + drop logging
- **Breaks if:** Dropping one sample as duplicated maths, or removing the lossy-scale multiply.
- **Confidence:** high

### A HELD card is never re-homed
- **Where:** `PlayTray.PlaceCard`, `PlayTray.PlacePickCard`, `PlayTray.SetOverlayOffset`, `CardsDriver.RelayoutField`, `PlayTray.SyncFromGameState`
- **Rule:** Every path that asserts a home pose skips `card.IsHeld`.
- **Why:** `SetHome` re-parents, which yanked the card out of the hand and pulled it onto the slot — **the source of the phantom ACCEPTs**: the card then sat inside the capture radius, so the next unrelated grip release self-accepted it as a placement. Named in-source as "the phantom-ACCEPT lesson".
- **Established by:** `32d3151`
- **Breaks if:** Hoisting the guard out because "`SetHome` is idempotent".
- **Confidence:** high

### `SyncFromGameState` preserves the player's chosen slot
- **Where:** `PlayTray.SyncFromGameState`, `PlayTray.PlaceRoundCardIfMissing`
- **Rule:** The sync only evicts occupants that left the round and seats round cards that are in **no** slot. It never forces the initiative card into slot 0.
- **Why:** The game tracks only the *set* of two `RoundAbilityCards` plus the initiative leader — never the VR slot. Forcing a mapping fought the player's own placement every rebuild, and unconditional re-placing spammed the log and re-parented held cards.
- **Established by:** `c2625f2` fix(cards): free slot placement + lock cards after selection confirm; `32d3151`
- **Breaks if:** "Simplifying" back to `PlaceCard(initiative, 0); PlaceCard(other, 1);`.
- **Confidence:** high

### The origin slot is a valid drop target
- **Where:** `CardsDriver.OnCardReleased`, `CardsDriver.UpdateSlotHighlight`
- **Rule:** No origin self-exclusion; unselect requires releasing away from **both** slots.
- **Why:** The old rule force-excluded the origin, so the only way back into the tray was the *other* slot — restoring a card to its exact original slot was impossible. Both the release path and the glow telegraph had to change together.
- **Established by:** `c7e6266` fix(cards): return card to origin slot; visible keycap side walls (following `02889d8`)
- **Breaks if:** Re-adding `if (slot == originSlot) slot = -1;` as a "no-op drop" optimisation.
- **Confidence:** high

### Swap onto an occupied slot: Unselect FIRST, then Select, both queued
- **Where:** `CardsDriver.OnCardReleased` (fan → occupied slot)
- **Rule:** Two `CardActionQueue.Enqueue` calls, occupant-unselect before newcomer-select, neither called inline.
- **Why:** The round pile holds at most two, so selecting first is rejected and the swap silently drops a card. Queued so they serialize one per frame in that order, and so the blocking spin-wait never runs inside an interaction callback.
- **Established by:** `02889d8` fix(cards): return placed cards to hand; swap fan card onto occupied slot
- **Breaks if:** Reordering, or calling either API directly from the release handler.
- **Confidence:** high

### Fan-order splice happens in the unselect COMPLETION
- **Where:** `CardsDriver.OnCardReleased` (tray take-back), `CardsDriver._fanOrder`, `CardsDriver.ReorderFanBuffer`
- **Rule:** The gap's neighbour ids are captured at release time; the `_fanOrder.Insert` runs inside the queued unselect's completion callback.
- **Why:** The fan may change while the unselect is queued, and splicing earlier races `ReorderFanBuffer`'s prune — the id is not in the game hand until the unselect lands, so the prune drops it and the position is lost.
- **Established by:** `4ae5335` fix(cards): tray→fan-gap insert, no slot glow for browse cards, trimmed-sprite mip bake (T1)
- **Breaks if:** Splicing immediately "since we already know the gap".
- **Confidence:** high

### One drop per real release — the live-grab session set
- **Where:** `CardsDriver._liveGrabs`, `CardsDriver.OnCardGrabbed` / `OnCardReleased` (a Released without a live session is dropped with a Warn, before any slot logic)
- **Rule:** A slot placement fires exactly once per real user release.
- **Why:** Double-fires and stale events after a rebuild/hot-reload produced phantom placements. A card recycled mid-grab is removed from the set so its release cannot route a drop.
- **Established by:** `32d3151`
- **Breaks if:** Treating `_liveGrabs` as a debug aid and removing the `if (!_liveGrabs.Remove(card)) return;`.
- **Confidence:** high

### The glow gate and the release refusal share one predicate
- **Where:** `CardsDriver.UpdateSlotHighlight` (MustRestInsteadOfPlay gate) and `CardsDriver.OnCardReleased` (Drop REFUSED branch), both with the `slot >= 0 && !wasInTray` carve-out; `CardsGameApi.MustRestInsteadOfPlay`
- **Rule:** A drop the release path would refuse must not be telegraphed, and tray-origin drops (reorder / take-back) stay allowed because they never grow the round pile.
- **Why:** Otherwise the mod promises a placement and then silently rejects it. Fixing one side only reintroduces exactly that.
- **Established by:** `1034ab6` (task #4b)
- **Breaks if:** Fixing one side, or dropping the `!wasInTray` carve-out (reorder and take-back get refused too, stranding the player).
- **Confidence:** high

### Read-only viewer cards never telegraph a slot
- **Where:** `CardsDriver.IsReadOnlyViewerCard`, applied in **both** branches of `CardsDriver.UpdateSlotHighlight`
- **Rule:** Browse-arc and active-column cards get no slot glow.
- **Why:** Their release always returns them to the viewer, so a yellow slot overlay is a lie. Testing only `_browser.Contains` misses the active column, which has the identical read-only release contract.
- **Established by:** `4ae5335` (T2)
- **Breaks if:** Applying the check in one branch only, or testing only the browser.
- **Confidence:** high

### Wanted-slot count comes from placeable state, not from "empty"
- **Where:** `CardsDriver.UpdateWantedSlots`, `CardsGameApi.SelectionCardsStillWanted`, `CardsGameApi.PickCardsWanted`
- **Rule:** The number of glowing slots is derived from the same authoritative state the placement gate reads: `min(2 − RoundAbilityCards, HandAbilityCards)`, 0 when the player must rest, and the `maxCardsSelected` remainder for extra-turn picks.
- **Why:** Lighting every empty slot unconditionally meant that with one unplayable card left, both overlays still pulsed; and a hardcoded max of 2 made a **one**-card burn keep wanting the right slot after the left filled.
- **Established by:** `85bbb8c` (pick count), `0ed8d3c` fix(cards): overlay count from placeable state; laser-only card hover (task #4)
- **Breaks if:** Reverting to "light every empty slot" or a literal 2.
- **Confidence:** high

### Overlays are gated on game-state commit-blocked, computed once per tick
- **Where:** `CardsDriver.UpdateOverlayGate` (state flip applied **before** the log throttle), `CardsGameApi.IsCardCommitBlocked`
- **Rule:** The verdict is computed once per tick from the game's own state (`UIResultsManager.IsShown`, `ActionProcessor.CurrentPhase == ScenarioEnded`, `StoryController.IsVisible` + its display delay) and read by both overlay paths. The ESC/pause family is **deliberately excluded**.
- **Why:** The yellow wanted-slot overlays and the snap glow kept pulsing during the victory/defeat window and the scenario-start narrator dialog, where no card can be placed. Computing it once keeps the two consumers frame-consistent. The pause family is excluded because cards stay fully interactive under the reachable pause menus.
- **Established by:** `53e9124` fix(cards): suppress slot overlays while results window / narrator dialog is open
- **Breaks if:** Hoisting the `Time.unscaledTime < _overlayGateNextLogAt` throttle to the top of the method — a very natural-looking "throttle the whole thing" edit that leaves the gate stuck for up to a second.
- **Confidence:** high

### Short-rest suppresses the wanted glow entirely
- **Where:** `CardsDriver.UpdateWantedSlots` (`IsShortRestChoosing` gate)
- **Rule:** No wanted-slot glow while a short-rest burn/redraw choice is open.
- **Why:** A short rest is not `IsShortRestSelected` once its confirm ran `Select(false)`, and no card sits in `_occupants` (`PlacePickCard` deliberately leaves them empty), so the existing checks do not cover it: both empty slots glowed, and the pulsing teal behind the display-only sacrificed card made it look like a glitch.
- **Established by:** `cd42d15` (task #9)
- **Breaks if:** Assuming the occupant/selected checks already cover it.
- **Confidence:** high

### Slot roots scale 1.3×; captions divide it back out
- **Where:** `PlayTray.SlotScale` (1.3), applied before the dependent visuals build; caption fit boxes divided by `SlotScale`
- **Rule:** Frames, highlights, badge and parked cards enlarge together; caption metrics compensate.
- **Why:** The collision budget (highlight outer edges ±0.129 clear of the rest plate at −0.19 and the CONFIRM column at ~0.177) is computed against this exact factor, and the one-pitch no-collide guarantee for captions holds only if their effective size is unchanged.
- **Established by:** `24629af` feat(cards): card slots 1.3x — frames, highlights, badge and parked cards enlarged
- **Breaks if:** Applying the scale after the highlights build, or giving a caption raw metres because the `/ SlotScale` looks like a typo.
- **Confidence:** high

### Two glow layers at two Z depths, both depth-correct
- **Where:** `PlayTray.SlotGlowBaseZ` (−0.006, gold snap) vs `PlayTray.WantedGlowBaseZ` (−0.004, teal wanted); `PlayTray.SlotHomeOffsetFor` / `SetOverlayOffset`
- **Rule:** The gold snap glow sits prouder than the teal wanted glow; both are negative-Z (toward the player) with **no** `RenderOnTop`; the resting card and both glows share the same per-board overlay offset and pair spread.
- **Why:** The Z split preserves the gold-over-teal ordering. Negative Z + no `RenderOnTop` makes them occlude naturally instead of shining through the slab. And the card must move with its glow, or tuning the Overlays element separates the visual cue from the physical target.
- **Established by:** `424d7db` (depth-correct glows), `1126b6f` (overlay-coupled slot)
- **Breaks if:** Unifying the two base Z values, or pushing them back to `RenderOnTop`.
- **Confidence:** high

### The pick field hides the play slots — HISTORICAL, the field no longer exists
- **Where:** was `PlayTray.SetPickFieldVisible`
- **CORRECTED (phase 4):** documented as live; it was not. `SetPickFieldVisible` had **zero** external callers, so `_pickFieldVisible` could never be true — `BuildPickField` allocated five `GameObject`s, a glow material and a TMP caption on every board build and ended with `SetActive(false)`, and nothing ever turned them on. The whole cluster is removed as Tier-0 dead code by Batch D of this refactor. Pick flows home into the slot recesses instead. Kept here because the *design* rule is what a future pick-field-shaped feature must obey, and because `Net/RemoteBoardFurniture` still builds and shows its own mirror of it — see the open MP question in `REVIEW-Cards.md` §7.
- **Rule (if the field ever returns):** While the pick field shows, both play-slot roots hide; hiding it restores them and clears the field highlight.
- **Why:** Two empty slot frames flanking a third read as three competing targets. The slots are guaranteed empty in pick modes.
- **Established by:** `eb1e206` feat(cards): home single-card picks into the left slot…
- **Breaks if:** Showing the field alongside the slots "since they're empty anyway".
- **Confidence:** high

### Pick cards never enter `_occupants`
- **Where:** `PlayTray.PlacePickCard`
- **Rule:** Pick/short-rest cards home into slot geometry but leave the slots logically empty.
- **Why:** `SyncFromGameState` and `SlotOf` keep their CardsSelection meaning only if occupancy means "a selected round card".
- **Established by:** `eb1e206`
- **Breaks if:** Recording occupancy "for consistency with `PlaceCard`".
- **Confidence:** high

### A 3rd+ pick card is laid beside Slot2, logged, never silently capped
- **Where:** `PlayTray.PlacePickCard` fallback, `CardsDriver.RelayoutField` (`_loggedFieldOverflow`, re-armed when the count drops back to ≤ 2)
- **Rule:** Graceful fallback with exactly one log line.
- **Why:** A silent cap would make a card vanish in a flow nobody has hardware-tested.
- **Established by:** `eb1e206`
- **Breaks if:** Capping `_fieldCards` at 2.
- **Confidence:** high

---

## 9. Rebuild ordering and animation ownership

### The Cards tick is wrapped in an attribution guard — but only the interaction tail
- **Where:** `CardsDriver.Update` (try/catch scope), `CardsDriver.TickInteractionsAndStatus`, `CardsDriver.NoteTickThrow`
- **Rule:** `CardActionQueue.Pump`, `HandSuppression.Tick` and `Rebuild` stay **outside** the guard; `TickInteractionsAndStatus` contains no top-level early return. The first throw logs once with its stack under a `[Cards]` tag; repeats are summarised once per 10 s.
- **Why:** Unity logs an unhandled `MonoBehaviour.Update` throw in this Player build **without a stack**, so a per-frame flood is untraceable to a subsystem. But a throw in the interaction path must never starve the queue pump — that is the reopen guarantee. And the guard must not itself become the flood.
- **Established by:** `fd82d20` fix(cards): isolate + attribute the per-frame Cards tick path
- **Breaks if:** Wrapping the whole `Update` body (card commits then deadlock on a throw), or adding an early `return` inside `TickInteractionsAndStatus`.
- **Confidence:** high

### `Update` ordering: board switch → rebuild → placement → resume → watchdog → tuning → pose watch
- **Where:** `CardsDriver.Update`
- **Rule:** `RebuildBoard` runs before `Rebuild`; `TickBoardPoseWatch` runs last.
- **Why:** `PlayTray.EnsureBuilt` early-returns while its root exists, so a mere `_dirty` rebuild would keep the old board. And the one-frame pose token only works if the watch observes the move in the same frame the mover set it.
- **Established by:** `a0a39e2` (board switch), `1a9b071` (pose watch)
- **Breaks if:** Merging `_boardChanged` into `_dirty`, or moving the watch next to the placement calls.
- **Confidence:** high

### Previous-frame card sets are snapshotted at the TOP of `Rebuild`
- **Where:** `CardsDriver.Rebuild` (`_lastHalfCards`, `_lastTrayCards`, `_lastVisibleCards` captured before `_tray.SyncFromGameState`)
- **Rule:** They read `_tray.Occupant(s)` live, while those are still the **outgoing** character's cards.
- **Why:** These three sets are what distinguish a cleared round card (fly to pile) from a character-switch slot card (vanish in place) from a just-dropped card (keep its release glide).
- **Established by:** `8d54444` fix(cards): in-place appear for slot cards, short-rest discard choreography…
- **Breaks if:** Moving the snapshot after the sync, or reusing `_halfBuffer` post-clear.
- **Confidence:** high

### The park sweep is a strict `else if` chain
- **Where:** `CardsDriver.Rebuild` park sweep: `TryStartFlyToPile` → `else if TryStartBurnFly` → `else if` Vanish → `else` Park
- **Rule:** Mutually exclusive, in that order, with intentionally empty bodies on the first two.
- **Why:** "Fly-to-pile owns this card — never also vanish it (no double animation)". And the burn fly must run **here**: `Rebuild` runs before `TickBurnToPile` in the same `Update`, so without it the card was already parked (pose gone) by the time the burn watcher noticed — the "card just vanishes" report.
- **Established by:** `f1e5359` (damage-burn), `6168369` fix(cards): fly-burn from true position (no teleport), animate board-card appear/disappear
- **Breaks if:** Turning them into independent `if`s, or "cleaning up" the empty blocks into a combined condition that falls through to `Park`.
- **Confidence:** high

### Cards mid-animation own their transform
- **Where:** `CardsDriver.Rebuild` zone loop (`if (card.IsFlying || card.IsVanishing) continue;`)
- **Rule:** No re-zone, re-home or re-park while an animation runs.
- **Why:** Any of those teleports the card out of its animation, or double-hides it while it shrinks out.
- **Established by:** `38c171e`, `6168369`
- **Breaks if:** Dropping the guard because "`Park` is idempotent" — it is not, with respect to a running animation.
- **Confidence:** high

### Last-known world pose is recorded for NOT-PARKED cards, not for active ones
- **Where:** `CardsDriver.Rebuild` (`if (card.GameCard != null && !IsParked(card))`), `CardsDriver.IsParked`, `_lastCardWorldPos`/`_lastCardWorldRot`
- **Rule:** The predicate is "not parked", explicitly **not** `activeInHierarchy`.
- **Why:** A card in a CLOSED hand fan is inactive (the fan root is disabled) yet its transform still carries its true world pose at the player's hand — which is exactly where a card burned straight out of the hand must fly **from**.
- **Established by:** `6168369`
- **Breaks if:** "Simplifying" to `card.gameObject.activeInHierarchy` — burns out of a closed fan lose their origin and are skipped entirely.
- **Confidence:** high

### With no recorded pose, the burn animation is SKIPPED
- **Where:** `CardsDriver.TryAnimateBurn` (`if (!hasFrom)` → Warn + skip), `CardsDriver.BurnSlab`
- **Rule:** Never substitute an origin.
- **Why:** The old fallback flew a card-back slab **from the discard pile** — teleporting the card to a different place before the flight, which was the exact reported glitch ("glitched over the pile then appeared somewhere else"). With no recorded pose there is genuinely nowhere to fly from.
- **Established by:** `6168369`
- **Breaks if:** Adding "any sensible default origin" (board centre, discard stack) to avoid the Warn.
- **Confidence:** high

### Flight orientation is locked for the whole flight
- **Where:** `VRCard.FlyToPile` / `FlyFromPile` (single `_flyRot` captured once), `CardsDriver.BurnSlab.Launch` (`fixedRot`, never updated)
- **Rule:** No billboarding, no reorient at launch, arrival or intro-settle.
- **Why:** Verified requirement: the card must stay equally oriented start to finish and never snap to a billboard or the pile's orientation.
- **Established by:** `8d54444` (issue 3), `6168369`
- **Breaks if:** Adding a look-at "so the player can see it".
- **Confidence:** high

### Flights arc along WORLD up, eased by one smootherstep, floored in duration
- **Where:** `VRCard.SmootherStep`, `VRCard.FlyArcOffset`, `VRCard.FlyArcHeightFraction` (0.55), `VRCard.MinFlySeconds` (0.35), `CardsDriver.BoardArcMin` (scaled by the board's live `lossyScale.x`), `CardsDriver.FlyToPileSeconds` (0.4)
- **Rule:** One smootherstep-eased parameter drives slide **and** arc **and** scale; the arc lifts along `Vector3.up`, ignoring the caller's board-up; the height is `max(BoardArcMin, distance · FlyArcHeightFraction)`; the duration is floored; `dt` is unscaled with a 0.05 s hitch cap.
- **Why:** An ease-out slide fighting a linear bow put the spatial midpoint away from the temporal midpoint where the arc peaks — a lopsided, stepped look. World up so the arc reads toward the ceiling regardless of board tilt. The absolute minimum arc exists because a flat skim over the board was unreadable on a short hop, and it is scaled so it tracks board size. Unscaled time because card phases pause `timeScale`; the hitch cap stops a GC/loading hitch teleporting the card most of the way in one frame.
- **Established by:** `f1e5359` (arc), `8d54444` (min arc), `25fe23a` feat(cards): smoother world-up card flights + crumble/materialize dust
- **Breaks if:** Restoring a per-term ease, using the caller's `arcUp`, dropping the duration floor, or switching to `Time.deltaTime`.
- **Confidence:** high

### Burn claims its widget at LAUNCH, and the baseline is rebuilt from scratch
- **Where:** `CardsDriver.TryStartBurnFly` / `TryAnimateBurn` (`_knownBurntWidgets.Add(widget)` at launch, `_lastCardWorldPos` consumed), `CardsDriver.TickBurnToPile` (baseline rebuilt from the current pile at the tail)
- **Rule:** Claim before the watcher's diff sees it; rebuild rather than union.
- **Why:** The flight lasts 0.4 s, far longer than a frame, so a deferred claim lets the same burn play a real-card flight *and* a fallback slab simultaneously. Rebuilding drops departures, so a recovered-then-reburned card animates again.
- **Established by:** `f1e5359`, `6168369`
- **Breaks if:** Deferring the add to the completion callback, or changing the tail to `UnionWith` "since sets dedupe anyway".
- **Confidence:** high

### Burn watching re-baselines silently on a hand change
- **Where:** `CardsDriver.IsFreshBurn` (`!ReferenceEquals(hand, _burnWatchHand)` → false), `CardsDriver.TickBurnToPile` (re-baseline branch records and returns), `CardsDriver.TickInteractionsAndStatus` (`_burnWatchHand = null` when the tray hides)
- **Rule:** Until the baseline is seeded for **this** hand, nothing is a fresh burn.
- **Why:** On a character tab switch or scenario load a hand's long-burned cards must never animate retroactively — the same "no storm" discipline the dock animations use.
- **Established by:** `f1e5359`
- **Breaks if:** Seeding and diffing in the same tick.
- **Confidence:** high

### A card already owned elsewhere is skipped entirely, not fallen back
- **Where:** `CardsDriver.TryAnimateBurn` (`ownedElsewhere` computed before the live-card branch, with its own `return`)
- **Rule:** Held / flying / `_flyingToPile` / `_lastHalfCards` cards do not even reach the slab fallback.
- **Why:** The turn-clear sweep or a live grab is already animating that exact card. Merging the test into the live-card `if` lets the ownership case fall through and launch a duplicate slab.
- **Established by:** `f1e5359`
- **Confidence:** high

### First-rebuild animation suppression
- **Where:** `CardsDriver._dockAnimSuppressed` (set in `OnEnable`, `OnHandDestroying`, `RebuildBoard`; cleared at the **end** of `Rebuild`)
- **Rule:** Dock appear/disappear animations are suppressed for exactly the first `Rebuild` after a board build or teardown.
- **Why:** The scenario-load population would otherwise storm. Clearing at the end (after both appear loops) is what makes every *later* change animate.
- **Established by:** `6168369`
- **Breaks if:** Clearing it at the top of `Rebuild` (guard becomes a no-op), or never clearing it (real character switches pop).
- **Confidence:** high

### Appear only for cards that were parked/hidden
- **Where:** `CardsDriver.Rebuild` (issue-1 appear loop gated on `!_lastVisibleCards.Contains(card)`; issue-2 appear loop placed **after** `_half.SetCards`)
- **Rule:** A card that was visible anywhere last rebuild keeps its release glide; the docked-card appear runs after the home poses are asserted.
- **Why:** A card the player just dropped in from the fan must glide, not scale in. And an appear that runs before `SetCards` grows toward a stale pose and then snaps.
- **Established by:** `8d54444`
- **Breaks if:** Testing `_lastTrayCards` instead of `_lastVisibleCards` — every manual fan→slot drop then gets a scale-in that eats its glide.
- **Confidence:** high

### The card body is hidden during appear/disappear
- **Where:** `VRCard.SetBodyVisible`, called from `VRCard.Vanish` / `VRCard.PlayAppear`; restored on every exit path including `OnDisable` and `Park`
- **Rule:** Renderer `.enabled` only — no material is created, mutated or cloned.
- **Why:** Two symptoms, one cause. The vanish only fades the **face art** (a `CanvasGroup`) while the 3D body stays fully opaque, because the settle shrink only reaches `DustSettleScale` (0.82). (1) `CardMesh`'s near-black `EdgeColor` front is progressively **uncovered** as the art fades, so the last two thirds of the vanish is a black card-shaped slab sitting in a freshly lit teal slot overlay — the user's "sieht aus wie ein Glitch". (2) The opaque slab ZWRITES, and the additive `Overlay`-shader wanted-glow depth-tests against it, producing jagged teal wedges radiating from the slab's centre-fan triangulation. Materials must not be faded because the front/rim/back materials are **shared** with every other card and with `Net.RemoteHandFan`'s opponent hand backs.
- **Established by:** `ce4c1a5` fix(cards): the vanishing card no longer flashes black over the slot overlay
- **Breaks if:** "Simplifying" to a material alpha fade (tints every card in the scene), or skipping the restore on any exit path (a pooled card comes back invisible).
- **Confidence:** high

### `_instantNext` is a one-frame flag consumed by `Update`
- **Where:** `VRCard.SetHome(..., instant)` → `_instantNext`; cleared by `VRCard.UpdateBody`, `FlyToPile`, `FlyFromPile`, `Vanish`, `PlayAppear`, `OnRelease`
- **Rule:** Instant seeds (fan open/close animation, pool return) win over the release glide, and the flag never survives a frame.
- **Why:** `GrabbableBehaviour.DetachFromHand` restores the pre-grab parent **and local pose** (`worldPositionStays: false`), so a released card teleported to its origin. `OnRelease` captures the world pose across the detach and lets the exponential home-lerp fly it back — but the fan's own animation must still be able to hard-seed a pose.
- **Established by:** `bb3502c` fix(cards): glide-back on release + roll-only reveal gate in degrees
- **Breaks if:** Making `instant` a persistent mode rather than a one-shot.
- **Confidence:** high

### Release glide window uses unscaled time
- **Where:** `VRCard.ReleaseGlideSeconds` (0.35), mirrored in `ItemsPile.ItemChip.ReleaseGlideSeconds`
- **Rule:** The glide keeps running while the game pauses `timeScale`.
- **Why:** The game pauses during card selection, which is exactly when cards are released.
- **Established by:** `bb3502c`
- **Confidence:** high

### Short-rest sacrifice: animate BEFORE parking, and record its seat pose explicitly
- **Where:** `CardsDriver.RemoveShortRestCard` (`TryStartBurnFly` before the park; `if (!_shortRestCard.IsFlying)` park), `CardsDriver.PresentShortRestCard` (seeds `_lastCardWorldPos`/`Rot` from `VRCard.TryGetHomeWorldPose`)
- **Rule:** Both are required and neither is duplicated bookkeeping.
- **Why:** `RemoveShortRestCard` runs inside `Rebuild`, i.e. **before** `TickBurnToPile` in the same `Update`; parking on the spot left the watcher with a parked card and no pose ("animation skipped"), so the sacrifice simply blinked out of the left slot. And the `Rebuild` zone loop provably cannot record the present-time pose, because the fly-in marks the card flying and the loop skips flying cards — and no further `Rebuild` need happen before the player commits the burn.
- **Established by:** `5477618` feat(cards): burned cards slide into the burnt pile instead of vanishing
- **Breaks if:** Reordering to park-then-animate, parking unconditionally, or deleting the explicit pose seed.
- **Confidence:** high

### `TryGetHomeWorldPose` exists because the live transform can read the pile
- **Where:** `VRCard.TryGetHomeWorldPose`
- **Rule:** A card asked for its "true position" during the very frame `FlyFromPile` seeds the flight start reports its **seat**, not the pile.
- **Why:** The live transform then reads the pile, while the pose that matters for a later burn animation is the seat it is heading to.
- **Established by:** `5477618`
- **Breaks if:** Replacing calls with `transform.position` because "it is the same once the flight lands".
- **Confidence:** high

### Stale-occupant eviction before the sacrifice docks
- **Where:** `CardsDriver.PresentShortRestCard` (re-homes any tray occupant to the fan first)
- **Rule:** The sacrifice and a played card must never overlap in a recess.
- **Why:** `PerformShortRest` runs `DeselectAllCards` game-side, so a card the mod still shows in either slot is stale by definition — and this covers any ordering where the sync lags the present by a frame.
- **Established by:** `1034ab6` (task #4b), `7b05085`
- **Breaks if:** Relying on `SyncFromGameState` having already evicted them.
- **Confidence:** high

### `PollShortRest` is polled every tick and only sets `_dirty`
- **Where:** `CardsDriver.PollShortRest`, guarded exactly like `Rebuild`
- **Rule:** `Rebuild` remains the sole executor.
- **Why:** `PerformFinalShortRest` re-points `ShortRestedCard` at the alternate card **without** a mode or selection change, so nothing else marks the driver dirty. Two executors would fire the redraw fly-back twice.
- **Established by:** `7b05085` feat(cards): lay the short-rested card at the board centre
- **Breaks if:** Making it event-driven, or letting it present/remove directly.
- **Confidence:** high

### Pile fate is read from the card's OWN actor
- **Where:** `CardsDriver.PileFateOf`
- **Rule:** Read `widget.PlayerActor`'s piles; fall back to the presented hand only when the widget has no owner; an unresolved fate defaults to **Discard**, not Burnt.
- **Why:** In a two-character sequential turn a card cleared during the *other* character's turn belongs to a different actor than the currently presented hand, so using `hand.PlayerActor` flies cards into the wrong pile.
- **Established by:** `38c171e` feat(cards): rest buttons hide when irrelevant; played cards fly to their pile on clear
- **Breaks if:** Using the presented hand directly — the obvious read.
- **Confidence:** high

---

## 10. Stale-state traps in the game API

> `CardsHandUI.currentMode` is the single most dangerous field in this subsystem.
> It is only re-driven by the next `CardsHandManager.Show(...)`, which does **not**
> run during an enemy turn — so it is stale for most of a scenario. Three separate
> deadlocks and one "cards still reclaimable during the enemy turn" bug came from
> trusting it.

### Interactivity gates on the PHASE, never on `currentMode`
- **Where:** `CardsGameApi.IsSelectionPhase`, consumed by `CardsDriver.Rebuild` (`selecting`) and `CardsDriver.PollModeChange`
- **Rule:** The `CardsSelection` layout is interactive only while `PhaseManager.PhaseType == SelectAbilityCardsOrLongRest` (or the actor is picking for an extra turn).
- **Why:** `currentMode` stays `CardsSelection` after confirm, so the mod sat in a grabbable selection fan all through the enemy turn with the played cards still reclaimable and the fan bound. Every selectable/valid path in the game's own `CardsHandUI.SetMode` is gated on exactly this phase.
- **Established by:** `c2625f2` fix(cards): free slot placement + lock cards after selection confirm
- **Breaks if:** Trusting `mode` alone — the most tempting simplification in the file.
- **Confidence:** high

### Docked round cards gate on `IsActionTurn`
- **Where:** `CardsGameApi.IsActionTurn` (`Choreographer.CurrentActor` IS this hand's locally-controlled player), consumed by `CardsDriver.Rebuild` and `CardsDriver.UpdateActive`
- **Rule:** The board shows a character's cards only during that character's own action turn (or the shared selection phase).
- **Why:** Same stale-mode trap: the two played cards stayed docked all through the enemy turn. The active-card column had the same bug and needed the same gate.
- **Established by:** `fd8b174` (task #5), `c3a3ddd` feat(cards): hide the active-card column while another actor is up (#5)
- **Breaks if:** Reverting to the raw mode.
- **Confidence:** high

### The presented hand resolves from `Choreographer.CurrentActor`
- **Where:** `CardsGameApi.ActionSelectionHand`, `CardsDriver.CurrentHand` (`ActionSelectionHand() ?? ActiveHand()`)
- **Rule:** During `ActionSelection` the acting actor's hand wins over `CardsHandManager.CurrentHand`.
- **Why:** The game's own click gate (`FullAbilityCard.OnAbilityClick`) silently rejects any half whose owner is not `CurrentActor`. When the turn passed from character 1 to character 2 **within** `ActionSelection`, `CurrentHand` lagged, the mod kept the first character's cards docked, every real uGUI click hit the guard and no-oped — a hard deadlock.
- **Established by:** `7fa3095` fix(cards): break second-character ActionSelection deadlock (present acting actor's hand)
- **Breaks if:** Collapsing to `ActiveHand()`.
- **Confidence:** high

### `PollModeChange` needs all four signature keys
- **Where:** `CardsDriver.PollModeChange` (mode, `IsSelectionPhase`, acting actor, `ActionSelectionSignature`)
- **Rule:** None of the four is redundant.
- **Why:** Each covers a transition the others miss. `LoseCard` entry during the actor's own turn raises no mod event (long rest deadlocked without it). The selection **lock** edge leaves `currentMode` stale at `CardsSelection`. And the character-to-character hand-off inside `ActionSelection` changes neither the mode nor `IsSelectionPhase` — that one is the second-character deadlock.
- **Established by:** `eb1e206` (mode poll), `c2625f2` (phase key), `7fa3095` (actor + signature)
- **Breaks if:** Trimming the tuple — each removal reinstates a distinct hard deadlock.
- **Confidence:** high

### Round cards are collected from the ROUND PILE, with the phase pair only additive
- **Where:** `CardsDriver.CollectRoundCards` (`IsInRound` authoritative; `CardsActionControlller.topCard/bottomCard` only added)
- **Rule:** The phase machine's pair is never used alone.
- **Why:** It is a static singleton that lags a same-mode turn hand-off, so a stale pair could dock the wrong character's cards. It is still added because it covers the extra-turn pile.
- **Established by:** `7fa3095`
- **Breaks if:** "Simplifying" to just the phase-machine pair, which looks like the direct answer.
- **Confidence:** high

### Confirm/Undo gates mirror the game's OnClick guards with NO visibility term
- **Where:** `CardsGameApi.CanConfirm`, `CardsGameApi.CanUndo`
- **Rule:** Mirror `ReadyButton.OnClick` (`ButtonComponent.enabled` + no warning mask + `interactable`) and `UndoButton.OnClick` (`interactable`) exactly. Never test `canvasGroup.alpha > 0`.
- **Why:** VR hides the 2D UI stack, so an alpha gate is permanently false and CONFIRM/UNDO never reacted at all. A missing `warningMask` is treated as no mask.
- **Established by:** `32d3151`, `e30e35d` fix(cards): treat missing warningMask as no-mask in CanConfirm
- **Breaks if:** Adding a visibility term "so we don't press an invisible button" — the game's own `OnClick` does not check it either.
- **Confidence:** high

### The mod CONFIRM hides only when the native is docked AND rendering — currently INERT
- **Where:** `PlayTray.TickStatus` (`ContinueDocked && ContinueVisible`); note the deliberate asymmetry with `_undo`, which gates on `UndoDocked` alone
- **CORRECTED (phase 4):** the branch **can never be taken today**. `WorldUI/Surfaces/TrayControlDockSurface.cs` hardcodes `ContinueDocked => false`, `ContinueVisible => false`, `UndoDocked => false`, `ShortRestDocked => false` and sets `_controls = Array.Empty<DockedControl>()` — nothing docks any native widget any more, which is also *why* `CardsGameApi`'s three `*Widget()` accessors had no callers (removed as Tier-0 dead code by Batch D of this refactor). The reasoning below is still the record of why the twin gating exists and must be restored intact if docking is ever re-enabled; the code is inert. The constants live in `WorldUI`, so this is not a Cards-only matter — see `REVIEW-Cards.md` §3.5.
- **Rule:** Both terms are required for Continue.
- **Why:** The native `ReadyButton` stays docked and interactable while the game drives its `CanvasGroup` alpha to ~0. Gating on `ContinueDocked` alone hid the mod Confirm too, leaving nothing visible yet still pressable through the docked host's raycaster.
- **Established by:** `d56e4c8` (item 7)
- **Breaks if:** Dropping the second term as redundant, or "fixing the asymmetry" with Undo.
- **Confidence:** high

### Solo-host confirm rescue re-runs the game's own idempotent check
- **Where:** `CardsGameApi` solo-host branch → `InitiativeTrack.CheckRoundAbilityCardsOrLongRestSelected()`, scoped to online + host + exactly one player + card selection + non-interactable toggle
- **Rule:** Call the game's own re-enable, never fake state; a strict no-op with ≥ 2 players.
- **Why:** Hosting multiplayer solo, the game deactivates the single-player `ReadyButton` and forces the MP `UIReadyToggle` uninteractable, and only re-enables it when another player connects or on a fresh card (de)select. A solo host who finished selecting before hosting triggers neither, so both commit affordances stay dead and the round cannot advance.
- **Established by:** `7d0a589` fix(cards): surface confirm for a solo online host in card selection (#5b)
- **Breaks if:** Widening the scope, or replacing the call with a direct `SetInteractable(true)` — the game's own call sets it *iff* every owned actor has a valid selection.
- **Confidence:** high

### Long-rest turn pump: never edge-detected, re-resolved inside the queued action
- **Where:** `CardsGameApi.LongRestTurnHand` / `TryAdvanceLongRestTurn`, `CardsDriver.PumpLongRestTurn` (`_longRestPumpNextTry`, `_longRestPumpQueued`)
- **Rule:** State is re-evaluated **every tick** (only the game calls are throttled), at most one advance is in flight, the hand is re-resolved *inside* the queued lambda, and the pump stands down on `Mode == LoseCard` or under a blocking modal.
- **Why:** On the long-rester's turn the vanilla game parks itself on two hidden 2D widgets (a `LongRestConfirmationButton` toggle and an inactive "PERFORM LONG REST" ReadyButton with the LoseCard-opening action queued on it). Both live in the suppressed 2D stack, so in VR the turn dead-ended and the burn step never activated. Edge detection fails because the arming transitions can arrive in any order; the lambda runs a frame later, behind pending card selects, so every gate must be re-checked against fresh state.
- **Established by:** `963bb96` fix(cards): long-rest turn auto-advance via game's own flow + docked-card grab apron
- **Breaks if:** Converting to an edge-triggered one-shot, or capturing `hand` from the outer scope.
- **Confidence:** high

### Non-current-player select is refused during the action phase
- **Where:** `Cards` prefix on `InitiativeTrackPlayerAvatar.OnClick` (human-click seam only)
- **Rule:** Clicking another of your own characters' avatars during `ActionSelection` plays the game's own invalid-click SFX and skips the select. The mod's *programmatic* selects (`CardsGameApi.SelectActor` → `InitiativeTrack.Select`) bypass it.
- **Why:** Re-pointing the selected actor re-docked that actor's cards, whose halves the game refuses (owner ≠ `CurrentActor`) — deadlocking the action board. Bypassing it for the programmatic path is required so the take-damage attacked-actor selection still works.
- **Established by:** `04ce1dd` fix(cards): reject non-current player select during action phase (deadlock guard)
- **Breaks if:** Moving the guard into `SelectActor` "so it covers all paths".
- **Confidence:** high

### The active-cards change signature folds in the round number
- **Where:** `CardsDriver.ActiveSignature`
- **Rule:** Hash the widget ids **and** `CardsGameApi.RoundNumber()`.
- **Why:** Bonus half-activity can shift at a round boundary without the card set changing, so an id-only signature leaves the highlighted halves stale.
- **Established by:** `3d63abb` feat(cards): ACTIVE CARDS display area (feature 6)
- **Breaks if:** Hashing only the ids — the obvious "set changed?" test.
- **Confidence:** high

### A hidden active column clears its list, it does not just hide
- **Where:** `CardsDriver.UpdateActive` (`_active.SetCards(_activeBuffer)` on the empty path)
- **Rule:** Zone membership must stay accurate even while invisible.
- **Why:** `_active.Contains(card)` is a zone predicate in the `Rebuild` park sweep and in `IsReadOnlyViewerCard`; a stale list keeps cards out of the park sweep forever and routes released cards back to a hidden column.
- **Established by:** `c3a3ddd`
- **Breaks if:** Just calling `SetVisible(false)` and returning.
- **Confidence:** high

---

## 11. Modal gating and input blocking

### Card input blocks on `BlockingWindowModalActive`, not `WindowModalActive`
- **Where:** `CardsDriver.TickInteractionsAndStatus` (`_modalInputBlocked`), `WorldUI.ModalFallback.BlockingWindowModalActive`
- **Rule:** The reachable pause/ESC/Options/Multiplayer/Compendium family imposes **zero** restrictions on cards.
- **Why:** `WindowModalActive` means "any floated window", which was wrong twice over: an open ESC menu froze all card input, and a **closed** menu whose sticky float had not been released yet still counted as open — "I closed the menu but cards stayed dead".
- **Established by:** `a840692` (the original block), corrected by `646fc5f` and `eef8eb0` fix: cards never blocked by the pause/options menu family
- **Breaks if:** Reverting to the broader predicate "to be safe".
- **Confidence:** high

### Un-blocking marks dirty; held cards are exempt
- **Where:** `CardsDriver.BlockCardInteractions` (skips `card.IsHeld`), release edge sets `_dirty = true`
- **Rule:** The block is destructive (it stomps per-card `Grabbable`/`PokeSelectEnabled`), so the next `Rebuild` must restore them; a card already held when the menu opens stays held.
- **Why:** Without the `_dirty`, cards stay permanently inert after the menu closes. Blanket-disabling a held card drops/orphans it mid-grab.
- **Established by:** `a840692`
- **Breaks if:** Dropping either.
- **Confidence:** high

### Cards refuse grab STARTS in `ModalUI`, but the Grab interactor stays enabled
- **Where:** `VRCard.CanGrab` (`CurrentMode != VRMode.ModalUI`), mode policy in `CardsDriver.OnEnable`
- **Rule:** The interactor stays live so the tray handle keeps working; per-object gating decides who may take the grip. A card already held when the dialog opens stays held.
- **Why:** The `ModalUI` interactor matrix had no Grab at all, so the tray grab handle was dead while a dialog floated. Removing Grab wholesale also force-dropped held cards on interactor teardown.
- **Established by:** `101afb6` feat(events): Grab interactor active in ModalUI; cards refuse modal grabs
- **Breaks if:** Going back to a blanket interactor removal, or extending the refusal to grab *continuation*.
- **Confidence:** high

### `BoardTargeting` grants Grab via the mode machine's extension API
- **Where:** `CardsDriver.OnEnable` (grants Grab to both hands in `BoardTargeting`)
- **Rule:** Granted through the documented extension API, never a patch on the frozen mode class.
- **Why:** `WaitingForPlayerWaypointSelection` → `VRMode.BoardTargeting` carried no Grab, so every grab primitive died with it during move-destination selection — "bars vibrate/flicker but won't grab, laser grab dead" (eleven "LASER-CARRY armed" lines with no engage).
- **Established by:** `bce9a63` (bug A)
- **Breaks if:** Hardcoding the policy in the mode machine.
- **Confidence:** high

### Poke-select is never armed on hand cards
- **Where:** `CardsDriver.Rebuild` zone loop (`card.PokeSelectEnabled = false;` unconditionally, re-asserted every rebuild)
- **Rule:** The **only** commit path is a deliberate slot drop.
- **Why:** The pick modes used to arm `pokeSelect = true`, so a fingertip within 8 mm fired `OnCardPoked` → `TryCommitPick` → `SelectCard` with no board placement. Touching a card burned it.
- **Established by:** `098ca86` (item 10, marked SERIOUS)
- **Breaks if:** Removing the "dead" assignment, or re-arming poke-select for pick modes as a convenience.
- **Confidence:** high

### `PokeSelectEnabled` registers/unregisters the pokeable
- **Where:** `VRCard.PokeSelectEnabled` setter
- **Rule:** The card registers with `VRInteractables` only while enabled.
- **Why:** The `PokeInteractor` targets the single nearest pokeable, and a permanently registered card collider would swallow the half-selection zone pokes.
- **Established by:** `8061069` feat(cards): palm fan, play tray with initiative slots, rest tokens, half selection
- **Breaks if:** Registering once at build and gating in the callback.
- **Confidence:** high

### `HelpBox` is a passive window, not a modal fallback
- **Where:** `WorldUI.ModalFallback.FallbackIds` (HelpBox removed)
- **Rule:** The game's passive bottom hint strip must never assert `ModalUI`.
- **Why:** The game shows a `HelpBox` right after a successful hero placement; with it in the fallback list, the mode machine held `ModalUI` from that moment to shutdown. In `ModalUI` the palm-gate interactor is off — the fan could **never** open — and the board pick is inactive. The whole "dead fan" test round was this.
- **Established by:** `1898aca` fix(worldui): passive HelpBox hint strip no longer asserts ModalUI — frees the card fan
- **Breaks if:** Adding new window ids to the fallback list without checking whether the game itself keeps playing with them visible.
- **Confidence:** high

### The fan-state diagnostic line is load-bearing
- **Where:** `CardsDriver.LogFanState` (change-deduped, Info level, at the end of the interaction tail)
- **Rule:** It must stay at Info and must remain reachable on every path (hence no early returns in `TickInteractionsAndStatus`).
- **Why:** The `#16` log only proved the rebuild side at Debug level, which the `LogOutput` capture drops. This one line — mode, widgets, fanBuffer, gateEnabled, revealed, open, boundHand, vrMode — makes any future fan disagreement attributable from `LogOutput.log` alone. It is also what proved `CardsDriver.Update` completes every frame during the NRE flood.
- **Established by:** `1898aca`, relied on by `fd82d20`
- **Breaks if:** Demoting it to Debug, or moving it above a `return`.
- **Confidence:** high

---

## 12. Item cards

### The consumed-item fog: three writes, all required
- **Where:** `ItemsPile.ItemChip.ClampCardEffectSmoke`, `ItemsPile.ItemChip.SmokeCardSpan` (0.9), `ItemsPile.ItemChip.SmokeClamp` / `ScaleCurve` / `UpperBound`
- **Rule:** Every `ParticleSystem` on the hosted `ItemCardUI` gets `simulationSpace = Local` **and** `scalingMode = Hierarchy` **and** a computed geometric shrink so the plume's world size *and* drift fit inside 0.9 × the card's rendered width. Start size/speed are recorded and rewritten as whole `MinMaxCurve`s, scaled in every curve mode.
- **Why:** Unity's default `Local`/`Shape` scaling makes the emitter **ignore the parent scale chain**, so under the FaceCanvas — downscaled by card-metres ÷ ~300 canvas px, three orders of magnitude — `fx_Smoke` emits at its authored **canvas-pixel size in metres**. That is the green fog: puffs tens of metres across, following the card. `Hierarchy` restores the *authored* look, which a stray authored value can still blow past a card width, hence the geometric clamp on top. And in `TwoConstants`/`TwoCurves` mode a multiplier writes only the MAX side, so a naive `startSizeMultiplier *= f` leaves the MIN side at fog scale and can invert the range.
- **Established by:** `b9e48b5` fix(items): restore the game's "verbraucht" FX on the card, bound only the smoke; `347e343` (item 10)
- **Breaks if:** Dropping the geometric shrink as "redundant once Hierarchy is set", setting `simulationSpace` without `scalingMode`, or reducing the curve handling to a multiplier.
- **Confidence:** high

### The emitter scan must include inactive children
- **Where:** `ItemsPile.ItemChip.ClampCardEffectSmoke` (`GetComponentsInChildren<ParticleSystem>(includeInactive: true)`)
- **Rule:** Plural, and inactive-inclusive.
- **Why:** The timeline only `SetActive(true)`s `fx_Smoke` once the burn starts, and `RestoreCard` switches it back off — so at host time it is normally **inactive**. A default scan finds zero emitters and clamps nothing. The singular `GetComponentInChildren` variant is exactly the earlier bug: the prefab's *other* emitters stayed at World simulation and authored screen scale and sprayed across the whole diorama.
- **Established by:** `b9e48b5`, `347e343` (item 10)
- **Breaks if:** Simplifying to `GetComponentsInChildren<ParticleSystem>()` or the singular form.
- **Confidence:** high

### The clamp runs AFTER the canvas fit, in the same frame
- **Where:** `ItemsPile.ItemChip.TryHostRealCard` (ordering)
- **Rule:** After `canvasRect.localScale` is fitted and `_faceWidth`/`_faceHeight` are set, still inside the host frame.
- **Why:** The clamp measures the card's final world scale; hoisting it next to `SpawnCard` makes `lossyScale` pre-fit and `span` use the wrong `_faceWidth`, so the shrink is wrong in both directions. It is still the same frame the effect coroutine started in, and particles do not simulate until after this `Update`, so nothing oversized is ever emitted.
- **Established by:** `b9e48b5`
- **Breaks if:** Hoisting it for readability.
- **Confidence:** high

### `cardEffects` is NOT nulled before `Show`
- **Where:** `ItemsPile.ItemChip.TryHostRealCard`
- **Rule:** The hosted card keeps its `cardEffects`; only `fx_Smoke` is clamped.
- **Why:** An earlier round nulled `cardEffects` wholesale to kill the fog and threw the on-card FX away with it — the whole "verbraucht" look (burn tint, dissolve, grey-out sweep, `fgFx` overlay) is uGUI on the card and must stay live. The user asked for the burn look **back**.
- **Established by:** `aa63e87` fix(cards): kill the screen-filling green fog around consumed items (#1) → corrected by `b9e48b5`
- **Breaks if:** Re-nulling `cardEffects` as a cheaper fog fix. Note this is a *reversal* of `aa63e87`: the commit that established the null is not the current law.
- **Confidence:** high

### Item chips do not spawn the separate consumed plume
- **Where:** `ItemsPile.ItemChip.Create` (comment block after the backing build), `BurnCardFx.SpawnConsumedPlume`
- **Rule:** The consumed look comes only from the hosted card's own clamped `ItemCardEffects`.
- **Why:** That separate, screen-card-authored plume was the second fog source, and nothing bounded it either.
- **Established by:** `aa63e87`
- **Breaks if:** Re-adding it for a "stronger" burn. (See also "Suspected vestigial".)
- **Confidence:** high

### `BurnCardFx.Bind` reparents; `SpawnConsumedPlume` force-scales — deliberately different
- **Where:** `BurnCardFx.Bind` (reparent onto the world card **plus** the module writes) vs `BurnCardFx.SpawnConsumedPlume` (forced world scale + `ItemPlumeCardSpan` + `ItemPlumeMaxLifetime` cap)
- **Rule:** They must not be unified.
- **Why:** `Bind` needs the reparent because `scalingMode = Hierarchy` alone multiplies the plume by the huge **screen-space** card's scale — still diorama-covering — and because the reparent also anchors the plume at the burning card's world position. The spawn path has no such parent to inherit from, so it substitutes an explicit world scale. The lifetime cap stops a fast particle drifting across the map.
- **Established by:** `a04945d` fix(worldui): board-anchor enemy reveal height; reparent burn smoke to world card; `b7fe205`; `347e343`
- **Breaks if:** "Unifying the two smoke clamps".
- **Confidence:** high

### `RestoreBound` is unconditional and instance-keyed
- **Where:** `BurnCardFx.RestoreBound`, `BurnCardFx.Tick` (`ReferenceEquals(smoke, _bound)` early-out), `BurnCardFx.Detach` (calls `EndBurn` when `_effectActive`)
- **Rule:** Restore the original parent and all four module values on every instance change, with **no** `isActive` guard; rebinding is keyed on instance identity; `Detach` balances the burn ref-count.
- **Why:** The smoke instance is pool-owned and the pool swaps instances underneath us. Writing module values on an inactive system is harmless and leaves the pooled prefab clean; guarding on `activeInHierarchy` recycles a permanently clamped, permanently reparented smoke into the flat game. `BeginBurn`/`EndBurn` are a balanced pair — a widget disable mid-burn otherwise leaves the flat screen-space hand suppressed forever.
- **Established by:** `b7fe205` fix(cards): bound the burn CardSmoke plume to the card (test #22)
- **Breaks if:** Adding an active guard, comparing by null-ness, or reducing `Detach` to `RestoreBound()`.
- **Confidence:** high

### Collider BEFORE component, then resize
- **Where:** `ItemsPile.ItemChip.Create` (placeholder `BoxCollider` → `AddComponent<ItemChip>` → resize the collider)
- **Rule:** That exact order.
- **Why:** `GrabbableBehaviour.OnEnable` caches `GetComponent<Collider>()`; finding none it warns and **never arms grab**, so the laser/trigger pluck was dead and only the collider-free finger sweep worked.
- **Established by:** `9d03c5c` fix(cards): item cards grabbable (collider before Grabbable) + item-aspect body (no black bars)
- **Breaks if:** Reordering to "build the object fully, then add components".
- **Confidence:** high

### Item geometry uses the MEASURED card size
- **Where:** `ItemsPile.ItemChip._faceWidth`/`_faceHeight` (set in `TryHostRealCard`), consumed by `Create`, `FaceWidth`/`FaceHeight`, `GetHeldPose`, `ClampCardEffectSmoke`
- **Rule:** Never `CardsConfig.CardWidth`/`CardHeight`.
- **Why:** Item cards are **near-square**, not the tall ability rect: fitting them into the w×h ability box left black backing bars top and bottom, and using the tall `CardHeight` in `GetHeldPose` lifts the card the wrong amount out of the pinch.
- **Established by:** `9d03c5c`
- **Breaks if:** Reunifying with the ability-card constants "for consistency".
- **Confidence:** high

### `ClipIntoSlot` re-parents; it never chases
- **Where:** `ItemsPile.ItemChip.ClipIntoSlot` (child of the slot at exact zero local pose, world size preserved by dividing the fan's lossy scale out), `ItemsPile.ItemChip.Update` (`if (PendingUse) return;`), `ItemsPile.TickPendingUse` (re-asserts the **parent** only)
- **Rule:** A clipped chip does **zero** per-frame pose work.
- **Why:** "Wenn die Item-Karte auf dem Overlay fixiert ist und man mit dem Kopf wackelt, wackelt die Karte auch etwas und zieht nach." The chip stayed parented to the items-fan root, which **billboards to the head every frame**, while the slot is bolted to the board — so the owner re-derived the slot pose in fan-root local space each tick and the chip chased it with an exponential lerp: a target that jumps with every head movement, followed by something that only converges asymptotically. The card could not help but swim. Re-parenting removes the chase instead of tuning it. The scale division is required because the two parents sit at different points in the board's scale chain.
- **Established by:** `fc34ae9` fix(items): use-button size, USE caption depth-fight, and the swimming clipped card
- **Breaks if:** Reintroducing any per-frame lerp/settle/pop on a clipped card, or dropping the scale-ratio division as over-complication.
- **Confidence:** high

### Every exit from PendingUse routes through `UnclipChip` — including the grab
- **Where:** `ItemsPile.UnclipChip` / `ItemsPile.ItemChip.UnclipFromSlot`; `ItemsPile.ItemChip.OnGrab` calls it **before** `base.OnGrab`
- **Rule:** Confirm, cancel, invalidation **and** grab all unclip, preserving world pose; the grab's unclip is the first statement.
- **Why:** `_homePos` is fan-root local, so a chip still under the slot glides toward a meaningless point. And `base.OnRelease` restores exactly the parent it saw at grab time — so a clipped card grabbed and dropped elsewhere would have been put back **under the slot** while its glide-home target is fan-root local.
- **Established by:** `fc34ae9`
- **Breaks if:** Removing the `UnclipChip` in `CancelPendingUse` as "the grab path already did it" (the invalidation path did not), or moving the grab's unclip after `base.OnGrab`.
- **Confidence:** high

### Release captures the drop point before the base re-parent
- **Where:** `ItemsPile.ItemChip.OnRelease` (drop world pos read **before** `base.OnRelease`, world pose re-applied after, then `_releaseGlide` armed); `ItemsPile.OnChipReleased` (`chip.CancelReleaseGlide()` before `ClipIntoSlot`)
- **Rule:** That ordering, and the glide is cancelled before a clip.
- **Why:** The base restore is an instant teleport to the fan slot, so a slot-proximity test after it measures the fan home rather than where the player let go, and the card snaps instead of gliding. A live glide fights the new parent — `Update` re-runs it next frame.
- **Established by:** `33cd259` feat(items): make held/released/back-face of item chips match ability cards; `554f947`
- **Breaks if:** Moving the proximity test after the base call, or dropping the glide cancel because `ClipIntoSlot` "sets the local pose anyway".
- **Confidence:** high

### Item chips make exactly the ability cards' sounds — grab and place, nothing else
- **Where:** `ItemsPile.ItemChip.OnRelease` (deliberately silent), `ItemsPile.OnChipReleased` (`CardPlaceSound` on clip-in)
- **Rule:** A release that merely glides back to the fan plays **no** mod sound.
- **Why:** "Die Item-Karten sollten die selben und nicht mehr Geräusche machen als die anderen Karten auch." The chip played the take-back click on every non-clip-in release on the claim that this matched the ability cards' SFX set. It did not: `CardsDriver` plays that click in exactly one place — a card pulled back off the **field** during a pick reopen, and only there because the game itself is silent in that one case. An ability card released back into the fan makes no mod sound at all.
- **Established by:** `fe23a94` fix(items): drop the extra take-back click when an item card glides back to the fan
- **Breaks if:** "Restoring symmetry" by adding a release click.
- **Confidence:** high

### `IsActivatable` and `CanUseNow` are LIVE, never cached
- **Where:** `ItemsPile.ItemChip.IsActivatable` → `ItemsPile.IsItemActivatable`; `ItemsPile.CanUseNow`; `ItemsPile.Tick` (`HeldActivatableChip()` → `SetItemUseSlotVisible`); `ItemsPile.ItemChip.TickFaceMaintenance` (the `want != _usabilityShown` write gate)
- **Rule:** Computed on every read from `Item`'s live `SlotState` and the turn state; only the `SetActive` **write** is change-gated. The predicate is byte-for-byte the gate `UseItemService.UseItem` enforces.
- **Why:** State changes with every use and every turn, so a value captured at chip-create time is wrong within one action; and a highlight or a use slot that promises a use the service would reject is worse than none. The cheap part is the compare, not the answer.
- **Established by:** `9c367b1` feat(items): real item-card faces, normal-card interaction, clip-in USE slot; `554f947`; `2ec7d43` feat(items): highlight the items you CAN play instead of dimming the ones you can't
- **Breaks if:** Caching into a `bool _activatable`, throttling the whole poll rather than the write, or re-implementing the predicate separately for the highlight and the slot gate (they would drift).
- **Confidence:** high

### The closed-stack cue counts inventory, not chips
- **Where:** `ItemsPile.UsableCount` (reads `ItemsOf(hand)`), `PileViewer.PileStack.SetUsableHighlight`
- **Rule:** The ember cue on the **closed** items stack must be derived from the live inventory.
- **Why:** While the fan is closed there **are** no chips — they are built on open and destroyed on close. A `_chips.Count(...)` rewrite kills the cue exactly when it matters, and lets the stack cue and the per-card frames disagree.
- **Established by:** `4569ba9` feat(items): soft rounded frame on usable item cards, drifting embers on the deck
- **Breaks if:** Rewriting it in terms of `_chips` for DRY-ness.
- **Confidence:** high

### The use flourish reads `YMLData.Usage`, not the post-use `SlotState`
- **Where:** `ItemsPile.ConfirmPendingUse`
- **Rule:** The Spent/Consumed intent is read from the **static** usage type *before* `UseItemService.UseItem`, with `SlotState` only as an additional OR.
- **Why:** Online, `UseItem` sends the state change as a `GameAction` that resolves a frame or more later, so `SlotState` is still Useable/Selected the instant we return — the old "spent = `SlotState == Spent`" read was false and **no tap animation ever played in multiplayer**. It worked in singleplayer, which is why it survived.
- **Established by:** `347e343` (item 9b)
- **Breaks if:** Reading `SlotState` after the call.
- **Confidence:** high

### The confirmed chip is detached from the fan before the flourish
- **Where:** `ItemsPile.ConfirmPendingUse` (reparent to `keep`, `_chips.Remove(chip)`, `_handWinner` cleared, then `PlayUseThenCollapse`)
- **Rule:** Out of the live list and out of the fan root before the animation starts.
- **Why:** The fan live-rebuilds to show the item's new Spent/Consumed state, and `Populate` → `ClearChips` → `DestroyImmediate` would kill the chip mid-animation.
- **Established by:** `554f947` (item 6)
- **Breaks if:** Leaving it in `_chips` "so layout stays consistent".
- **Confidence:** high

### The fan never live-rebuilds while a chip is held or pending
- **Where:** `ItemsPile.Tick` (gate `!AnyHeld() && _pendingUseChip == null`), `ItemsPile.Signature` (includes each item's `SlotState`)
- **Rule:** Signature-driven rebuilds are suppressed while the player holds a chip or a decision is open; the signature keys on state, not count.
- **Why:** A rebuild is `ClearChips` → `DestroyImmediate`, i.e. yanking the card out of the player's hand or out of the use slot. And using an item never changes the item **count** — only its state — so a count-only key never refreshes the fan after a use.
- **Established by:** `554f947`, `fc34ae9`
- **Breaks if:** Reducing the gate to a plain signature compare, or the signature to `items.Count`.
- **Confidence:** high

### The item fan is board-anchored in position but head-billboarded in rotation
- **Where:** `ItemsPile.Tick` (billboard branch), `ItemsPile.FaceHead`, `ItemsPile.PlaceAboveBoard` (leaves `localRotation` identity), `ItemsPile.ItemFanOffset` (live config read each tick)
- **Rule:** Local *position* from the board-local anchor (so it rides the board's pose and scale); *world* rotation re-billboarded every frame; the fan offset re-read live and kept separate from `CardsConfig.BrowseFanOffset`.
- **Why:** Opened piles must face the player before pickup. Writing the billboard as a `localRotation` compounds the board's own rotation. The offset is separate because item cards are a different, near-square shape, and live because the debug-menu steppers must move an **open** fan.
- **Established by:** `192b928` fix(cards): discard/burnt pile browse faces the player + closes on click-away (issue #7); `554f947` (item 2)
- **Breaks if:** Baking the facing at open time, or merging the two offsets.
- **Confidence:** high

### Item fan emerge/collapse: root-local seed, world-space glide, re-parent before deactivate
- **Where:** `ItemsPile.Open` (call order: parent → `SetActive` → `Populate` → place → `EmergeAll`), `ItemsPile.EmergeAll` (root-**local**), `ItemsPile.ItemChip.BeginCollapse` (**world**), `ItemsPile.CollapseChips` (re-parent to `keep` first; `_box.enabled = false`)
- **Rule:** `EmergeAll` runs last, after the root is placed; collapsing chips are re-parented out of the fan root before it deactivates, with their colliders off.
- **Why:** `EmergeAll` computes `_root.InverseTransformPoint(PileConvergeWorld())`, which is garbage before placement. `Update` does not run under an inactive root, so a collapse that stays parented freezes mid-air and never self-destroys. The collider disable stops a grab on a self-destroying chip.
- **Established by:** `554f947` (item 5), `347e343` (items 2/3)
- **Breaks if:** Folding `EmergeAll` into `Populate`, skipping the re-parent because `Close` "deactivates afterwards anyway", or unifying the two coordinate spaces "for symmetry".
- **Confidence:** high

### The item fan converges on the ITEMS stack
- **Where:** `PileViewer.EnsureBuilt` (`_itemsBrowse.SetAnchor(_items.transform)`)
- **Rule:** Pass the items stack transform, not the shared `PileMount`.
- **Why:** `PileMount`'s origin sits by the **discard** stack, so the item fan emerged from and collapsed into the wrong pile.
- **Established by:** `347e343` (item 3)
- **Breaks if:** "Simplifying" to `mount`, which is only the null fallback.
- **Confidence:** high

### `ItemsPile.Current` / `PileBrowser.Current` are published for the mirror and the wire
- **Where:** `ItemsPile.Current`, `PileBrowser.Current` (published in `Open`, cleared in `Close`/`Destroy` **only** via a `ReferenceEquals` guard); `ItemsPile.IsHeldByLeftHand` / `PileBrowser.IsHeldByLeftHand`
- **Rule:** Publish on open, unpublish only if the static still points at *this* instance; send the holding side explicitly.
- **Why:** The desktop mirror and the net extras sender could only find the ability fan, so item cards were invisible in the mirror **and** on every peer. Guessing "the dominant hand" put the peer's fan on the wrong arm whenever the user held it in the other one.
- **Established by:** `554f947` (item 3), `da2ee2a` feat(net): item cards in the mirror…, `c7bff30` fix(mirror,net): the hand fan is back in the mirror…
- **Breaks if:** Clearing the static unconditionally in `Close`, or deriving the side from the dominant-hand setting on the receiver.
- **Confidence:** high

### The use slot and the drop ghost are torn down on every fan exit
- **Where:** `ItemsPile.Close` / `ItemsPile.Destroy` (`_pendingUseChip` cleared, confirm button hidden, `SetItemUseSlotVisible(false)`, `_useGhost` hidden/`DestroyImmediate`d)
- **Rule:** `Destroy` must destroy the ghost **explicitly**.
- **Why:** The ghost is parented under the use slot — board furniture the fan does not own — so root destruction does not take it with it, and it would float there forever.
- **Established by:** `347e343` (item 8)
- **Breaks if:** Relying on the root teardown, or only deactivating in `Close`.
- **Confidence:** high

### The USE caption is parked outside the card footprint
- **Where:** `PlayTray.BuildItemUseSlot`, `PlayTray.ItemUseLabelDrop` (0.026), `PlayTray.ItemUseLabelHeight` (0.030)
- **Rule:** The caption sits below the recess, clear of the glow rim, fitted into a 30 mm strip — **not** into the card's height.
- **Why:** "Der 'Use'-Text glitcht immer mal wieder vor die Karte und darunter." The caption used to sit at the slot's centre, two millimetres proud of exactly where the clipped card lands. Two co-planar surfaces 2 mm apart — one of them a card whose face art and backing are pushed into a high render queue — resolve their order **per frame and per view angle**. Nudging the z would only move the flicker; the fix has to be geometric. A card-tall fit box would let TMP grow the glyphs back across the recess and undo the separation.
- **Established by:** `fc34ae9`
- **Breaks if:** Re-centring with a z tweak, or setting the fit box to the card height "so the text isn't tiny".
- **Confidence:** high

### The usable-item frame is a hollow, constant-thickness sprite outline
- **Where:** `ItemsPile.ItemChip.BuildUsableFrame`, `FrameReferencePixels` (300), `FrameOutsetPixels` (8), `FrameCornerRadiusPx` (14), `FrameZ` (−0.0022), `sortingOrder = 1`, `fillCenter = false`, `raycastTarget = false`; art from `WorldUI.SoftCueArt.FrameSprite` + `SoftFramePulse`
- **Rule:** A pixel-referenced 9-slice outline on its own world-space canvas, hollow, non-raycasting, with an explicit sorting order and a rounded corner radius.
- **Why:** Border thickness must be **constant in metres**: a stretched textured quad scales its border with the card and turns a hairline into a slab on a wide card. Hollow because a wash over the art was rejected. Non-raycasting because the chip is grabbed through its collider and nothing may raycast this. The corner rounding is the user's one hard requirement ("nicht viereckig") — the flat rectangular quad predecessor was rejected outright. `FrameZ` is close enough that it never parallax-separates from the card at an angle.
- **Established by:** `2ec7d43`, `4569ba9`
- **Breaks if:** Replacing it with a scaled mesh/quad, or leaving the sorting order to depth luck.
- **Confidence:** high

### Layers are applied per mod-owned child, never to the chip root
- **Where:** `ItemsPile.ItemChip.Create` (the explicit `// do NOT VRLayers.Apply(go)` note), `ItemsPile.ItemChip.TryHostRealCard` (`VRLayers.Apply(canvasGo)` **before** `ObjectPool.SpawnCard`)
- **Rule:** Apply to the backing, fallback face, frame and ghost individually; layer the FaceCanvas while it is still **empty**.
- **Why:** `VRLayers.Apply` recurses, and the hosted `ItemCardUI` is a **game-owned** canvas that must keep its authored UI layer (the reversibility rule) — it renders via the VR camera's UI-layer bit owned by `CanvasConversion`. Re-layering it either makes the card vanish or leaks the mod layer back into the pool.
- **Established by:** `9c367b1`, `554f947`
- **Breaks if:** "Simplifying" to one `VRLayers.Apply(go)` at the end of `Create`, or moving the canvas apply down next to the other setup lines.
- **Confidence:** high

### Every `GraphicRaycaster` on the hosted card is disabled
- **Where:** `ItemsPile.ItemChip.TryHostRealCard`
- **Rule:** Disable them all.
- **Why:** Interaction is driven through the mod's collider (grab/laser), never the game's uGUI input module — which would raycast this world canvas at the **parked mouse pixel**. Same class of bug as `CardFaceRaycaster` solves for ability cards.
- **Established by:** `9c367b1`
- **Breaks if:** Dropping it because "the card isn't clickable anyway".
- **Confidence:** high

### Restore order before the pool recycle
- **Where:** `ItemsPile.ItemChip.OnDisable`: `RestoreCardEffectSmoke` → `CardFaceMipBake.RestoreSprites` → `ObjectPool.RecycleCard`, with both restores **outside** the try/catch that wraps the recycle
- **Rule:** Both restores run first, unconditionally.
- **Why:** The card widget is game-owned and pooled, so the flat UI would inherit our clamp (and our baked textures) on the next spawn. Keeping them outside the try means the widget the pool gets back is byte-for-byte the one it handed out even if the recycle itself fails.
- **Established by:** `554f947` (item 1), `b9e48b5`
- **Breaks if:** Folding them into the `try`, moving them after the recycle, or skipping them when `_cardGo == null`.
- **Confidence:** high

### De-shimmer: tight art poll for 2 s, then a 1 s cadence forever
- **Where:** `ItemsPile.ItemChip.TightArtPollSeconds` (2), `_tightArtPollUntil`, `_artBaked`, `ItemsPile.ItemChip.MipRescanInterval` (1)
- **Rule:** Poll `cardBackground.sprite` every frame for ~2 s after host and bake the instant the async art appears; then rescan on a 1 s cadence **indefinitely**.
- **Why:** The 1 s cadence alone landed the first successful bake up to ~1.5 s after the fan opens, so the card visibly shimmered until then. And async arrivals and state changes keep putting the mipless originals back, so the cadence must never stop.
- **Established by:** `554f947` (item 1), `347e343` (item 1)
- **Breaks if:** Deleting the tight poll as duplicated work, or guarding the cadence with `if (!_artBaked)`.
- **Confidence:** high

### The item FaceCanvas re-binds its `worldCamera` every frame
- **Where:** `ItemsPile.ItemChip.TickFaceMaintenance`
- **Rule:** Per frame, mirroring `VRCard.UpdateCanvasCamera`.
- **Why:** A WorldSpace canvas with a null `worldCamera` culls and sorts differently per camera, so the item face was **absent from the desktop mirror**. The head camera is recreated on rig rebuilds, so a one-shot at host is not enough.
- **Established by:** `554f947` (item 3)
- **Breaks if:** Moving it into `TryHostRealCard`.
- **Confidence:** high

### Chip motion uses clamped unscaled time; the held pose uses scaled time
- **Where:** `ItemsPile.ItemChip.Update` (`Mathf.Min(Time.unscaledDeltaTime, 0.05f)` for pop/glide) vs `ItemsPile.ItemChip.TickHeldPose`
- **Rule:** The asymmetry is intentional.
- **Why:** Pop and glide must play while the game pauses for a dialog; the held pose tracks the wrist and belongs on the same clock as the hand.
- **Established by:** `33cd259`, `554f947`
- **Breaks if:** Unifying them "for consistency".
- **Confidence:** medium

---

## 13. Piles and the browse fan

### Pile poke: entry-only toggle + exit re-arm + cooldown; the laser bypasses the re-arm
- **Where:** `PileViewer.PileStack.OnPoke` (`_pokeArmed`), `PileViewer.PileStack.OnPokeExit`, `PileViewer.PileStack.PokeToggleCooldownSeconds` (0.4), `PileViewer.PileStack.LaserToggle` (cooldown only)
- **Rule:** A finger poke toggles only while armed; the flag is cleared on toggle and re-armed **only** by `OnPokeExit`; a 0.4 s cooldown applies to both paths. The laser path must **not** require the re-arm.
- **Why:** Poking a pile toggled the browse OPEN on entry and immediately AGAIN while retracting: the opening browse fan spawns colliders near the fingertip, so the `PokeInteractor`'s hover flips away and back, re-arming the contact edge while the tip is still inside the stack. The cooldown alone loses slow retracts; the entry gate alone loses the exit+enter flicker inside one poke. The laser is already a clean `TriggerDown` edge and the beam legitimately rests on the stack between two deliberate clicks — requiring a collider exit would silently eat the second click.
- **Established by:** `4a75265` fix(cards): edge-robust pile-stack poke — entry-only toggle + exit re-arm + 0.4s cooldown
- **Breaks if:** Dropping either guard as redundant, or unifying `OnPoke` and `LaserToggle` into one method.
- **Confidence:** high

### `ItemChip.OnPokeExit` and `PileStack.OnPokeExit` have opposite semantics
- **Where:** `ItemsPile.ItemChip.OnPokeExit` (clears the laser pop) vs `PileViewer.PileStack.OnPokeExit` (re-arms the toggle)
- **Rule:** Same interface, deliberately different meaning.
- **Why:** Chips are hover-pop + pluck targets with no toggle state; stacks are toggles.
- **Established by:** `4a75265`, `9c367b1`
- **Breaks if:** Refactoring them into a shared `IPokeable` base "since they look the same".
- **Confidence:** medium

### The browse arc gets the same single-winner sweep as the hand fan
- **Where:** `PileBrowser.UpdateHandSweep` / `ItemsPile.UpdateHandSweep` (`ContactTipReach` 0.035 OR `ContactPalmReach` 0.13 for candidacy, ranked by tip distance alone, `ContactStickyMargin` 0.02 incumbent bonus, all × `dom.WorldScale`); `PileBrowser.ClearHandSweep`; `VRCard.HandArbitrationHand`
- **Rule:** Exactly one card lifts; the suppression set is rebuilt from scratch each tick and cleared on close/destroy; cards that cannot pop (`IsHeld`, `!CanGrab`, `Holder != null`, `PendingUse`) are excluded from **candidacy**, not from the apply step.
- **Why:** Same three findings as the hand fan: multi-lift on a sweep, palm-metric flutter at the midpoint, and a dead card winning and suppressing the real candidate beside it. Stale-flag proof because a card that leaves the arc mid-frame is cleared here or by its own `OnDisable`. `HandArbitrationHand` scopes the suppression so it cannot leak to the other hand.
- **Established by:** `eee8769` feat(cards): physical hand-sweep single-highlight for the pile browse fan
- **Breaks if:** Ranking by `min(tip, palm)`, dropping the `* scale`, maintaining the suppression set incrementally, or moving the "cannot pop" filter to the apply step.
- **Confidence:** high

### Laser and hand never fight — the arbitration is purely spatial
- **Where:** `PileBrowser.Tick` / `PileBrowser.UpdateHandSweep`, `CardsDriver.UpdateBrowseLaser`
- **Rule:** Hand in the arc → a fingertip candidate exists and the hand drives; hand out at pointing distance → no winner, and the browse laser hover is the sole highlight.
- **Why:** Stated in-source as the design: no precedence rule is needed because the two conditions are mutually exclusive by geometry.
- **Established by:** `eee8769`
- **Breaks if:** Adding an explicit precedence rule that can disagree with the geometry.
- **Confidence:** medium

### Browse emerge is a one-shot applied AFTER `SetHome`
- **Where:** `PileBrowser.Relayout` (`_emergePending && !instant`, world position written after `SetHome`, flag cleared unconditionally)
- **Rule:** Only the first non-instant layout after open emerges.
- **Why:** `SetHome` leaves `_instantNext = false`, so the per-card home-lerp then flies the card up into the arc — but the write must come **after** the home is set, or it is immediately overwritten. Without the one-shot, every mid-browse rebuild yanks the cards back onto the stack.
- **Established by:** `347e343` (item 2)
- **Breaks if:** Clearing the flag inside the loop, hoisting the write above `SetHome`, or dropping the `!instant` condition.
- **Confidence:** high

### Browse collapse re-parents out of the browser root first
- **Where:** `CardsDriver.StartBrowseCollapse` (re-parent to `AnchorParent()` with `worldPositionStays: true`), `CardsDriver.CloseBrowser` (collapse launched **before** `_browser.Close()`)
- **Rule:** Cards leave the browser root before it deactivates.
- **Why:** An inactive card's `Update` does not run, so the flight would freeze mid-air and never park.
- **Established by:** `347e343` (item 2)
- **Breaks if:** Calling `Close()` first, or using `worldPositionStays: false`.
- **Confidence:** high

### One funnel closes the browse: `ForeignInteraction`, with two exemptions
- **Where:** `CardsDriver.ForeignInteraction`; `CardsDriver.OnCardGrabbed` exempts `_browser.Contains(card)`; `CardsDriver.UpdateBoardLaser` exempts `_tray.IsItemUseConfirm(button)`
- **Rule:** Every foreign-interaction seam routes here; browse-card grabs and the item-use CONFIRM are exempt; lifecycle closes (hands-down, hand destroyed, mode/dialog change, pile emptied) keep their **own** paths.
- **Why:** Scattering `CloseBrowser` calls through the handlers is how dismissal cases get missed. Grabbing a browse card is *part of* the browse. The item-use CONFIRM is part of the item interaction — a foreign close would drop the pending chip before the confirm callback runs. The lifecycle closes are reversibility guarantees, not user interactions, and must not be conflated with dismissal.
- **Established by:** `4f688c7` feat(cards): dismiss an open pile browse on any foreign interaction (test #22 item 8); `192b928` (click-away)
- **Breaks if:** Re-scattering the calls, or removing either exemption as a special case.
- **Confidence:** high

### Pile-fan mutual exclusion is enforced in BOTH directions
- **Where:** `PileViewer.DispatchPoke` / `DispatchGrabOpen` (`ItemsOpening?.Invoke()` before opening; `_itemsBrowse.Close()` before the ability browse), `PileViewer.SetVisible` (force-closes the item fan on hide)
- **Rule:** Both directions, plus a hide-time force-close.
- **Why:** Only one pile fan may ever be open; and hiding the stacks (piles off, no hand) must never leave an item browse floating.
- **Established by:** `4aebef0` (#6 part 1), `554f947` (item 4)
- **Breaks if:** Keeping one direction "since closing on open is symmetric anyway".
- **Confidence:** high

### The pile stack never leaves the board, and an empty pile is inert
- **Where:** `PileViewer.PileStack.Create` (`snapToHand = false`), `PileViewer.PileStack.CanGrab` / `OnPoke` / `LaserToggle` (`_hasCards` gate)
- **Rule:** The grip holds the browse open; the stack itself stays docked. An empty pile cannot be grabbed or poked.
- **Why:** Stated in-source; a pile you can pull off the board is not a pile, and an empty pile has nothing to browse.
- **Established by:** `1ff1d37` feat(cards): discard/burnt pile stacks on the board + browse fan (test #21)
- **Breaks if:** Enabling `snapToHand` to match the chips, or dropping the `_hasCards` gate.
- **Confidence:** high

### Browse cards are read-only all the way to the release seam
- **Where:** `PileBrowser` (adopted cards are never grabbable-into-a-game-seam nor poke-selectable — `CardsDriver` clears both), `CardsDriver.OnCardReleased` (browse guard intercepts **before** any game commit), `ActivePileViewer` (same contract)
- **Rule:** A plucked browse or active card always returns to its viewer; it never reaches select/slot code.
- **Why:** Discard and burnt cards must be readable but can never be accidentally played or selected.
- **Established by:** `23dc038` feat(cards): grab a pile into a hand-held reading fan (test #22 item 5), `3d63abb`
- **Breaks if:** Letting the release fall through to the shared drop routing "since the slot check will reject it anyway".
- **Confidence:** high

### Browse and active raycasts use the same sticky-hover rule as the fan
- **Where:** `PileBrowser.TryRaycast`, `ActivePileViewer.TryRaycast` (sticky early-return before the nearest-distance compare; `denom` backface reject)
- **Rule:** A sticky card that is hit at all wins immediately.
- **Why:** Both layouts overlap by design (`ZStagger`; the active grid's lower rows sit nearer the viewer), so nearest-hit alone oscillates the highlight.
- **Established by:** `23dc038`, `85bbb8c`
- **Breaks if:** Collapsing to a pure nearest-hit search.
- **Confidence:** high

### Pile captions use the game's SECTION-header keys
- **Where:** `PileViewer.Caption` (`GUI_CARD_SECTION_DISCARDED`, `GUI_CARD_SECTION_BURNT`)
- **Rule:** Nouns, not the `GUI_TAKE_DAMAGE_*` verb phrases.
- **Why:** The burnt caption showed the truncated German "Verfuegbare Karte Ver" — `GUI_TAKE_DAMAGE_BURN` is an action verb phrase ("1 verfügbare Karte verbrennen"). The discard caption was wrong in both languages and only masked by the English fallback string.
- **Established by:** `0435cf9` fix(cards): use game section-noun keys for pile captions
- **Breaks if:** Swapping in a key that "obviously" mentions burning or discarding.
- **Confidence:** high

### Stack embers: change-gated, lazily built, stopped not cleared
- **Where:** `PileViewer.PileStack.SetUsableHighlight`, `PileViewer.PileStack.BuildUsableEmbers` (`simulationSpace = Local`, `scalingMode = Hierarchy`, `SoftCueArt.MoteTexture()`)
- **Rule:** Built on the first `on` only (the discard/burnt stacks never pay for it); switching off calls `StopEmitting`, not `Clear`; Local sim space; an explicit soft-mote texture.
- **Why:** Change-gating stops the emitter being re-triggered per frame. World space would smear the motes into a trail behind the board when the player repositions it. `Sprites/Default` untextured draws hard **squares**, which is precisely the look being replaced. And a hard clear makes motes vanish mid-air, which reads as a bug when a turn ends.
- **Established by:** `4569ba9`
- **Breaks if:** `SetActive(on)` / `Clear()` instead of stop-emitting, or dropping the texture as decorative.
- **Confidence:** high

### Mote spawn Z is between the slab and the caption
- **Where:** `PileViewer.PileStack.BuildUsableEmbers` (just proud of the ±0.0009 top slab, but behind the count/caption at −0.0025)
- **Rule:** Both bounds matter.
- **Why:** Motes born inside the pile are invisible; motes in front of the caption fog the number.
- **Established by:** `4569ba9`
- **Confidence:** high

### The browse fan is a board-root child so it inherits scale and pose
- **Where:** `PileBrowser.PlaceAboveBoard`, `PileBrowser.BoardFloatHeight` (0.26) / `BoardFloatProudZ` (−0.05), `CardsConfig.BrowseFanOffset` (global, board-local, re-read every frame while board-anchored)
- **Rule:** Parented under `PlayTray.Current.Root`, position placed **once per open** (deliberately no per-frame follow), facing billboarded every frame; the offset is global rather than per-board because the anchor is board-local and already rides every board's pose and scale.
- **Why:** Being a board-root child means the fan tracks a two-hand resize and a board switch for free. The pile **stacks** were floated above the board in the same round and the user rejected that — it was reverted; only the browse fan floats.
- **Established by:** `cd42d15` → reverted by `99d8c9b` fix(cards): revert floating pile stacks; float the pile BROWSE fan above the board (#1); `dca727c` feat(cards): debug-menu-adjustable pile browse fan anchor (BrowseFanOffset)
- **Breaks if:** Re-floating the stacks, or adding a per-frame position follow.
- **Confidence:** high

---

## 14. Card faces, textures and render order

### `CardFace.Maintain` re-asserts the FULL anchor frame every frame
- **Where:** `CardFace.Maintain` (anchorMin/anchorMax/pivot/anchoredPosition3D/localRotation, plus `SetActive(true)`)
- **Rule:** All five fields, every frame — not just `anchoredPosition3D`.
- **Why:** `AbilityCardUI.ToggleFullCard(active: true)` sets `anchorMin = anchorMax = (0, 0.5)` — **left**-middle — on ActionSelection entry. With only the position restored, the face's pivot sat on the host canvas's left edge and the card art (and the backing fitted to it) rendered half a card left of the slot frame. The game also deactivates the face whenever it refreshes the invisible 2D hand.
- **Established by:** `f898041` fix(cards): re-assert face anchors/pivot every frame — action-selection x-offset
- **Breaks if:** Reducing to `anchoredPosition3D`, or moving the activation into `Adopt` as a one-shot.
- **Confidence:** high

### The fit scale is recomputed against the host's CURRENT size
- **Where:** `CardFace.RefreshFitScale`, `CardFace.Maintain` (`_host.rect.size != _fitHostSize` guard), `CardFace.BorderFraction` (0.06)
- **Rule:** Re-fit on adopt, on every re-pose, and whenever the host size changes; inset by 6 %.
- **Why:** `CardFace.Adopt` computed the fit while the host still carried the placeholder 270×400 size; `VRCard.AttachGameCard` then resizes that host to the real `FullAbilityCard` pixel size *right after* `Adopt` returns. When the real face is larger, the stale scale left the art shrunk inside a large canvas and `Maintain` re-asserted it every frame — so the mesh's dark rounded front showed as a **huge black frame** ("the BLACK BORDER around the cards is WAY TOO BIG"). The 6 % inset (≈ 3 %/1.9 mm per edge) is the tuned residue that makes the dark front read as a thin outline instead.
- **Established by:** `ff7b579` fix(cards): shrink the fat black card border to a thin rounded outline
- **Breaks if:** Caching `_fitScale` at adopt and dropping `_fitHostSize`, or "simplifying" `BorderFraction` to 0.
- **Confidence:** high

### The face is re-claimed from a `DialogPopup`, yielded to anything else
- **Where:** `CardFace.Maintain` three-way branch, `CardFace.IsDialogContent` (keyed on `GetComponentInParent<DialogPopup>()`), `CardFace.Yield` (restores `LockFullCard` only, touches **no** transform)
- **Rule:** A `DialogPopup` parent → pull the face back onto our own FaceCanvas. Any other foreign parent → yield, never fight. `Yield` must not re-parent or re-pose.
- **Why:** The game hands the LIVE face straight to `DialogPopup.Show(fullAbilityCard.gameObject)` for the burn/lose/discard confirm, **without** the healthy full-card preview prep. On a world-space float the card's custom screen-space shader then resolves to deep black, its hover FX quads blow up to popup canvas scale, and the burn flame overlay reads as a fullscreen sheet. `DialogPopup` restores the face to this same parent on Hide, so the re-claim is safe. Fighting an unrecognised parent produces a per-frame tug-of-war with game flows; writing the transform back in `Yield` would corrupt the dialog's layout mid-flow.
- **Established by:** `7db67dd` fix(cards): keep burn/lose confirm card readable on the dock (test #22)
- **Breaks if:** Folding the dialog branch into the generic yield branch, keying the detection off mod modal-dock state, or implementing `Yield` as "just call `Restore()`".
- **Confidence:** high

### Silhouette capture is a bounded retry latched only on success
- **Where:** `CardFace.s_silhouetteTried`, `s_silhouetteAttempts`, `CardFace.MaxSilhouetteAttempts` (16)
- **Rule:** Retry across adoptions; latch on success or once the budget is spent — **never** on the first attempt.
- **Why:** Card art loads async, so the first adopted card usually has no sprites yet; a plain one-shot burned on that attempt permanently blocked capture for every later card whose art *had* loaded. The budget stops an unbounded re-blit on a genuinely rectangular card set.
- **Established by:** `a9987cf` fix(cards): retry card-art silhouette capture until art loads (was one-shot, burned before async art)
- **Breaks if:** Converting to `if (s_tried) return; s_tried = true;`.
- **Confidence:** high

### Silhouette capture: tiny-icon skip, transparency skip, `stamped` gate, `finally` cleanup, blit readback
- **Where:** `CardFace.TryCaptureSilhouette`, `CardFace.ReadTexture`
- **Rule:** Images under 3 % of the face area and under 0.2 alpha are excluded; nothing is submitted unless something was stamped; every readback `Texture2D` is destroyed in a `finally`; source atlases are read via a GPU blit into a RenderTexture, never `GetPixels`.
- **Why:** Tiny icons are never part of the outer outline and would stamp interior blobs that pollute the footprint. An empty footprint fed to `SetSilhouette` is an invisible-card class of bug. Each readback is a full atlas-sized RGBA32 copy — leaking them over 16 retries blows Quest 3 VRAM. And a direct `GetPixels` throws on the real bundled atlases, which are not CPU-readable.
- **Established by:** `6d7f1bb` feat(cards): capture live card-art silhouette to drive the 3D outline (test #25)
- **Breaks if:** "Unioning everything for accuracy", moving cleanup to the success path, or replacing the blit with `GetPixels` since a `Texture2D` is already in hand.
- **Confidence:** high

### `SetSilhouette` rejects bad footprints and refuses without the Standard shader
- **Where:** `CardMesh.SetSilhouette` (`0.12 < frac < 0.985` **and** a centre-opaque test; hard requirement `edge.shader.name == "Standard"`)
- **Rule:** Near-empty, near-solid and hollow-centre footprints are rejected; no Standard shader means no silhouette at all.
- **Why:** Near-empty means a bad/early capture; near-solid means a plain rectangle, where clipping is a visual no-op *and* is the signature of a bad capture; hollow means the capture inverted. The guard's stated purpose: "it can never make a card invisible". A fallback shader would only alpha-blend (unlit, sorting hazards) — not worth the risk.
- **Established by:** `5b0cba0` feat(cards): alpha-clip card slab to the real art silhouette (test #25)
- **Breaks if:** Widening the bounds, dropping the centre test as paranoid, or relaxing the shader check to "any shader with alpha".
- **Confidence:** high

### Card materials are SHARED (live) — and the render-queue bump is REVERTED
- **Where (live half):** `CardMesh._edgeMaterial` / `_backMaterial` — shared singletons, mutated in place by `SetSilhouette`
- **Where (reverted half):** `VRCard.ApplyRenderOnTop` / `CardMesh.HeldCardRenderQueue` (4200) — **retained but inactive**, and annotated as such at the code
- **CORRECTED (phase 4):** this entry used to present both halves as current law. Only the first is. `VRCard.SetRenderOnTop(bool)`'s whole body is `_ = on; RestoreRenderOnTop();` — the bump was reverted because it swallowed all card TEXT (the revert note is in place at `VRCard.cs`). `ApplyRenderOnTop` has no caller, `_renderOnTop` can therefore never become true, `RestoreRenderOnTop`'s guard always returns, and all five `SetRenderOnTop` call sites are no-ops. The code is deliberately **kept** (same category as `PlayTray.SyncPinHolder`'s comment-with-no-code and `CardGlow`'s removed-pulse note, and matching the `FigureGrabbable` ruling in `INVARIANTS-Hands-Board-Core.md`) because it is the record of a tested-and-rejected approach.
- **Rule (live):** Card materials stay shared.
- **Rule (dormant, applies only if the bump is ever revived):** it must be per-instance, ZTest **LEqual** + ZWrite **On** preserved. `Net.RemoteHandFan` is the third party that makes it so.
- **Why:** Sharing is what lets a silhouette captured *after* cards exist re-shape every live card at once. The queue bump must be per-instance because those same shared materials clothe the opponent's hand backs — a shared write would draw every remote card on top of everything. The queue value (4200) is chosen to beat the `ButtonCluster` label's 4003 and the held mini's 4100; ZTest stays LEqual so the card still self-occludes and hides behind real walls and board geometry. An earlier attempt that used ZTest Always destroyed card text and figure depth and was reverted — and the *whole* bump was reverted after it, for swallowing card text. The `4200 > 4100 > 4003` ordering survives the revert: it is still the design rationale for `PlayTray`'s and `ButtonCluster`'s widget queues.
- **Established by:** `5b0cba0` (shared), `9e78031` fix(cards): draw VR cards over control-board button widgets (Bug #2) — after `4ef1d76` reverted the ZTest-Always version
- **Breaks if:** Making materials per-card "for safety" (silhouette stops propagating), writing the queue on the shared instance (remote hand backs render on top), lowering 4200 toward 4003, or switching to ZTest Always.
- **Confidence:** high

### Card mesh winding, rim UV inset and mirrored back alpha
- **Where:** `CardMesh.Build` (front fan `center → next → i`, back fan reversed; back UVs mirrored in x), `CardMesh.RimUvInset` (0.02), `CardMesh.SetSilhouette` (back alpha sampled mirrored, lattice RGB unmirrored)
- **Rule:** Front and back windings are opposite; rim vertices sample ~2 % **inside** their geometric outline point, identically front and back; only the back **alpha** is mirrored.
- **Why:** The outline is CCW in XY and the viewer is on −Z, so the front needs clockwise winding — this file is the mod's cited ground truth for the convention. The rim inset makes the 1.5 mm edge sample fully opaque interior instead of straddling the ~0.5 alpha boundary, so it survives the alpha clip. The back is drawn through mirrored UVs, so its alpha must mirror to line up with the front; the lattice RGB is left-right symmetric, so mirroring it would be invisible either way.
- **Established by:** `321a619` (mesh), `5b0cba0` (silhouette/UVs)
- **Breaks if:** Normalising both faces to one winding order, setting `RimUvInset` to 0 for "correct" planar UVs, or "fixing the inconsistency" by mirroring both or neither.
- **Confidence:** high

### The card-back pattern texture stays CPU-readable
- **Where:** `CardMesh.GetBackTexture` (`Apply(updateMipmaps: true, makeNoLongerReadable: false)`)
- **Rule:** `false`, deliberately.
- **Why:** `SetSilhouette` samples this pattern with `GetPixelBilinear` to composite the card back with the captured outline alpha. Setting it `true` to match the mip-bake textures throws the first time a silhouette lands.
- **Established by:** `5b0cba0`
- **Confidence:** high

### `CardFaceRaycaster` answers only mod pointer ids
- **Where:** `CardFaceRaycaster.Raycast`, `CardFaceRaycaster.ModPointerIdCeiling` (−100)
- **Rule:** Any `pointerId > −100` (the game's mouse −1..−3, touch ≥ 0) gets an empty result from a card FaceCanvas.
- **Why:** Card-area highlights followed **head** movement. The half-hover FX is pure uGUI pointer enter/exit and never reads `InputManager.CursorPosition` — the coupling came from the game's InControl input module, whose per-frame `EventSystem.RaycastAll` uses the **parked** virtual/hardware mouse pixel through each FaceCanvas's `worldCamera` (the **head** camera). A fixed pixel through a moving camera sweeps a world ray across the fan and the docked cards, firing enter/exit purely from head motion. The board pick patches were innocent. Fixing it at the card level leaves every `CursorPosition` consumer (board hover, tooltips, melee-AoE facing) untouched by construction.
- **Established by:** `0ed8d3c` fix(cards): overlay count from placeable state; laser-only card hover (task #5)
- **Breaks if:** Replacing the component with a stock `GraphicRaycaster`; letting the ceiling drift out of sync with `Hands.Interact.UguiPointer`'s private id block (poke −101/−102, laser −111/−112) — the "keep in sync" comment exists because there is no compile-time link; or attempting the fix in cursor code instead.
- **Confidence:** high

### Mip bake: CPU row-slices of a FULL-rect readback, never a sub-rect `ReadPixels`
- **Where:** `CardFaceMipBake.ReadbackAtlasPixels` (the single, full-rect-only readback primitive), `CardFaceMipBake.TrimmedReplacementFor` (`Array.Copy` row slices)
- **Rule:** There is exactly ONE readback path and it always reads the full rect.
- **Why:** On D3D11 (the shipping player) the RenderTexture has a top-left origin; Unity's flip compensation makes a **full**-rect `ReadPixels` come out upright — a full rect is invariant under `y → H − y − h`, which maps 0 → 0 — but a **partial** rect's Y origin lands at the flipped position, so the copy pulled rows from around `atlasH − srcY − h`: a completely different part of the atlas. `XPArrow` requested at y = 3174 actually read ~y = 858, producing the white/garbled icons in the bug screenshot. Invisible to GL-minded reasoning; the classic `UNITY_UV_STARTS_AT_TOP` trap.
- **Established by:** `b6c8bba` fix(cards): CPU-slice per-sprite regions from the proven whole-atlas readback (v4)
- **Breaks if:** "Optimising away the 64 MB readback" with `ReadPixels(new Rect(srcX, srcY, w, h), …)`, or adding a second readback helper that takes a rect.
- **Confidence:** high

### Skip, never clamp: tight-packed, rotated and inexact-trim sprites
- **Where:** `CardFaceMipBake.IsTightPacked` (mode query **and** the throwing-`textureRect` catch), `CardFaceMipBake.IsRotatedPacked`, `CardFaceMipBake.TrimmedReplacementFor` (exact-fit contract, else skip with a named log line)
- **Rule:** A sprite is swapped only when its reconstruction is provably exact. The hard rule is "corrupt never, mipless ok".
- **Why:** Tight packing interleaves different sprites' polygon meshes, so any rectangular copy of the sprite's atlas area contains fragments of **neighbouring** sprites that the original polygon mesh never renders — and a FullRect replacement renders them all. That was the v3 corruption (white angular artifact next to "Angriff 3", garbled fragments at "Springen"). A rotated placement is not expressible as a rect sprite. And clamping a borderline trim rect means cropped or misplaced content on the card. The v3 mesh-UV-bounds fallback that guessed is **deleted**, and must stay deleted.
- **Established by:** `3f4c4df` fix(cards): skip tight-packed sprites in per-sprite mip bake — rect copies dragged in neighbor pixels
- **Breaks if:** Re-adding a UV-bounds fallback to "recover" these sprites, narrowing the catch blocks because the mode query "already covers it", or inserting a `Mathf.Clamp` to rescue borderline geometry — that is literally the v3 bug.
- **Confidence:** high

### Skip log lines are the only evidence channel
- **Where:** `CardFaceMipBake.LogSpriteSkip` and the per-sprite bake log
- **Rule:** Every skip logs its reason, once (the null cache dedupes).
- **Why:** With no automated tests the hardware log is the only diagnostic; silent skips made the "shimmer persists" round undiagnosable — the log showed 8/8 texture bakes consumed while the sprites were quietly not swapped.
- **Established by:** `4ae5335` (T3), `3f4c4df`
- **Breaks if:** Deleting them as log spam.
- **Confidence:** high

### Failure verdicts are cached as null
- **Where:** `CardFaceMipBake.s_replacementBySource`, `s_bakedByTexture`, `s_bakedByIdentity`, `s_regionTextureByKey`, `s_atlasPixelsByIdentity` — all store `null` on failure
- **Rule:** Negative caching everywhere; failures are permanent, never retried.
- **Why:** `Rescan` runs every second per adopted card. An uncached failure is a per-second GPU blit storm.
- **Established by:** `6c90126` (T3), `b6c8bba`
- **Breaks if:** Changing a lookup to `if (cache.TryGetValue(k, out v) && v != null)` so nulls fall through and retry.
- **Confidence:** high

### Two caches: instance id AND content identity
- **Where:** `CardFaceMipBake.BakedTextureFor`, `CardFaceMipBake.IdentityOf` (`name|WxH|format`)
- **Rule:** Both dictionaries must exist.
- **Why:** The hardware log showed the same 4096² `sactx-…` atlas whole-baked **twice** at ~85 MB VRAM each — two `Texture2D` instances wrapping the same atlas. An instance-id cache alone cannot see that. The `sactx` names embed a content hash, which is what makes name-based identity sound here.
- **Established by:** `b6c8bba`
- **Breaks if:** Deleting the identity dictionary as "redundant with the instance cache".
- **Confidence:** high

### Budgets: 24 atlases, 256 MB pixel cache, over-budget is slower not wrong
- **Where:** `CardFaceMipBake.MaxBakedTextures` (24), `MaxAtlasPixelCacheBytes` (256 MB), `CardFaceMipBake.AtlasPixelsFor` (over budget → return **uncached**, do not refuse)
- **Rule:** The atlas cap logs when hit; the pixel budget never fails a bake.
- **Why:** The first cut's cap of 8 was fully consumed in the hardware log (per-class art strips arrive async and stack up across classes), so any later class's art was silently left mipless; 24 covers a full four-class party with headroom. And correctness must never depend on cache capacity — the readback still happens, it is just not retained.
- **Established by:** `4ae5335` (budget 8 → 24), `b6c8bba` (pixel cache)
- **Breaks if:** Lowering the cap "to save VRAM", removing the over-budget warn, or moving the budget check before the readback "to save the work".
- **Confidence:** high

### Mip-0 is captured before `makeNoLongerReadable`
- **Where:** `CardFaceMipBake.Bake` (`GetPixels32()` before `Apply(updateMipmaps: true, makeNoLongerReadable: true)`)
- **Rule:** In that order, in the same method.
- **Why:** Retaining mip 0 saves the per-sprite path a second full readback of the same atlas — and after `makeNoLongerReadable: true` the data is gone.
- **Established by:** `b6c8bba`
- **Breaks if:** Hoisting `Apply` for readability.
- **Confidence:** high

### The bake path is sRGB; the silhouette path is Linear — deliberately
- **Where:** `CardFaceMipBake.Bake` / `ReadbackAtlasPixels` (`RenderTextureReadWrite.sRGB`, destination `linear: false`) vs `CardFace.ReadTexture` (Linear)
- **Rule:** Do not unify them.
- **Why:** The bake must be sRGB round-trip preserving so values match the original in both linear and gamma colour-space projects; the silhouette path only reads **alpha**, so its settings are irrelevant. Sharing one helper shifts the baked card art's colour.
- **Established by:** `6c90126`, `b6c8bba`
- **Breaks if:** Extracting a common readback helper.
- **Confidence:** high

### Rescan skips Images already wearing a replacement; both scans include inactive
- **Where:** `CardFaceMipBake.Rescan` (`s_originalByReplacement.ContainsKey(...)` early-continue; `GetComponentsInChildren<Image>(includeInactive: true)`), `CardFaceMipBake.RestoreSprites` (same inclusive scan)
- **Rule:** Never re-process our own replacement; always include inactive children on both scan and restore.
- **Why:** The caches are keyed on the **source** id, so a replacement fed back through `ReplacementFor` bakes a copy-of-a-copy every second. And the game toggles face sub-widgets (enhancement slots, XP orbs) — swapping them while hidden means re-activation shows the baked copy at once, and symmetrically the restore must reach them or inactive Images carry our textures into the pool.
- **Established by:** `6c90126`, `4ae5335`
- **Breaks if:** Removing the early-continue as "redundant, the cache handles it", or matching `includeInactive: false` to the (deliberately different, diagnostics-only) call in `CardFace.LogFaceTextureDiag`.
- **Confidence:** high

### One-shot diagnostics latch on the right condition
- **Where:** `CardFace.LogFaceTextureDiag` (`s_texDiagLogged` set only when `count > 0`, but also set in the `catch`)
- **Rule:** Two different latch policies for two different outcomes.
- **Why:** Zero textures means the art is still loading async — retry on a later adoption. An exception must never spam a throwing path.
- **Established by:** `8454e88` (task 3)
- **Breaks if:** Hoisting the latch to the top of the method as a normal one-shot guard.
- **Confidence:** high

### `CardActionQueue` pumps exactly one entry per frame, with `onDone` on the same frame
- **Where:** `CardActionQueue.Pump`
- **Rule:** One entry per `Update`; the completion callback runs immediately after the action; the action and the callback have **separate** try/catch blocks.
- **Why:** `CardsHandUI.OnCardSelected/OnCardDeselected` spin-wait up to 1 s for the ScenarioRuleLibrary ack, and every select/deselect path funnels through them — there is no spin-wait-free entry point. One per frame means the block lands on a single frame boundary (reprojection covers it) and never re-enters game code from inside another callback; two in one frame is a 2 s hitch in VR. `onDone` runs the same frame because by then the SRL has acked or timed out, so game-state reads are final and the caller can verify the outcome ("did the card actually enter the round?"). Separate catches because those verify-callbacks are how the driver recovers from a refused call — swallowing them strands the VR card mid-flight.
- **Established by:** `b9bbc61` feat(cards): game-API layer, module config, serialized action queue
- **Breaks if:** `while (Queue.Count > 0)` to "drain the backlog faster", deferring `onDone` to the next pump, or merging the two try blocks.
- **Confidence:** high

### Half zones are invisible triggers that still track validity
- **Where:** `HalfSelection.HalfZone` (BoxCollider trigger only, no renderer), `HalfSelection.SetPlayable`
- **Rule:** An invalid half's poke is a no-op, but nothing is ever drawn.
- **Why:** The mod's per-half hover tint doubled the game's own uGUI highlight (rejected by the user), and the invalid-half dim quad read as a 55 %-black sheet over the cards. `SetPlayable` is still load-bearing: it gates `OnPoke`.
- **Established by:** `3afd786` fix(cards): drop the half-zone hover tint — game highlight only; `bc30911` (item 9)
- **Breaks if:** Re-adding a dim/tint quad "so the player can see which half is invalid", or deleting `SetPlayable` as unused-because-invisible.
- **Confidence:** high

### Half commits use the game's own click with `checkValid: true`
- **Where:** `HalfSelection.RequestPlay` → `CardsGameApi.PlayHalf` → `FullAbilityCard.OnAbilityClick(type, isProxyAction: false, checkValid: true)`
- **Rule:** Identical to the 2D buttons; the mod only mirrors the phase machine, never drives it.
- **Why:** All validity and phase guards apply, and `CardsActionControlller` (Select1st → Pick1st → Select2nd → Pick2nd) is what decides which halves are playable. This overload also has no spin-wait, unlike the select path.
- **Established by:** `8061069`, `0c8fab9`
- **Breaks if:** A direct state mutation or `checkValid: false` shortcut because the mod "already checked".
- **Confidence:** high

### Docked action cards are poke-only, and the head layout is a fallback
- **Where:** `HalfSelection.SetCards` (`card.Grabbable = false`), `HalfSelection.SetVisible` (`PlaceAtHead()` only when `DockSlot(0) == null`)
- **Rule:** No grab in this layout; head-floating placement runs strictly as the no-tray fallback.
- **Why:** A grab on a docked card during action selection pulls it out of the slot mid-commit. Docked cards parent under the slot transforms and are therefore stable under tray follow/pin/move/resize; calling `PlaceAtHead` unconditionally double-positions them and makes them drift with the head.
- **Established by:** `0a3faa1` feat(cards): dock the action-selection cards into the tray's card slots
- **Breaks if:** Unifying with the fan/browse layouts where cards *are* grabbable.
- **Confidence:** high

### `UguiPokeSurfaces` registration is strictly paired
- **Where:** `HalfSelection.RegisterCanvas` / `DisarmCard` / `DestroyZonesFor`, tracked by `set.RegisteredCanvas`
- **Rule:** One register, one unregister, tracked by a nullable field nulled on unregister.
- **Why:** Registration is what keeps the game's own face widgets (default-action, consume, infusion buttons) reachable by finger poke and laser. A leaked registration keeps a dead or pooled canvas in the poke registry.
- **Established by:** `8061069`
- **Breaks if:** Calling `RegisterCanvas` unconditionally, or skipping the disarm for cards leaving the layout.
- **Confidence:** high

### Active-column housekeeping
- **Where:** `ActivePileViewer.EnsureBuilt` (`_cards.Clear()` when rebuilding under a fresh mount; `_root != null` uses Unity fake-null on purpose), `ActivePileViewer.Relayout` (`card.ResetColliderRegion()` per card; grid recentred), `ActivePileViewer.Columns` (3)
- **Rule:** Drop stale refs on rebuild; reset each card's collider region; keep the grid symmetric about the mount and vertically centred.
- **Why:** A tray teardown destroys the column with the mount, and Unity's fake-null is what makes the `== null` check true — prior card refs are stale. Active cards are **not** fan-stripped, so a card arriving from the hand fan carries its narrowed collider and is mostly un-hittable in the grid. Top-anchored layout would drift the column off the board edge as the pile grows.
- **Established by:** `3d63abb`, `85bbb8c` (grid)
- **Breaks if:** Using `ReferenceEquals(_root, null)` (defeats fake-null), dropping the `Clear()`, moving `ResetColliderRegion` to `SetCards` only (plucked-and-returned cards route through `Relayout`), or anchoring the grid top-left.
- **Confidence:** high

### Rest keycaps stay up while their rest is selected
- **Where:** `RestControls.TickStatus` (`visible = can || selected`; edge-triggered logging seeded `true`)
- **Rule:** Not just `can`.
- **Why:** A keycap that vanishes the frame the player presses it gives no confirmation; the accent on the still-visible cap *is* the commitment feedback. The log seed is `true` because the keycaps are built visible, so the first "not relevant" tick logs the hide.
- **Established by:** `38c171e` feat(cards): rest buttons hide when irrelevant; played cards fly to their pile on clear
- **Breaks if:** Reducing to `SetVisible(canShort)`, or seeding the log flag `false`.
- **Confidence:** high

### `Loc.OnChanged` is subscribed once and always detached
- **Where:** `RestControls.EnsureBuilt` / `Destroy`, `ActivePileViewer.EnsureBuilt` / `Destroy` (`_locHooked`)
- **Rule:** Subscribe-once flag plus an unconditional detach.
- **Why:** `EnsureBuilt` runs on every tray rebuild and board switch; without the flag each rebuild adds another handler to a **static** event holding a reference to a destroyed viewer.
- **Established by:** `692d51f` feat(loc): localize all mod VR text to follow the game language live
- **Breaks if:** Removing `_locHooked` as "the `+=` is idempotent" — it is not, for instance methods on new instances.
- **Confidence:** high

### The bundle is reused, never re-loaded, and unloaded only if owned
- **Where:** `VRCardFactory.GetBundle` (scan `AssetBundle.GetAllLoadedAssetBundles()` first; `_bundleOwned`), `VRCardFactory.Dispose` (`Unload(unloadAllLoadedObjects: false)`)
- **Rule:** Reuse a loaded instance, own only what we loaded, and never destroy already-instantiated objects on unload.
- **Why:** The Hands module loads the same file, and Unity forbids loading a bundle twice — `LoadFromFile` on an already-loaded bundle fails outright. `unloadAllLoadedObjects: true` would null out live boards and backings mid-session on hot reload.
- **Established by:** `dad90f4` feat(cards): VR card objects hosting the game's live card canvases
- **Breaks if:** Replacing the probe with a direct `LoadFromFile`, unloading unconditionally, or flipping the unload flag "to reclaim memory".
- **Confidence:** high

### Parking is reparenting to an inactive root
- **Where:** `VRCardFactory.PoolRoot` (`SetActive(false)` + `HideAndDontSave` + `DontDestroyOnLoad`)
- **Rule:** Inactivity is a property of the **parent**.
- **Why:** `Park` works purely by reparenting; making the root active and deactivating cards individually breaks that, and `DontDestroyOnLoad` keeps parked cards alive across scenario loads.
- **Established by:** `dad90f4`
- **Breaks if:** Restructuring the pool to per-card `SetActive`.
- **Confidence:** high

### Face detach precedes card destruction
- **Where:** `VRCardFactory.ReleaseWidget` (`_byWidget` entry removed → `card.DetachGameCard()` → `Object.Destroy(card.gameObject)`)
- **Rule:** That order.
- **Why:** Destroying the VR card with the face still parented under it destroys the game's live `FullAbilityCard`. Removing the map entry first prevents re-entrant lookups during teardown.
- **Established by:** `dad90f4`
- **Breaks if:** Trusting `Destroy` to "take the face with it".
- **Confidence:** high

### Board prefab lookup has a three-tier logged fallback
- **Where:** `VRCardFactory.GetTrayPrefab` (selected board → Oak → null/procedural)
- **Rule:** Each tier logs; a missing prefab never throws.
- **Why:** Steel and Bronze were added before their assets shipped; the code has to run either way, and the log says which board actually rendered.
- **Established by:** `a0a39e2` feat(cards): switchable control board selectable in VR settings
- **Breaks if:** Removing the Oak tier once "all boards are in the bundle".
- **Confidence:** medium

---

## 15. Patches, suppression and the action queue

### `CardsHandManager.Show` is NEVER prefix-skipped
- **Where:** `Patches/HandSuppressionPatches` (postfixes only), file header
- **Rule:** `Show` always runs in full; only its visual result is suppressed.
- **Why:** Its body mixes visuals with bookkeeping the whole card pipeline depends on: `SwitchHand` → `Choreographer.OnSwitchHand`; `CardsHandUI.UpdateView → SetMode` → per-card selectability, validity, the long-rest gate **and** `CardsActionControlller.Init(top, bottom, …)` (the half-selection phase machine is seeded here); and the push/pop fields `Choreographer` reads to restore the hand after interrupts. Skipping `Show` leaves `AbilityCardUI.OnClick`'s guards stale and breaks `SelectCard`/`UnselectCard` and the whole VR flow.
- **Established by:** `181ddbb` feat(cards): 2D-hand visual suppression + pool-safety patches
- **Breaks if:** "Optimising" the suppression to `return false` — the obvious simplification, and the one this entire header exists to forbid.
- **Confidence:** high

### Three Show/ShowHands postfixes, none redundant
- **Where:** `CardsHandManager_ShowList_Patch`, `CardsHandManager_ShowAll_Patch`, `CardsHandManager_ShowHands_Patch`
- **Rule:** All three stay: private `ShowHands()` is the single reliable "hand became visible" choke point; the `List` overload is kept **additionally** because only it carries the `(playerActor, mode)` payload.
- **Why:** The active-hand overload and the `ShowCoroutine` iterator do **not** pass through the List overload, but every path ends in `ShowHands()`.
- **Established by:** `181ddbb`
- **Breaks if:** Deduplicating to "just the public overloads" (SwitchHand/coroutine paths stop rebuilding the VR fan) or "just `ShowHands`" (the `HandShown` event loses its actor/mode payload).
- **Confidence:** high

### Suppression writes the CanvasGroup every frame and never the state flags
- **Where:** `HandSuppression.Tick` (alpha 0, `blocksRaycasts = false`, re-asserted per frame; `window.IsOpen`/`VisualState` untouched)
- **Rule:** CanvasGroup only, every frame.
- **Why:** `UIWindow`'s own show-tween writes alpha 1, so a one-shot at `Arm()` is overwritten. And leaving the state flags alone is what keeps `CardsHandManager.IsShown` and everything reading it behaving exactly as in 2D.
- **Established by:** `181ddbb`
- **Breaks if:** A one-shot on arm, or "simplifying" to `window.Hide()` / `SetActive(false)`.
- **Confidence:** high

### The dialog rescue requires the dialog to be INSIDE the suppressed subtree
- **Where:** `HandSuppression.Tick` (`dialogPopup.transform.IsChildOf(window.transform)`)
- **Rule:** Both conditions — open **and** a descendant.
- **Why:** The short/long-rest flows re-parent `UIManager.Instance.dialogPopup` under `CardsHandManager.Instance.transform`; if that lands inside the suppressed subtree the dialog would be invisible. Reducing to `dialogPopup.IsOpen()` means any unrelated game dialog un-hides the whole 2D hand into the player's face.
- **Established by:** `181ddbb`
- **Breaks if:** Dropping the ancestry test.
- **Confidence:** high

### `!BurnActive` gates the lift
- **Where:** `HandSuppression.Tick`
- **Rule:** While a burn is active the dialog-rescue lift stays **down**.
- **Why:** The flat 2D burn dissolve/flame is the game's screen-space uGUI on its hand canvas. The burn-confirm dialog normally lifts suppression, un-hiding that canvas, and `FlatScreen` then mirrors the burning card into the VR modal quad. The ordering that makes this safe: the confirm click happens *before* the burn effect starts, so the dialog is still visible when the player commits; only the post-confirm burn is flat-suppressed.
- **Established by:** `bc30911` fix(cards): drop invalid-half dim quad; suppress flat burn leak (item 2)
- **Breaks if:** Dropping it as "the dialog should always be visible".
- **Confidence:** high

### BOTH burn edges refresh the tail hold
- **Where:** `HandSuppression.BeginBurn` **and** `HandSuppression.EndBurn` both call `RefreshBurnHold()`; `BurnTailSeconds` 0.5
- **Rule:** `EndBurn` refreshing the hold is not a bug.
- **Why:** `BurnCardFx`'s per-card effect detection flickers on and off between frames — the card's `CardEffects` FXTask is transiently unreadable while its face is re-adopted — which oscillated the ref-count `BeginBurn`/`EndBurn` every frame and strobed the flat screen-space burning card onto the `FlatScreen` mirror. Refreshing on **both** edges keeps `BurnActive` solid across the flicker and past the true end.
- **Established by:** `cd42d15` (task #10)
- **Breaks if:** "Correcting" `EndBurn` to only decrement — the intuitively right thing, and the regression.
- **Confidence:** high

### The trailing-ModalUI latch, with a 6 s cap
- **Where:** `HandSuppression.BurnActive` (`_burnModalLatch`, self-releasing the instant the mode leaves `ModalUI`), `_burnLatchCapUntil` / `BurnModalLatchCapSeconds` (6 s)
- **Rule:** Past the wall-clock tail, hold `BurnActive` while the mode is `ModalUI`; release immediately when it is not; hard-cap the hold.
- **Why:** After the burn-confirm dialog closes the game stays UI-locked for a **short, variable** window while it resolves (removes the sacrificed card, settles piles). The FX and its fixed 0.5 s tail routinely expired first, so `FlatScreen.WantVisible`'s empty-`ModalUI` catch-all raised the full desktop-mirror quad for a few frames. A fixed timer cannot cover a variable lock: any value either flashes or over-suppresses. The self-release matters because burns that never enter `ModalUI` (a card lost/discarded from hand, plays in CardSelection/HalfSelection) clear the latch on the first post-tail read. The cap is the stuck-on guard: a permanently-hidden 2D hand with the dialog rescue blocked is an unrecoverable soft-lock.
- **Established by:** `252b45d` fix(cards): eliminate end-of-burn FlatScreen flash via trailing-ModalUI latch
- **Breaks if:** Replacing it by raising `BurnTailSeconds`, or removing the cap as unreachable defensive code.
- **Confidence:** high

### Shutdown clears static burn state
- **Where:** `HandSuppression.Restore` (resets `_burnCount`, `_burnHoldUntil`, `_burnModalLatch`, `_burnLatchCapUntil`; writes alpha 1 only when `window.IsOpen`), `CardsSignals.Clear`
- **Rule:** All statics reset; visibility restored only for a window the game considers open.
- **Why:** The module is hot-reloadable; a stale `_burnCount > 0` makes `BurnActive` permanently true on the next session, and a surviving static event holds a destroyed `CardsDriver`.
- **Established by:** `181ddbb`, `252b45d`
- **Breaks if:** Setting `alpha = 1` unconditionally, or trimming the resets because "the driver unsubscribes in `OnDestroy`" — `Clear` is the belt to that braces.
- **Confidence:** high

### Pool-safety patches are PREFIXES
- **Where:** `CardsHandUI_OnDestroy_Patch.Prefix`, `CardsHandUI_DestroyCardUI_Patch.Prefix`; `CardsSignals.RaiseHandDestroying` / `RaiseCardRecycling` (try/catch around every invoke)
- **Rule:** Both signals fire **before** the game's recycle, and a throwing subscriber must not propagate.
- **Why:** The game pools `AbilityCardUI` objects; if a card went back to the pool while its face is still parented under a VR object, the pool would hold a corrupted widget. Converting to postfixes means the widget is already in the pool — permanently corrupted. An exception escaping a prefix aborts the game's own recycle mid-way, which is exactly the corruption these patches exist to prevent. (`OnDestroy` was chosen as a target because it is a Unity magic method and therefore never inlined.)
- **Established by:** `181ddbb`
- **Breaks if:** Converting to postfixes "since the object is definitely gone by then", or removing the guards as "we control all subscribers".
- **Confidence:** high

### The burn/lose commit gate is the defensive last line
- **Where:** `CardsHandUI_OnLoseCardClick_Gate.Prefix` (refuse when `SelectedCards.Count < maxCardsSelected`, then `TakeDamagePanel.ToggleVisibility(true)`)
- **Rule:** Refuse locally, restore the damage row, fake nothing, send nothing on the wire.
- **Why:** The 2D game keeps this safe **by construction** — the popup only opens at full selection and locks every card while open — so `OnLoseCardClick` blindly indexes `selectedCardsUI[0]/[1]`. VR free card placement broke that invariant (cards plucked back while the popup showed); the commit threw, the catch routed into `GlobalErrorMessage` → `ErrorHandlingUnloadSceneAndLoadMainMenu`, and the scenario was torn down — the reproduced crash to menu. Restoring the damage row is what keeps the player from being stranded without an affordance.
- **Established by:** `ae1f6e7` (task #11c)
- **Breaks if:** Removing it because `CardsDriver` already cancels the popup on a pick-card grab — this is deliberately the second line behind that.
- **Confidence:** high

### Burn hover suppression covers ENTER and EXIT, but never the click
- **Where:** `TakeDamagePanel_BurnHover_Skip` (all four of `OnMouseEnter/ExitBurnOne/Two`), `DialogPopup_Show_HoverStrip` (strips `onMouseEnter`/`onMouseExit` only, on the `List<GameObject>` overload only)
- **Rule:** Symmetric enter/exit suppression; click paths untouched; the five-argument List overload is the patch target.
- **Why:** With the enters gone, `ResetPreviewing` on exit would still re-run `BurnAvailableCard`/`BurnDiscardedCards` for a toggled option — a hidden hover side effect. The card-content dialog options carry enter/exit actions that run the card's full burn/ghost dissolve timeline **both ways**, so every laser pass over "Karten verbrennen" churned a theatrical dissolve on the dock card. The single-GameObject overload delegates to the List one, and the string-content overloads (plain text dialogs) must stay untouched. A deliberate click must still preview.
- **Established by:** `ae1f6e7` (task #10)
- **Breaks if:** Patching only the enters, pushing the suppression down into `Preview*` (the intentional toggle preview dies), or calling `RemoveAllListeners()` on `onClick` too — the burn can then never be committed.
- **Confidence:** high

---

## 16. Config, migrations and diagnostics

### One-time migrations are marker-gated and never touch user-tuned values
- **Where:** `CardsConfig.Bind` — the `BoardScaleDefault04Applied` marker and the roll-gate v3 scale migration. `TableScaleDefault25Applied` lives in `Rig/ComfortSettings.cs`; the `[TransientButtons]` → `[RoundButtons]` / `[SquareCaps]` fan-out lives in `WorldUI/ButtonTuning.cs`
- **CORRECTED (phase 4):** this entry (and the "config categories" entry below it) used to attribute all of that to `CardsConfig.Bind`, and the button sections with it. `CardsConfig.cs` binds **exactly one section, `[Cards]`** — 100 `_file.Bind("Cards", …)` calls and nothing else. `[RoundButtons]` / `[BoardButtons]` / `[BoardDashboard]` / `[RestButtons]` are all `WorldUI/ButtonTuning.cs`. Anyone auditing a migration from this entry would have looked in the wrong file.
- **Also note:** `BoardScale_{board}` binds at default **0.5** while this migration writes **0.4** (and the marker key still says `04`). See the comment at the migration; harmonising the two numbers is a behaviour change, not a tidy-up.
- **Rule:** A changed default is adopted only where the saved value is *exactly* the old default; a marker makes each migration run at most once per config file; a grab-written value (`TrayScale`) is **never** migrated.
- **Why:** BepInEx keeps saved values, so a changed default alone only reaches fresh installs. Adopting it unconditionally would stomp deliberate tuning. The roll-gate migration is subtler still: it requires **both** enter ≥ 84.9° and exit ≥ 75° — a pair that is physically absurd on the new 0–90° scale but exactly what any old 0–180° config lands on after BepInEx clamps it — specifically so a deliberately step-maxed new-scale enter is not eaten.
- **Established by:** `f7c9b88` feat(config): default table scale 2.5x, board default 0.4x to match; `d7ec01c` (roll-gate migration); `e0432fe` fix(buttons): per-category geometry binds, numeric defaults, Versatz Z, rigid cluster dock
- **Breaks if:** Dropping a marker "because the value is already correct", or widening a migration predicate.
- **Confidence:** high

### Config categories must not leak across button families
- **Where:** the `[RoundButtons]` / `[BoardButtons]` / `[BoardDashboard]` / `[RestButtons]` accessors — **in `WorldUI/ButtonTuning.cs`, not `CardsConfig`** (see the correction above); `PlayTray.BoardButton.Create` takes press travel per instance
- **Rule:** Nothing outside a category reads its binds; every bind's default is the exact authored value it replaced.
- **Why:** The user's stated requirement — one shared `[SquareCaps]` set silently resized unrelated button families. Numeric defaults (rather than a `0 = auto` sentinel) are what make the shipped look bit-identical and the steppers show real numbers.
- **Established by:** `e0432fe`
- **Breaks if:** Re-introducing a shared geometry section, or a `0 = auto` sentinel.
- **Confidence:** high

### An unbound config key is silently dropped from the user's file
- **Where:** `CardsConfig` — the `DebugMenu` removal note (a comment with no code)
- **Rule:** Removing a bind is a *user-visible* act; the reason must be recorded where the bind used to be.
- **Why:** Stated in-source: "an unbound key is simply dropped from the user's cfg on the next save, and there is nothing for it to switch any more." This is the documented precedent for how to retire a config entry in this codebase.
- **Established by:** `cc99144` fix(ui): audit every VR setting — remote-board mode now gates ALL remote content
- **Breaks if:** Deleting the comment as dead, or retiring another bind without leaving the equivalent note.
- **Confidence:** high

### Throttled diagnostics use shared static clocks and unscaled time
- **Where:** `VRCard.s_nextLaserRectLogAt`, `VRCard.s_nextFanColliderLogAt`, `VRCard.s_nextAnimLogAt`, `VRCard.s_nextRootedLogAt`, `CardsDriver.NoteTickThrow`, `PlayTray.BoardButton` press logs, `CardFan` curve/param logs
- **Rule:** The throttle clock is **static** (shared across all instances) and uses `Time.unscaledTime`.
- **Why:** A relayout that animates several cards in one frame must log a couple of lines, not a storm — a per-instance clock does not throttle a storm at all. Unscaled because the game pauses during card phases, which is exactly when most of these fire.
- **Established by:** `306e8ea`, `6b36616`, `25fe23a`, `bce9a63`
- **Breaks if:** Making the clocks instance fields, or switching to `Time.time`.
- **Confidence:** high

### Diagnostics carry the numbers that make the next hardware log conclusive
- **Where:** `VRCard.LogLaserRectDecoupled` (resting centre vs live transform + pop), `VRCard.LogFanColliderFit` (box world centre/size vs the visible rect), `PlayTray.BoardButton.LogCapDiagnostics` (`CLOSED SOLID`, 40/20/6-24-30), `CardsGameApi.DescribeActionGate` / `DescribeConfirmGate` / `DescribeUndoGate` (built only on rejection), `CardsDriver.LogFanState`
- **Rule:** These lines state the fix in one number; the gate descriptions are constructed **only** on rejection, never per frame.
- **Why:** This is the entire verification loop for a subsystem with no tests. Three rounds of logs never showed the lift-priority branch because it was silent unless a grab fired. And a per-frame gate description is a per-frame allocation.
- **Established by:** `4330504`, `6b36616`, `32d3151`, `5b3ac2d`, `7fa3095`
- **Breaks if:** Deleting them as noise, or hoisting a description build out of its rejection branch.
- **Confidence:** high

### Perf scopes wrap bodies without altering exception flow
- **Where:** `CardsDriver.Update` → `using (PerfMonitor.Scope("Cards.Driver")) UpdateBody();`; `VRCard.Update` → `Scope("Cards.VRCard")`; `Core.PerfConfig.FanRelayoutInterval`
- **Rule:** The scope wraps a private `UpdateBody`; the bespoke throw guard stays inside it; the levers default to today's behaviour.
- **Why:** The cards driver is the busiest per-frame block in the mod that was not already on `TickGuard`, and `VRCard.Update` runs per card (a full hand plus a pile browser is twenty-odd instances) — these two steps are what let the `[Perf] STEPS` line say whether a head-turn spike lives in the cards or elsewhere. Measuring must not change exception semantics, and no lever may change behaviour silently.
- **Established by:** `ceef661` feat(perf): measure the mod's own frame cost, and remove the work that was pure ceremony
- **Breaks if:** Inlining `UpdateBody` back into `Update`, or defaulting a perf lever to a non-zero value.
- **Confidence:** high

### String building sits behind the change gate, not in front of it
- **Where:** `CardsDriver` fan-state / action-selection diagnostics; the `NoteFanOcclusion` callers
- **Rule:** Interpolate the message *after* deciding to log it — and note that reading `Object.name` allocates.
- **Why:** Measured: two `StringBuilder`s and three strings every frame of the whole card-selection phase, in front of the change gate, for a line printed a dozen times a scenario. Log **volume** was measured at 3.2 lines/s and was not the problem; the string building for lines then thrown away was.
- **Established by:** `ceef661`
- **Breaks if:** Re-hoisting an interpolated message above its gate for readability.
- **Confidence:** high

---

## Suspected vestigial

> Flagged, **not** removed. Each of these looks like code from a fix whose cause was
> later removed elsewhere. Every one still has to clear Charter §5 (Harmony surface,
> Unity messages, reflection, config keys, log grep tokens, debug menu) before Phase 2
> can call it dead — and several are deliberately-retained escape hatches rather than
> accidents.

### `PlayTray.GenericClusterButtonSize`
- **Status:** Defined, referenced only from two comments. Genuinely unreferenced code.
- **Reasoning:** `fc34ae9` deliberately stopped routing cap size through it, because it shrank every cap once a third cluster member joined. The method is left in place and the commit message calls it out. It is a Tier-0 removal candidate — **but** the comments that reference it are the record of *why* it must not be re-wired, so if the method goes, that explanation must stay.

### `BurnCardFx.SpawnConsumedPlume` and `ItemsPile.ItemChip._plume`
- **Status:** `SpawnConsumedPlume` has no remaining call site; `_plume` is never assigned, only null-checked and destroyed in `OnDisable`.
- **Reasoning:** `aa63e87` removed both spawn sites ("no clamp bounded it either"), and `b9e48b5` then routed the consumed look through the hosted card's own clamped effects instead. The `ItemsPile` comment calls the destroy path "the safety net". Removable as a pair, but only as a pair — and the "stays removed" comment must survive, because re-adding the plume is a documented temptation.

### `CardsConfig` binds with no reader
- **CORRECTED (phase 4):** the old list was wrong in both directions. `InspectForward`, `InspectUp`, `RevealPreset` and `RevealDemeo` **do not exist** — zero occurrences anywhere in `src/`; they survive only in `.planning/research/DEMEO-HANDS-CARDS.md` and in this registry, so a third of the entries this document asked the user to decide about were fictional. Two real ones were missing: `TrayTilt` and `RoundButtonThickness`. `FanArcDegrees` is a seventh.
- **Status (verified at HEAD):** exactly seven `[Cards]` entries are bound and read by nothing — `HeldTiltDegrees`, `RoundButtonDiameter`, `RoundButtonThickness`, `RestButtonInsetX`, `ConfirmUndoInsetX`, `TrayTilt`, `FanArcDegrees`. Each is declared and bound in `CardsConfig.cs` and referenced from nowhere else in `src/`.
- **Resolution (phase 4, user-approved):** **kept bound**, description prefixed `LEGACY — no effect, superseded by <X>.` Successors, each verified against the source: `HeldTiltDegrees` → `HeldFaceBias` (`6db51a2`, "now legacy, kept bound so existing cfg files load"); `RoundButtonDiameter` → `RestButtonDiameter_{board}` (read by `RestControls`); `RoundButtonThickness` → `ButtonTuning.RestCapDepth`; `RestButtonInsetX` → `RestButtonOffset_{board}`; `ConfirmUndoInsetX` → `ConfirmUndoOffset_{board}`; `TrayTilt` → `BoardTilt_{board}`; `FanArcDegrees` → `FanArcSweepDegrees` (seeded to the old 70 × 1.3 = 91). The per-board successors arrived with `17862bb` ("remain bound for back-compat"). `RoundButtonThickness`'s successor is not a `[Cards]` key at all — it is `[RestButtons] Depth`, reached through `WorldUI.ButtonTuning.RestCapDepth`.
- **Why not unbind:** **Charter §5 explicitly protects config entries**, `CardsConfig`'s own `DebugMenu` note documents the consequence (an unbound key is silently dropped from the user's file), and the superseding commits promised in writing to keep them loadable. Removing them would reverse a deliberate promise. Re-labelling costs one line each and turns a misleading knob into an honest one. `RestButtonInsetX` was the worst case: its description opened `LIVE FIT KNOB (dial in dev.gloomhavenvr.cards.cfg without a rebuild)` and walked the user through tuning a value nothing reads.
- **Guard note:** unlike every other doc fix, this one **is** visible to the guard — `Bind` descriptions are string literal *arguments* and do survive into the assembly.

### `PlayTray.BoardButton` dwell machinery
- **Status:** `DwellHoldRange`, `_dwellHand`, `TickDwell` and the charge tint exist but the dwell duration is 0 everywhere.
- **Reasoning:** `239acb5` removed the dwell **by user directive** while explicitly keeping the `ActivationGuard` accident window. This is a *configured-off feature*, not dead code — and it is also a documented reversal, so deleting it discards the record. Flagged so nobody removes it as unused **and** so nobody re-enables it as "clearly intended".

### `PlayTray.SeatOnBoardFace` / `ReseatProud` — REMOVED (this entry was wrong)
- **CORRECTED (phase 4):** this entry claimed *"They still have callers (the bundle Confirm/Undo and rest anchors …), so this is **not** dead."* That was false at HEAD. `ReseatProud` had **zero** callers; `SeatOnBoardFace`'s only caller was `ReseatProud`; `SeatStandoff`/`SeatProud` were used only inside `SeatOnBoardFace`. The bundle Confirm/Undo anchors come from `FindDeep` in `EnsureBuilt` and are used as-is, the rest anchors are built by `RestControls`, and the gear/pin/readout go through `NewAnchor`, which seats at the fixed `FixedProudZ` and says so. The entry was written from the surrounding prose rather than the call graph — precisely the trap this registry warns about, committed by the registry.
- **Status:** removed as Tier-0 dead code by Batch D of this refactor (~85 lines). The *reason* survives in §6 "Raycast auto-seating is GONE; fixed proud Z replaces it".

### `CardFace.LogFaceTextureDiag`
- **Status:** A one-shot diagnostic added to prove the game's card atlases ship mipless.
- **Reasoning:** That question was answered (`8454e88`) and acted on (`6c90126` built `CardFaceMipBake`), so the diagnostic's original purpose is spent. It is cheap and one-shot, and it still reports the *live* mip/aniso/filter state of the atlases the adopted canvases sample, which is the fastest way to tell whether the bake ran. Low-confidence vestigial; more likely still useful.

### `PalmGate.UseDevicePalmNormal` (cross-subsystem)
- **Status:** `6b5c38c` states outright: "UseDevicePalmNormal is now vestigial."
- **Reasoning:** Roll gate v4 measures on the visual hand frame, so the device-normal path has no consumer. It lives in `Hands/`, not `Cards/`, but it is *driven* from `CardsDriver.UpdatePalmGate`, so it belongs on the Cards census. Hand it to the Hands subsystem review rather than removing it from here.

### `CardGlow`'s removed pulse note
- **Status:** A `// NOTE (removed)` comment with no code under it.
- **Reasoning:** **Not vestigial — load-bearing documentation.** It records that the pulsing glow *quad* was removed because the user rejected that flat look, and that the board slot pulse (`PlayTray.SlotPulse`) is where the maths went. Same category as the `SyncPinHolder` comment-without-code: deleting it invites the exact re-addition it forbids. Listed here only so it is not mistaken for a dead comment during a cleanup pass.

---

## Appendix — reversals worth knowing about

These are cases where a commit in the history is **not** the current law. A reader
who greps the log and finds only the first commit will draw the wrong conclusion.

| Superseded by | What was tried | Why it is not the current law |
|---|---|---|
| `4b7ac8a` reverts `3b86e72` | Thinning `VRCard`'s grab collider from 0.02 to a 0.003 "exact fit" (the "41 cm slab" analysis) | It made docked cards nearly unhittable and was aimed at the wrong object. The real cause was the beam clamp (`4330504`). **`VRCard.Build`'s `_box.size.z = 0.02f` is deliberately NOT the visible card thickness.** |
| `807c2fa` reverts `3f91e79` | Flipping the board anchor normal to `−nF` | Chasing a stale in-game report; an offscreen render proved `+nF` is correct. |
| `99d8c9b` reverts part of `cd42d15` | Floating the discard/burnt pile **stacks** above the board | The user did not want it. Only the pile **browse fan** floats. |
| `4ef1d76` reverts the first attempt at `9e78031` | Card + held-figure render-on-top via ZTest Always | Destroyed card text and depth-correct figures. The shipped fix raises the render **queue** while keeping ZTest LEqual. |
| `b9e48b5` corrects `aa63e87` | Nulling the hosted card's `cardEffects` to kill the green fog | It threw the whole on-card "verbraucht" look away with it. The shipped fix clamps only `fx_Smoke`. |
| `f6d9725` corrects `fb2e6e3` | Re-asserting the pin holder's scale from the live rig each frame | The drift it guarded against is structurally impossible, and the re-assert *was* the reported "world zoom resizes the pinned board" bug. |
| `5e79abe` supersedes `9e2b53d` | A moving bow apex, re-normalised by the longer side | Produced a felt threshold at the fan centre and pushed mid-hand cards further back than the neutral shape. Replaced by multiplicative relief on a fixed resting bow. |
| `6b5c38c` supersedes `d7ec01c` supersedes `bb3502c` | Three earlier reveal-gate measures | Each degenerated at a hand pose the previous one had not been tested in. |
| `239acb5` supersedes `c78ce61` | Poke dwell on CONFIRM/UNDO/SET | Removed by user directive; the accident window was kept. |
| `eef8eb0` / `646fc5f` correct `a840692` | Blocking card input on `WindowModalActive` | Froze cards under the reachable pause menu family, including lingering closed-menu floats. |
