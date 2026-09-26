# Repeated resident entry, poses and cloth recovery — ModBuild 566

The supplied Debug log identifies ModBuild 565. It records fifteen immersive town-service
openings, including repeated temple visits. No `Immersive town native window show/hide sound
suppressed` record appears. The departure path instead reaches the real World Map
`ExtendedToggle` through synthesized pointer events; the log names its
`PlaySound_UIMapOpen` mouse-down item before `UIWindow.Hide` can run. The same log measures the
first temple presentation at 373.13 ms of a 400.33 ms frame and a later visit at 331.30 ms of
a 355.98 ms frame, with comparable presentation stalls on the intervening temple sessions.

## Changes

- Automatic resident departure still runs the game's complete map-mode toggle listeners, but
  uses the existing native no-pointer selection route. Physical map-cap presses and their sounds
  are unchanged. Flat town windows retain the 1.0.6 presentation and audio path.
- `UITempleWindow.EnterTemple` enters native party selection mode and can select the first assigned
  slot when another service cleared the selection. The approach path now retains the exact
  `NewPartyCharacterUI` object and restores that native slot after the synchronous mode change.
- The primary visitor already stands at the permanent resident station. It no longer constructs
  an invisible duplicate furniture hierarchy and two new Cloth solvers. Additional multiplayer
  ordinals still receive full visible workspaces and cloth; temple workspaces are reused between
  visits and disposed on map exit or when immersive services are disabled.
- Purse visibility is independent of transaction eligibility. A nearby owner always sees the
  physical purse and its original price/status inscriptions. Affordability, an earlier donation,
  native modal state and visitor ownership continue to gate grabbing, dropping and the bowl guide.
- Merchant and priestess attention targets are calibrated against the actual imported skins.
  Both residents lower their arms to their hips. The unavailable priestess moves both hands to
  the real bowl rim and uses the replicated fading availability blend when returning, so a changed
  native availability boolean cannot remove the pose in one frame.
- The cloth solver works in a unit-scale hidden mesh while the map uses about 198 world units per
  perceived metre. Its gravity is now converted into that scale, movement is limited to 11 cm,
  and overlapping table supports close the gaps that previously allowed a fingertip to invert the
  runner through the furniture. Four neighboring driver points deform each visible vertex instead
  of a single nearest point. Approach and contact use separate bounds: the solver can prepare while
  invisible, then a real palm/tip contact fades in a new episode-relative deformation. Departure
  fades continuously to the authored drape and pins the existing solver; no Cloth component is
  destroyed or rebuilt.
- The priestess candles were checked against complete rendered bounds and moved clear of both
  lanterns, the book and the donation bowl.

No wire record or configuration key changed. The existing resident activity and cloth streams
carry the resulting owner-authored state.

## Validation

- Native town audio: 27 runtime assertions; two focused mutation controls reject the former
  audible automatic-exit route.
- Temple ritual: 200 runtime assertions and 20 mutation variants, including exact-slot restore,
  repeated donation, ownership changes and modal timing.
- Workspace: 281,166 assertions. The measured primary path takes 0.104 ms on first entry and
  0.522 ms for 1,000 repeats, creates no Cloth owner, and still constructs one visible cloth
  workspace for a non-primary multiplayer ordinal.
- Imported activity: 627,580 assertions. Actual merchant and priestess transition renders were
  inspected; the hip poses, bowl cover and return sequence are visibly continuous.
- Actual-bundle cloth: three runners, 15 mutation controls, 0 table drop, monotone visible return,
  0 visible re-entry pop, 10.86–19.56 world units of new contact response and bounded long-session
  displacement. The solver remains at 120 Hz with one vertex snapshot per 90 Hz render.
- Decoration: 134 assertions and 15 mutation controls over complete prop bounds.
- Integrated refactor guard: all 14 source suites and all 79 local runtime/Unity suites pass;
  wire validation completes 286,569 assertions. Its final non-zero status is the expected
  compiled-form report against the older `080c505e9` feature baseline, with no removal from the
  guarded configuration, Harmony-patch or log-token surfaces.
- Town-service interaction: 1,300 production assertions and all 51 mutation controls pass. The
  fixture now models map teardown when retiring the intentionally cached temple workspace.
- Strict Release compilation reports zero warnings and zero errors.

These checks establish call ownership, construction cost, native slot identity, actual imported
skin geometry and bounded solver behavior. Headset verification still decides the perceived cloth
weight, the final stereo silhouettes and complete absence of the obsolete flat open/close cue.

## Hardware checklist

1. Enter and leave each resident repeatedly and after visiting another resident. No flat window
   open/close cue should play, and ordinary voice, coin, spell and cabinet sounds should remain.
2. Approach the priestess while a non-first owned character is selected. The selection must not
   change and entry must not hitch. Repeat after merchant and enchantress visits.
3. Verify the purse remains in the free hand when donation is unaffordable or already spent; the
   book explains the state, the guide stays absent and the purse cannot submit another donation.
4. Observe merchant and priestess approach/departure from front and side. Hands should settle at
   the hips, unavailable priestess hands should cover the bowl, and every transition should blend.
5. Push each table runner strongly with fingertips, including repeatedly toward the furniture.
   It should stay on the table, react physically, return promptly, and never reveal an old fold
   when a hand approaches again.
6. Check the priestess stand from both sides: no candle may intersect a lantern, book or bowl.
