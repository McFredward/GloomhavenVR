# The attack modifier deck as a physical deck — findings, and the one thing that blocks it

**Status: PARKED at the user's word (2026-08-30), after a feasibility pass. No code was written.**
Everything below was read out of `decompiled/`; nothing was measured on hardware, and nothing here
has been tried.

**Why it was looked at:** it is the one ritual that *is* Gloomhaven — you draw a card and for half a
second you do not know whether it is a Null or a ×2. The digital edition plays that as an
animation. In VR it could be a stack you reach for.

---

## 1. The rules model is complete, authoritative, and readable

`CMonsterAttackModifierDeck` (`decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary/`) is a real deck:

| Member | What it gives us |
|---|---|
| `AttackModifierCards` | the draw pile, in order |
| `DiscardedAttackModifierCards` | the discard pile |
| `AttackModifierCardsPool` | the full pool the deck was built from |
| `LastDrawnAttackModifierCards` | **what was just drawn** — the read-only hook a presentation needs |
| `DrawAttackModifierCards(actor, attackStrength, advStatus, out notUsed)` | the draw itself, advantage/disadvantage included (it returns a LIST) |
| `CheckAttackModifierCardShuffle(force)` | the reshuffle |
| `ResetAttackModifiers` / `LoadAttackModifierDeck(deckState)` | save/load |

There is one deck per side — `MonsterClassManager.EnemyMonsterAttackModifierDeck`, `Boss…`,
`Allied…`, `Enemy2…`, `Neutral…` (`ActorStatPanel.cs:660-686`) — and each character has their own
through `CCharacterClass` (`ModifiersDisplayController.Display(CCharacterClass, bool)`).

Per card, `AttackModifierYMLData` carries `Name`, `MathModifier` (`"+1"`, `"x2"`, `"Null"`),
`Shuffle`, and a `Card` with `Infuse` / `InfuseElements` / `NegativeConditions`. So the *rules* of
every card, including blesses, curses and perk cards, are fully in hand.

**The hard constraint that follows:** the rules belong to the game. The mod may not patch
`ScenarioRuleLibrary` and may not write game state from presentation code. A physical deck is
therefore a **presentation of a draw the game performs**, never a replacement for it. That is not a
compromise — at the real table the card is already determined by the shuffle; you just have not
turned it over yet.

**Not yet established:** *where* `DrawAttackModifierCards` is called from, and when the 2D display
appears relative to it. That decides whether the physical draw happens before or after resolution,
which is the question that decides how it feels. It is the first thing to look up if this is
revived.

---

## 2. The art is the blocker, and it is not a technical one

### What the game has

| Source | What it returns |
|---|---|
| `UIInfoTools.GetAttackModifierSprite(mathModifier)` | one sprite per modifier, looked up by the string (`"*"` → `"x"`) |
| `UIInfoTools.GetAttackModifierIcon(modifier, useOriginal)` | the EFFECT icon: a custom perk icon, a generic one, an element-infusion icon, or a negative-condition icon |
| `UIInfoTools.NullModifierIcon` | the Null symbol |
| `AttackModifier.Init(modifier, isAvailable)` | composes a display from `mainImage` + `additionalImage` + `numberText` |
| `AttackModifierCardGUI` | a level-editor widget: a `Text` and a `Toggle`. Nothing more. |

### What that means

**The digital edition has SYMBOLS, not cards.** If whole card faces existed, `AttackModifier` would
not be composing one from three parts. The user said this from memory and the code agrees.

### The user's requirement, and why it closes the cheap path

> *"Bei den Modifikator-Karten geht es mir um die Original-Karten mit der Kunst darauf … Soll das
> Feature aber immersiver umgesetzt werden sind die echten Karten eine Grundvoraussetzung, so wie
> das Spiel die anderen Karten auch mit der Original-Kunst darstellt."*

He is right, and it is a design argument rather than a technical one: every *other* card in this
mod is the game's own licensed artwork. A modifier card assembled by us out of a symbol and a frame
would be the one hand-made object in a room full of real ones, and it would show.

So **composing the faces is off the table**, and with it the only route that uses assets we have.

### What cannot be done

The original cards are Cephalofair's copyrighted artwork. Extracting them from the physical game or
from another product and shipping them inside `gloomhavenvr.bundle` would be distributing someone
else's work. That is not a route this project takes — the controller models were shipped precisely
*because* their licence permits it (MIT), and the same standard applies here in the other direction.

### The route that does work

**Ship the feature, not the art.** A defined contract for a player-supplied folder:

```
BepInEx/plugins/GloomhavenVR/ModifierCards/
    plus0.png  plus1.png  plus2.png  minus1.png  minus2.png
    x2.png     null.png   bless.png  curse.png   <perk-name>.png
    back.png
    manifest.json      (optional: explicit MathModifier/Name -> file mapping)
```

* Folder present → those images are the card faces.
* Folder absent → **the feature does not draw a card at all**; the modifier stays exactly as the
  game shows it today. Deliberately **no** composed fallback, because a fallback we built is the
  thing that was rejected.

The mod distributes nothing, each player uses their own material, and the feature is complete for
whoever has it. This is the standard shape for exactly this situation.

The other option is to ask Cephalofair for permission to ship the art. That is the user's call.

---

## 3. Sketched user stories (from the same pass, unbuilt)

1. **The normal draw.** The attack resolves and the game draws. Instead of an overlay, your own
   stack sits beside the control board; the top card lifts, you take it and turn it over. Only in
   your hand is it readable. Releasing it puts it on a discard pile that visibly grows.
2. **Advantage / disadvantage.** The game draws two. Both come up, side by side; you see which one
   counts *and* the one that did not — the moment the 2D version swallows.
3. **The shuffle symbol.** A drawn card carrying the shuffler stays up a beat longer while the
   discard pile visibly travels back under the draw pile. You *see* that it reshuffled.
4. **Reading the deck.** Point the laser at the discard pile and it fans out like your card hand —
   `DiscardedAttackModifierCards` is already the list, and the pile-fan machinery already exists.
5. **Bless and curse.** A bless is a glowing card that visibly flies into the stack; a curse the
   same in another colour. You keep a feel for what is in there.
6. **The monster deck.** A second stack on the enemy side of the board, same gesture — except you
   do not draw it, you watch it.
7. **Multiplayer.** The owner's draw is visible to everyone: same card, same moment, same place,
   per the 1:1 ruling. This is the only genuinely new wire content, and
   `LastDrawnAttackModifierCards` suggests it is a card identity plus a timestamp.

---

## 4. If this is revived, in this order

1. Find the call site of `DrawAttackModifierCards` and the 2D display's timing relative to it.
2. Settle the art question (player folder, or permission). Without art, stop here.
3. Design the wire record for the peer-visible draw, before writing any presentation code —
   multiplayer is a first-class constraint, not a retrofit.
