# Closing the 1:1 residue — design

> Follows `AUDIT-1TO1-2026-08.md`, and **corrects one of its findings**. Written 2026-08-27 after
> the user ruled: "Befund 2 muss auf jeden Fall geschlossen werden … prüfe ob nicht Metadaten
> ausreichen … Eventuell kann man hier subroutines vom Spiel selber nutzen … auch wie das Bild auf
> einem mouseover oder klick reagiert soll den anderen Spielern genauso dargestellt werden."

## 0. The user's instinct is right, and the mod already proves it three times over

**No picture has ever needed to travel.** Every peer owns the same game, the same prefabs, the same
art bundles and the same localisation table. Three shipped classes already exploit exactly that:

| class | what travels | what the receiver does |
|---|---|---|
| `RemoteWidgetMirror` | nothing but the model state | `Object.Instantiate`s the game's OWN live panel and runs it as a **puppet** — the clone is built under an inactive host so no `Awake` fires, so it registers with no singleton and mutates nothing |
| `RemoteUseBarSymbols` | **zero bytes** | resolves the game's own slot icon on the receiving machine via `UIInfoTools.GetItemConfig(...).miniIcon` |
| `RemoteCardFx` | **one event byte** | plays the whole card animation locally between two named pieces of that player's furniture |

So the design question is never "how do we send the image". It is only ever: **what is the smallest
metadata that lets the receiver's own game produce the same picture, and where is the seam that
lets us drive the game's widget without waking it up.**

## 1. Correction to the audit: item decisions were already closed

`AUDIT-1TO1-2026-08.md` Finding 2 attributed "Gegenstands-Entscheidungen nicht dargestellt" to the
`DecisionRoleUnknown` fallback. **That attribution was wrong.** The reported defect —

> "alle anderen Dinge wie entscheidungen wegen Gegenständen etc. sieht man nur eine box. Ich will
> 1:1 genau das sehen wie der Mitspieler — das gleiche Symbol vom Spiel, in genau der gleichen
> Größe und Position." (2026-08-13)

— was a mirrored **use-bar slot**, which was "a flat coloured quad by design", and it is retired by
`RemoteUseBarSymbols` with zero wire bytes. The ModBuild-137 logs identify it rather than infer it:
the receiver wrote `Remote use bars: 1 mirrored bar row(s), 1 slot tile(s)` while record 12
published nothing outside the take-damage prompt.

**What genuinely remains under `DecisionRoleUnknown` is two prompts, and only two:**
the short-rest `YesNoDialog` and `DialogPopup`'s option buttons.

## 2. The residue, and why each is now cheap

### 2.1 Short-rest `YesNoDialog` — a localisation key and nothing else

`ShortRest.cs:96` instantiates it from **`dialogPrefab`**, so the prefab is on every client. And the
populating call is:

```csharp
public void Init(string descriptionKey, bool autoClose, UnityAction yesAction, UnityAction noAction = null)
```

**`descriptionKey`, not text.** The wire carries a key; the receiver's own game localises it — so a
German client and an English client each read their own language, which is *more* correct than
shipping the owner's string, and it costs a few bytes.

The game also ships the subroutine the user guessed at:

```csharp
public void PrepareInteractabilityForPlayer(CPlayerActor playerContext)
```

That is what decides which options are enabled for a given player. Called on the puppet with the
**owner's** actor, the mirrored dialog greys exactly what the owner's greys — with no per-button
wire field.

**Shape:** instantiate `dialogPrefab` under the remote board's decision drawer, `Init` it with the
wired key, `PrepareInteractabilityForPlayer(ownerActor)`, then neutralise it as a puppet exactly as
`RemoteWidgetMirror` does. Wire cost: one loc key + one actor id.

### 2.1a CORRECTION — where the prefab actually comes from (checked 2026-08-27)

The paragraph above said "the prefab is on every client", and that is true but not sufficient. The
reference is not.

