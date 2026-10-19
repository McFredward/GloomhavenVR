# Genuine VR gameplay replacement

## UI-only inventory and stronger attack (v16)

The maintainer explicitly keeps the cinematic card near 1:30 (FG12) and requests
an inventory limited to visible game UI. A fresh actual-v15 picture review finds
33 UI inventory phases: 21 already in VR and twelve still native. These are
inventory phases rather than independent cuts; FG14–16 and FG36–38 each share
one continuous composition. The current work list has eight action groups:

| Trailer | Native UI still visible | Material decision |
| --- | --- | --- |
| 1:22.75–1:25.20 | Trap action and targeting | Review existing environmental actions; capture a missing specific action if needed. |
| 2:04.53–2:05.90 | Equipment assignment | New footage showing actual equipping, rather than another purchase. |
| 2:05.90–2:07.83 | Enhancement purchase | New footage showing a confirmed enhancement. |
| 2:07.83–2:10.13 | Quest completion / Claim Rewards | Review existing rewards footage first; scenario Victory alone is insufficient. |
| 2:41.82–2:43.12 | Victory banner over the map | Actual VR Victory footage already exists. |
| 3:04.52–3:09.32 | Guildmaster mission markers | Review existing map footage for real mission UI; a decorative map alone is insufficient. |
| 3:19.20–3:23.15 | Card selection, system mosaic and turn transition | Existing fans, actions and menu footage cover many elements; build the moving composition while preserving cinematic tiles. |
| 3:28.70–3:37.20 | Cooperative player panels | Record at least two genuine synchronized POVs; compose according to the real player count. |

HUDless campaign combat over a painted map does not require replacement.
The actual Victory UI starts at frame 9709, three frames before the former FG30
boundary, and ends before 9787; only `[9709,9787)` belongs to that remaining UI
window. Guildmaster's first mission-marker edge appears at 11071, so its UI phase
is `[11071,11359)`. The purely illustrated lead-in and subsequent bare map are
excluded. Branding and film titles are not game UI. The approved FG12 card is
explicitly excluded from outstanding work and requests for new footage.

The private `vr-gameplay-v7/inventory/INVENTAR-UI.html` provides a player, times,
actual pictures and separate existing-material/new-capture decisions. Its
analysis authority remains the reviewed v15, with a link to the current v16
review. The reviewer personally inspects 239 Watch cues and 112 exact boundary
pictures. Root independently views all twelve remaining-UI representatives,
the approved card, five campaign/context sheets and two exact boundary sheets.

For “spot some weaknesses to exploit”, v16 changes only FG07 `[4359,4534)`.
The real `2026-10-17 11-28-25.mp4` source frames `[12000,12088)` show Cragheart's
Avalanche hitting two Living Bones for five damage each, both collapsing and
leaving gold. The 175 output frames repeat native 30fps at exactly 1x; the last
source frame is used once. There is no speed fitting, interpolation or held
ending. The native hard entry and following FG08 flare are inherited. Every
other v15 frame address, original AAC and the separate approved intro remain.

All 88 source pictures are reviewed; an independent sequential-PTS comparison
passes all 175 component frames, at maximum 320-by-180 MAE 0.60440. A deliberately
wrong next-source-frame clock has mean MAE 2.90216 against 0.45797 for the correct
mapping on moving frames. Root views sixteen exact encoded-component witnesses
and three full-size source damage/result pictures. Source and clock evidence
live under `vr-gameplay-v7/attack-search/`. No paid inference or runtime change
is used. The previous v15 section remains historical evidence.

V16 SHA256 is
`3c38629aecd0acf4ad7c335c20d54efe6097a771882c7f4bc50836874ab44234`.
The assembler passes all 13 checks. Independent sequential validation passes
all 14,810 frames, 14,635 outside-window pictures against v15, all 175 new
component pictures, the continuous fan mark and all 11,573 unchanged AAC packets
including clocks/configuration. Maximum 320-by-180 MAE is 0.68808 outside and
1.38360 inside, below the unchanged 2.0 threshold. The source-clock proof above
is explicitly inherited from the independent attack reviewer, not rerun.
Root additionally views fifteen actual-export Watch cues and seven exact export
boundary/result/card witnesses. This is focused media evidence, with unchanged
v15 creative evidence inherited; no game-runtime or headset gate is claimed.

