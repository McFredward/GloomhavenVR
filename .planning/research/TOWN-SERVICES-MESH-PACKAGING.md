# Offline town-service mesh packaging

Developer preparation only. These tools do not change runtime assets, game data, the mod's
Unity project or ModBuild. Provider selection and hardware acceptance remain separate.

## Preparation

Requires Blender 4.2 (tested with 4.2.22). The Python launcher does not need bpy installed:

```bash
python3 scripts/prepare-npc-assets.py \
  --blender /home/claw/blender-4.2/blender \
  --input /absolute/npc/model.glb \
  --output-dir /absolute/selected/merchant --name merchant
```

The output directory must be empty. Repeat with `priestess` and `enchantress` names in sibling
directories. `--yaw-degrees` optionally rotates around the imported Blender Z axis; glTF's
Y-up conversion is already performed by the importer. Height defaults to 1.75 metres, centre
X/Y is zero, and the lowest mesh point is at floor zero. Inspect the resulting pose: this
operation cannot distinguish soles from low-hanging generated accessories.

Outputs include byte-identical `raw/original.glb`, extracted original texture payloads,
normalized packed `source.blend`, normalized source GLB/FBX, and independent 80k/30k/10k
triangle LOD candidates in both formats. Each LOD is derived directly from the full source,
not from another LOD. Actual triangle counts, bounds, scale/translation, original material
records, file sizes and SHA-256 hashes are recorded in `manifest.json`. UVs, material slots
and imported surface normals are retained; decimation may alter local shading/UV interpolation
and needs visual review. No welding, smoothing, anatomy repair, remeshing or texture baking
is performed. Rigged or animated GLBs are rejected explicitly rather than silently losing data.

Base colour PNGs use sRGB. Normal and metallic/roughness PNGs are non-colour data. The
Unity metallic/smoothness PNG stores source blue * metallicFactor in red, and
1 - source green * roughnessFactor in alpha. It is saved without RGB gamma conversion,
and Unity imports it with sRGB disabled and alpha-as-transparency disabled.

`--roughness-floor 0.55` is an **optional Unity-only material study**, applying
`roughness = max(roughness, 0.55 * (1 - metallic))` to that derived mask. Its default is zero.
The original GLB, Blender material and derivative GLB material remain unchanged. Do not use
this study option for an accepted cross-format package without separately reviewing and
adopting the equivalent source-material treatment; it intentionally produces a recorded
Unity material proposal, not evidence of original appearance parity.

## Standalone Unity preview

Create a fresh temporary Unity 2021.3 project outside the mod's Unity asset project. Copy
`scripts/npc-unity-preview/Editor/NpcPreviewBuilder.cs` to its `Assets/Editor` directory, then:

```bash
xvfb-run -a /home/claw/unity-2021.3.5/Editor/Unity -batchmode -nographics \
  -projectPath /absolute/temporary-project \
  -executeMethod NpcPreviewBuilder.Build \
  -npcInputs /absolute/selected \
  -npcPackage /absolute/NpcPreview.unitypackage \
  -quit -logFile /absolute/npc-preview-build.log
```

On headless Linux use `xvfb-run` even with `-nographics`: Unity asset import workers still
require an X display.

The input root contains each NPC's preparation directory; a single preparation directory is
also accepted for smoke tests. The builder imports FBX meshes and PNGs, creates Standard
materials and a three-level static LODGroup prefab per NPC, places the NPCs side by side in
a lit scene, and exports `Assets/NpcPreview` as a `.unitypackage`. Full-resolution source FBX
is included for artist review; the scene uses the LOD candidates. Source `.blend`, raw GLB
and provenance stay in the outer preparation directory and should accompany any artist ZIP.
The builder refuses an existing `Assets/NpcPreview` folder or package output.

Standard materials are a preview approximation. Source GLB extensions, nondefault UV sets,
texture transforms and double-sided materials can require dedicated shader/material work.
The Hunyuan merchant has `KHR_materials_specular.specularColorFactor = [2,2,2]` and is
double-sided; its GLB export retains both, while the Standard preview warns about culling
and cannot reproduce that specular extension. Warnings in preparation manifests must be
reviewed before calling a Unity conversion appearance-equivalent. No normal green-channel
flip is applied: these are imported glTF tangent-space normal maps.

## Evidence

2026-09-20 worker smoke using the existing Hunyuan merchant input:

- Source: 499,470 triangles; LOD0 79,999; LOD1 30,000; LOD2 9,999.
- Normalized source bounds: Z min 0.0, max 1.75 metres.
- Base colour, normal and metallic/roughness PNG pixels match their embedded source images
  exactly (maximum absolute channel error zero).
- Unity metallic R and smoothness alpha matched bytewise ORM B and `255 - G` respectively
  (maximum absolute channel error zero, default factors 1 and roughness floor zero).
- Normalized source GLB material JSON retains the source specular extension and double-sided flag.
- Blender outputs are reproducibly generated offline. Unity import/build evidence is recorded
  separately after its smoke test; successful import alone is not a visual or VR acceptance.

Generated meshes are static artist starting points. No rigging, skinning, animation, facial
expression or headset quality claim is made. Finger silhouette, facial likeness, material
seams and LOD transitions still require artist and in-headset review.
