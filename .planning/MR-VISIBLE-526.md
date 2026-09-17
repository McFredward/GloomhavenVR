# Native header frames and visible MR extents — build 526

## Hardware and source evidence

Local logs identify build 525; remote logs remain historical build 500. The user confirms
merchant improvement but reports repeated temple openings growing the background downwards.
`kirche_hintergrund.jpg` shows a large empty rectangle below the original temple artwork.

The actual MR contributor is again Image 314858, `UI Adventure Header/Icon`. First temple
opening (local LogOutput.log:250) stays within y -540..540. On the second (272), the icon
reaches x -1464.662 and y -861.619. On the third (293), it reaches x -1702.454 and y -2817.243.
Later merchant openings also inherit this bad header state (324, 351). The measured artwork
itself remains 1920x1080. Thus build 525 did not fully solve the shared-header lifecycle.

Native temple entry reparents the header **before** the mod borrows it. The temple window
can already have been converted, or can be restored before the deferred header return.
Build 525 captured too late and replayed one parent's local coordinates into a different
parent's coordinate system. Its initial harness varied VR parents but assumed equivalent
native parent frames; those tests did not model this ordering.

## Source repair

Observe the original header's native pose before destination conversion and retain the
canonical TRS relative to its original native parent. A read-only resolver reconstructs the
native frames behind converted targets from their conversion snapshots. Borrow and return
express the canonical pose through the chosen parent's native frame, independent of whether
that window is currently converted or already restored. No template clone, gameplay callback
or new animation is introduced. Native layout, root header configuration, child contents and
native parent/sibling choices remain owned by the game.

## General MR visibility contract

The audit also found independent holes in the common MR measurement: renderer-owned alpha
was missing, and graphics outside a supersampled window's actual displayed footprint could
enlarge an opaque plate despite being absent from its rendered image.

MR bounds now consider renderer alpha and clip **individual contributors before unioning**
them against the displayed capture footprint. Intersecting an already enlarged union would
still retain empty space from a fully cropped contributor. No host-size fallback or arbitrary
direction/size ceiling replaces visible content; uncaptured native UI and direct remote
clones remain free to draw legitimate overflow.

Materialisation must retain pre-effect native alpha and per-graphic geometry, including
temporarily unavailable meshes. Its backing follows the existing window progress rather
than shrinking to only the elements currently materialised. Raw snapshot pieces retain
native masking but are clipped against the current capture footprint, so later capture
availability/changes cannot freeze an unbounded initial plate.

A modal additionally watches its four measured extremal graphics and actual capture footprint.
A disappearing edge triggers an MR-only resample and one prompt independent confirmation;
it does not wait for the grab bar's sixty-frame verification cadence. Normal unchanged frames
do not walk the complete graphic tree, and native/grab geometry keeps its existing policy.

## Validation and hardware limits

Integrated validation:

- Native banner frames/borrows: 314 runtime assertions, seven integration bindings, seven
  runtime mutations and one binding negative. Tests include temple-first, twelve repeated
  temple openings, merchant afterwards, distinct native parent frames, both restoration
  orders, native layout updates, replacement and destroyed parents.
- Native ink/paint/capture and modal sample watch: 447 runtime assertions, 28 rejected runtime
  mutations and one placement binding negative.
- MR layout/accessor: 258 runtime assertions, 52 integration bindings, thirteen rejected runtime
  mutations and three binding negatives.
- Actual materialisation lifecycle: 552 runtime assertions, three bindings and five negatives.
- Strict Release: zero warnings/errors. Frame-order, source invariants, bilingual docs, shell
  syntax and whitespace pass. Config/patch/log census unchanged: 625 / 172 / 4,733.
- The broad refactor wrapper was stopped after source checks when it entered unrelated card
  harnesses; this round claims focused validation, not a full-suite or compiled-baseline pass.

CPU bounds remain conservative for arbitrary custom Graphics, sliced/tiled images and radial
fills. They do not inspect texture-internal transparent pixels or arbitrary shader silhouettes.
The guards close the evidenced native transform, renderer-alpha and displayed-crop defects;
these tests are not a pixel-opacity proof for every possible shader.

On headset, start with temple as the first destination, reopen it repeatedly, switch to
merchant and back, and close/reopen quickly. Check other map windows, all four background
edges, real content that legitimately extends beyond a frame, and the materialisation/dust
transition. A current matching peer is needed to establish the remote outcome. Automated
tests establish their modeled cases and compilation, not every possible headset image.