Desktop Chromium and mobile emulation each play only the new attack context at
1x. Thirty-eight paused chapter checks, twelve time/state-preserving comparisons
and eight UI-inventory scene jumps pass. The reviewer personally views eighteen
media pictures and the upper player/layout regions of four page captures;
unseen lower page regions are not claimed as visually reviewed. The approved
1:30 card remains visible. JavaScript/media checks pass; request cancellations
are the expected aborted seeks/source switches. Final authorities are
`vr-gameplay-v7/{qc-independent/technical-final.json,browser-review/final-review.json}`.

Postpublication navigation passes 72 HTTP/Range requests and 38 paused chapter
checks, reaches v16/CURRENT/the UI inventory from the homepage and confirms all
eight action groups. No playback run is added. The separate intro section is
byte-identical. Both completed clean worker worktrees are removed and the
canonical cleanup API has run after process reaping. All 38 protected media
hashes still match; used recordings, comparison references, selected components,
masters and review evidence remain. See
`vr-gameplay-v7/{browser-review/final-navigation.json,editor/protected-media-final.json}`.

## October 19 UI-focused capture pass (v15)

The maintainer permits equivalent broad actions with different classes, cards
and rewards, and explicitly prioritizes visibly Flat UI. HUDless gameplay is
acceptable for now. The new `2026-10-19 19-33-36.mp4` recording supplies seven
further windows. No inference or runtime implementation is involved.

| Trailer | Window at 60fps | Actual VR demonstration |
| --- | --- | --- |
| 1:02.93 | `[3776,4088)` | Mindthief takes lethal hits, collapses/disappears, then shows the native exhausted tooltip. |
| 1:12.65 | `[4359,4534)` | Mindthief executes Fearsome Blade and deals a melee hit. |
| 1:19.65 | `[4779,4965)` | A Bandit Archer hits Cragheart; Receive Damage 1, HP 10 to 9 and a red pulse follow. |
| 1:25.20 | `[5112,5304)` | Push targets and confirmation move a guard into a floor trap; the trap vanishes with an effect. |
| 1:32.48 | `[5549,5690)` | Crater is selected to avoid damage, the player confirms, and its loss animation starts. |
| 1:34.83 | `[5690,6067)` | Rock Tunnel executes and its played card burns; the gold wipe leads into physical Minor Healing Potion use with HEAL3. |
| 1:46.88 | `[6413,6617)` | Chest loot resolves with the native item-reward message. |

The short Crater excerpt does not show completed disappearance or a burned-counter
increment. That damage sacrifice is distinct from the next excerpt's played-loss
ability. The push uses a mechanical trap rather than the original pull into fire;
no trap-damage amount is claimed. The chest awards Resonant Crystal instead of
the original gold; its upper card artwork is partly outside the recorded view.
These differences are named in the review. FG07's old request for an entire
push/trap/attack sequence is corrected to the native window's visible melee hit.
FG11 separately demonstrates repositioning into a trap.

Source motion stays at exact 1x, with ordinary 30fps repeats in the 60fps trailer.
The two gold wipes follow measured native angle, width and frame clock. FG04
retains the accepted outgoing cinematic outside the advancing wipe. The combined
FG14–FG16 wipe composites two genuine VR sources, without old native actors/UI.
FG09's first/last twelve frames use optical horizontal smear on corresponding
live VR pictures and adjacent row palettes. No source-time fitting, synthesized
gameplay, interpolation or held ending is used.

Nine further inventory rows contain VR pictures: 21 covered, 20 still native.
Nineteen review chapters include the combined three-row loss/item sequence.
The remaining capture list has nine UI-focused groups; additional HUDless combat
captures and exact original-class variants are not required in this pass.
The current private review and selection live under `vr-gameplay-v6/`.