`dialogPrefab` is a **private serialized field on the `ShortRest` component**, and `ShortRest`
itself is instantiated by **`CardsHandUI`** (`CardsHandUI.cs:1809`,
`Instantiate(shortRestPrefab, abilityCardsHolder)`). `CardsHandUI` is **the local player's own
hand**. So a receiver has no `ShortRest` for a FOREIGN player — which is exactly what
`NetProtocol`'s original note meant by "the short-rest `YesNoDialog` belongs to a HAND", and the
note was right.

**What that does and does not change:**

- The receiver *does* eventually own a `ShortRest` — **its own**, created lazily the first time its
  own hand needs one. `dialogPrefab` is reachable off that instance by reflection, so a mirrored
  dialog can still be built without a single pixel on the wire.
- But it is an **availability race**: a peer who has not yet opened their own short-rest has no
  `ShortRest`, therefore no prefab reference, therefore nothing to instantiate. The mirror needs a
  defined behaviour for that window, and the only honest one is the existing record-12 plate —
  i.e. the fallback is not removed, it is *narrowed* to "until this client has seen its own
  short-rest once".
- `PrepareInteractabilityForPlayer(CPlayerActor)` is unaffected: the owner's actor is replicated,
  so the receiver can pass it.

**Revised cost.** This is no longer "one loc key and one actor id". It is that PLUS a reflection
handle with a documented failure path, a lazily-satisfied prefab dependency, and a fallback window
that has to be reasoned about rather than assumed away. It is still the right design and still
sends no picture — but it is a feature with three failure modes, not a one-bit change like the card
dust was.

**Recommended order change:** do §2.3 (hover/press) BEFORE this one. It generalises a pattern that
is already shipping (record 16), touches no game prefab and has no availability race, so it buys
more of the user's stated requirement — "auch wie das Bild auf einem mouseover oder klick reagiert"
— per unit of risk than the short-rest dialog does.

### 2.2 `DialogPopup` options — the option array is the metadata

```csharp
public void Show(string descriptionString, string captionString, DialogOption[] options,
                 bool allowHide = false, int cancelOption = -1)
```

Here the description IS a string rather than a key, so it rides record 12's existing text path. The
option array is a small list of labels plus an enabled flag — the same shape record 12 already
carries for the take-damage buttons.

**The one trap, and it is the reason this must not simply call `Show`:** `DialogPopup` is a
`MonoBehaviour` with a `UIWindow` and `IEscapable`. Calling its real `Show` on a peer would open a
real modal **on that peer's own screen**. The mirror must populate the *visual* subtree and never
the window — which is precisely why the puppet discipline exists, and why the clone is built under
an inactive host before anything is written into it.

### 2.3 Hover and press — the pattern is already shipped, for the initiative track

The user's requirement that "wie das Bild auf einem mouseover oder klick reagiert" must also be 1:1
is already met once, and the wire for it exists: **extension record 16 carries track hover**, and
`RemoteInitiativeTrack` *strips the local player's own hover from the clone* so that each board
shows its owner's hover and never the viewer's.

That is the whole pattern, and it generalises to the decision widgets — but **not unchanged**, and
checking that before implementing changed the shape of the work in both directions.

### 2.3a HALF OF THE REQUIREMENT IS ALREADY MET, BY CONSTRUCTION (checked 2026-08-27)

"A peer's pointer must never light up someone else's board" needs no work at all.
`RemoteWidgetMirror`'s puppet pass walks the clone three times and **`DestroyImmediate`s every
component that is not a `Transform` and not on the presentation whitelist**, then sweeps colliders
and rigidbodies as belt and braces. `Button`, `Selectable`, `EventTrigger` and `GraphicRaycaster`
are all gone before the clone is ever shown.

