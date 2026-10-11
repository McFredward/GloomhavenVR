# Articulated merchant cabinet — build 551

## Hardware request and implementation

Build 550's cabinet screenshot (`VirtualDesktop.Android-20260924-145513.jpg`) shows
category buttons but no indication that additional stock pages exist. The maintainer
approved category changes, but requested vertical scrolling with physically folding
card holders instead of repeating the same shutter animation.

The original cabinet keeps its category-change cassette/shutter. Stick and crank page
changes now circulate its three actual four-card holder rows around the upper/lower lips.
The original leather seats, brass clips, card faces, backs and native price strips travel
together. A short 100 mm rearward lead-in clears the existing front fascia; the shutter
stays open. All rows face backward at half a revolution, so page identities change behind
the opaque holder backing. They unfold toward the viewer and settle into exactly the
original slots. Held, returning or offered merchandise continues to prevent a page turn.

An always-visible numeric `↑ / current page / total pages / ↓` indicator beside the crank
makes additional pages discoverable without aiming a laser. It uses the original game font,
contains no new interactive UI buttons and hides for single-page stock. Its observer value
tracks the displayed physical clock, including delayed dependencies.

## Multiplayer and compatibility

Additive TLV 87 carries version 1, signed scroll direction (-1/0/+1), and an unsigned
16-bit page count (1–256). Direction zero retains the original category shutter. Direction
must be explicit: a two-page wrap has identical source/destination pages whether the stick
moves up or down. Historical TLVs 78, 85 and 86 retain their exact bytes, and protocol stays 3.

Owner and observers sample the same production curve. Existing causal card membership,
prewarming, detached-card priority, loss recovery and authority handoff stay in charge.
Late joining observers reconstruct intermediate positions and hinge angles; no viewer-facing
rotation is introduced. The matching articulated `Row0/Row1/Row2` town bundle is required.

## Validation

- Actual Unity catalog/input suite: 22,836 assertions and 27 detected negative controls.
  Evidence: `/tmp/town551-cabinet-importcatalog/run-iglnmbd1`.
- Complete original-widget mirror suite: 230,589 assertions and 17 detected controls,
  unchanged visual tolerances. Evidence: `/tmp/town551-import-fullmirror/run-95o634ts`.
- Public cabinet suite: 1,334 assertions and nine detected controls, including both
  directions at 12%, 50%, and 82% elapsed, each original card corner and delayed-clock
  page labels. Evidence: `/tmp/town551-import-publicmirror/run-7h96bsoe`.
- Golden wire executable: 286,120 assertions, including independent TLV 87 byte vectors,
  duplicate/malformed direction/count rejection, and unchanged historical payloads.
- Strict Release build: zero warnings/errors.

`check-town-service-catalog.py` exports the actual production Unity trajectory as
`roller-geometry.csv`. `check-town-cabinet-roll.py` imports the real furniture and articulated
FBXs in Blender and checks 1,206 row poses against the cabinet, stationary cassette sides,
and each other. An injected uncleared trajectory must intersect the original fascia.
This audit found backing/side and original guide-rail intersections. The final shelves
are .678 m wide and .138 m high; original guide rails moved from ±.347 m to ±.370 m.
Final regenerated FBXs pass all 1,206 sampled row poses with zero cabinet, stationary-side,
or row-to-row surface intersections; the uncleared-path negative is detected.
Evidence: `/tmp/town551-cabinet-normalized-clearance.json` and its adjacent log.

### Actual Unity import correction

The combined source-asset review caught a real factory defect that the Blender world-geometry
audit could not reveal: Unity preserves FBX empty nodes with a -90-degree X rotation and
100x scale. Directly animating those nodes erased their import rotation, and parenting native
cards under them would have magnified the cards. The production factory now creates fresh
metre-space `Row0/Row1/Row2` pivots, preserves the imported nodes and geometry beneath
`ImportedHolder`, and attaches cards to the normalized pivots. No support assertion was weakened.
The catalog fixture now injects the actual importer transform conventions and checks both
unit-space parenting and unchanged imported geometry.

The original failed Unity project was resumed with only the compiled production factory fix,
using `ValidateTownAssets.ReviewSources`. All original corner-support assertions and nine
visual negative controls pass: **951,242 assertions**. Evidence is
`/tmp/town551-assets-fixed/town-assets-likhscsh/evidence-normalized/`; its
`resume-evidence.json` records exact compiled source hashes, original failure, fix commit and
scope. The original manifest remains historical; this resumed source review did not rebuild
a Linux bundle. The Windows asset bundle needs no change for this runtime factory correction.

The publication review also repaired two independently verified omissions: original palm
confirmation parts now publish while only merchant inspection is active, and the native
enhancement price tooltip publishes as its own registered boundary in the new folio layout.
The final mirror assertion totals above include these routing checks.

These checks establish code, wire and geometric behavior. They do not establish perceived
motion quality, typography in a headset, or real multiplayer transport timing.
