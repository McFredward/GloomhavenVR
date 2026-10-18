# Native MR quality with only game and Frame changes

The maintainer's boundary is explicit: change the game/mod and Frame receiver,
never inject into or patch PC Steam, vrcompositor, vrlink or its encoder. A
separate SteamVR display driver is not a quiet substitute for that requirement.
No new device action is authorized by this investigation.

## Actual707 result

The newest Player.log identifies Build707. Both explicit attempts report an
ordinary2048-square world quad, width0.6m/aspect1, local visibility1 and
dashboard visibility0. The maintainer sees no test surface. Local setup is
therefore established; remote delivery is not. Do not repeat the same overlay
preview, blank the scene or claim an independent-alpha path on that evidence.

## Why the current image is softer

The v3 logical canvas is3168-square; useful RGB and useful coverage are each
2048-square. RGB41.79%, useful mask41.79%, and halo/guard/remaining layout16.42%.
The physical mask rectangles with halos occupy45.10%, already included in that
accounting. RGB keeps64.65% of each axis:35.35% fewer samples per axis or58.21%
fewer useful RGB samples than a full-area **equally sized uniform** carrier.
This is layout arithmetic, not a percentage of perceived quality or an exact
comparison with MR-Off's different foveated sample distribution.

## Preferred next route

Keep ordinary Steam Link RGB/foveation and send exact same-generation coverage
through an application-owned authenticated channel. A small source token must
be recovered from the **selected decoded image**, not the newest message or an
approximate timestamp. A candidate token footprint may replace only a proved
zero-contribution **and** zero-coverage patch; A0 additive RGB is valid visible
content. No fixed large RGB rectangle or silent source-resolution clamp.

Identity and spatial registration are separate obligations. The Frame must
apply exactly the color's current source mapping to coverage, including PC
composition/prewarp and native reconstruction. A token does not prove that
mapping or exclude a mixed-generation/overlaid body. The existing R1 algebra
and static resource handles must not be promoted into those missing contracts.
Missing, ambiguous or late association retains opaque game output without a
render-thread wait. Repeated frames retain their own immutable GPU mask lease.

The current MRA1 helpers provide bounded R8 transport/selection foundations,
not a live adapter, authentication or sufficient limits for5050/6060 sources.
Their4096-axis/16MiB limits require an explicit negotiated redesign or refusal,
not shrinking the player's image. A direct app-token/selected-image association
must not invent the private Steam TX key required by the older native ledger.

## Next local proof and fallback

Before another headset request, build a local source/token/codec/receiver
prototype: immutable real E/A inputs, unchanged visible RGB, real codec output,
exact mask joins under reordering/reuse/drop/reconnect, and actual GPU composition
under explicitly known mappings. Wrong body, unknown mapping or composition
must remain opaque. These fixtures must continue to label actual Steam Link
mapping/composition authority false; a simulator is not a proprietary-sender proof.

If that mapping cannot be established without a sender hook, stop this branch.
A complete mod-owned color+coverage stream to an owned Frame renderer is the
controlled fallback. It keeps PC Steam untouched but adds encoder/decoder,
tracking/input/pose-space, reprojection and local-menu coexistence work. A
compact in-band mask is only a budget compromise, not a full-quality guarantee.

The canonical native developer reviews are
`docs/research/app-only-public-api-feasibility-20261018.md` and
`docs/research/app-only-receiver-feasibility-20261018.md` in
`steam-link-native-passthrough`. They retain primary references and source
boundaries. This round changes no game/runtime/Frame behavior and creates no
new test build; research and the bounded CI-fixture repair are recorded separately.

## Bounded707 CI repair

The707 hosted source check and three runtime shards pass; the projection-owner
fixture fails in the fourth. Its old runner suppressed the inner exception, so
the original CI exception is not retrospectively proved. Source review finds
a real fixture scheduler race: a terminal phase is published immediately before
the worker Task completes. Polling only that phase is not permission for an
immediate new Start; the production owner correctly refuses overlapping work.

The repaired fixture awaits the actual existing `Owner.Settled` task. A private
instrumented-source control pauses exactly after terminal phase publication:
five boundary assertions pass, while the old phase-only wait fails at the
intended assertion. The unchanged production owner/UI passes65 checks; two
bounded-error-tail checks retain the future inner failure message in CI. No
timeouts are broadly increased and no product file is changed. Callback,
typecheck and prior mutation evidence is inherited, not rerun or relabelled as
a full gate. The instrumented five-assertion control is explicitly distinct
from the actual uninstrumented production fixture.
