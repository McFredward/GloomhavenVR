# GSD State

- **Milestone:** v0.1 (first playable VR release)
- **Position:** **Hardware iteration loop, multiplayer-capable.** Current build:
  **`NetProtocol.ModBuild = 148` shipped, bundle 67,064,834 bytes — awaiting its hardware run.** Rounds are run as parallel agents on
  disjoint file sets; every diff reviewed before merge, cross-file changes applied by the integrator.
- **Last update:** 2026-08-14

---

## 0. READ THIS FIRST (successor briefing)

This file is the handover. Everything below is written for someone who has never seen the
project before and has to be productive in the first ten minutes.

### The one-paragraph description

A BepInEx 5 + HarmonyX mod that turns *Gloomhaven Digital* (Unity 2021.3.5f1, Mono, net472)
into a room-scale VR game on a Quest 3 over Virtual Desktop (VDXR), MultiPass stereo, 90 Hz
(11.1 ms). The board becomes a physical diorama on a table; the player's hand holds a real
card fan; the game's own 2D UI is *converted* into world-space panels docked on a wooden
control board. **Multiplayer is supported and is a first-class constraint, not an
afterthought** (see §3).

### The user

Frederik (`frederik@lissek.info`). **Always answer in German.** Worker/agent prompts may be
English. He tests on real hardware every round and reports numbered findings; each round is
"read the reports → dispatch workers → review their diffs → merge → bump → push → write him a
German report".

### Where the truth lives

| Question | Authority |
|---|---|
| What is patched, by whom | `docs/PATCH-INVENTORY.md` (**generated** — `scripts/patch-inventory.sh generate`) |
| Why a patch exists, what it costs | `docs/PATCH-NOTES.md` (hand prose, not checked) |
| Architecture, module boundaries | `.planning/ARCHITECTURE.md` |
| Refactor rules, invariants | `.planning/refactor/CHARTER.md`, `INVARIANTS-*.md` |
| Frame-order contracts | `.planning/refactor/FRAME-ORDER.lock` |
| Performance measurements | `.planning/perf/FINDINGS.md`, `.planning/perf-zoomed-out.md` |
| Multiplayer design | `.planning/multiplayer/PLAN.md`, `PLAN2.md`, `RESEARCH.md` |
| Parked/abandoned work | `.planning/static-batching-removed.md`, `.planning/wall-fade-stereo-rivalry.md` |
| Anything user-facing | `src/GloomhavenVR/Core/Loc.cs` (+ `Loc.Config*.cs`) |
| Every shipped default | `src/GloomhavenVR/Defaults/*.cs`, one annotated line each |

**The code is the documentation.** This project deliberately writes long block comments that
state the user report verbatim, the root cause, the evidence and the rejected alternatives.
When you change something, write that comment — several rounds have been saved by a comment
that said "do NOT swap this back, here is why".

---

## 1. Hard rules (violating any of these is a defect, not a style choice)

1. **Mod-side only.** Never modify game data. `ressources/` is a READ-ONLY symlink to the
   installed game (`Managed/*.dll`). Never `git add` it or `libs/`. Decompiled reference
   sources are under `decompiled/` (and `decompiled-demeo/`), also never edited.
2. **`NetProtocol.ModBuild` +1 on every build handed to another player.** It is the
   multiplayer handshake key; mismatched peers get a blocking dialog. Write a full build note
   next to it — the note history in `NetProtocol.cs` is the project's real changelog.
3. **Wire:** magic `GVR1`, `Version` byte stays **3**, all changes **additive TLV** only, old
   readers skip unknown records by length. **Card identity NEVER goes on the wire, ever** —
   reveals go only through `Net/RevealGate.cs`. Records 1–17 and 22–29 are used; 30+ free. The
   extras flag byte is FULL and record 4 has no free bits: claim a NEW record rather than
   squeezing (the reservation comment in `NetProtocol.cs` records a past double-claim
   incident).
4. **Lights are never hidden or written by any visibility system. Figures are never touched by
   any wall/visibility system.**
5. **Everything that fades or moves does so WITH the animation.** Popping is unacceptable.
   Exception granted by the user for one case only: the decision area's *collapse* is instant.
6. **The user's approved look outranks geometric or technical correctness.**
7. **Localisation:** every user-facing string via `Core/Loc.cs`, German **and** English.
8. **Config:** every default on one annotated line in `src/GloomhavenVR/Defaults/` with a
   `// => [Section] Key` comment. A new KEY must end in a unit word `ConfigSteps` recognises —
   an unrecognised suffix silently falls back to "a fiftieth of the shipped default" and
   produces an unusable stepper. This has been user-reported **twice**; there is now a test
   (`tests/GloomhavenVR.WireTests/ConfigStepVectors.cs`) that pins it.
9. **Never mention remaining context or advise a fresh session** (the readout is a harness
   bug). Do not talk about token budgets.

## 2. The five gates — every change must pass all of them

```
./scripts/build.sh                        # 0 errors (6 pre-existing nullability warnings are OK)
bash scripts/wire-tests.sh                # currently 1441 assertions
./scripts/refactor-guard.sh check --summary
python3 scripts/rebase-defaults.py check
python3 scripts/check-wire-coverage.py
```

Reading `refactor-guard`: only the **five gate lines** matter (patch surface, frame order,
mirrors, remote defaults, bundle). Its **exit code is 1 whenever the compiled form differs
from the stored baseline**, which is normal after any change — that is a report, not a gate.
The baseline lives in gitignored `.planning/refactor/.guard/` and can be regenerated from a
clean tree with `./scripts/refactor-guard.sh baseline`.

`refactor-guard` hard-fails if `docs/PATCH-INVENTORY.md` is stale. Fix with
`bash scripts/patch-inventory.sh generate`.

**Bundles are built ONLY with `/home/claw/unity-2021.3.5`** — not `unity-2021.3`. The wrong
editor produces a bundle that silently loads nothing.

## 3. The multiplayer 1:1 ruling (user, verbatim, standing)

> "generell gilt die Regel, das man alle Interaktionen, Animationen und Anzeigen des
> Controllboards in MP auch synchronisieren soll. Die einzige Ausnahme ist hier die geheime
> Quest des characters und während der Auswahlphase die tatsächlichen Oberseiten der Karten.
> Ansonsten soll alles 1:1 übertragen werden."

Narrowed and extended since:
- Card **fronts** of a remote player are visible in every phase **except** card selection.
- The **initiative NUMBER** stays masked to "?" during selection.
- The personal quest / battle goal is never mirrored.
- **The tuning guarantee:** "Ändert ein Spieler also die Positionen für sich selber, so sollen
  alle anderen diese Position bei seinem board auch sehen." Board-affecting dials therefore
  ride **record 28**, which is range-paged (a full lap is a snapshot; convergence ≤ 400 ms at
  two pages). `scripts/check-wire-coverage.py` fails the build when a board-affecting dial has
  neither a record-28 field nor an annotated EXEMPT.

**The trap to avoid** (it has been hit): a wire field with *no consumer*. `check-wire-coverage`
would call it covered while the peer still sees no difference. See the `[Cards]
FanCloseDuration` note in that script.

## 4. Where the project stands — recent rounds

Newest first. Each entry names the *root cause*, because that is what generalises.

- **ModBuild 186** (bundle UNCHANGED — plugin DLL only) — one missing set membership, two reports,
  and the flicker turns out not to be a stereo bug at all.
  * **(2)+(3) are one bug: a floated window fell out of `OpenWindows`.** 185 fixed ONE of the two
    exclusions that the mod's own conversion makes true, so the loop survived — 1417 float/release
    lines. The other is three lines further down the same method: **the world-space test**
    (*"never float world-space UI — it is already visible in VR"*) matches because **our** conversion
    put the root canvas in WorldSpace. Patching a second individual test would leave a third, so the
    repair is on the invariant: `OpenWindows` means "the windows the VR layer is presenting", and a
    window we float **is in that set by definition** — re-added unconditionally, ahead of every
    "already handled elsewhere" question. **That is also why the merchant came up twice**: a sticky
    window survives leaving the set, so the shop window stayed floated while dropping out of it, and
    `AncestorWillBeFloated` asks exactly that set.
  * **(3) second half — convert the PANEL, not the window.** The log settles the hierarchy: the
    mod-layer sweep moved **56** transforms for `UI Shop Item Window` and **1998** for `Scroll View`.
    The sweep walks the whole subtree, so 56 cannot contain 1998 — **the item list is a sibling, not
    a child.** A guildmaster destination now converts the nearest common ancestor of its window and
    everything its own component **references** outside its subtree (read off serialized fields —
    the game's own statement of ownership). One host, both halves, flat layout verbatim: nothing
    re-parented, no anchors touched. Bounded: the ancestor is refused if it is the root canvas or
    >6× the window's area, and both outcomes are logged with the numbers.
  * **(1) THE FLICKER IS NOT A STEREO BUG — five builds hunted the wrong animal.**
    `CameraOrderProbe` logged **one** order shape for the whole session:
    `MapCamera[→RT] → UI Camera[→RT] → GUI 3D Camera[→RT] → Head[Left] → Head[Right]` — **zero
    cameras between the eye passes.** Every RT is finished before either eye starts, so **both eyes
    sample the same pixels**; the probe's own correction never fired because there was nothing to
    correct. With `PanelFlickerProbe` silent across two sessions, the conclusion is forced: the
    flicker is **temporal and identical in both eyes**. His narrowing confirms the subject — the 4
    portraits (sprites) are fine, the live render (`RawImage` on
    `Character 3D assembly render texture`) is not. New `WorldUI/RenderTargetProbe` watches that
    texture and its writing camera and reports any field going **A-B-A** across three ticks. The
    game's own code supplies the candidates: `Character3DDisplayManager.Display` flips
    `beautify.enabled` (a full-screen image effect), `Character3DDisplayCameraSettings` writes
    `_camera.renderingPath`, and `Character3D.Show/Hide` toggles the model through a
    `HashSet<Component>` of show-requests two floated windows could be fighting over.

- **ModBuild 185** (bundle UNCHANGED — plugin DLL only) — the fuse was hiding a convert/release
  loop, and the camera order finally gets measured.
  * **(3) The mouseovers: 184 removed a band-aid and exposed the wound.** Exempting hover cards
    from the churn fuse was right — and the 184 log then shows the popup floating and **releasing
    on alternating ticks** (`open=True, convertWanted=True`), never surviving long enough to be
    revealed. **`IsAdoptedByConversion` counted the window's OWN modal float.** `TryConvertWindow`
    converts the window's own `RectTransform`, so from the tick after a catch-all float the window
    matched *itself*. Tick order is rebuild `OpenWindows` → `TickCatchAll` → release loop, so:
    N floats, N+1 calls it "adopted" and does not re-add it, the release loop drops every
    **non-sticky** window not in `OpenWindows`, N+2 floats it again — **one full conversion per
    tick, forever.** *That* is what the fuse was really capping (hence the ~1000 ms/frame that
    motivated it). Sticky windows never showed it, and the map room makes nearly everything sticky
    — the hover card is the one non-sticky window there.
    **Also: a hover card is no longer a blocking modal.** `MapRoomParallel` excludes hover cards so
    they are never sticky, and that exclusion leaked into `IsBlockingWindow` — every mouseover
    flipped the mode machine to `ModalUI` and gated card commits off and on.
  * **(2) The X did not close the character screen, it SPLIT it.** Log line 2941 closes
    `New Party display` (PartyPanel); twelve lines later `Campaign Adventure Party Assembly Variant`
    (PartyAssemblyWindow) — until then a child rendering inside the party display's host — becomes
    eligible and floats on its own. **"The parent wins" can only hold while the parent is there**,
    so a screen whose parts are nested windows must not have a removable parent. That family now
    floats with **no X** in the map room and is refused by `CloseFloatedWindow` and the escape chord
    (user: *"das soll hier in der Phase nicht schließbar sein"*). The pause menu is the way out.
  * **(1) The flicker: the measurement the silence pointed at.** `PanelFlickerProbe` printed nothing
    across two sessions ⇒ panel state is steady, the whole panel-state family is retired. What
    flickers are exactly the two things a camera feeds into a **RenderTexture** (live character
    render, story picture); the text on the same canvas at the same order does not. New
    `WorldUI/CameraOrderProbe` records the per-frame camera sequence and asks whether a
    `targetTexture` camera draws **between the two MultiPass eye passes** — if so, one eye samples
    generation N-1 and the other N. The game's character render is **`GUI 3D Camera` at depth 24**
    against a head camera at depth 1. **The correction is data-driven**: a camera is hoisted below
    the head only after it has been *observed* between the passes, and the observation is logged
    first. Every distinct order shape is logged once, so a quiet log still says what the order was.

- **ModBuild 184** (bundle UNCHANGED — plugin DLL only) — three reports, one ancestor, and a fuse
  that counted the wrong thing.
  * **(1) The mouseovers had TWO causes, both already in the 183 log.**
    **(1a) The churn fuse counted hover cards.** Log line 835: `CATCH-ALL FUSE: window 'UI Quest
    Preview Popup' re-floated 4× in 60s … suppressed for this session`. The fuse's own premise,
    written in its doc, is *"decisions open once and wait"* — and a hover card is the one floated
    thing that is not a decision. It floats once per hover **by design**. Four laser sweeps and the
    popup was dead for the session. Hover cards are exempt from the count and the verdict now.
    **(1b) Even before the fuse, the card was never shown.** Every `MODAL DIAG` for that popup reads
    `canvas.enabled=False` and the draw-order line says `(hidden: reveal gate)`. The gate waits for
    the host pose to hold **still** for `RevealStableFrames` — and `TickHoverCards` rewrites that
    pose every frame, from a head-relative direction, because that is how the card follows the icon.
    **A stillness test cannot be satisfied by a host whose pose someone else owns.** New
    `ConvertedPanel.PoseOwnedExternally` drops criterion 3 only; treatment and fit still gate it.
    (1c) The arc no longer counts hover cards, so a mouseover stops re-arranging the room.
  * **(3) The merchant: a parent that was never going to float.** The five destinations (merchant,
    temple, trainer, enchantress, town records) are each a `UIWindow` **and** a serialized child of
    `UIGuildmasterHUD`, whose own window is open forever and permanently refused (its VR surface is
    the table caps). 181's "parent wins" asked only whether an ancestor was **open**, so it refused
    every destination on account of a parent that would never be floated — and 182's root-canvas
    exemption then let the destination's **inner scroll view** through instead. Log: `Scroll View`
    floated, the shop window never did, the merchant arrived as bare rows. **The rule now asks
    whether the ancestor will ACTUALLY BE FLOATED** (`AncestorWillBeFloated`); the root-canvas
    exemption is gone with it, because a child canvas inside a floated host is *adopted*.
    **The background travels**: the destination art is one shared `UIGuildmasterBanner` living in
    the HUD, so the window alone was never enough — the mod now parks it inside the floated window
    (sibling 1, the exact move the game makes for the temple) and hands it back on release, only if
    it is still ours.
  * **(2) The dead character clicks were the merchant all along.** Seven `uGUI click: 'Adventure
    Character Slot'` dispatches with nothing behind them. `UIShopItemWindow.EnterShop` puts the
    party display into **selection mode** (`buttonsCanvasGroup.interactable = false`,
    `EnableSelectCharacter(false)` on every slot); only the mode's `Exit` undoes it, and that runs
    only when **another mode is selected**. He pressed the Merchant cap and never left the mode —
    there was no window to leave it from — so the slots stayed dead for the rest of the session.
    The X on a destination now **presses the bar's map button** instead of hiding the window, so
    the game's own Exit chain runs. **Not parallelism**: the guildmaster modes are a one-at-a-time
    state machine (`UpdateCurrentMode`, `allowSwitchOff=false` while active) — merchant AND temple
    together cannot be granted without driving it into a state the game never produces. A new
    `PARTY SLOTS n/m interactable` line makes the next log prove this instead of arguing it.

- **ModBuild 183** (bundle UNCHANGED — plugin DLL only) — the new window belongs in front, and the
  probe answered by staying silent.
  * **(1)+(3) are one number: `staggerIndex = Converted.Count`.** The log shows `stagger=3` on every
    spawn: 181 put the map room's arc in the SPAWN path indexed by how many windows were already
    open, so with three sticky windows standing every fresh one landed at **68° to the side** — and
    the hover cards went with it, which is why the mouseovers looked like they had stopped.
    **Placing the arc at spawn can only ever position the NEW window, and the new one is precisely
    the one that must be dead ahead.** The arc moved into `RelayoutMapRoomArc`, run whenever the
    SET changes: newest at 0°, the rest outward, each keeping its own clamped height. Never touches
    a grabbed window (`UserMoved`), never runs per frame (that would drag windows with the head).
  * **(2) Deselection**, both routes through the game's own `MapLocation.Deselect`: trigger with
    the ray on no location, and the quest popup going away. One-second grace on the popup test —
    the absence of a thing that has not arrived yet is not its departure.
  * **(4) THE FLICKER — the probe printed NOTHING, and that is the result.** Across the session
    neither eye pass disagreed inside a frame, nor did frames alternate, on canvas enabled /
    sortingOrder / overrideSorting / renderMode / worldCamera / layer / host active / pose or any
    adopted child's state. **The panels' own state is steady; the flicker is downstream of it** —
    which kills the entire family the last four builds worked through, all of which were about
    panel state. What flickers are the two things fed by a camera into a **RenderTexture** (live
    character render, story picture) while the text beside them does not. **Next measurement: the
    per-frame CAMERA RENDER ORDER** — a camera with a `targetTexture` rendering *between* our two
    MultiPass eye passes gives the eyes different generations of the same RT.
  * (5) Buttons accepted ("erstmal gut") — left alone.

- **ModBuild 182** (bundle UNCHANGED — plugin DLL only) — two proven fixes, one probable, and an
  instrument instead of a fourth guess.
  * **(1) The hover card caught the character screen — my 181 regression.** 181 classified with
    `GetComponentInParent` **and** `GetComponentInChildren<UILocalTooltip>`; the character screen
    *contains* tooltips, so the whole thing became a hover card — flown to the hovered icon,
    stripped of grab bar and X. **A containment test answers "is this related to a tooltip"; the
    question is "IS this a tooltip".** Own GameObject only now. **Same mistake shape as 179's
    `GetComponentInParent<UIGuildmasterHUD>`** — twice in four builds, see
    [[containment-is-not-identity]].
  * **(4) Buttons lie flat, frame gone.** `CapTiltDegrees` is measured from the **table plane** and
    is 0; the up-hint moved to the rail's +Z so `LookRotation` stays conditioned at a flat face.
    `NativeButtonSkin.CreateFace` removed — on a uGUI bar that 9-slice sprite *is* the button, on a
    physical cap it is a second button on top of the first. Tinting moved to the disc's material.
  * **(3, probable) The missing subtitles are a 181 regression.** "The parent wins" assumed the
    child draws through the parent's canvas. **False when the child carries a ROOT canvas** — then
    refusing to float it makes it invisible rather than handing it over. Now exempted.
  * **(2)(3) The flicker: three hypotheses falsified, so measure it.** (i) the `overrideSorting`
    write war — real, fixed at 179, flicker survived; (ii) the mod-layer sweep dragging the
    character rig — the next log says **`0 subtree(s) LEFT ALONE`**, there is not one `Renderer` in
    those windows, so 180's skip never fired; (iii) distance-sort thrash — the pass already has
    hysteresis by design. New `WorldUI/PanelFlickerProbe` samples every floated panel at **both**
    MultiPass eye passes and compares field by field: pass 0 vs pass 1 ⇒ **stereo rivalry**;
    A-B-A-B across frames ⇒ **temporal write war**. Names the panel *and* the field.

- **ModBuild 181** (bundle UNCHANGED — plugin DLL only) — a hover is not a window, a parent is not
  its children, and the caps faced the map. *(His item 1 closed: the joystick works.)*
  * **(2) Every mouseover became a window — my own 180 regression.** 180 made every map-room float
    sticky so merchant+temple could coexist. Right for windows you OPEN, wrong for windows the
    pointer TOUCHES: the quest-preview popup and local tooltips open/close with the hover, so
    sticky made each one permanent and they piled up. **Treating "floated" as one category was the
    mistake.** HOVER CARDS are now their own class (matched by `UIQuestPreviewPopup` /
    `UILocalTooltip`): no grab bar, no X, not sticky, and the pose written every tick — bottom
    seated on the hovered icon, billboarded, flattened to the horizon. **Pose, not parent**: the
    map rebuilds its icons on every `InitMap`.
  * **(4) The character screen is one screen.** `Campaign Adventure Party Assembly Variant` →
    `Campaign Party Assembly Character Display` → `Party Display UI ` is a **nest** of UIWindows and
    the catch-all floated each separately. In the map room the **parent wins**: a window with an
    OPEN ancestor window is not eligible. Level-triggered, so it reverses by itself either way.
  * **(3) The caps faced the map, and the icons z-fought.** `seat.Rotation` makes the player FACE
    the map, so the rail root's **+Z runs seat → map**, not toward the player — 179 and 180 both had
    it backwards in comment *and* maths. Face dir is now `(0, sin, −cos)`; local +X falls out as
    world +X so the row also stops being mirrored. **The symbol flicker was z-fighting:**
    `GetRoundCap(d,h)` puts the front face at exactly −h/2 and 180 placed the sprite at exactly
    −depth/2 — coplanar with an opaque depth-writing surface. Now 0.8 mm apart, layer by layer.
  * **(5, part)** Map-room windows spawn on an **arc** (0°, ±34°, ±68°, capped 85°) instead of the
    overlap-stagger. And the seat: `TrySolveSeat` asked for the orbit camera's focal **diff**, which
    is zero when the camera sits on its focus — hence the world −Z fallback on hardware. It now
    falls back to the camera's **forward**, which is how the flat game reads the map (euler
    (80,90,0) ⇒ the −X side). Direction only; test #8 not repeated.
  * **Not in this build (both his):** the MP **spawn ring** in the map room, and syncing icon
    highlight + mouseovers between peers (3D mode only). The sync needs its own wire record.

- **ModBuild 180** (bundle UNCHANGED — plugin DLL only) — the character was drawn twice, and 179's
  exclusion was too wide. Six reports, five proven causes, one instrument.
  * **(1) The flicker was a 3D rig on the mod layer.** 179 ended the `overrideSorting` write war
    (18,994 repeats → 0) and the flicker survived, so that was never the whole cause. His earlier
    screenshot had it: the character model rendered **large, in the world, in front of its window**.
    `ApplyModLayer` moved the window's *entire* subtree (2,084 transforms) onto the mod layer — but
    **uGUI draws through `CanvasRenderer`, which is not a `Renderer`**, so any real `Renderer`
    inside a converted window is by definition not UI. Here it is the live character rig, which the
    game renders with **its own preview camera into an RT — culling by layer**. The move blinded
    that camera *and* handed the raw model to our (deliberately broad-masked) head camera. Two
    pictures of one character over the same pixels. The sweep now **walks** the tree and skips any
    branch rooted on a `Renderer`/`Camera`, children included.
  * **(4)(5)(6) 179's HUD exclusion caught the whole family.** `shopWindow`, `templeWindow`,
    `trainerWindow`, `enhancementWindow` are serialized **children** of `UIGuildmasterHUD`, so
    `GetComponentInParent` excluded them all: the log shows `MAP TABLE BUTTON 'Merchant' pressed`
    followed by **nothing**. Now tested on the window's **own** GameObject. Plus the map room's own
    rule (user: parallel, non-blocking, never self-closing): every floated window there is
    **sticky** + **non-blocking**, confirmation boxes exempted. `ReassertStickyVisible` gained a
    **STICKY FIGHT** counter — sticky is right against an *event-driven* hide, wrong against a
    per-frame writer, and change-gating the write does not save you there (179's lesson, one layer
    up).
  * **(3) Physical buttons.** Static socket disc + travelling body disc (`Cards.CardMesh` round
    caps on the shared lit keycap material), face/icon/glow/badge parented under the body so the
    assembly sinks 7 mm on a press — fast down, soft back, and it travels whether or not the game
    accepts. **The icons were mirrored:** 179 aimed the cap's +Z at the player, but a
    `SpriteRenderer`'s front is its **−Z**; they were only visible because `Sprites/Default` is
    `Cull Off`. Frame flipped; +Z is now "into the socket".
  * **(2) The joystick — no proof, so an instrument.** Mode is TableIdle, so 178's gate is not it,
    and **every early-out in `Flight.Update` returned in silence** (only the scroll suppression had
    a line — which is exactly why *that* class of report was always solvable in one round). Flight
    now reports its idle reason, change-gated: FlightEnabled off / no tracked FlightHand / world
    grab owns the stick / mode / LIVE.