V15 SHA256 is
`a9423dc88ed5dc2175f00d6d18b53d050e446c5c0142b8fe59aa306dcaa287ed`.
The assembler passes 13/13 media checks. Independent sequential validation
checks all 14,810 frames, 13,223 outside-window pictures against v14, the fan
mark on every frame and all 11,573 unchanged AAC packet payloads/clocks/config.
Outside-picture maximum 320-by-180 MAE is 0.79283. All 1,587 new component
frames pass at maximum MAE 1.42408, below the unchanged 2.0 threshold.

This is composed evidence: the initial full run passed outside pictures, audio,
footer and five components, but twelve saturated optical-edge frames in FG04/09
failed a direct color-path comparison. A focused check of all 498 FG04/09 frames
uses their exact assembler RGBA/overlay/color pipeline; matrix controls confirm
the reference mismatch. It passes without changing the master, components or
threshold. The original failed report remains. The final authority is
`vr-gameplay-v6/qc-independent/technical-final.json`; this is not a second full
gate or a claim of pixel identity.

An exact source-PTS check passes 1,504 untreated component frames across eight
spans, including both compound-shot source clocks. Maximum MAE is 1.24333;
adjacent-source-frame negative controls are consistently worse. Only 83 optical
transition frames are excluded from that direct source-picture check; every
new frame remains covered by final component validation. Root personally views
99 exact component witnesses, 56 actual-export Watch cues and 33 exactly decoded
export boundary/result pictures. Evidence lives under
`vr-gameplay-v6/{source-analysis/1019-193336,qc-root,qc-independent}/`.
These are focused media checks; unchanged v14 creative evidence is inherited,
and no game-runtime or headset gate is claimed.

Desktop Chromium and mobile emulation each play all seven new contexts at 1x;
38 paused chapter checks and 12 time/state-preserving comparisons pass.
The reviewer personally inspects 62 player pictures, two final page headers and
two corrected loss descriptions. Both headless viewports report no dropped
frames; no JavaScript/media errors occur. The final wording correction is
checked without replaying unchanged media. See
`vr-gameplay-v6/browser-review/final-review.json`. Root additionally views the
desktop/mobile exhaustion player witnesses. Full-trailer branding remains
continuous; the separate approved game intro retains its explicit exception.

Postpublication navigation reaches v15 from the homepage and checks all
nineteen chapters in both viewports without adding playback runs. All 71
HTTP/Range requests pass. The separate intro section remains byte-identical.
See `vr-gameplay-v6/browser-review/final-navigation.json`.

Both completed clean worker worktrees and stopped owned bytecode caches are
removed. All 36 protected media hashes still match, including the five supplied
recordings, accepted sources/components, separate intro, new master and two
normalized comparison references. Logs, manifests and review evidence remain.
See `vr-gameplay-v6/editor/protected-media-final.json`.

## Fourth October 17 capture (v14)

The new `2026-10-17 11-28-25.mp4` recording was compared with the actual native
pictures and verified English narration. Only two further regions match well
enough to replace: active gold collection at `[6213,6413)` and successful scenario
completion at `[7090,7234)`. The source recording remains retained and unchanged.
No paid inference or mod runtime change is used.

The first excerpt actually activates Cragheart's Unstable Upheaval bottom Loot1,
shows the highlighted area, moving coins and `+4 Gold`. The original shows
Scoundrel/Throwing Knives Loot2. Class, radius and amount differ, but both show
active combat looting; the generic narration does not name Loot2. This is not
passive end-turn gold. The second excerpt shows the real Heart of Stone tutorial
Victory and Fraanc's closing dialog, replacing the original Victory/Trainer
sequence. No campaign quest completion or rewards are claimed.

Minor Healing Potion is only inspected, so it does not replace Poison Dagger use.
Short-rest burning does not replace a played loss. Fire Orbs and Avalanche have
actual loss consequences, but their card-burn presentation is outside the view.
The damage prompts end in Receive Damage and do not show the original card-focused
sacrifice choice for the required duration. An outgoing Fire Orbs attack also
cannot replace FG09's incoming Shaman attack and Spellweaver HP damage. Those
native passages remain pending better captures.

