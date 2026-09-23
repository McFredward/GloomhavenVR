# Build 545 — physical town services and portrait identity

## Current scope

The maintainer accepted the eleven build-544 screenshot findings and requested fixes,
closer identity to the original flat-game portraits, richer stands using original game
assets, entirely physical merchant/temple interactions, minimal interface elements for
enhancement, and a substantially more visible enchantress spell. Build 544's automated
passes do not establish acceptable hardware appearance. The implementation is integrated for the next hardware candidate; final validation status is recorded below.

The hardware evidence is the eleven `npc_probleme/VirtualDesktop.Android-20260921-21*.jpg`
images and current local logs identifying ModBuild 544. Remote logs still identify 500
and are not evidence about this test. Visible defects include damaged hands, hovering
claw-like attentive poses, stretched necks/hollow costume joins, poorly integrated dark
eyes, floating wall-mounted lights, blank books, plain purple slabs and poorly lit faces.
The merchant's cards also failed to lift in the reported test.

## Interaction contracts

- Merchant: all available native catalog entries have stable physical rack locations,
  including the fully unlocked catalog. No page buttons, category tabs or flat menu on
  the counter. Lift to inspect with hand or laser. Deliberate release into an indicated
  buy/sell area submits the original native transaction after revalidating identity,
  owner, stock and cost. Grip, hover, cancellation and unrelated release never spend.
- Temple: physical offerings represent the actual native blessing options and prices.
  Deliberately deposit the chosen offering; preserve character selection, affordability,
  already-purchased restrictions, devotion progress and every original reward/continuation.
- Enchantress: physical ability cards, legal rune choices and a work surface replace list
  and page controls. Native rules still decide legal slots, prices, capacity, preview,
  purchase and removal/refund. Necessary original card symbols and readable quotations
  stay attached to the objects; do not silently omit detailed rules or permission reasons.
- Every physical interaction preserves native server validation and permissions. Original
  controllers stay the backend; presentation never writes gameplay state. Immersive mode
  remains default-on and disabling it restores the original window workflow.
- All owner-visible objects, intermediate poses and feedback must have multiplayer parity.
  Artwork/identity disclosure keeps the existing authorized item/card channels. A larger
  catalog must not be silently truncated by prior six-card or transport bounds.

## Art direction and work ownership

Original exported portraits under `.planning/debug/npc-references/` are identity authority;
generated variants are secondary references. Hand topology, eye/lid integration and neck/
costume deformation require actual close-up and oblique review, not only marker assertions.

The built-in imagegen skill produced one internal stand concept sheet, saved privately as
`.planning/debug/town545-concept/stations-concept.png`. It is design guidance, not game
geometry or a promise to replace original game decoration with generated assets.

Workers start from dev `b3d47378` in separate initialized worktrees. Faces owns actor
authoring/final assets; merchant owns catalog/token interaction; stands owns original
decoration, lighting and work motions. Root owns temple/enhancement integration, shared
presentation, input hooks, transport, localization and final review. No release is requested.

## Integrated interaction and lifecycle details

- The merchant retains 164 base/DLC item identities plus actual owned instances in physical
  wooden filing drawers. There is no six-card or page cap. The original game service decides
  actual availability. Drawers have real constrained pulls, stable card slots, and explicit
  buy/sell marks visible only for an eligible held object. Free inspection never trades.
- Temple offerings use the original `Treasure.Clutter.Shelf.Individual#1` mesh. The original
  devotion level, donated gold, progress and help text appear as inscriptions on the native
  open ledger. Enhancement cards retain original printed ability hotspots; physical rune
  choices retain native costs, capacity, preview, availability and optional removal/refund.
- All three classic source windows remain alive under reversible masks. Their controllers,
  selection and server validation are authoritative. Exact-owned automatic confirmation
  prompts stay visually suppressed through the original hide fade; `UIWindow.onHidden`
  occurs at fade start and is not used as a completion signal. Reused prompts restore their
  native controls; a bounded timeout restores controls if the native transition stalls.
- Additional visitors receive service-specific furniture and inert original decoration,
  including the real offering bowl and ledger. Each workspace owns its material fades.
  Native candle animation time is copied from the permanent resident's shared work clock.
  Observer flame clocks advance between packets through renderer property blocks; animation
  time is excluded only from the verified TownFlame immutable material cache key. Delayed
  packets do not rewind playback or allocate another material for every sample.
  Local/remote practical lights track the same actual lantern material visibility and pose,
  without adding children to captured module topology or running native gameplay scripts.
- Canonical decoration templates retire when their unvisited resident disappears; reopening
  freezes current resources. Printed card surfaces and inscriptions do not acquire MR window
  backings. Native visibility/permission rules continue to control the remaining card hotspots.
- Late catalog scalability uses persistent physical slots and omits opaque closed contents
  from mirror traversal. The bounded town module manifest supports 2048 entries within the
  existing 60000-byte packet envelope. No presence field or cosmetic action traffic is added.
- Native confirmation dispatch completes after its hide animation. A narrowly scoped Harmony
  prefix captures the original confirm and cancel delegates during a physical selection and
  revalidates ownership, selection, eligibility and session at completion. Stale selections
  run native cancellation once; the mod never writes the controller's pending flag or currency.
- Enhancement capacity retains its original label, icon, count and warning group as book
  inscriptions, together with the original selected-card availability explanation. The native
  level-9 capacity parent was inspected: its children are Text, Info, Image and Warning.
  Inactive native explanations stay hidden locally and are omitted from observer publication.
  Quiet offering eligibility checks affordability/availability before CanBuy, whose failure
  branches otherwise display warnings every time a held offering is sampled.

## Physical layout and native catalog bounds

