# Hardware-observable changes — refactor 2026-09

One section per lane. Tier 0–2 commits add nothing here. Every Tier 3 fix adds one line: what he should observe, and the log token that proves the code ran.

## worldui-front

**T1 — the flat 2D campaign map stopped sweeping the whole scene once per frame.**
`FlatScreenStereo.DetectActiveMap` opened with a bare
`Object.FindObjectOfType<MapChoreographer>()`, and its caller chain runs every frame while that
path is engaged (`EndStackSync` → `EnsureAlbedoReady` → here). It now reads the cache
`TickFastMapEngage` has kept since the fast-engage round, with the same 10-frame throttle and the
same Unity-fake-null re-arm; the COLD path is unchanged.

*What to do:* only with **`[Rig] Vanilla2DMap = true`** (the flat 2D map — the 3D map room does
not take this path at all). Open the campaign map, leave it open a few seconds, switch
**world ↔ city**, and open a scenario from it.

*What he should observe:* **nothing different.** The map appears when it appeared before, the
world↔city switch re-gathers as before. If anything is smoother on the map screen that is the
per-frame sweep being gone.

*The log token that proves it ran:* `MAP SELECT (ISSUE 3)` — it must still print on every
world↔city switch, naming `active map = CITY` / `= WORLD`. Its ABSENCE on a switch is the
regression: it would mean the cached choreographer went stale instead of re-arming.
`MAP RENDER detection (FAST positive)` must still print once when the map opens.

## worldui-frame

### One behaviour change, and it is the only one in this lane

**F-01 — a trigger pull inside a drag bar's hover tail now grabs the figure behind it.**
*Observe:* hover a floated window's (or the tray's) drag bar with the palm, move the hand onto a
miniature or a map item within 0.20 s, and pull the trigger. The mini is grabbed. Before this build
the pull did nothing at all: the two ray drivers had already yielded the uGUI press and the laser
carry to an offer the grabber then refused.
*Log tokens:* a `uGUI press YIELDED` or `laser-carry YIELDED` line, followed by a grab — and NO
`is no longer the elected candidate` refusal for a `GrabWithGrip` target.
*If it is wrong:* the pull takes the figure behind the bar when he wanted the bar. The bar still
takes the GRIP button, which is the 2026-08-11 two-button ruling, so the bar itself is unaffected.

### Everything else in this lane is a LOG-ONLY change. Nothing about the picture, the poses, the
### dials or the wire moves. What follows is what he should now SEE IN THE LOG that a default-level
### log has never carried — each line's token, and what its appearance means.

