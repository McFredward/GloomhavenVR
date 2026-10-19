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

## New B712 capture received during removal

The later `quest-capture-20261019T165519Z-f0b6a59c.zip` capture was bound by the integrator
to actual B712 while this removal was in progress. Its bounded terrain sample
reported68 prepared surfaces,56 classified as pillars and27 selected as reduced.
The last paired camera draw had15 surfaces,14 pillars, but only one reduced
pillar; original/submitted triangle totals28,676/28,582 saved94 triangles in
that draw. Across64 captured samples, terrain Update averaged0.289ms with1.541ms
maximum; pre-cull averaged1.043ms with1.639ms maximum. Prepared membership is not
actual per-eye savings. The last draw can reflect a paused state and cannot be
generalized into an entire-game cost or a Frame comparison. These observations
support measuring the full substitution path rather than crediting every
prepared reduced mesh as an active benefit; removal still follows the explicit
user decision.

## Implemented removal and focused evidence

The ordinary terrain allowlist now contains only its15 previously admitted wall
identities. Pillar surface classification, radial cylinder/radius measurement,
figure-policy bands and every pillar detail branch are removed. The old setting
is bound only as hidden `INERT —` storage; it has no runtime getter, curated row,
topic entry or profile write. Existing saved values cannot restore the feature.
The diagnostic token remains `pillarDistanceLOD=False` for log compatibility.
Figure LOD, ordinary wall detail, hand protection, native visibility and exact
gameplay meshes/colliders remain.

Delete261 reduced pillar50/0 streams and their metadata,14,396,885 raw bytes.
Keep all1,139 exact originals and2,909 total streams, including131 pillar-named
exact originals in the generic environment bank. Some of those exact originals
are shared by the independent environment chunk owner; the general exact bank
is not an exclusively pillar-LOD distribution asset.

The actual tracked `prebuilt/ghvr-environment.bundle` was independently repacked
with Unity2021.3.5f1. Its size drops from56,858,683 to52,361,903 bytes, saving
4,496,780 compressed bytes. New SHA256:
`1e95caed4ca1614e95b39f51e165d3ae3087b17fa8b562491df68cbdeee2609f`.
The inventory/hash checker verifies every remaining stream, exact index bytes,
both compiled shader families and all retained fog/instancing variants. Native
Unity loads the actual file and production-decodes all2,909 streams. This Null
graphics loader proves package/decoder compatibility, not headset appearance.

The actual main, town, voice, Steam Link and stereo-video banks contain no
environment-stream paths and retain their original bytes. The only tracked
desktop environment bank is the file above; Android environment banks are built
from owned inputs by the separate Quest Builder and must apply the same retirement
in its checkpointed producer. Original Campaign conversion need not change.

Fresh focused checks:

- Actual Unity terrain:370 assertions and four causal controls. All10 former
  native pillar identities remain original under zero caps and cheap shading;
  reintroducing one allowlist entry fails the real proxy-count assertion.
  Unchanged ordinary-wall hand proximity, distant cap and exact proxy ownership
  controls also fail their named assertions when deliberately broken.
- Profiles:328 source-bound assertions, including hidden compatibility storage
  and no profile reactivation. English/German help:2,314 production lookups and
  its existing content/lookup controls.
- Producer guards:9 assertions, including warm removal of stale50/0 files and
  metadata while preserving real exact100 bytes; an isolated former-generator
  control actually recreates the forbidden reductions.
- Strict Release:zero errors/warnings. Source group initially passed15/16;
  the expected retired-setting family exception was recorded explicitly and its
  affected options check then passed. This is composed source evidence, not a
  second new complete local gate.

The former7,027 terrain count intentionally falls because its removed radial,
size-band and per-pillar reduction permutations no longer describe product
behavior. Their historical evidence remains above; they are not silently counted
as current checks. The first new fixture build exposed an unused figure-helper
boundary and the initial native-loader check still expected the old3,170-stream
census; both test bindings were corrected and their failures retained. No new
PC/Frame/Quest picture or FPS result is claimed.

Detailed logs, source receipts, bank inventories and cleanup results live under
`/home/claw/quest3-local/build/evidence/quest713-loading-review-20261019/pillar-removal/`
and this worker's ignored `.planning/debug/quest713-removal/`. Owned child
processes are reaped before common completion cache cleanup. Current results,
source/hash evidence and hardware inputs remain retained.