So the mirrored decision row is inert by the same discipline that stops the clone's
MonoBehaviours registering with singletons. The viewer-hover leak that `RemoteInitiativeTrack` had
to strip by hand does not exist here — and the reason it existed THERE is worth keeping in view:
that mirror is a per-frame faithful copy of a widget the receiving client is itself using.

### 2.3b WHAT REMAINS IS HARDER THAN "ONE INDEX", AND FOR THE SAME REASON

The owner's hover is not carried, so a peer sees a neutral row while the owner's finger is on an
option. Carrying the index is the easy half; **applying it is not**, and the puppet pass is why:

> the `Selectable` that WOULD have drawn the hover state has been destroyed.

A mirrored highlight therefore cannot be "set the button's state" — there is no button. It has to
be re-drawn from the game's own values: the `Selectable`'s `ColorBlock` (or its sprite swap) read
off the SOURCE widget before the strip, cached, and applied to the mirrored option's `Graphic` by
this mod. That is the same shape as `RemoteUseBarSymbols` — resolve the game's own value locally,
send nothing — but it is a new capture step inside the mirror, not a field on a record.

**Revised cost for §2.3:** one small index on the decision record, plus a colour capture in
`RemoteWidgetMirror` (read the source `Selectable`'s normal/highlighted colours before the puppet
pass destroys it), plus per-option application on the receiver. No picture travels; the widths and
positions are already right. It is one coherent block, and it is bigger than the card dust was.

### 2.3c SHIPPED — ModBuild 300, and two things the estimate above got wrong

Built and gated 2026-08-27. What actually landed, and where the paragraph above was off:

**The colour capture already existed, and cost two fields rather than a new step.**
`RemoteDecisionWidgets.BindRole` was ALREADY reading the source widget's `ColorBlock` before the
puppet pass — `NormalTint`, `DisabledTint` and `colorMultiplier`, because the greyed look was
already being drawn from the game's own numbers rather than an invented factor. So the work was
`highlightedColor` and `pressedColor` beside them, and the paint became
`disabled > pressed > highlighted > normal` — `Selectable.DoStateTransition`'s own precedence, not
one chosen here. The §2.3b reading was right about WHY it is hard (there is no `Selectable` left on
the clone) and wrong about the size, because it did not check whether the same problem had already
been solved once next door.

**It is not "one index" — it is two bits, and they cost nothing.** Record 24 already sends one state
byte per option and bits 3..4 were free, so the record's LENGTH did not move. An older peer masks
them straight back off and renders exactly what it rendered before.

**What the estimate missed entirely: BOTH cadences had to give.**

- *Sender.* The decision records publish every 0.25 s. That is the right rate for a toggle and the
  wrong one for a beam — a hover sampled at 4 Hz reaches a peer as a stutter that lands on options
  the owner never stopped on. A moved pointer bit now clears the publish gate and rides out on that
  tick, the same bypass the focus-hidden withdrawal already used. The test for it is a *question*
  and never a second sampler: eight cached widgets, two dictionary probes each, no walk, no string,
  and it writes nothing — `SampleOptionState` stays the only writer of a state byte.
- *Receiver.* `RemoteBoardFurniture.Refresh` also runs on the 4 Hz content pass, so publishing fast
  would have bought nothing on its own. `RemoteDecisionWidgets.TickPointer` now runs per frame,
  gated on a folded 16-bit key, and calls the SAME `Apply` — so there is no second description of
  the row that could drift. The precedent is two dozen lines away in the same class: the half-card
  hover of record 14 is driven per frame "so the glow lands with the synced edge, not on the 4 Hz
  content cadence".

**The source of the two bits is the mod's own pointer tables, and one of them was built for this
row.** `UnityEngine.UI` is not publicized, so `Selectable`'s hover latch cannot be read. It does not
need to be: `UguiHoverTracker` — whose class doc names the defect it was born from, *"user
'Hover-Animation der Entscheidungsknöpfe'"* — already refcounts enter/exit for all four VR pointers
and IS what drives the highlight the owner sees. It gained a read seam; the press got its twin,
`UguiPressTracker`, behind a SINGLE WRITER (`UguiPointer.Pressing`) so four assignment sites and one
refcount cannot desync.

