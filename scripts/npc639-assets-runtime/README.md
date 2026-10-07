# Native town-service atlas resolution proof

Run the focused proof from a prepared worktree:

```sh
python3 scripts/check-npc639-assets.py --controls
```

`--case production` or `--controls --case <control-name>` runs a bounded subset.
The fixture needs Unity 2021.3.5, .NET, Xvfb, UnityPy and the read-only original
`ressources/GH_Data` reference. `UNITY_PATH` and `UNITYPY_PYTHON` override their
usual local paths. Evidence belongs in the main checkout's ignored debug directory;
use `--output-dir` when running from a worker worktree.

The exporter retains the original serialized bytes of all 895 members of
`resources.assets`' `BattleOverlayCanvas` atlas (path ID 10304), its Texture2D
(206), and its compressed texture stream. It creates only a test AssetBundle
container with aliases for the original path IDs. Every original object is hashed
before and after export. No sprite is reimported, repacked or recreated.

The original Texture2D keeps its external `resources.assets.resS` path. Unity's
async GPU upload needs the exact stream companion at the test project root; merely
putting that stream inside the generated bundle is insufficient. The exporter
writes the original prefix covering this texture, and the runner links it into
the project. This bank and its companion are test evidence, never release assets.

The proof uses real Unity play mode. It first captures every materialized original
with the production registry, unloads them, starts the receiver's ordinary scan,
then loads the atlas while the ordinary two-second scan throttle is still active.
The cold receiver has zero materialized atlas sprites. It must independently
prepare and resolve all original members, preserving native rect, pivot, border,
pixels per unit, vertices, triangles and packed UVs. It also checks exact clone
identity, unknown-artwork rejection and clone disposal without destroying originals.

The controls use the exact old Build 638 source, remove atlas enumeration, prevent
normalization of Unity's `GetSprites` clone suffix, or remove native geometry from
the asset identity. Each must fail at its corresponding real source boundary.
Native geometry matters because ten original pairs have identical names and
logical sprite geometry but different packed meshes/UVs. The fixture rejects a
logical-name collision choosing whichever member happened to load first.

This proof covers descriptor resolution, geometry and lifetime, including a cold
resolution measurement. It does not prove final GPU pixels, transport deadlines,
headset presentation, existing mip-bake provenance, or mod shader loading. The
separate NPC first-picture proof must exercise complete native modules, actual
rendering and the packet path. A metadata-only pass cannot replace that evidence.

The runner loads the editor's actual TMP Essential Resources before the measured
atlas census. These package Settings are not the game's TMP Settings and do not
replace any native atlas artwork. Loading the shipped Settings' exact fallback
chain into BattleOverlayCanvas is proven separately by read-only inspection of
`globalgamemanagers` ResourceManager dependencies; this bank does not contain or
exercise that shipped Settings resource itself.
