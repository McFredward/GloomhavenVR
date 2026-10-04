# Native scenario figure derivatives

Build 606 adds real topology reduction behind the existing player/enemy detail
percentages. This complements native LOD caps: Wind/Sun demons and Berserker have
no original coarser body, while the far Drake LOD remains expensive.

The bank covers 409 unique original non-cloth body/LOD meshes. It contains 1,191
usable derivatives in 49 independently loadable parts (294,017,137 bytes on disk;
largest part 7,280,055 bytes). The external immutable index maps a mesh signature
and tier to a part. Only the requested tier and the parts required by discovered
scenario actors load. Original 100% settings do not open the index or banks.

| Detail range | Target triangle fraction |
| --- | --- |
| 67–99% | 75% |
| 34–66% | 45% |
| 0–33% | 20% |
| 100% | Exact original mesh and LOD table |

These are targets, not promises to remove a fixed fraction: UV/material seams,
open boundaries, manifold topology, small closed limbs, bone-weight discontinuities
and face/error limits take precedence. Every surviving position, normal, tangent,
color, UV coordinate, bone influence and blendshape delta comes from its exact
original vertex. Bindposes, material slots and original conservative bounds remain.
Native Cloth meshes, weapons and small ornaments are excluded. Runtime code changes
only renderer mesh slots and existing native LOD references; bones, animations,
action/events and colliders remain untouched. Build 606's base mesh substitution
does not change skinning quality; the later optional influence cap below is separate.

Representative lowest-tier results:

| Actual native model | Vertices before / after | Triangles before / after |
| --- | --- | --- |
| Wind demon | 7,652 / 4,755 | 11,120 / 5,326 |
| Sun demon (`MO_NightDemon_Mesh` in the native Sun bundle) | 7,652 / 4,756 | 11,120 / 5,328 |
| Berserker | 10,325 / 5,875 | 15,738 / 6,838 |
| Far Spitting Drake LOD | 5,079 / 3,999 | 6,745 / 4,585 |

`ScenarioFigureMeshBank` prepares immutable derivatives during actor/loading/config
admission. Applying a cached detail or grabbing a figure never performs file I/O or
simplification. Existing local/remote home and highlight twins follow source mesh
changes and exact restoration. Bank meshes remain owned for the process lifetime;
restoring a setting never destroys a mesh still referenced by another visual twin.
The catalog, part-attempt set and source identity cache are bounded.

## Regeneration and verification

Licensed originals under `ressources/GH_Data` remain read-only. UnityPy exports
complete original streams into ignored local evidence; the separate Unity 2021.3.5
project builds Windows bundles compatible with the shipped game. Generated `.asset`
intermediates, raw exports and Unity Library files are ignored and regenerable.
`manifest.json` pins source bundle hashes and produced bank hashes/counts.

```bash
python3 scripts/generate-figure-meshes.py
python3 scripts/generate-figure-meshes.py --validate
python3 scripts/check-figure-mesh-runtime.py
python3 scripts/check-scenario-figure-detail-runtime.py
python3 scripts/check-scenario-figure-detail-budget.py
```

`--repack` rebuilds parts from existing generated assets; `--skip-extract` reuses
existing exports. The validator checked all 1,191 serialized derivatives with
87,818,972 survivor/channel/bindpose/index/material/boundary assertions. The runtime
fixture loads original Wind/Sun/Berserker/Drake prefab assets directly from their
licensed bundles without instantiating native gameplay controllers. It checks real
bank matching, unreadable Drake metadata, moving-bone skinning, preserved geometry,
local/remote ghost restoration, foreign-slot ownership, and saves original/coarse
Wind/Sun/Berserker silhouettes from real Unity graphics. Negative controls exercise
unsafe topology removal, lost weights, stale ghosts and foreign replacement. The
quality driver separately proves that a large native Cloth mesh and its coefficients
are never admitted, whether the solver is ON or OFF.

These are source and actual Unity runtime results. Headset appearance and frame-time
gains require hardware validation; no measured FPS improvement is claimed yet.

## Optional scenario distance banks

`python3 scripts/generate-distance-figure-meshes.py` appends separate scenario
far-distance parts and an index without rebuilding the existing 75/45/20 banks.
The original endpoint-derived banks remain byte-for-byte unchanged. Far bodies
use the MIT UnityMeshSimplifier SmartLink implementation vendored under
`UnityMeshSimplifier/` (pinned revision and license are included). This tool runs
only offline. Its 5% triangle target is a request; boundary constraints can retain
more geometry. `distance-manifest.json` records actual counts and hashes.

The current append has **403 derivatives in 17 parts (29,243,005 bytes)**. The
original 1,191 derivatives and their 49 parts remain unchanged. Representative
far triangle counts are Berserker 1,189 (old lowest 6,838), Sun demon 1,038 (5,328),
and far Spitting Drake 2,007 (4,585). A focused `--only` generation requires
`--no-package` to prevent a partial run overwriting complete numbered parts.

The three immersive map residents always use their exact original full-detail
meshes. Build616 removes their broken simplified variants, twelve indexed records
and distance parts17–28 (53,762,395 bytes). The NPC exporter and face/body hybrid
simplifier are removed. Reused extraction directories explicitly exclude old NPC
rows; original town bodies, eye/facial geometry, animation and materials remain
unchanged. The retained `Optimize/TownNpcDetailPercent` configuration key is inert
and omitted from the options catalog. Scenario quality controls remain supported.
Optional per-renderer skinning influence caps are independent of mesh selection.

`python3 scripts/check-figure-distance-runtime.py` compiles the actual scenario
LOD helpers, original NPC preservation and isolated skinning helper. The Unity
fixture uses original hero/drake/demon bodies and the three shipped resident
prefabs, asserting that NPC mesh/material/bone identity stays unchanged at all
distances and retired cap values, and that no NPC identity or derivative bank is
prepared. Real player-frame captures verify visible original bodies. Windows-only
shader previews use one common Linux Standard surrogate; this is geometry evidence,
not headset shader or frame-time parity. Scenario mesh substitutions and exact
restoration remain tested separately. Hardware logs establish performance gains.
