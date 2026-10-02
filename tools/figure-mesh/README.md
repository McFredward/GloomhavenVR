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
action/events, colliders, gameplay and global hand skin quality remain untouched.

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
