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


## wallfade-rescan

**N2 — a throw inside the wall-fade COMMIT no longer re-runs that commit on every frame.**
(`REVIEW-core.md` §8.4, deferred there because the trade-off needed a ruling; **he ruled on it
2026-09-08**.) `_rescanStage` was left standing at `Commit` when a commit phase threw, and
`StepRescanCycle` has no `Commit` stage block — so every subsequent `LateUpdate` fell through to
the commit and re-ran the whole ~73 ms pass against a half-built table, for the rest of the
session. The wall-path audit is gated on `_rescanStage == RescanStage.Idle`, so it was starved
for exactly as long. The cycle is now abandoned and the next one starts clean from a fresh sweep.

*What to do:* nothing special — play a scenario as usual, ideally the "advanced tileset" one that
has produced the previous wall-fade rounds, and walk far enough that walls fade in several rooms.

*What he should observe: **nothing different at all**.* This path only exists once a commit has
thrown. In a healthy session the commit does not throw, and then not one line of this changes:
same fade, same cadence, same commit cost. **The `[WallSegmentFade] BUDGET` line must keep
printing every ~5 s with a non-zero cycle count** — that is the proof the pipeline still runs.

*The log token that proves the new path ran:* **`WALL COMMIT THREW`** (`VRLog.Alert`, so it
prints at the shipped default level; marked `// HW-VERIFY`). Every occurrence carries
`occurrence <n> this session` and the stage, and the exception's own stack names the commit
phase. It is change-gated on the exception's type+message with a 30 s heartbeat, so a persistent
failure is one line plus a heartbeat and never a flood — but the **count keeps rising**, so a
single printed line reading `occurrence 412` is the honest report of 412 failures.

*How to read it:*

| what the log says | what it means |
|---|---|
| no `WALL COMMIT THREW` anywhere | the commit never threw. This build behaves exactly like ModBuild 480. |
| one line, `occurrence 1`, and the fade keeps working | a transient throw. It healed on the next cadence (≤ 2 s, `[WallFade] RescanIntervalSeconds`). **This is the trade-off he accepted**: before this build it would have healed on the next FRAME instead — and before that, in the persistent case, never. |
| a line whose count climbs across heartbeats | a persistent throw. The stack in the line names the phase; that is the bug to fix. The cost is now one commit per ~2 s instead of one per frame, and the wall-path audit runs between them. |

*If it is wrong:* the failure mode to watch for is walls that stop fading and **stay** unfaded
while the log is silent. That would mean the cycle is being abandoned without the line printing —
the opposite of what this change is for. `[General] LogLevel = Debug` then also brings back
`driver tick threw (logged once)`, which is unchanged and still one line per session.


## integrator (ModBuild 481)

Cross-lane changes made at integration, none of them a pixel move:

| what | what he should see | how to tell it ran |
|---|---|---|
| four German descriptions corrected (map-room bar height, ghost hand, two of the four per-style seat texts) | the tooltips now match the English text and the shipped default beside them | read them in the menu; nothing else changes |
| four one-shot migration markers withheld from the settings browser | four fewer toggles in the raw Erweitert list: `[PeerBoardFade] DwellsMigrated312`, `[Perf] ProfileDefaultsMigrated227`, `[WallFade] WallFadeBarsMigrated252` and `256` | they are gone from the browser and STILL IN THE .cfg — the keys are untouched, only their menu row is withheld |

The keys are deliberately NOT removed. A removed key reverts a player's tuned value with no
message; a withheld one keeps its value and simply stops offering a row whose only effect is to
re-run a migration against numbers he has since tuned.


## options-degrade-scope

**One behaviour change: a failed VR-menu injection now costs ONE options window instead of the
whole run** (his ruling on F-76, 2026-09-08 — scope the latch to the window instance, with a
consecutive-failure counter as the backstop).

`VROptionsTab._degraded` was set by the FIRST failed injection and cleared by nothing: `Forget`
cleared eleven other per-pane flags and not that one, and `Tick`'s first statement is
`if (_degraded) return;`, so the re-injection the class's own doc promises ("re-injects when the
options window is replaced") could never run again. `Forget` now clears it as its twelfth flag; the
window that failed is refused by identity so it is never re-cloned; and the process-wide give-up is
moved to the THIRD consecutive failed window (`MaxConsecutiveInjectionFailures`, a const, not a
config key).

