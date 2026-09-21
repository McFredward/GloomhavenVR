# Town services: first hardware variant

Implementation record, 2026-09-21. This is development work for the maintainer,
not a release announcement or a claim of headset validation.

## Interaction scope

Build 539 adds **Immersive town visits** to VR options, disabled by default. The setting
controls the local merchant, temple and enchantress presentation. With it off, their original
1.0.6 window flow remains in charge. Switching off during a visit cancels cosmetic samples,
restores native sections and portraits, and keeps the same native controller and selection.
Another visitor's enabled station remains visible for multiplayer presentation parity.

The merchant, temple and enchantress keep the game's service entry, selected character,
permissions, stock, prices, confirmations and exit callbacks. Opening the existing destination
introduces its animated NPC and furniture. Each visitor has a reachable, movable work tray;
one NPC serves all visitors of that destination.

The original inventory/blessing/rune sections become independently movable reading surfaces.
Their native tabs, scrolling, explanations and buttons remain usable. A trigger grip lifts a
non-authoritative copy of an original catalog entry. Releasing it over the work tray invokes
that entry's original selection button. Payment or enhancement still requires the native
confirmation. Laser selection follows the same original flow without requiring physical reach.
The work tray uses the existing window handle for palm and laser carrying, reeling and resize.

The enchantress retains the original card display, enhancement-point selection, compatibility
filter, price calculation and buy/remove modes. Card samples are not added to the player's hand
and cannot become playable cards. Pool reuse, character/card/mode changes, tracking loss,
closing and synthetic cancellation invalidate a held sample before it can dispatch a click.

This first variant uses original illustrated entries rather than speculative meshes for every
item or blessing. The NPCs have authored body/finger rigs and greeting/idle/gesture clips.
Facial animation, separate book/staff handling, transaction-specific hand contact and richer
coin/rune choreography remain art/interaction polish; no such animation owns a continuation.

## Placement and ownership

Station positions use the native parchment scale, tracking-floor solve and map reading
direction. They occupy the far half outside the tabletop and initial spawn ring. A visitor's
zoom or head pose never changes another visitor's station. The active author publishes its
station pose; different decorative environments must not change the public interaction frame.
Exact clearance against all native and custom scenery still requires headset inspection.
Mixed reality adds no scenery backing, enclosing room or floor plane.

Original window conversion is retired and its parent restoration verified before any child
section receives a separate owner. Teardown reverses that handoff: retire the remaining context,
restore children in reverse order, then let the ordinary native window lifecycle continue.
Failure restores the original service window. Animations never delay that restoration or any
native confirm, cancel, reward or exit callback.

## Validation and handoff checklist

- [x] Strict Release compilation of the integrated local interaction and transport.
- [x] Real Unity interaction harness: 77 assertions, 19 compiled negative controls.
- [x] Codec/delta tests and actual existing router/fragment golden vectors.
- [x] Original-widget multiplayer publisher and real Unity playback/render verification.
- [x] Final NPC bundle: real Unity import/pose/visibility and clean-project bundle-load checks.
- [x] Complete install archive verification.
- [x] Repository source/runtime gates and build 538 version stamp.
- [x] Installable development package; handoff is on `origin/dev` (not a release).

## Hardware pass

1. Install the complete development package, including both asset bundles. Enable immersive
   town visits in VR options. Open each service
   in Campaign and Guildmaster; repeat open, X/exit, reopen and service switches. Native
   first-visit introductions, confirmations and any threshold rewards must remain actionable.
   For each service, toggle off while open and while holding a sample: expect the original
   complete window and portrait, unchanged selection and no accidental confirmation. Repeat
   off/on, X/reopen and restart with the setting off. Check mixed enabled/disabled visitors.
2. Inspect NPC scale, feet, face/hand detail and animation in the original room, cellar,
   night forest and mixed reality. Check furniture clearance and both-eye shader rendering.
3. Merchant: change character, buy/sell, browse all categories, scroll, inspect a sample,
   return it outside the tray, place it on the tray, cancel a quote, then accept one purchase.
   Check the actual payer, stock, equipment and original item description.
4. Temple: select available blessings, inspect cost/recipient, cancel and confirm. Check
   devotion progress and native rewards. Repeat with insufficient funds and unavailable choices.
5. Enchantress: inspect several cards, choose enhancement points, change rune and buy/remove
   mode where permitted. Cancel, then accept one valid enhancement. Switch character or card
   while holding a sample; the old gesture must not affect the new target.