- **ModBuild 179** (bundle UNCHANGED — plugin DLL only) — one overflow, one write war, and the
  table buttons.
  * **(1)+(2) ONE LINE.** `if (Time.frameCount - _scanFrame >= RescanIntervalFrames)` with
    `_scanFrame = int.MinValue` **overflows negative**, so the test is false forever (the field is
    only written inside the branch). `MapLocationInteractor.Rescan` never ran once in 178 — and the
    feature was **silent** about it, because the "armed" line only printed on a non-empty scan.
    `MapIconLayer` and `TickPredicate` both special-case the sentinel; this one did not.
    **(2) falls out of the same line:** `RayInteractor.Mask` starts at
    `Physics.DefaultRaycastLayers` and the only other writer (`BoardDriver.SyncRayMask`) early-outs
    without a scenario `Controller`. So the pick hit the table — and `RayGrabDriver` refuses a bar
    grab whenever the pick is nearer than the bar. In a scenario that test is safe **only because
    the mask is narrow there**. The map room now owns the mask unconditionally (location layer, or
    **zero** when there are no icons — the correct value in a room whose only physics targets are
    those icons).
  * **(3) The flicker was a per-frame write war, and the log measured it: 18,994 of 20,173 lines**
    were `MODAL DIAG: adopted canvas … had overrideSorting flipped back ON by the game — re-cleared`.
    The guard cleared it in LateUpdate; a game writer set it every Update. Neither wins — the
    canvas re-sorts every frame and the two MultiPass eyes can disagree. **A write war with the
    game is never won by writing harder.** The adoption now **concedes** after 3 caught frames:
    stops touching the FLAG, owns the NUMBER (sortingOrder pinned to the host's live draw order).
  * **(4) Phase 6 shipped** — `WorldUI/MapRoom/MapButtonRail`: `UIGuildmasterHUD`'s bar as physical
    caps on the table rim, world-fixed. **The look is SAMPLED, not modelled** (user ruling: same
    symbols *and* same animations): per frame each cap copies the live icon sprite (re-assigned by
    the game on every `SetMode`), its colour/scale, the `CanvasGroup` alpha, and the highlight
    graphic under `highlightAnimator` — the object the game `SetActive`s and drives with
    `LoopAnimator.StartLoop`, whose live alpha and scale *are* the press-me pulse. A copy of an
    animation drifts; a sample cannot. Press = `ExecuteEvents.pointerClickHandler` on the real
    `Toggle`; a cap the game would refuse is dimmed with its collider off.
    `UIGuildmasterHUD` joins `ModalFallback`'s known-HUD list — the log's own churn fuse had already
    called it *"a cycling HUD banner, not a waiting decision"*.

- **ModBuild 178** (bundle UNCHANGED — plugin DLL only) — the map room gets its UI, its laser and
  its locations.
  * **User on 177:** *"Der Laser funktioniert nicht … das Optionsmenu öffnet sich nicht & die UI
    Elemente sollten angezeigt werden (die Charactere) in verschiebbaren Fenstern. Auch fehlen die
    Knöpfe auf dem Tisch vollständig bisher."*
  * **(a) 177's own reasoning was wrong on a fact.** His log: `[OptionsToggle] X tap … -> OPEN`,
    then `MODAL FALLBACK: 'UI Map Esc Menu' opened without a VR conversion (scenario=False)`. The
    menu opened and was never shown — `ModalFallback.Tick` gated the whole floated-window layer on
    `ScenarioBoardExists`. 177 had kept the map room in `Menu2D` arguing that promoting it would
    take the flat screen away; but `FlatScreen.ScreenWanted`'s **first two lines** are
    `if (MapRoom.MapRoomDriver.Active) return false;` — the screen is off there *before* the mode
    is consulted. **177 protected an invariant that did not exist and paid for it with a room that
    had no UI in it at all.** Fix: the map room resolves to `TableIdle`/`ModalUI`;
    `ModalFallback`'s scenario local became `TableInFrontOfPlayer`. 177's three locomotion guards
    go back to the plain mode test (one mechanism); `BoardPick` moves the other way and now asks
    `ScenarioBoardExists` outright, because it is entirely about hex tiles.
  * **(b) The laser was never off — it was 0.15 mm wide.** `RayInteractor` sizes beam and reticle
    by ANGLE: `Clamp(headDist × factor, …MinMeters, …MaxMeters)`. `headDist` is a **world**
    distance, so the product is world units, but the bounds are **real-metre** intentions. At rig
    scale ≈ 1 (every scenario diorama — the only place this had run) they coincide. The map room
    seats at **198.12** units/m: angular width 0.21 world units, clamped by `BeamWidthMaxMeters` to
    **0.03**. Drawn every frame, invisible. All four bounds now × rig scale.
  * **(c) Phase 4 shipped** — `WorldUI/MapRoom/MapLocationInteractor`: laser hover, trigger click,
    fingertip press on location icons. **No new authority**: the click is
    `ExecuteEvents.pointerClickHandler` on the real `MapLocation`, the same dispatch the game's own
    gamepad path makes, so `IsSelectable()` + the game's `m_OnClickAction` still decide and nothing
    goes on the wire. The layer mask is **measured** off the live icons, not copied from the game's
    private field. Hover uses the **shared** ray pick so the beam ends on the icon.
    `MapLocationSelector.Update` is prefixed off while the room stands (it raycasts the frozen map
    camera's screen centre and would cancel our hover every frame) and its transitions reproduced.
  * **Not in this build:** the table buttons — that is the `UIGuildmasterHUD` bar physicalised onto
    the table rim (phase 6), a new rail with its own skin and tuning, not a closed gate.

- **ModBuild 177** (bundle UNCHANGED — plugin DLL only) — the map room is a room.
  * **User:** *"Aktuell ist es einfach nur ein Tisch ohne jegliche Umgebung und ich kann mich auch
    nicht frei Bewegen. … die Bewegung und alles andere soll sich exakt genau so verhalten wie in
    einem Szenario, da soll es keinen Unterschied geben."*
  * **ROOT CAUSE — one false premise, stated in five places.** `VRModeStateMachine` composes
    `!inScenario ? Menu2D : …` off `ScenarioBoardExists` (a live `Choreographer`). The campaign map
    screen has none — which is exactly why `MapRoomDriver`'s own gate is a *positive*
    `MapChoreographer` signal — so the 3D map room is unavoidably `Menu2D`. Every locomotion
    subsystem stands down there and each says why in its own comment: *"no scene to fly through"*
    (`Flight`), *"no table exists"* (`WorldGrab`), *"there is no board in front of you"*
    (`SnapTurn`). Those sentences are **true of the flat 2D menu and false in the map room**, which
    builds precisely the thing they assume is absent. The environment was the same premise a fourth
    time (`SkyAlternative.Tick` returned early on `!ScenarioBoardExists`) and `Haunt` a fifth.
  * **Fix: ask the premise, don't infer it from the mode.**
    `VRModeStateMachine.TableInFrontOfPlayer = ScenarioBoardExists || ModRoomStands`, the second
    term pushed by `MapRoomDriver.Engage`/`StandDown` (Core keeps no handle on WorldUI's lifetime).
    All five sites read it. **The mode itself is deliberately unchanged** — Menu2D has a dozen
    consumers that are *right* about the map screen (`FlatScreen`'s show policy and pointer,
    `ModalFallback`'s catch-all, `CameraInventory`, `ButtonCluster`, `WorldTooltips`), and the flat
    screen is how the player picks a location at all. Promoting the room to a scenario flow mode
    would have been the louder regression.
  * **Environment: a second subject, the same arithmetic.** `TryMeasureBoardWorld`/`…Yaw` gained a
    first arm measuring the **parchment's world bounds + transform yaw**. Everything downstream is
    untouched: play space 4.5 × the subject, floor 0.75 × below the underside, the 0.2–20 m
    plausibility window (the map seats at 1.2 perceived m), the `MinFarWorldUnits` far budget. The
    two arms are **provably disjoint**: a scenario has no `MapChoreographer`, the map has no tiles.
  * **MP:** nothing on the wire; both map readings are pure game-scene state, so every client
    resolves the same numbers — which is what keeps moon and light shafts agreeing between peers.
  * **Free:** flight, world grab (two-hand zoom of the map), snap turn, comfort vignette, vertical
    lift, env sound, haunt. Not one needed a map-specific line.
  * **Not in this build:** no re-seat on a world↔city switch (131's teleport ruling stands);
    `ElementMood` stays scenario-only (no infused elements on a campaign map).

- **ModBuild 176** (bundle UNCHANGED — plugin DLL only) — the map is a background, not an owner.
  * **ROOT CAUSE (one defect, two symptoms).** The game's camera never had this bug —
    `CameraController.LateUpdate` consumes the wheel only when `IsPointerOverGameObject()` is
    false — but that method is prefix-skipped in VR, so **the mod drives the map's pan and zoom
    itself** and gated both on `FlatScreenStereo.MapActive`: a **screen-wide** fact used to claim
    input across the **whole surface**, while a window on top covers only part of it. So
    `TickStickScroll` returned on `mapActive` before ever looking at what the laser pointed at (no
    list could be scrolled), and the generic uGUI-drag path is gated on `!mapActive` (no scrollbar
    could be dragged; the trigger fell through to map-pan).
  * **The claim is now per pixel**, the same rule the game uses: `PointerOverUiHandler<T>(pixel)` —
    one `EventSystem.RaycastAll` at the RT pixel, the mechanism `DirectClick`/`TickStickScroll`
    already share, asking for a **handler** of the gesture rather than any hit (a full-screen
    backdrop is a raycast target and would kill the map everywhere; scrollbars/sliders/scroll views
    all implement `IDragHandler`). `TickStickScroll` no longer early-outs on `mapActive`, and
    `TickMapInput` stands down while `UiScrollFocus.IsScrolling(hand)` — reusing that class's
    already-written ruling (*scroll wins over ambient claimants*) rather than inventing one.
  * **Precedent that was one step away:** `MapActive` already excluded scenario/story/encounter
    overlays "so the map can't be panned behind it" — the same reasoning stopped at the three
    windows the previous report had named.
  * **`[Rig] Experimental3DMap` was unfindable** — implemented and documented, but with no display
    name (menu showed the spaced raw key) and no curated row. Now named in both languages, with a
    hint, on the curated page under the environment choice. Still defaults to off.

- **ModBuild 175** (bundle unchanged) — **his own numbers; 169 froze the wrong
  ones and nobody noticed for six builds.**
  * He sent `dev.gloomhavenvr.water.cfg`. Until 169 the `[Water]` section was live and he had tuned
    it through the Erweitert menu a long way off the defaults: **RippleSpeed 1.0 vs 0.00875 (114×)**,
    SwellHeight 0.045 vs 0.005 (9×), Shimmer 0.35 vs 0.03 (11.7×), Smoothness 0.21858, Opacity
    0.2304364, Reflectivity 0.1446353, ProbeBrightness 0.3604062.
  * **ROOT CAUSE.** Every verdict from 166 on was about **his** numbers, not the ones being
    re-based. 169 removed the section at his request, wrote in its own header that it was freezing
    "168's accepted values", and froze the **defaults** — a 114-fold slowdown on his machine, in the
    same build. That is the moment the water stopped. 172 and 173 then re-tuned around a baseline he
    had never seen; 174 was spent measuring whether the shader animated at all.
  * **The mistake is specific and mine:** the section was removed without ever reading the file it
    retired. `AnnounceRetiredFile`, written in that same build, names all seventeen keys — it exists
    to warn *him* the file went inert, and it never occurred to me to ask what it said.
  * **Ships his values verbatim** (standing rule for dropped cfgs; this is the only water
    configuration ever confirmed on hardware by the person looking at it). Resolved: bob 1.10–0.57 s,
    amplitude 60 mm (clamped from 117), crest slope 13.2°, peak vertical 411 mm/s, crossfades
    1.8/2.9/4.7 s — about **500× 169's normal rate**. The amplitude is 164's, but 164 also
    *translated* at 4.29 wu/s and put the glint in **alpha**; both were deleted in 165/166 and
    cannot return, so this is 164's geometry without the two things actually complained about.
  * **The calibration apparatus is deleted, not re-derived** — the verdict table, the deg/s band,
    `VisibleFastestPeriodSeconds` and the gates "proven to fire" against them. Every attribution was
    wrong. *A precise instrument calibrated against misattributed data is worse than none: it agrees
    with every broken build and it argues back.* What remains are bounds that never needed a verdict
    (basin-bed clamp — load-bearing at his height, 117 mm resolved vs 60 allowed; lattice mismatch;
    no translation; no view-dependent term). The rate figures stay on the census as **reporting**.

- **ModBuild 174** (bundle unchanged) — stop tuning; two measurements instead.
  * **THE ANOMALY THREE ROUNDS WALKED PAST.** 168 was accepted ("Beide Probleme behoben, top" — one
    of the two *was* the water animation); 169 was "komplett stillstehend/freezed". Diff the water
    between them and the entire change is **comment renames**: shader and `.cginc` byte-identical,
    `WaterOwnSurface` edits are `[Water] X` → `WaterSettings.X` in doc text, and the constants 169
    froze are 168's config *defaults* to the digit. Identical code cannot give opposite verdicts, so
    the water tuning was never the variable — and 169/172/173 were three rounds re-tuning a number
    that was not the cause. **174 changes no water value at all.**
  * **(A) His config file.** The section was live until 169, and 169's own log proves the file
    existed (`AnnounceRetiredFile` only prints when it does). If he ever moved `RippleSpeed` or
    `SwellHeight` — in the file or via the Erweitert menu, which writes to it and which he had
    plainly been in — then 168's approved water ran on **his** numbers and 169 dropped it to the
    shipped ones. `AnnounceRetiredFile` now **reads and prints every key=value** beside the shipped
    constant. Read-only. A difference there is the whole answer, and the fix is to re-base the
    constants onto his values.
  * **(B) Which SubShader actually draws.** The census has always claimed "shader level 50, so the
    TESSELLATED SubShader is the one being drawn" — an inference about *hardware*, which has
    reported success on every frozen build. Selection also depends on the **LOD ceilings**: anything
    may lower `Shader.globalMaximumLOD`, and under 300 the LOD 100 fallback is picked silently — the
    same wave on the game's own 33-vertex hex, i.e. a 2.4 m swell with no vertices to carry it. That
    surface is flat, hence genuinely motionless, and unreachable by any tuning. `SubShaderInForce`
    now **measures** both ceilings, the effective one and the pass count.
  * **Ruled out first:** the field moves (0.75 mm in 1 s, 7.9 mm in 10, off the pinned C# mirror)
    and the shipped shader's own preview renders at t=0 vs t=450 s differ over 2–17 % of pixels.

- **ModBuild 173** (bundle unchanged) — amplitude cannot buy past a temporal
  floor, and **ModBuild 170's explanation was wrong**.
  * **First: the mechanism is alive.** The displacement field was evaluated at *t* and *t+dt* off
    the C# mirror the wire test pins against the shader — 0.75 mm in the first second, 7.9 mm in
    ten. Nothing is stuck at t=0 and 172's census reports exactly what 170 intended. So it is
    perception, not a bug, and only one of those is worth tuning.
  * **ROOT CAUSE, and it overturns 170.** That round claimed the perceived quantity is the *product*
    amplitude × steepness × frequency and traded frequency for amplitude. Its own successor
    disproves it:

    | build | fastest component | max Δh in 1 s | verdict |
    |---|---|---|---|
    | 166 | 16.3 s = 0.0615 Hz | 1.04 mm | "Sehr gut … nur noch etwas zu schnell" |
    | 167 | 32.5 s = 0.0307 Hz | 0.52 mm | "gerne noch langsamer" |
    | 169 | 65.1 s = 0.0154 Hz | 0.27 mm | **frozen** |
    | 172 | 45.9 s = 0.0218 Hz | 0.75 mm | **still frozen** |

    172 moves the surface **44 % more per second** than 167 at 1.4× its normal rate — and 167 was
    visible while 172 is not. No product of amplitude and frequency can be the judged quantity.
    What sorts the four cleanly is **temporal frequency alone**, and it is a *floor*: human temporal
    contrast sensitivity is band-pass, and below ~0.03 Hz a luminance modulation is not perceived as
    change whatever its size. This surface is forbidden to translate, so it has **no optic flow at
    all** — that slow shading modulation is the only carrier.
  * **Ships:** `SwellSpeed` 0.0124 → **0.0175** = 167's own clock, the slowest he has ever confirmed
    seeing move ("gerne noch langsamer" presupposes something to slow). 170's doubled `SwellHeight`
    stays, so it is 167's tempo at twice the relief — 5.79° slope, 3.11 mm/s, 0.758 °/s.
  * **`VisibleFastestPeriodSeconds = 35 s`**, in the measured gap between 172's 45.9 (frozen) and
    167's 32.5 (seen), placed against the floor. The wire test asserts the shipped value is under
    it, **that the floor reproduces all four remembered verdicts** (every SEEN build one side, every
    FROZEN build the other), and that the clock is no slower than 167's. **Proven to fire**: at
    169's values four checks fail, at 172's two.
  * **The old period bound was deleted, not loosened.** It asserted the clock must be *slower* than
    167's because "gerne noch langsamer" seemed to stand — and obeying it produced 169 and then 172.
    A test that encodes a superseded request keeps steering builds into it.

- **ModBuild 172** (bundle 70,218,494 bytes) — the glove gets its
  own bake.
  * The same three-map set the arcane hand arrived with, now for the leather glove: re-baked 2048²
    base colour, 2048² **normal map**, displacement. The FBX is byte-identical to the one 171
    imported (`md5 f77bc534…`), so no rig, weight or mirror work — a texture round, and
    `import_glove_fbx.py` did not need to run.
  * Rendered against the outgoing albedo before shipping: skin is warmer and carries knuckle/pore
    detail where it was a flat pale tone, the leather has grain and stitching, the studs read as
    brass. **The check that matters is that the studs sit on the strap and the stitching follows the
    seams** — that says the bake is against *this* mesh's UVs and not a re-authored layout.
  * Glove is now the second set with a real normal map. Only the AI-generated **Plate** gauntlet is
    still flat-bumped and still double-sided — it is the last shell in this project with holes in it.
  * **Displacement again not shipped** (both sets delivered one): `BoardLit` has no height, parallax
    or tessellation term and the hands are not subdivided, so ~1.7 MB of bundle for no pixel.
  * **Privacy check** (he asked): the delivered FBXs embed two absolute Windows paths — texture and
    source `.blend` — carrying a user name. None of it has ever reached the repo: `.planning/debug/`
    is gitignored and `import_glove_fbx.py` exports `path_mode='STRIP'` from a freshly built scene,
    so the shipped rigs hold no paths at all. Verified across every tracked file under
    `Assets/Bundle/Hands`. This round's three PNGs carry no metadata at all; the albedo they replace
    had an XMP block reading only `xmp:CreatorTool="GIMP 2.10"`.

- **ModBuild 171** (bundle 67,859,030 bytes) — the arcane hand
  joins the artist pipeline, and the wrist anchor turns out to be a contract.
  * A revised **glove** (same mesh and atlas byte for byte; the rig lost its leaf bones and its
    fingertip bones now match their parents' lengths) and a complete new **arcane** set —
    20,654-tri hand-authored mesh, 2048² base colour, 2048² **normal map**, displacement map.
    `import_glove_fbx.py` drives both sets now (`GLOVE_SRC`/`GLOVE_NAME`). It learned two things:
    take `Anchor_IndexTip` from `Anchor_Index_Tip.tail` when the export has no leaf bones, and
    check the **custom-split-normal flag** through the round trip — both artist meshes carry baked
    split normals and an export setting that drops them ships a smooth hand faceted, silently.
  * **ROOT CAUSE-CLASS FINDING: the wrist anchor is tuning surface, not decoration.** The delivered
    arcane rig puts `Anchor_Wrist` **160 mm** from where the shipped one has it, and the wrist HUD
    hangs off that bone with `[WristHud] Arcane*` tuned *by hand* as offsets **from** it — adopting
    it verbatim would have moved his watch face 14 cm up the forearm with every tuned number still
    in the file looking correct. Which placement is right was **measured**, by scanning each mesh's
    cross-sectional girth for the waist where the hand narrows into the forearm: old arcane bone
    **79 mm forward** of its own waist, delivered bone **67 mm behind** it, glove 28 mm behind. The
    two meshes agree on the anatomy to within a millimetre; the two rigs disagree by 145 mm and
    **neither sits on it**. So there is no correct placement to restore — only the one the tuning is
    measured against. Snapped back, both numbers printed every run. Consequence documented so nobody
    "fixes" it: `Anchor_Palm` now reads 25 mm *behind* `Anchor_Wrist`, which is what the shipped rig
    has always done.
  * **First real normal map.** `BoardLit` has declared `_BumpMap`/`_NormalStrength` and read
    `TANGENT` since it was written; no hand set had ever supplied one. `BuildHands` forces the
    importer to `NormalMap` (a normal map left as a colour texture samples happily and every slope
    is wrong) and tangents are `CalculateMikk`. The **displacement** map is deliberately not
    shipped — nothing in this pipeline can read it, so it is 3.4 MB of bundle for no pixel.
  * **The arcane hand stops paying for holes it no longer has** — 0 boundary / 0 non-manifold,
    +1748.22 cm³, against the AI shell's 869/1680. `Cull Off` is a repair, not a look; only Plate
    still needs it.

- **ModBuild 170** (bundle unchanged from 169) — a period is not perception.
  * **ROOT CAUSE of "komplett stillstehend/freezed":** every water gate and every census field in
    this project measures a **period**, and 168/169 shipped periods exactly as designed. The eye
    follows the surface **normal**, and that rate is a *product* — amplitude × steepness ×
    frequency. Four rounds of "slower" moved only the frequency while the other two sat still, and
    the product fell under the threshold of motion perception. His own three verdicts bound the
    band: `0.189 °/s` = frozen (169), `0.379` = "gerne noch langsamer" (167), `0.758` = "Sehr gut,
    nur noch etwas zu schnell" (166). Slope has sat at 2.9° through all three and has never been
    what he complained about — 164/165 were "hektisch" at 13.2° and 8.1° *while translating*.
  * **Neither axis alone could fix it**, and that is why the fix is two-part: reaching a visible
    rate by amplitude alone needs ~8° of slope (165's rejected steepness); by frequency alone it
    means undoing both halvings he asked for. So **the two clocks were split** — `SwellSpeed`
    0.0124 drives the relief, `RippleSpeed` stays at 168's 0.00875 and drives only the crossfades
    (203/329/533 s, unchanged) — and **`SwellHeight` doubled to 0.010** (2.9° → 5.8°, 28 % under
    the rejected 8.1°). Shipped: longest bob 88.7 s (still 1.4× slower than the 167 he asked to
    slow), 2.20 mm/s, **0.537 °/s** = 2.8× frozen, 71 % of brisk. The bloom stays on the swell's
    clock so `GhvrSwellBloom` needs no new property — **the bundle is untouched**.
  * **AND THE GATE WAS POINTED AT THE WRONG NUMBER.** `SpeedDialIsOneClock` asserts "the shipped
    bob period is inside 50–80 s" and was GREEN through two builds that shipped 126 s, because it
    kept its own `private const float ShippedSpeedDial = 0.0175f` which never followed 168's
    halving. *A gate that holds its own copy of the value it is gating is not a gate.* Every
    shipped water constant now lives once in `WaterOwnSurface`; `WaterSettings` forwards. New
    `MotionIsInsideTheReportedBand` asserts the **product**, against a band recomputed from the
    dials of the builds that define it — **proven to fire**: rebuilt at 169's exact values it fails
    three checks.
  * **107 NREs per options-menu build** — `StampRow` instantiated an ACTIVE template, so
    `ButtonSwitch.Awake → Refresh` ran before `StripForReuse`, and its unguarded
    `text.SetTextKey(...)` hit the `TextLocalizedListener` this method destroys on every row. Clone
    is born inactive now; `ButtonSwitch` joined the strip list; the strip is `DestroyImmediate`
    because its `OnDestroy` touches the same Toggle the bool-row path also destroys.
  * **Two ERROR walls at boot that were not errors** — `BundleShaders` shouted "the bundle never
    loaded" twenty lines before the shader resolved out of it. Split on whether ANY bundle is
    loaded: pending gets its own latch, a real miss keeps the wall.

- **ModBuild 169** (bundle 66,607,352 bytes) — the glove is
  someone's work now, and the water menu is gone.
  * **The default hand style stops being AI output.** One artist-authored LEFT-hand FBX arrived
    already on the 19-name rig contract, with hand-painted weights and a matching 2048² atlas, so
    the job was to ADOPT it (`unity/hand-prep/import_glove_fbx.py`), not to rig it — re-running
    `rig_hand.py` would have replaced all three with generated equivalents. Two additions only:
    `Anchor_IndexTip` promoted from Blender's `Anchor_Index_Tip_end` leaf and axis-aligned to the
    wrist frame (the poke probe is a place, not a knuckle), and the right hand mirrored as the
    conjugation `M' = S·M·S`, which keeps each bone's local +X so the SAME positive local-X curl
    the runtime applies produces the mirror-image tuck.
  * **ROOT CAUSE, caught by a gate written in the same hour:** the first mirror reversed each face
    by rewriting its loops' vertex indices in place, which reverses the winding and leaves the UVs
    on the wrong corners. The shell gate said so as a number — left `+297.98 cm³` against right
    `+152.94 cm³` — and `bmesh.ops.reverse_faces` (which carries every loop layer) made them equal.
    Four meshes have shipped wound against the side they are seen from; this is the first that
    could not have. Six gates run before anything is written: CONTRACT, SHELL, CURL AXIS, FLEXION
    (on the *evaluated* mesh, so it tests the skinning), MIRROR (needed because every other gate is
    mirror-invariant and would pass a hand never mirrored at all) and ROUND TRIP.
  * **`_Cull Off` is a repair, not a look** — it makes a hole show the surface behind it, and costs
    a second shaded fragment over the whole hand in both eyes every frame. Now per set, from the
    measurement: glove 0 boundary / 0 non-manifold → single-sided; Plate 540/1072 and Arcane
    869/1680 are open shells and keep it.
  * **The whole `[Water]` section is deleted** — thirteen entries, the cfg file, the display names
    and the German help text — and replaced by twelve `const`s in `WaterTerrainVR.WaterSettings` at
    168's values *verbatim*, so it is a removal of the dials and not a retune. Every one of them
    existed to answer a question during the six water rounds; all are answered. An A/B switch whose
    OFF side restores a reported defect is not a setting. `AnnounceRetiredFile` is the one line
    that stops a tester's now-unread `dev.gloomhavenvr.water.cfg` from looking authoritative.

- **ModBuild 168** (bundle 67,172,123 bytes) — half again, and
  the skull was a different skeleton all along.
  * **`[Water] RippleSpeed` 0.0175 → 0.00875** — a quarter of the value called "viel zu hektisch".
    The "0 freezes the surface" test survived a second halving only because 167 re-based it onto a
    RATIO instead of an absolute period.
  * **FOUR ROUNDS ON THE WRONG SKELETON.** The screenshot — finally opened by the integrator rather
    than worked from descriptions — shows a skeleton slumped on a WOODEN DECK at floor level. The
    `CR_OS_Skeleton_Statue_*` renderers that ModBuild 167 was built around anchor at 3.5 and 5.1 wu:
    **wall statues elsewhere in the level.** 167 fixed a real defect, just not this one, and its own
    zero `PROP UNIT` lines said so in the terms it had set in advance.
  * **The guard's FIRST term was the blocker.** ModBuild 157 required *figure/actor ancestry* — a
    scenery skeleton is not an actor — and that term is redundant against the rule's own
    justification ("wall-mounted dressing never reaches the floor"). Now two arms: the FIGURE arm
    bit-for-bit, plus a FLOOR arm with no ancestry, unit-based geometry and a **measured** 2.5 wu
    height cap (lowest architecture 2.8, tallest floor dressing 2.5).
  * **The guard had to be added to the MOUNTED sweep too**, or the floor arm would be theatre: a
    renderer refused by the wall path is left unclaimed and the geometric sweep adopts it anyway.
  * **`FADE WRITE` now names every renderer any fade path hides**, with its path, height, ancestor
    chain — and for a torn unit, the siblings left solid. Four rounds went by without knowing which
    renderer disappears.

- **ModBuild 167** (bundle 67,162,676 bytes) — half the tempo,
  four dials gone, and the statue keeps its head. **The water look is ACCEPTED by the user.**
  * **The halving came from one number**, and that it reaches all three temporal families was
    *verified*, not assumed. Measured: mean |ΔL| over 4.5 s 0.112 → 0.054 (0.48×), net translation
    still exactly (0,0).
  * **A test that pins an absolute number expires silently when the thing it guards is retuned.**
    The `RippleSpeed` zero-clamp made "0 freezes the surface" false the moment the default halved;
    the test now pins the RATIO. Caught by the existing suite, which is the point of it.
  * **Four water dials removed with their whole code paths** — `DebugPaint` (its magenta answer had
    already settled ownership), `BodyOnly`, `DepthFade`, `ShoreFoam` (which bought a full extra
    opaque submission per eye for a shader the film no longer runs). What survived was checked
    against a live code path: **34 of the 51 tracked renderers are the basin and still run the
    game's shader**, so its dials stay.
  * **The skeleton is a STATUE BUILT INTO THE WALL, torn apart by two owners** — one rescan has
    `Wall 6` holding skull+body+broken while `Wall 3` holds a skull. ModBuild 157's STANDING PROP
    rule needs figure ancestry AND a foot in the ground band; both fail correctly at anchors 3.5 and
    5.1 wu up. Refusing the claim would be worse (a solid skull in a dissolved wall), so the fix is
    **one prop = one unit = one owner**, grouped by the highest still-prop-sized ancestor with the
    standing rule's own caps. **It is a class**: `SB_AC_Arch_Top` + `_Pillars` is the same shape.

- **ModBuild 166** (bundle 67,167,091 bytes) — nothing on the water translates, and it is measured.
  * **"Nets to zero over time" is not "does not move".** ModBuild 165 shipped a sway that reversed —
    an integrator instruction — and the user described it back verbatim: *"es fließt einmal in die
    eine Richtung, stoppt kurz und fließt dann wieder in die andere. Erscheint nicht mehr immersiv."*
    A pattern that runs one way and back reads as **more** artificial than a steady drift, because
    nothing in nature does it.
  * **Deleted, not zeroed.** The three speed properties are gone from the shader table and from C#,
    and a wire test sweeps both shaders and the driver for any reappearance. A user ruling stated
    three times gets removed from the design space, not given a default of 0.
  * **The spatial phase now contains no clock at all** — that is what makes travel impossible rather
    than merely unlikely. Six standing components, ripple crossfaded between three fixed ROTATED
    frames (rotations, not offsets, so a crossfade cannot read as a smeared slide).
  * **A second flow nobody had noticed:** 165's "calm modulation" was `sin(k·d·p + ωt)` — a
    travelling envelope at 2.1 cm/s. Now three standing modulations that fade in place.
  * **Measured, not asserted.** Orthographic overhead station where a pixel offset IS a world
    displacement: net translation over 4.5 s — reference **(−19.17, −19.17) cm**, shipped **(0,0)**;
    change rate 52.88 % of pixels vs **0.00 %**. The wire test converts phase to **metres** and
    requires a deliberately drifted control field to be *found*, so it cannot pass vacuously.
  * **Cross-correlation is the wrong tool inside a wire test** — a standing wave returns inverted so
    the peak wanders. Projection onto each component's own sine/cosine is the right one.

- **ModBuild 165** (bundle 67,163,699 bytes) — standing water, and the geometry is finally there.
  * **A log line hid a whole round, and it was a printing ORDER.** 164 printed `MESH SWAP: no film
    mesh handled yet`, which reads as a diagnosis and was not one: the census ran *before* `Apply()`
    and is capped to one emission, so that field could only print its own initialiser. **A line that
    cannot say anything else is worse than no line** — it looks like evidence.
  * **`33 verts / 0 tris` is the NON-READABLE signature.** Game assets are imported without
    Read/Write, so `mesh.triangles` returns empty and the CPU subdivision could only ever refuse, on
    every film, forever. `WaterSwellMesh.cs` deleted; **GPU tessellation** (`#pragma hull/domain`,
    target 4.6) needs no CPU access at all. The culling pad moved to `Renderer.localBounds`.
  * **The tessellation factor is FIXED, not distance-based** — a camera-derived factor subdivides
    the same patch differently per MultiPass eye: stereo rivalry through the *geometry*.
  * **Per-tile repetition was one coordinate.** The shader keyed off `uv + object world ORIGIN`,
    which on a tile grid is identical per tile by construction; and the ripple repeated every 1.92 m
    against a 1.998 m tile pitch. Now the interpolated **world XZ** everywhere. Irregularity comes
    from four incommensurate standing components plus a large-scale calm modulation — **never a
    per-quad seed**, which would draw a seam at every tile edge. `LatticeMismatch` scores it: 0.156
    shipped vs 0.022 for 164.
  * **The preview had been lying about the mesh** — it staged its own subdivided grids, which is why
    the failed swap stayed invisible. It now stages the real 33-vertex hex. It also had an unlit
    shader lookup returning null in batch mode and a far wall backface-culled in *every sheet it had
    ever produced*.

- **ModBuild 164** (bundle 67,150,300 bytes) — the water has a surface now, and it is calm.
  * **The 163 render SHOWED the streaks and they were rationalised as texture grain.** That is the
    round's real lesson and it is a process failure, not a coding one: *render it, then actually
    judge the frame against the complaint*. This build's contact sheet was judged frame by frame,
    with a flat-vs-swell A/B and a reference column that reproduces the reported defect.
  * **"Hectic" was a UNIT, not a value.** The scroll was applied in texture space, so its rate was
    divided by the tiling: layer A ran 0.6/0.14 = **4.29 world units per second** sideways. Drift
    now moves the world coordinate and tiles afterwards.
  * **The streak shape was 43:1 anisotropy** (`_NormalTilings` 0.14 × 6.00 = a band 7 m long and
    17 cm wide). Tamed toward the geometric mean, product preserved: 43:1 → 3.1:1.
  * **The glint reached ALPHA**, so a highlight went bright *and* opaque — a white streak by
    construction. And the body term sat at 1.148 for undisturbed water: the pool rendered 15 %
    above the authored tint before anything moved.
  * **Shading cannot make a plane look shaped.** The film is a 2-triangle quad; four corners cannot
    carry a wave. The driver now midpoint-subdivides the game's own mesh (new vertices on existing
    edges, so footprint/UV/colour are untouched) and displaces `wp.y` only, with the fragment normal
    taken from the **analytic derivative of the same wave function** — relief lit as though flat is
    exactly the "stripes on a flat surface" complaint.
  * **Culling still cannot see a vertex program**, but here the pad is EXACT rather than a guess:
    the displacement is a bounded translation along one axis, so bounds + `MaxSwellAmplitude` is the
    true swept volume. A wire test pins that ceiling below the 9 cm film-to-bed gap — the preview
    caught real trough-through-bed holes when the bed was mis-set.

- **ModBuild 163** (bundle 67,162,211 bytes) — the water moves again, on a shader of the mod's own.
  * User confirmed 162 fixed both defects, then: *"Das Wasser sieht jetzt sehr viel schlechter aus.
    Das echte Wasser hatte ANimation und co."* Overlay was a stopgap whose job was to prove the
    mirror lives inside `VFX/Water_Shd_Trans`; it did, so it is replaced.
  * **`GloomhavenVR/WaterVR` rebuilds the game's own motion instead of inventing one:** the
    tileset's animation is two scrolling normal layers and nothing else, so the shader samples the
    game's `_Normal_Map` twice at the authored tilings and speeds, all read off the game material at
    runtime.
  * **No term in the file depends on the view direction** — not even a specular. That is the
    reported defect *and* MultiPass stereo safety in one rule, and a wire lint now bans every
    spelling of a view vector across both film shaders.
  * **The offscreen render found two defects reading could not have.** A UV-keyed ripple drew the
    identical tile on all 17 quads (the game gives each UV 0..1); the ripple coordinate is now
    `uv + object world origin XZ`. And the scroll offset needed `frac()` or a long session eats the
    fractional bits. **Render the thing before shipping it.**

- **ModBuild 162** (bundle unchanged, 67,164,721 bytes) — **five builds optimised the wrong
  invariant, and one second of headset time closed what three rounds of logs could not.**
  * **"Mitzoomen" never meant what five builds took it to mean.** ModBuild 161's log proves the
    board held perfectly still *in the eye* — 47.3 cm, 0.62 m, 41°, through rig ×22 → ×64 — and the
    user still rejected it, because the same log shows the board travelling through the room
    (`world pos (41.46,22.11,14.41) → (38.02,19.35,15.36)`, `world scale 47.662 → 41.826`) on every
    pinch. **He was always talking about the WORLD, never about his eye.** Read that way, every
    report from 158 onward is the same report. Ruling, final: *"Fixiert heißt FIX. Keinerlei
    Abhängigkeit zum Spieler mehr, sondern fix in der Welt."*
  * **The zoom carry is deleted.** The delta from 158 is 137 lines in two files, no new component.
    While FIXIERT and not grabbed, nothing writes the board's world transform (every writer was
    counted; only the rig-rebuild carry remains). The min/max divisor is the pin holder instead of
    the live rig, so the settings window stops riding the zoom.
  * **A diagnostic that measures the player frame will agree with any build that rides the player.**
    Five versions of the board line did exactly that. `BOARD ANCHOR` now prints the WORLD pose and
    WORLD scale and calls a move a defect.
  * **The water mirror is inside the game's shader.** `[Water] DebugPaint` came back **magenta**, so
    we own the renderers; and the read-back off the live material instance shows every band at 0,
    the keyword list empty, `_Smoothness` at 0.08 and a flat probe reaching the surface. Only a
    texture or a compiled-in constant is left, and no property can reach it. The film's material is
    now replaced with a mod-owned one on `GloomhavenVR/Overlay` (source-linted to contain no
    environment sample). `EnvPuddle` was disqualified — it computes `reflect(-V, N)`, i.e. the
    reported defect under a new name. **Price: this film does not ripple** until a shader is
    authored into the bundle.
  * **Ship the one-second diagnostic sooner.** `DebugPaint` was written in round four and answered
    in round five what rounds two and three could not.

- **ModBuild 161** (bundle unchanged, 67,164,721 bytes) — **the board half of 160 is REVERTED on the
  user's instruction, and both defects turned out to be a term nobody had looked at.**
  * **The zoom pivots on the HANDS, not the head** — `Rig/WorldGrab.cs:400`,
    `rig.position = _midAnchorWorld - rot * (mid * s)`. Four rounds reasoned as though the eye were
    the pivot, in which case a world-static board keeps its angular size and there is nothing to fix.
    It is not the pivot, so a scale change moves the head through the world and a world-fixed object
    necessarily rides the zoom. **Neither pure anchor is correct**: rig-local is zoom-invariant but
    travels with the player (160, "geht immer mit"); world-static stays in the room but rides the
    zoom (158, "mitgezoomed"). Four builds moved back and forth between them.
  * **The rule that separates them:** the pinned board is re-derived from its rig-local pose ONLY on
    frames where the rig's lossy scale changed. Zoom → invisible; walk/teleport/flight/snap-turn →
    the gate is shut and the board stays in the room. **The gate is the entire difference from 160.**
  * **The water: the visible pixels were never the ones being tuned.** The photograph shows a pale
    near-WHITE sheet; the authored body tint is dark green (0.195, 0.311, 0.131) and only
    `_Edge_Colour` is near-white (0.887). Three rounds capped the body of a surface whose visible
    pixels come from the edge band. "No difference" was an accurate report each time.
    **Open a user's screenshot before the fourth round, not after.**
  * **`instancing=True`** on both water materials, logged retroactively: in the built-in pipeline a
    batched instanced draw takes non-instanced properties from the MATERIAL, so 159's property block
    never reached the shader at all.

- **ModBuild 160** (bundle unchanged, 67,164,721 bytes) — two reports, and **both were a
  measurement that agreed with the wrong thing.** *(The board half was REVERTED in 161 — it made
  FIXIERT travel with the player. The diagnosis below about the missing distance term was right; the
  cure was not.)*
  * **The pinned board: four rounds moved *where* a world-space pin was re-derived; the pin itself
    was the fault.** Angular size = size ÷ distance. ModBuild 159 froze the numerator (world size ÷
    rig scale) and left the board at fixed WORLD coordinates while the zoom rescales the PLAYER —
    146 log lines hold it at `(24.50, 7.83, 16.39)` while the rig swept ×19.01 → ×83.53, so it grew
    4.4× in the eye while the `BOARD SIZE` line reported its size unchanged. **An instrument that
    measures one term of the thing the user is looking at will agree with every broken build.**
    The diagnostic now prints distance in player metres and the subtended angle.
  * **A second, independent defect in the same place: a one-frame lag, proven by identity.** The
    holder's scale was re-asserted in `CardsDriver.Update`, before `WorldGrab.Update` writes the new
    rig scale — `parent chain ×64.79 ÷ rig ×68.50`, where 64.79 is the previous frame's rig scale
    (82 of 158 moving-zoom samples). ±10 % breathing through every pinch. **Per-frame arithmetic in
    the wrong phase cannot be tuned into the right one**; it was deleted, not moved. `TrayPinFrame`
    shadows the rig frame in `LateUpdate` at execution order 20000 and the board hangs under it with
    a constant local pose, so nothing computes where the board goes. A *shadow* and not a child
    because `TearDownRig` would take the subtree — and the board — with it.
  * **The water: `MaterialPropertyBlock` cannot set a shader keyword.** `_EdgeColour_Toggle` is an
    Amplify `[Toggle]`, the live keyword list is `[_EDGECOLOUR_TOGGLE_ON]`, and such a property
    compiles to a `shader_feature` branch that never reads the float. Half of 159's water retune was
    inert **by construction**, and the "0 undone re-asserts" line proved only that our block was
    still attached — not that anything read it. Blocks replaced by owned material instances.
  * **And the scope was too narrow, which the user's own word had already said.** He wrote "Tiles".
    `TERRAIN_Crypt_Water_02_Base`/`_Edge` run `Amp_Basic_N_MRAO` — metallic — directly under the
    film, and `WaterTerrainVR` scoped itself to the `Water_Sh` shader stem and excluded them. **When
    a report names a thing you did not model, that is data, not imprecision.**

- **ModBuild 154** (commit `c7af67e`, bundle 67,148,369 bytes) — two reports, **one shape of fault:
  a lever that was built, logged and shipped without ever being reached.**
  * **The figures' darkening never executed once in 153.** `Shader.Find("GloomhavenVR/HeadUnlit")`
    returned null — `Shader.Find` resolves only shaders that are already **loaded**, and a bundled
    shader is loaded when something pulls it in; for `HeadUnlit` that is a head-avatar mask material,
    absent in a cellar. The fail-dark branch engaged as designed and the figure rendered
    pixel-for-pixel as in 152. **Nothing** about the blit, the colour space or the four fitted
    constants was tested by that build.
  * **And this repository had already paid for the lesson.** `PlayTray.6.Build.cs:625` carries a
    comment written after the same trap cost build `0258fbb`. The fix was two files away, in prose,
    for months. Now one helper (`Core/BundleShaders.cs`) owns the name→path table and **three**
    mechanisms, and **caches successes only** — the old code latched the MISS for the process, so a
    bundle loading later could never be picked up (the same latch was found in the flat-screen map).
  * **The guard is a build gate:** a source lint fails if `Shader.Find("GloomhavenVR/` appears in
    non-comment `src/`, if a table path does not exist, or if the `.shader` there does not declare
    that exact name. **It would have failed 153 before the bundle was built.** It fired on
    NetProtocol's own build note first, which is why comments are exempt — a lint that forbids
    *writing down* the bug it guards pushes the explanation out of the file the next reader opens.
  * **A log line that describes an intention rather than an outcome is how a round is lost.** The
    `light level` line claimed "IT IS MULTIPLIED INTO THE ALBEDO TEXTURE" unconditionally while the
    mechanism was bypassed. It is now composed *after* the attempt and reports the measured ratio.
  * **The candle vanished because frustum culling cannot see a vertex program.** The bookcase's fall
    is a vertex rotation — no transform moves — so Unity kept culling every rider against its
    **upright** box. Walk up to the fallen candle and the stale box leaves the frustum while the
    pixels are straight ahead; in MultiPass the two eyes cull against different frusta, which is the
    **one-eye** band exactly. Six of seven riders shipped a box too small — by 2.32 m (the wax),
    2.59 m, 5.67 m and **7.96 m** (the candle halo). The one that was covered was covered by a
    **typed pad**, which is why nobody noticed the other six.
  * **Reproduced, then removed**: a near-approach series over nine stations shows `FireGlowShelf`
    present at 2.96 m and CULLED at 2.36 m on the shipped boxes; afterwards nothing is culled at any
    station in any pose. The fix is an **arc, not a pad** — and the reachable angle set is
    `[0, _TipAxis.w]` by construction because `GhvrShelfTip` returns a `saturate()`, so it cannot
    drift when the fall is retuned.
  * **Two editor traps worth carrying:** `Renderer.localBounds` **is not serialized** in 2021.3.5
    (measured, not assumed), so `mesh.bounds` is the only mechanism; and `mesh.bounds = …;
    SetDirty()` without `AssetDatabase.SaveAssets()` at the point of computation reads back as the
    vertex box — a perfect log and nothing shipped.

- **ModBuild 153** (commit `5650602`, bundle 67,150,694 bytes) — four reports, and **three of them
  were already written down in this repository as known, accepted faults.** That is the lesson:
  **a trade-off recorded in a comment is not a trade-off the user has agreed to**, and it will be
  filed as a bug the first time it is seen.
  * **The figures: the lever is the albedo TEXTURE.** 152's escape hatch fired exactly as written —
    `_MOD_TINT` is inert on `Amp_Char_Shader` (`lit=NO`, value read live off the rendering material),
    so all four earlier fits solved for the wrong unknown. Now a multiply on the albedo texture,
    blitted through the bundle's own `HeadUnlit` with `_Color = (k,k,k,1)` so the alpha the
    `_Cutoff` test reads passes untouched. **The fallback fails DARK** — `Levered` is populated from
    a read-back, so a missing blit shader can never make the figure *brighter*. Refit from three
    photographs: p50 and p90 land on **0.12 cellar / 0.20 wood independently**, and that agreement
    is what makes it a fit rather than a wish.
  * **The materialise is the game's own, driven by `_Cutout`** — read out of the decompiled source
    rather than guessed. Both runtime drivers ramp `_Cutout` under `_Toggle_Dissolve`; nothing at
    runtime writes `_DeathDissolvePos`. Following the runtime precedent makes the object-vs-world
    space question **moot instead of guessed**.
  * **The fire carried the wind because the candles play the wind buffer.** `EnvSoundClip.Bed` has
    three callers; two are Air-gated and the three candle beds never were, so the wind clip was
    audible with Air off, and their `+0.90 × Fire` term raised it **+6.2 dB per source**.
  * **...and the roar itself measured as a wind.** Its deliberate 3.81 dB pulse was *smaller than
    the stationary draught's 4.02 dB accident*, because the envelope was normalised by a peak whose
    peak/σ is 3.13. Now 7.47 dB.
  * **`GhvrTipUpright²` is a blend-mode fact, not a tuning**: `EnvParticleAdd` premodulates so the
    sparks' drawn energy already goes as upright², while `EnvGlow`'s is linear in alpha — squaring
    makes wash and sparks shed the same fraction of drawn energy at every instant.
  * **The instrument was wrong in two ways that both made it agree.** `Renderer.bounds` on the shelf
    is the **swept culling volume** (the fall is a vertex rotation), so every station "covered 100 %
    of the frame" — including one **65° off**; and in batchmode `cam.aspect` is the phantom 640×480
    screen's 1.333, not the render target's 1.778, so every horizontal measurement was 33 % too
    wide. Four of seven stations mis-aimed, one **197° off with all eight corners behind the
    camera**. Third mis-aimed station in eight days.
  * **A toolchain finding worth carrying:** written as nested `if/else` inside a branch, glcore's
    shader compiler **process died** on EnvGlow's vertex program, the shader fell back to Unity's
    error pass, and the wash rendered **magenta** — through a bake that reported OK and a preview run
    that produced thirteen confident PNGs. Rewritten as a select.

- **ModBuild 152** (commit `fe71ecf`, bundle 67,138,818 bytes) — **the darkening lever was never
  connected**, and two of the user's own photographs prove it.
  * **`_MOD_TINT`'s fourth component is the shader's BLEND WEIGHT**, and every write this feature
    ever made preserved the material's authoring default of **zero**. Four rounds of colour work went
    through a gate held shut. The shipped game has no path to that state: `Choreographer.cs:851-853`
    writes the tint from `ColorUtility.TryParseHtmlString(ColourHTML)`, which returns alpha 1 for a
    6-digit `#RRGGBB`, and `MonsterYMLData` initialises the string to `#FFFFFF`. `(1,1,1,0)` is an
    ASSET default; our clone bypasses `Choreographer` and inherited it.
  * **And the pixels said so.** Two frames of the same forest event: nominal `Level` 0.321 measures
    the figure at p90 **0.0290**; nominal 0.078 measures **0.0291**. The multiplier fell **4.1×** and
    the figure moved **0.3 %** — the user's "ich sehe keinen Unterschied", as a number.
  * The pop was the same bug: 151's fade was correct and drove the inert lever, so the only visible
    transition was the renderer switch.
  * **A shuffle indexed by the wrong thing is not a shuffle.** Successive apparitions are ~3 slots
    apart and the cast is 3, so a SLOT-indexed permutation lands on the same position every time —
    28-35 % repeats, no better than the hash it replaced. Indexed by the **apparition ordinal**:
    0.0 % over 4000 slots in both rooms, still stateless and still zero wire bytes.
  * **The fire's bed WAS THE DRAUGHT.** The cellar's flame beds have ridden the wind buffer since the
    feature shipped, so a Fire infusion made *wind* louder and eleven seated fires made no sound.
    Three new synthesised layers; 71-87 % of every crackle sits in 1-5 kHz. Rolloff was the other
    half: the candle beds' 0.6 m minimum costs −15.3 dB at 3.5 m.
  * **A second preview station was found pointing at nothing** — `HauntShelf` has photographed a
    table and two candles since the bookcase moved 3.85 m in 147. Second in one week.

- **ModBuild 151** (commit `a7b501d`) — **four causes, three of them invisible to the instrument
  meant to find them.**
  * The apparition's face glowed for two reasons, neither the albedo: an **emissive map**
    (`_UseEmissiveMap = 1`, `_EmissiveMapBoost = 2.0` — emission is ADDED after lighting, so no tint
    round could ever reach it; 190 of 8.3 M pixels over luminance 0.05, all in one rectangle, peak
    0.892 against a body median 0.00015) and **a live point light inside the creature**
    (`LivingSpirit_Light (1)`, intensity 20, range 1 m). `Strip` swept
    `GetComponentsInChildren<MonoBehaviour>()` and **`Light` derives from `Behaviour`, not
    `MonoBehaviour`** — a structural blind spot that also hid `LensFlare` and `Projector`.
  * **The census that should have caught it had a cap of 24 properties, and the shader declares
    exactly 24 interesting ones** — every dump truncated precisely where `_MOD_TINT` would appear.
  * **The instrument watched the wrong transform AND the wrong creature**: `AnimPin` reported 0.00 mm
    for two builds, but `applyRootMotion = false` only stops Unity *extracting* translation — on
    these Generic rigs the travel stays in the BONE CURVES, and the once-per-process latch had been
    spent on a creature that never takes a step.
  * The wind no longer moves the fire at all (user ruling: sparks instead of streaks) — and
    **deleting a term deletes its frequency too**: the first cut also removed the only two
    frequencies in the bonfire path not near-harmonic with the fire's own bands.
  * The ice's white smears were the **plates**, not the seams — measured before anything was tuned.

- **ModBuild 150** (commit `7e2a50b`) — the debug triggers go on the wire, and four findings the
  instruments answered without a line being changed first.
  * Three user rulings, the third governing the other two: debug-menu events **synchronise** (new
    extension record 32, 8 bytes — a state, never a stream, last-writer-wins with an explicit
    release); the easter-egg **frequency comes from the host** (record 31 grew a sixth byte, riding
    the clock record so "host" and "clock owner" cannot be two clients); and **local settings take
    precedence** — the wire carries the schedule override, the local settings carry the permissions.
    Wire format stays v3; every packet of a player not holding a latch is byte-identical to 149's.
  * **A type bug Unity had been printing all along**: on `Amp_Char_Shader_2Side`, `_Diffuse` is the
    albedo **TEXTURE**. `HasProperty` answers true, `GetColor` logs an error and returns
    `(0,0,0,0)`, and the near-black guard then dropped the material **silently** — 37 % of everything
    the figure emitted. A uniform multiply was also making the figure MORE colourful as it darkened
    it (mean saturation 0.253 → 0.437), hence the desaturation toward the room's light colour.
  * **"Teleportiert sich" was the gait.** `RunBlend` is a blend WEIGHT, not a speed, held at 0.55 for
    every creature at every speed while the game's own sustained travel drives it to 1.0.

- **ModBuild 149** (commit `95d6d97`, bundle 67,154,625 bytes) — seventeen user findings, and the
  through-line is a **class of bug**, found three times in three unrelated files by three lanes.
  * **AN ELEMENT STRENGTH MULTIPLIED A FREQUENCY THAT IS THEN MULTIPLIED BY ABSOLUTE TIME.** `t` is
    the shared environment clock and reaches thousands of seconds, so
    `GhvrWave4(t * (0.612 + 1.05 * storm))` sweeps its argument by `1.05 * t` **cycles** while
    `storm` ramps over one second — 1890 periods at t = 1800 s against a carrier running at 1.6. The
    phase scrubs chaotically for exactly the ramp and then locks. **THE SIGNATURE IS THAT IT IS
    CORRECT AT t = 0**, which is why every preview and every early test passed. Found in
    `EnvGrowth.GhvrWind` (the user's "die Bäume zucken"), `EnvBeam`'s shimmer, and
    `EnvFire.GhvrFireHz` — the last of which also drives the seated wash and the glut on every room
    and ground surface. Fix: **two carriers at fixed rates that the element crossfades**, endpoints
    bit-identical. Worst per-frame leaf-tip step 63.4 mm → 3.3 mm, and flat in the clock.
  * **The fire's "Fäden": the erosion field is sampled in CARD UV, and UV is not square in metres.**
    On a card 0.39 as wide as tall, a field cell measured **3.02:1 vertical** — the holes *were* the
    threads. The rejected fix is written up in the file: squaring the cell by *lowering* tileU leaves
    under a third of a period across a card, and straight-edged parallelograms come back.
  * **"Die Glut fehlt auf dem Asset" — the term was never absent, it was a smooth wash**, which on
    wood reads as *a light shining on wood*. Hard-thresholded object-space noise took p99/p50 of the
    added light from ~1.0 to 57× on the deadfall. **A wash and a texture are different objects.**
  * **A preview station that points at nothing does not fail — it renders, and it agrees with you.**
    Both cellar-shelf preview cameras had aimed at the shelf's *old* position for several builds,
    which is why a fire floating 18.6 cm above a board survived a whole round of previews and had to
    be found on hardware.
  * **The figures: an honest negative result.** The canopy hypothesis is TRUE about the bake (the
    floor keeps 9.5 % of the moon the figure's SH gets at 100 %) and is still the **wrong lever** —
    `Amp_Char_Shader` has no ForwardBase pass and never samples light probes, so the SH is a
    *measuring instrument*, not a light. Applying the correction makes the two-point fit demand
    `DarkFloor = −0.13`, i.e. inexpressible; it is logged and not applied. The old `DarkFloor` alone
    was **three times the cellar's whole wanted answer**, and `MaxLevel` needed a luminance neither
    room can reach, so it was never a clamp.
  * **The game DOES have root motion.** `ActorBehaviour.ApplyMotion` harvests and cancels it every
    `LateUpdate` — which is why nothing in the game ever assigns `applyRootMotion` and why this
    project's own doc had it backwards: the prefabs ship with it **on**.
  * **A comment is not a guard.** `HauntWindowEnv` held 7.60 s while `CardSeconds` held 5.50 s for
    four builds, and the comment beside it *warns about exactly that failure*. `check-mirrors.sh`
    lints C# against C# and cannot see a bake constant, so `AssertHauntCards` now compares them and
    fails the bake — proven to fire.
  * Also: the ice was **one lerp toward a constant blue** with both call sites then *flattening* the
    normal (a puddle by construction); Light in the cellar reached every surface because the moon is
    an unoccluded directional (now traced back to the window plane — 3.22× inside the throw, exactly
    1.00× outside); the candle halos had escaped the winding-fix retune on a comment citing two
    element rulings that say nothing about authored alpha; the bookshelf's impact **was playing all
    along and was spectrally inaudible** (97 % of its energy below 500 Hz into a speaker that returns
    nothing under ~200 Hz).
  * **Numbering:** thirteen comments claimed "ModBuild 149" for the round that shipped as **148**;
    they were retargeted. Lanes label their work with a guessed number — check it at merge time.

- **ModBuild 148** — the second pass over the SAME nine subjects, and the through-line is
  that **four separate findings had a cause other than the one reported**. Worth reading as a set:
  * **"Die Figuren teleportieren sich"** was not a jump but a **full rebuild**: a latched trigger's
    loop period is event length PLUS a gap, and in that gap the event counted as ended, so the clone,
    its materials, its Addressables child and its light bind were destroyed and rebuilt. Nine `armed
    at` lines 2.5 s apart on one latch in the log prove it. 147's guard against exactly this was
    **dead code** — the gap retired the figure before a re-anchor could ever be recognised.
  * **"Die Texturen laden zu langsam"** was our own dissolve. A half-streamed texture is flat grey; it
    is never a swirl with holes shaped like a noise field.
  * **"Die Figuren sind voll angestrahlt"** — and 147's fix could never have worked: **SH is additive
    ambient**, it can add light and never remove it, and the game's own scene lights reach the mod
    layer and cannot be masked per renderer. The lever that does work was found in the game rather
    than guessed: `Choreographer.cs:826` names the character shader `Amp_Char_Shader` and drives
    `_MOD_TINT`, a whole-model albedo tint. GENERAL RULE: **to make something darker you need a
    multiplicative term; no amount of ambient will do it.**
  * **"Die Pfütze ist verschwunden"** — it never was. A LOCATE pass returned 45,895 px with `Cull
    Back` and 45,895 with `Cull Off`, identical to the pixel. It was unrecognisable: its sheen's
    Fresnel was half angle-INDEPENDENT (milk, edge to edge) and its rim is a 32 cm feather, so it had
    no edge to be seen by. And **the bright patch that survives full Dark is not the puddle at all** —
    it is the moonbeam's landing pool, 31 cm away, on a particle path with **no moonlight term**.
  * **The moss is DELETED**, on his fourth complaint. The general rule is the one that generalises
    furthest this round: **a function of the albedo has no silhouette** — it computes inside the
    object's own outline, which is the definition of a stain. Three rounds each answered the
    *adjective* and were each right about it and wrong about the category.
  * Measured while fixing the sound: the shelf's impact fired **3.68 s early**, when the shelf had
    leaned about one degree; the ice was **Q ≈ 157**, a tuning fork, on a 0.45 s beat inside the band
    the ear reads as rhythm.
  * **The forest's growth annulus is r ∈ [4.90, 11.50] m against a 4.5 m play radius** — nothing grows
    in the clearing the player stands in, which is why Earth never changed it. Now planted inward to
    1.75 m with a height taper; the board's own footprint shows 0.00% of pixels changed.
  * **THE SEVENTH WINDING BUG, and the worst of them.** `Env_GlowSphere` shipped INSIDE-OUT: `Cull
    Back` kept the FAR hemisphere while the normals pointed outward, so `dot(N,V)` was never positive
    and the halo's core term was exactly 0. **Every halo in both rooms** — nine fire halos, the
    window, the wisps, the lantern — had been drawing nothing but a **one-pixel seam at its geometric
    limb**, and that 16-gon seam is the "Striche" in the user's own `feuer1.jpg`. Every `_Tint.a` in
    the project had therefore been authored blind against an invisible halo. GENERAL RULE, now seven
    times over: **a new mesh gets a closed-and-outward gate, and the gate must be PROVEN to fire.**
  * The handprints were **5.5× life size** — and in VR the player has their own hands in frame as a
    scale reference, so no texture could ever have saved them.
  * The draught was the **comic-book speed-line**, and the invariant across BOTH of his rejections of
    that effect is not the sprite shape but **bright matter in unlit air**.

- **ModBuild 147** — nine user findings, eight lanes: the apparitions became the game's own monsters,
  the fire's "zappeln" was a spectral ASSIGNMENT fault (a 40 cm tongue driven from the 3.6 cm band),
  the wind fault was **an element strength multiplying a FREQUENCY** in all three of plants, fire and
  trees, the window gained a real sky behind it, the shelf fell on the pendulum separatrix and stood
  up on that same curve rewound, Light indoors moved out of the ambient and into the moon, and the
  moss became fungi. **Bundle 66,332,888 bytes.** Found in passing: BOTH JAMBS of the window reveal
  had been wound inside out since they were written — the **fifth** mesh in this project wound against
  its own viewer — and the wisp ambience had never played, because the bake names that node
  `WispWisp`.

- **ModBuild 146** — **HOTFIX: 145 froze on the loading screen.** No wire change, **bundle
  UNCHANGED from 145** (C#-only — reinstall the plugin, keep the bundle).
  Reported on entering a forest scenario, but it is neither the forest nor the environment: the
  clip bank is style-independent, so the cellar would have frozen identically.
  **Root cause — a `while` condition tested against a convergent series.**
  `EnvSoundBank.MakeCreak` scheduled its stick-slip bursts as `while (t < 1.20f) { …; gap *= 0.90f;
  t += gap * jitter; }`. Those times are a **geometric series that sums to ≈1.09 s against a 1.20 s
  window**, so the condition could never go false; after ~800 passes `gap` underflowed to a
  denormal and `t` stopped moving at all. Infinite loop on the main thread, inside the first
  `EnvSoundBank.Build()` — which runs on the ONE frame the room is first placed, i.e. at the end of
  scenario loading. Verified by re-running the loop's arithmetic outside the game at 48000/44100/
  24000 Hz: `t` converges to 1.02–1.11 and never reaches 1.20.
  **Why the log said nothing.** A spin throws no exception, so the generator's own `try/catch` was
  blind, and the bank logged nothing on the way through. `Player.log` therefore ends on
  `SkyAlternative`'s "ROOM placed" — a line written by a *different* subsystem a few statements
  earlier. **GENERAL RULE:** the last line in the log names the last thing that *finished*, not the
  thing that hung; on a silent freeze, enumerate everything that starts on that frame.
  **Fix — structural, not a bigger constant.** `Core/EnvSoundSchedule.cs` owns the burst train: the
  caller states how many bursts, the loop is a `for` over that count, and the gaps are normalised
  *afterwards* so the last lands exactly on the end of the window whatever the shrink does. **The
  span is an input now, not an outcome of a series.** Free of everything but `Mathf` so it links
  into the wire tests — 37 new assertions (1520 → **1557**) pin termination, the span at six shrink
  factors, monotonicity, determinism and every degenerate input, and a regression **hangs the test
  run**, which is louder than a red line. The bank also logs one line when it finishes, with clip
  count, rate, size and milliseconds.
  **The bug class to watch for:** a float accumulator whose step shrinks multiplicatively, compared
  against a fixed bound. Audited the rest of `src/` — every other `while` is bounded by a count or a
  hierarchy walk; `MakeSkitter` had a constant step floor and was safe, and was moved onto the same
  schedule anyway so the pattern is gone from the file.

- **ModBuild 145** — the shelf riders and the fire, the two things 144 owed. No wire change.
  **Bundle 65,793,726 bytes.**
  **Shelf riders** (a bug the user ruled on). `GhvrShelfTip` moved into its own header and **the
  shelf became a rider like the rest**, so there is no privileged copy of the curve for a rider to
  drift from. Each rider is authored where it stands, so `vertex − hinge` already exists in its own
  object space; only the HINGE travels, through the same door `ApplyRig` uses for the candle
  positions, with a build gate asserting it survives the round trip to under a millimetre. The
  flame rotates rigidly, bends back about its own origin, lags along the true tangential velocity,
  and GOES OUT a quarter-turn into the fall. Not riding, stated plainly: the spark emitters
  (Shuriken simulates in world space; the bundle has no scripts) and the wash on the wall.
  **Fire, third attempt — and the failure was finally MEASURED**: the sprite was the CANDLE flame
  texture (one laminar teardrop, which enlarged is a big candle) and the "turbulence" ran at
  0.63–1.03 Hz where real fire is 3–8. GENERAL RULE: when two rounds of tuning fail to change a
  reading, measure the primitive instead of tuning the parameters. New atlas (wide holed bed, torn
  tongues, ragged puff), 4.6 Hz, pieces that DETACH and die. The forest burns for the first time.
  **Fire+Light gives the fire a SMOKE PLUME** — smoke is only visible when lit, so the physics that
  would have made that pair the loser is what makes it work.

  **NOT MERGED, and waiting in a worktree:** the ten non-fire element pairs and the 64-subset
  arbitration. The lane finished and is sound, but its base predates the fire lane and a `--3way`
  apply half-landed (shaders calling `GhvrPair*` helpers that had not arrived). Backed out cleanly
  rather than shipping a broken bundle. **Two findings from it that matter regardless:**
  (a) the weighted-blend composition rule — `value = (base + Σ wᵢ·targetᵢ)/(1 + Σ wᵢ)` with
  `wᵢ = aᵢ·bᵢ·authority` — which is what makes any of the 64 subsets compose without summing
  contradictory targets into mush; (b) **the forest has no light source that Dark spares**, so
  every subset containing Dark reads mostly as "dark" there, where the cellar keeps its candles by
  the user's own ruling. That asymmetry needs a ruling. (c) Shuriken emitters run with
  `useAutoRandomSeed` ON, which makes previews incomparable across bakes AND is a multiplayer
  determinism hole — pinning it is a real fix, not just tooling.

- **ModBuild 144** — TWENTY of 22 reported items, plus environment SOUND. No wire change.
  **NOT IN THIS BUILD: the two fire items** (the cellar fire still reads as candle flames; the
  forest still has no burning trees). The lane was stopped before it landed. The RECEIVING half of
  the fire wash did land — `EnvRoom`/`EnvGround` carry `_FirePos0/1/2`, `_FireCol`, `_FireRate` —
  and sits unread and black until a sending half arrives.
  **KNOWN BUG shipped in 144:** the candles and the fire standing ON the bookshelf do not tip with
  it (their wax, flame and halo are separate meshes/shaders). The user has ruled it must be fixed.
  **Bundle 65,681,683 bytes.**
  **"Keine 2D-Pappaufsteller" — said seven times in one message.** Every apparition is now real
  geometry, posed at bake time from the MakeHuman CC0 base mesh by a new pipeline script. Assets
  were searched first, per the user's standing instruction: Smithsonian Open Access IS usable
  (unauthenticated CC0 OBJ/glTF) and was rejected on non-technical grounds — its human forms are
  identifiable historical people, and **an apparition you can recognise is a statue, not a
  fright.** GENERAL RULE that came out of this: the earlier "no rigged FBX, so build it yourself"
  finding was about ANIMATION, not geometry — apparitions that barely move need no skeleton, and a
  600k-triangle museum scan is good SOURCE for a decimation step this project already has.
  **Three apparitions were invisible, and the cause generalises**: they were authored as "a hole in
  the scene" — coverage with almost no light. That works against a lit window or a candle-lit wall
  and is nothing against a wood whose 95th percentile is 0.005. **A hole cut in nothing is
  nothing.** Value plans must name the background they are cut against.
  **THE PUDDLE HAS BEEN INVISIBLE SINCE MODBUILD 134** — `PuddleMesh` wound face-down against
  `EnvPuddle`'s `Cull Back`; ~600 triangles discarded from every camera above the floor. It hid for
  ten builds because the moonbeam's pool lands 35 cm away and a bright wet patch in roughly the
  right place was taken for it; the tell was ModBuild 143's own note that tripling the ripple
  "moved a measured maximum of 3/255". **Fourth instance of the winding bug class.** Found by a
  LOCATE pass — paint it magenta, count pixels with `Cull Back` (0) vs `Cull Off` (134,000).
  **Ice could not have been tuned into ice**: two of its three terms were wrong in principle. A
  sheet of ice is NOT a better mirror than water — it is a rough scattering dielectric, duller and
  broader, and bright from straight above where water is a black hole. Sharpening the lobe made a
  stiller puddle, which is exactly what the user reported.
  **Moss stopped being a stain** by getting the five things a stain cannot have: micro-relief with
  an analytic gradient, its own normal that DESTROYS the one underneath, two greens mixed by the
  relief, a contact lip, and gloss only on the thin frontier. Two failed bakes on the way are
  instructive: an analytic pattern at frond scale has no mip chain (moiré), and four fixed axes are
  quasi-periodic (honeycomb) — fixed by warping the lattice with the patch field itself.
  **`EnvGround` had NO element response at all** — it belonged to another lane the round the
  channel landed, so the forest floor, which is what "die Lichtung" mostly IS, never read Light or
  Dark. Measured 1.00× before, 1.71×/0.60× now. In the cellar the candles are provably untouched
  (+1.3% / −1.1%) while the moon moves 2.09×/0.45×.
  **Environment sound (new)** — spatialised, synthesized (BBC's library is RemArc, NOT
  redistributable in a public mod), switchable, on the shared clock. It exposed that **there was no
  AudioListener on the VR head**: the ear sat at the parked flat-screen camera, so every positional
  game sound has come from the wrong place. The room runs at 21–26× scale and Unity's rolloff is in
  WORLD units, so all distances are stored in PERCEIVED metres with exactly one conversion site.
  **The frequency dial and multiplayer** — worth carrying because the user asked: no value "wins"
  and none has to. The dial is a monotone gate on a setting-independent hash, so a player at 0.9
  sees a strict SUPERSET of a player at 0.4 — same apparition, same place, same second, the lower
  setting simply misses some. A host-wins rule would need a wire record AND would override a
  comfort setting the player chose.

- **ModBuild 143** — seven reported items, all environment craft. No wire change.
  **Bundle 65,331,900 bytes.**
  **The rat: two one-line defects, both instructive.** Its entire albedo was a lerp between two
  greys 1.4 stops apart — measured on the shipped build at 43–53 luma, flat, over the whole animal,
  which is why it read as untextured. And "it shrinks instead of entering the hole" was literally
  `wp = lerp(P, wp, vis)`, a shrink written back when the hole was a black rectangle painted on the
  wall; the file's own header admitted it. Now a coat (pigment ramp + sin-free noise evaluated in
  the AUTHORED position so the fur does not swim as the body waves + bare-skin mask in the one free
  vertex channel) and a real entry down a bore derived from the route's own tangent — the south
  route arrives 60° off the wall normal, so the pocket is sheared to match. Hidden by stone after
  47 cm and PARKED there between crossings.
  **Real fire**: six seats, each with a cause in the frame. A burning crate is not a big candle —
  `EnvFlame` needed a bonfire branch (wide low bed, tearing tongues, temperature ramp), and the
  first bake proved it by rendering a row of tall candle flames on a crate.
  **Surface growth — Ice and Earth are ONE problem**: a coverage that advances and retreats through
  a fixed pattern, not a tint that fades. Per-pixel AFFINITY (noise in room metres + the normal
  map's own grain + where frost and moss really start: wall feet, mortar, shade, damp) with the
  element sliding a THRESHOLD down through it. GENERAL RULE, and the reason 142 failed: a global
  fade of a tint can never read as growth, however well tuned — the frontier has to be spatial.
  Grass/moss cards FOLD FLAT onto their own base edge at rest: zero area, no fragments.
  **Wind**: the prior art is unanimous — Crytek, Unity Tree Creator and SpeedTree all use the
  cubic-smoothed triangle wave, not `sin()` (no transcendentals, bounded time argument, |wave| ≤ 1
  by construction). Tip amplitude 4.5 cm chosen AGAINST the canopy shadow map's 5.5 cm texel, so
  the baked shadow cannot visibly disagree with the leaves moving under it.
  **Moon**: Dark slides Earth's umbra across the disc with a Danjon copper gradient — a SHADOW, not
  a body, because a body would have had to occult stars. Light swells it ×1.34 in RADIUS, not area,
  because the caller already multiplies 1.9 and 1.9 × 1.8 is a headlight. Both drive
  `GhvrMoonLight()`, which the room shaders multiply into their moon term, so the wood darkens
  BECAUSE the moon is being covered. **The cellar never sees the disc** — the lane rendered it and
  recorded the negative result; there, both elements arrive only as the beam's brightness.
  **AN INTEGRATION FAULT OF MY OWN MAKING**, found at the merge and worth carrying: two lanes built
  the two halves of the element channel in the same round, each declared the uniforms it needed,
  and each picked the obvious name — one made `GhvrElems` a struct, the other a function. It
  compiled everywhere EXCEPT in the one shader that wanted both headers. LESSON for splitting work
  by file ownership: a shared NAMESPACE is a shared file even when the files are disjoint. Name the
  contract in the brief, as I did for `GhvrMoonLight()` and `_GhvrHauntForce` — those two crossed
  lanes without a scratch.

- **ModBuild 142** — four reported items. No wire change. **Bundle 65,226,455 bytes.**
  **Element effects, the art half.** 140 shipped only the sensor, so the user saw nothing with Air
  up — and his log proved the sensing side was fine (`Air=Waning now 0.00 -> 0.40±0.12`, nothing
  reading it). All six now reach both rooms through nine `Env*` shaders and seven new emitters.
  **The zero state is proven bit-identical** (md5-equal renders for channel-off vs
  all-six-Strong-with-master-0 vs master-on-all-inert), which is what protects the levels tuned
  over ModBuild 133–139. **Light and Dark are not one axis**: ambient is lifted by Light only while
  Dark is down and crushed to 20% by Dark, while SOURCES are lifted by Light and HARDENED by Dark
  — both strong gives a black room with small fierce candle pools, not a grey average.
  **Horror redesign — a grammar problem, not a tuning one.** User verdict on 141: "weit entfernt
  von echtem Horror … eher lächerlich", and the render is literally an emoji. A handful of SDF
  primitives in a fragment shader can only produce A SILHOUETTE WITH FEATURES DRAWN ON IT, which is
  the grammar of a pictogram. What frightens is VALUE, and value needs a modelled surface with cast
  shadows — so the apparitions are baked from a CPU-rendered atlas (depth field, grazing key light,
  40-tap heightfield shadow march, cavity occlusion). GENERAL RULE: if a thing must look
  photographic, bake it; the fragment shader is where you spend ALU, not where you get craft.
  Two bugs found on the way, both would have shipped silently: `SaveMesh` drops UV1–UV3 when
  overwriting an existing asset (each re-bake kept the PREVIOUS catalogue's ids against the new
  positions — it looked exactly like a shader bug), and `floor()` on an interpolated integer picked
  the wrong atlas tile at 4.0 minus one ulp.
  **Shaft striping — the best diagnosis of the session.** The beams were combed into hard 5.5 cm
  vertical teeth. A shaft runs ALONG the light and both shadow-map axes are perpendicular to it, so
  the map coordinate is **exactly constant down a beam**: a vertical strip of blade reads ONE TEXEL
  COLUMN from canopy to floor, and one texel sideways swaps one of seven taps, which the bite ramp
  amplifies to 0.375 of full shadow held over the entire length. The floor never showed it because
  a floor moves in u AND v AND depth at once. **And my own verification was blind to it**: the
  probe walked the beam AXIS, the one direction in which the comb cannot appear, so the log
  reported a perfectly smooth profile while the picture showed a comb — both true. LESSON: a probe
  that samples along the axis of the thing it measures proves nothing about its cross-section.
  Fixed by splitting occluders by SCALE — trunks keep the crisp map and the bite ramp, needles get
  a separate 128² mass map (exact 4×4 area averages, bilinear, linear response, 64 KiB). Worst
  across-beam step 2.03% → 0.22% while the beam's own across-contrast ROSE 5.4% → 8.8%.
  "Shafts through trunks" is the necessary geometry of a beam parallel to the light (what shadows a
  point lies on that point's ray to the moon, which IS the beam further up) — not a bug; what was
  wrong is one shaft whose axis sat 0.21 m inside a trunk.
  **Test triggers** (new Advanced page): each element (Strong and Waning) and each apparition on
  demand. LOCAL ONLY — the element board is a multiplayer desync invariant, so the buttons override
  the PUBLISHED value, never the game's state.
  **Open:** the cellar's shelf face reads as a dark rounded blob in my own render — it is backlit by
  the candle behind it and shows no internal value. Needs re-siting or a key from the candle side.

- **ModBuild 141** — HORROR EASTER EGGS (user feature request), forest and cellar only. No wire
  change. **Bundle 65,265,924 bytes.**
  Twelve apparitions, six per room, plus the rat stopping to stare (6% of crossings, free). Slot
  beat 83 s, one per slot at most, shipped at half frequency → median 1.9 min apart, shortest
  quiet stretch 41 s.
  **The sync is the rat's schedule pattern generalised**, and that pattern is now the project's
  answer to "make it random but identical everywhere": cut time into slots off the SHARED clock,
  make every decision a hash of the slot index, and never let `Random`, per-client state, head
  input or `sin()` (vendor-divergent last bits) into it. Zero wire bytes, because the clock is
  already shared and element state is already replicated.
  **Nothing re-orients with the head** — the faces are world-fixed cards aimed at the BOARD, which
  is where the player is anyway. That is the trick that makes "it looks at you" legal under the
  permanent no-billboard ruling.
  Placement respects the play space (cellar ≥ 4.95 m from the board, forest ≥ 7.6 m); nothing
  flashes or lunges; longest event 8.6 s, shortest 0.34 s. Cost +12 tris and one draw call per
  room, no textures — five of six cards collapse to a point at any instant.
  **Elements FLAVOUR the haunt instead of fighting it**: Dark ×1.59 frequency and blacker, Light
  ×0.65 and sharper-edged with a dark contour (so a pale apparition cannot be washed out), Fire an
  unsteady warm rim, Ice ×1.35 duration / ×0.35 motion, Air drift, Earth sinks it. Read straight
  off `_GhvrElemA`/`_GhvrElemB`.
  **Assets: procedural SDF after searching six CC0 sources.** No CC0 face texture with an alpha
  channel exists; the only CC0 human faces are 100k–600k-triangle museum life masks that cannot
  smile, and the bundle forbids MonoBehaviours so nothing rigged could animate anyway. Drawn
  shapes morph (the grin widens while it watches) and stay crisp at 25 m.
  **Verified by rendering, not by intention** — `ENV_PREVIEW_OUT=… xvfb-run … RenderAll`, and the
  preview solves the SHIPPED schedule backwards to an instant the event is really running rather
  than forcing it with a debug constant. Two rounds of retuning came out of the renders: bodies at
  0.020 linear rendered as the brightest thing in frame (the wood's blacks are 0.002), and two
  SDFs had their heads swallowed by their own torsos.
  **Open risk for hardware:** the five dark cellar silhouettes are borderline — in my own render
  the window figure is barely separable from the wall, and a preview is BRIGHTER than the headset.
  The beam-dim (−45% measured) carries that event even if the figure does not. Knob is `_Rim` plus
  the per-card colours.

- **ModBuild 140** — five reported items + the sensing half of the element feature. No wire change.
  **Bundle 65,255,033 bytes — 140 NEEDS it.**
  **Focus pin.** The board waits for a hex pick from the acting character, but the VR focus could
  be moved to another one — and the confirm caps follow the FOCUS while the pick follows the
  ACTOR, so the player was left unable to confirm. `CharacterFocus.PinnedActor` requires all three
  of: a real targeting wait state read LIVE from `Choreographer.m_WaitState` (not the mod's
  event-driven mirror — a gate that takes a control away may not fire on a stale bit),
  `ThisPlayerHasTurnControl`, and `CurrentPlayerActor` mine and alive. Clearing the OVERRIDE (not
  the selection) is what returns the view, so no "nobody selected" state can appear. Use
  `CurrentPlayerActor`, never raw `m_CurrentActor`: during exactly these waits the game re-points
  it at the driven figure (`Choreographer.cs:4269, 9878, 10013`) and a summon there is a `CActor`.
  **Forest shafts — the third pass, and both earlier ones were wrong in ways worth carrying:**
  (1) a candidate search that maximises "how CLEAR is this beam" finds beams with nothing in them;
  the objective was the inverse of the goal. (2) **Alpha-test speckle averaged LINEARLY is a
  uniform dimming, not a shadow.** A fir crown is a sieve (23.6% of the atlas over cutoff), so a
  beam collects 0.2–0.5 coverage spread over metres — which is why "42% occluded" and "perfectly
  smooth beam" were both true, and why more throw could only have made it greyer. Fixed by a BITE
  RAMP (below a coverage threshold nothing casts, above it everything does; the PCF taps still
  average BEFORE the ramp so edges keep their penumbra) plus a minimum-visibility floor, which is
  what lets throw and bite be aggressive without extinguishing a beam. GENERAL RULE: when a
  visibility term is built from many small partial occlusions, average then THRESHOLD — a linear
  mean of speckle is grey.
  **Measure before tuning:** the lane computed EnvGround's own arithmetic and found the moon is
  **77% of the lit floor's brightness**, so `_DirScale` stayed at the user's approved 0.38. Lit vs
  shadowed now reads 2.04x (deepest 2.84x).
  **Forest ground props.** 'Roots' was a 2.4 × 2.7 m photoscan of forest floor **0.17 m thick** —
  a ground decal shipped as geometry, with no height at which it works; removed. The stumps' pale
  aprons were `GroundLift`'s 99.5th-percentile rule, right for a barrel and exactly wrong for a
  shell: new `Bed()` measures how far a prop's BEARING SURFACE stands over the ground under it and
  drops it, never lifts (Stump0 −18.7 cm, Stump1 −16.1, nine more 1.5–10.8 cm).
  **The rat was inside-out** — `AddTube` wound every triangle against the normal it handed the
  same vertex, so `Cull Back` discarded the near surface and drew the far one: you looked INTO the
  animal and saw its legs. **Third instance of this class of bug** (after the ModBuild 137 moon
  hull), so it now has a build gate — closed, outward, signed volume — which reports the old mesh
  as 82 unpaired edges and 282/282 triangles backwards. Its walk became a SCHEDULE hashed off the
  shared clock (2541/2541 crossings unique, 30% turn back, 32% sniff); `sin()` is banned from the
  hash because its last bits differ between GPU vendors and the schedule must be bit-identical.
  **Rat holes** were one flat black quad each; now really cut out of the wall through `WallMesh`'s
  own holes array, derived from the route endpoints, with an asymmetric arched mouth, a bent
  pocket, a ring overlapping the courses, and darkness as a vertex-colour gradient.
  **Element mood (new, sensing half).** Six elements polled — the game offers no event and nine
  methods mutate the board — smoothed and published on two global shader vectors. ZERO WIRE:
  element state is replicated by the game and desync-checked every round (`ScenarioState` codes
  117/118). Strong 1.0, Waning 0.4 BREATHING 0.28–0.52 on the shared clock (so the player can see
  an element is about to go out), Inert 0. It keeps sensing under MR **by ruling**: the MR rule is
  about GEOMETRY over passthrough, and a published number is not geometry — the "additive only,
  board-anchored" constraint belongs to the art that reads it.
  **Tooling discovered:** `EnvironmentsPreview.RenderAll` runs headless under `xvfb-run`, so
  environment lanes can render before/after and compare instead of shipping numbers. Previews are
  brighter than the headset, so only structural claims are settled that way.

- **ModBuild 139** — three reported items. No wire change. **Bundle 65,245,628 bytes — 139 NEEDS
  it** (the canopy shadow map is new bundle content and the cellar's meshes changed).
  **Turning is never blocked** — the ruling hardened from 138's "not another seat's targeting" to
  unconditional. 138 stopped one scope short: the acting player placing a waypoint is in
  `BoardTargeting` WITH turn control, so the 137/138 gate said "MINE" and stood turning down — but
  `AoeControl.CanRotate` declines there too (`CurrentDisplayState == MovementSelection`, and
  `WaitingForTileSelected` is an explicit refusal). Same mistake as on the remote peers, one scope
  smaller: the stick taken for a consumer that had declined it. Two changes, because "never" admits
  no exception — (a) `LocalTurnControl` asks the CONSUMER (`AoeControl.ClaimsStick`, computed from
  the gates `Tick` applies) instead of testing `VRMode`; (b) `AoeControl.ResolveRotationHand` moves
  pattern rotation to the hand `[Comfort] TurnHand` does NOT use, so the claim cannot land on the
  turn stick at all. `TurnMode=Off` frees it and rotation keeps the primary hand. `Flight`'s strafe
  test became hand-accurate (after the split, AoE and flight share the LEFT stick on the shipped
  defaults). GENERAL RULE: arbitrate a control against what really reads the axis this frame, never
  against a game STATE — a state is not a claim.
  **Forest — the trees cast shadows** (user: the shafts "clippen durch die Bäume"). No Unity Light
  exists in these prefabs to cast from, so it is baked DATA: an orthographic depth map along
  `MoonDir`, 512², 5.5 cm/texel, read by `EnvShaft` (blades) and `EnvGround` (the moon term only —
  ambient, all three point lights and the landing pool are separate addends and cannot be
  darkened). Three findings that each cost a pass and generalise:
  (1) **A depth, not a mask.** The shafts run UP through the canopy tear; orthographically a
  shaft's top and the boughs beside it are the SAME texel, so a mask blacks out exactly the thing
  ModBuild 137 exists to show. Only a depth answers "is the segment from here to the moon blocked".
  (2) **Alpha-cut cards are not solid.** Rasterised as opaque quads the crowns became a lid: 81.2%
  of texels occluded and all three shafts scoring 0% clear — it would have DELETED the shafts.
  Alpha-testing against the sprig atlas drops 2.4 M texel writes.
  (3) **Nearest is the wrong layer.** The nearest occluder in a texel is almost always the canopy
  20-30 m up-light, so the throw test measured the floor against the ROOF and the trunk four metres
  away never voted — 5.2% clearing shadow. The map now carries TWO 16-bit layers (nearest + deepest,
  hence RGBA32, 1024 KiB): the floor reads the deepest (exact for a floor), a blade takes the
  stronger of both. Clearing shadow 5.2% → 28.8%.
  **MAXIMUM THROW** (floor 9 m, blades 4 m, soft release) is a deliberate departure from physical
  exactness: exactly, nothing in a wood this dense is lit, and the authored fiction is the approved
  one. A blade is inside a trunk for ~1.2 m of its own length, so 4 m is "the tree this beam passes
  through" and 9 m is "the trunk whose shadow rakes the clearing".
  **Cellar — the box broken** (user: perfect 90° at every junction, cubes under the beams). The
  cause was in the wall code: `WallMesh`'s inward bulge TAPERS TO ZERO at every edge and hole rim,
  so the one place irregularity was wanted was the one place it was switched off. Now: rubble
  skirtings that heap and thin along each run (cleared at the stair and the rat's route, taken from
  `SnappedHole` and the rat's own Bézier), four corners each treated differently, a crumbling cove
  and three wall plates under the ceiling, hewn corbels instead of `BoxMesh` cubes, beams that sag
  and wander and are DERIVED from the two corbels under their own ends, and a plank plane that sags
  between them. The twelve transforms became TWO welded meshes — required, not an optimisation:
  the light rig bakes in OBJECT space, so twelve transforms sharing a material are all lit from the
  first one's position. −10 draw calls, −10 materials, +2346 tris. `PruneUnreferenced` deleted the
  14 now-unused beam/corbel assets, as designed.
  **Process note:** `scripts/build-bundles.sh` only PACKS. The environment prefabs (and any bake in
  them) come from a separate run — `-executeMethod GloomhavenVR.EnvironmentsBuilder.BuildAll`. A
  green bundle build that silently did nothing is what hid the first bad bake for a full pass.

- **ModBuild 138** — the big multiplayer round: 16 reported items. **Wire gains record 31.**
  Bundle 64,474,086 bytes — 138 NEEDS it. Root causes worth carrying:
  **Locomotion is local.** A peer's targeting froze everyone's snap-turn AND stick strafe:
  `SnapTurn.cs:88` / `Flight.cs:303` gated on `VRMode.BoardTargeting`, which
  `VRModeStateMachine` derives from `Choreographer` state that `ProcessMessage` sets on EVERY
  client (ownership is decided *after* the assignment). Matched log pairs from both machines on
  the same NetworkAction event prove it. New `Rig/LocalTurnControl` uses the game's own
  `ThisPlayerHasTurnControl` (true offline ⇒ SP bit-identical). RULE: another actor's turn may
  gate GAME actions, never the local player's locomotion or view.
  **Empty selection** was MP-only: `CardsHandManager.SwitchHand` has no ownership test, so the
  presented hand follows whoever acts; `CurrentHand()` correctly answers null for a foreign
  hand and `CharacterFocus` latched that as 'nobody' (and `Hide()` never clears `currentHand`,
  hence through the whole enemy phase). Selection floor added.
  **Capes**: Unity `Cloth` (no custom solver exists in the game), coefficients are absolute
  metres cached at spawn and never re-seeded; suspend during resize → rescale → resume.
  **Peer card fronts**: `FullAbilityCardAction`'s skin refs lack `[SerializeField]`, so
  `Instantiate` cannot copy them, the clone's `ApplyImage` early-returns, and a null sprite
  draws Unity's built-in WHITE texture. `SetSkin` replayed pre-activation. **Item chips**: the
  punch-out was fine — `CardH = CardW*1.15` was a guess for a card that measures 270x258.
  **The peer's 'box' was never the decision dock** (record 12 published nothing outside the
  damage prompt) — it is a use-bar slot tile, anonymous BY DESIGN. Resolved locally instead:
  the bars are per-client singletons the game raises from replicated messages. Same fact
  answers finding 8 — `CheckForInitiativeAdjustments` raises the bar on every client and gates
  only the ready button, so a teammate's client docked it on ITS board showing another
  character; the guard compared against the EXPLICIT focus override, null while following the
  game. Both halves closed (sender withholds, local dock refuses foreign-only bars).
  **The cap-state byte** was sampled, logged 46x and never assigned into `PresenceState` — one
  line; it made the pile cue dead and every mirrored skip cap paint through the DISABLED path.
  **Bar size dials** bounded an intermediate that is exactly 1.000 at the shipped zoom
  (his drop 3.368283 vs shipped 3.3683) — unreachable, removed; the band lives in code.
  **'50/50'**: `UISliderBar.amountTexts` is a LIST and the placed control also carries an
  inactive gamepad key-tip label; only ONE was bound. All are now.
  **Moonbeam**: hull wound against `Cull Front` (a debug pass measured ZERO hull pixels from
  inside) + a point sample on an axis singularity → analytic line integral, `t0 = max(0,…)` IS
  the inside case. **Sky yaw**: sky branch took the head yaw, room branch `boardYaw` — that
  difference WAS the moon/shaft mismatch and made two players see different moons.
  **MP env clock**: record 31 `[style][u32 ms]`, 7 bytes only while a shell stands, owner =
  lowest player id on the same style, followers walk 0.2 s/s. Style is a comparison key, never
  an instruction. Rat/drip/flicker/sway/shimmer and (integrator addition to both sky shaders)
  star rotation now share one epoch. NOT synced: shooting stars, fireflies, motes, fog.
  Flight max 3. Wire tests 1504 → 1520 (+16 for record 31's vectors).
  NOT DONE / next: the per-card gold pulse on usable item chips in the open fan is still
  unsynced; a deliberate PING (marker at a hex every peer sees) is written up as its own round.
- **ModBuild 137** — his tuned setup is the shipped default. 31 values taken over VERBATIM from
  the cfg drop via `python3 scripts/rebase-defaults.py apply` (standing rule: a dropped cfg is
  always against the NEWEST build — never re-express it, see memory tuned-cfg-drops-are-current).
  Shipped board style is now **Bronze**; the play tray moves to where he holds it (TrayForward
  0.585→0.679, TrayDown 0.094→0.127, TrayRight −0.264→−0.035, TrayPitch 33→42.4, TrayYaw
  −41→−10, TrayScale 0.57→2); BoardScale rebased on all three boards (Oak 0.924→0.554, Steel
  0.299→0.437, Bronze 0.4→0.437); the item-use slot gains per-board offsets on all three; the
  decision dock, pile, rest discs, slot overlays and active card follow; world scale
  SavedScaleMultiplier 2.82→3.37; RoundButtons Depth 0.009→0.015 and OffsetZ 0.025→0.005.
  Only `Defaults/{Cards,Rig,WorldUI}.cs` changed — no code, no wire, same bundle as 136.
  **`rebase-defaults.py check` exits 0 for the first time** — the reference cfg directory was
  missing until now, so that fifth gate had been reporting an environment gap in every round's
  worker reports. The 109 cfg keys with no Defaults line are correctly left alone; ~20 of them
  are the dials removed in 136's audit and are inert orphans by design.
- **ModBuild 136** — STACK TRACES RESTORED (the round's biggest win), settings audit round 2,
  moonlight instead of lasers, Off (black) environment, three reported bugs closed.
  **DIAGNOSTICS:** `GloomhavenShared.LogBuildInfo()` (decompiled GH.Shared:57-61) calls
  `Application.SetStackTraceLogType(..., None)` five times at boot including LogType.Exception —
  so EVERY exception in every log this project ever read was a bare unattributable one-liner
  (1172 in his run). `Core/ExceptionTraces.cs` restores ScriptOnly and re-asserts on scene load;
  Shutdown writes the game's authored value back. Traces land in Player.log only. **From now on
  exceptions are attributable — read Player.log stacks before theorising.** The restart NREs are
  game-side (they fire identically on quit-to-menu, which is no restart); the options-tab burst
  is one NRE per clone of the game's settings-row prefab (AdvancedIndex = 11 link rows → exactly
  1), harmless. Found while proving it: WorldUIModule's ~25-call shutdown chain ran UNGUARDED —
  one throw silently skipped every restore below it (MrBacking alphas, ActorBars, flat screen);
  that chain + Hands/VRHand/VRCard teardowns are now per-step TickGuard-isolated, order verbatim.
  Diagnostic `RESTART TEARDOWN` (released / already-destroyed census 2 frames later).
  **SETTINGS ROUND 2:** 20 dials removed, 16 clamped, method mirrored from round 1 (8594ebd).
  Decision test that settled every borderline: *does the flat game let you switch this display
  off?* → readouts REMOVED (initiative track, element board, objectives, stat panels, prop info,
  enemy reveal, actor bars, tooltips, action hints, hex outline+fill), mod-invented extras KEPT
  (wrist HUD, combat log). Also removed: ClickLatch (off = nothing clickable), ClickMode (only
  'execute' ever worked), ForceMouseMode, 2 map-composite switches (off = documented black
  screen), Keyboard (unfillable text field blocks campaign creation), TutorialVRAdapt (off = the
  known camera-step deadlock), MenuRig (off = no rig at all). Clamps where 0 deleted the thing
  (canvas scale, screen size/distance, card width, inspect scale, fan radius, rest discs,
  per-board furniture scales); BoardScale floored at the READ because the two-hand grab writes
  that key. No wire change (no removed key is in a board section), 1506 assertions unchanged.
  **BUGS:** rest keycaps now ride the game's own offer predicate (phase ==
  SelectAbilityCardsOrLongRest && !IsImprovedLongResting) instead of `can || selected` — the
  boots prompt kept 'Lange Rast' up because the selected flag survives until confirmation, and
  vanilla doesn't switch hands in that phase (hence "egal welchen Character"). Commit cap and
  item-use cap now share ONE derived seat: the Y offset was exactly half the tuned per-board
  spacing because the item cap counted as a third cluster member it is never co-visible with
  (5 mm Steel / 4 mm Oak / 0 Bronze — matching his report); the peer mirror's 52 mm error fixed
  with it. **[Sky] OffBlack** appended (never renumbering): hides the sphere, loads and spawns
  nothing, doesn't set `_active` so the far plane stays the game's, MR untouched. Perf audit:
  the mod never HIDES an environment, it destroys it — so the "disabled renderer still
  simulates" trap cannot apply; cellar's inactive GlowTemplate has no ParticleSystem; prefabs
  have 0 MonoBehaviours / 0 colliders. Diagnostic `SKY IDLE`. **CONTENT:** the five moonbeam
  slats become ONE analytic volume (EnvBeam) with NO faces — density from ray/axis distance,
  super-Gaussian section, path-length term so it brightens looking along it; hull sized where
  density is 1e-6 of peak. Pool re-lights the FLOOR'S OWN ALBEDO through an elliptical mask
  (mortar comes up cold) instead of a glow sprite; additive sill glows were tried and REJECTED
  (edge-on = one-pixel line = the laser failure again). Cobwebs from TextureCan others_0015
  (CC0; ambientCG/PolyHaven/Kenney carry none, the 4K pack forbids redistribution). Forest
  ground `_DirScale` 0.38 + the cold tint the trunks got in 133 (0.61 → 0.22 at centre) with the
  board's landing patch raised; `_VCol` debt paid with per-class floors so the canopy recedes
  instead of vanishing. Fog velocity curves unified (his log's warning spam).
  **TOOLING FINDING:** PreviewEnvironments rendered into an 8-bit LINEAR RT — first step after
  gamma is 18/255, so every dark gradient showed hard bands that do not exist on the headset.
  Now ARGBHalf. **Earlier rounds' previews under-reported darks and over-reported banding.**
  Bundle 64,473,235 bytes — 136 NEEDS it.
  MERGE NOTE: two lanes' stale-based patches reverted already-merged lanes this round; caught
  both times by the marker sweep. Restrict patches to owned paths AND grep every prior lane's
  marker after every apply — see memory git-worktree-merge-hazards.
- **ModBuild 135** — cellar reworked into a place, hex decal leak killed at the root, forest
  polish. FOREST APPROVED by the user this round ("gefällt mir schon sehr gut") — only the three
  requested fixes touched it (axe pose derived so it bites the stump centre, fireflies −40%,
  shooting stars at 40 m / 0.35× angular size). CELLAR: ranges 4.6/4.0/4.6 → 3.1/2.9/3.0 m plus
  a hardened falloff (`_PtHard`, default 0 so the forest is bit-identical) — north wall −4.8× at
  1.55 m, −870× at 3 m, table top unchanged. The flicker DID reach surfaces; it was invisible
  (±10%) and desynchronised — all flame cards shared one material at phase 0 while light slots
  ran 0/2.1/4.4. Now ±31%, `SlotPhase[]`/`SlotRate[]` the single source for flame, halo and
  puddle reflection. TWO more silent-default bugs of the `_RimDir` class: cellar `dirWorld` was
  hand-typed 21° off the visible moon (→ MoonDir), and SHARED MATERIALS across transforms baked
  object-space lighting as if every bar/candle stood where the first one does (welded to one
  mesh each). WINDOW: bars floated because they sat proud AND `WallMesh` quantises the hole to
  its 0.16 m grid → new `SnappedHole()` + a 0.34 m reveal (zero-thickness walls had no "inside"
  before); window moved west so the beam lands 3.95 m out, clear of the 3.25 m play radius; the
  beam is 5 EnvShaft slats, one per bar gap, so bar shadows are geometry. LIFE, script-free on
  one clock: EnvDrip (hang 1.55 s → gravity fall 0.813 s → 5 ballistic splash droplets) with
  EnvPuddle's ring train phase-shifted by exactly hang+fall; EnvCritter rat on a cubic Bézier
  routed THROUGH the moonbeam and candle pool (build assert: ≥3.25 m from centre, ≤0.12 m from
  the beam axis; it alone gets the shaft as a real light); cobwebs with `_Sway` (mip coverage
  preserved or thin threads vanish); blinking eyes on ONE shared material; unphased `_Gust` so
  all flames lean together with the dust. HEX DECAL round 2: intermittency REPRODUCED — a target
  without a stencil attachment degenerates `Comp Equal` to always-pass (FlatScreenStereo.2
  documents the mod hitting exactly that; XR eye textures aren't ours). Stencil COLLISION
  eliminated (poison draw setting all 255 bits changes nothing — the prepass rewrites over its
  own coverage). Second intermittency found: 133's band was along the view ray, vertical reach
  tol·sin(elev) — same tile painted at 57°, vanished at 8°. Now an object-space SLAB (y=0 to
  y=−tol, both ray∩plane of the same ray), roles swapped so the far bound is a literal ZTest
  GEqual needing NO stencil; Ref 0 keeps zero-reading targets passing; guards clamp non-positive
  /NaN tolerance. Verified across 16 harness scenarios incl. the no-stencil and poison cases.
  Bundle 64,152,301 bytes — 135 NEEDS it. Style labels now plain "Keller"/"Nachtwald".
  KNOWN DEBT: `EnvRoomCutout` receives `_VCol` but never applies it, so the forest canopy's
  baked depth fade does nothing — deliberately NOT fixed (it would visibly darken the approved
  room); decide next round. If the highlight ever draws THROUGH figures, that confirms the eye
  target lacks stencil and needs a C# fix.
- **ModBuild 134** — board floats small in a large place, sky fully procedural, night dark.
  PROPORTIONS: 133 normalized on the prefab's TOTAL renderer extent (forest 60.2 m incl. tree
  bands out to 28.5 m) → the CLEARING came out smaller than the 30.95 wu board and the tree ring
  stood inside the level (his log 537; the cellar only looked sane because total ≈ interior).
  Fix: prefabs carry a `PlaySpace` marker child (localScale.x = authored usable diameter; swamp
  9.0 m, cellar 6.5 m), taken and DESTROYED before any other pass sees it; runtime scales that
  to `PlaySpaceToBoardRatio` 4.5× the board world extent and drops the floor
  `FloatGapToBoardRatio` 0.75× below the board underside so the board HOVERS. Both ratios are
  board-proportional — an authored-metre value would break under zoom. Worked on his numbers:
  clearing 139 wu (perceived 9.4 m vs 2.1 m board), tree band at 14.9 m perceived, his real
  floor 2 cm under the new forest floor. No marker = old total basis + warn + FALLBACK note in
  the placement line. Far plane budgets the TOTAL art, not the play space. Sanity window and
  zero-re-seat both intact. SKY: astrophoto + its bake pipeline DELETED. Gradient (inverted —
  darkest at the horizon, which also killed the lifted ridge band) + 8404 Yale stars (cut to
  the catalogue's own V≤6.5 limit) + Milky Way in REAL galactic coordinates (IAU 1958; basis
  from NGP + centre, orthogonality 1.4e-6 and l(NCP) 122.93190 vs 122.93192 published — both
  HARD build failures) + sub-visual star dust (denser in the band — the MW *is* unresolved
  stars); one celestial frame, dome direction from the OBJECT-SPACE vertex (only space where
  dome and star geometry cannot slide); moon 3.27°→1.39°, occludes stars per-star in the vertex
  shader (no depth-buffer assumptions). DARKNESS: ambient ~3× down, depth fade 6.5–22 m →
  3–10.5 m, far fog α 0.055→0.012, lantern range halved, WHILE dirCol and _RimCol go UP —
  contrast not dimming. Bug found en route: `_RimDir` was never set, so the moonlit-side gate
  read EnvRoom's default and rimmed trunks all the way round. New build gate
  AssertPlaySpaceClear walks transformed vertices and FAILS on intruders (caught 6 props + the
  fog donut). Bundle 64,097,948 bytes (−12.3 MB) — 134 NEEDS it. UNVERIFIED: ratio 4.5 and gap
  0.75 by eye; `_MwGain` 0.040 (band soft by design); cellar window now a dark hole.
- **ModBuild 133** — BOARD-ANCHORED room (invariant at last), hex decal banded, creepy forest,
  real turning star sky. ANCHOR: his ruling "Verhältnis zum Raum drumrum MUSS fix ... nur drehen
  und kleiner/größer beim Zoomen" is incompatible with perceived-constant rooms. Room branch
  (RoomGeo + GroundFog/GroundFogFar/Fireflies) is derived ONCE from the board — 3.0× its world
  extent from the live hex tiles via ObjectCacheService (+half a hex per side, SpawnRing's own
  math), floor at the board underside (hex renderers AND SceneRegistry room-chunk volumes,
  since floor ART hangs off map tiles), yaw from the board hierarchy — then WORLD-FIXED forever
  (zero writes, zero re-seats: 131's teleport is structurally impossible). Invariance proof:
  all locomotion writes the rig (WorldGrab 362/401/403, Flight 213, SnapTurn 162), nothing
  moves the diorama. 130's miniature root cause found: it encapsulated DISABLED BoxColliders
  (zero bounds at origin) → 2.1 wu; now a perceived-extent window [0.2, 20] m refuses to place
  rather than ship nonsense. Sky branch unchanged (perceived-constant, RigPoseVersion re-seat).
  HEX DECAL: NOT a Projector (HexSelect_Control.HexProjector is a MeshRenderer decal box) —
  HexDecalStable's depth scheme was one-sided (LEqual rejects nearer, accepts ALL farther; the
  black sky hid it until the room floor arrived). New ColorMask-0 pre-pass exports P pushed
  back by _VRSurfaceTolerance (0.25 m) with ZTest GEqual + stencil bit 128; colour pass draws
  Comp Equal. Verified by standalone renders (leak cut at the board edge, 0 changed px for
  shared borders and figure occlusion). _CameraDepthTexture rejected: off by default
  (HeadDepthPrepass=false) and a full extra scene submission per eye under MultiPass.
  _VRStencilBit=0 = rebuild-free kill switch; all degenerate paths fail to the OLD behavior.
  Also: game's real projectors (DeathDissolve, RFX4) now ignore the mod layer, additively.
  FOREST (moor rejected): 106 procedural trunks in 4 depth bands to 28.5 m w/ photoscanned
  needle atlases, canopy torn toward the moon, 3 moonlight shafts, forest floor + trodden path,
  2 HorizontalBillboard mist layers, will-o'-wisps, far lantern, eyes at 12.5 m, story props.
  SKY: 5080 Yale Bright Star Catalogue stars (public domain; HYG rejected CC BY-SA, Shadertoy
  NC/SA) as per-star quads, B−V→temperature colours, celestial-pole rotation 1 turn/2880 s,
  extinction, rise/set, Rozenberg scintillation; photo dome suppresses its own star cores +
  haze veil + dither + wrapped clock. FLOATERS: Renderer.bounds is a transformed-AABB (399 mm
  phantom drop on the tilted tree), photoscan pivots arbitrary (40 cm) → transformed-vertex
  heights, pivot re-centring, ray-cast stacking, overhang = build error, baked contact pools.
  Bundle 76,384,749 bytes — 133 NEEDS it. UNVERIFIED: reversed-Z branch of the decal band,
  stencil bit 128 free on this rig, room-to-board ratio 3.0 by eye, preview gamma vs headset
  (rooms went darker this round).
- **ModBuild 132** — game-asset environments DELETED (user ruling 2026-08-13); custom photoscan
  rooms; teleport killed. THE TELEPORT ("darf unter keinen Umständen passien"): 131's
  scale-settle re-seat fired 5× in his log — re-seating a room the player stands IN reads as a
  player teleport + board displacement. MapGen deleted whole (3,450 lines); SkyAlternative back
  to the 128 FX-shell model (scenario gate, MR precedence, one world-anchored perceived-constant
  frame, recenter-chord-only re-seat). STANDING RULING: never re-seat an occupied room. Full
  saga + all Apparance/Addressables knowledge preserved in .planning/game-env-postmortem.md +
  memory game-env-rooms-abandoned. CUSTOM ROOMS (Poly Haven CC0 photoscans; ready-made CC rooms
  researched and rejected as stylized/low-poly/paid/login-gated): Env_Cellar = 10.5×9 m stone
  room, candle groups w/ animated flame cards, barred star window, stair alcove, barrels/table/
  shelf, 105k tris; Env_Swamp = 30 m moonlit clearing, mud/leaf heightfield, 3 ponds w/ moon
  glint, snags/logs/mossy rocks/standing stones/reeds, treeline berm, far fog ring
  (HorizontalBillboard), 150k tris. Five new baked-lighting shaders (EnvRoom/Cutout/Ground/
  Water/Flame) — ZERO real Lights, zero scripts in prefabs; closed opaque floors. Assets under
  Environments/Imported/ (~50 MB srcs, polyhaven_pipeline.py reproduces). Bundle 65,8xx,xxx
  bytes — 132 NEEDS it. KNOWN OPEN: perceived-constant model means extreme zoom-out can put the
  board below the cellar floor line (the old finding-3 tension, documented in the postmortem) —
  if reported again, solve in CONTENT (softer floor edge), never with re-seats. Cellar leans
  warm-amber; light constants in BuildEnvironmentRooms.cs rig blocks for one-line tuning.
- **ModBuild 131** — LIFE-SIZE single-room environment, alive, floor-tight. Same bundle as 130.
  130 verdict: 'Map ABHM' LOADED (dual route works) but as a 7 cm miniature next to the board —
  the 130 board-relative sizing was the bug (board extent is world-tiny at diorama zoom; log:
  5.8 wu at rig scale 85). DELETED; sizing is perceived again: room world scale = live rig
  scale, main room 11 REAL meters, player's floor point at room center, floor at real floor,
  world-frozen between seats (finding-3 ruling holds mid-gesture), RE-SEAT when a zoom settles
  beyond 1.4x (0.7 s settle timer; recenter-chord event class; board-appears probe re-centers
  SKY only). (a) ONE ROOM: preference lists = DLC_SC*_RM* single-room scenario maps (Cellar
  SC02/04/06_RM01, Swamp SC03_RM01/SC16/SC20_RM01; all in his MAP CATALOG census; Map A
  terminal), multi-tile arrivals CULLED to center tile; main-room measure fixed (renderer
  bounds ≤ map bounds — collider claimed 60.2 vs map 35.0). (b) ATMOSPHERE: particles/
  Animators/flames PRESERVED; LightFlicker kept + rebased per seat (its Start caches the
  STAGING pose — flames would teleport); DynamicAmbience SetLightLevel(1) then parked;
  StaticAmbience destroyed (writes RenderSettings globals — from source). (c) FLOORS never
  deleted (130 heal REMOVED StoneRooms.Floor.Tile = the see-through floor): 4-rank variant-
  stripped donor match across ALL loaded packets, clone-patch holes, re-enable reveal-disabled
  hex floors, 10x10 FLOOR GATE; RESOURCE TOPOLOGY census on first miss. UNVERIFIED: DLC room
  themes (inferred from scenario numbering), floor-band heuristic vs short props, room-board
  intersection when recentering far zoomed out.
- **ModBuild 130** — composites LOAD (dual route), board FIXED in the room, cellar night sky.
  129 verdicts from his log: staging invisible CONFIRMED, red cubes 0 CONFIRMED (heal found
  donors), but 'Map ABHM'/'Map DDM' "failed to load" → Map A rectangle again. REVISED root
  cause: the maps EXIST on his install (Player.log boot dump line 1490: 113 'Map *' prefabs
  in always_loaded_standalone) — the full-path Addressables key resolves only for
  single-letter maps on his catalog. Fix: per-candidate DUAL ROUTE — (1) the game's
  always-loaded asset store (AssetBundleManager._alwaysloadedHandles, session-lifetime,
  synchronous), (2) Addressables path key; preference lists Cellar ABHM→GI→A / Swamp
  DDM→LML→A (all verified in his dump); 'MAP CATALOG' census line on first activation.
  BOARD FIXED (his ruling overrules perceived-constant): frame split — ROOM branch (map +
  GroundFog + Fireflies by node name) world-FROZEN after placement, zero per-frame writes,
  zoom changes only perceived size; sized 2.75× board's larger extent, board center =
  main-room center, room floor = board underside (can never sink); SKY branch (StarDome,
  shooting stars, dust motes) keeps NotifyRigScaled pivot algebra; far plane = max of both
  budgets. Board drag needs no follow code (all locomotion writes the rig — WorldGrab 304/
  401-403, Flight, SnapTurn). CELLAR SKY: Env_Cellar gains the swamp's StarDome node (shared
  assets by GUID, 'StarDome' = node-name contract with src). Bundle 41,865,652 bytes — 130
  NEEDS it. UNVERIFIED: ABHM/DDM room shapes/themes (letter-count inference), always-loaded
  route on future game updates (census covers), room-floor-at-table-height look (new ruled
  behavior, his test judges).
- **ModBuild 129** — REAL authored maps, invisible staging, zero red cubes, astrophoto sky.
  128's rendering fix CONFIRMED on hardware (census: 680 drawing, T+3s survival) — remaining
  findings: staging visible (fixed −50 wu = 36 cm real at rig scale 137 — user watched the
  build as a miniature), Map A too generic ("Rechtecking ... kein interesannter Ort"), 9–19
  residual Red Cubes. REAL MAPS: authored multi-room composites Cellar='Map ABHM' (4 rooms) /
  SwampNight='Map DDM' (3 rooms), AUTHORED style axes kept (fill only Inherit/Default axes;
  swamp forces Tone=ForestMoonlight, logs overrides), main room → 10 m / map ≤ 24 m, placed by
  MAIN room center (bounds center can fall inside a wall); fallback composite→Map A→FX shell.
  STAGING: ≥ 50 REAL meters down AND beyond head-cam far plane + hidden layer runtime-verified
  vs Camera.allCameras every poll (head mask 0xFFFFFFFF is re-asserted per frame by its owner —
  carving rejected). RED CUBES: decompile — a loaded category list lacking a piece MINTS a
  session-poisoning null placeholder (terminal; RefreshResourceList purges Objects, not lists).
  Fix: warmup from the map's own effective styles + per-list placeholder purge + HEAL at settle
  (donor by piece-suffix via transform-derived procedure frame, alias injected into the game's
  own list) + no-donor removal (gap beats red box); census prints NAMES, 0 by construction.
  SKY (round 3, user: real + hi-res from the internet): 16k Rogland Clear Night (Poly Haven
  CC0) → 8192x2560 BC7 sky-band dome (float pipeline, TPDF dither vs banding, sRGB dark-end
  precision, npotScale assert), painted moon kept, twinkle reduced; bundle 41,863,274 bytes —
  129 NEEDS it. UNVERIFIED: composite catalog presence (log names what loaded), donor
  availability for Marsh floors/doors (worst case gaps), composite renderer count perf.
- **ModBuild 128** — the generated room RENDERS, the ally banner's real occluder, the painted sky.
  ROOM (was: gray floor, black/no walls): FOUR causes — (1) the 127 freeze (disable
  ApparanceEntity) let ApparanceEngine DESTROY all generated content one tick after placement
  (EntitiesGameTick ignores 'enabled'; CheckEntity→DestroyEntity wipes Generated-Content — the
  user saw the template skeleton); fix = empty m_GenerationTiers/m_GenerationRoot/m_Instances
  FIRST, then disable. (2) Apparance resource packets load on demand and a miss caches a
  session-poisoning 'Red Cube' fallback engine-globally by name; fix = WARMUP phase (pre-load
  style packets, purge via RefreshResourceList(clear_unused:true), settle refuses while
  fallbacks remain). (3) DynamicAmbience clones every light at intensity 0 — only the real
  scenario's UpdateAmbience blends tiles in; fix = SetLightLevel(1f), room lights masked to MOD
  LAYER ONLY (never restyles the board), ranges rescaled with room scale (Unity light range
  ignores transform scale), 2-point fallback rig. (4) WallSegmentFade adopted the room's doors
  (log 4253); fix = mod layer from birth + tile machinery components DESTROYED at finalize (no
  sweep can adopt). Diagnostics: grep 'ROOM CENSUS' (placement, lights, T+3s survival proof).
  BOARD CENTERED (finding 4): frame origin = ProceduralScenario tile-bounds center (ALL tiles
  incl. hidden — reveals never re-center), player-point fallback + 60-frame re-center probe.
  BANNER ROUND 4 ("unverändert"): all three prior mechanisms provably ran in the 127 log,
  pixel-identical cut ⇒ occluder is UI CLIPPING (the initiative ScrollRect viewport's
  game-owned prefab-level clipper; the ally banner is the only part crossing its top edge —
  enemy popups have no banner, hence immune). UnmaskedUiGraphics: maskable=false +
  RecalculateClipping() per shown-popup graphic (BOTH load-bearing in shipped uGUI 1.0.0),
  internal-clipper guard, restore at both doors; rounds 1–3 STAY. Proof: 'BANNER-CLIP DIAG'.
  PAINTED SKY (style round 2): dome repainted (milky-way band, 4200 PSF stars w/ halos,
  repainted moon + layered halo, comet streaks, bokeh fireflies, wispier fog — HorizontalBillboard
  held), 64x32 dome, slow vault drift; star tex BC7 2.1 MB VRAM (was 8.4), bundle 30,152,582
  bytes — 128 NEEDS it. UNVERIFIED ON HARDWARE: warmup token list is inferred (census names
  any residual fallback); room light budget vs pixelLightCount; board-center pop if the board
  appears after fallback placement.
- **ModBuild 127** — environments are SCENARIO-ONLY and built from the game's own art.
  SCOPE (user ruling: "Ich WILL garnicht das die Umgebung im Menu rendert - sondern nur im
  Szenario"): SkyAlternative gates on VRModeStateMachine.ScenarioBoardExists (Choreographer-
  alive, NOT the save-state phase which flips during loading); menu/world map untouched, leaving
  a scenario stands down next tick + cancels in-flight generation. GAME-BUILT ROOMS
  (SkyAlternative.MapGen.cs, new): Addressables 'Map A' (session-cached handle) staged 50 wu
  below the viewpoint in the ProcGen scene, ambience muted FIRST, styles written (Cellar =
  Dungeon/StoneRooms/Candlelight; SwampNight = Forest/Marsh/StillWaters/ForestMoonlight), walls
  populated, detail focus BORROWED onto the staging map for the ≤30 s build window (staging is
  outside detail range — round-3 evidence, not conditional), settle poll = renderer census
  stable + no busy entities (30 s timeout → FX shell only, one-shot warn), freeze = disable
  Apparance COMPONENTS never GameObjects (inactive entities destroy their native side), strip
  colliders, mod layer, normalize to ~9 real meters (n = 9/max(bounds.xz) at staging scale 1,
  clamp 0.02–10), floor = avg ProceduralMapTile height, center → frame origin (player inside).
  GameEnvProbe RETIRED (923 lines — mission complete; logic lives in MapGen). FX SHELLS rebuilt
  from own shaders, third-party low-poly art DELETED (folder 4.2 MB → 980 KB; bundle 29,639,808
  bytes — 127 NEEDS it): Swamp = star dome (baked moon+twinkle) + shooting stars (Stretch,
  cameraVelocityScale=0) + fireflies + ground fog as HORIZONTAL billboards (user fog ruling:
  never re-orient with head movement — PERMANENT CONSTRAINT in BuildEnvironments.cs); Cellar =
  dust motes + disabled GlowTemplate. UNVERIFIED ON HARDWARE: whether disabled components fully
  stop native re-tiering after placement; the 9 m normalization target may want tuning. First
  scenario start with a style selected is the real test — the engine dresses live, log records.
- **ModBuild 126** — environments become WORLD PLACES, the game-asset probe, ally banner round 3.
  FREE MOVEMENT: all locomotion writes RigRoot (Flight 213, SnapTurn 162, WorldGrab 304/362/401/
  403); the env now spawns world-anchored at the player's floor point + gaze yaw, re-seats on
  RigPoseVersion bumps, and mirrors rig-scale writes around the SAME pivot (NotifyRigScaled from
  WorldGrab/Comfort; algebraic no-drift proof in SkyAlternative) — fly/turn/drag/walk move
  THROUGH the room, zoom never changes its perceived size. STYLE PLAN: user wants Gloomhaven's
  own style, ideally game assets — investigation (.planning/game-env-assets.md) picked APPARANCE
  MICRO-GENERATION (Map A prefab + style enums Dungeon/StoneRooms/Candlelight resp. Forest/
  Marsh/StillWaters/ForestMoonlight); GameEnvProbe ships in 126: passive catalog/engine dumps
  every session + one-shot [Sky] EnvProbe menu experiment (detail-focus pointed at the probe map
  — distance-scaled synthesis would fake 'never built'; idempotent cleanup) → ENV PROBE VERDICT
  decides B vs scenario-time capture (C). Low-poly envs stay as placeholders. ALLY BANNER R3:
  round-2 lift WORKED (log 529); remaining occluder = WORLD DEPTH (the board's raised wooden
  rail in front of the canvas plane where the banner reaches up). Fix: OnTopUiGraphics — every
  Graphic in a SHOWN popup swaps to a session-cached ZTest-Always clone of its own material
  (ActorBars mechanism inverted, TMP via fontSharedMaterial, CardEffects subtrees excluded,
  reference-checked restore). NEXT after his log: read ENV PROBE VERDICT → build the styled
  environments via the winning approach.
- **ModBuild 125** — 3D ENVIRONMENTS (panoramas rejected + removed), mouse round 2 + INCIDENT
  RESTORE, ally banner. [Sky] Style = Default/Cellar/SwampNight ("Umgebung"): two bundled
  prefabs from CC0 Quaternius packs (Env_Cellar 53k tris — stone room, torches, chests, stairs
  into darkness; Env_Swamp 29k tris — 2200-star dome w/ baked moon + twinkle shader, moon-glint
  water, fog banks, fireflies, glowing mushrooms, shooting stars), ALL world-anchored (user
  rule), no bundle scripts, spawned under RigRoot at identity (stands still in real space,
  perceived-size-constant under zoom, zero per-frame writes), MR on = always off. Preview
  renders reviewed by builder AND integrator before shipping (6 iterations; renders in
  scratchpad env-previews/). Bundle rebuilt 30,060,345 bytes (panoramas deleted) — 125 NEEDS it.
  MOUSE: OS cursor hidden per frame (WarpCursorPosition moved the real cursor); virtual pointer
  parks at (-4096,-4096) unless the laser actively drives it. INCIDENT: the 8594ebd merge had
  silently reverted 123's MouseWorldSurfaceCut + grip fall-through (stale-base diff) — restored;
  hazard recorded in memory (restrict merge patches to owned paths; grep prior lanes' markers).
  ALLY BANNER: 'VERBÜNDETER' clip closed (branch lift + ancestor-chain flatten, both occluders).
- **ModBuild 124** — SKY ALTERNATIVES. [Sky] Style (Default/Night/Sunset/Cellar; DE Standard/
  Sternenhimmel/Abendrot/Gewölbekeller), curated in Grafik ▸ Darstellung, localized dropdown,
  live-switchable. Three CC0 Poly Haven panoramas (4096x2048) + GloomhavenVR/SkyPanoramic
  (view-direction equirect, non-occluding by construction) in the REBUILT 39 MB bundle
  (unity-2021.3.5; prebuilt/ updated — 124 NEEDS this bundle, old one = one-shot warn + game
  sky untouched). Runtime: SkyAlternative hides GH_SkySphere (renderer.enabled=false, the safe
  half of SkyBackdrop's trap), shows a mod-layer head-following inverted sphere; lazy bundle
  load, session-kept textures. MR precedence hard-wired: MR on stands the alternative down
  before the chroma sweep; dial re-applies on MR off. Local-only, nothing on the wire.
- **ModBuild 123** — the seven-item round after the border victory. MOUSE DEAD: game EventSystem
  pointer swept world UI with the head; MouseWorldSurfaceCut strips game-pointer hits on
  world-space root canvases at the shared source (EventSystem.RaycastAll postfix; mod pointer
  ids pass). GRIP FALL-THROUGH: trigger-only highlight no longer eats the grip — nearest
  GrabWithGrip target re-elected (tray bar beside a highlighted card). MENU: sliders show live
  values (donor caption + deferred-Destroy rebind bug), dependency layer folds children under
  parents (VROptionsTab.8), SyncPeerFades → Avatar & Mehrspieler. ELEVEN always-on dials removed
  (danger audit: PileViewer, ActivePile, Master, FlatScreen+AutoShow, UseBars, DoomPicker,
  DistributePanel, CatchAllModals, MenuPopupFloat, ManualScreenChord); three borderline
  questions queued for the user (ForceMouseMode, ClickLatch, the two map-fix diagnostics).
  HELD FIGURE: auto-release is PER-FIGURE (own bar/named wait/own turn/own animator leaves
  idle — Choreographer.IdleStates vocabulary, same predicate as the grab gate), global flows no
  longer end a hold, and every forced release takes the normal 0.28 s glide (instant-when-busy
  branch removed; authoritative-move release stays instant). Deadlock-safety argument in
  FigureBusy docs; watchdog stays.
- **ModBuild 122** — the border saga CLOSED (user on 121: "Großer Erfolg!"). Bottom fix: the
  contour derived from the v5 footprint (punched pixels ∩ frame-era CardOutline bands) — body
  stopped higher than the stock art draws. Capture now stamps the STOCK art's own alpha only
  (shadow trim / dark-border peel / OUTLINE CLIP deleted); CacheVersion 5→6 relearn; body and
  face coincide by construction. THE GREAT CLEANUP: net −7,200 LOC — deleted CardShapeMask (incl.
  Net call sites), CardDissolveFloor+dial, CardShaderProbe, CardBandPainter, CardBandPixelCapture,
  CardFaceCrop, CardOutline, punch/crop factories in CardFaceMipBake, FramePunch sweep + band
  diagnostics, cutout material bake, inert [Cards] GrabButton (+ dead ProximityGrabber branch +
  menu rows). KEPT: CardContour+AttachBody, stock-alpha capture v6, mip bake, emission floor,
  umber EdgeColor, SlotSeatLiner+mirror, FaceBlackout (stock-path). Tab captions wrap (explicit
  '&' breaks DE/EN; Erweitert chooser same). Saga history: build notes 105-121 + git. MP test of
  the shaped bodies still outstanding.
- **ModBuild 121** — border attempt 17: THE USER'S OWN DESIGN. His ruling: face must render
  completely correct again; the MESH must be punched out to the surface outline. (His "120" log
  was really a 119 run — the 120 deciders never executed; geometry moots the shader question.)
  ORDER A: punched/cropped sprite serving retired at the mint (PunchServingEnabled=false,
  CardFaceCrop.Enabled=false, restores via existing contracts) — stock art everywhere incl. the
  designed printed frame; the broken bottom dies with its cause; FramePunch sweep = outline
  derivation only; capture's OUTLINE CLIP now load-bearing. ORDER B: CardContour (marching
  squares 0.5 iso → closed-loop DP ≤120 verts → ear-clip front / mirrored back / extruded rim,
  bounds pinned to full box) + CardMesh.AttachBody serving shaped meshes to every registered
  body on contour-learn (rounded until then; warm cache = shaped from first draw); cutout
  baking retired (CutoutMaterialsEnabled=false); metrics consumers verified box-only. ALL SIX
  mirrors adopted AttachBody same build (RemoteHandFan/ItemFan/BrowserFan/CardFx/Avatar +
  AvatarMirror; two submeshes → back material twice; mirrors never destroy the shared cached
  meshes). If a band still shows on a seated tray card it can only be the deliberate LINER wood.
- **ModBuild 120** — border attempt 16: THE DIFFERENTIAL (instrumentation only, deliberately).
  119's partial success proved the painter chain (band = slab umber+light, opaque). The paradox:
  cutout texture alpha provably transparent in the bands + material state provably correct, yet
  the band renders. Code audit exonerates all local writers (emission floor touches only
  emission; _Cutoff 0.5; ConfigureCutout authoritative last on every path) → chief suspect: the
  game build's Standard shader may lack the _ALPHATEST_ON variant (EnableKeyword is a request;
  stripped variant never clips → slab draws its full envelope in EdgeColor = the measured band).
  Shipped deciders: CARD BAND DIFF (re-render with exactly one candidate suppressed per pass —
  Backing / edge submesh / liner / face canvas — per-strip verdict names the painter), CARD BAND
  TEX-VS-RENDER (live GPU texture alpha via Blit readback beside at-capture material state),
  CUTOUT CLIP PROBE (live edge material, UVs pinned to a verified alpha-0 texel → "clip
  EXECUTES" / "DOES NOT EXECUTE (variant stripped)"). If DOES NOT EXECUTE: fix lane = bundled
  clip-capable shader (BoardLit + clip(), bundle rebuild with /home/claw/unity-2021.3.5) or true
  mesh trimming to the outline. NO other changes in 120.
- **ModBuild 119** — border attempt 15: THE LIGHTING CONVICTION. Fourteen albedo/texture rounds
  were invisible because the card slab renders through stock 'Standard' in DARK scenes — albedo
  × ~0 light = black regardless of what we painted (karten4 band (4,4,3) ≈ 16 % of even the old
  EdgeColor's lit value). BoardLit (the board's own ambient-floored shader) provably cannot clip
  (bundle source: alpha hard-coded 1.0, no clip()) → fix is an EMISSION FLOOR
  (CardMesh.ApplyEmissionFloor: _EmissionMap = own albedo, tint × 1.0) on the shared card-body
  pairs + baked cutout textures; Net mirrors borrow the same instances → peers fixed free. Same
  floor on every Standard near-card surface (fallback board BoardLit-first, ItemsPile slab,
  PileViewer slabs, fallback caps). The 118 capture was BLIND (mask copied from game
  ScenarioCamera which excludes mod layer 27) → now HeadCamera mask ∪ card-subtree layers,
  centre-canary retry ~0, honest INSTRUMENT FAILURE verdict, and a mid-height RLE SCAN line
  (width + face-x + RGB per run) in every CARD BAND PIXELS log. Known caveat: a stripped
  _EMISSION shader variant would silently no-op — the scan line is the arbiter next run.
- **ModBuild 118** — border attempt 14 (two convictions + the pixel instrument), mirror ghost
  hands, trigger-only cards. THE BORDER: 117's liner was RIGHT in kind, WRONG in units — sized
  off the BASE card box while the seated card renders at the live SlotOverlayScale dial (~1.7 on
  his rig): ~1.10× coverage vs a ~1.30–1.40× well → "unverändert". Now seated-card ×
  SlotLinerSeatRatio (1.45) from the same dial, z from the tuning chain, on BOTH wire sides
  (RemoteBoardFurniture takes field 171 into the product; SlotLinerScale const deleted). Second
  conviction: the thin NEUTRAL near-black fan ring is CardMesh.EdgeColor (0.10,0.09,0.08) baked
  into every silhouette-surviving Edge texel → retinted warm umber. CardBandPixelCapture (new)
  renders the real card in situ at latch/re-arm and classifies band-strip means against known
  signatures — the remaining maroon PRINT band (91/264 punched-header + 27–28/36 unpunched
  action-default probes in the 117 log) gets convicted per placement in the next log. MIRROR:
  AvatarMirror read legacy single-side HandGhosts.LocalSide → now LocalLeft/Right per side
  (peers were already correct). TRIGGER-ONLY: grip proximity fallback removed for cards+figures
  (named throttled refusal); [Cards] GrabButton now INERT — retirement candidate next purge.
- **ModBuild 117** — the border SOLVED (attempt 13), the dead-dial purge, the menu restructure.
  THE BORDER: 116's CARD BAND PAINTER acquitted every card layer on the user's own rig while his
  screenshot showed the band — so the reach was wrong. Convicted: **the bundled tray's authored
  AO-dark RECESS FLOOR**, an ancestor mesh ~1.2x the card no card-root sweep could see; pixel
  proof warm-vs-cool (band r−b +7..+21 = tray wood; card layers −11..−18). Punch/crop/silhouette
  worked for rounds — every erased pixel just rendered recess-black, incl. the bottom "kaputt"
  (kept bright ornaments floating on black). The 116 log's 132 % slab spans were a world-AABB
  measurement artifact (tilted cards); the 111 "0.94 coincide" finding stands. Fix =
  **SlotSeatLiner** (opaque rounded CardMesh slab 1.78x card box, keycap grain wood, per recess;
  mirrored 1:1 on peer boards). Diagnostic upgraded: local-tight rects, subtree sweep with
  backdrop verdicts, throttled re-arm — a hand-fan residue would be NAMED in the next log.
  DEAD-DIAL PURGE: 48 keys removed (per-key re-verified; kept ScrollWithStickOnly — live reader
  UguiPointer.cs:505, the audit was wrong there — Experimental3DMap, WorldTilt family);
  ScreenLeftMirrorFallback description fixed (it gates the black-map probe). MENU RESTRUCTURE
  (all 20 integrator recommendations ruled in by the user): everyday tabs Komfort / Grafik /
  Brett & Karten / Tafeln / Avatar & Mehrspieler / Erweitert (ex-Debug); ~30 promotions;
  hand-built trees for the two oversized topics (VROptionsTab.7.TopicTrees.cs); GroupWordLabel
  localization; [Cards] CardSoundsEnabled master switch. Audit basis in `.planning/menu-audit/`.
  Wire tests now 1509.
- **ModBuild 116** — three lanes. BORDER ATTEMPT TWELVE: the 115 probe EXONERATED the card shader
  (verdict (a): alpha honored at rest) — eleven rounds edited layers that could not be painting the
  band. Convicted in source instead: **`FullAbilityCard.unfocusedMask`** — ShowCard loads the SAME
  background sprite into headerImage AND this full-card dimming copy at its own slightly different
  rect (the 115 two-placement warning), toggled by SetUnfocused, ACTIVE IN MULTIPLAYER for every
  presented character not under local control — and served the header-rect punch copy it erased
  ~2 % into the card bottom (the 115 "unterer Teil kaputt" regression). Fix: the mask is a sixth
  named plate (punched + cropped against ITS OWN rect); per-placement punch copies keyed
  (source, mapping); dissolve floor default 0 (probe: cures nothing; _Dissolve_VerticalGradient
  discards bottom-first — the other bottom suspect); NEW CardBandPainter latches a complete painter
  inventory (Graphics + Renderers + rest-output GPU probe per custom shader, incl. the never-probed
  120 %-of-face CardEffects fgFx 'UIFX_Overlay'). If the band survives 116: **read CARD BAND
  PAINTER — it names the painter.** STRETCH BOUNDS + INFO TOGGLE (items 2+3): total-based
  Min/Max with grab-time clamp, [FigureGrab] StretchLimits off-switch, [FigureGrab] HeldFigureInfo
  gating StatPanelSurface.ShowHeldFigure at the registration seam; all in VR settings, DE/EN.
  ALSO RUNNING: the VR options menu overhaul audit — five parallel read-only auditors writing
  `.planning/menu-audit/0*.md` (dead dials, NORMAL vs POWER audience, structure proposal); the
  synthesis + user decision list is the next deliverable after this round's report.
- **ModBuild 115** — two lanes. BORDER ATTEMPT ELEVEN, the first SHADER-side one: 114's log closed
  the texture-side case — the rect crop provably applied (294x450→291x436) and the user still saw
  an identical band, while CARD SHADER IDENTITY named the painter ('GUI/AbilityCard_Shd', a per-
  image material clone, no readable blend state). The user's own report is the tell: **the band
  turns transparent during the game's dissolve animation**, i.e. a clip of the shape
  clip(f(tex.a) − _Dissolve·..) that discards nothing at rest 0 and paints alpha-0 pixels opaque
  black — one mechanism explaining all nine texture rounds at once. Shipped: `CardShaderProbe`
  (once per session, renders the punch's exact alpha-0 pixels through a CLONE of the live material
  at _Dissolve 0 and 0.004 into an RT, reads pixels back, logs a CARD SHADER PROBE verdict:
  (a) alpha honored → another painter exists, (b) epsilon discards → floor proven by that log,
  (c) neither → shader replacement is the only road, (d) unproven; plus the full shader property
  table) and `CardDissolveFloor` (the fix: [Cards] DissolveFloorFraction = 0.004 held at rest,
  never lowering an animated value, on every card-FX material by SIGNATURE match _Dissolve +
  _PosAndBounds — ability faces via CardArtWatch, item cards via ItemsPile.ItemChip, remote fronts
  free because RemoteCardArt already rides CardArtWatch; full restore on yield/recycle). STRETCH
  FIXES from the 114 test: capture was a fixed 80 mm sphere around the mini's CENTRE, so a
  stretched mini sat outside its own zone and could not be shrunk — capture is now min
  closest-point over the held visual's renderer AABBs (cached per hold, 0.5 m sanity clamp,
  centre fallback), scaling with the figure by construction; d0/d stay centre-based on purpose.
  Plus the requested zone-entry haptic (HapticPreset.HoverTick, edge-triggered). If the border
  survives 115: **read CARD SHADER PROBE first** — verdict (a) means hunt the other painter,
  (c) means build a replacement shader; either way the guessing era is over. Wire tests 1529.
- **ModBuild 114** — two lanes. BORDER ATTEMPT TEN: heuristics retired after losing twice on the
  same two sprites — the five face layers resolve BY IDENTITY from CardEffects' serialized fields;
  a CARD SHADER IDENTITY line settles whether the custom card shader ignores alpha (the only
  theory left that explains nine invisible texture rounds at once); and the shader-agnostic rect
  crop removes the band's geometry entirely on local ability faces. HELD-FIGURE STRETCH: second
  hand + trigger near a held mini scales it ratio-based (s0 x d/d0), this-hold-only, wire record
  30 (first new record since the reservation note; additive, neutral-omitted, fail-closed decode),
  +40 wire vectors -> 1526. If the border survives 114: read CARD SHADER IDENTITY first — its
  verdict clause dictates whether the next move is shader-side or a sixth-layer hunt.
- **ModBuild 113** — the border, attempt NINE: the punch region is GEOMETRY now, not luma. 112's
  BAND INVENTORY named the culprits (punched background still 102/264 band probes — the luma-BFS is
  interrupted by decoration; the two ACTION-HALF plates 55/72, never punched). Screenshot profiling
  found the structural failure of every luma approach: content luma 55-65 vs threshold 48 vs frame
  <40 — a knife edge — while the gold TRIM sits at ~213. The card's true outline is now derived from
  the bright-trim contour once per kind, validated against the screenshot's band widths, and erased
  on EVERY layer mapping outside it. **The decisive find: the action halves fell at Image.Type.Simple
  — they are 9-SLICED Button plates, and the punch now replicates GenerateSlicedSprite's mapping.**
  First time in nine attempts they are touched. CacheVersion 4→5 (fourth load-bearing bump). If it
  fails: "CARD OUTLINE refused" = behaviour is exactly 112; otherwise BAND INVENTORY prints derived
  bands beside the culprit — disagreement indicts the derivation, agreement indicts an escaped
  consumer. Known pre-existing residual: hovered halves render Image.overrideSprite (§5e), uncovered
  by any bake.
- **ModBuild 112** — the border, attempt EIGHT: round 7's falsifier fired (ART RECT 100 % x
  100 % — no letterbox on his art), leaving the series' one POSITIVE measurement standing: the
  PEEL hit its depth cap twice (11/11, then 18/18 texels, mean luma 22). The band is a PRINTED
  near-black frame in the art's own opaque pixels — which is why mesh work was invisible (behind
  opaque art) and the verified stencil clip changed nothing (an opacity-captured outline INCLUDES
  the frame). Fix: pixel surgery on the mod-owned 'VR-mip' sprite copies the bake already swaps in
  — boundary-seeded BFS erosion with LEARNED depth (two guessed caps were both too small), alpha 0
  on the frame, gates logged. Punch runs BEFORE capture so the footprint is stamped from punched
  pixels — mesh and face agree by construction. CacheVersion 3→4 (third load-bearing bump). Round
  4 had declined exactly this as risky-against-speculation; the peel's measurements re-weighed it.
  **If it fails: grep CARD FRAME BAND INVENTORY — the culprit graphic is named, not guessed.**
- **ModBuild 111** — the black card border, attempt SEVEN, and **the lesson is about method, not
  about cards**. Six rounds reasoned about art that could not be read offline; the user supplied a
  SCREENSHOT and pixel measurement settled it in one pass. The band is TOP/BOTTOM (4.76 % / 6.6 %
  of the card height), not left/right (0.8 % / 2.4 %); its corner is a clean arc equal to
  `CardMesh.CornerRadius` and its colour a flat (4,4,3) identical across cards at different
  orientations — so it is the mod's MESH. Cause: slab and face rect coincide (round 5's "0.94
  refutation" stands), but the ability ART is poker-shaped and `Image.preserveAspect` letterboxes it
  to 90.54 % of the face rect's height, leaving 4.73 % dead top and bottom. Measured 4.76 %. The
  capture normalised by each candidate's LAYOUT rect, so the mask was stretched ~10 % vertically and
  declared the body "card" exactly where the art draws nothing — every one of six clips was correct
  and aimed 10 % away from the edge it sought. Fixed by replicating uGUI's `PreserveSpriteAspectRatio`
  (pivot re-anchoring included) from live values; nothing about size or proportions changes, which is
  what the user required ("Ich will es also so wie es jetzt ist … nur eben ohne die schwarzen
  Ränder"). `CacheVersion` 2→3, because a stale mask silently ships the previous round — that has
  already happened once. **Two integrator hypotheses were wrong in this series (sprite-less dark
  quads, and the aspect's axis); both were plausible, checkable and false. Measure before deducing.**
- **ModBuild 110** — attempt FIVE at the black card border, and the first that clips the layer the
  black is on. **The generalisable lesson: four attempts all acted on the card BODY, and the body was
  never what bounds the visible card.** The 109 log refuted the standing hypothesis with its own new
  diagnostic (exactly ONE sprite-less quad on a face, and it is WHITE) and reported a captured
  outline with real corner cuts (bbox 95.5 % of the face, 0.928 fill) — so if the clipped body bound
  the card, the cards would have stopped looking rectangular in attempt 2. They did not. Therefore
  the FACE paints over the body's whole footprint. `Cards/CardShapeMask.cs` now stencil-clips the
  adopted face itself. The reason that was not trivial: `MaskUtilities.FindRootSortOverrideCanvas`
  stops at `FullAbilityCard`'s own Canvas, whose sorting the game TOGGLES — so a mask above it can
  render a plain rectangle while every log line claims success, exactly how attempt 1 failed. It
  therefore neutralises `overrideSorting` on every canvas inside the face (recording each original,
  because a toggle can flip one after install) and VERIFIES itself by making the same two calls
  `MaskableGraphic` makes, refusing and removing itself if the stencil would not resolve.
  Integrator-verified against the local uGUI package: `Mask.IsRaycastLocationValid` filters by RECT
  only, so input is untouched, and `StencilMaterial` really does set `useAlphaClip`.
  A 0.94 coordinate-mismatch hypothesis (the integrator's) was raised and REFUTED by arithmetic —
  recorded as do-not-re-test.
- **ModBuild 109** — the card SILHOUETTE round. **The black card border took four attempts, and
  every wrong turn was a layer error**, which is why it is worth reading in full.
  Attempt 1 shipped an alpha clip that never executed (`isActiveAndEnabled` against cards adopting
  art under an inactive pool root; the rejecting branch logged nothing). Attempt 2 made it execute
  — and the border did not move. Attempt 3 found why, from the user's own sentence: during a
  character switch the loader takes the art down, nothing paints, and the border VANISHES — so the
  mesh behind it is already clipped, and **the remaining black is painted by the adopted uGUI FACE,
  not by the card body**. A clipped mesh cannot un-clip itself. The suspects are sprite-less
  `Image`s (22 enabled ones on a single face), which uGUI draws as plain colour quads — rectangles,
  incapable of carrying the card's shape. Attempt 4 mutes them in the same frame the art arrives,
  on the seam `CardFaceMipBake` already uses to swap sprites before a card's first drawn pixel.
  User then narrowed it: every placement, **no visible transition**, item cards, and every card a
  PEER draws. The transition is answered by not learning the shape in that session — the footprint
  is persisted to `BepInEx/config` and re-applied from inside the body's own material factory,
  before any renderer that will draw it has a material. Two uncovered local paths and two missed
  mirror sites were found; the peer FRONT was the bigger hole and needed no wire (the pump passes
  the CANVAS, whose GameObject carries no card component — the clone is its child).
  Also in 109: held figures keep the size they had at the grab (`TickHeldScale` pinned WORLD size,
  and the diorama zoom IS the rig scale the anchor hangs under — log: `boardWorld=1` at anchorScale
  41.368 vs 10.149); the slot a card lands in is ranked by the CARD, not the hand (an eligibility
  test had been used as a ranking key); and the boot spinner is the GAME's symbol (the previous
  round's "it is serialized inside the scene being loaded" was true of that OBJECT and wrong as a
  conclusion — a second copy lives in the Intro scene, switched on a frame before our arming edge).
- **ModBuild 108** — six reports, one round. **The generalisable lesson: three of the six were
  defects in a layer nobody had looked at, not in the layer the symptom pointed at.**
  (1) **THE DEADLOCK** (critical, user-flagged): lifting a figure mid-attack killed the turn
  machine permanently. No exception — a wait that never completes, hence nothing logged.
  `ActorBars` hides the figure's panel HOST while its mini is held; `AttackModBar`'s flow is a
  coroutine started ON that controller; Unity kills a coroutine when its GameObject is
  deactivated; `FinalizeFlow` (sole writer of `IsFlowActive = false`) is its last statement, so
  the flag latches; and `WaitingForPlayerIdle` waits on it with NO timeout. The find that shaped
  the fix: the game waits on the attack's **TARGET**, not the acting figure — a guard on "whose
  turn is it" would have missed it. Prevented structurally (`FigureBusy`), mechanism removed
  (`ActorBars` no longer hides a live-flow bar), watchdog behind both.
  (2) **MR plate flicker** on the pile captions — 107's per-registrant fix does not scale, because
  the order writer is usually not the registrant. `MrBacking` re-syncs plates phase-blind now.
  (3) **Pile symbols** — a second defect at the same place: `PileViewer.SetVisible` hit by the
  pooled-hand re-bind window. Hide debounced 2 frames. Closed rather than distinguished on a run.
  (4) **Card X spacing** — `HalfSelection` used the spread-free slot-home accessor (its only
  caller); a docked pair sat 5.5 mm wider, a 42 % change in the gap the eye judges. Accessor
  retired; the peer mirror fixed by converting through the board root (local slots carry
  `SlotScale`, remote prefab anchors do not).
  (5) **No turning while scrolling** — the axes never contended, the thumb did. Releases on the
  turn axis returning to rest, not on the scroll ending.
  (6) **Card silhouette** — the alpha clip had shipped in `6d7f1bb` and had NEVER RUN
  (`isActiveAndEnabled` against cards adopting art under an inactive pool root, rejecting branch
  logged nothing, retry budget burnt blind).
  Startup freeze: measured, not assumed — 21.3 ms of 2667 ms is the mod, 62 % is game-side YML
  parsing on the main thread. Spinner now covers the window; the mod's own 981 ms bundle inflate
  is prewarmed. See `.planning/startup-freeze.md`, incl. the bundle rebuild the user declined.
- **ModBuild 107** — two defects ModBuild 106 had already claimed to fix, both of which had been
  fixed ONE LAYER AWAY from where they live. That generalises, so it is the entry's headline.
  (1) The figure highlight is not raised by `FigureGrabDriver` at all — `ProximityGrabber` raises
  it, on the nearest grabbable inside its CARD-sized 13 cm palm reach. The driver's election is
  only a VETO, and the veto was distance-gated (`suppressed = inReach && !winner`), so a figure
  outside the sphere was actively written back to ALLOWED; the grabber reads those flags a frame
  late, so the figure CROSSING IN was momentarily the only allowed candidate and won "nearest" by
  default. One frame of amber at the far edge of the reach, walking figure to figure — "verschiedene
  Figuren", and "zu weit weg" because the leak fired at 130 mm, not at the 40 mm pick radius. Log:
  137 highlight lines against 9 elections. The 106 hysteresis/dwell stabilise the ELECTION and were
  never in this path. Veto is unconditional now and fails closed.
  (2) The quest text's one-frame blank was in the RENDER path; 106 had hardened the POLL path,
  which cannot produce a one-frame anything (the goal re-derives every 0.5 s). Two render paths
  were open and BOTH were closed rather than distinguished by a diagnostic on the next run: the
  label's draw order was written in Update *and* LateUpdate while its opaque MR plate copies it in
  Update only (plate paints over the glyphs in the frame between), and a single `!activeInHierarchy`
  frame on the tray mount takes the whole host panel down. Rank is Update-only now, guarded by a
  once-per-session assertion; the mount HIDE is debounced 2 frames, the SHOW is untouched.
- **ModBuild 106** — the two blinking slot rectangles and the card that lands in them were sized by
  two unrelated numbers with no dial between them: the card took `[Cards] SlotCardFill` = 1.45, the
  overlays took CODE LITERALS off the card metric (teal wanted-pulse 1.36, gold snap glow 1.24), so
  the card overhung the rectangle that had just marked its spot by 6.6 % (119.7 mm inside 112.3 mm,
  board metres, shipped defaults). The dial the user found, `[Cards] ActiveCardScale_*`, sizes the
  ACTIVE PILE via `ActivePileViewer`, which does not exist during selection — hence "no effect". The
  POSITION half of the coupling already existed (`SlotOverlayOffset`/`Spacing` feed both the glows
  and `SlotHomeOffsetFor`); only SIZE was missing. Now one per-board dial,
  `[Cards] SlotOverlayScale_{board}`, seeded with SlotCardFill's 1.45: the card is untouched, the
  teal grew to meet it exactly (user: "exakt ausfüllen"), the gold keeps its shipped 0.912 ratio so
  it still reads inside the teal when both show. `SlotCardFill` retired (ConfirmUndoSize pattern) —
  no migration marker, the successor key is new. Wire: field **171** (FACTOR range), needed because
  the peer's glows are not cards — `RemoteBoardFurniture` builds them from the recess metric × a
  factor, and record 11 only carries the finished card WIDTH. Ratio is now one shared constant
  `PlayTray.SnapGlowRatio` instead of a literal on each side. Side finding: `Fill` is in no
  `ConfigSteps` unit row, so the retired dial had been stepping off its own magnitude all along —
  the successor's `Scale` ending fixes that and a step vector pins it.
- **`76daf29`** (folded into the 106 bump) — quest text flickered because ONE empty poll blanked it: the goal
  resolves through `CardsGameApi.ActiveHand()`, which is null while the game re-binds a pooled
  hand, so "no goal" and "ask again in a moment" were indistinguishable. Now told apart at the
  source. Figure-grab highlight flashed because a bare radius is a step function and the hand
  was drifting *on* the boundary (log: 39 → 38 → 37 mm against a 40 mm radius) — now
  enter/exit hysteresis plus a 6-frame dwell.
- **ModBuild 105 `e4ccf8c`** — a peer's board was being **described** instead of shown. The
  decision row crossed as a label string; the receiver rebuilt flat plates. Fixed by realising
  the game already builds the whole widget tree on every client (`TakeDamagePanel` is a
  per-client Singleton, `ShowOtherPlayer`), so only ROLES need to travel — new record 29. Also:
  a peer's board sat on a static sub-ladder (0/4/8) below the panel ladder's base (100), so it
  could never rank against anything; card aliasing was a 1 s rescan timer, not a slow bake; the
  refusal sound was keyed on ownership after free focus had made "look at another player's
  character" a success.
- **ModBuild 104 `0064307`** — performance. `FindObjectsOfType<T>()` is O(*every loaded
  object*); three independent measurements price ONE call at 10–15 ms in a big room, and the
  mod made ~7 per second (~89 ms/s, delivered as 20–100 ms hitches). Replaced by
  `Core/SceneRegistry.cs`. The perf instrument also gained a **zoom axis** (`FRAME`/`SPLIT`
  now bucket a window's frames into near/middle/far thirds).
- **ModBuild 103 `06cb184`** — six reports, three of which were one defect class (a
  transparent deciding its paint order from something that moves). Includes the **invisible
  buttons** root cause, found after three failed rounds: the cap FACE is multiplied by the
  player's `[ButtonColors]` tint, the WELL it sits in is not, so at tint 0.5 a disabled face
  (0.105) is darker than its own hole (0.15) — pure arithmetic, hence a steady state.
- **ModBuild 102 `f200f7f`** — the wrist HUD. A uGUI canvas is read from its **-Z** side; an
  identity base therefore aimed the readable face out of the back of the hand while the gate
  revealed it from the palm, so the player saw the mirrored back face.
- **ModBuild 101 `a1623e1`** — `ConfigSteps` resolver: a key ending in a bare axis letter never
  matched a unit word, so its step fell to a fiftieth of the shipped default (0.05 mm per
  press). 73 of 351 dials changed step.

## 5. Open items — the successor's queue

### 5a. Committed to the user, not yet done

*(Empty. The slot-overlay unification that stood here shipped in ModBuild 106 — see §4. Note for
next time: "overlay" in the user's vocabulary means the two BLINKING RECTANGLES on the board where
a hand card may be laid, not the active pile off its right edge. The predecessor's entry here read
it the other way and that cost a clarification round.)*

### 5b. Awaiting the next hardware session (diagnostics are already in place)

- **The glasses among the symbols** (`[WorldUI] USE BARS: bonus-bar split KEPT …`). The
  predicate `CardsGameApi.BonusIsPlaceable` is a four-way conjunction that **fails open**, which
  is also the shape a host/client divergence takes. The new line names WHICH condition kept each
  row — compare the host's and the peer's logs for the same bonus.
- **Fog-tile shimmer**: grep `UNSEEN TILE ORDER` and read the **apply count**. A rewrite is the
  only event that driver has which can change anything on screen, so "the tiles moved" and "the
  count did not move" cannot both be true.
- **Perf**: the next capture should run with `[Optimize] QuietDiagnostics = true` (about a third
  of the ModBuild 104 saving is behind that switch) and `[Perf] SceneProfile = true` (feeds the
  zoom clause). Then read the new `ZOOM` clause on `[Perf] SPLIT`.
- **Figure pick radius**: `[FigureGrab] PickRadiusMillimeters` (5…130; **130 restores the old
  palm-wide reach exactly**). At the user's zoom 40 mm ≈ 1.1 hex widths — if the flashing
  persists after the hysteresis, the radius itself is the next lever.
- **ModBuild 149, apparition brightness**: read the `HAUNT FIGURES light level` line. It now also
  prints `_DirScale`, the canopy `MinVis`, their product, and the luminance an equally-occluded
  figure *would* measure. If the new mapping (forest 0.0783 / cellar 0.0205) is still too visible,
  a third photograph plus those numbers decides whether the canopy correction becomes applicable.
- **ModBuild 149, the walking figure**: read the `AnimPin` line printed once per process at release.
  **Non-zero drift means the animator really is being moved by root motion and THAT is the teleport;
  zero means the remaining suspect is the gait.** The `armed at shared clock` line now prints metres,
  seconds, m/s and the `RunBlend` beside the CENSUS line that prints the clip lengths, so one round
  settles whether 0.55 is the right blend for 1.39–1.62 m/s.
- **ModBuild 149, the shelf bang**: each shelf cue now logs on fire *and* on both drop paths with
  gain, ceiling, master, duck, dial, game volume, final source volume, clip peak and attack. If it
  is still inaudible, that line says which of the seven factors ate it.
- **Positional debt found in 149**: cellar card 5 resolves through the second link of
  `HauntPosition`'s chain — there is no `Haunt5` node, so the bang's x/z is the welded catalogue's
  centre rather than the shelf's. The floor y is right. The scheduling log names the node that
  resolved.
- **Open question put to the user in the 149 report**: whether "die kleinen blinkenden Kugeln"
  included the **fireflies**. They were deliberately left in (3–7 cm moving motes, and he has
  praised the flying sparks before); the three standing wisps and both eyeshine pairs are gone.
- **Highest-value fire move still unmade**: `FireSeq1.png` is a **2×2 flipbook of four distinct
  flame frames** and `fire_atlas_pipeline.py` takes **one** of them as a static puff. A real
  four-frame flipbook needs a larger atlas and a cell-index-over-time change.

### 5c. Declined by the user — do not re-propose without new information

The zoomed-out overview cannot reach 90 Hz without changing what the head camera declares
visible. The user was presented with the options and chose **none of them**:
- narrowing the head camera's culling mask (`[Optimize] HeadMaskFromScenarioCamera`),
- distance-culling VFX/particles,
- animation LOD for distant figures,
- bar material de-duplication (small but real look risk).

Expected outcome of what WAS built: close-in should reach a stable 90 Hz; the far overview
should be a **hitch-free 45**, not 90. Say so plainly rather than implying more.

### 5d. Parked by user ruling

- **Per-eye wall-fade stereo rivalry** — proven unfixable on the game's masonry shader without
  losing the dissolve (`.planning/wall-fade-stereo-rivalry.md`).
- **Static batching ("Bündelung")** — tried and completely removed; Apparance reveal-clones of
  batched sources are born without material slots (`.planning/static-batching-removed.md`).

### 5e. Known gaps, carried

- `Net/BoardVisual.cs`'s sub-ladder was fixed in 105, but mod-owned free plates near a peer's
  board (fan hints, ping labels) are still at order 0 and lose to it.
- 16 PENDING wire-coverage debts (`check-wire-coverage.py` lists them with reasons).
- `Selectable` sprite-swap transitions write `Image.overrideSprite`, which the mip bake has
  never covered — hovering an action half can still sample a mipless state sprite.
- A receiver whose own take-damage prompt is docked falls back to plates for that window
  (self-healing, logged).

## 6. Working practice that actually matters

### Your role: INTEGRATOR (user ruling, 2026-08-11, standing)

> "Ich will, dass du hauptsächlich dafür da bist die Ergebnisse der agenten zu mergen und deine
> Aufgaben an spezialisierte Agenten abgibst die auf feature branches arbeiten. So sollst du deine
> Anstrengen auch parallelisieren. In Begründeten Außnahmefällen kannst du auch mal direkt etwas
> implementieren, sonst an Implementierungsagenten auslagern."

Implementation goes to specialised agents on feature branches, in parallel. Implementing yourself
is the exception and needs a stated reason. **"The files are coupled" is not one** — that was the
argument used in ModBuild 106 and the user rejected it, because coupling is something the
integrator designs away *before* dispatching:

1. **Land the shared contract first.** Any symbol several workers need (a config accessor, a Tune
   id, an interface) is a thin slice you write and commit to `main` yourself BEFORE fanning out.
   Minutes of work, and every worker then compiles on its own.
2. **Split by FILE OWNERSHIP, not by topic** — name each worker's exclusive file set in its brief.
   Overlapping FILES cause collisions; overlapping subject matter does not.
3. Typical lanes for a board feature: local/render · net mirror · localisation + options UI ·
   tests + checkers.
4. **Always pass `isolation: worktree`** — without it they collide in the shared checkout.
5. Brief every worker with: the hard rules (§1), the five gates (§2), "do NOT bump ModBuild",
   "do NOT commit or push", "never `git stash`", and the exact user report **verbatim in German**.
6. **You** review every diff, merge, bump ModBuild, write the build note, push, and report.

Worker first command: `bash scripts/worktree-setup.sh` (links `ressources`, `libs`,
`.planning/debug/default` and the refactor-guard baseline).

### Hazards that have actually bitten (all of these cost a round)

1. **`git stash` is NOT worktree-isolated.** `refs/stash` is shared by every worktree; two
   agents stashing concurrently pop each other's work. Forbid it in worker briefs.
2. **The shell's working directory persists between tool calls**, and `cd`-ing into a worktree
   to inspect a diff leaves you there. Two commits landed on a worker branch instead of `main`
   this way. **Use absolute paths, and verify with `git log --oneline -1 origin/main` after
   every push.**
3. **`git apply` is atomic.** It prints "Applied patch to X cleanly" per file and then rolls
   *everything* back if a later file conflicts. A green build afterwards proves nothing — a new
   file compiles on its own. Verify a merged symbol with `grep`.
4. **`grep -c` returning 0 exits non-zero** and breaks `&&` chains. Use `;`.
5. Bash heredoc + Python triple-quote collide — write the script to the scratchpad first.

### Logs

The user drops hardware logs in `.planning/debug/` (gitignored); a peer's logs go to
`.planning/debug/remote/`. Tuned config snapshots land in `.planning/debug/default/` and are
taken over with `python3 scripts/rebase-defaults.py apply`. **Those cfg values are always tuned
against the NEWEST build — take them over verbatim, never re-express them for a frame change
that shipped in the same round.** (Learned the hard way; see the do-not-swap note in
`WristHud.ApplyPose`.)

Always check the build stamp at the top of a dropped log before reasoning about it:
`[Core] GloomhavenVR ModBuild N` and `v0.1.0 build <sha> [main]`.

### Never invite a test before the round is pushed (user ruling, 2026-08-11, standing)

> "Ich hatte aber gepulled und kompiliert. Sorge dafür das nach einer Runde wenn ich zum tsten
> aufgefordert bin die änderungen auch immer im master gepusht sind!"

He pulls and compiles from `main`; an open worker lane is invisible to him. So:

- **Do not name log strings to grep, expected visual changes, or things to watch for** for any
  item whose lane is not yet merged AND pushed. Describing what to look for reads as an
  invitation to test even without the words "please test" — that is exactly how this was broken.
- **End every round-closing report with two explicit lines:** the commit to test (verified with
  `git log --oneline -1 origin/main` *after* pushing), and which reported items are NOT in it.
- While lanes are still running, either say plainly "noch nicht testen" or scope the test to the
  merged items and name them.

Same economics as the test-confidence rule: a run whose outcome is already known costs him a
full game launch plus headset time.

### Reporting to the user

German, prose, no bullet-point dumps. State the **root cause**, separate what was **proven from
the log/source** from what is **inferred**, and say plainly what you did *not* do and why. He
values a stated limitation far above a smoothed-over one, and he has corrected over-confident
claims more than once.

---

## Standing technical decisions

- BepInEx 5.4.23.5, HarmonyX, net472, publicized refs; OpenXR 1.10.0 + XR Management 4.5.0;
  MultiPass.
- Never patch `ScenarioRuleLibrary`/Bolt; commit through UI seams only.
- License GPL-3.0 (LCVR/RepoXR pattern reuse, credited); SteamVR hands BSD-3.
- Module config: `dev.gloomhavenvr.<module>.cfg` via `ModuleConfig.Create`.
- Distance ladder (`WorldUI/CanvasConversion.8.Order.cs`): base 100, step 16, nearer = higher
  order; `OrderAboveDistance(eyeDistance, lift)` is the seam for non-panel plates.
- `HandRig.Wrist` is **not** in `HandRig.Root`'s frame: `Anchor_Wrist` carries +90° about X, so
  wrist +Y is along the fingers and wrist +Z is out of the palm. A uGUI canvas parented there is
  read from its **-Z** side.
