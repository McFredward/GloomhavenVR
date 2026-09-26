# NPC hardware follow-up — ModBuild 572

The supplied screenshots, two videos and Debug log are from ModBuild 571. The video of the
merchant cabinet's side cloth shows no visible response to a fingertip. The priestess video
shows her bowl-cover arms crossing and a hard return to prayer. The cabinet images show a
detached lantern, category controls projecting from the face, a broken/open lower stand and
misaligned legs. The enchantress image and log show an outstretched hand without its card
overlay on first approach: native opening was still pending for roughly 220 ms. These are
hardware observations of build 571, not proof of how build 572 will look in a headset.

The same log records a 5.11-second first map frame, including 4.26 seconds inside
`TownServicePopulation`. The first resident prefab access reached a synchronous town-bundle
load. This is one startup stall, not evidence of a progressive leak.

## Corrections

- The merchant's hanging side cloth is now an actual native Unity cloth surface with a
  dedicated hand-contact runner. Its owner publishes compressed cloth state through the
  resident snapshot; observers render that state rather than running a second solver. The
  cloth surface is set away from the cabinet wall so a fingertip can displace it visibly.
- The cabinet's authored shell now has joined legs, bracing and a closed lower compartment.
  Category hardware seats follow the sculpted cabinet face, and a lower mount carries the
  lantern. The category icons and physical input anchors retain their native behavior.
- The priestess has separate paths for prayer and bowl cover: one palm covers the bowl while
  the other rests beside her. An outer elbow guide and stable hand orientation avoid arm
  crossing and the abrupt transition; late cover hydration blends through the pose.
- The enchantress shows a neutral, non-actionable handoff preview while her native station
  finishes opening. The actionable overlay appears as soon as a valid owned card is ready.
  Merchant, enchantress and temple handoffs now strengthen their border and scale as a valid
  held item approaches, with a bounded hover pulse and an inner-volume snap pulse on the
  owning controller. Ineligible items do not trigger acceptance feedback. Existing resident
  visual replication carries the owner's presentation to the other players.
- Town-bundle loading and direct resident dependencies start asynchronously during UI setup.
  A native station whose assets are still loading is retried rather than permanently marked
  failed. The ordinary native service remains the fallback if the optional bundle is absent.

The ordinary UI and NPC voice contracts are unchanged. The merchant cloth uses the six bytes
remaining in the resident snapshot budget; the legacy snapshot is still accepted. Build 572
needs a full install because the town bundle changed. No arbitrary art-size ceiling was added;
the bundle remains below the Git transport limit for a regular file.

## Verification

- Release compilation: zero errors and warnings. The native Windows town bundle was rebuilt
  from the integrated prefab and copied to `prebuilt/ghvr-town.bundle`.
- Source, codec, resident, offer and activity checks exercise first approach, both controller
  releases, eligible-only haptics, shared visual presentation, priestess arm contacts and
  wire-size limits. The native Unity cabinet checker passed 951,601 assertions and nine
  visual negative controls, rendering the front, side, underside and rear geometry. The
  shipping Windows bundle is 104,453,773 bytes (SHA-256
  `ab5c1aca10d03ad6e3552f3d89382721a6bc6281618a19ebb6e035991920349c`). The native
  cloth checker pressed its actual merchant panel and measured 0.10424 m peak visible motion
  and 0.00000 m residual motion after settling.

## Headset checklist

1. Touch and drag the merchant's hanging red side cloth and both table cloths. Verify
   immediate visible movement, gravity and a return without crossing the furniture.
2. Approach the priestess while donation is available and again after donating. Check the
   prayer-to-cover motion from front and side, including walking away during cover.
3. Inspect the merchant cabinet from the front, both sides and below. Use every category
   button and the page crank; verify all hardware, supports and lantern stay attached.
4. On the first enchantress approach, watch the overlay move from neutral preview to an
   actionable card target. Bring a valid and an invalid card near her hand. Check border,
   scale and controller pulse, then repeat at merchant hand and priestess bowl.
5. Enter the map both after lingering in the main menu and immediately after launch. Check
   whether the former first-map pause is reduced and whether all three residents appear once
   their optional art finishes loading. A missing bundle must leave the original windows usable.
