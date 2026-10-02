# Build 604: actual native figure scope, static cloth and complete decorative trees

## Hardware evidence

Both supplied Frame logs identify **ModBuild 603 / 5c60f6604**, not an older DLL.
Readings and immutable copied files/hashes are retained under the gitignored
`.planning/debug/frame604-hardware-evidence/` directory. No screenshots accompany
this drop. The active scene reports show a scenario run; startup's list of all
added scenes is not evidence that the maintainer visited the 3D campaign map.

`LogOutput.log` records the effective graphics choices: player/enemy detail 0%,
cloth false and all three scenery budgets 0%. Its three settled scenery summaries
inspect 9,077 unique meshes across four tiles, classifying 1,061 grass, 1,310
vegetation and 906 dressing meshes. All 3,277 are actually masked. That proves
work removal for admitted branches, not that all trees or grass vanished.

The decisive figure diagnostics are in **Player.log**, not LogOutput.log: lines
5862, 8461 and 8530 each report **zero actors, zero LOD caps and zero disabled cloth
solvers**. The debug sink difference must not be mistaken for absent execution.
The native figure models are parented to `ClientScenarioManager.m_Board`; the
geometry lives in the additive ProcGen scene. Build 603's root.scene == ProcGen
filter rejected the models on the native board in the separate Game scene.
The old fixture put both in one scene and could not detect this hardware defect.

Five post-build scenario windows at 3408 pixels per eye average 64.76–102.12 ms
per frame, with medians 54.81–83.13 ms. Logic averages 36.49–61.73 ms and the measured
render-loop span 13.83–21.61 ms. Views, options and reprojection rates vary, so
these are not a matched A/B or evidence of a memory leak. Initial loading is
excluded. XR's gpu statistic remains a frame interval/wait, not GPU busy time.
Neither supplied game log contains an Error/Fatal or exception-header entry.
The legacy FAN ORDER MIRROR line compares the drawn order with the original native
list; a difference alone is not a peer-image comparison, and this run has no peer
log. The owner-authored order stream remains in place.

## Corrected policies

- Admit original player/summon/monster models by native board ancestry and actor
  identity across scene boundaries; do not admit unrelated town models, map
  previews or UI. Original mesh caps also apply to held local/remote figures.
- Cloth OFF suspends secondary deformation, hand contact and rescale cooking,
  including held figures. The native animator and locomotion continue; clothing
  remains attached through original skinning. Turning ON restores owned native
  cloth state. Native temporary reset/teleport behavior is respected.
- Vegetation includes complete decorative native trees, including trunks and
  tree-pillar meshes, rather than treating their tree names as masonry. Dedicated
  original wall plant layers qualify independently of their solid parent.
- Suppress only owned picking colliders represented entirely by a hidden
  non-gameplay tree. Shared native wall/floor and gameplay obstacle colliders
  retain their original behavior; invisible hidden trees must not block a laser.
  Density restoration, scene exit and VR shutdown restore owned state.

These are configurable compromises on PC and Frame, not forced platform behavior.
Fresh defaults and persisted choices are unchanged. Scenario gameplay, essential
solid masonry/floors, actual obstacles, doors, lights and local/remote cards/UI
stay protected. The merchant, enchantress and priestess mesh rigs are outside the
scenario figure controls; no substitute town LOD is introduced.

## Validation and next hardware acceptance

Focused fixtures now reproduce a separate native Game board and ProcGen geometry,
completed native actor binding, locally/remotely held settings and cloth ownership,
plus original decorative tree collider and wall plant composites. Mutations must
fail these causal cases; a passing config-value test is insufficient.

Final integrated validation is recorded here once complete. The next headset run
must show nonzero actual actor/LOD/cloth counts for the eligible native models,
complete decorative tree removal at vegetation 0%, restoration at 100%, and no
secondary cloth reaction while held or on the board when OFF. Compare equivalent
views and unchanged 3408 eye targets. Coarser original meshes may already be in
use at distant views; that is not evidence of further vertex or frame-time savings.
Check doors/reveal/retry, picking and local/remote hands as well as both map modes.