**One defect found on the way.** The receiver's paint key packed each option into seven bits; a
five-bit state made adjacent options overlap, so two different rows could have hashed alike. Only
the log line rode that key — but a change gate that silently stops detecting changes is this
project's oldest wound. It is a rolling hash now.

**Known gap, deliberate.** The mod-drawn PLATE row still shows no hover. That row exists precisely
when the receiver could NOT resolve the owner's widget — so there is no `ColorBlock` to read and any
factor there would be invented. The means to close it honestly is on the shelf and named:
`NativeButtonSkin` already samples `pressedColor`/`disabledColor` off the game's own button into
`_pressedMul`/`_disabledMul`; a `_highlightedMul` beside them would give the plate a real hover
factor from the same source. It was not done in this build because it widens a heavily-tuned
button-styling path for a fallback row, and that is a separate decision.

**Not verified on hardware.** Two clients, a take-damage prompt on one: the other board's decision
row must highlight the option the OWNER's beam is on, in the game's own highlight colour, and darken
while they hold the trigger — and the watching player's own beam must do nothing to it.

## 3. Finding 3 re-checked: the seven animation debts are two different problems

| debt | what is actually missing | cost to close |
|---|---|---|
| `[Cards] CardDust` | **a call site, not a wire field.** `CardDustFx` is a mod-side static gated on the config; the emitter is not tied to `VRCard`. On a peer the mirrored slabs are `RemoteCardArt`/`RemoteHandFan` objects and nothing calls `EmitCardDust` at their pose. | one call at the mirrored card + **one bit** for the owner's toggle |
| `[Cards] GameCardParticles` | same shape | same |
| `[ButtonAnim] Enable`, `AppearSeconds`, `DisappearSeconds`, `AppearParticles` | **a renderer debt.** The mirrored cap's crumble/assemble clock is a pair of *static consts* on `RemoteBoardFurniture` shared by every peer's board — not per-peer state. There is no per-cap fade to drive. | thread the clock through the cap instances FIRST; then four fields |
| `[Cards] FanCloseDuration` | **a renderer debt, and the checker says so.** Field id 156 is declared and deliberately NOT sampled, because `RemoteHandFan` has no collapse animation at all — it hides the fan outright. | grow the collapse first |

**The conclusion for point 3 is a warning about order.** Five of the seven cannot be closed by wiring
anything: wire them first and the checker turns green while the picture stays identical — the exact
failure `check-wire-coverage.py`'s own `FanCloseDuration` note was written to prevent. **The renderer
comes first, the field second.** The two that ARE closable now — the card dust and the card smoke —
are the cheapest items in the whole residue: one call site and one bit each.

## 4. What this is, and what it is not

This is a **feature build**, not a hygiene edit: it adds wire fields, instantiates game prefabs on a
receiver and needs a hardware round with two clients to confirm. It does not belong inside the
refactor, and nothing here has been implemented yet.

Suggested order, cheapest and most certain first:

1. **Card dust + card smoke** — SHIPPED, ModBuild 299 (dust). The smoke was wired and then
   REVERTED on purpose: it is the game's own particle system and a peer's mirrored cards are mod
   slabs with none, so the field would have had no consumer.
2. **Short-rest `YesNoDialog`** — one loc key and one actor id, and the game's own
   `PrepareInteractabilityForPlayer` does the state.
3. **Hover/press on the decision widgets** — SHIPPED, ModBuild 300 (see §2.3c). Not record 16's
   pattern in the end: two free bits on record 24, plus a cadence bypass at both ends.
4. **`DialogPopup` options** — the largest, because the puppet must be populated without ever
   touching the `UIWindow`.
5. **The cap animation clock, then its four fields** — renderer first.
