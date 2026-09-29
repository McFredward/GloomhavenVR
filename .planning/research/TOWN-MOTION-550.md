# NPC motion correction — build 550

Evidence: all five `debug/npc_probleme/VirtualDesktop.Android-20260924-*.jpg`
images and the accompanying ModBuild 549 banner. The previous imported-skin gates
established contact and continuity limits, not perceived naturalness. The new
hardware report supersedes the earlier motion-quality assumptions.

## Causes established in source

- Prayer deliberately placed the actual palm surfaces 70 mm apart at face height.
  The targets were not wrist centres; the gap in screenshot `131903` follows
  directly from those values.
- The planted-leg solver took its bend direction from a generated knee close to
  full extension. Small source rotations changed the knee plane even though the
  final ankle marker remained stationary. The previous foot-drift assertion could
  not detect this. Additionally, the solver stopped running at exactly zero work
  weight, introducing a last-frame stance change when greeting finished.
- The elbow guide clamped practically the entire recorded lateral and forward
  excursion to fixed minima. The generated torso still moved, but the solver
  erased two coordinates of its corresponding elbow motion.
- The enchantress's broad recorded performance raised the shaping hand as high as
  1.56 m and paired it with up to 195 degrees of hand pronation. Retiming slowed the
  same unsuitable pose; it did not change its exaggerated two-palm silhouette.
  Separately, raising the supporting palm faded its level orientation reference,
  so an authored 180-degree roll still produced a partly vertical palm.

## Revision

Keep the recorded whole-body performance and its shared interruption clock. Fit
its hand trajectory to the work volume above the book: a supporting palm carries
the spell, while the opposite hand shapes it with a smaller turn. Retarget elbows
with the hands and preserve their recorded lateral/forward motion outside the
measured coat clearance. The supporting palm keeps its level orientation reference
through the raised casting phase. Existing spell
intensity and coin-contact mechanics remain on the same timeline. No model API
or paid generation was used.

Plant the legs through a stable anatomical bend plane with a small flexion
reserve, including the completely attentive pose. Hip motion still transfers
weight across the planted feet. Bring the priestess's cupped hands together below her face with aligned fingers
and adducted thumbs. Their contact allowance follows actual skin thickness. Move
the elbow approach forward to clear the robe during greetings and reversals.

The activity diagnostic renderer now binds the real practical-light registry;
its older direct Unity light could only produce silhouettes after build 549's
shader change. This is a fixture correction, not a runtime lighting change.

## Validation

- Final imported-rig production run: **291,765 assertions and 22 detected mutation
  controls**, including actual casting-palm orientation, prayer separation, knee
  plane stability, the last greeting frame, original contact, shared clocks and
  intermediate observer playback. Evidence:
  `/tmp/town550-motion-final/run-7ev5pl0x/results.txt`.
- At 90 Hz, maximum knee displacement per frame is 0.652 mm (merchant), 0.421 mm
  (priestess) and 1.277 mm (enchantress), including approach/departure. Actual foot
  drift remains below 0.001 mm. These measurements replace the earlier ankle-only
  assertion that missed knee twitching.
- Complete actual-skin triangle checks cover **1,943 poses with zero arm/torso or
  opposite-arm intersections**: 794 merchant, 546 priestess and 603 enchantress.
  All six injected penetration/detached-seam controls are detected. Maximum seam
  growth: 0.123 mm, 3.436 mm and 0.302 mm respectively. Evidence:
  `/tmp/town550-arm-final-proof.json`; geometry: `/tmp/town550-anatomy-final`.
  Each resident's unchanged final branch retains its already exported geometry;
  `provenance.json` identifies its actual runtime export and hashes. Targeted
  `--anatomy-service` / `--service` fitting avoids repeatedly exporting unchanged
  residents. Default complete checks still require all three.
- Rendered review uses actual imported skin and the final fitted furniture bundle.
  Merchant work/offer: `/tmp/town550-renders-v12/service1-phase{0,2}-view0.png`;
  prayer: `/tmp/town550-renders-v15/service2-phase0-view0.png`; casting:
  `/tmp/town550-final-spell-frames/service3-phase{21,28}-view0.png`.
  Full 30-second performances at 8 fps are
  `/tmp/town550-merchant-motion.mp4` and
  `/tmp/town550-enchantress-motion-final.mp4`. Continuous pose limits are checked
  at 90 Hz separately; the videos are visual review evidence, not a headset FPS
  measurement. The diagnostic scene does not reproduce the complete game map.

No actor geometry, likeness, room proportions, ordinary logging, paid generation
or network record layout changed. Hardware review remains necessary for perceived
naturalness, stereo detail and the interaction with the full game environment.