Resident and additional-visitor stations use one canonical parchment-relative layout in all
map environments. Local environment choice, reading side and spectator gaze cannot change
shared furniture poses. Actual forest/cellar meshes and authored-active campaign/Guildmaster
furniture were checked with a 5 cm safety margin, including fully extended merchant drawers.
The layout clears the native Guildmaster bench and barrel rather than assuming an empty map.
The merchant retains all 164 base/DLC item identities and up to 512 distinct owned copies in
the shipped 21-character roster. Sixty-four exposed card strips fit each physical drawer;
seven lower levels stay above the floor, and additional upper cabinets clear the original
ledger and hands. The native common-item stock limit grows with roster size, so the ordinary
six-copy campaign allowance was not used as the Guildmaster bound. Buying/selling marks and
actual release acceptance share the narrower central clearance.

Enhancement placement uses the original shipped rules: the full 21-class census contains at
most 31 ability cards, and the native enhancement query exposes at most 28 simultaneous rune
choices. All remain physical and individually accessible on the existing worktop. Refreshes
smoothly rearrange resting pieces; held and returning objects retain their motion ownership.
The original capacity count, warning, selected-card explanation and exact original callbacks
remain available. The temple offering lies flat on its supporting surface.

## Final asset and focused runtime evidence

- Final Windows town bundle: 100,501,555 bytes; SHA256
  `08ff85016de6350533a1540b64591ed1ff4c3555ac6d7986ba79486df0fe597e`.
  The main asset bundle is unchanged. All peers need the complete matching package.
- Final asset validation: 599 render assertions and six visual negative controls. Actual D3D eye
  shader variants bind practical lights; corneal highlights were measured under the actual
  2.6-power stand lamps. Anatomical hands use a shared 2K atlas and one additional material.
- Final actual-asset activity/contact validation: 125,088 assertions and eleven compiled
  negative controls, using the matching immutable Linux review bundle. Final actor/furniture
  envelope validation passes 1,775 assertions. These do not establish headset appearance.
- Complete physical merchant: 7,459 assertions and sixteen compiled negatives, including
  all 512 distinct owned copies, eight-column picking and supported overflow cabinets.
- Original enhancement data and physical placement: 8,341 assertions and five negatives.
  Deferred native ritual confirmation guards: 120 assertions and ten compiled negatives.
  Continuous mirrored flame playback: 1,022 assertions and six clock negatives, including stable
  material ownership across repeated packets and intermediate owner/observer rendering.
  A separately compiled shader negative reproduces disappearing billboards: dynamic batching
  destroyed their per-object origin. `DisableBatching=True` restores the actual candle/glow
  billboards without changing their colors, geometry or native texture.
- Authoring inputs and review evidence are archived privately in
  `.planning/debug/npc-authoring545/`. Original portraits remain the identity authority.
  No paid mesh generation was used for this iteration.

The integrated local run passes all 14 source gates and all 59 runtime suites. The wire
executable passes 260312 assertions after registering TownFlame's explicit bundle path.
Strict Release has zero warnings/errors and bilingual documentation checks pass. One
subsequent parallel repeat exposed a nondeterministic existing PanelInk allocation assertion;
the positive rerun passes. Its measurement now has three fixed full-boundary warmups and
still requires exactly zero bytes, without retries/tolerance. Forty concurrent fresh-process
runs pass, and an intentional allocation mutation fails with 240000 bytes. The transient's
cause remains unproven. The final complete guard also passes all 14 source / 59 runtime suites and 260312 wire
assertions against this frozen fixture; its exit 1 reports only the expected historical
compiled-baseline differences (0 moved, 109 changed, 140 added/removed). Strict Release and
docs checks exit 0. Final guard evidence is the 20260922-001235 / 001251 test-run reports.
The complete 185848785-byte development ZIP is CRC-verified and matches the DLL plus both
bundles; hashes and the exact package source commit are recorded in
`.planning/debug/town545-package-verification.json`. Dev CI is checked before handoff.

## Hardware acceptance checklist

1. Inspect each face from the front, side and above, while working and while following a
   visitor: portrait identity, eyes/blinks, neck/hood joins and relaxed finger contact.
2. Inspect all stands in each map environment, including Guildmaster and mixed reality:
   floor/support contact, native decoration, practical lighting and larger hand spell.
3. Merchant: pull both stock/owned drawers; inspect freely using near grip and laser;
   buy/sell only by deliberate eligible drop. Test expensive/unavailable items, owner
   changes, full late-game stock and cancelled/repeated operations.
4. Temple/enchantress: native donation limits, devotion progress, card/slot/rune choice,
   price/capacity, confirmation, refund/removal and refusals. Reopen and switch character.
5. Multiplayer: compare physical cards, held details, drawers, drop marks, native
   inscriptions, shared work/attention and additional-visitor furniture with the owner.
6. Disable immersive services during and between visits; confirm original service windows
   and their native gameplay continue. Check frame timing on hardware with multiple peers.

The final bundled-shader cast proof changes 8,325 pixels against its same-pose hidden control,
with no shader substitution. Player attention removes the cast and rests the hands on the
counter. Evidence: `npc-authoring545/final-bundled-cast/`; Linux bundle SHA256
`add071e017817d75e51eb08851d70c045ea6b57e715dca8c874efe14b2a1fa51`.
The earlier full contact/envelope runs use the identical actor art before this shader-only
repack; the final asset manifest verifies that no actor input changed.

Close stereo appearance, dense-stock ergonomics and hardware performance remain unverified.
The cloth/collar art is still lower detail than the newly fitted faces; no photorealism claim
is made. Automated passes establish the tested behavior, not visual acceptance in a headset.
