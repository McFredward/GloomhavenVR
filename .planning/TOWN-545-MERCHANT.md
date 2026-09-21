# Physical merchant — build 545

This replaces the build544 six-card counter and page/filter buttons. The complete native
buy catalogue and the selected owner's sell inventory coexist as physical item cards.
Native categories fill real pull-out filing drawers:48 persistent cards per drawer,
six columns and eight separately exposed strips. Overflow creates another drawer; it
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

Two banks have centres x±.68m and1.04m drawers. Fixed opaque housing encloses closed cards.
Drawer levels start .15m below the counter, separated by .13m. Six drawers fit each bank
above the floor. The supplied base+DLC catalogue has164 unique definitions; native grouping
puts Hands54 into two drawers and the other categories into one each. Tests include all
164buy and164owned entries. More rows are never capped, but a larger custom catalogue's
additional vertical bank envelope needs placement validation before claiming compatibility.

Travel is .78m: a .48m pull left back cards under the widened counter. The resulting front
edge is about z−.815m, bank housing |x|≤1.213m. New counter top half-width1.27m and extra-user
workspace placement need a geometry review against the environment and neighbouring stands.
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
Cellar and forest use separately measured six-station arrangements. Custom-room rotation,
not the current reading-side yaw, defines their frame. Default/MR uses the cellar arrangement
in the common reading frame. Ground support registration includes the new centre/outer posts.

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
Native Guildmaster bench/barrel geometry is outside that custom-environment bundle and
still requires its separate scene check. The final build545 furniture/character bundle
must repeat the focused geometry checks. None of these checks establishes headset quality.
