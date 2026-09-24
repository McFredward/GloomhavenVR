# Offered-card enhancement-point coverage

`check-town-card-slots.py` compiles the entire production
`TownServiceCardSlots.cs` and the production `RemoteWidgetMirror.Neutralize`
implementation against real Unity 2021.3.5/uGUI assemblies. It runs one Editor
project containing the positive fixture and separately compiled negative controls.
The source checkout is read-only and source hashes accompany the result.

The native card/handoff selection is a boundary supplying actual Unity transforms
and per-point Images. The mirror boundary records source ownership and update
calls and gives the helper a small live image clone; it deliberately uses the
real neutralizer before activating that clone. This tests the new helper's
selection, lifetime and animation cadence, plus callback/input suppression.
It does not substitute for the full native mirror, fitting, renderer or transport
suites and does not establish headset readability or pixel parity.

Cases cover 0/1/2/9/24 enhancement points without a capacity assumption, mixed
occupied/available states, native-point replacement with unchanged count, live
upgrade/removal between structural refreshes, new pulse descendants, temporary
null/pooling states, card reclaim, repeated offerings, missing selection and
idempotent disposal. Clones must have neither colliders nor native Button or
controller components, and their CanvasGroup must reject raycasts and interaction.

Seven intentional compiled defects exercise stale pooled identity, stale card
reclaim, missing live animation, missing structural refresh, partial pooled
content, leaked mirrors and a raycast-blocking clone. Compilation errors never
count as detected defects. Normal teardown and hierarchy copying do not invoke
native transaction callbacks.