6. Move/resize the work tray and reading surfaces with either hand and laser. Check that
   a handle blocks hover/click behind it, and that trigger release selects at most once.
7. Multiplayer: browse different services and the same service concurrently, including
   late join/reconnect and an unmodded flat peer. Compare original content, selected character,
   prices, tooltips, held samples, movement and animation from owner and observer viewpoints.
   Observers must never activate another player's controls or receive a second transaction.
8. Leave for a scenario and return; repeat with a held sample or open confirmation. Nothing
   may remain as an invisible input blocker, duplicate NPC, orphaned window or stale purchase.

The normal log retains bounded lifecycle/failure context. Use the maintainer's usual Debug
setting for the first hardware pass; automated checks cannot establish headset appearance.

## Asset evidence

`ghvr-town.bundle` is 95,895,013 bytes (SHA256
`b2a923b1ad496116587bbecc1efda379b808c64649a917341387d060b21efc29`).
The original `gloomhavenvr.bundle` remains byte-identical at 74,942,975 bytes (SHA256
`fe1a659c17b4151e929691aa070d402b8cd299a462315b1d6691d2622d491693`).
Both use UnityFS 7 / Unity 2021.3.5f1. The new bank exposes exactly four service prefabs.
Clean-project loading, all three LODs, twelve animation clips and the visibility shader were
checked in real Unity. Evidence is retained in `.planning/debug/town-assets-review/`.
See [TOWN-SERVICES-RUNTIME-ASSETS.md](TOWN-SERVICES-RUNTIME-ASSETS.md) for reproducible
authoring and the explicit limits of the first face/finger rigs.

The final local interaction run `.planning/debug/town-service-interaction/run-emey_qgy`
passes 77 runtime assertions and rejects all 19 compiled negative controls. Strict Release
also compiles against the committed CI reference assemblies with zero warnings/errors.

## Integrated runtime evidence

The integrated production mirror run `town-service-mirror/run-evlmt2yh` passes 285 real
Unity assertions. The separately compiled ten negative controls pass in `run-lhg4is1r`;
the additional rapid hide/reopen regression and its negative control pass in `run-ss40thon`.
Original and observer pixel pairs include an animated outer Canvas, nested module order,
masking and intermediate motion. Layer and camera assignment are checked before image
isolation. These are real Unity engine tests with explicitly documented game-adapter fixtures,
not an in-game native-prefab or headset recording.

The final codec suite passes 50,240 assertions; the actual production wire/transport suite
passes 255,887. Sixteen source/docs/bundle gates pass, as does the surface comparison
(625 configuration keys, 174 patch registrations, 4,746 log markers; no removals).
The existing broad runtime harnesses passed up to item presentation, where a source-binding
check exposed the changed order of the transport's condition. Keeping its original prefix
restored that integration contract without weakening the test. Item presentation, flight
timing, figure release and send-path tests then completed separately, including their negative
controls. Final source gates and wire vectors were repeated after integration; unaffected
earlier runtime harnesses were not redundantly repeated. The compiled audit compares both the
historical guard baseline and the immediate pre-feature source to distinguish old differences.
[The final audit](TOWN-SERVICES-FINAL-AUDIT.md) finds 27 new and 16 changed type files against
the pre-feature build, with four changes solely from build-number constants. No unexpected
legacy runtime change was found. The historical guard difference remains expected
(0 order-only / 105 changed / 90 new-or-gone entries); it is not a claim of a zero-difference
refactor.

The complete install archive is `dist/GloomhavenVR-1.0.7.zip`, containing both matching
bundles, plugin, preloader and runtime dependencies. ZIP CRC, bundled-file hashes, plugin
identity and Windows text encoding/layout were verified. This is an unreleased dev build;
all VR participants in the hardware test need build 538 and the complete asset set.

Cold observer construction is bandwidth-dependent. A synthetic context with eight visible
rows takes about .94 seconds on an otherwise idle scheduler; 24 rows take 1.83 seconds.
Under deliberately saturated competing presentation streams, the larger 64-row fixture takes
22 seconds. The shared transport budget remains unchanged. This is a measured first-variant
limit, not a measured native catalog size or an exception to visual parity. See
[TOWN-SERVICES-SYNC.md](TOWN-SERVICES-SYNC.md) and
[TOWN-SERVICES-MIRROR-VALIDATION.md](TOWN-SERVICES-MIRROR-VALIDATION.md) for workloads,
CPU/GC observations and coverage boundaries.