Both excerpts retain exact source time: frames `[14640,14740)` and
`[16179,16251)` at 30fps repeat normally into the 60fps trailer. There is no
speed fitting, interpolation or held ending. Twelve opening Victory frames use
progressive Gaussian defocus and dimming on their corresponding actual VR
pictures to join the native defocused entry. No native scenery is mixed into
the replacement. All other accepted cinematics, gameplay, full-trailer fan mark,
original soundtrack and the separate approved campaign intro are inherited.

The private review is `vr-gameplay-v5/REVIEW.html`; its source comparisons name
the class/radius/tutorial differences. Twelve inventory regions now contain VR
pictures, with 29 still native. FG33/FG34 remain one continuous shot. The updated
`NAECHSTE-AUFNAHMEN.txt` has 18 grouped requests; closer original-class variants
are optional rather than mandatory retakes of already matching demonstrations.

V14 SHA256 is
`ac93cd713684eb5ce55fe2cf3bfd71cb2884238fb072666d5963dc89a1541403`.
The assembler passes 13/13 media checks. Independent sequential QC passes all
14,810 frames, 14,466 outside-window comparisons against v13, all 344 component
frames, the continuous fan mark and all 11,573 unchanged AAC packet payloads,
clocks and codec configuration. Maximum 320-by-180 MAE is 0.7720 outside and
1.1916 inside, below the unchanged 2.0 limit. This is lossy fidelity, not pixel
identity. A separate exact-PTS source check passes all 200 Loot frames and
132 untreated Victory frames at ordinary 1x; adjacent-source-frame negative
controls have substantially larger error. The 12 optically treated entry frames
are excluded only from this source-picture check, not final component QC.
Root viewed 41 exact component cues, 33 actual-export Watch cues and eight
exactly decoded final boundary pictures. Evidence lives under
`vr-gameplay-v5/{qc-independent,qc-root,source-analysis/112825}/`.

Desktop Chromium and mobile emulation each play the two new contexts at 1x;
24 paused chapter-navigation checks and 12 time/state-preserving comparisons
pass. The reviewer personally views all 24 player pictures and two page-header
crops. No JavaScript/media errors occur. Headless playback reports 0/443 dropped
frames on desktop and 2/441 in mobile emulation; encoded media has no missing
frames. The first browser attempt used an unbound localhost address and failed
before opening Chromium; the retained retry uses the actual LAN binding.
Unchanged v13 creative/playback evidence is inherited. These are focused media
and browser checks, not a new game-runtime or headset gate.

Postpublication desktop/mobile navigation reaches v14 and all twelve chapters;
55 HTTP/Range requests pass. The separate intro section/link remains byte-identical.
See `vr-gameplay-v5/browser-review/final-navigation.json`.

The completed worker worktree and owned bytecode caches are removed. All 25
protected media authorities retain their hashes, including the four supplied
October 17 captures and separate approved intro. See
`vr-gameplay-v5/editor/protected-media-final.json`.

## Squad-formation clarification (v13)

The maintainer clarified on October 17 that this passage demonstrates assembling
a team; the original Cragheart identity is not required. FG34 at
`[11801,11861)` therefore uses the next 30 actual source frames after the existing
Scoundrel selection. The class is confirmed and the name form opens. Neither
completed naming nor completed mercenary creation is claimed in this excerpt.

Source `2026-10-17 09-44-07.mp4` frames `[4139,4169)` repeat normally at 60fps.
The preceding selection ends at source frame 4138, so the new second continues
at exact 1x without a camera cut or held ending. It replaces the remaining Flat
class view and leaves the original timing and soundtrack intact. The explicit
class-identity relaxation applies to this squad-formation passage; required
combat mechanics and outcomes elsewhere still need equivalent actual footage.

Current review and capture requests are `vr-gameplay-v4/REVIEW.html` and
`vr-gameplay-v4/NAECHSTE-AUFNAHMEN.txt`. The Cragheart-specific request is removed.
Ten inventory regions now have VR pictures; FG33 and FG34 form one continuous
Scoundrel shot. The v12 delivery evidence below remains the inherited baseline.

