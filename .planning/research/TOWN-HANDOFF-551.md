# Town handoff corrections — build 551

## Evidence and causes

Reviewed all eight `VirtualDesktop.Android-20260924-*.jpg` screenshots supplied in
`.planning/debug/npc_probleme` and the local build-550 log banner. The images show
small enhancement artwork and overlapping native controls; they do not prove a
successful purchase or multiplayer presentation. No new remote capture was supplied.

The offered-card reclaim callbacks existed, but the real grab eligibility path
rejected them while the native confirmation owned the modal. Item fan suppression
and stale card collider state could also reject an otherwise valid handoff. The
new exception is narrowly scoped to the actual offered object and the exact native
transaction callback; unrelated modal input remains blocked. Tests enter through
`ProximityGrabber.ForceGrab` and production grab predicates, rather than calling
`OnGrab` alone.

## Implementation

- Merchant and enhancement cards can be withdrawn during their own pending native
  decision. Withdrawal cancels only that live transaction. Ordinary item fan hand
  restrictions remain intact; a current holder is not evicted by a stale fan gate.
- Original title, information, price, enhancement icon/name and confirm/cancel
  widgets move intact below the palm. Native callbacks, pointer feedback, fade and
  hide continuation are retained. There is no extra window, grab handle, automatic
  payment, or replacement confirmation text.
- A dedicated frame follows the actual offering palm, separately from the larger
  card's hovering seat. Controls sit 12 cm toward the visitor; button height is at
  most 4.5 cm. The lowest edge is at least station-local .970 m, above the authored
  .955 m worktops. This replaces the initial excessively distant front-face layout.
  Native aspect ratio is preserved at small, ordinary and scenario-scaled stations.
- The merchant offers a palm only for nearby eligible held/parked merchandise.
  Active zone membership publishes this intent; a pending card keeps the zone
  active with zero overlay opacity. Remote authoring consumes the published state.
- Enhancement artwork retains normal inspected-card world size independently of
  station scale. Its complete collider is restored and the seat rises enough for
  the full card to clear the hand.
- The enhancement folio keeps the original inventory `ScrollRect`, viewport, rows,
  card holder and native controls. Bounded layout descriptors replace obsolete
  tests of the discarded fake rune layout. Original scroll input remains active.
- Inscription teardown tolerates a parent destroyed by scene/native closure.

## Validation

Worker checks use the Unity 2021 runtime and production-bound source with explicit
boundaries. Results are evidence of code behavior, not headset appearance:

- Merchant handoff: 1,228 assertions, 10 rejected negative controls; final active-zone
  run `/tmp/town551-merchant-zone/run-vkrdokq5`.
- Enhancement handoff: 833 assertions, 17 rejected negative controls; run
  `/tmp/town551-enhancement-size/run-mwjqy4k_`.
- Native folio geometry: 65 assertions, five rejected negative controls; run
  `/tmp/town551-folio-layout/run-e57ismw_`.
- Interaction/confirmation: 1,137 assertions, 44 rejected negative controls; final
  compact-layout run `/tmp/town551-compact-confirmation-final/run-g0th4uj5`. Includes native pointer
  hover/click, cancellation identity, modal regrab routing, fade restoration,
  original scrolling and destroyed-parent teardown.

An earlier negative-control run failed prematurely because the newly added fake
native enhancement window omitted its CanvasGroup. The fixture now includes that
native component, allowing the existing mask-state mutation to reach the intended
assertion; production assertions had already passed.

Strict combined compilation is performed in the integration checkout, which also
contains the cabinet and motion dependencies absent from this isolated lane.
Hardware checks still needed: readable text at all three stations, unobstructed
buttons and large card while animated, withdrawal with either hand, native buy/sell
and enhancement completion/cancel, walkaway and character switches, and remote
ownership/presentation parity.
