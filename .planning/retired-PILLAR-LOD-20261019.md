# Retired structural pillar LOD — history before removal

Recorded before changing the implementation, from shared dev `a3b7ed777` on
2026-10-19. The maintainer explicitly requests complete removal of pillar LOD,
its active settings and its generated distribution meshes on both desktop/Steam
Frame and Quest. Figure LOD and ordinary wall/terrain presentation remain.

## Original PC/Steam Frame implementation

`f9ae5656e` (2026-10-05) introduced private continuous3D terrain substitutes.
Immutable original meshes were exported read-only, reduced offline into100/50/0
tiers, and retained the original indexing for continuous morphs. A private inert
renderer temporarily substituted a source only inside a paired real head-camera
pass. Native gameplay, visibility, colliders and original cloning templates were
retained. Admission later covered10 audited pillar and15 wall identities from
the crypt/cave/city assets; the much larger general bank also supplies independent
floor and environment rendering, so that bank cannot simply be deleted.

The original Frame0/0 terrain caps reduced nearby columns excessively. The
follow-up chain was:

- `3242f36f7` (2026-10-09): replace world-AABB proximity with the original
  enclosing-cylinder radial distance, preventing orbit/yaw-dependent LOD.
- `aabf38273` (2026-10-09), Build659: preserve exact pillars throughout the
  configurable75cm viewing radius instead of only the18cm general touch guard.
- `8f7388a59` (2026-10-10), Build663: each pillar selects independently from its
  own original full cylinder radius and the unchanged figure distance policy
  bands8/7 or20/18. No live party/actor reference is used. A party-reference
  experiment was rejected before shipping; it is not the final implementation.
- `d13cc3dfe`: undefined/zero native height keeps original geometry instead of
  borrowing a rotation-dependent fallback radius.

`Optimize/ScenarioTerrainPillarDistanceLod` was default-on on all platforms.
Disabling it selected the older saved manual metre radius. The selected detail
could still be applied through the general terrain cap, with hand protection,
continuous morphs and native ownership checks. Larger detailed ranges can
increase GPU work; this was never proof of a performance improvement.

The original full pillar radii were1.703–1.925 source units; the audited figure
unions were1.127–1.702. The horizontal thickness alone was0.295–0.874, explaining
why that earlier metric reduced columns too soon. Native/runtime validation and
its limitations are preserved in:

- [Frame wall/radial review](FRAME-WALL-LOD-REVIEW.md).
- [Build659 near-radius review](FRAME-PILLAR-DISTANCE-REVIEW.md):2,669 Unity
  production assertions and94 causal controls, with inherited composed evidence.
- [Build663 independent policy review](FRAME-PILLAR-FIGURE-LOD-REVIEW.md):7,021
  final production assertions;106 composed variants, including an initially
  escaping toggle-coupling control subsequently repaired. These checks did not
  establish matching-headset appearance or an FPS gain.
- [Original terrain architecture](../docs/performance/FRAME-TERRAIN-PRESENTATION.md).

## Quest integration and observed failures

Quest reused the shared selector and included environment bank derivatives in
its independently prepared mod assets; original Campaign conversion remained
separate. The maintainer repeatedly could not observe pillar LOD despite later
improvements to figure LOD and general image quality. A missing IL2CPP uint-array
serializer had prevented environment bank admission; the Quest-specific explicit
reader restored the existing index/source identities without modifying the
original geometry or regenerating the Campaign.

B711 `quest-capture-20261019T160527Z-9c15535d.zip` showed startup, complete rooms
and the newer movie path. The maintainer then reported that columns affected by
LOD incorrectly disappeared under automatic wall fading although they obscured
nothing. Canonical WallFade materials could enroll detached terrain proxies as
a second unrelated wall, in addition to the legitimate native source.
`a3b7ed777` ports the exact private-renderer ownership ledger to shared dev;
Quest B712 carried the corresponding fix. Focused shared validation recorded
19,515 classifier and7,027 terrain assertions plus exact-ownership controls.
Those are software checks, not a hardware FPS or final-picture acceptance.

The maintainer also suspected a performance regression. The retained evidence
did not establish that pillar LOD caused an FPS regression. Size/radial selection
added bounded source-Update scalar work; private proxy preparation, source lease
checks, per-eye submission and retained original triangle counts can still trade
CPU and GPU cost. B712 added bounded actual CPU/triangle observations, but the
new capture was still pending when removal was requested. Removal is the user's
explicit product decision, not a claimed measurement result.

## Removal boundary

Pillars will retain their native geometry and the ordinary native wall-fade
authority. Remove pillar admission, distance-policy branches, active setting/UI
and pillar-only generated bank streams. Keep original PC game assets read-only,
figure LOD, ordinary wall detail/cheap shading and independent floor/environment
streams. Historical source, native audit identities, diagnostics, recipes and
prior proof remain recoverable from Git and retained capture evidence.

If the old configuration key remains bound for save-file compatibility, it must
be explicitly inert and hidden: no profile, UI or runtime branch may reactivate
pillar LOD. Distribution checks must inspect the actual rebuilt bank, not just
an edited index, and preserve non-pillar stream bytes and source provenance.
