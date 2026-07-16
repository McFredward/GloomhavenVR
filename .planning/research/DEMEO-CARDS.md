# DEMEO-CARDS — how Demeo handles the card hand (research, 2026-07-16)

Purpose: hardware test #8 found our palm-gate reveal painful and the fan interactions
rough. Demeo (Resolution Games) is the genre reference the user compared against —
this note collects how Demeo's card UX actually works, from developer material and
reviews, and derives the UX targets for GloomhavenVR P6.

## Findings

1. **Reveal — palm-toward-face flip of the OFF hand, very forgiving.**
   "The card deck system is stylishly represented by a literal deck held in your left
   hand. It pops out when you turn your left palm toward your face" (CogConnected
   review, echoed by THE VR GRID: "to activate action cards, players turn one of
   their hands palm up and grab the card"). Meta's dev blog on the hand-tracking port:
   "when the inside of the hand faces the person playing, a floating deck of cards
   appears". It is a *casual* wrist supination — reviewers describe it as "with just a
   flip of the wrist" (no deliberate contortion), and the deck stays out while you
   browse. There is no permanent floating fan; the gesture *is* the toggle, but the
   accept cone is generous and the deck does not flicker away while you interact.

2. **Attachment — the fan is anchored to the off-hand and faces the player.**
   Cards fan out just above the palm/wrist, tilted toward the face, and follow the
   hand. The Meta blog: players "run their hand over their wrist where the cards are
   displayed" — i.e. the fan lives in hand space, browsing happens in place.

3. **Highlight/selection — proximity of the OTHER hand, not gaze.**
   Meta dev blog (hand-tracking implementation): "the game code performs a proximity
   check to evaluate where your hands might be in relation to the cards", with the
   Interaction SDK HandGrab poses attaching a card to the grabbing hand. Mixed-news:
   "the fingers of the second hand pull out a card". The hovered card pops
   forward/enlarges; a short haptic tick accompanies the highlight change (controller
   mode). With controllers the same pluck is done by pointing at the card and pulling
   the trigger/grip.

4. **Inspection — the plucked card is held in the dominant hand, enlarged and
   angled toward the face**, readable at arm's length. It stays attached to the hand
   until played or returned; letting go outside a valid target returns it to the deck.

5. **Play — drag & drop onto the board.**
   Meta blog: "select the item they want, and play the intended effect on the board by
   dropping it on the space of the board they want to affect" (same wording in
   CogConnected: "use any ability in your deck by grabbing the card and dropping it
   onto the map"). Releasing over an invalid spot cancels and the card flies back.

## What we adopt (mapped to Gloomhaven's rules)

| Demeo | GloomhavenVR P6 |
|---|---|
| Off-hand palm-flip reveal, generous cone, no contortion | `[Cards] RevealMode = tilt` (default): palm gate on the **device** pose (grip-pitch offset removed from the math), enter dot 0.35 (~70° cone), wide hysteresis. `RevealMode = always` keeps the fan out for the whole selection phase for players who want zero gesture. |
| Fan anchored above off-hand palm, tilted to face | unchanged (CardFan already does this) |
| Other-hand proximity pluck | ProximityGrabber kept; plus **laser hover + trigger-pluck** from the dominant hand (controller equivalent of Demeo's finger pull) |
| Held card enlarged, angled to face, never occluded by the hand | held pose re-anchored per frame: above/in front of the holding hand, facing the HMD, right-side-up |
| Drop on target to play, drop elsewhere returns to deck | drop on PlayTray slot = select (Gloomhaven picks 2 cards instead of playing directly); release elsewhere = animated return to fan |

Gloomhaven difference: cards are not played directly onto the board — the round flow
is *select two → tray*, so the tray replaces Demeo's board-drop as the valid target.

## Sources

- Meta dev blog — "Hand Tracking + Mixed Reality for Richer Immersion: A New Twist on
  Demeo's Adventures": https://www.meta.com/blog/demeo-hand-tracking-mixed-reality-mr/
- THE VR GRID — Demeo review: https://www.thevrgrid.com/demeo/
- Mixed-news — "Meta Quest hand-tracking makes Demeo tangible":
  https://mixed-news.com/en/meta-quest-hand-tracking-makes-demeo-tangible/
- Mixed-news — "Tabletop VR role-playing game hit Demeo will get hand tracking":
  https://mixed-news.com/en/demeo-tabletop-vr-role-playing-game-gets-hand-tracking/
- CogConnected — "Demeo Review – Take a Roll on This":
  https://cogconnected.com/review/demeo-review/
- SameTeem community review (wrist-flip description):
  https://sameteem.com/threads/demeo-review.21734/
