# Physical merchant — build 545

This replaces the build544 six-card counter and page/filter buttons. The complete native
buy catalogue and the selected owner's sell inventory coexist as physical item cards.
Native categories fill real pull-out filing drawers:64 persistent cards per drawer,
eight columns and eight separately exposed strips. Overflow creates another drawer; it
never discards stock or replaces a page. A native stock refresh retains unchanged row,
card and drawer identities. Changing the selected owner invalidates the old gesture.

## Interaction and native authority

The previous card face had a transparent clickable uGUI overlay. That surface could
claim the trigger before ProximityGrabber, while RayGrabDriver only supported panel bars;
there was no far-grab route for TownServiceToken. Build544 logs confirm a token could be
proximity-elected, but do not prove which refusal term blocked each reported attempt.
The new face has no clickable overlay. Registered physical card and drawer colliders own
near/far pickup, with UI/physics/solid occlusion and shared carry/release suppression.

Inspection ignores affordability, and displayed physical props remain grabbable when the
native backend button is hidden. An eligible held card reveals only its matching BUY/SELL
zone. Real trigger-up, a valid tracked pose, a completed hand-follow sample, unchanged
item/context/session and the matching drop volume are all required. Every other release
returns the sample, including cancellation and a rapid click before tracking has moved it.

A transaction resolves the original native row afresh after switching the hidden original
inventory mode. Native row click opens the native confirmation; only that exact item may
be confirmed after rechecking affordability, stock, ownership context and native locks.
No service.Buy/Sell, currency, network action or authoritative item state is written here.
A pre-existing confirmation is retained. The native multiplayer server still validates
races and final stock. Presentation.SelectionContext must ignore the merchant backend's
mode switch, since buy and sell are simultaneously available physical inventories; it
must still include selected owner and retain enhancement-mode scoping.

Original item fronts and native price/stock labels remain. Held inspection calls the
original item tooltip's Show with its original discount service and owner binding, then
attaches an inert detail/rules mirror beside the held card. It never selects or spends.
The original tooltip stays under the hidden backend row. The latest picked held card owns
that native tooltip; release restores inspection of another held card if present.

## Geometry and multiplayer integration

Two lower banks have centres x±.68 m and 1.04 m drawers. Fixed opaque housing encloses
closed cards. Seven levels fit below the countertop, separated by .13 m. Additional drawers
continue above the countertop rather than through the floor. Upper banks move out to x±.70 m
and forward to z0 so their closed backs remain ahead of the NPC and their inside walls clear
the original .32 m ledger. The first upper housing extends down to the countertop as support.

Every drawer holds eight columns of eight exposed card strips. Card faces are at most
.12×.096 m, with .126 m column pitch; outside columns clear the actual inner walls by 5 mm.
Lower drawer travel is .78 m and upper travel .59 m, preserving the same fully open front
extent around z−.82 m. Upper housing expands the overall bank width to |x|≤1.238 m, inside
the 1.27 m countertop. Narrow BUY/SELL marks stay in the clear centre gap at x±.078 m.
Their canonical mirror templates use the same .14×.26 m dimensions as the live marks;
merchant release acceptance uses .07 m half-width, while other services retain their defaults.

The complete supplied native 21-character roster can hold 512 non-quest item copies,
requiring ten owned drawers; the three upper levels remain below 1.551 m. No catalogue rows
are capped or paged. Custom rulesets or a roster beyond the supplied native classes may
increase upper height and require a new room-clearance audit; that is not covered by the
512-copy native geometry proof below.

Cards in hand or returning prevent their drawer closing and defer workspace relocation.

Integration exposes catalog.Drawers (Root and fixed HousingRoot), Zones (Root), and
Entry.Exposed. Drawer/housing/zone static CreateTemplate methods supply inert templates.
All intermediate drawer motion and exposed cards are published. Only fully enclosed,
unseen drawer contents may skip widget traversal and remote modules; this is never based
on the observer's gaze. Existing record grammar remains root integration ownership.

## Focused evidence

- Strict Release:0 warnings and0 errors.
- Real Unity catalogue gate:1,406 assertions and10 compiled negative controls, evidence
  `.planning/debug/town-service-catalog/run-la5d4sen`.
- Real Unity interaction gate:917 assertions and34 compiled negative controls, evidence
  `.planning/debug/town-service-interaction/run-578bl5vq`.
