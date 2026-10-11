# Native material catalog tooling

This is a read-only developer census of the complete installed native content,
including Addressables DLC dependency closures. It exports metadata and hashes,
never game artwork, textures, meshes, shader payloads or executable game code.
Native references are resolved by source, serialized file, external file index
and object path ID. A path ID or material/shader name alone is never an identity.

Use an existing private interpreter with UnityPy; the current reference scan uses
`/home/claw/unitypy-venv/bin/python`, UnityPy 1.25.2. The normal checker needs only
Python's standard library. Game data stays read-only.

```sh
/home/claw/unitypy-venv/bin/python tools/material-audit/audit.py \
  --game-data ressources/GH_Data --output .planning/debug/material-catalog/native-scan
/home/claw/unitypy-venv/bin/python tools/material-audit/enrich_programs.py \
  --game-data ressources/GH_Data --native-output .planning/debug/material-catalog/native-scan
python3 tools/material-audit/compact.py \
  --native-output .planning/debug/material-catalog/native-scan --output tools/material-audit
python3 scripts/check-material-catalog.py \
  --native-output .planning/debug/material-catalog/native-scan --game-data ressources/GH_Data
/home/claw/unitypy-venv/bin/python tools/material-audit/verify_native.py \
  --game-data ressources/GH_Data --native-output .planning/debug/material-catalog/native-scan \
  --report .planning/debug/material-catalog/native-controls.json
```

`export_materials.py` can recover an incomplete property JSON export from an
already complete source census. It reparses every source containing a Material,
checks each original source hash and count, and preserves the failed export.
Explicit `+Infinity`, `-Infinity` and `NaN` strings represent actual non-finite
native floats; raw object hashes preserve their exact source bytes. Such
materials are excluded from simplification, rather than silently clamped.

The tracked compact catalog/index include all sources, materials, shader
families, property schemas/ranges, keywords, queues, variant hashes, exclusions
and coverage limitations. Complete saved property values and serialized-file
external tables remain in private audit evidence. The source census and program
signature enrichment are separate stages; differing compressed program blobs
do not prove different or equivalent shader semantics.

The catalog's candidates are an offline family/feature screen. Runtime world
provenance, native dynamics, MPBs, globals, clone/lease ownership and exact shader
equations remain independent checks. A catalog pass proves neither a correct
headset image nor an FPS gain.