V13 SHA256 is
`ea621fa53396e68b1fce34db382dfcc94cfff58b41157c8d519f73b065f264e2`.
The assembler passes all 13 media checks. Independent sequential validation
passes all 14,810 frames, 14,750 outside-window pictures against v12, all 60
new component frames, the continuous fan mark and all 11,573 unchanged AAC
packets including clocks/configuration. Maximum 320-by-180 MAE is 0.8881 outside
and 0.6772 inside, below the unchanged 2.0 limit. This measures lossy fidelity,
not pixel identity. Exact 11800/11801 and 11860/11861 boundary witnesses pass.
Root viewed all 30 distinct component frames, five component Watch cues and
16 actual-export cues on two sheets. Desktop and mobile emulation each play
only the changed passage at 1x; 20 chapter navigations and 12 time/play-state
comparisons pass. Unchanged v12 creative/playback evidence is inherited; no
new complete game-runtime or hardware gate is claimed. Receipts live in
`vr-gameplay-v4/qc-independent/` and `vr-gameplay-v4/browser-review/`.

Postpublication desktop/mobile navigation reaches v13, all ten chapters and the
correct source/caption at 3:17; all 50 HTTP/Range requests pass. The separate
intro section and link remain byte-identical. The owned worker worktree and
stopped caches are removed; all 21 protected media hashes match after cleanup.
No new paid inference was used. See `vr-gameplay-v4/browser-review/final-navigation.json`
and `vr-gameplay-v4/editor/protected-media-final.json`.

## October 17 recording pass (v12)

The three new recordings were inspected against actual original pictures and the
verified English narration. Six further regions are selected: outward ranged
attack and its target/result, an enemy attack in the forest, immersive merchant
selection, Tinkerer and Scoundrel class views, and a confirmed item purchase.
The existing ability overview now uses Spellweaver, matching the original class.
The two retained item-inspection and triangular-area-hit shots remain inherited.
No new inference is used. Unmatched original gameplay remains pending captures.

The selection distinguishes actual actions from nearby contexts. The merchant
selection is not labeled a completed purchase; the later purchase really closes
the Buy dialog and reduces gold. Neither establishes equipping. The forest
hound attack triggers Frost Armor but does not establish HP loss, so the next
native damage-choice shot remains. Campaign class views show the same classes
as the original Guildmaster roster; the review labels that mode difference.
The cropped Cragheart view is rejected. Scoundrel planning, short-rest burns and
automatic end-turn gold are not substituted for attacks, played loss or Loot2.

A previous analysis error is corrected from the actual original at 1:34:
FG13 offers "Burn The Mind's Weakness" to avoid damage. It requires a confirmed
damage-avoidance burn, distinct from both Short Rest and the subsequent played
Shield Bash/loss sequence. Historical v10/v11 receipts below are retained as
historical evidence; their old capture request for this slot is superseded.

All selected source motion remains at 1x. Ordinary 30fps frames repeat into the
60fps timeline; no speed fitting, interpolation or held ending is used. The
forest shot joins the existing light/whip transition using procedural light and
horizontal blur on actual VR pictures. The merchant continues behind the native
diagonal gold wipe with a spatial matte; native incoming inventory remains.
This is ordinary transition compositing, not generated gameplay.

The current private review and corrected capture list are
`vr-gameplay-v3/REVIEW.html` and `vr-gameplay-v3/NAECHSTE-AUFNAHMEN.txt`.
The complete trailer keeps its fan-mod mark and accepted AI cinematics. The
separate, previously approved campaign intro is unchanged. Original October 17
recordings remain retained and hash-pinned; the two historical October 15 source
recordings are currently absent from the input folder, so inherited-shot links
use their retained components rather than broken original capture links.

The v12 master SHA256 is
`f942650d71948b419c95605cef81e062336f3835aece8073ac315eee12009b76`.
Seven updated windows contain 988 frames; nine inventory regions now contain
VR pictures including two inherited shots. The merchant window includes its
spatially mixed wipe handles, not 260 wholly VR pictures. Thirty-two inventory
regions remain original. Coverage counts are not a reason to weaken matching.

