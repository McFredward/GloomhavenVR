# R2 — 1:1 review of the whole remote board

Base: `origin/dev` @ `9f646c86`, ModBuild 479. Read-only lane; nothing under `src/` was touched.

Ground truth for game behaviour: `decompiled/GH.Runtime/`. **Note for future lanes: that tree is
gitignored and therefore absent from every worktree** — it exists only in the main checkout at
`/home/claw/gloomhaven_vr/decompiled/`. Every decompiled citation below was read there.

## How the elements were enumerated

Not from the previous audit. Three independent passes, intersected:

1. **The wire.** Every `public const byte ExtId*` in `Net/NetProtocol.cs` — 42 records, ids 1..45
   with 38, 40 and 42 unallocated and undocumented. Each record names the surface it drives.
2. **The renderers.** All 46 files under `Net/Remote/`, plus `Net/Board/`, `Net/Avatar/`,
   `Net/PresenceState.cs`, `Net/NetPacket.cs`, `Net/RevealGate.cs`, `Net/PeerCardFaceCensus.cs`.
   Every `RemoteX` class draws one or more named surfaces of a peer's board; the surfaces are
   tabulated in §"The element table".
3. **The dial census.** Every live `ConfigEntry.Value` read reachable from a mirror-side draw path,
   direct and one call deep through the mod's own helpers, plus every material/shader recipe used
   on a mirrored surface. This pass found most of the defects, and **it is the pass no shipped
   checker performs.**

Every claim below was re-derived. `AUDIT-1TO1-2026-08.md` and `DESIGN-1TO1-RESIDUE.md` were read
first and then treated as hypotheses; where they are stale it is said so.

---

## THE HEADLINE: ALL THREE CHECKERS ARE GREEN

Run today at `9f646c86`:

```
remote defaults: 95 frozen constants / wire-overridable fallbacks resolve to the same Defaults entries
wire coverage:   194 board dial(s) on extension record 28, 80 exempt (0 of them PENDING debts)
mirrors:         41 mirrored-constant groups agree; 7 shared-expression groups have one implementation each
```

**Most of the confirmed findings below are invisible to all three, by construction.**

- `check-remote-defaults.py` compares a **frozen constant** against the `Defaults` entry it copies.
- `check-wire-coverage.py` asks **"is this dial on the wire"**.
- `check-mirrors.sh` compares **declared constant groups** and shared expressions.

**None of them asks the question that actually decides 1:1: at runtime, does the mirror read the
OWNER's copy of this value or the VIEWER's?** A live `ConfigEntry.Value` read on the mirror side is
not a frozen constant, so `check-remote-defaults.py` has no shape in which to express it — F2 and
F3 below are exactly that. A shader choice (F7) and a fit *rule* (F8, F9) are outside all three.

### What `check-remote-defaults.py` covers, and what it does not

It is a **hand-maintained list of 95 pairs** (`("Net/Remote/X.cs", "member", "Section", "Key")`),
each asserting that a remote-side constant or seeded field names the same `Defaults` entry as the
local `Bind`. It accepts both the frozen `const float X = Defaults.Y` and the wire-overridable
`private float _x = Defaults.Y` forms, and it verifies the *entry*, not just the number.

Its header closes with: *"What is genuinely no longer covered: **nothing**. A default change still
has to be made in one place."* **That sentence is false**, and the script contradicts it twice in
its own body:

- **Products of two defaults are unpinnable.** `scripts/check-remote-defaults.py:244-247` —
  *"`RemoteItemFan._radius` / `RemoteBrowserFan._radius` are deliberately NOT listed: they are
  PRODUCTS … this checker verifies a constant IS one named `Defaults` entry."*
  `RemoteMapRoom.FallbackScaleFactor` cites the same limitation (`RemoteMapRoom.cs:1100-1102`),
  and `RemoteBoardTooltip.DefaultMetersPerUiPixel = 0.0005f` is a third unpinned product
  (`Defaults.CanvasScaleMm 1.0 × 0.001 × 0.5`).
- **Anything not on the list is not covered at all.** There is no sweep; 95 lines is the whole
  scope.

And the third gap it does not mention: **it cannot see which side's dial a runtime read comes
from.** That is the gap F2 and F3 fall through, and it is the one worth closing.

### The recommendation, in one line

The missing guard is not another constant list. It is a lint that flags **any live
`ConfigEntry<T>.Value` read reachable from `Net/Remote/`** and requires the call site to name whose
dial it means — the shape the project has already invented twice and applied correctly twice:
`Cards.CardDustFx.Permission` and `WorldUI.NativeButtonSkin.LabelOwner`. Both of those seams were
verified fully taken (every call site passes the owner arm). A third dial needed one and has not
got one; that is F2.

---

## The findings at a glance

| # | finding | verdict | needs a dial moved? |
|---|---|---|---|
| **F1** | `FxSurface` enum grew 4→6, its three latch arrays did not — the ACTIVE and HELD card looks throw and are permanently dead | CONFIRMED | no |
| **F2** | the mirrored item-fan cue beats on the VIEWER's dial, against a recorded ruling, while the sibling cue on the same board beats on the owner's | CONFIRMED | yes |
| **F3** | a peer's map placard is sized by the VIEWER's `[WorldUI] CanvasScaleMm` | CONFIRMED | yes |
| **F4** | the mirrored slot glows are alpha-blended where the owner's are ADDITIVE | CONFIRMED | no |
| **F5** | the mirrored burn HOLD paints the settled char instead of the owner's 2 s ramp | CONFIRMED | no |
| **F6** | the mirrored scenario-RULES panel gets a height correction the owner's never gets | CONFIRMED | no |
| **F7** | the mirrored board tooltip's box size and type style are both wrong | CONFIRMED | no |
| **F8** | three shared-window SPAWN-POSE dials are client-local | CONFIRMED (declared, unruled) | yes |
| **F17** | `RemoteMapStory.SendDue` latches true — extras go out at frame rate instead of 5 Hz | CONFIRMED | no |
| **F18** | the decision-label mask covers names of cards the discard-fronts ruling made public | CONFIRMED asymmetry, ruling collision | no |
| **F9** | `TryDockRect` reads the RECEIVER's own converted panel — the two canvas widths | PLAUSIBLE (one-grep falsifier) | no |
| **F10** | the mirrored decision row's seat comes from a TMP height measured on a VIEWER-language string | PLAUSIBLE | no |
| **F11** | the mirrored slot-glow pulse runs on the viewer's process clock — phase is arbitrary | CONFIRMED | no |
| **F12** | `[Optimize] RemoteContentInterval` is a VIEWER dial setting a mirror's live cadence | CONFIRMED | yes |
| **F13** | the option colour transition snaps on the viewer and cross-fades on the owner | CONFIRMED | no |
| **F14** | the element strip's mod-drawn fallback orders chips by enum, not by creation history | PLAUSIBLE | no |
| **F15** | the mirrored decision row can be antique-tinted twice | PLAUSIBLE | no |
| **F16** | the mirrored tooltip corner is measured over the MIRROR's own subtree | PLAUSIBLE | no |
| **F19** | the approved voice-badge exception has spread past SCALE into presence and animation | CONFIRMED (on/off) | no |

Findings are presented below in rank order F1..F16, then F17..F19 (which arrived from the last two
lanes and are ranked in the table above rather than renumbered).



# FINDINGS

Ranked. CONFIRMED means the mechanism was read in the code that runs and, where it concerns the
game, verified against `decompiled/GH.Runtime/` today.

## F1 — CONFIRMED — `FxSurface` grew to six members; its three latch arrays are still `new bool[4]`, so the ACTIVE and HELD card looks are permanently dead

**Where.** `src/GloomhavenVR/Net/Remote/RemoteCardArt.cs:2167-2169` (declaration); thrown at
`:3178`, `:3216`, `:3236`; entered from `:2500`. Drivers:
`src/GloomhavenVR/Net/Remote/RemoteBoardCard.cs:1032-1037` and
`src/GloomhavenVR/Net/Remote/RemoteHeldCardFace.cs:732-733`.

**Mechanism.** `FxSurface` has six members — `Unnamed=0, Recess=1, Flight=2, Pile=3, Active=4,
Held=5` (`RemoteCardArt.cs:1997-2023`). The three one-shot latches are sized four:

```csharp
private static readonly bool[] s_burnRigLogged     = new bool[4];   // :2167
private static readonly bool[] s_burnRigRefused    = new bool[4];   // :2168
private static readonly bool[] s_burnRigNoFxImages = new bool[4];   // :2169
```

and are indexed **unguarded** — `if (s_burnRigLogged[(int)surface])` at `:3178`. The fourth latch
of the same family, `s_flameDrawnLogged` (`:2801`), **is** guarded
(`s < 0 || s >= s_flameDrawnLogged.Length`, `:2843`), which is why only these three throw.

The `IndexOutOfRangeException` lands inside `BuildBurnRig`'s own `try`, **after** the state has been
committed:

```csharp
_burnRigState = BurnRig.Ready;          // :2497
_burnRigLook  = CardFxLook.Burn;        // :2498
BuildFlameQuad(all, CardFxLook.Burn);
ReportBurnRigOnce(Surface, …);          // :2500  → THROWS for Active(4) / Held(5)
…
catch (System.Exception ex)
{
    _burnImages = null;                 // :2505
    _burnTexts  = null;
    VRLog.Debug(…);                     // Debug tier — silent at the shipped level
}
```

`_burnRigState` stays `Ready`, so `BuildBurnRig` never runs again (`:2275` calls it only when
`Unbuilt`), and every subsequent write is refused by the next line:
`if (_burnRigState != BurnRig.Ready || _burnImages == null) return false;` (`:2277`).

Both drivers set the surface **before** the first paint, so the rig is *always* built under a
throwing index — `_art.Surface = FxSurface.Active;` then `SetAbilityCardFxProgress(want, 1f)`
(`RemoteBoardCard.cs:1032/1037`), and `art.Surface = FxSurface.Held;` then the same call
(`RemoteHeldCardFace.cs:732/733`).

**This is a regression of the build under review.** `git log -S` places the `Active` and `Held`
members in `c14e5220` — the ModBuild 479 round — while the `new bool[4]` arrays predate it
(`8e72856f`). The enum grew from four members to six; the arrays did not.

