# NPC hardware follow-up — ModBuild 575

The supplied `Player.log` and `LogOutput.log` both identify ModBuild 574,
commit `ca2138385`. The four screenshots in the main checkout's
`.planning/debug/npc_probleme/` show merchant cards standing proud of the cabinet,
hands near the counter edge, a distorted priestess shoulder/arm silhouette, and
the enchantress's narrow cyan effect through an offered card. No video or remote
player log was supplied for this run. The Build 575 headset result remains open.

## Source-grounded findings and corrections

- The merchant's first two native Cancel clicks reached `UIWindow.Hide`, but
  `UIItemConfirmationBox` delivered its cancel callback only after the hide fade.
  Between those events the offer watchdog logged `retrying the retained offer
  (1/2)` and `(2/2)` in both logs. Capture the actual Cancel button click and
  start the existing card return flight immediately; the later native callback
  remains idempotent. Keep the original native transaction authority.
- The merchant catalog no longer treats an item parked in the resident's palm
  as an item still moving across the cabinet: category buttons and crank remain
  usable. Physical handoff tests cover stock-to-owned and owned-to-stock
  replacement. Accepted item drops give the releasing controller a pulse; a
  source-bound Debug event now distinguishes release, gesture, zone, eligibility
  and native-commit failures without normal-level per-action logging.
- The native stock cards, price strip and pickup collider move 15 mm inward
  together; the carved category controls stay at their authored seats. An
  out-of-stock card visibly carries a localized band on its original item art.
  Actual pickup of an unaffordable or sold-out card requests one of five new
  context-specific merchant lines. The elected face author chooses the exact
  cue, and the existing shared speech/mouth channel publishes it to observers.
- The narrow cyan enchantment effect is the game's separate `CardHilight/Aura`
  animation, not the previously corrected `GUI_LevelUp_Frame`. At the final
  canvas submission boundary its visible native ink is made square in world
  space without replacing the original animation or area buttons. The offered
  card still occludes windows behind it, but laser pickup is disabled everywhere
  on that card. Native area buttons accept a physical grip poke as well as the
  laser; an unpressed finger does not confirm them.
- Incidental priestess prayer and enchantress casting lines are now spaced at
  least 180 and 150 seconds apart, respectively. Visitor and transaction replies
  retain their immediate contextual timing.
- The priestess's attentive hand targets now remain within the imported arm's
  actual reach, and her elbow guides keep the upper arms beside the robe. The
  production skin was scanned over all 403 transition frames with zero arm/robe
  intersections; the neutral and prayer-transition renders were reviewed from
  frontal and oblique views. The merchant's free hands move above the counter
  toward the belly. Native cloth has a smaller 8 cm tether envelope, stronger
  bending and damping, and a brief 45 ms visual contact onset so touch is visible
  without the abrupt fine wrinkling reported on build 574.
- The newly recorded merchant replies pushed the previous single town bundle
  above GitHub's ordinary-blob limit. Keep the full source quality and ship two
  independently loaded town parts: art in `ghvr-town.bundle` (101,844,591 bytes,
  SHA-256 `2b8d13050e1c63ffdc2d05082676279ee96ee8fffa793a04f2b274caa0a46bda`)
  and voices/curves in `ghvr-town-voices.bundle` (3,298,342 bytes, SHA-256
  `4e44667adc9c77c94066aaf8ab4fe2ef19eb7a3f56cc2f9b5342b3efe207a356`).
  Both start asynchronously while the menu is open, and the installer and
  release packagers include and validate both. A missing voice part leaves the
  NPC art usable and emits a bounded ordinary-log warning, rather than one
  warning per absent cue.

## Validation and remaining headset checks

The integrated merchant handoff fixture passed 1,428 production assertions and
37 mutation controls; the cabinet fixture passed 22,914 and 39. The enchantment
handoff fixture passed 1,152 and 34; the native-visit fixture passed 97 and 7.
The voice fixture passed 3,858 and 20, and the multiplayer voice-relay suite
passed. Async bundle warmup, missing voice-part recovery, the Unity bundle-format
check and full release packaging passed; the ZIP contains both town bundles. A
Release build reported zero errors and warnings. The imported-skin Unity motion
fixture passed 629,160 positive assertions and all 46 negative controls. The
actual Unity cloth solver passed with visible finger deformation and a zero-
motion negative control; all 23 source mutations were rejected. The final
repository run passed 14/14 source gates, 80/80 presentation/runtime suites,
286,578 wire assertions and the format checks for all three bundles. Surface
comparison removed no keys, patches or log markers. The compiled-form comparison
against the older `dev` baseline still lists the expected broad NPC-feature
additions and exits nonzero by that tool's design; it is not a failing test.

The next headset test must confirm first-click cancel and visible return flight;
both directions of cabinet/owned-card replacement and matching controller pulse;
cabinet buttons and crank while a card is parked; sold-out art and contextual
speech; merchant palms outside the counter; priestess silhouette and full pose
transitions; a card-wide native enchantment aura, grip selection of original
enhancement areas, and no laser pickup; smooth physical cloth contact; and the
longer spacing of incidental speech. These are presentation and interaction
outcomes that source fixtures cannot establish in a headset.
