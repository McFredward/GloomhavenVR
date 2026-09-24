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

- Actual Unity catalog/input suite: 22,830 assertions and 27 detected negative controls.
  Evidence: `/tmp/town551-cabinet-finalcatalog2/run-bn8jc9fi`.
- Complete original-widget mirror suite: 230,588 assertions and 17 detected controls,
  unchanged visual tolerances. Evidence: `/tmp/town551-cabinet-fullmirror/run-00jfudho`.
- Public cabinet suite additionally checks both directions at 12%, 50%, and 82% elapsed,
  each original card corner, delayed-clock page labels and nine negative controls.
- Golden wire executable: 286,120 assertions, including independent TLV 87 byte vectors,
  duplicate/malformed direction/count rejection, and unchanged historical payloads.
- Strict Release build: zero warnings/errors.

`check-town-service-catalog.py` exports the actual production Unity trajectory as
`roller-geometry.csv`. `check-town-cabinet-roll.py` imports the real furniture and articulated
FBXs in Blender and checks 1,206 row poses against the cabinet, stationary cassette sides,
and each other. An injected uncleared trajectory must intersect the original fascia.
This audit found backing/side and original guide-rail intersections which required fitted
asset changes; final geometry evidence is recorded by the integrator after asset regeneration.

These checks establish code, wire and geometric behavior. They do not establish perceived
motion quality, typography in a headset, or real multiplayer transport timing.