| token (grep) | what its appearance means | finding |
|---|---|---|
| `TURN STALL WATCHDOG FIRED` | the ModBuild 107 turn deadlock happened and the mod repaired it. **A defect report, not a healthy recovery** — its own text says so. Silent since ModBuild 107. | F-19 |
| `STRETCH CAPTURE ON` … body radius | the line now reports the renderer it measured instead of always claiming the CENTRE fallback. If it still says "NO renderer survived the sanity ceiling", that is now a real reading. | F-18 |
| `[Props] NO ghost for '<name>'` | a held map item left no ghost at its hex, and why. Previously both failure paths returned in silence and read like "the prop was never grabbed". | F-22 |
| `[Ownership] HAND FAN` census | now prints once per SCENARIO. Before, a second scenario with the same party and assignment printed nothing at all, and the missing falsifier read like "the duplicate did not happen". | F-31 |
| `[EnemyInfo] ENEMY-INFO PHASE SKIPPED` | the mod pressed the host's "Fortfahren" for the whole table. | F-32 |
| `[Ping] press rejected` / `no ping entry point found` | why an A-press did not ping. The whole chain was silent at the shipped level, which is what made "the joining peer cannot ping at all" undiagnosable. | F-33 |
| `[Tap] → SELECT/PING` and the touch-commit line | the fingertip proof. The 2026-09-03 "Fingerspitze" report was diagnosed by reasoning because this line was invisible. | F-38 |
| `[Focus] switch REFUSED` / `FOCUS PIN engaged` / `now looking at` / `SELECTION GUARD` | the character-focus evidence chain, quoted in its own docs as read off `LogOutput.log` — it has not appeared in a default log since 2026-08-30. | F-35 |
| `stable hex decal ZTest=` / `HEX PROJECTOR guard [install]` | two lines INVARIANTS lists as grep tokens. Once per session each. | F-36 |
| `GRAB STATE heal:` | the mod force-released a stuck hold. This is the line that explains "the laser vanished and came back". | F-02 |
| `Poke WITHHELD` / `Poke press CANCELLED` | why a physical fingertip press did nothing. Both are the falsifier for the grip-chord ruling that produced them. | F-03 |
| `PANEL SUPERSAMPLE engaged on '<window>'` | **the one that makes the rest readable.** A hardware log can now say whether a given floated window was supersampled at all. | F-64 |
| `PANEL SUPERSAMPLE stands down` / `refused` / `capture-layer POOL` | why sharpening did NOT happen on a window — each of these lines ends by naming the shimmer he would still see. | F-64 |
| `MIP BAKE ARRIVAL watch could not be installed` / `tick failed` | freshly loaded art stays aliased for ~1 s again. | F-64 |
| `CAMERA ORDER` (shape / depth → / restored) | the probe's baseline, and the mod's WRITE to a game camera's depth. Its class doc promised the baseline "even when it finds nothing" and could not deliver it. | F-68 |
| `HOST SCENE PIN FAILED` | the "Quest verwerfen" deadlock guard did not manage to pin a host. | F-48 |
| `MODAL REVEAL: … FORCED` | a window revealed before it settled and may show one visible correction; any pending re-place was skipped. This is the "the window is in the wrong place / it snaps" line. | F-49 |
| `MODAL RENDER PHASE VIOLATION` | a visibility write landed inside the render phase — the one-eye class. Carries a stack trace. | F-50 |
| `HIT RECT: … swallowed` | a window may be unclickable outside its own frame. It could previously be silenced for the whole session by an unrelated throw. | F-47 |
| `MODAL WINDOW: … NOTHING MEASURABLE` / `FIXED FIT CONCEDED` | a window revealed at its raw rect, or its sub-view is drawn at the size the game gives it. For a SHARED window the first of these is the reason its home looks wrong on both machines. | F-51 |
| `VR options: no VR settings menu this session` | the VR settings menu is gone for this run, and why. | F-74 |
| `UI SOUND EAR cannot be repaired` | **the answer to "still no button sounds"**, which the code has always known and never printed. | F-74 |
| `disabled — …` (input-field watch, ESC-menu block, settings click exemption) | one of three input seams is off; the first of them costs frame time inside a scenario. | F-74 |
| `CHARACTER 3D REFCOUNT: … stands down` | two windows on one character will blank each other's model. | F-74 |
| `PartyPreviewStorm STOOD DOWN` / `PARTY PREVIEW STORM` | the attribution pair that decides whether the next flicker round is worth spending. | F-74 / F-80 |
| `VR options: could not leave the escapable/controller input-area stack` | the ModBuild 336 separation did NOT happen. Previously its absence was identical to three other states. | F-75 |
| `CONFIRMATION RESCUE: no ConfirmationBox…` | one arm of the deadlock guard is inert on this build of the game. | F-77 |
| `OPTIONS TAP: no ESCMenu object exists` / `SECOND CHANCE TOOK` | "the X button did nothing", and the rescue that fired instead. | F-78 |
| `RE-ASSERT WATCH NOT ARMED` / `FAILED TO START` / `RE-ASSERT FIRED` | the logo watch did not run, or something outside the patch is rewriting the logo — with the graphic named. | F-79 |
| `MODAL WINDOW: … RE-FACING ABOUT THE FRAME ORIGIN` | the pre-ModBuild-240 behaviour returned on a release. Its own comment says "NEVER SILENTLY". | F-81 |

**What to expect on a healthy session:** none of the tokens above except
`PANEL SUPERSAMPLE engaged on …` (once per floated window) and, if a map item is held,
`[Props] ghost spawned` / `NO ghost for`. Every other line in the table reports a degradation, a
refusal or a repair — if the log is quiet, that IS the result, and for the first time it is a result
we can distinguish from an instrument that was never printing.

**The one number to watch:** the log should not get materially longer. Every promotion in this lane
is behind a one-shot latch, a per-window/per-release/per-tap edge, or a Harmony resolver; no
per-frame-capable line was promoted anywhere, and the ones deliberately left silent are named
individually in the review (§6 F-51, §7 F-78/F-80/F-81/F-87, §8 F-64).

## net

Tier 0–2 add nothing here (comments, crefs, one dead const, pure motion, three identical-output
merges). The Tier 3 fixes below are all INSTRUMENT-side or failure-path: at the shipped defaults,
with nothing throwing, the picture on a peer's board is unchanged.

| what he should observe | the log token that proves it ran |
|---|---|
| **A refused burn rig now says so at the shipped tier.** Previously a throw inside `BuildBurnRig` left the rig standing as `Ready` over null arrays, so that card silently ignored every later look for the rest of its life and printed nothing above DEBUG. If it ever fires, the card draws CLEAN (unchanged picture) and the log now says which clone gave up. Zero occurrences is the expected and best reading. | `Remote burn rig skipped` (now `VRLog.Note`, was `VRLog.Debug`) |
| **The card-FX arming lines can now tell two surfaces apart.** A peer's mirrored HAND-FAN used-card look and a plain card FLIGHT both used to latch as `[Unnamed]`, so whichever armed first silenced the other's line for the whole process. Expect `[HandFan]` and `[CardFlight]` to appear where `[Unnamed]` did — and `[Unnamed]` itself to stop appearing (if it appears, a NEW driver is unnamed, which is what the line is for). | `Remote BURN look` — read the `[surface]` tag on it |
| **`MAP PLACARD SCALE` prints the millimetres the placard was actually sized with.** It printed THIS viewer's `[WorldUI] CanvasScaleMm` and a paragraph saying the owner's copy was out of reach — both true before ModBuild 480 fixed F3 and false after it. The placard's SIZE does not change (480 already fixed that); the line does. Read `ownerCanvasScaleMm` against that peer's own `DeriveWindowScale` reading: they must agree unless a `(SHIPPED default …)` marker says why. | `MAP PLACARD SCALE` (unchanged token, and `THE LEGIBILITY FACTOR IS THE OWNER'S` still appears in it) |
| **`Decision widgets RECEIVED` prints role CODES instead of option-flag words.** Record 29 carries role bytes (an enum), and the receiver was rendering them with the bit-field vocabulary — role 4 read as `greyed+CHOSEN`, role 1 as `OFFERED`. Now `#0=4, #1=5`, matching the sender's own line so the two can be diffed. Picture unchanged. | `Decision widgets RECEIVED` vs `Decision widgets SENT` |
| **A packet this build cannot parse is now visible.** Previously a refused packet was counted as received and otherwise silent, so a peer on a different wire version looked identical to a healthy one (nobody appears, no line). Nothing about the picture changes. In a normal session expect ZERO of these; a foreign side action would also read as one and is harmless. | `PACKET REJECTED` (once per sender), and the `REJECTED` clause on the 10 s `RX` summary |