**Failure scenario.** A peer activates a persistent card that burns on use (any `CardPile == Lost`
persistent — a Brute or Scoundrel persistent will do).
*Owner sees:* the card in his ACTIVE column wearing the permanent burnt wash, kept across rounds —
the picture user item 4 of 2026-09-07 asked for.
*Viewer sees:* a clean, bright, unwashed card in that peer's mirrored active matrix, forever.
Then the peer picks that card up into his fist: *owner sees* the charred card in his hand,
*viewer sees* a fresh one — verbatim the symptom the 2026-09-07 evening ruling was written against
(*"Wird sie in die Hand genommen soll sie lokal und remote genau gleich angezeigt werden mit allen
gleichen FX effekten"*, quoted in the enum's own doc at `:2019-2023`).

**Second-order.** `SetAbilityCardFxProgress` calls `TakeFxLookHold()` at `:2274`, *before*
`BuildBurnRig`. Both dead surfaces therefore **take** `CardHalfTone.HoldCardFxLook` and then never
paint — the hold stands a normaliser down for a clone nobody is washing, which is exactly the
failure `HoldMirroredDim`'s own doc warns about (`RemoteBoardCard.cs:990`). The choke point itself
is correct (the hold is taken and released inside `SetAbilityCardFxProgress` / `ClearAbilityCardFx`,
`RemoteCardArt.cs:2091, 2103`); the throw defeats it.

**Reachable on hardware this round:** yes — first activated card of the first scenario, both
machines, no dial touched. **The falsifier is already shipped:**
`RemoteBoardCard.LogActiveWashIfChanged` prints `REMOTE ACTIVE WASH … (rig took the write = False)`
for every activated card.

## F2 — CONFIRMED — the mirrored item-fan cue beats on the VIEWER's dial, against a recorded ruling, while the sibling cue on the same board beats on the owner's

*Found independently by three of this review's six passes, which is why it is ranked here.*

**Where.** `src/GloomhavenVR/Net/Remote/RemoteUsableFrame.cs:122` (declaration of intent at
`:117-119`; the defending argument at `src/GloomhavenVR/Net/Remote/RemoteItemFan.cs:1910-1918`).

**Mechanism.** The mirrored "you can play this NOW" frame around a peer's usable item chip is built
with

```csharp
beatSeconds: Mathf.Max(0.2f, CardsConfig.ItemCueBeatSeconds.Value),   // :122
```

— a live read of **this viewer's** `[Cards] ItemCueBeatSeconds`. The owner drives the identical
frame from **their** copy (`Cards/Piles/ItemsPile.cs:5262`).

The owner's value is already on the wire and already decoded: `NetProtocol.TuneItemCueBeatSeconds`
= id 161 (`NetProtocol.cs:23927`), sampled at `Net/Board/BoardTuning.cs:392-393`, resolved as
`RemoteBoardTuning.ItemCueBeatSeconds` (`BoardTuning.cs:1069, 1347`). **The other half of the same
cue on the same mirrored board already consumes it** — `RemoteControlBoard.cs:3443`,
`_itemCueBeatSeconds = Mathf.Max(0.2f, tuning.ItemCueBeatSeconds)`, under a comment quoting the 1:1
ruling. And `RemoteItemFan` already latches `_owner.BoardTuning` on the revision edge
(`RemoteItemFan.cs:1518-1557`, eighteen other fields), so the owner's value is in scope at the call
site; only `RemoteUsableFrame.Build`'s signature `(Transform, float, float)` carries no tuning.

**It contradicts a ruling recorded in this tree.** `NetProtocol.cs:647`, in the ModBuild 478/479
"RULINGS TAKEN THIS ROUND" block: *"ItemCueBeatSeconds follows the OWNER on both surfaces.
`[Voice] BadgeScale` is an approved viewer-local exception."* One of the two surfaces follows the
owner; the other does not. `RemoteItemFan.cs:1910-1918` argues the opposite in prose
(*"how fast the room breathes is the room's"*) and cites no ruling.

**Failure scenario.** Maintainer sets `[Cards] ItemCueBeatSeconds = 3.0`; his peer stays at the
shipped 1.25 (`Defaults.Cards.cs:206`). Maintainer opens his item fan with two usable items.
*Owner sees:* his closed pile-stack cue and both chip frames breathing together at 3.0 s.
*Viewer sees, on that one mirrored board:* the closed pile-stack cue at 3.0 s (owner's) and the two
chip frames at 1.25 s (viewer's) — **two rhythms on one board**, which is precisely the "one cue
rendered as two" the defending comment claims to prevent, and neither rhythm is the owner's.

**Reachable on hardware this round:** yes, but only with the dial moved on exactly one machine. At
the shipped default both read 1.25 s and the breach is invisible. It is a *ruling* breach today and
a *visible* breach the moment either player tunes it.

## F3 — CONFIRMED — a peer's map placard is sized by the VIEWER's `[WorldUI] CanvasScaleMm`, in the same expression whose comment says the factor is the owner's

**Where.** `src/GloomhavenVR/Net/Remote/RemoteMapRoom.cs:1265-1266`, comment at `:1261-1264`.

**Mechanism.**

```csharp
// THE FACTOR IS THE OWNER'S DIAL (ScaleFactorFor), not this viewer's, …
float scale = WorldUI.WorldUIConfig.CanvasScaleMm.Value * 0.001f
              * WorldUI.PanelLayout.WorldScale * ScaleFactorFor(playerId);
```

`ScaleFactorFor` (`:1153-1157`) is `ModalFallback.WindowScaleFactor × TryGetPeerWindowLegibility(…)`
— correctly the owner's, under a verbatim user ruling of 2026-08-28 (*"Auch hier soll die 1:1 Regel
gelten, also die Größe des Besitzers."*, `:1113-1119`). **`CanvasScaleMm` beside it is the
viewer's**, and the comment's "not this viewer's" is true of only one of the three factors it
annotates.

The owner's value is on the wire: `NetProtocol.TuneCanvasScaleMm` = id 143 (`NetProtocol.cs:23851`),
sampled at `BoardTuning.cs:328-329`, decoded as `RemoteBoardTuning.CanvasScaleMm`
(`BoardTuning.cs:1023, 1319`). **The sibling mirror does it correctly** — `RemoteBoardTooltip.cs:179`
uses `Mathf.Max(0.01f, tuning.CanvasScaleMm)`. The accessor to add is one line beside the existing
one at `NetAvatarDriver.cs:694-702`.

The placard is unambiguously a mirror, not a shared window: its pose is `MapRoom.HoverCardPose.Place`
— *"the same code that seats the local card"* (`RemoteMapRoom.cs:80`) — and it is a picture of the
card **that peer** is reading. The owner's own card is sized through
`ModalFallback.DeriveWindowScale`, which reads the same `CanvasScaleMm.Value` on the owner's machine
(`ModalFallback.9.Spawn.cs:1767-1770`), so the two expressions differ in exactly this one term.

**And the project's own law says so.** `WorldUI/Modal/SharedWindowSizeLaw.cs:55-62`:
*"The recorded ruling [[one-to-one-beats-local-legibility]] is that a MIRROR wears the OWNER's dial:
**a peer's placard is a copy of one player's object**, so there is an owner to follow"* — and it
names `[WorldUI] WindowLegibility` and `[WorldUI] CanvasScaleMm` in one breath as the two dials of
this kind. The options tree files them as siblings too (`VROptionsTab.7.TopicTrees.cs:74-98`:
*"both answer 'how many of my headset's pixels does one authored UI pixel get'"*).

**Failure scenario.** Maintainer sets `[WorldUI] CanvasScaleMm = 1.5` ("Tafel-Maßstab (mm/px)" in
the VR panel). Peer stays at the shipped 1.0. Both are in the 3D map room; the maintainer sweeps his
beam over a location.
*Owner sees:* his own quest placard at 1.5 mm/px.
*Viewer sees:* the maintainer's placard at 1.0 mm/px — **33 % smaller than the maintainer is reading
it at**. The reverse also holds: if the *peer* raises their own dial, the peer sees the maintainer's
placard larger than the maintainer sees it, which is the leak direction the `WindowLegibility` ruling
was made to close.

**Reachable on hardware this round:** yes — map room, one hover, one dial moved on one machine.

## F4 — CONFIRMED — the mirrored slot glows are alpha-blended where the owner's are ADDITIVE; the shared glow choke point has three callers and none of them is this one

**Where.** `src/GloomhavenVR/Net/Remote/RemoteBoardFurniture.cs:3881` (built at `:1477` and `:1479`).

**Mechanism.** `BuildSlotGlow` mints its material itself:

```csharp
var mat = BoardVisual.Unlit(color);   // :3881
```

`BoardVisual.Unlit` (`Net/Board/BoardVisual.cs:356-367`) is
`Shader.Find("Sprites/Default") ?? "UI/Default"` — standard `SrcAlpha / OneMinusSrcAlpha` alpha
blending, with alpha driven per frame by `RemoteGlowPulse` (`c.a = k`, k in 0.30…0.85).

The owner's two glows do not. Both `PlayTray.4.Slots.cs:404` (the "wanted" rim) and `:346` (the snap
rim, via `CardGlow.CreateGlowQuad`) go through `CardGlow.MakeGlowMaterial`
(`Cards/Art/CardGlow.cs:23-32`), which resolves the bundled `GloomhavenVR/Overlay` shader and sets
`_SrcBlend = One`, `_DstBlend = One`, `_ZWrite = 0`, `renderQueue = Transparent` — **a true additive
emissive glow.**

The choke point exists and mirrors elsewhere already take it: `RemoteBoardCard.cs:1928/1930` uses
`CardGlow.CreateGlowQuad` for the mirrored half-card hover glows, and `CardFan.cs:2127` for the fan
insert highlight. `RemoteBoardFurniture.BuildSlotGlow` is the one glow surface that never took it —
the `CardHalfTone.HoldCardFxLook` shape, one build later.

**Failure scenario.** Owner is in the card-pick flow with one empty recess.
*Owner sees:* an additive emissive teal halo that only ever adds light to the dark board, and a
brighter gold snap rim over it.
*Viewer sees:* the same two rims as alpha-blended translucent quads. At the bottom of the breath
(k = 0.30) they are a 30 %-opacity teal wash that **darkens** the wood instead of glowing on it.
Two untuned default clients, no dial touched.

Note the failure direction of `MakeGlowMaterial` itself: it falls back to `Sprites/Default` when the
bundle shader is missing — so the mirror's look equals the owner's only on a machine where the
owner's own glow is also broken.

**Reachable on hardware this round:** yes. `SetWanted`/`SetSnap` are driven per frame from
`TickWire` off the board-UI record; a wanted glow lights on every peer board with an empty recess
during selection, and the snap rim on every recess hover.

## F5 — CONFIRMED (mechanism) — the mirrored burn HOLD paints the settled char instead of the owner's 2 s ramp, and the ramp value is read thirty lines later for a log string

**Where.** `src/GloomhavenVR/Net/Remote/RemoteBurnFx.cs:856`.

**Mechanism.** In the branch taken when no recess is drawing the card (a short-rest sacrifice; a
burn whose recess is hidden), the slab is shown and then:

```csharp
if (b.HasFace && b.Art != null && !b.Art.SetAbilityBurnProgress(1f))   // :856
    b.HasFace = false;
```

`SetAbilityBurnProgress(1f)` forwards to `SetAbilityCardFxProgress(Burn, 1f)`
(`RemoteCardArt.cs:2248`) — the **settled end state**, written on the slab's first drawn frame and
every frame after.

The owner's side is a real ramp, verified in the decompiled game today:
`CardEffects.BurnCardTimeline` sets `float burnTime = 2f` (`decompiled/GH.Runtime/CardEffects.cs:515`)
and runs `dTime += deltaTime / burnTime; … material.SetFloat(_greyOut, Mathf.Clamp01(dTime))`
(`:564-571`). **This class already has that number**: `BurnArtwork.PaintProgress(ownerFx)` is called
at `RemoteBurnFx.cs:886` purely to print it in a log string. `:905`, the ARC, is correctly `1f`.

**Three in-source comments claim the ramp exists** — `RemoteBurnFx.cs:54-57` (*"hold at their board
anchor with the burn ramping on the real face"*), `:709-712` (*"it lies on their board and chars,
exactly as it does on theirs"*), and `RemoteCardArt.cs:2003-2005` (`FxSurface.Flight`: *"the same
2 s ramp during the hold"*).

**Failure scenario.** A peer takes a short rest and accepts the sacrifice — the case with no recess
to draw the card, named in the code at `:832-835`.
*Owner sees:* the card lying still and darkening from clean to charred across the game's 2 s
timeline, then flying.
*Viewer sees:* the slab appear already fully black on frame one, stand there, then fly.
The end state matches; the animation does not — and the ruling quoted eight lines above the defect
(`:829-832`, *"das soll so synchron mit den anderen Spielern sein (also die sehen auch dass die
Karte kurz liegen bleibt und erst dann kommt die Animation)"*) is about exactly this window.

**Reachable on hardware this round:** yes — a short rest with an accepted sacrifice is an ordinary
two-client event. The class's own instrument reports `stationary=` for this branch, so a non-zero
`stationary=` beside `artwork=observed` confirms it from a log.

## F6 — CONFIRMED — the mirrored scenario-RULES panel gets a content-height correction the owner's panel never gets

**Where.** `src/GloomhavenVR/Net/Remote/RemoteWidgetMirror.cs:1012-1017` (unconditional for both
`CloneAtBoardOwnersWidth` mirrors), reached for the rules mirror built at
`src/GloomhavenVR/Net/Remote/RemoteObjectivesPanel.cs:238`.

**Mechanism.** `Core/LayoutContentHeight.Apply` has **exactly two call sites** in the whole tree:
`WorldUI/Surfaces/TablePanelSurfaces.cs:2289` (inside `ObjectivesSurface.ApplyContentHeight`, called
only from `:2244`) and `RemoteWidgetMirror.cs:1017`. But there are **three consumers**: the
mirror-side call serves *both* `CloneAtBoardOwnersWidth` mirrors — the objectives panel
(`RemoteObjectivesPanel.cs:206`) **and** the scenario-rules panel (`:238`).

`ScenarioRulesSurface` (`TablePanelSurfaces.cs:3465-3861`) has its own `ApplyContentWidth` — whose
column product the mirror reproduces term for term — but **no height write of any kind**. So the
owner's `ScenarioModifierContainer` root keeps whatever height the conversion left it (the
`max(size, 100)` placeholder for a degenerate authored rect), while the clone of it is grown to its
own preferred height.

**Failure scenario.** A scenario with a special rule whose paragraph exceeds the placeholder height
(the user's own item-3 case, *"jede Runde einen Schaden"*, in German, or two rules at once).
*Owner sees:* the `VerticalLayoutGroup` squeezing children toward minimum while each row's
`ContentSizeFitter` puts its own height back — the documented overlap that seats row 2 over row 1's
last line (`LayoutContentHeight.cs:32-41`).
*Viewer sees:* the same rules laid out correctly and taller, and therefore fitted at a *smaller*
metres-per-pixel against the shared 0.09 m budget — different row positions **and** different glyph
size from what the owner is reading.

Note the direction: **the mirror is better than the owner**, which `RemoteObjectivesPanel`'s own rule
forbids — *"Absent on both sides beats different on each."*

**Reachable on hardware this round:** yes — any scenario carrying special rules. Falsifier already
shipped: the mirror prints `OBJECTIVES HEIGHT (mirrored) 'ScenarioRules': … before -> after`
(`RemoteWidgetMirror.cs:1032`); **the owner's log carries no matching line for `ScenarioRules` at
all**, and that silence is the finding.

## F7 — CONFIRMED — the mirrored board tooltip's BOX is a different size and a different type style from the owner's

**Where.** `src/GloomhavenVR/Net/Remote/RemoteBoardTooltip.cs:411`, `:419`, `:571-574`.

**Mechanism, size.** The mirror content-fits its own frame — `wrapPx = GameSkin.WidthPx` (which is
`tip.m_DefaultWidth`, `:574`), then `textW = Mathf.Clamp(pref.x, 1f, innerWrap)` (`:419`): the frame
**hugs the text**. The game does the opposite. `UITooltipTarget.PrepareTooltip`
(`decompiled/GH.Runtime/UITooltipTarget.cs:145-149`) always calls `UITooltip.SetWidth(width)` with
the **target's own serialized `width`, default `100f`** (`UITooltipTarget.cs:20`), and
`Internal_SetWidth` writes `m_Rect.sizeDelta.x` (`UITooltip.cs:804-807`). `m_DefaultWidth = 257f` is
only what `InternalOnHide` resets to (`UITooltip.cs:613`). Horizontal `ContentSizeFitter` is
`Unconstrained` and `UITooltip.SetHorizontalFitMode` has **no caller anywhere in the decompiled
game** — so the shown box is a *fixed* width that auto-fits vertically only. Record 9
(`NetProtocol.cs:20974`) carries text and nothing else — no width, no style.

**Mechanism, style.** `GameSkin` samples exactly one trio (`:571-573`): `m_TitleFont`,
`m_PCTitleFontSize`, `m_TitleFontColor`. `UITooltip.EvaluateAndCreateTooltipLines`
(`UITooltip.cs:681-732`) switches per line — `Title`, `Attribute`, `Description`, `Keyword` — each
with its own font, size, colour, `lineSpacing` and `fontStyle`. **`UITooltipLines.AddLine`'s default
style is `Attribute`, not `Title`** (`UITooltipLines.cs:63-78`), and the standard board path is
title + description (`UITextTooltipTarget.cs:89,92`; `TileBehaviour.cs:59,63`).

**Failure scenario.** Owner hovers an initiative-track portrait.
*Owner sees:* a bold title in the title font over a body paragraph in the description font, at the
description size and colour, in a box fixed at that target's serialized width (say 100 px, wrapping
to two lines).
*Viewer sees:* both lines in the title font at 16 px in `m_TitleFontColor`, at TMP default line
spacing, in a box wrapped at 257 px and then shrunk to hug the resulting single line — roughly twice
as wide and half as tall, at the same corner. Two untuned clients.

**Reachable on hardware this round:** yes; any board hover that publishes record 9.

## F8 — CONFIRMED (declared, unruled) — three shared-window SPAWN-POSE dials are client-local, so two differently-tuned clients seat the same shared window in different places

**Where.** `src/GloomhavenVR/Defaults/Defaults.WorldUI.cs:210` (`SharedWindowArcRadiusMeters`),
`:237` (`MapRoomWindowBarHeightMeters`), `:250` (`ScenarioWindowBoardClearanceMeters`); consumed at
`src/GloomhavenVR/WorldUI/Modal/ArcSeats.cs:6038` and scored at
`src/GloomhavenVR/Net/Remote/RemoteSharedGaze.cs:920-923`. Declared at
`src/GloomhavenVR/Net/NetProtocol.cs:9962-9968` and `Defaults.WorldUI.cs:233-236`.

**Mechanism.** A shared window's DIRECTION is decided by the host and obeyed by everyone —
`RemoteSharedGaze` publishes one yaw byte, and its own log line says *"THE HOST DECIDES AND EVERY
CLIENT OBEYS — this is what makes a shared window 1:1 here."* Its RADIUS and HEIGHT are not: each
client seats the window at `centre + ahead × Max(WorldUIConfig.SharedWindowArcRadiusMeters.Value,
0.05f)` (`ArcSeats.cs:6038`) at its own bar height. **None of the three has a `Tune*` id** —
verified, zero hits for `TuneSharedWindowArcRadiusMeters` /
`TuneMapRoomWindowBarHeightMeters` / `TuneScenarioWindowBoardClearanceMeters` in `NetProtocol.cs`.

Record 21 carries a pose **only once a human has moved the window** (`SharedPoseBit`: *"Set only
once the sender's user has really moved or resized the window — an untouched window publishes no
pose"*). So an untouched shared window diverges and stays diverged.

**Why this is a finding and not a note.** The project has already applied the correct remedy to the
*other half of the same object*: `SharedWindowSizeLaw` **freezes** `WindowLegibility` and
`CanvasScaleMm` to their shipped defaults for a shared window's SIZE, with the argument written out
in full — *"A shared window is not a mirror and has no owner … the only value every client can agree
on without an exchange is the SHIPPED one"* (`SharedWindowSizeLaw.cs:55-62`). The identical argument
applies to the spawn POSE and was not applied. The declaration at `NetProtocol.cs:9962-9968` is a
**bare engineering assertion** — no user ruling is cited — and it names its own remedy: *"freezing it
to a const is a one-line change once it is [settled on hardware]."*

**Failure scenario.** The mod's own spawn log *tells the user to move this dial* —
`ArcSeats.cs:6162`: *"raise `SharedWindowArcRadiusMeters` to put it back on the ring."* Maintainer
follows it and sets 1.10; his peer stays at 0.80. In the 3D map room a quest-confirm window opens
and nobody drags it.
*Owner sees:* the shared window 1.10 m out from the table centre.
*Viewer sees:* the same shared window 0.80 m out — 0.30 m nearer, at a different reading angle. The
maintainer points at a line on it and his finger lands somewhere else in the peer's view.
Same shape for `MapRoomWindowBarHeightMeters` (different heights) and
`ScenarioWindowBoardClearanceMeters`.

**Reachable on hardware this round:** yes, with one dial moved on one machine — and the mod's own log
line invites exactly that.

## F9 — PLAUSIBLE — `TryDockRect` reads the RECEIVER's own converted panel, so the "identical by construction" claim does not survive two canvas widths

**Where.** `src/GloomhavenVR/Net/Remote/RemoteWidgetMirror.cs:1853-1875` (`TryDockRect`), committed
at `:1698-1703`, claimed at `:1675-1679` and re-asserted to the log reader at `:1637-1643`.

**Mechanism.** `TryDockRect` walks `WorldUI.CanvasConversion.ActivePanels` — a **local** list — and
returns `p.HostRect.rect` for the panel whose `Target` is `_source`, i.e. **this client's own copy**
of the global widget. That rect is uGUI pixels of *this client's* canvas, with no canvas-width
normalisation anywhere on the path (`CanvasConversion.1.Core.cs:188`,
`CanvasConversion.3.Fit.cs:347-357 / 912-945`). `SharedWindowSize`'s design-frame repin is gated to a
closed four-member `SharedWindowKind` population (`SharedWindowSize.cs:13-23`); the initiative track
and element board are not in it.

**The brief's premise is real and is stated in the tree.** `WorldUI/Modal/SharedWindowSize.cs:25-33`
and `NetProtocol.cs:3162-3167`: host 1920x1080, co-player 2580x1080, the game's canvas scaler matches
on HEIGHT, and *"any rule that turns authored pixels into metres inherits it."* I verified the
2580x1080 figure independently — eleven sites cite it, including two-machine log evidence
(`GrabbableModal.cs:3458`, `CanvasConversion.3.Fit.cs:4701`).

The regime matters. The mirror's own logged track fit is 740x204 px into 0.309x0.085 m
(`RemoteWidgetMirror.cs:460`) ⇒ metres-per-pixel 4.176e-4 against 1/2400 = 4.167e-4, i.e. the fit is
**clamped at `MaxDensityScale = 1`**. In that regime metres-per-pixel is a constant and physical size
is *directly proportional* to the local pixel measure — a pixel difference is not absorbed into
scale, it lands straight in the size.

**Failure scenario.** If any visible graphic inside the initiative track's or element board's union
is stretch-anchored to the HUD canvas, the co-player measures a wider union than the host. Owner
(host) reads their own track at 0.309 m wide; the co-player's mirrored copy of that same owner's
track is drawn at `union_px / 2400` from their **own** 2580-wide canvas — physically wider, at the
same glyph density, on a board whose mount budget is identical. Nothing closes the gap and nothing
logs it as a difference.

**Why PLAUSIBLE.** No measurement of the initiative track's or objectives' authored pixels exists on
both machines anywhere in `src/`, `docs/` or `.planning/` — the two-machine pixel comparisons that do
exist are for the quest popup and the merchant art. **The decision row is the one member of this
family with real two-machine evidence and it is CLEAN**: `RemoteWidgetMirror.cs:1755-1768` records
owner-peer-log 720x48 against mirror-host-log 708x48 → 720x48 after padding and frame clamp, agreeing
to the pixel across the two canvases.

**Reachable on hardware this round:** yes, and it is one grep, no screenshot. The mirror already
prints `Remote board 'InitiativeTrack' mirror fitted: WxH m (NxM px) … measured via converted host
rect`. Diff that `NxM` against the **owner's** own `Docked 'GloomhavenVR.Panel_InitiativeTrack' …
(NxM px)` line in their log. Equal ⇒ exonerated; unequal ⇒ confirmed with no further work.

## F10 — PLAUSIBLE — the mirrored decision ROW's seat is derived from a TMP height measured on a VIEWER-language string

**Where.** `src/GloomhavenVR/Net/Remote/RemoteBoardFurniture.cs:2743`, consumed at `:2777-2782`.

**Mechanism.** `_promptMeasuredHeight = _decisionPrompt.renderedHeight` after a `ForceMeshUpdate`,
then `y = promptLineShown ? _decisionCeilingY - lineH - gap : _decisionCeilingY`. The string measured
is `RemoteDecisionPrompt.Compose(...)`, composed **on the viewer's machine in the viewer's language**
and laid out in a mod TMP. The owner's own row hangs off *his* HelpBox's measured bottom, in *his*
language, in the game's widget.

**Failure scenario.** Owner runs German, viewer runs English. The German damage prompt wraps to two
lines in the mirror's rect; the English one the owner's dock measured fits on one. The mirrored button
row — and the use-bar drawer stacked below it, which re-seats from `DecisionRowHeight` — hangs one
line-height lower on the viewer's board than the owner's does on his.

**This is not covered by the mixed-language exception.** That ruling permits the *text* to differ; it
does not licence the *row position* to. The file already carries its own falsifier (`:2825-2835`).

**Reachable on hardware this round:** yes, on any take-damage / short-rest prompt whose mirror falls
back to the mod-drawn plates.

## F11 — CONFIRMED — the mirrored wanted-slot pulse runs on the viewer's process clock, so its PHASE is arbitrary against the owner's

**Where.** `src/GloomhavenVR/Net/Remote/RemoteBoardFurniture.cs:5147` (`RemoteGlowPulse.Update`).

**Mechanism.** `float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.2f);` — byte-identical to the
owner's `PlayTray.7.Nested.cs:61`. `Time.unscaledTime` is **time since that process started**, so the
two clients evaluate the same sine at arguments separated by an arbitrary constant (minutes). Period
matches, phase does not, and nothing on the wire carries a phase or an epoch.

**Failure scenario.** Selection phase, owner has one empty recess. At any instant the owner's teal rim
is at the top of its breath (k ≈ 0.85) while the viewer's copy of that same rim is at the bottom
(k ≈ 0.30). The state is synchronous; the blink is not.

**Reachable on hardware this round:** yes whenever a wanted glow is up, but it needs two headsets
compared side by side — every glow on any one headset is mutually in phase.

*The same shape, recorded rather than claimed: the mirrored active-card pulse
(`RemoteActiveCardPulse.ApplyFace:246-253`) and the owner's `VRCard` (`VRCard.cs:1238-1243`) both
assert the game's own LeanTween loop — same period, same shine, but each starts when its own driver
first asserts. No wire field short of a phase byte closes either.*

## F12 — CONFIRMED — `[Optimize] RemoteContentInterval` is a VIEWER dial that sets a mirror's live cadence

**Where.** `src/GloomhavenVR/Net/Remote/RemoteBoardContent.cs:110`, backed by
`Core/PerfConfig.cs:188-189`.

**Mechanism.** `float over = Core.PerfConfig.RemoteContentSeconds; return over > 0f ? over :
DefaultRefreshSeconds;` where `RemoteContentSeconds` is `Mathf.Clamp(RemoteContentInterval.Value, 0f,
2f)`. This is a **live** value read on the receiving side — not a fallback for an absent wire field —
and it gates every peer board's structural pass, the furniture rebuilds, the use-bar symbols, the
decision row and the draw-order sweep (`RemoteControlBoard.cs:1044`, `:1170`).

**Failure scenario.** Viewer sets the row to 2 s. The owner's decision drawer opens and its button row
is on his board immediately; on that viewer's copy the row appears up to 2 s later. The per-frame
`TickWire` half paints *state*, but the row itself is *built* on this cadence. Owner and viewer are
looking at different boards for two seconds.

**Reachable on hardware this round:** only if the tester has moved that dial (ships at 0 = no change).
Listed rather than dismissed because it is a declared, un-ruled TIMING exception in the one direction
1:1 forbids, and because it is the only live viewer-config read on the mirror side outside F2/F3.

## F13 — CONFIRMED — the option colour transition snaps on the viewer and cross-fades on the owner

**Where.** `src/GloomhavenVR/Net/Remote/RemoteDecisionWidgets.cs:1339-1344`.

**Mechanism.** Declared in the source: uGUI cross-fades between `ColorBlock` states over
`fadeDuration` (0.1 s default); `PaintOption` writes the target colour outright. The hover *grow* is
reproduced faithfully (`AimHoverScale` / `AdvanceHoverScale`, `:1441-1512`); only the tint is stepped.

**Failure scenario.** Owner's beam crosses an option. *Owner sees* a 0.1 s tint fade. *Viewer sees* an
instant colour step — so on a peer's board the option pops in size smoothly and changes colour in one
frame. Small, but the rule counts a snap as a breach even when the end state matches.

**Reachable on hardware this round:** yes, on any mirrored prompt — but it needs a side-by-side video
to judge, not a log.

## F14 — PLAUSIBLE — the element strip's mod-drawn fallback orders chips by enum, while the owner's row order is a running history of creations

**Where.** `src/GloomhavenVR/Net/Remote/RemoteElementStrip.cs:1151-1200`.

**Mechanism.** `InfusionElementUI.ShowCreating()` ends with `base.transform.SetAsLastSibling()`
(`decompiled/GH.Runtime/InfusionElementUI.cs:158`), and nothing in `InfusionBoardUI` ever restores
sibling order. An element that has entered creation is permanently moved to the end of
`elementsHolder`, and later creations stack after it. The strip's fallback deals slots strictly by
`EElement` index (`:1153`, `:1199-1200`).

**Failure scenario.** Fire is infused via a creation pulse, then Ice. The owner's row reads
`… Ice, Fire`. A viewer whose strip has fallen back — the `_mirrorBlanked` demotion at `:1076` (three
blank content ticks, 0.75 s) or a menu/loading window — sees `Fire, Ice`: two elements swapped
left-to-right, with no cue.

**Why PLAUSIBLE.** The sibling reorder is confirmed in the decompiled source; whether it changes the
*drawn* order depends on `elementsHolder` carrying a `HorizontalLayoutGroup`, which is prefab scene
data this review cannot read. The strip's own comment at `:1151-1152` asserts that layout group
exists — and that assertion is itself unverified.

**Reachable on hardware this round:** only on the fallback path, which the existing `MIRROR BLANK` /
`DEMOTED` log lines already announce — so a log grep decides it without a screenshot.

## F15 — PLAUSIBLE — the mirrored decision row can be antique-tinted twice

**Where.** `src/GloomhavenVR/Net/Remote/RemoteDecisionWidgets.cs:1063` (`BaseColor = srcBg.color`,
captured once per clone rebuild), consumed at `:1348` (`node.BaseColor * tint * antique`).

**Mechanism.** `MakeNode` snapshots the **live** source widget's `Graphic.color` as the authored base.
But `WorldUI/Surfaces/DecisionDockSurface.cs:1960` mutates that very field on the live game widget
while a row is docked locally (`bg.color = bg.color * AntiqueTint;`, restored on undock at `:2238`).
`RemoteDecisionWidgets.ResolveShortRestBox` (`:886`) resolves through `CardsGameApi.AnyShortRestDialog()`,
whose first answer is the *active hand's* dialog (`Cards/CardsGameApi.cs:2880-2885`) — exactly the
object `DecisionDockSurface` docks and tints (`:2076`). A clone bound during that window carries
`authored × AntiqueTint` as its `BaseColor`, and `PaintOption` multiplies by `antique` again.

**Failure scenario.** End of round; viewer and peer both take a short rest. The viewer's own dialog is
docked and antique-tinted; the peer's short rest publishes roles `[4,5]`; the mirror rebuilds its
clone from the same live `YesNoDialog`. *Owner sees* Yes/No plates at `authored × antique`; *viewer
sees*, on the peer's board, the same plates at `authored × antique²` — noticeably darker — for the
whole prompt, because `BaseColor` is only re-captured on a clone rebuild while `RestoreRowAdjustments`
restores only the source. Labels are unaffected (overwritten with the owner's gold at `:1259-1268`).

**Why PLAUSIBLE.** Every link is verified in source; the simultaneity (two overlapping prompts) is not.

**Reachable on hardware this round:** only in a session where two players' prompts overlap. Not a
scripted test.

## F16 — PLAUSIBLE — the mirrored tooltip corner is measured over the MIRROR's own subtree

**Where.** `src/GloomhavenVR/Net/Remote/RemoteBoardTooltip.cs:389-399` (`Reseat`).

**Mechanism.** `Cards.PlayTray.MeasureBoardLocalExtents(_boardRoot, out topLocalY, out halfLocalX)`
walks **this client's mirrored board**; the seat is
`authored + (BoardHalfWidthLocal − halfLocalX, topLocalY − BoardTopLocalY, 0)`. The owner runs the
same call over *his* board. The two subtrees are not guaranteed to have the same extent: the
objectives and initiative docks are `MirroredWidget` clones only when they resolve and fall back to
narrower mod-drawn panels otherwise, and the active-card matrix's width is a function of how many
cards the mirror actually seated.

**Failure scenario.** The mirror cannot clone the objectives widget and falls back to the mod-drawn
panel, which reaches less far off the left edge. `halfLocalX` comes back smaller, the correction is
larger, and the viewer's hint sits tens of mm inboard of where the owner's sits. The file's own doc
computes the same lever at up to 281 mm for the active column.

**Reachable on hardware this round:** only when a widget mirror falls back, which the content line
reports; the `Seats()` diagnostic already prints the measure path per panel.


## F17 — CONFIRMED — `RemoteMapStory.SendDue` latches true for the rest of a map-room session, so the extras packet goes out at frame rate instead of 5 Hz

*Ranked here rather than higher because the damage is to the CHANNEL every mirror rides, not to one
picture. It is the project's most-repeated defect shape — a gate that never closes, silently — and
the comment directly above it asserts the opposite.*

**Where.** `src/GloomhavenVR/Net/Remote/RemoteMapStory.cs:471-473` (the `SendDue` term) against `:939`
(the bookkeeping `Sample()` writes). Consumed at `src/GloomhavenVR/Net/Avatar/NetAvatarDriver.cs:1960`
(the pre-emption gate) and `:1968` (`_extrasAccumulator = 0f`).

**Mechanism.** The getter's test has two branches:

```csharp
bool finished = (box == null && StoryLocal.Key != 0u
                 && Time.unscaledTime < StoryLocal.FinishedUntil)
                || StoryClickedThroughHere(box);            // :471-473
```

The field it is compared against has one:

```csharp
_sentStoryFinished = box == null && StoryLocal.Key != 0u && now < StoryLocal.FinishedUntil;  // :939
```

`StoryClickedThroughHere` (`:581-587`) **requires `box != null`**. So in the state it describes —
the mod still floats the map story window while the game says closed, on the last page, past the
settle — `finished` is `true` while `_sentStoryFinished` is written `false` on every send.
`finished != _sentStoryFinished` is therefore permanently true and the getter never converges.

`NetAvatarDriver.cs:1931-1966` is an `if (…all false…) return;` gate with `!RemoteMapStory.SendDue`
as one conjunct, so a stuck-true `SendDue` means the gate is never taken and `_extrasAccumulator` is
zeroed every frame. It does not self-clear when the 60 s `FinishedLingerSeconds` expires: `:838-843`
deliberately *keeps* `StoryLocal.Key` on the `clickedThrough` branch. It clears only when the game
re-opens a story window or `MapRoomDriver.Active` goes false.

**Failure scenario.** Two clients on the campaign map with the 3D world map on. Player A clicks the
last page of a map story through. From the next frame, A's client sends the full extras packet
(worst case 1747 B) at frame rate rather than 5 Hz for the rest of that map-room session — roughly
18× the designed load on the **unreliable** side channel that carries every mirrored surface, and
the same multiple of sender CPU (`BoardTuningSampler.Sample` walks ~118 entries, plus the arc-order,
held-face, wall-fade and use-bar samplers).

*Owner sees:* nothing — a sender never applies its own record.
*Viewer sees:* no specific wrong picture, but every extras-borne mirror of A now depends on an
unreliable channel carrying 18× its designed load. **Stated as a resource defect, not a picture
defect** — dropped-packet consequences were not measured.

**Reachable on hardware this round:** yes. It needs only the 3D map room standing and one map story
clicked through — the exact flow this file's own ModBuild 449 block was written from.

## F18 — CONFIRMED asymmetry, PLAUSIBLE verdict — the decision-label mask covers card names that the discard-fronts ruling has already made public

*This is the one finding that is a 1:1 breach in the STRICT direction, and it is a collision between
two rulings given on the same day. Reported as a collision; the maintainer decides which wins.*

**Where.** `src/GloomhavenVR/Net/DecisionLabelMask.cs:314` and `:390`; predicate at
`src/GloomhavenVR/Net/RevealGate.cs:253`; its face-side twin at `RevealGate.cs:1205-1209`.

**Mechanism.** The mask's covered-name set walks the owner's **discard pile**
(`DecisionLabelMask.cs:314`, `AddCovered(into, actor, cc.DiscardedAbilityCards)`) and admits a card
only via `RevealGate.PeersMayNameOurCard`:

```csharp
// RevealGate.cs:253 — the NAME gate
public static bool PeersMayNameOurCard(CPlayerActor? actor, int cardInstanceId) =>
    PeersSeeOurCardFronts || IsPubliclyRevealedCard(actor, cardInstanceId);

// RevealGate.cs:1205-1209 — the FACE gate
private static bool CardIsPubliclyVisible(PeerCardPopulation population, CPlayerActor? actor, int id)
    => IsPubliclyRevealedCard(actor, id)
       || (PileFrontsReach(population) && IsDiscardedCard(actor, id));
```

`IsPubliclyRevealedCard` walks only `ActivatedCards` / `LostAbilityCards` /
`PermanentlyLostAbilityCards` (`:950-965`) — **there is no discard term.** The FACE gate has one; the
NAME gate does not. `PileFrontsReach` is true for `Selectable`, the population the mirrored pile fan
declares (`RemotePileFronts.cs:481, 666`).

**Failure scenario, from the game's own source.** `decompiled/GH.Runtime/CardsHandUI.cs:768` builds
the short-rest wording from `playerActor.CharacterClass.DiscardedAbilityCards[shortRestLostCardID]`
and formats `GUI_LOSE_CARD` with `cardUI.fullAbilityCard.Title`, shown as a `DialogOption` (`:823`).
A short rest runs inside `SelectAbilityCardsOrLongRest` (`CardsHandUI.cs:248`), so
`PeersSeeOurCardFronts` is shut, and `PerformShortRest` removes nothing — the card stays in
`DiscardedAbilityCards` throughout.

*Owner reads:* `Verliere "Zusatzdolch"` on his own row.
*Viewer reads:* `Verliere "<versiegelte Karte>"` on the mirrored row — **while, one surface over, the
viewer's mirrored discard fan is drawing that same card's FRONT**, by the user's own
*"Die Fächer der piles werden also ab jetzt immer mit Vorderseiten gezeigt ohne Ausnahme."*
A name the viewer can read off the card is blanked in the sentence about it.

**Why PLAUSIBLE as a verdict.** The code asymmetry is CONFIRMED. Which of two unqualified same-day
rulings wins — the anti-cheat mask (*"der Name der Karte in dem Dialog im remote board muss
ausgeblendet werden"*) or the discard-fronts ruling — is a user question, not this lane's. What is
not in doubt is that the mask masks **every** name in `DiscardedAbilityCards`, including cards the
mirror is simultaneously drawing face-up. It fails in the safe direction: over-covering, never
leaking.

**Reachable on hardware this round:** yes — a short rest is routine, and the peer's discard fan is
browsable at the same moment.

## F19 — CONFIRMED (on/off) / PLAUSIBLE (deflection) — the approved viewer-local VOICE BADGE exception has spread past SCALE into the badge's presence and its animation

**Where.** Carrier `src/GloomhavenVR/Net/Remote/RemoteNameTag.cs:267`; mechanism in
`src/GloomhavenVR/Voice/VoiceBadge.cs:121-125` and `src/GloomhavenVR/Voice/VoiceSpatial.cs:457-472`.

**Mechanism.** The approved exception is the SCALE (`VoiceBadge.cs:147, 162`, and it is correctly
scoped there — a fraction of `avatarSize`, nothing else). But the badge's **presence** is also
viewer-local:

```csharp
bool want = carrierVisible && avatarQuad != null
            && VoiceModule.SpeakingBadge != null && VoiceModule.SpeakingBadge.Value   // viewer dial
            && VoiceSpatial.TryGetVoice(_playerId, out bool speaking, out _) && speaking;
```

and `speaking` itself is `IsSpeaking(b.Voice) && !IsMuted(b.Voice)` — **this viewer's mute of that
peer** (`VoiceSpatial.cs:458`). The loudspeaker **deflection frame** is then computed from
`b.Source.GetOutputData(...)` — this viewer's own `AudioSource` — through this viewer's rolloff and
`[Voice] FullLevelMeters` / `SilenceMeters` / `RolloffShape` (`:459-471`). The class doc states the
intent outright (`VoiceSpatial.cs:145-151`: *"a measurement of THE EXACT AUDIO THE PLAYER IS
HEARING"*), which is exactly what makes it viewer-local.

**Failure scenario.** Player C is talking. Viewer A stands next to C's mask; viewer B is across the
table (or zoomed out, or has `FullLevelMeters` set low). At the same instant A sees the loudspeaker
at full deflection and B sees it at step 0. If B has muted C in the game's own voice UI, B sees no
badge at all while A does. Same speaker, same word, two different animations.

**Two honest qualifications.** First, C has **no counterpart badge over their own head**, so this is
a divergence between two VIEWERS rather than viewer-versus-owner — which is why the approved
exception was written for the scale in the first place, and why this is ranked last. Second, whether
`AudioSource.GetOutputData` returns data after 3D distance attenuation is a Unity behaviour that
could not be settled from this repo; if it does not, the badge still varies with the viewer's mute
and dial, just not with distance. The on/off half is unambiguous.

**Reachable on hardware this round:** yes, with voice chat in use — no special state needed.


---

# NOTES — mechanism, but no failure scenario I can state

1. **`s_flameDrawnLogged` is the only guarded latch of its family.** `RemoteCardArt.cs:2801` is also
   `new bool[4]`, but `:2843` bounds-checks it. Growing the three arrays in F1 without adding the
   same guard leaves the next enum member in the same trap.

2. **Two surfaces drive the card-FX rig without naming themselves.** `RemoteCardFx.Acquire`
   (`:1097`) creates its `RemoteCardArt` with no `Surface` assignment while `RemoteBurnFx.Acquire`
   (`:1077`) sets `Flight`; `RemoteHandFan.TickUsedCardFx` (`:5190`) deliberately leaves it
   `Unnamed`. Both therefore latch on index 0, so whichever builds a rig first silences the other's
   arming line for the process — defeating the per-surface latch that exists to stop exactly that.
   Instrument-only; no picture is affected.

3. **`RemoteActiveCards.DrivePulse` has no path to run when the viewer's board is hidden.** It is
   reached only from `RemoteControlBoard.RefreshContent`, behind the early return at
   `RemoteControlBoard.cs:746-759`. `DriveSingle(_owner.HeldSlab(1)…)` (`RemoteActiveCards.cs:712`)
   is the only writer of `ActionHighlightDriver.Off` onto a HELD slab, and held slabs are avatar
   content that keeps ticking. With `[Net] RemoteBoards` at `Off`/`ActionPhaseOnly`, a pulse
   asserted before the board hid keeps running indefinitely (the game's `ShowHover()` LeanTween loop
   is self-re-entrant). Not reachable at the shipped default (`Defaults.RemoteBoards = Always`).

4. **Six item-berth constants are unpinned.** `RemoteBoardFurniture.cs:2398, 2402, 2407, 2413-2415`
   are documented as "verbatim from `PlayTray.4.Slots`" and are **not** in `check-mirrors.sh`. They
   agree today (`PlayTray.4.Slots.cs:465, 469, 474, 479-481`). Their neighbour `UseSlotInnerFactor`
   *is* pinned, which makes the omission look like an oversight.

5. **`RemoteBoardTooltip.DefaultMetersPerUiPixel = 0.0005f` is an unpinned PRODUCT** of
   `Defaults.CanvasScaleMm 1.0 × 0.001 × 0.5`. Correct today, invisible to
   `check-remote-defaults.py` by that script's own stated limitation, and a change to
   `Defaults.CanvasScaleMm` would silently mis-size every peer's tooltip with nothing failing.

6. **`SoftCueArt.KeySafe` shifts two mirrored golds against the LOCAL player's chroma-key colour**
   (`RemoteBoardFurniture.cs:2537`, `RemoteControlBoard.cs:3660`). It is a mixed-reality safety
   guard with a stated reason, but it does make a mirrored surface's colour a function of a
   viewer-local setting: two players with different key presets see the same berth in slightly
   different gold.

7. **`Pair.Apply` copies `Image.sprite` but never `Image.overrideSprite`**
   (`RemoteWidgetMirror.cs:2288-2292`). `m_OverrideSprite` carries no `[SerializeField]`, so
   `Object.Instantiate` does not carry it either — the same non-serialized-field class
   `RemoteAbilityCardSource.cs:120-165` documents at length. Every `overrideSprite` writer in
   `GH.Runtime` is a `UnityEngine.UI` transition, so on these panels it is a *local* interaction
   state that arguably should not be copied — but nothing in the file says so, and the gap is
   currently silent either way.

8. **`_dstTmp.fontSize = _srcTmp.fontSize` is copied on the `CloneAtBoardOwnersWidth` path**
   (`RemoteWidgetMirror.cs:2276-2280`), where the clone is deliberately laid out at a *different*
   column from the source. If TMP auto-sizing is on for any label in the objectives or rules
   subtree, the source's `fontSize` is the value TMP solved for the **viewer's** column, and copying
   it makes mirrored glyph size a function of the viewer's `ObjectivesWidth` — contradicting
   `LogOwnersColumn`'s own claim that "glyph size must move in NEITHER" (`:1070-1071`). The prefab
   dump records no autosize flag, so this could not be settled; if autosize is off it is a no-op.

9. **Record ids 38, 40 and 42 are unallocated and undocumented.** 42 records exist across ids 1..45.
   Nothing in `NetProtocol.cs` says why the three are skipped, and no checker or wire test pins the
   id space. Harmless while `VersionGuard` blocks mismatched builds by default, but an additive TLV
   id space with undocumented holes is how a future allocator re-uses one.

10. **`RemoteEmptyFanHint.Place()` billboards at the LOCAL head** while its two siblings
    (`RemoteItemFan.TryResolvePose`, `RemoteBrowserFan.TryResolveAnchor`) billboard at
    `_owner.HeadHolder`. Three watchers see three orientations of one placard. Aiming it at the
    owner would present its back to every reader, and the class argues that — so it is listed as a
    declared exception rather than a defect, but the family is inconsistent.

11. **`RemoteDecisionPrompt.CompanionName` reads the singleton `TakeDamagePanel.actorBeingAttacked`**
    (`:264-277`), which holds one decision. With two concurrent summon-damage prompts the peer's
    board falls back to the generic wording while the owner reads the named one. Rare, deliberate,
    and the guard itself is correct.

12. **`RemoteBurnFx.cs:609-628` widens the face permission on an unresolvable card id** —
    `cardId == int.MinValue` falls back to `PeerCardPopulation.AlreadyPublic` rather than
    `Selectable`. The provenance argument (the widget came out of `GetPileWidgets(hand, burnt:true)`
    narrowed by `PileWidgetIsArcMember`, so Lost-list membership is structural) is sound and I could
    not falsify it, but it is the one place a face permission is granted from provenance rather than
    from a card test. Worth a line in the next round.

13. **`PresenceState.MaxSize` bookkeeping is healthy.** Worst case 1747 against `MaxSize` 2100,
    margin 353, which is more than the largest single record (257, board tuning). The rule ("every
    new record adds its worst case in its own commit") is being followed. One stale paragraph
    (`:1770`, "RAISED 848 → 1280") sits below several later raises, which is confusing but not wrong.

14. **`RemoteCardArt.RescanMips` is gated on the VIEWER's `[Cards] FaceMipBake`** (`:538`). A
    viewer-local rendering-quality dial applied equally to local and mirrored cards, of the same
    class as the supersampler — not a content/size/pose/timing term. Recorded so it is not silently
    exempted.

---

# FALSIFIED COMMENTS

Every one of these states a behaviour the code does not have. Ranked by how likely each is to
mislead the next reader. **Four of them protect or describe a live defect.**

| file:line | the claim | what is actually true |
|---|---|---|
| `RemoteBoardFurniture.cs:4863-4866` | "The viewer's `[ButtonAnim] Enable` switch gates it… The DURATIONS are the AUTHORED ones and not the viewer's tuning." | **Both halves false.** The gate is the OWNER's switch (`InertCap.SetShown:4494`, `_anim.Enabled = tuning.ButtonAnimOn`) and the durations are the owner's off record 28 ids 174/175. This is a verbatim description of the class-1 defect the code no longer has — the most dangerous kind, because it invites a "correction" back to it. |
| `RemoteBurnFx.cs:54-57`, `:709-712`; `RemoteCardArt.cs:2003-2005` | "the burn ramping on the real face" / "it lies on their board and chars, exactly as it does on theirs" / "the same 2 s ramp during the hold" | There is no ramp anywhere in the class: `:856` writes `SetAbilityBurnProgress(1f)` every frame. **= F5.** |
| `RemoteItemFan.cs:1910-1918` | "THE BEAT PERIOD IS THE VIEWER'S OWN `[Cards] ItemCueBeatSeconds` AND THAT IS DELIBERATE… not a per-sub-feature sync carve-out" | Overruled by the ruling recorded at `NetProtocol.cs:647` and in project memory. The comment argues against a decision already taken, and is the reason the consumer change was never made. **= F2.** |
| `RemoteWidgetMirror.cs:1675-1679`, restated at `:1637-1643` | "its fitted HOST RECT… makes the mirrored panel geometrically identical to the owner's dock **BY CONSTRUCTION** rather than by two measurements agreeing" | `TryDockRect:1853-1875` walks the **local** `CanvasConversion.ActivePanels` and returns the *receiver's* own converted panel, fitted from the *receiver's* canvas pixels. It is precisely "two measurements agreeing", which the same sentence disclaims. **= F9.** |
| `Core/LayoutContentHeight.cs:66-69`, restated at `RemoteWidgetMirror.cs:1005-1007` | "**Both call sites** are here so that the owner's panel and the mirrored one cannot drift the day one of them is retuned." | Two call sites, **three consumers**. The mirror-side call serves both `CloneAtBoardOwnersWidth` mirrors; `ScenarioRulesSurface` has no owner-side height write at all. The two sides drift *by construction*, not "the day one is retuned". **= F6.** |
| `RemoteBoardTooltip.cs:51-58` | "SAME UNITS, SO SAME SIZE… Same text ⇒ same box, **by construction** and with ZERO wire bytes" | The unit conversion is right; "same box" is false — the wrap width is the wrong number and the fit rule is the wrong rule. **= F7.** |
| `RemoteBoardTooltip.cs:49-50` | "the game's `m_TitleFont` at `m_PCTitleFontSize` in `m_TitleFontColor`" | True only for `LineStyle.Title`. The game switches per line across four styles, and `AddLine`'s default is `Attribute`. **= F7.** |
| `RemoteBoardFurniture.cs:3874-3877` | "the teal 'wanted' pulse and the gold snap flash share this shape, **exactly as on the local board**" | The *shape* is shared; the *material* is not — alpha-blended here, additive there. **= F4.** |
| `RemoteCardArt.cs:3160-3163` and `:1996` | "`[Unnamed]` NEVER APPEARS… an `[Unnamed]` line would mean some surface started driving the look without naming itself" / "a face nobody drives at all" | Two surfaces do exactly that today — `RemoteHandFan.TickUsedCardFx` (which says so at `:5150`) and `RemoteCardFx.DriveFlightLook`. |
| `RemoteCardArt.cs:1628-1631` and the shipped log string at `:1838-1841` | "`RemoteHeldCardFace`… is not a `PeerBoardFade` follower and never fades today, so it has no bleed to correct yet" | `RemoteHeldCardFace.cs:146` calls `PeerBoardFade.Follow(..., FollowRule.WhileOverBoard)`. The held slab **is** a fade follower. The code turns out fine (print and body are the same rectangle either way) but the log line will tell a future reader nothing is wrong when the surface has already moved. |
| `RemoteBoardCard.cs:1236-1238` | "`FxSurface.Unnamed`'s doc… OWES a correction plus an `Active` member; that file was not handed to this lane, so the active wash deliberately leaves `Surface` at `Unnamed`" | The `Active` member exists (`RemoteCardArt.cs:2016`) and `SetActiveCardLook` sets it (`:1032`). The comment describes a state two builds old — and the member it now uses is the one that throws. **= F1.** |
| `RemoteControlBoard.cs:1036` | "The mark is re-derived locally every frame from THIS client's read of who is at turn" | `CharacterFocus.MarkForPeer` reads only `Peers[playerId]` — `ActorId`, `OwnsAttention`, `AttentionId`, all three from record 22 — and touches no local turn state. The block comment 20 lines below (`:1123-1126`) says the opposite and is the correct one. Code is fine. |
| `RemoteControlBoard.cs:101-102`, same over-claim at `RemoteWidgetMirror.cs:76` and `RemoteDecisionWidgets.cs:78` | "a **runtime guard** (`RemoteBoardFurniture.StripColliders`) destroys anything that ever slips through" | It is a **build-time sweep** with six fixed call sites. Everything built after them — the lazy pile-cue rings/embers, the tooltip canvas, the focus outline, the grab-bar rod, every `RemoteWidgetMirror` clone — is never swept. Inertness still holds, but by each builder's own discipline, not by this "guard". |
| `Net/Board/BoardTuning.cs:369-374` | "`[Cards] FanCloseDuration` is **DELIBERATELY NOT SAMPLED**, though id 156 is declared for it… The guard carries it as a PENDING debt" | It **is** sampled — by `:367-368`, the statement immediately above the comment — and `:360-364`, five lines up, explains that it started riding in ModBuild 306. Two contradictory comments, five lines apart, with the stale one sitting directly *below* the line that falsifies it. `check-wire-coverage.py` reports 0 PENDING debts today. **This also corrects `DESIGN-1TO1-RESIDUE.md`, which still carries the old claim.** |
| `RemoteElementStrip.cs:202-205` | "the vanilla creating branch… writes exactly ONE field, `creationImage.enabled = isCreating`, and calls `ShowCreating()`" | `InfusionElementUI.cs` `case Inert` writes **four** and calls `SetAvailable(false)`; `ShowCreating()` is called only when `lastState != newState`. The narrower sub-claim is true and the conclusion still stands. |
| `RemoteDecisionWidgets.cs:816-822` | "the layout… is the one Unity computed while the panel was active at scene init (**which it must have been**, or the Singleton would not be initialized)" | An inference, not a check. It happens to hold because `UIWindow.Hide` deactivates only conditionally (`UIWindow.cs:744-747`), but the file's own `Pair.External` doc (`RemoteWidgetMirror.cs:2118-2131`) and `RemoteDialogOptions.LayoutCaption` (`:348-378`) both record the opposite outcome elsewhere. Right conclusion, wrong argument. |
| `RemoteDecisionPrompt.cs:44-46` | the mandatory-use variant renders **without** the active-bonus card names the owner sees | Stale: `:136-148` records that record 33 carries the keys since ModBuild 307 and the names *are* composed. The class doc contradicts the code below it. |
| `RemoteUseBarSymbols.cs:35-39` (pre-479 half) | "the game raises them from REPLICATED messages… so a peer's own bonus bar is populated with the same rows for the same actor" | False for the prevent-damage prompt — **but already corrected in the same file's ModBuild-479 block (`:62-90`) and already fixed by record 45.** Listed for completeness; not an open defect. |
| `scripts/check-remote-defaults.py` header | "What is genuinely no longer covered: **nothing**." | False; the script contradicts it at `:244-247`, and it cannot see runtime dial ownership at all. See the checker section above. |

**Claims checked and NOT falsifiable** (recorded because each is load-bearing): `RemoteBoardLayout`'s
whole mount derivation; `RemoteTrayVisual`'s shared-prefab argument; `RemoteBoardFocus` rule 1
(verified — the owner's own board drops the focus in the secret window too,
`CharacterFocus.PresentedHandCore:1593`); `RemoteElementStrip`'s "the three element masks are
replicable" (verified in full against `UIUseAugmentationsBar.cs:129/326`,
`CardsActionControlller.cs:598-607`, `UIUseItemsBar.cs:589-594` — **the `DESIGN-1TO1-RESIDUE.md` §6
account of this is correct**); `RemoteStatusReadouts`' "vanilla's own rule, verbatim"
(`InitiativeTrackPlayerAvatar.cs:105-112` ≡ `RevealGate.cs:154-158`); `RemoteAbilityCardSource`'s
"the widgets exist for every actor" (`CardsHandManager.cs:482-505`, no ownership gate);
`ItemsPile`/`PileBrowser` `SplitPivotIndex` matching their mirrors term for term;
`InitiativeTrack.NeedMinimizeEnemyInitiativeTrackAvatars` being an entry count against a serialized
prefab constant and **not** a screen term; `CardGlow`/`ButtonTuning`/`ButtonStroke` constants
carrying no viewer config; the `CardDustFx.Permission` and `NativeButtonSkin.LabelOwner` seams being
fully taken at every mirror call site.

---

# THE ALLOWED EXCEPTIONS, AND EVERY OTHER EXCEPTION DECLARED IN THE TREE

## The ones I was told to expect — all present, all correctly scoped

| exception | where declared | verdict |
|---|---|---|
| **Mixed language** — text that TRAVELS is the sender's rendered words; text carried as a KEY is the viewer's language | `Core/Loc/Loc.cs`; applied at `RemotePickBanner` (travels), `RemoteDialogOptions.cs:43-46` (`DialogOption.text` is a runtime string → owner's language), `RemoteDecisionWidgets.cs:892-897` (short-rest question is the constant key `"GUI_SHORT_REST_CONFIRMATION"`, its only call site — `ShortRest.cs:100` — → viewer's language) | **Correct, and no record has drifted across the line.** Every match KEY on the wire is a localization key, YML id or guid, never a translated string (`HashMapKey`, `RemoteStorySync.HashDialog`, record 33). **One consequence is NOT covered by it and is a finding: F10** — the ruling permits the text to differ, not the row *position* derived from measuring it. |
| **Voice badge scale** — viewer-local | `NetProtocol.cs:647`; read only at `Voice/VoiceBadge.cs:147, 162` as a fraction of `avatarSize` | **Correctly scoped to the SCALE.** It has not spread to the badge's position, content, colour or timing. |
| **Secret quest** | `RemoteWidgetMirror.cs:2144-2151` (`SuppressSecretBranches`) | **Ruling-backed**, cites `ActorStatPanel.cs:557/566` and `BattleGoalContainer.cs:44/73/81`. Permanently killed on every clone; verified 0 hits on the shipping widgets. |
| **Card FACES during `SelectAbilityCardsOrLongRest`** | `Net/RevealGate.cs` (the whole file) | Correctly scoped, with two nested carve-outs that OPEN a face the phase would close — a burning/active/lost card ("Beim Verbrennen EGAL AUS WELCHEM GRUND…") and the discard/burnt pile arcs ("immer mit Vorderseiten gezeigt ohne Ausnahme"). Both quoted verbatim with dates at `RevealGate.cs:421, 470, 983, 1303, 1424` and applied per card at `RemotePileFronts.cs:202-247`, `RemoteHandFan.cs:1657-1694`, `RemoteCardFx.cs:496-509`, `RemoteHeldCardFace.cs:204-257`. |

## A THIRD approved exception exists that my brief did not list

| exception | where declared | verdict |
|---|---|---|
| **The CARD NAME is masked out of a mirrored decision prompt while the face rule says that card is covered** | `Net/DecisionLabelMask.cs:7-14` ("THE ONE APPROVED EXCEPTION TO THE 1:1 RULE") and `Net/RevealGate.cs:588-598` ("THE FIRST APPROVED, NAMED EXCEPTION TO THE 1:1 RULE") | **Cites a user ruling**, quoted verbatim in German and dated 2026-09-07: *"wegen dem Anti-Cheat-System in der Auswahlphase muss hier ein genehmigte Ausnahme der 1:1 Regel greifen, der Name der Karte in dem Dialog im remote board muss ausgeblendet werden."* Correctly scoped: applied at the **SENDER**, per card via `RevealGate.PeersMayNameOurCard`, and **before** both byte caps see the string, so a truncation can never cut a mask off and leave the name. |

## Every OTHER exception declared in the tree, with its status

| file:line | what it exempts | ruling or assertion |
|---|---|---|
| `RemoteBoardScenarioGate.cs:12-16` | no peer board exists outside a scenario, for anybody, at any dial setting | **USER RULING**, dated and quoted (2026-08-22 item 1) |
| `RemoteBoardFocus.cs:55-62` | an exhausted owner's board keeps its surface and panels but loses every card | **USER RULING**, 2026-09-05 item 13, quoted |
| `Net/Board/PeerBoardFade.cs` (`:904`, `:932`, `:961`, `:970`, `:2649`) | viewer-local see-through of a peer's board while it occludes the play field | **USER RULINGS**, four of them quoted verbatim with dates (items 6, 7, 10, 11a of 2026-09-06/07), including the "entweder alles oder nichts" scope. Viewer-local by necessity — occlusion is a relation between the viewer's eye and the board. Ships `Off`. |
| `RemoteInitiativeTrack.cs:93-108` | initiative NUMBER and FX of a foreign player are not mirrored during the secret phase | **USER RULING** (2026-08-08) and **correctly scoped** — the mirrored path inherits vanilla's own gate (`InitiativeTrackPlayerAvatar.cs:105-112`) and the fallback path uses `RevealGate.ShowRoundCardFronts`; no leak outside the phase |
| `RemoteBoardVisibility.cs:8-14` / `RemoteBoardGate` | `[Net] RemoteBoards`: Off / ActionPhaseOnly / Always — whole-board, viewer-local | **Bare assertion in source**, but it matches the standing project ruling recorded in memory ("it syncs fully or not at all; whole-board off is `[Net]`"). Whole-board granularity, so not a per-sub-feature carve-out |
| `RemoteBoardVisibility.cs:60-72` | hand-held surfaces are AVATAR content and are not gated by the board dial | Bare assertion, argued from the setting's own name |
| `RemoteBoardContent.cs:99-108` | `[Optimize] RemoteContentInterval` may widen a mirror's content cadence | **Bare assertion, un-ruled — and it is F12**, a declared TIMING exception in the one direction 1:1 forbids |
| `RemoteItemFan.cs:1910-1918` | the item-cue beat is the viewer's | **Bare assertion that CONTRADICTS a recorded ruling — F2** |
| `NetProtocol.cs:9962-9968`, `Defaults.WorldUI.cs:233-236` | shared-window spawn radius/height are client-local | **Bare assertion, un-ruled — F8.** Names its own remedy |
| `RemoteDecisionWidgets.cs:1103-1110`, `:1410` | a non-interactable option never grows on the mirror although the game grows it | Bare assertion, self-derived, cost stated |
| `RemoteDecisionWidgets.cs:1339-1344` | the option colour cross-fade snaps | **Bare assertion — F13** |
| `RemoteStatusReadouts.cs:43-46`, `:165-181` | an "INI" badge the owner's board does not have, shown only while the real track falls back | Bare assertion, self-derived, correctly scoped (`SetShownWhileTrackFallback` removes it the moment the track mirrors). The paragraph beside it deletes a REST plate for failing the same test |
| `RemoteInitiativeTrack.cs:660-668` | the order override stands down during the ~0.5 s reorder slide | Bare assertion, honestly stated, with a convergence argument |
| `RemoteInitiativeTrack.cs:284-286` | the entry `Mask.enabled` flip is not re-driven | Bare assertion, named as a known limit |
| `RemoteInitiativeTrack.cs:1895-1901` | the mirrored hover popup draws over the viewer's hands (ZTest Always) | Bare assertion, scoped and time-bounded to the peer's hover; the owner's own popup ships under the same exception |
| `RemoteItemFan.cs:505-511`, `:1218-1227` | the controller HAPTIC of a chip lift is not replayed | Bare assertion, reasoned, consistent across all three fan mirrors |
| `RemoteItemFan.cs:1247-1252` | with the arc down, the recess card's hover lift is dark on the mirror | Bare assertion, named as an open gap needing a sender-side source |
| `RemoteElementStrip.cs:162-175`, `:341-348` | the created pop / creating breath run on a house curve because the owner's `GUIAnimator` curves are prefab data | Bare assertion, scoped to the mod-drawn fallback only |
| `RemoteEmptyFanHint.cs` header | the placard billboards at the LOCAL head | Bare assertion; text half correctly cites the mixed-language ruling. Inconsistent with the two sibling fans — see NOTE 10 |
| `RemotePileFronts.cs:1000-1007`, `RemoteItemFan.cs:1903-1908` | item SPENT flags and the usable mask are deliberately not behind `RevealGate` | Bare assertion, but well reasoned: a slot state is not a card identity, and gating it would make the mirror disagree with its owner in the one phase the ruling grants no exception to |
| `RemoteUseBarSymbols.cs:336-337` | an augment tile stays blank | Bare assertion, and **verified symmetric** — the owner's `UIUseAugmentation` has no icon field either, so nothing is owed |
| `RemoteBoardFurniture.cs:2536-2537`, `RemoteControlBoard.cs:3660` | `KeySafe` reads the local player's chroma key | Bare assertion with a stated MR-safety reason — NOTE 6 |
| `RemoteBurnFx.cs:609-628` | face permission widened on an unresolvable card id | Bare assertion — NOTE 12 |
| `RemoteBoardFurniture.cs:2295-2299` | rest-disc / follow-pin **engravings** are in the viewer's language while cap LABELS carry the owner's words | In scope of the mixed-language ruling |

**Nothing in the tree claims an exception beyond what a ruling grants**, and no exception was found
that had quietly widened past its scope. The two most load-bearing ones — the reveal gate and the
card-name mask — are the best-documented code in `Net/`.

---


15. **`HasDecisionNames` is the one tail guard with no clause in the extension gate.**
    `PresenceState.cs:1813-1958` builds `extensions` as an OR over 46 `Has*` terms; the tail then
    writes record 33 under `if (state.HasDecisionNames && …)` at `:2881`. A mechanical diff finds
    exactly one guard with no matching gate clause. Harmless today — `WireMandatoryNames` rides only
    the mandatory-use variant, which is sampled only when `decisionNow` is non-empty, which opens the
    gate via `HasDecisionLines` — but it is the "a cascade clears only what it lists" shape, and the
    gate and the writer should be one list.

16. **`PeerBoardFade` freezes material *property* animations on cloned surfaces.**
    `MakeTransparentClone` (`PeerBoardFade.cs:2246`) returns `new Material(m)` — a copy — while
    `RemoteBoardFurniture.PaintCap` (`:4567-4574`) and `SetTint` (`:4597`) write the **detached
    originals**. Mid-fade, a cap's assemble ramp and its state re-tint are invisible on the viewer
    and snap on release. The material-*array* seam (`SetSubmeshMaterial`) exists; there is no
    property seam. Not raised as a finding only because `Defaults.PeerBoardFade_Mode` ships `Off`.

17. **`PeerBoardFade.Restore` clears the whole property block** (`:2090`, `SetPropertyBlock(null)`),
    and `WriteAlpha` (`:1948`) does `_mpb.Clear()` then `SetPropertyBlock(_mpb)` — either would
    destroy a foreign per-renderer block. Verified safe today (`grep SetPropertyBlock src/…/Net`
    returns zero hits outside this file). Fragile by construction, not broken.

18. **`PeerBoardFade.Unfollow` (`:1042`) has no callers anywhere in the repo** — a producer with no
    consumer. Harmless (the `freeze = _engaged` path at `:1606` is what actually protects membership
    mid-fade), but it is dead surface a future reader will assume is load-bearing.

19. **`PeerCardFaceCensus.ReportPeerGone` (`:192-196`) clears `slot <= 2` only**, while `Report`
    accepts any byte slot. Safe today: the only site passing a slot is `RemoteHeldCardFace`,
    constructed with 1 and 2.

20. **`FocusCue.Phase` is a per-process clock** (`Board/FocusCue.cs:141-142`,
    `0.5 + 0.5*sin(Time.unscaledTime * 2π * 0.667)`). `AvatarTurnRing` is correct by the
    no-per-instance-clock criterion, but `Time.unscaledTime` is seconds since *this* process started,
    so the owner's board stroke and the viewer's mirrored head ring pulse at the same 0.667 Hz with
    an arbitrary offset up to 0.75 s. Whole-cue-family property (same root cause as F11). A shared
    clock already exists on the wire if the project ever wants it — `SkyAlternative.EnvClockSeconds`,
    record 31.

21. **The map-room loadout arc has no order channel at all.** `LocalRigSampler.SampleFanArcOrder`
    (`:815`) refuses with *"no hand lists this arc"* for the map loadout, `RemoteHandFan.ApplyFanArcOrder`
    (`:2804`) reorders `_handBuffer` only, and `SampleFanSource` cannot say `HeldFaceListMapLoadout`,
    so record 44 could not describe that list even if it were sampled. No in-arc reorder gesture was
    found for it (`CardFan`'s "swap" is a whole-fan exchange), so no divergence could be constructed
    — but the file's own *"reads like a defect and USUALLY IS NOT"* is a bare assertion about a
    surface with no order channel.

22. **`PlayerBadges`' `[VR]` tag is a per-viewer inference** (`:182-183`, `IsVr = playerId ==
    localPlayerId || VersionGuard.IsModdedPeer(playerId)`, and `IsModdedPeer` means "did *I* see a
    packet from them this session"). Two clients can briefly disagree about who wears `[VR]` in the
    game's own roster window. A local decoration of a game window with no owner counterpart.

23. **Two copies of one expression in `LocalRigSampler`.** `SampleHeldCardFaces` (`:320`, `:345-346`)
    and `OwnedByPresentedCharacter` (`:1114-1120`) each spell out `ItemsPile.Current?.OwnerActor` →
    `CharacterFocus.PresentedActor` when `RevealGate.InScenario`. Identical today; the file's own
    doctrine ("one expression for one fact") makes the pair a latent drift.

24. **Localization-dependent page counts fail safe, but they do fail.**
    `RemoteStorySync.HashDialog` folds `pages.Count`, and `MapChoreographer.CheckForIntroMessages`
    (`decompiled/GH.Runtime/MapChoreographer.cs:1515-1537`) terminates its page loop on
    `LocalizationManager.TryGetTranslation` failing. A locale missing one numbered line gives the two
    clients different page counts, therefore different keys, therefore a silent no-op. Depends on
    game data completeness, not on mod code.

25. **`RemoteTestTriggers`' crossing-press note is understated.** `:47-52` says two near-simultaneous
    presses mean "the later-arriving SET wins wholesale and the other press is lost". `ChangedAt` is
    a *local arrival* stamp, so each presser follows the other, both stop transmitting, and the
    shared set converges to **EMPTY** rather than to either press. The documented recovery ("press
    again") still applies.

## Additional falsified comments (avatar / story / plumbing lanes)

| file:line | the claim | what is actually true |
|---|---|---|
| `RemoteUsableFrame.cs:117-119` | "the same `[Cards] ItemCueBeatSeconds` the owner's frame **and the closed stack's rings** beat on. This is a DIAL, and it is deliberately the VIEWER's own copy of it **rather than a wire field** — see the note in `RemoteItemFan.TickUsableFrames`." | **Wrong four separate ways.** (a) The cross-referenced note **does not exist** — `TickUsableFrames` (`:1920-2000`) contains no such argument. (b) It *is* a wire field, id 161. (c) The closed stack's rings do **not** beat on the viewer's copy — `RemoteControlBoard.cs:3443` takes `tuning.ItemCueBeatSeconds`. (d) `NetProtocol.cs:647` records the opposite ruling. **= F2.** |
| `RemoteMapStory.cs:467-470` | "ModBuild 449 — THE SAME TWO-BRANCH TEST `Sample()` uses, and it has to be the same or the edge would not pre-empt." | `Sample()`'s *entry decision* is two-branch; the *bookkeeping field the getter compares against* (`:939`) is one-branch. Not the same test. **= F17.** |
| `DecisionDockSurface.cs:1478-1479` | "Cards that are open (burnt, activated, or any card once the phase ends) are not touched, so the sentence uncovers on the same tick the card does." | The enumeration omits **discarded**, which the 2026-09-07 pile ruling made open. The sentence and the card do not uncover on the same tick. **= F18.** |
| `DecisionLabelMask.cs:534-536` | "in force ONLY while `RevealGate.PeersMayNameOurCard` is false for that card — **the same predicate** the recess beside it draws its face from." | Narrowly true of the *recess*; **false of the pile FAN**, which draws from `CardFaces(Selectable, …)` whose per-card term includes `PileFrontsReach && IsDiscardedCard`. States an equality where there is a strict inclusion. |
| `RemoteAvatar.cs:29-32` | "the type-system tell: `RemoteAvatar` appears in exactly **ONE** remote-widget signature (`RemoteBoardFurniture.Refresh`)" | It appears in at least **20** signatures across 13 remote-widget files. Stale by many builds; documentation only. |

## Additional exceptions declared (avatar / story / plumbing lanes)

| file:line | what it exempts | ruling or assertion |
|---|---|---|
| `RemoteMapStory.cs:15-24` | story sync applies only to players who have the 3D world map ON | **USER RULING**, dated 2026-08-22, quoted verbatim |
| `RemoteMapStory.cs:52-58` | the "Begegnung" is POSE ONLY, never a page or a button | **USER RULING**, quoted verbatim |
| `RemoteMapStory.cs:1935-1944` | a remote pose landing outside the local usable cone is applied **verbatim, not clamped** | **USER RULING**, quoted verbatim |
| `RemoteStorySync.cs:11-17` | the story window is fully synchronised; clicking through releases the lock for all | **USER RULING**, dated 2026-08-15, quoted verbatim |
| `RemoteTestTriggers.cs:18-20` | a debug-menu event fires on every client | **USER RULING**, dated 2026-08-15, quoted verbatim |
| `RemoteTestTriggers.cs:61-67`, `:589-591` | `Drive` **ANDs the owner's wire set with the viewer's** `[Sky] Style`, `[Haunt] EasterEggs`, `[Elements] EnvironmentResponse` | **USER RULING**, dated 2026-08-15, quoted verbatim: *"Die Events können verständlicherweise nur syncen, wenn die Spieler die selbe Umgebung eingestellt haben … die lokalen Einstellungen haben Vorrang."* **This is the one place in the whole tree where a mirror legitimately ANDs the owner's bit with the viewer's dial, and it is scoped exactly as ruled** — `ResponseStrength` is deliberately NOT consulted (`:80-83`) |
| `RemoteTestTriggers.cs:592-596` | the extra haunt gate `SkyAlternative.WireStyleCode != 0` — "Not a permission, a surface" | Bare assertion, argued from `Haunt.Tick` / MR stand-down |
| `RemoteNameTag.cs:88-95` | `MinScale = 0.4f` readability floor | Bare assertion, but **clean under 1:1**: a pure function of the OWNER's `AppliedScale`, identical on every viewer, and the owner has no self tag |
| `PeerBoardFade.cs` `:221`, `:327`, `:472`, `:904`, `:932`, `:961`, `:970`, `:2318`, `:2649`, `:2732` | ten separate scope statements for the viewer-local fade | **All cite user rulings**, quoted verbatim with dates, and each superseded ruling is kept beside its replacement rather than overwritten. `:1393` ("THIS ONE STAYS INSTANT") is the nearest thing to a bare assertion and concerns a ramp-vs-snap choice, not scope |
| `RevealGate.cs` `:461`, `:498`, `:569`, `:623`, `:708`, `:758` | the six card-face populations | **All six cite user rulings, quoted verbatim with dates** |
| `RemoteMapStory.cs:1866-1888` | `UsableHalfConeDeg = 35f` is a value copy of `ModalFallback.FallbackUsableHalfConeDeg` | Bare assertion, correctly scoped — it decides only whether to LOG |

`BoardTunePages.cs`, `BoardVisual.cs`, `LocalBoardSlots.cs`, `IBoardAnchor.cs`, `PresenceState.cs`
and `PeerCardFaceCensus.cs` declare **no** 1:1 exception at all.

# WHERE MY BRIEF IS WRONG

Three corrections, stated plainly as asked.

1. **`RemoteItemCardSource` is NOT an instance of the "impossible on a watcher" class.** The brief
   names it alongside `RemoteDecisionWidgets` as an unfixed further instance. It is not: the class
   manufactures a face from **`ObjectPool.SpawnCard(item.ID, ECardType.Item, …)`** — a static,
   global, non-actor-gated factory (`decompiled/GH.Runtime/ObjectPool.cs:415`; **zero** occurrences
   of `IsUnderMyControl` in that whole file) — over item identities read from the host-replicated
   `CPlayerActor.Inventory.AllItems`. Both are available on every client. Verified twice
   independently in this review. There is no owner-only population path here.

2. **The `RemoteUseBarSymbols` case the brief describes as the model was FIXED in the commit
   immediately before the base.** `59efed69` ("a peer's decision symbols could never resolve") lands
   extension record 45 and rewrites the resolve. The brief is right that it is the archetype and
   right that `RemoteDecisionWidgets` / `RemoteItemCardSource` were not touched by it (verified:
   neither file appears in that commit's stat) — but the archetype itself is closed, and this review
   re-verified the fix's own root-cause claim against
   `decompiled/GH.Runtime/UIScenarioMultiplayerController.cs:236-247` and
   `TakeDamagePanel.cs:1102-1133`. It holds.

3. **The `FanCloseDuration` premise carried in `DESIGN-1TO1-RESIDUE.md` is stale.** Field id 156 is
   *not* "declared and deliberately not sampled" — it has ridden since ModBuild 306
   (`BoardTuning.cs:367-368`), and `check-wire-coverage.py` reports **0 PENDING debts** today. A
   contradictory comment still sits five lines below the sampling call.

One thing the brief got exactly right and that this review confirms independently: **the 2580x1080
vs 1920x1080 canvas split is real and is systemic**, not a one-off. Eleven sites cite it, including
two-machine log evidence, and it is the mechanism behind F9.


4. **`LocalBoardSlots.cs`, `BoardVisual.cs` and `IBoardAnchor.cs` are not "where a peer's board is
   placed and how big it is".** The brief assigns that question to those three files. None of them
   contains a size or placement term. `LocalBoardSlots` is an **owner-side sampler** of slot ORDER
   (`TrySampleSlotOrder`) and default-action flags; `BoardVisual` is draw-order tiering
   (`TierForDepth`, `AdoptBoardOrder`, `OrderWithPanels`) plus two material/quad factories;
   `IBoardAnchor` is the identity world-space conversion, and its own doc already records the
   falsified premise that `PlayTray._root` was the shared origin. The real terms are
   `PresenceState.BoardScale` (sampled at `NetAvatarDriver.cs:1212` as the owner's tray-root **world**
   scale) applied at `RemoteControlBoard.cs:892-909`.

5. **The brief's suggestion that a peer board's PLACEMENT is "arguably viewer-local by necessity" is
   wrong, and the code is stricter than that.** A peer board is seated at the owner's exact world
   pose through an identity anchor conversion. There is no viewer-room, eye-height or rig term in a
   peer board's position at all.


---

# THE ELEMENT TABLE

Every visible element of a peer's board, what carries it, and the verdict on the seven terms.
`✔` = verified equal to what the owner sees. A finding id replaces the tick on the term it breaks.
Terms marked `—` do not apply to that element.

## The board itself

| element | carried by | content | order | position | size | state | timing |
|---|---|---|---|---|---|---|---|
| Board surface (Oak / Steel / Bronze prefab) | extras byte A bits 5..6 + record 28 mesh pose | ✔ | — | ✔ | ✔ | ✔ | ✔ |
| Board pose / rotation / **scale** | extras `FlagHasBoard` block | — | — | ✔ | ✔ owner's `BoardScale` | ✔ | ✔ one easing rate, snapped on first apply |
| Board dimensions `BoardW`/`BoardH` | three declarations, pinned by `check-mirrors.sh:62-63` | — | — | ✔ | ✔ **pinning re-verified; no new unpinned copy** | — | — |
| Dock seats (objectives, elements, initiative, piles, active, readout, banner, tooltip) | record 28 via `RemoteBoardLayout` | — | ✔ | ✔ `PlayTray.<X>MountBase + tuning.<X>Offset` | ✔ | — | — |
| Whole-board suppression outside a scenario | `RemoteBoardScenarioGate` | ✔ byte-identical to the local board's own existence predicate | — | — | — | ✔ | ✔ |
| Board see-through while occluding | `PeerBoardFade` | — | — | — | — | viewer-local **by ruling** | viewer-local **by ruling** |
| Inertness (no raycast, no collider) | `StripColliders` + per-builder discipline | ✔ holds — but see falsified comment on "runtime guard" | — | — | — | — | — |

## Cards

| element | carried by | content | order | position | size | state | timing |
|---|---|---|---|---|---|---|---|
| Hand fan: slab count + arc | record 6 | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Hand fan: **arc order** | record 44 + latch, belted by `ValidateFanArcOrder` | ✔ | ✔ every refusal fails closed to the game's order | ✔ | ✔ | ✔ | ✔ |
| Hand fan: pose, billboard, follow rate | record 28 + owner's palm/head | — | — | ✔ **owner's head, verified** | ✔ | ✔ | ✔ incl. the rigid `FanFollowSmoothing == 0` branch |
| Hand fan: per-card layout (arc, roll, curve, toe-in, depth bow, gaze relief) | record 28 + owner's head pose | — | — | ✔ | ✔ | ✔ | ✔ `_gazeSmoothing` now the owner's |
| Hand fan: open / close / swap / hover split / pop | record 28 durations + record 6 index + record 22 | — | — | ✔ | ✔ | ✔ | ✔ |
| Hand fan: fronts vs backs | local resolve + `RevealGate` | ✔ | — | — | — | ✔ | ✔ |
| Card dust | record 28 id 233 only, via `CardDustFx.Permission.OwnerAlreadySaidYes` | ✔ **the 2026-08 precedent defect is correctly closed** | — | ✔ | ✔ | ✔ | ✔ |
| Card plume | record 236 only | ✔ | — | ✔ | ✔ | ✔ | ✔ |
| Round-card recess: face / back / occupancy | record 4 nibble + record 18 order + record 39 + `RevealGate.CardFaces` | ✔ | ✔ compaction belt refuses on length disagreement | ✔ | ✔ | ✔ | ✔ |
| Round-card recess: materialise / crumble | `VRCard` constants referenced not copied, unscaled clock | — | — | ✔ | ✔ | ✔ | ✔ |
| Round-card recess: half hover / selection | record 14 via the shared `ActionHighlightDriver` | — | — | ✔ | ✔ | ✔ | ✔ |
| Round-card recess: spent halves | record 41 + the game's own `ToggleSideInteractivity` | — | — | — | — | ✔ hold taken at the write | ✔ |
| Round-card recess: used-card FX ramp | local resolve, 2 s | — | — | — | — | ✔ | ✔ |
| **ACTIVE column: the burnt/grey wash** | local resolve + `FxSurface.Active` | **F1** | ✔ | ✔ | ✔ | **F1** | **F1** |
| **HELD card: the burnt/grey wash** | record 36 + `FxSurface.Held` | **F1** | — | ✔ | ✔ | **F1** | **F1** |
| Held card: silhouette / front / body box | record 36 + length belt | ✔ | ✔ | ✔ | ✔ owner's slab scale | ✔ | ✔ |
| Active matrix: cells, layout, held-seat blanking | zero wire + record 28 + record 36 | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Active-card pulse | zero wire, `ActiveCardSet.ActiveHalves` | ✔ | — | ✔ | ✔ | ✔ (NOTE 3) | phase not locked (F11 family) |
| Card flights: anchors, curve, size ramp, rotation, face | record semantic ids + shared `RemoteFlightCurve` | ✔ | — | ✔ | ✔ | ✔ | ✔ `CardFxSeconds 0.4f == FlyToPileSeconds` |
| **Burn: the HOLD** | local resolve off `LostAbilityCards` | ✔ | — | ✔ | ✔ | ✔ | **F5** |
| Burn: the ARC | same | ✔ | — | ✔ | ✔ | ✔ | ✔ |
| Pile fronts (discard / burnt / items) | local resolve off host-replicated model | ✔ | ✔ zip belted by length agreement | ✔ | ✔ | ✔ | ✔ |

## Furniture and readouts

| element | carried by | content | order | position | size | state | timing |
|---|---|---|---|---|---|---|---|
| Keycaps: seat, size, shape, depth, travel, tint, label colour | record 28 ids 81..100 / 228..232 / 48..53 / 170 | ✔ | — | ✔ | ✔ | ✔ | ✔ |
| Keycaps: show/hide, press, four state colours | record 4 bytes 1/2 + record 14 bits 3..7 | — | — | — | — | ✔ | ✔ per-board `CapAnim`, **verified per-instance end to end** |
| Keycap wordings | record 13 | ✔ owner's rendered words | — | — | — | ✔ | ✔ |
| Board engravings | `Loc` keys + record 28 nudges | ✔ viewer's language **by ruling** | — | ✔ | ✔ | — | — |
| FOLLOW/PIN toggle | record 4 byte 1 bit 2 | ✔ | — | ✔ | ✔ | ✔ | ✔ |
| **Slot glows (wanted pulse, snap rim)** | record 4 mask / hovered slot | **F4** (material) | — | ✔ | ✔ | ✔ | **F11** (phase) |
| Item-USE berth + caption + reveal/collapse | record 4 + record 13 + record 28 ids 70/80/166..169 | ✔ | — | ✔ | ✔ | ✔ | ✔ logical edge, so the animation starts when the owner's does |
| Pile stacks (slabs, count, caption, greying) | record 15 + record 28 id 70 | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| **Items "usable" cue — closed stack** | record 4 byte 2 bit 7 + record 28 ids 161..165 | ✔ | — | ✔ | ✔ | ✔ | ✔ **owner's beat** |
| **Items "usable" cue — item-fan frames** | record 35 | ✔ which chips | ✔ | ✔ | ✔ | ✔ | **F2 owner's beat ignored** |
| Round readout / initiative readout | local resolve (global + per-actor, gated) | ✔ | — | ✔ | ✔ owner's own literal | ✔ gate is vanilla's rule verbatim | ✔ |
| "INI" badge | local, fallback-only stand-in | declared exception, correctly scoped | — | ✔ | ✔ | ✔ | ✔ |
| Focus outline (board frame + avatar ring) | record 22 | ✔ | — | ✔ | ✔ | ✔ | ✔ shared `FocusCue` palette |
| Exhausted-board card clear | `RemoteBoardFocus` | ✔ | — | — | — | ✔ | ✔ |
| **Board tooltip** | record 9 (text only) | **F7** (style) | — | **F16** (corner) | **F7** (box) | ✔ | ✔ |

## Docked widgets

| element | carried by | content | order | position | size | state | timing |
|---|---|---|---|---|---|---|---|
| Decision row: which options stand | records 29 + 24 | ✔ | ✔ one sampler walk, index-aligned | ✔ | ✔ | ✔ | ✔ |
| Decision row: option art / icons / wordings | local resolve (receiver's own serialized prefab fields) | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Decision row: greyed / dimmed / chosen | record 24 | — | — | — | — | ✔ game's own `ColorBlock` precedence | ✔ |
| Decision row: hover / press GROW | record 24 bits 3-4, per-frame `TickPointer` | — | — | — | ✔ | ✔ | ✔ **fold key verified collision-free**, 2 bits × ≤8 options = 16 bits |
| Decision row: hover / press TINT | same | — | — | — | — | ✔ | **F13** (snap vs cross-fade) |
| Decision row: base colour | live source `Graphic.color` snapshot | **F15** (double antique tint) | — | — | — | ✔ | ✔ |
| Decision row: label colour | record 28 id 48 → `LabelColorFor(TheBoardOwnersDial, …)` | ✔ **owner's dial, seam fully taken** | — | — | — | ✔ | ✔ |
| Decision row: **seat / height** | local measure | — | — | **F10** (viewer-language TMP height) | ✔ 720x48 both machines | ✔ | ✔ |
| Decision prompt text | record 23 variant + record 33 keys | ✔ | — | — | — | ✔ | ✔ |
| Dialog-popup option row | record 12 wordings + record 24 states + receiver's own prefab | ✔ owner's language **by ruling** | ✔ index identity | ✔ | ✔ gap from the game's own layout spacing | ✔ | ✔ |
| Use-bar symbols — bars 0 & 3 | **record 45** (`UseBarSlotIdentity`) | ✔ 16-bit fold of the game's own cross-machine identity; unique-match-or-refuse | ✔ | ✔ | ✔ | ✔ | ✔ |
| Use-bar symbols — bars 1 & 2 | local resolve | ✔ premise **verified**: `CardsActionControlller.cs:598-607` raises the bar on every client, no ownership gate | ✔ | — | — | ✔ | ✔ |
| Use-bar drawer structure / per-slot state | record 25 | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ states per frame |
| Objectives panel | local resolve (global widget) + record 28 id 129 | ✔ real cloned widget | ✔ | ✔ | ✔ **column verified term-for-term against the owner's product** | ✔ | ✔ `TickLive` per frame |
| **Scenario rules panel** | same | ✔ | ✔ | **F6** | **F6** | ✔ | ✔ |
| **Initiative track / element board fit** | local resolve via `TryDockRect` | ✔ | ✔ | ✔ | **F9** (plausible) | ✔ | ✔ |
| Initiative track: entries, enemy order, reorder slide, grayscale, dead, extra turn | mirrored widget, zero wire | ✔ all host-replicated | ✔ | ✔ | ✔ | ✔ | ✔ |
| Initiative track: player block order | record 27 | — | ✔ permutation of slots read off the clone; bails whole on set mismatch | ✔ | — | ✔ | ✔ |
| Initiative track: hover | record 16 | ✔ | — | ✔ local hover forced to vanilla's rest pose, peer's re-applied | ✔ | ✔ | ✔ |
| Initiative track: vanilla selection frame | record 23 | ✔ | ✔ | — | — | ✔ forced off on every clone, re-enabled only for wired ids | ✔ |
| Initiative track: focus / at-turn / selection-ready rings | record 22 + 27 + record 28 id 238 | ✔ | — | ✔ | ✔ | ✔ | ✔ blink consts, not dials — one clock everywhere |
| **Element strip: the picture** | `RemoteWidgetMirror` per-frame | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| **Element strip: mod-drawn fallback** | local | ✔ | **F14** (plausible) | ✔ | ✔ | ✔ | house curve, declared |
| Element strip: creating / reserved / available masks | local resolve, zero wire | ✔ **the "un-replicable" debt is FALSE — re-verified today** | ✔ | ✔ | ✔ | ✔ | ✔ |

## Transient fans and placards

| element | carried by | content | order | position | size | state | timing |
|---|---|---|---|---|---|---|---|
| Item fan: geometry, open/close, hover lift, split | record 28 ids 70/79/158/198 + record 6 + eight `ItemFan*` dials | ✔ | ✔ | ✔ **owner's head** | ✔ | ✔ | ✔ all eight pinned in `check-remote-defaults.py` |
| Item fan: spent roll, use-recess clip, held-chip removal | local model + record 26 + record 36 | ✔ | ✔ | ✔ | ✔ | ✔ | ✔ |
| Browse fan: shape, emerge, collapse, return glide | record 28 ids 79/157/158-160/198-200 | ✔ | ✔ | ✔ **owner's head** | ✔ | ✔ | ✔ collapse is a code literal on both sides |
| Pile arcs: hover pivot | record 6 | — | ✔ **same expression `HighlightedIndex` reports — verified against `ItemsPile.cs:1785`/`PileBrowser.cs:215`** | ✔ | ✔ | ✔ | ✔ |
| Empty-fan placard | record 14 byte 1 bit 4 | ✔ viewer's language **by ruling** | — | ✔ owner's palm + owner's offset | ✔ owner's `CardWidth` | ✔ | ✔ owner's fade constants, called not copied |
| Empty-fan placard: FACING | local head | — | — | declared exception, NOTE 10 | — | — | — |
| Pick banner | record 7 | ✔ owner's rendered words | — | ✔ | ✔ grows from the drawn text as the owner's does | ✔ | ✔ (codec cap alerts) |
| Fan enhancement refresh | zero wire, reacts to the game's own forwarded action | ✔ | — | — | — | ✔ | ✔ no blink |
| **Peer map placard (3D map room)** | local clone + record 20 + record 28 id 180 | ✔ | ✔ | ✔ same `HoverCardPose.Place` | **F3** | ✔ | ✔ |
| **Shared windows (map story / quest confirm / encounter)** | record 21 + host-decided yaw | ✔ | ✔ | **F8** until dragged | ✔ frozen by `SharedWindowSizeLaw` | ✔ | ✔ page/finished pre-empt the rate gate |


## Avatar, story and shared surfaces

| element | carried by | content | order | position | size | state | timing |
|---|---|---|---|---|---|---|---|
| Head mask identity | rig packet byte 7 `MaskId` | ✔ owner's dial | — | — | — | ✔ | ✔ |
| Head mask **size** | extras block byte A bit 4 `MaskSizeCode` | — | — | — | ✔ **owner's `[Net] MaskSize`, verified end to end**; written to the `HeadVisual` CHILD so the rig scale cannot stomp it | ✔ | ✔ |
| Head pose | rig packet head block, 15 Hz | — | — | ✔ world frame, uniform 67 ms lag | — | ✔ | ✔ no per-viewer term |
| Hand style / scale / pose / finger curls | rig packet + extras record 1 + record 28 ids 201-204 | ✔ | — | ✔ | ✔ owner's | ✔ | ✔ curl DEGREES are the owner's too |
| Ghost hand fade | extras `GhostSidesMask` / `GhostStrength` | — | — | — | — | ✔ owner's strength | ✔ |
| Held card slabs 1 & 2: pose | rig `FlagHeldCard` + extras record 10 | — | — | ✔ same easing constant as the head, so no relative drift | — | ✔ | ✔ record 10 promoted to rig rate while moving |
| Held card: size | `AppliedScale × (ownerCardWidth/Default) × ownerInspectScale` | — | — | — | ✔ three owner terms, no viewer term; the `HeldCardSizing` seam has both call sites | — | — |
| Held card: rigid vs billboard | extras record 34 | — | — | ✔ | — | ✔ absence = "billboard both", written unconditionally | ✔ |
| Held figure / second figure / held props | rig `FlagHeldFigure` + records 8, 30, 37 | ✔ | — | ✔ | ✔ | ✔ | ✔ |
| Turn ring over the mask | record 22 via `CharacterFocus.MarkForPeer` | ✔ | — | ✔ | ✔ parent-local, scales with the row | ✔ | 0.667 Hz shared rate; **cross-machine phase is the F11 family** |
| Name tag (avatar + username) | the GAME's own replicated registry, zero wire | ✔ no second source of truth | — | ✔ | ✔ owner's `AppliedScale` | ✔ | ✔ `[Net] NameTags` is viewer-local and **the owner has no counterpart tag** ⇒ nothing to be 1:1 with |
| Owner tag on a peer's board corner | same registry, board-local seat | ✔ | — | ✔ inherits the peer's board pose+scale | ✔ | ✔ | ✔ billboard is necessarily viewer-local, no owner counterpart |
| **Voice badge** | local voice state | **F19** | — | ✔ | scale = approved exception | **F19** | **F19** |
| `[VR]` roster badge | local `VersionGuard.IsModdedPeer` | local decoration of a game window, no owner counterpart | — | — | — | — | — |
| Scenario story page | record 19, ABSOLUTE page + finished bit + content key | ✔ key is FNV over the loc KEY + speaker guid, never the translated title | ✔ | — | — | ✔ terminal latch verified released | ✔ |
| Scenario story window pose / size | record 19 pose block, seat-anchor-local REAL metres | — | — | ✔ divided by the sender's `WorldScale`, re-multiplied by the receiver's | ✔ quantised factor through `SharedWindowSizeLaw` — design constants, **no viewer dial** | ✔ | ✔ |
| Map story page | record 21 kind 1 | ✔ same pure `ResolveStoryPage`, same shared key function | ✔ | — | — | ✔ | **F17** (the send cadence, not the picture) |
| Map / quest / encounter window pose | record 21 kinds 1/2/3 + frame byte | — | — | **F8** until dragged; otherwise ✔ (frame written as a byte so the receiver decodes in the sender's frame) | ✔ | ✔ | ✔ page + finished pre-empt the rate gate |
| Quest / encounter content | the GAME's own `SelectQuest` / `ContinueRoadEvent` | ✔ no second channel — verified the receive path has no page arm for kind 3 at all | ✔ | — | — | ✔ | ✔ |
| Debug test-trigger override | record 32 (latch only) | ✔ | — | — | — | ✔ | ✔ the viewer-dial AND here is the **ruled** exception |
| Wire round-trip | `AvatarSerializer` | ✔ max 127 ≤ `MaxSize` 132; quaternion re-normalised both ends; `WorldScale` NaN/Inf/≤0 guarded on write **and** read | — | — | — | — | — |
| Extras framing | `NetPacket` + `PresenceSerializer` | ✔ magic/version/length checked before any parse; 42 ids declared, 42 written, 42 read — **no orphan in either direction**; worst case 1747 ≤ `MaxSize` 2100, margin 353 | — | — | — | — | — |

## The reveal gate — both directions

| question | verdict |
|---|---|
| Surfaces the gate serves | recesses, hand fan, held card, burn flight, card-FX flight, active matrix, the three pile fans, initiative track, board visibility; and on the SENDER side records 12/13/33, tooltips, the local battle-goal line and the focus feature |
| Scoped to the agreed exceptions? | **Yes, plus one that is not scope creep**: `ShowBattleGoal` / `ShowPersonalQuest` are the game's own rules reproduced verbatim (`ActorStatPanel.cs:557/566`, `BattleGoalContainer.cs:44/73/81`) — i.e. the "geheime Quest" exception |
| Too LOOSE anywhere? | **No.** Every non-public population routes through `ShowRoundCardFronts`; every widening is per-card and every `catch` fails closed (face → `None`, wording → withhold the whole row, pile → backs) |
| Too STRICT anywhere? | **Yes — F18**, the missing `IsDiscardedCard` term in the NAME gate |
| "Discard and burnt show FRONTS, immer ohne Ausnahme" | **Holds on every serving surface.** Burnt is answered by `IsPubliclyRevealedCard` walking both Lost lists, which is NOT gated by `PileFrontsReach`, so it opens for every population. List mapping verified at `decompiled/ScenarioRuleLibrary/CCharacterClass.cs:486/491/496` |
| "A burning/active/lost card shows its front in EVERY phase" | **Holds.** `CardFaces` re-asks as `AlreadyPublic` *after* the population verdict returns `None`, so the phase term cannot suppress it |
| The two `PileFrontsReach` exclusions (`SacrificedCard`, `BoardPickSeat`) | Correct, not over-strict — a sacrificed card is still merely *discarded* while it lies in the recess, and the moment `FinalizeShortRest` moves it to `LostAbilityCards` the burn exception opens it |

## Record 28, field by field

Diffed mechanically, sampler against reader:

- **131 field ids written, 131 read. Zero written-but-not-read. Zero read-but-not-written.**
- **Wrong-`Defaults`-entry pairs: 0.** Thirteen pairs differ textually only by a type conversion of
  the same entry; all round-trip correctly, including the two `Quantized` scalings.
- **Wire fields with no consumer: 0.** Four properties have no direct external reference and all
  four are consumed through a computed accessor or are declared log-only.
- **Sampled but ignored by the mirror: 0 inside record 28.** The two dials that are on the wire, read
  back, and then *not used by the consumer that needs them* are `ItemCueBeatSeconds` (F2) and
  `CanvasScaleMm` (F3) — both cases of a mirror reading the viewer's copy instead.
- Paging is sound: `Publish` refuses over `BoardTuneMaxFieldBytes`/`BoardTuneMaxFields` (247 ≤ 255,
  so the byte header cannot truncate), `FieldsWellFormed` re-derives the count rather than trusting
  the header, and a generation change resets the seen set.
- Guards run today: `check-tune-fields.py` — *145 declared ids all inside a declared width range;
  `BoardTuning.Sample` writes 131 of them in strictly ascending order.*

---

# WHAT WAS CHECKED AND FOUND CLEAN

Stated positively, because most of this board is right.

**The two named dial seams are fully taken.** `Cards.CardDustFx.Permission` — every mirror call site
passes `OwnerAlreadySaidYes` (`RemoteBoardCard.cs:2291-2292`), so the 2026-08 card-dust defect that
gave the project this pattern is genuinely closed. `WorldUI.NativeButtonSkin.LabelOwner` — both
mirror call sites pass `TheBoardOwnersDial` (`RemoteBoardFurniture.cs:3112`,
`RemoteDecisionWidgets.cs:1263`), and the only remaining `NativeButtonSkin.LabelColor` readers are
genuinely local surfaces (`MapButtonRail`, `DecisionDockSurface`). A third dial needs the same seam
and has not got one — that is F2.

**No mirrored geometry reads the viewer's screen, canvas scale factor, rig scale or eye height.**
Swept `Net/` for `Screen.width`, `Screen.height`, `scaleFactor`, `referencePixelsPerUnit`,
`EyeHeight`, `RigTarget`, `lossyScale`, `Camera.main`. Every `lossyScale` read in `Net/Remote/` is on
the mirrored board's own transforms — which already carry the owner's `BoardScale` — or is
measurement-only. The single viewer-rig read (`RemoteMapRoom.cs:1275`) is a lane LIFT in real metres,
which must be converted by the local rig. The single `Camera.main` (`RemoteCardArt.cs:909`) is a
world-space canvas's `worldCamera` and carries no pose. The only canvas-derived size term that
survives is F9, and it has a one-grep falsifier.

**A peer's board pose and scale are the OWNER's, not the viewer's — stricter than my brief assumed.**
`boardScale` is sampled as the owner's tray-root **world** scale (`NetAvatarDriver.cs:1212`), so the
owner's rig scale is baked in; `_owner.BoardPosition`/`BoardRotation` are the owner's exact world
pose through an identity anchor conversion. There is no viewer-room term in a peer board's pose at
all.

**`BoardW`/`BoardH` are still pinned and no new copy has appeared.** Exactly three declarations, all
`0.64f`/`0.32f`, all three listed in `check-mirrors.sh:62-63`, and the script passes.

**All three mirrored fans face the OWNER's head**, not the viewer's — `_owner.HeadHolder` at
`RemoteHandFan.cs:4576/4714`, `RemoteItemFan.cs:832`, `RemoteBrowserFan.cs:1211`. Only
`RemoteEmptyFanHint` uses the local head, and it declares that (NOTE 10).

**The pile arcs' hover pivot was fixed correctly.** Both mirrors take the pivot from the same
expression `HighlightedIndex` reports, verified against `ItemsPile.cs:1785-1791` and
`PileBrowser.cs:215`, with the owner's own split gains matching term for term.

**The per-board cap animation clock is genuinely per-board.** `RemoteBoardFurniture.CapAnim` is a
`readonly struct` field seeded from `tuning.ButtonAnimOn` / `ButtonAppearParticles` /
`ButtonAppearSeconds` / `ButtonDisappearSeconds`; all four wire ids (234/235/174/175) are written and
read; every cap construction passes it; `RemoteCapFx` holds it per instance. The old shared statics
survive only as a named fallback. **The 2026-08 residue's renderer-first warning was honoured.**

**The three element masks really are replicable, and the debt that said otherwise really was false.**
Re-verified today against `UIUseAugmentationsBar.cs:129/326`, `CardsActionControlller.cs:598-607` and
`UIUseItemsBar.cs:589-594`. `DESIGN-1TO1-RESIDUE.md` §6 is correct on this point.

**Record 45 closed the maintainer's item 8 correctly.** The root-cause claim was re-verified against
`decompiled/GH.Runtime/UIScenarioMultiplayerController.cs:236-247` and `TakeDamagePanel.cs:1102-1133`
— `ShowOtherPlayer` raises neither bar, only `Show` reaches them. The fix is a fold of the game's own
cross-machine identity with unique-match-or-refuse, and the two sources are never mixed inside one
bar. Bars 1 and 2's local resolve was independently verified sound.

**No record has drifted across the mixed-language line.** Every match KEY on the wire is a
localization key, YML id or guid — `HashMapKey`, `RemoteStorySync.HashDialog` (verified against
`DialogLineDTO.cs:25/40/45/52/69`: it folds `.text`, the key, and `.character`, a guid, never
`title`), record 33, record 21's content keys. Every travelling STRING is the sender's own rendered
words. The one consequence that escapes the ruling is F10, and it is about geometry, not text.

**The puppet discipline holds.** `RemoteWidgetMirror`'s whitelist was checked for silently-dropped
presentation: `Graphic` covers every TMP text *and* `TMP_SubMeshUI`; `CanvasRenderer`, `Mask`,
`RectMask2D`, `BaseMeshEffect` and `CanvasGroup` all survive; the assembly-identity test on
`IsStockLayout` closes the "a game script subclasses `LayoutGroup`" hole; and pairing is done
**before** activation so TMP's sub-mesh spawn cannot offset the two walks. No animator, mask or
submesh is silently dropped.

**The decision row's hover/press fast path really does run per frame**, and its fold key really is
collision-free — 2 bits × ≤8 options = 16 bits, and it folds the *states* array so a role-less
`DialogPopup` is not silently gated off. The five-bit packing defect that gave the project this
lesson is fixed.

**`PeerBoardFade` stays inside its ruling.** It never writes a transform, parent, rect, mesh, count or
ordering index; its whole write surface is `forceRenderingOff`, an alpha property block, and
`CanvasGroup.alpha`. `Restore` puts back the value captured at engage — the "a hide saved a foreign
value" shape was hunted for by enumerating all 17 `sharedMaterial =` writes in `Net/Remote/` and does
**not** reach today. All seven `Follow` registrations are board content or deliberately-excluded
hand content.

**Wire framing is sound.** `NetPacket.PeekType` rejects foreign/short buffers before any parse. 42
extension ids declared, 42 written, 42 read, no orphan in either direction. `AvatarSerializer` max
127 ≤ 132; `PresenceSerializer` worst case 1747 ≤ 2100 with a 353-byte margin, and the "add your
worst case in your own commit" rule is being followed. One tail-gate asymmetry (`HasDecisionNames`)
is harmless today and recorded in NOTE 15.

**All five shipped guards are green today** — `check-remote-defaults.py`, `check-wire-coverage.py`
(0 PENDING debts), `check-mirrors.sh`, `check-tune-fields.py`, `check-desync-surface.py`.

---

# CLOSING

Nineteen findings: **twelve CONFIRMED, six PLAUSIBLE, one confirmed-mechanism-with-a-user-question
(F18).** Nothing was manufactured; four candidates were dropped after the decompiled source or a
two-machine log falsified them, and they are recorded in NOTES rather than promoted.

**The two worth acting on first are F1 and F2.** F1 is a regression introduced by the commit under
review, needs no dial to reproduce, kills two of six card-FX surfaces outright, and already ships its
own falsifier line. F2 is a breach of a ruling recorded in this very tree, was found independently by
three of the six passes, and the fix is a parameter plus a rebuild-on-revision-edge.

**The systemic lesson of this round is the checker gap.** Every guard is green and most of these
findings are structurally invisible to all of them, because they all answer *"is this value declared
in one place / on the wire"* and none answers *"at runtime, whose copy does the mirror read."* The
project has invented the right shape twice — `CardDustFx.Permission` and `NativeButtonSkin.LabelOwner`
— and applied it correctly twice. A lint over live `ConfigEntry.Value` reads reachable from
`Net/Remote/` would have caught F2 and F3 the day they were written.

**The second lesson is the comment.** Nineteen in-source assertions were falsified this round, and
**six of them describe or protect a live defect** — including one three-line comment
(`RemoteUsableFrame.cs:117-119`) that is wrong in four separate ways, one of which is a cross-reference
to a note that does not exist.