The assembler passes all 13 media checks. Independent composed validation
covers all 14,810 frames, the fan mark on every frame, 13,822 pictures outside
the new windows against v11, and all 11,573 identical AAC packets including
clocks/configuration. Maximum 320-by-180 outside-picture MAE is 0.7882 on an
8-bit scale; all 988 component/reference comparisons remain below 2.0
(maximum 1.6714). These are lossy picture-fidelity comparisons, not pixel identity.

The first comparison incorrectly interpreted the untagged RGB-pipe component
and BT.709 master alike, exceeding the limit on five saturated FG08 flare
frames. That failed attempt is preserved. Exact RGBA/overlay/BT.709 normalization
and a reverse-matrix negative control establish the cause. A focused comparison
of all 245 FG08 frames then passes at maximum MAE 0.6533; the original outside,
glyph and AAC evidence is reused. This is explicitly composed evidence, not
a second complete run. Master and selected components are unchanged. See
`vr-gameplay-v3/qc-independent/technical-composed.json`.

Root personally viewed 101 actual-export Watch cues on seven sheets and eleven
additional exactly decoded witnesses, including the 4087/4088 and 4249/4250 cuts
and visible DAMAGE3. Time-addressed Watch cues can select a neighboring frame;
exact boundary claims come from sequential frame decoding. Manual review is
sampled; no every-frame manual whole-film or headset-runtime claim is made.

Desktop Chromium and mobile emulation each play all nine windows at 1x and
both soft-transition contexts. Twelve ordinary source comparisons preserve time,
rate and pause state; 36 rapid comparison-button changes pass without JavaScript
or media errors. A source-switch race found on the first mobile attempt is fixed
with cancellation and generation guards; that failed attempt is preserved and
the changed controls were actually retested. All 13 unique dependencies are
reachable. The browser reviewer personally viewed 30 actual screenshots;
root additionally viewed desktop/mobile player witnesses. These are browser and
media checks, not new game-runtime or headset evidence. See
`vr-gameplay-v3/browser-review/final-review.json`.

Postpublication desktop/mobile navigation reaches v12 and its nine chapters;
all 27 unique homepage/review dependencies pass HTTP/Range checks. The separate
intro section remains byte-identical. Owned rejected videos, duplicate raw
attempts and stopped caches were removed; both worker worktrees were removed.
All 19 protected media authorities, including all three new recordings and the
separate approved intro, retain their expected SHA256 values after cleanup.
Final receipts: `browser-review/final-navigation.json` and
`editor/protected-media-final.json`, relative to `vr-gameplay-v3/`.

## Historical v11 material trial

The maintainer rejected v10 after the first review: the actual demonstrated
action and its transition matter more than replacing many regions. More
recordings will follow. Treat the available footage as a first material trial,
not an almost-complete gameplay trailer.

A fresh comparison of the original pictures, English captions and actual VR
shots withdraws seventeen replacements. V11 retains only three demonstrations
on native hard cuts: an ability-card overview, item inspection and an executed
triangular area attack. Their card/class identities differ where the narration
is generic; their demonstrated actions match. The 350 frames retain source
time. All other pictures and transitions come from accepted cinematic v9.

The removed clips included important semantic reversals: placing two cards
instead of the original action/target selection, healing instead of enemy
attacks, a Spellweaver attack instead of damage received, trap inspection
instead of disarm, short-rest/damage burns instead of a played loss, item
preview instead of use, browsing instead of purchase, and one cursor instead
of multiple cooperative viewpoints. Passive end-of-turn gold collection is
also deferred until an actual Loot2 action is recorded. The actual victory
is semantically suitable, but its sharp entry interrupts the native blur and
its short view is partly obscured by the control board; it remains original.
The sparse Guildmaster map and free nine-tile montage are also withdrawn.

The active private review and exact next capture list are
`vr-gameplay-v2/REVIEW.html` and `vr-gameplay-v2/NAECHSTE-AUFNAHMEN.txt`.
The v11 master SHA256 is
`6f4007b7db11ebffde14ea92e1585d0bc8f6bb43f48fb6e8d6ba3b21a1d127ee`.
Its actual area-attack excerpt shows impacts/resolution; it does not contain
complete target selection beforehand. The final review says this explicitly
and requests a closer Ink Bomb take with that lead-in.

