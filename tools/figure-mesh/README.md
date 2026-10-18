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

### Quest owned-bank delivery

The Quest builder uses `QuestFigureMeshGenerator` and `figure_banks.py` to recreate
the same 75/45/20% endpoint derivatives and 5% SmartLink distance surfaces as Android
AssetBundles. It reads only the owner's native PC figure bundles. Game-free builder
archives contain this recipe and the MIT simplifier sources/license, never original
mesh streams, generated vertices, or the desktop prebuilt banks.

The producer key contains the original bundle hashes, exact geometry producers,
Unity version/target and the runtime mesh-key function. Unrelated mod, host UI and
diagnostic edits do not regenerate geometry. Each original bundle, derivative source
and Android bank part has a durable completion boundary. A damaged mutable Unity
mesh input is restored from its exact cached source; a retry retains completed parts.
The complete Campaign bank is neither imported nor converted by this operation.

Full builds stage independent StreamingAssets members; compact updates stream these
same files into `assets/QuestFigureMeshes/` with a small signed manifest. The startup
helper installs them into the existing resource directory before plugin activation.
Cold copying uses the existing delivery view and byte progress. Warm launches consult
bounded private per-part receipts and file sizes, without rereading all bank bytes.
The runtime loader, native LOD tables and on-demand bank loading stay unchanged.

Focused qualification: `scripts/quest-figure-banks-tests.py` reads two licensed
hero/enemy bundles, checks all four tiers and actual Android serialization, compares
the real runtime key across NativeFormat import including read/write OFF, repairs
an edited Unity asset and forbids exporter/compiler calls on a completed retry.
The private Android project must enable `com.unity.modules.assetbundle` as well
as JSON serialization. Without it, Unity 2021.3.5 can emit readable Mesh data but
omit the native AssetBundle container; the Quest then reports a misleading newer
runtime incompatibility. Compiler modules are pinned in the bank recipe. This
module-only repair retains matching previous native exports and simplified meshes,
then rebuilds only their Android containers. Finished bank sets are checked for an
exact Android/Unity container-to-index mapping once; warm receipts stay cheap.
The focused producer test also loads every part through Unity's native bundle
loader. `--broken-container PATH` adds a known Mesh-only native-loader control.
`scripts/quest-figure-content-tests.py` checks exact file/ZIP/hash installation,
warm reuse, cancellation, targeted updates and index-last publication with causal
controls. These checks do not establish whole-game generation time, Windows tool
execution, headset silhouettes or frame-rate improvements.
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
