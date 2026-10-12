# Take Your Place

Original cinematic concept for **GloomhavenVR**. Target: **70 seconds**, eight
scenes, 16:9, 1920×1080 master at 24 fps. English narration. The maintainer's
first fal.ai budget is **$30 total**. This is a revised concept for review;
**generate no imagery, video or narration before the maintainer approves it**.

## The film

The first twelve seconds look like a premium Gloomhaven render trailer. A low
camera travels through a beautiful, source-faithful dungeon or ruined chamber.
Torchlight grazes stone. Familiar original mercenaries stand at their actual
positions. We see the world at their height. There is no table edge, oversized
hand, mask, room backdrop or revealing card fan yet.

Then something interrupts the light. A vast shadow crosses the chamber. The
camera remains at mercenary height as **Grimhorn**, the actual horned carved-wood
mask shipped by the mod, leans into the open sightline above. Its enormous face
looks down with curiosity. The score withdraws; the figure and viewer share one
brief moment of uncertainty. No human eyes appear behind the eye openings. No new eye
glow or facial anatomy is invented.

A giant **leather-glove hand** descends. It gently picks up one idle original miniature.
The camera travels with the lifted figure, following a precise locally animated
path. Stone gives way to the board edge. Beyond it, we discover the physical
scale of the VR table and its surrounding mod environment: the towering face
is a player, the dungeon a diorama. Only now does the narrator say **“Take your
place.”** The hand returns the miniature to its original board cell before any
native action begins.

The reveal continues. Across the table, **Runeveil** enters the light. The second
player's actual arcane hands catch a cool cyan rim; Grimhorn answers with a
small friendly nod. Both masks are the mod's disembodied multiplayer presence,
each with two shipped hands. No bodies are added. What seemed solitary becomes
shared. We hold this connection long enough for it to feel intentional.

The hands turn to the real tactical controls: two cards per controlled character,
considered and committed to their original trays. The cut becomes more decisive.
Selection privacy is preserved; a local player's fronts are visible while remote
selection cards remain covered. Once the native action phase begins, cards open
as the game's rules allow. The score answers the physical card contacts.

Three short native action beats bring the diorama alive. Figures act from their
board positions; giant masks and resting hands remain visible above or behind
them. The hands made the choices. The original game executes those choices.
The camera widens into a modest orbit around the VR table, revealing both
players and the board as one coherent space. The film ends on **GLOOMHAVEN VR**,
**TAKE YOUR PLACE**, and the narrator's final line: **“Gloomhaven. In your hands.”**

## The two reveals

1. **World to embodiment, 12–30 seconds.** Preserve the original game's apparent
   scale for twelve seconds. A mask breaks that assumption; the hand and camera
   lift explain it. The table reveal pays off a visual question already raised.
2. **Solitary to together, 30–37 seconds.** Runeveil and the second pair of hands
   establish another player. The friendly nod makes multiplayer legible through
   behavior. Let this beat breathe before cutting to card decisions.

The mask and hand never become fantasy gods with gameplay powers. Their apparent
scale comes from looking up from an actual miniature's world. Calibrate the actual mask, hand,
figure and board relationship against the real captured/rendered VR table.
Do not invent a fixed 1:20 scale ratio.

## Original narration

**51 words**, approximately 30–36 seconds of speech with deliberate silent space.
Quiet authority, mature low-mid register, natural consonants, warmth and slight
roughness. Avoid shouting. **12–25 seconds are completely free of narration**:
the Grimhorn reveal and most of the lift rely on material sound and the image.
The opening does not name VR or the table.

> You know this world.
> The roads that brought you here.
> The dangers waiting in the dark.
> And the weight of every choice.
>
> Take your place.
> Gather your party.
>
> Choose your cards.
> Make your move.
> Together.
>
> Every decision.
> Every victory.
> Shared.
>
> The world you know.
> Closer than ever.
>
> Gloomhaven.
> In your hands.

