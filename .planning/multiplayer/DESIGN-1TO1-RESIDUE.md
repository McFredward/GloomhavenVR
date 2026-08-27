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

That is the whole pattern, and it generalises to the decision widgets unchanged: one small hover/press
index per prompt, and the puppet must be built with its own `EventSystem` reactions dead so a peer's
pointer can never light it up. **Stripping the viewer's own hover is as load-bearing as carrying the
owner's** — without it a peer's board would react to the peer, which is the opposite of 1:1.

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

1. **Card dust + card smoke** — one call site and one bit each, no new record, no prefab work.
2. **Short-rest `YesNoDialog`** — one loc key and one actor id, and the game's own
   `PrepareInteractabilityForPlayer` does the state.
3. **Hover/press on the decision widgets** — record 16's pattern, generalised.
4. **`DialogPopup` options** — the largest, because the puppet must be populated without ever
   touching the `UIWindow`.
5. **The cap animation clock, then its four fields** — renderer first.