- The catalogue gate binds production catalogue, rows, transactions, drawer, zone and
  preview code. Native inventory/service responses, original gameplay confirmations,
  art pooling and mirror rendering are explicit fixture boundaries. Unity transforms,
  canvases and native-style button dispatch are real. It does not claim to execute the
  actual game transaction offline or prove headset output.
- New falsifiers cover a six-card truncation, hidden-drawer picking, insufficient drawer
  travel, closing on a held card, affordability/context/confirmation/identity refusals,
  missing hierarchy restoration and physical drop tracking/zone guards.

Final furniture/material rendering, nearest-hit tests for every overlapping card strip,
actual scene clearance and hardware appearance remain separate validation tasks. Original
inventory hierarchy and permissions are restored on teardown/opt-out.

## Native refusal and integration follow-up

A post-selection refusal now cancels the exact newly installed merchant confirmation
through its original `OnCancel` transition. The captured item and changed native callback
identify ownership; a pre-existing or unrelated prompt is never cancelled. Native service
validation remains authoritative. Strict Release compilation passed with no warnings/errors.

- Catalogue: 1,407 assertions + 11 compiled falsifiers,
  `.planning/debug/town-service-catalog/run-2snvnihc`.
- Updated presentation/token integration: 930 assertions + 34 compiled falsifiers,
  `.planning/debug/town-service-interaction/run-4tawfyev`.
- Original ritual Confirm/Click method bodies: 34 assertions + 6 compiled falsifiers,
  `.planning/debug/town-ritual-transaction/run-8bqdhxc6`.

The ritual callback fixture binds the integration checkout source read-only and records
its SHA-256. It uses real Unity buttons/EventSystem and controlled native prompt/cost
responses. Both temple and enchantress cover initial/post-selection affordability,
owner/item changes, existing/unowned prompts, native refusal and stale-own-prompt
cancellation. It does not execute the game's economic service offline. Physical release
cancellation is covered separately by the complete production Token integration fixture.

Presentation tests now expect whole-window suppression and physical ritual lifetime,
not build544's three floating sections. The retained legacy section/tray rollback branches
are explicitly injected fixture scenarios. Ritual artwork/geometry are boundary components;
actual production Token/session/context guards remain bound. Merchant backend tab changes
correctly preserve the held physical item; enhancement mode changes still cancel it.

## Measured expanded layout and exposed-card rendering

The first stock drawer and first owned-inventory drawer start open. Closing a drawer
suppresses every cached face Canvas (including nested native canvases), the row inscription
and the card body. The original ItemCardUI GameObject stays active, avoiding an artwork
OnEnable/reset cycle. Opening restores the cached flags before the first exposed frame;
returning a native row to its pool also restores its original flags.

Room-relative placement replaces the former common radial layout, which overlapped the
larger merchant and additional visitor drawers. The complete opened travel is reserved:
merchant/visitor top width 2.54 m, drawer front -0.82 m, housing outer edge +/-1.218 m.
The initial separately measured room arrangements are superseded by the common-layout
validation below, because peers may choose different environments. Ground support registration
includes the new centre/outer posts.

Focused evidence (original build544 shipping assets, not the pending build545 bundle):

- Catalogue: 1,835 real Unity assertions and 13 compiled negative controls, including
  164 stock + 164 owned entries, initial access, closed rendering without native lifecycle
  resets, and the actual source-fitted collider nearest-hit test for all 48 strips of a drawer.
  Evidence: `.planning/debug/town-service-catalog/run-wwps6_w4`.
- Workspace: 3,003 assertions and 10 compiled negative controls; each original body clip,
  four phases and all three LODs were CPU-skinned to bound the actual residents. Source
  poses stay fixed in the room under changes of reading-side yaw. Evidence:
  `.planning/debug/town-service-workspace/run-p7c163z2`; the export-only repeat
  `run-7oin_jwn` supplies the actual production `.poses.csv`.
- Portable setting/grounding: 1,569 geometry, 71 lifecycle and 243 grounding assertions,
  with 14 compiled negative controls. Strict Release build: zero warnings/errors.
- `check-town-scene-layout.py` intersects padded station envelopes against original
  opaque room mesh triangles, with all three visitor counters and both drawer banks open.
  Both rooms have six poses, 18 reserved parts and zero contacts. A mutated cellar pose
  placed against the north wall produces ten contacts and fails, without changing assets.
  Evidence: `.planning/debug/town545-scene-layout-final.json` and
  `town545-scene-negative.json`. Environment bundle SHA256:
  `fe1a659c17b4151e929691aa070d402b8cd299a462315b1d6691d2622d491693`;
  production pose CSV SHA256:
  `272e8d4f10faef3cb1b6eef51baf6f5df8f31d21e9ce624dc821dc5f7b1867f3`.