The reference performer's voice uses the maintainer's explicit consent
attestation reported by the integrator on 2026-10-12. Retain a clean authorized
sample and an internal consent/source receipt. Verify the actual source speaker:
a game voice sample is not automatically the reference narrator. The script is
original. Generate one continuous performance where practical and edit pauses
locally to retain vocal identity. Audition source noise and diction before a
paid whole-script call.

## Scene plan

| Time | Scene and camera | Narration / sound |
|---|---|---|
| 00–12 | **The world you know.** Ground-level move through an actual open dungeon/ruin. Original figures and architecture; no tabletop hints. | First four lines. Sparse native ambience, bowed low strings, warm torch details. |
| 12–19 | **Something above.** Shadow and light change. Huge exact Grimhorn peers into the open sightline; camera stays low. Hold the mask reveal. | No VO. Music withdraws; low material resonance and one breath. |
| 19–30 | **The hand and the scale.** Actual leather-glove hand lifts an idle miniature; camera travels with it past the board edge to reveal the VR table. Return the figure before action. | “Take your place” only after the table discovery, about 26 seconds. Leather articulation and one restrained rise in the score. |
| 30–37 | **A second presence.** Exact Runeveil opposite Grimhorn, arcane hands, distinct amber/cyan rims, friendly response/nod. | “Gather your party.” A second tonal voice joins the score. |
| 37–45 | **The choice.** Two-card consideration and coordinated commitments to the native trays; original local/remote face rules. | “Choose your cards. Make your move. Together.” Two decisive contact beats, brief air before the action. |
| 45–56 | **The world answers.** Three native action beats with masks/hands visibly observing the diorama; no hand-driven combat. | “Every decision. Every victory. Shared.” Low impact, strings and restrained brass open. |
| 56–64 | **Together in VR.** Broad table reveal and modest orbit; both original masks and both pairs of hands remain clear. | “The world you know. Closer than ever.” Full musical resolution begins. |
| 64–70 | **Take your place.** Exact mod title and invitation, followed by a clean final hold. | “Gloomhaven. In your hands.” Resolve by 68 seconds; two seconds of room tail. |

## Source and motion conditions

- Use the actual **Grimhorn `Mask_2`** and **Runeveil `Mask_1`** meshes, UVs and
  textures, with the actual leather-glove and arcane hand rigs. Pairings are art
  direction, not enforced character classes. Ironwatch is an optional source
  alternate, not a third player in the core edit.
- The opening needs an original game room with an already-open overhead sightline.
  A closed cellar ceiling cannot become a broken roof for the reveal. Choose a
  source-backed open chamber, ruins, doorway or wall-edge camera line. Do not
  modify game references or remove architecture to fake the reveal.
- Game figures and scenario scenery still require extraction from a legitimate
  read-only source or native capture. **Existing previews do not demonstrate this
  new reveal.** A shipped surrounding-environment prefab does not establish that
  the opening dungeon or original figures are available.
- Locally rig and render the critical hand contact, miniature lift and camera
  travel. Source geometry owns all five fingers, the figure and the grip. AI video
  should not invent a multi-object pick-up or change the miniature during travel.
  Use input and output reference frames if the selected model supports them.
- The lifted figure is idle. Native movement never begins from a held transform.
  End the hold and return the figure to its original board cell before native action;
  the turn is driven by the original game after the card decision.
- Scenario selection exposes only local controlled-character card fronts. Remote
  fans, held and placed selection cards remain covered. Action-phase cards are
  face-up. Use separate source-faithful angles for coordinated commitments rather
  than an omniscient shot that leaks every selected card.
- Keep both tracked hands for both represented players. No human faces, torso,
  body silhouettes or extra arms. No player sword combat, hand-cast invented
  spells or hand-moved figures fighting freely. Remove the previous NPC montage;
  this concept is about the VR conversion and shared presence.

Native original card artwork, figure models and action effects remain locked.
Warm practical light against restrained cyan/violet accents draws on the verified
reference's visual rhythm. Avoid generic medieval replacements, orange fire
covering contacts, extra fingers, warped masks, generated runes, text or logos.
The low camera and later wide must show the same source world, with coherent
lighting and unchanged figure/mask scale.

## Clearly unofficial, every frame