*What to do:* nothing special. Play a session that crosses scenes — main menu → campaign map →
scenario → back — and open the VR settings from the pause menu and from the main-menu row in each.
The options window is a `Singleton` that does not survive every scene, so this is exactly the path
that builds a second and third one.

*What he should observe:* **nothing different, on a healthy session.** The injection succeeds on the
first options window, the counter is never incremented and neither latch is ever set — the code
below the threshold does not run at all. The change is only visible on a run where the menu was
already failing, and there it can only ever add a menu that used to be missing.

| token (grep) | what its appearance means |
|---|---|
| `VR options: injection attempt` | **NEW, and the whole point of the round.** One options window failed to take the injection and got no VR menu — the line names the attempt number, the threshold and the reason. The NEXT options window is tried from scratch. At most two of these can ever print in a session, and a run that shows one followed by no third line is the case the old code turned into a dead settings menu for the session. |
| `VR options: no VR settings menu this session` | unchanged wording, changed meaning: it now means **three** options windows in a row failed, not one. Its absence where it used to appear is the fix working. |

*If it is wrong:* the failure mode to watch for is the opposite of the old one — a `VR options:
injection attempt` line repeating far more than twice, or repeating for the same window, would mean
the per-window refusal is not holding and the retry has become a loop. It cannot flood: the line is
below the threshold by construction and the third failure latches the process. The other direction
(the menu still missing after a scene change, with NO new line in the log) means the injection never
failed and the menu is missing for some other reason entirely.

## cadence-edges

One change, and it is a TIMING change only: **no byte of any record moved.** Records 36
(held-card face), 39 (sacrifice seat), 41 (spent half) and 43 (fan source) were sampled AFTER
`NetAvatarDriver.TickExtrasSend`'s pre-emption gate, so they were the only discrete, human-paced
edges in that method that could not force a packet out. A pluck therefore reached peers up to one
200 ms interval after the rig packet that had already moved the slab, and the peer's slab showed a
BACK for that window (`REVIEW-net.md` N7). His ruling of 2026-09-08: **1:1 covers TIMING**, these
four are human-paced and therefore rare, so the extra packet per edge costs practically nothing —
chosen over spending a hardware round to measure the delay first.

| what | what he should see | how to tell it ran |
|---|---|---|
| the four edges now pre-empt the 5 Hz cadence | a card plucked out of a hand or a pile shows its FRONT on the peer's board at the same moment the slab arrives, instead of up to 200 ms later; likewise the short-rest sacrifice's face, a half going grey, and a long rest's fan switching to the DISCARD pile | new grep token **`HELD-CARD EDGE PRE-EMPT`** at `Note` (prints at the shipped default level), naming WHICH of the four terms forced the packet and how many ms early it went |

**Read it as a pair.** The pre-empt line is immediately followed by that term's own existing SENT
line — `Held-card face SENT`, `SHORT REST SEAT`, `SPENT HALF SENT`, `FAN SOURCE SENT` — and then by
the peer's receive line (`Remote held card FRONT`, `SHORT REST SEAT`, `SPENT HALF`). None of those
tokens was touched.

**Falsifiers, and they say different things.**
1. *A whole session of the four SENT lines with NOT ONE `HELD-CARD EDGE PRE-EMPT` beside them* —
   then every one of those edges happened to land on a cadence tick, which for a human hand is a
   1-in-5 coincidence repeated N times. The terms are not in the gate and this change never ran.
2. *`HELD-CARD EDGE PRE-EMPT` at anything approaching frame rate* — then one of the four IS
   chattering despite all four being discrete latched state, the term the line names is the one to
   pull, and the 5 Hz cadence has become a stream. This is the one outcome that would cost
   something, and the line names the culprit without a second round.
3. *The pre-empt line fires, the peer's receive line follows, and he still sees a BACK for a beat* —
   then the delay was never on the send side. The receiver's own face resolve is throttled to
   `RefreshSeconds` (250 ms), which is a longer window than the one this change closes, and it is
   the next thing to read rather than anything in `TickExtrasSend`.

