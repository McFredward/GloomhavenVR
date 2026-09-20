# Town services: first hardware variant

Implementation record, 2026-09-21. This is development work for the maintainer,
not a release announcement or a claim of headset validation.

## Interaction scope

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
- [ ] Original-widget multiplayer publisher and real Unity playback/render verification.
- [ ] Final NPC bundle, import/pose/visibility evidence and archive verification.
- [ ] Full repository guards, version stamp, packaged install and origin/dev push.

## Hardware pass

1. Install the complete development package, including both asset bundles. Open each service
   in Campaign and Guildmaster; repeat open, X/exit, reopen and service switches. Native
   first-visit introductions, confirmations and any threshold rewards must remain actionable.
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
