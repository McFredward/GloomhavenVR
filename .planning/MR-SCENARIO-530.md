# Scenario MR terrain isolation — build 530

## Evidence and limits

The 2026-09-18 local hardware log identifies ModBuild 529. Remote evidence is still
build 500 and does not establish current multiplayer behavior. Inspected both supplied
images, `mixed_reality_block.jpg` and `mixed_reality_block_vergleich.jpg`: MR adds dark
planar patches among the revealed forest cliff dressing; ordinary rendering retains the
foliage silhouettes. The screenshots do not identify a Unity renderer by instance ID.

Two source defects exist in the affected rendering paths:

1. `MixedReality.RegionMembershipPass` accepted any mesh within a fog tile's bounding
   region below its top. It then force-filled every material slot with a textureless dark
   material and generated an additional box/hex prism. Hanging cutout foliage satisfies
   those spatial constraints too; its transparent card-shaped mesh becomes a dark rectangle.
   The native shader's transparency is irrelevant to the old forced path. This explains a
   plausible exact shape mechanism, but the supplied log does not name the pictured renderer.
2. `WallSegmentFade` treated only names beginning `VR` as mod-owned. Its comment incorrectly
   said that this also covered `GloomhavenVR.`. LogOutput line 252 actually reports two
   `GloomhavenVR.MrBacking` renderers adopted as orphaned wall architecture; line 250 also
   counts `MrUnseenUnderlay`, `MrUnseenFill`, and `MrUnseenRim` as ordinary scenery. This
   ownership failure is directly observed, independent of the screenshot attribution.

## Changes

- Keep the original unseen-family material path and ordinary UI backing geometry.
- Restrict the supplemental spatial fallback to its documented native `Simple Tile`
  cliff block (including Unity's clone suffix), and reject alpha-cutout materials. No
  revealed forest dressing or arbitrary neighboring native geometry can use that route.
  Existing geometry/figure/size rails still apply; the helper is not a new standalone
  backing registration path.
- Seed supplemental regions from active, enabled family sources only. Recheck existing
  supplemental backings on the existing scan cadence and remove them if their family
  host has disappeared, stopped qualifying, or the feature has been disabled.
- Share the union of both mod naming conventions between the wall snapshot and live
  adoption checks. Native renderer layer/material/visibility remains untouched.
- Update the already bounded Debug census to distinguish deliberately authored neighbors
  from unexplained missing fog backings. No new normal-level diagnostic stream.

## Validation

`scripts/mr-scenario-tests.sh` links the actual ownership and eligibility helpers:
31 production assertions, 10 source bindings, three negative controls. Negative controls
restore the missing qualified prefix, broad overlap adoption, and cutout acceptance;
each fails for the intended runtime assertion. Strict Release: zero warnings/errors.

Hardware still needs to confirm the photographed patches disappear and that legitimate
unseen hex tops/rims remain filled without a passthrough tint. Toggle MR repeatedly,
reveal a neighboring room, and inspect normal floating window backings as well. No claim
of a headset-verified result follows from the automated checks.