**Cost, and why it is close to nil.** A pre-empted packet zeroes `_extrasAccumulator`, so an edge
SHIFTS the next cadence packet earlier rather than inserting an extra one. Extra packets happen only
where two edges land inside one 200 ms interval. Worst realistic case per record: record 36 a
two-handed pluck, ~2 edges/s for a second or two; record 39 one edge per short rest or modal pick;
record 41 at most four edges per ROUND (two halves on each of two cards); record 43 two edges per
long rest. Against the 5 Hz baseline the steady state is therefore still 5 packets/s, with a brief
peak near 10/s during a pluck burst. Nothing here can sustain a higher rate, because all four read
discrete latched state and none is derived from a continuously-varying value.

## panel-order-behind

One commit, one finding (**F-46**, `REVIEW-worldui-frame.md` §6). The change is a **log-only
change with a negative observable**: a line that has fired once per session since ModBuild 439
must now never fire.

**F-46 — the panel ladder stops accusing the window-materialise debris of an out-of-band offset.**

The debris cloud that flies off a window as it materialises or dissolves is drawn by two
renderers, one at ladder offset `+1` and one at `-1`, with the window drawn between them: a
converted panel writes no depth, so `sortingOrder` is the only thing that can put geometry behind
one. The ladder's bound check (`CheckFollowerOffset`, ModBuild 439, survey row R41) accepted only
`[0, 16)` and was applied to that registration unconditionally, so **every session in which a
window materialised with debris on emitted `PANEL ORDER FOLLOWER OUT OF BAND` at the Alert tier**
— a player-visible warning about an offset the design had chosen on purpose — and then applied
the offset anyway. `PanelOrderStep`'s own doc listed the debris' `+/-1` and excluded it one
sentence later, in the same commit; the check was written from the second sentence.

*What to do:* nothing special — open and close any floated window (a character sheet, the combat
log, an item card) with **`[WorldUI] WindowMaterialise*`** left at its shipped values, so the
debris is on. One appear and one vanish is enough. Repeat once in multiplayer with a peer's board
visible, so a free-floating identity plate is on the ladder at the same time.

*What he should observe:* **nothing different in the picture.** The debris still flies from where
the window broke up, half of it in front of the window and half behind it. No number and no draw
order changed: `-1` and `+1` are still the compiled constants (the guard's decompiled assembly
shows `RegisterOrderFollower(panel, debrisCloud.Behind, -1, allowBehind: true)`).

*The log token, and it is the ABSENCE that is the pass:*

| token (grep) | before this build | now |
|---|---|---|
| `PANEL ORDER FOLLOWER OUT OF BAND` | once per session, at `Alert`, naming `'Behind' registered at offset -1` | **must not appear at all** |

- **If it appears naming `'Behind'` at offset `-1`,** the exemption did not reach the registration
  and the fix is inert.
- **If it appears naming anything else** — any name, any offset — that is a REAL out-of-band
  follower and a genuine finding: the check was not weakened, only its floor was lowered by
  exactly one for the one caller that asks by name. `-2` still reports, from that caller as
  loudly as from any other, and the printed bound now names the floor the check actually used
  (`outside [-1, 16)` for the exempt caller, `outside [0, 16)` for everyone else), so the line
  cannot quote a bound it did not apply.

*The tie that was accepted rather than removed:* slot−1 is also the seat the furniture band
reserves for the free-floating identity plates (`Net.BoardVisual.OrderWithPanels`), so the
debris' behind-half shares it whenever a board cluster's ladder rank equals the materialising
panel's. Both are under the window either way, and an equal `sortingOrder` resolves back-to-front
on camera distance. **This has never been reported and is not something to go looking for**; it is
written down at `CanvasConversion.BehindPanelOrderOffset` and at the registration site so that a
future report about a peer's name tag flickering against a dissolving window has somewhere to
land. If he ever does see the name tag and the debris trade places, that is the tie and the note
names it.

## integrator — the glove's normal map (BAKED, needs the new bundle)

| what | what he should see | how to tell it ran |
|---|---|---|
| the leather glove's `_NormalStrength` baked at 0.50 instead of the shader's 1.00 | the fingers stop reading as WRINKLED at arm's length; the leather's shape is still there, its micro-creases are half as deep. Plate and arcane hands unchanged. A peer's mirrored hands change with his own, because both are built by the same method | there is NO log line, and that is deliberate: the value is in the material, so nothing at runtime decides it. The proof is the picture and the bundle's new byte size |