Burn **UNOFFICIAL · FAN-MADE MOD** into the lower-right corner after the final edit,
including opening frames, transitions, black frames and the title hold. At 1080p,
use at least 24 px lettering, a 48 px safe margin and a subtle dark backing for
legibility. Keep it present for all 70 seconds.

The opening subtitle is **An unofficial community mod**: clear authorship without
spoiling the VR visual reveal. End with **Requires Gloomhaven Digital. Unofficial
fan-made mod.** and a modest community attribution, **A community project by
McFredward**. Composite all text and the existing mod logo locally. The trailer
should have premium production values while clearly representing an unofficial
community mod.

## $30 production envelope

The maintainer requested the full scene concept before actual generation.
Present this revision and the voiceover, then await creative approval. No paid
calls, images or videos were generated by this worker.

After approval, confirm original game figure/scenery extraction first. Build a
local full-sequence animatic and inspect full-resolution asset renders. Then
make one tightly scoped source-fidelity proof before further paid clips. The
first **13-second proof prioritizes the giant Grimhorn and lift/scale reveal**,
condensing the planned 12–30-second sequence. Use at most two source key plates:
miniature-height Grimhorn overhead, then the same figure/table after the lift.
Locally animate the exact hand/figure/camera contact path between them. The
proof must make three views coherent: the original world at figure height, the
colossal player presence above it, and the actual VR tabletop outside it. The
second Runeveil reveal is the next production phase; the first proof does not
claim to establish multiplayer or native action. Optional paid motion must
preserve the local contact animation rather than invent a pick-up.

| Stage | Maximum USD | Condition |
|---|---:|---|
| Local source extraction, renders, animatic and edit | 0.00 | Existing local tools; read-only game references. |
| Selected AI video, including first proof | 18.00 | Pilot maximum $4.50; at most three further selected calls. |
| Authorized cloned narration | 3.00 | Clean verified speaker sample; one whole-script call preferred. |
| Original music / sound generation | 3.00 | One reusable cue with local mix/edit. |
| Retakes / unavoidable paid processing | 6.00 | Log purpose and cost before use. |
| **Hard ceiling** | **30.00** | Queued reservations and failed jobs count. |

These are allocations, not asserted current provider prices. Verify the chosen
fal endpoint's price and billed units before each call. Log request ID, endpoint,
purpose, predicted/reserved maximum cost, actual cost, output artifact and review
result. Do not duplicate a still-running paid job after a network error; do not
assume cancellation means a refund. Source-locked local motion can complete
shots where paid generation changes identity. Present the first proof and ledger
to the maintainer before spending the remainder on similar generation.

## Evidence and final review

The integrator inspected 24 sampled frames of the reference at
`https://www.youtube.com/watch?v=N9vXBNJ5r1k`: 112.9 seconds, GOG launch CGI,
game-native dungeon/tavern/forest, ground-level sweeps, character portraits,
warm torch light against green/cyan/purple magic, rising action and a group
finish near 1:43. Those observations inform lighting and pacing, not copied
compositions or dialogue. This worker inspected `docs/img/styles-masks.png`,
`docs/img/styles-hands.png` and `docs/img/town-enchantress.png`; the new lift/reveal
has not been rendered or viewed.

Before delivery, watch the complete export with the watch skill. Inspect the
beginning, middle and end of each generated clip and the contacts at full
resolution. Check that the first twelve seconds preserve the apparent original
scale, the Grimhorn intrusion reads clearly, the lift maintains exact identity,
the second mask feels friendly, and the action stays native. Verify every-frame
unofficial attribution, title spelling, 70 seconds/1680 frames at 24 fps and the
actual mix (approximately -14 LUFS integrated, true peak no higher than -1 dBTP).
Listen once without looking at the image. Record what was fixed, cut or remains
for review; export checks alone do not establish visual quality.

Project contracts read: `AGENTS.md`, `CLAUDE.md`, `.planning/STATE.md` (Build672),
the newest notes beside `NetProtocol.ModBuild`, 1.1.0 release highlights,
`HeadMaskLibrary.cs`, mask localization entries and the hand-asset README.