The scene audit reserves 5 cm around each part, includes all simultaneously occupied
stations and preserves the complete native table diagonal. It explicitly excludes
non-solid effects/alpha foliage; it is not a visual foliage intersection oracle.
Native Guildmaster bench/barrel geometry is outside that custom-environment bundle;
the separate source-hashed native audit below closes that initial coverage gap. The final build545 furniture/character bundle
must repeat the focused geometry checks. None of these checks establishes headset quality.

## Deferred temple/enchantment completion safety

The original enhancement confirmation dispatches its purchase callback only after the hide
transition. Returning from the physical drop therefore does not prove that the selected
owner/item is still current when gameplay changes. A narrowly scoped prefix of the original
Sprite `ShowConfirmation` overload captures both native callbacks during physical selection.
At real completion it checks session, owner, selected identity and native eligibility again;
a stale selection invokes the captured original cancellation cleanup instead. The unused
native `_onCancelCallback` field is not a valid substitute: native Show captures its cancellation
argument in its transition listener and never assigns that field. Completion/cancellation is
one-shot. Ordinary flat prompts and other box instances remain outside the scope.

Native source audit confirms TempleShopService.CanBuy checks native stock/affordability and
network ownership/joining, not the pending-confirmation flag. The presentation session is the
service window's lifetime, not the confirmation's hide fade. Successful normal completion
therefore remains eligible. The existing native gameplay callback still performs the actual
transaction; no currency, pending flag or server authority is written by this helper.

Real Unity focused result: 120 assertions and ten compiled negative controls, including
both services, delayed owner/item/affordability/session changes, destroyed offers, native
disablement, cancellation before completion, duplicate completion and unrelated prompts.
Evidence: `.planning/debug/town-ritual-transaction/run-128ot7yk`. Confirm/Click and the complete
production guard are source-bound; native transition scheduling and Harmony prefix dispatch
are explicit fixture boundaries. Root registers the real patch and owns the narrow Confirm
scope hook. Strict production build with the new patch class: zero warnings/errors.

Catalogue acquisition now rolls back even when a constructor throws after the native
inventory was reparented. Native parent/sibling and permissions survive, partial presentation
roots are immediately hidden, and already-acquired zone subscriptions are disposed. Injected
failures at both detail-mirror construction boundaries are exercised before a successful reopen.
Updated catalogue evidence: 1,847 real Unity assertions plus 14 compiled negative controls,
`.planning/debug/town-service-catalog/run-f608an6y`; strict Release build remains zero warnings/errors.

## Final common layout: mixed environments and original native furniture

All environments now resolve the same six XZ/heading poses. A room-specific arrangement
would disagree when one participant selected forest and another cellar. The shared frame is
the original parchment quaternion's Y twist, matching SkyAlternative's room placement; default
and MR also use that frame, never the player's reading-side yaw. Local floor solving remains
unchanged and the elected resident author publishes its actual grounded pose.

| Station | Radius | Position bearing | Facing yaw |
| --- | ---: | ---: | ---: |
| Merchant | 2.30 m | 15° | 15° |
| Priestess | 2.30 m | 320° | 290° |
| Enchantress | 2.40 m | 160° | 160° |
| Extra visitor 1 | 2.30 m | 100° | 100° |
| Extra visitor 2 | 2.40 m | 220° | 220° |
| Extra visitor 3 | 3.10 m | 270° | 300° |

The angled stands fit actual gaps while keeping their interactive side generally towards
the map. Three residents and three simultaneous extra visitors, including every fully open
merchant bank, have zero padded-envelope contacts in either original custom room. Source
production poses are exported by the real Unity fixture and fed directly to the geometry
audit rather than restated as independent test constants.

The native audit verifies original level4/5/9/10 file hashes before accepting renderer bounds
and ancestor transforms. It applies the same parchment AABB-derived scale and yaw, then tests
all shared poses against active native furniture: Guildmaster table, bench and barrel in both
Guildmaster scenes; campaign tabletop in both campaign scenes. Authored-inactive Guildmaster
furniture present in the campaign files is explicitly recorded as excluded, not treated as
visible. All four scenes report zero contacts with the complete opened drawer envelopes.
The earlier custom-room-safe candidate intentionally fails the new native audit due to its
barrel/bench collisions, providing a geometry negative control independent of parser failures.