## core

Nothing in this lane changes a picture, a sound, a pose, a cadence or a wire byte. The compiled form
differs in nine files and every difference is a comment, a string literal, a renamed private method,
one added `Detach()`, one moved window reset, one unreachable branch removed, or a const that moved
into `Defaults` with its value unchanged. There is no Tier 3 behaviour change to observe.

Three things a tester WOULD see, none of which needs a test pass of its own:

1. **`[Haunt] EasterEggs`'s description text, EN and DE** (HT-6). The English no longer promises
   "eyes that open in the undergrowth" or "a dim warm door opening at the top of the stair" —
   both apparitions were deleted at ModBuild 149 and their cards are inert placeholders. The German
   was two rounds further behind and also promised the rejected window walk-past ("von dem du nur
   die Beine siehst"), the rejected stair walk and a face behind a tree. Visible in the settings
   browser and in `BepInEx/config/…Rig.cfg`. The KEY, the DEFAULT and the wire are untouched, so no
   tester's cfg value moves.
2. **`[Comfort] SavedScaleMultiplier`'s description text** (RV-2): "after each two-grip scale
   gesture" → "two-stick-click", which is what the gesture has been since the P8 rebind.
3. **Two log lines read differently** — `ENV SOUND up` no longer names the four night-call voices
   ModBuild 246 withdrew (SD-6), and `DOOR OPEN CLIP SAMPLE`'s two READING: tails no longer promise
   a wall-shader fallback that ModBuild 429 removed (DW-1). Every SHOUTED grep token on both lines
   survives — `AND FIVE MORE ANIMALS`, `AND THREE EERIE ONES SINCE`, `DOOR OPEN CLIP SAMPLE`,
   `PLAYABLE GRAPH`, `CLIP AND ITS PATHS` — and the guard confirms 0 tokens removed.

The one fix with a runtime consequence, RV-1 (`ComfortSettings.Unbind` now detaches the 22nd
wrapper), is reachable only through `RigModule.Shutdown()`, i.e. a hot reload. Nothing to observe in
a normal session.

## cards

**Nothing to re-test on the headset.** Every source change in this lane is Tier 0-2 and the
compiled-form guard says so: the four `CHANGED` types are exactly the ones the commits name, and
the three surfaces the guard cannot see are unmoved (config keys, harmony patches and log tokens
all diff to 0 removed).

- Tier 0 (`2db74932`) removed four members nothing read (`VRCard._heldRot`,
  `ItemsPile.ItemChip._heldRot`, `PlayTray._itemUseSlotGlow`, `PlayTray.SquareCapThickness`) and
  corrected seven comments. A field with no reader cannot change a picture.
- Tier 1 (`f3aa55fc` + `350a33f2`) cut `CardsConfig.Bind` into thirteen slices in the same order.
  The one thing to notice is a NON-event: **his `dev.gloomhavenvr.cards.cfg` must be unchanged.**
  The 156 keys were extracted in source order before and after and are identical, so BepInEx
  rewrites the same file in the same order. If a key ever moved, its tuned value would revert
  silently — which is why the order was proved rather than assumed.
- Tier 2 (`601bbdd8`) made the owner's active-half pulse call the same expression every peer's
  mirror already calls. Same inputs, same outputs; the only difference is that a throw out of the
  game's `FindCasterActiveBonuses` now answers "whole card lit" instead of reaching the driver's
  per-tick guard — the mirror's long-standing behaviour, and invisible unless the game throws.

**What he WILL notice, all of it off-headset:** a red build now stops `refactor-guard.sh` with
the compiler's own error lines instead of printing a green verdict (`e464f786`); the guard line
reads `config keys 625` instead of `412` (`07c383ce` — the census can now see the `[Comfort]`
and per-board keys, so the next `baseline` records the larger number); a pull request runs nine
more gates on GitHub (`dca03c86`); and `log-triage.py` marks debug-tier tokens in its SILENT
list instead of listing them bare (`7587e1e5`), which matters the next time a default-level drop
is read.

