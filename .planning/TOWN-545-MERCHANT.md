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