**THIS NEEDS THE NEW BUNDLE.** He ruled on it in those words — *"Es macht mir nichts aus, dass ob
das bundle dafür neu gebaut werden muss - lieber sauber"* — so the number lives where the material
is authored (`BuildHands.HandSets`) rather than being corrected at load. A DLL-only drop will NOT
show this change. If it is still too strong, or now too flat, the number to move is the
`normalStrength` column of the glove row in `HandSets`, and it needs another bundle build.

## cellar-one-sky

**T3 — the cellar has ONE star sky now: the curved patch at the window. The 45 m dome is gone.**

*His finding, verbatim (2026-09-08):* "Im Keller gibt es zwei Sternenhimmel — einmal der gekrümmte
am Fenster und trotzdem gibt es noch eine echte Kuppel wie in der Waldumgebung. Das braucht es dort
dann nicht mehr — der gekrümmte reicht."

`BuildEnvironments.BuildCellar` no longer calls `AddNightSky`, so `Env_Cellar` carries no `StarDome`
and no `StarField` (16,808 triangles, the heaviest single node in that room). The forest is
untouched — it is outdoors and the dome IS its sky.

**THIS IS A FULL INSTALL, NOT A DLL DROP.** The fix is in the asset bundle. Every build since
ModBuild 368 has been DLL-only; this one is not. He must install the rebuilt `~75 MB` bundle as well
as the plugin, and the bundle rebuild is OWED — this lane changed the bake source only; the
integrator runs `scripts/build-bundles.sh` once (Unity **`/home/claw/unity-2021.3.5`**, never
`unity-2021.3`) and `scripts/check-bundle-format.sh` once over the result.

*What to do:* `[Rig] Sky Style = Cellar`, in a scenario. Play normally first — stand in the room,
look at the barred window, look up at the plank ceiling and the beams. Then **zoom far out with the
world grab** until the cellar reads as a model in front of you, and look around.

*What he should observe:*
- **Inside the room: nothing different, anywhere.** The window's curved sky, its wood, the moon
  beam, the candles, the dust and the ceiling are identical. This is the whole point — the shell is
  closed (`Env_C_Ceil` spans the full footprint face-down; the stair alcove ends in a black
  `ShaftCap`; the rat holes are capped pockets; the window is fully covered by the derived 26 m
  patch), so nothing inside the cellar saw the dome.
  **With two millimetre-scale exceptions, stated because they were measured and not guessed:** the
  stair alcove is built from the UNSNAPPED hole while the wall is cut to the snapped one, which
  leaves a ≥24 mm slot down the doorway's south jamb and a 50 mm strip beside the bottom step. Those
  slivers — about 0.2° × 2° each, at the dark south jamb of an unlit doorway — showed the dome
  before this build and show the black `[Rig] VoidColor` clear after it. Black on black at that size
  is a non-event; **if he notices anything at all at that doorway, it is this and it is a pre-existing
  hole in the shell, not the sky change.** Filed with the arithmetic and a proposed fix in
  `NEEDED-OUTSIDE-cellar-one-sky.md` §3.
- **Zoomed far out, standing outside the model: the surround is now PURE BLACK** — the lit cellar
  box floating in the `[Rig] VoidColor` clear, with no stars around it. That is the second sky
  going away and it is stated rather than compensated: it is the only pose from which the dome was
  ever visible. If he wants stars back in that view, the answer is NOT this dome (it re-creates the
  two-sky report the instant he looks at the window) — it is a sky the ROOM branch owns, so it
  scales and yaws with the shell that hides it.
- **The forest is unchanged:** dome, cloud band, moon, shooting stars, all as before.

*The log token that proves it ran:* **`ENV SKY BRANCH`**, one `Note` per environment spawn.
- Cellar must read **`a 'StarDome' is ABSENT`**.
- SwampNight must read **`a 'StarDome' is PRESENT`**.

A cellar line reading `PRESENT` is the diagnosis, not a mystery: it means the **old bundle** is still
installed against the new plugin — the line says so in those words. That is the only way a bake-only
change can be read off a log at all, which is why the line exists.

*If it is wrong:* the failure to watch for is the window's own sky changing — it must not. The
curved patch is `NightSky`, built under `RoomGeo` (`BuildEnvironmentRooms.cs:4486`), so it rides the
board-anchored room branch and was never part of what was removed. If the view through the bars
changed, something took the wrong node and this commit is the suspect.
