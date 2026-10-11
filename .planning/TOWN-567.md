# Resident audio, temple presentation and cloth contact — ModBuild 567

The supplied Debug log identifies ModBuild 566. It records repeated merchant, temple and
enchantress transitions, but none of the existing `UIWindow.Show/Hide` suppression records. The
automatic map-cap route also states that it dispatches no pointer event and therefore cannot play
the native press sound. Source inspection found the missing path: `TownServiceActivitySoundClock`
emitted a synthetic `Cloth` event whenever resident attention crossed its approach or departure
thresholds, and `TownServiceActivityAudio` resolved that event to the flat game's
`PlaySound_ScenarioUIEquipmentToggle_Body` clip. The copied clip was played on a spatial source at
each resident, explaining both the reliable enter/leave cue and reports that it appeared randomly
from different directions.

The build-566 hardware screenshots also show the priestess upper-arm chains originating below the
visible shoulders, an outward-spread cover pose and candles within the arm/hip envelope. The cloth
report contradicted the old fixture's simulated movement result. Running the shipping cloth class
revealed that PhysX moved the hidden sheet, but the separate point-in-AABB visibility classifier
re-captured the displaced result as a new zero before it could reach the rendered mesh.

## Changes

- Resident approach, departure and priestess pose changes no longer emit invented `Cloth` foley or
  resolve the flat equipment-toggle clip. Coin contacts, spell effects and resident voices retain
  their existing spatial playback. Automatic immersive native mode changes also have a narrowly
  scoped direct-audio guard; physical map presses, classic flat services and transaction sounds do
  not enter that scope.
- Debug sessions receive bounded causal records for actual NPC foley and any suppressed native town
  audio request, including item, transition and managed caller. The budgets are 8 foley records per
  resident and 32 native suppression records per session. Normal player logs receive none.
- The temple purse uses the same palm gate as the ordinary card fan. It is hidden until the free
  wrist is turned into the configured fan-view pose, then remains informative even when the player
  cannot afford or repeat a donation. Eligibility still disables its collider, guide and callback.
- The blessing uses one seeded local-space Unity particle system with soft billboard motes and the
  existing shared blessing revision/elapsed time. Hand-animated tetrahedron meshes were removed.
- Cloth preparation and contact now measure the actual palm-to-fingertip capsule, including its
  radius, against the 25x13 physical sheet. A contact episode captures one stable origin; withdrawal
  fades to the authored drape even while the hand remains inside the wider approach margin, and
  gravity stops after recovery. The fixture now runs the production `TownServiceCloth` class with
  the imported priestess asset instead of validating only a parallel coefficient model.
- The priestess rig retains the imported clavicle/upper-arm origin rather than interpolating through
  the former low IK pole. Her attentive hands rest symmetrically at the hips. Bowl cover uses two
  complete palms-down frames with relaxed forward fingers, and availability plus departure blend
  continuously back to the resident activity. Merchant pose behavior is unchanged.
- Priestess candles moved to the visitor-facing table edge and are checked against complete lantern,
  book, bowl, arm and hip bounds.

No wire record or configuration key changed. The existing replicated resident pose and blessing
records carry the corrected presentation.

## Validation

- Native town audio: 32 runtime assertions and 5 production-bound negative controls. The forbidden
  equipment-toggle item is absent from resident attention/cloth paths; physical and flat-mode sound
  controls remain present.
- Temple ritual: 200 runtime assertions and 19 negative controls across wrist presentation,
  eligibility, donation continuation and shared particle blessing.
- Production cloth: imported priestess runner contact produces 0.08253 m visible response, null
  contact produces 0 m, and withdrawal while still near returns to 0 m. Three actual runners, 18
  source mutations and the in-player dead-visible negative control pass.
- Imported activity: 628,730 assertions and all 46 production/negative variants pass, including low
  shoulders, splayed cover hands, upturned palms and availability/departure cover pops. A final
  positive imported-rig repeat also completes 628,730 assertions. Three bundle views and the full
  192-frame attention/cover/exit sequence were inspected.
- Decoration: 137 assertions and all 16 negative controls pass, including full priestess body and
  arm clearance.
- Integrated refactor guard: all 14 structural source suites and all 79 local runtime/Unity suites
  pass; wire validation completes 286,569 assertions. The command's final non-zero status is the
  expected compiled-form report against the older `080c505e9` feature baseline: 0 guarded config,
  Harmony or log-token surfaces were removed.
- Strict integrated Release compilation reports zero warnings and zero errors. Documentation/i18n
  structure and `git diff --check` also pass.

These checks establish the source of the obsolete cue, production call ownership, imported rig
geometry and actual solver-to-render response. Headset verification still decides perceived pose,
particle appearance, cloth weight and complete audible silence at every resident boundary.

## Hardware checklist

1. Enter and leave merchant, priestess and enchantress repeatedly and in alternating order. No flat
   equipment/window open-close cue should play at either boundary; voices, coins, spells, cabinet
   movement, physical map presses and transaction confirmation sounds should remain.
2. Turn the free wrist as for the normal card fan at the priestess. The purse should appear only in
   that pose. Repeat after donating or without enough gold: it should still appear with status text,
   while grab/drop guidance and another donation remain unavailable.
3. Complete a donation. Confirm a soft blue/gold particle blessing without visible solid polygon
   pieces, and verify the same effect timing from another multiplayer client.
4. Push both priestess runners and the enchantress cloth with palm and fingertip. They should follow
   the hand, remain above the furniture, and promptly return even if the hand stays nearby.
5. Observe the priestess from front and both sides while approaching, while she covers the bowl and
   while leaving that state. Shoulders must remain at their anatomical height, hands should rest at
   the hips or lie relaxed over the bowl, and no transition should pop or twist.
6. Check the priestess stand from both sides through the same poses. Candles must remain clear of her
   sleeves/hands as well as the lanterns, book and bowl.