Focused evidence:

- Workspace: 3,075 real Unity assertions and 13 compiled negatives, including divergent
  environment layouts, default/MR parchment-frame regressions and unchanged-pose
  fades on reading-side changes;
  `.planning/debug/town-service-workspace/run-5nqlmdt7`.
- Portable geometry/lifecycle/grounding: 1,569 + 71 + 243 assertions, 14 negatives.
- Strict Release build: zero warnings/errors.
- Full measured audit: `.planning/debug/town545-common-native-final.json`; negative:
  `town545-common-native-negative.json`. Environment bundle remains
  `fe1a659c17b4151e929691aa070d402b8cd299a462315b1d6691d2622d491693`.
  Production pose CSV SHA256:
  `dec99bd1b34e13056424785d557c6ca8ca0a516288628706b11c83fc5e4b0512`.
  Native export SHA256:
  `ddc2397891ce309b475972fe9cdf7bd101168a2239093825fbeb0ae62f6ec598`.
  Each verified native scene hash and each active/excluded mesh is retained in the report.

The geometry gate uses original mesh triangles for opaque custom-room scenery and conservative
native mesh AABBs, reserving 5 cm around station parts. It is deliberately independent of
reading direction. The final build545 asset-envelope rerun is recorded below; hardware inspection remains
necessary; authored-inactive native objects unexpectedly enabled by a different game flow
would require an additional runtime scene sample.


## Final native capacity and asset-envelope gate

The capacity proof uses the game's actual reward-stock rules, not only ItemCard.TotalInGame.
`GetItemsToSell` returns every bound/equipped item for an owner (or all party inventories),
excluding QuestItem. Binding has no smaller inventory limit. Native campaign/Guildmaster
headquarters cap Rare at 2 and Relic at 1; Common uses `max(6, CheckCharacters.Count)`.
All eleven supplied Common definitions are SmallItem. At the supplied 21-character roster,
non-quest copies therefore total Head40, Body44, Hands102, Legs37 and Small289: 512 distinct
physical item identities. A six-character-or-smaller roster instead yields 347; the 285 sum
of authored TotalInGame values is not the reward-driven inventory bound.

Focused final checks:

- Catalogue: **7,459 real Unity assertions and 16 compiled negative controls**;
  `.planning/debug/town-service-catalog/run-6g7seiw3`. Includes all 512 distinct owned
  identities, all ten owned drawers above floor and within the upper envelope, all 64
  exposed card strips reachable by actual fitted colliders, inner-wall clearance, native
  refusal/rollback and initial visible stock. Mutants restoring below-floor overflow or
  widening columns into cabinet walls both fail the corresponding geometry assertions.
- Actual production Token: **18 assertions and two compiled negatives**, binding the
  latest parent integration boundaries with the changed Token;
  `.planning/debug/town545-drop-focused/output/run-ujy34lym`. Narrow-zone rejection and
  ordinary outside-zone rejection are independently falsified. This private focused run
  does not replace the integrator's full interaction suite.
- Strict Release build: **zero warnings/errors** after the final API/mark changes.
- Actual final545 Linux prefab envelope: **1,775 real Unity assertions**;
  `.planning/debug/town-service-workspace/run-8ixk1xrj`. All original four body clips,
  four samples per clip and all three body LODs are included. The measured actor envelopes
  remain inside reserved anatomy bounds; no old-bundle substitution was accepted.
  Bundle SHA256 before/after:
  `c35244d2df2ab86f33abbe0d5ba726ea33bacbdb6d83ad82a47c4de71f96a422`.
  Bundle source:
  `/home/claw/gvr-town545-faces/.planning/debug/town545-final-evidence-v2/town-review.bundle`.
- Repeated original room/native scene audit with the widened upper-bank reservation:
  `.planning/debug/town545-capacity-layout-final.json`. Both custom rooms and all four
  original map scenes have **zero contacts**, including all simultaneously opened banks
  and the unchanged 5 cm reservation padding. Input hashes match the preceding common
  layout audit. Upper closed backs stop at z+.226, ahead of the anatomy reservation z+.30;
  opening moves them further away from the NPC.

These gates establish source/geometry and native-callback boundaries, not headset comfort,
readability or the appearance of a live multiplayer hardware session.