The bounded technical check compares all 350 selected component frames and
14,460 inherited pictures, checks the fan-mod mark on all 14,810 frames and
verifies all 11,573 unchanged AAC packets including clocks/configuration.
Root viewed 48 actual export cues; separate focused review viewed 31 cues and
12 exact original/final cut pairs. Desktop/mobile Chromium tests cover the
three short shots at 1x and time-preserving v9 comparison. This is validation
of a provisional material trial, not complete creative approval of the trailer
or a new game runtime/headset gate. Receipts live under
`vr-gameplay-v2/audit-early/final/` and `vr-gameplay-v2/audit-late/final-browser/`.
The final `final-browser/final-review.json` also verifies the published homepage
links, final captions and twelve dependencies. Both owned review worktrees were
removed after their jobs finished; input footage and review evidence remain.

The original-v9 comparison is the reference. Old passing technical receipts
establish their measured media properties, not the suitability of rejected
creative substitutions. The v10 delivery account below is historical.

## Historical v10 account

The maintainer accepted the AI cinematics and requested an incremental exchange
of the remaining Flat gameplay in the official Gameplay Overview timeline.
The immutable starting point is the accepted cinematic v9 master, SHA256
`0b332c1e1860a2395154e6d99f938cc8dc1b54177cadd2a2466ed6e585b8ef9b`.
The English narration, existing GloomhavenVR name/logo patches and all eleven
accepted cinematic windows remain inherited. This pass uses no further inference.

## First selection

The inventory identifies 41 full-frame or mixed gameplay regions. Twenty chosen
inserts cover 25 regions with genuine VR images: 3,932 frames, or 65.533 seconds,
on the unchanged 14,810-frame/60fps clock. Sixteen inventory regions remain in
the previous picture pending suitable capture. These counts describe picture
coverage, not literal reproduction of every original character or mechanic.

Two user recordings and all three requested YouTube sources were inspected.
Local source A is `gameplay/2026-10-15 19-17-17.mp4` (720p30); B is
`gameplay/2026-10-15 20-06-57.mp4` (1080p30), relative to the private trailer
folder. The new gameplay inserts use only actual game recordings. Third-party gameplay
sources and actual review evidence remain in `vr-gameplay-v1/youtube-sources/`:

- [Meta Quest 3 gameplay](https://www.youtube.com/watch?v=w0mlw50PqZc)
- [Cards and tabletop gameplay](https://www.youtube.com/watch?v=ucXGnLKwOXs)
- [PCVR Gamer gameplay](https://www.youtube.com/watch?v=tgJAk6Z62b4)

The selected excerpts run at original source time. Ordinary 30fps captures
repeat at 60fps delivery; there is no motion interpolation, speed fitting,
slow motion or held ending. A dedicated nine-feature montage uses spatial
camera zoom only: 25 frames out from a card fan, 120 frames of a 3-by-3 grid,
then 92 frames into a real spell hit. It replaces the original montage block
`[11952,12189)` without claiming that its tiles are independent players.
The cooperative passage `[12522,13032)` uses one real VR viewpoint and its
native remote desktop-player cursor. It does not simulate four VR recordings.

## Matching limits and next captures

The review labels the actual replacement content. Some characters, locations
and mechanics differ while supporting the same narration. Damage choices do
not establish exhaustion; short-rest and damage-avoidance burns are distinct
from played loss actions. Trap/coin inspection does not establish a push,
pull or disarm. The Heater Shield excerpt shows a USE preview, and equipment
or merchant browsing does not establish a completed purchase.

The remaining original regions require push/pull into hazards, the Cataclysm
card-over-film composition, chest loot, enhancement, quest completion,
class/roster selection and the campaign map composition. The clean local
regional map is verified Guildmaster footage; it must not substitute for the
campaign map. The long campaign-table take has an obstructing gold symbol,
and the short unobstructed views cannot supply the whole 20-second sequence.
Additional synchronized VR viewpoints would restore the original cooperative
panel layout. A real completed item use, purchase/equip action, exhaustion and
played loss would also improve the current contextual substitutes.

The German inventory, selected-source links and concrete next capture list live
under `.planning/debug/trailer/remake-v1/vr-gameplay-v1/`. Incoming recordings
and the five requested source videos remain inputs for subsequent passes.
Unused preliminary derivatives may be removed after final review; no incoming
recording or shared game reference is eligible for that cleanup.

## Delivery evidence

The first unpublished export exposed a one-frame desktop leak at frame 4088
between adjacent inserts. The assembler compared floating timestamps with
rational window limits. It now rounds the normalized 60fps clock to its integer
frame before testing the window. A tiny real-FFmpeg regression exercises every
frame of all 20 windows, unchanged gaps and a still-graphic window: the old graph
fails exactly at 4088 and the corrected graph passes all 14,810 frames. This
fixture omits branding and uses 32-by-18 color plates; it establishes placement
and clock behavior rather than production image quality. The relevant expression
functions are documented in the [FFmpeg expression reference](https://ffmpeg.org/ffmpeg-utils.html#Expression-Evaluation).
The rejected export is not a delivery authority.

The corrected master is `full-timeline-v2/reference-remake-vr-v10.mp4`, SHA256
`213abdfaa03e7779870d4c9db3d8a88523d08ce7b8e73cdded79ce6d68ff24f3`,
relative to the private phase folder. It preserves the 246.861-second container
and original audio tail. The assembler passes all 13 technical checks.
Independent technical review decodes all 14,810 frames, checks the continuous
unofficial mark, compares all 10,878 outside-window pictures with v9 and all
3,932 changed pictures with their selected component frames. Maximum 8-bit
320-by-180 mean absolute errors are 0.7021 outside and 1.8869 inside, below the
2.0 limit; this allows ordinary export loss and does not claim identical pixels.
All 11,573 AAC packets, clocks and codec configuration match v9 exactly.

Root personally viewed 186 actual corrected-master Watch cues on 12 contact
sheets plus six full-size cues, supplementing the separate component reviews.
Manual film review is sampled; it is not an every-frame manual viewing of the
entire film. Independent Chromium review passes all 20 chapters and all changed
windows at 1x on desktop and mobile emulation, eight time-preserving comparison
switches and ten late seeks. All 156 HTTP dependencies pass; 28 input hashes
remain unchanged across playback. The browser reviewer personally inspected
56 actual screenshots. Root also viewed both overview layouts.

The private review is [VR gameplay review](http://192.168.0.80:8090/remake-v1/vr-gameplay-v1/REVIEW.html).
The bounded offline assembler regression establishes no new game runtime,
headset, phone hardware or subjective listening result.

The final visual reviewer personally inspected 358 changed/context samples,
156 Watch cues, 55 base/final pairs and four additional full-size witnesses.
The montage author identifies their own contribution; root and a separate
worker provide the independent montage component review. Final receipts are
`editor/root-final-approval.json`, `inventory/final-qc/technical.json`,
`inventory/final-qc/visual-creative-review.json` and
`youtube-sources/final-browser/run-5b7c26af/delivery-verification.json`, relative
to the phase folder.

Cleanup removed three unused preliminary MP4s, including the rejected first
export: 328,151,040 allocated file bytes. All 27 protected media authorities
were hashed before and after deletion and matched. The five incoming/requested
source videos, 20 used components, accepted base, current master, pinned files
and compact review evidence remain available. The deletion receipt is
`editor/unused-media-cleanup.json`. No runtime code or mod build changed;
the bounded offline tool regression and actual media/review-site checks are
the tested scope.

After cleanup, actual desktop/mobile navigation from the homepage reaches the
v2 master and its 20 chapters. All 19 direct homepage dependencies and the
156-item review dependency graph pass again, with six frozen authority hashes
unchanged. This bounded navigation check inherits the completed full playback
review; it does not repeat it. The receipt is
`youtube-sources/final-browser/run-ede63104/navigation-verification.json`.
