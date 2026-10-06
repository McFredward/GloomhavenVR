# Build636 material integration after NPC635

The separately audited material lane `a13de4883`, based on pushed634, is merged
after NPC635. All original native enchantress overlay repairs remain unchanged.
The native world material option is shared by the PC and Frame binary; it does
not introduce a second rendering environment or alter gameplay authority.

`[Optimize] WorldMaterialQualityModeCount` has three live stages:0 retains this
owner's originals plus existing individual floor/wall options,1 keeps original
color textures with simple lighting,2 keeps textured color without surface
lighting. Fresh Frame and Standalone defaults are2, PC defaults0. Saved values
persist. Stages1/2 take precedence over the old floor/wall shading options.
Normal/gloss/reflection and received lighting detail are deliberate compromises.
Original meshes, transforms, UV/color textures, native room reveal/fades and
collision remain. Unsupported, animated, interactive, actor, card and UI sources
keep original materials. Native mesh/slot/component/MPB changes revoke unsafe
variants and existing terrain/chunk/instance consumers before rendering.

The inventory covers3286 sources including all3255 catalogued bundles,9197
materials and156 families, with JoTL/Solo dependency closures. Exact shader
program intersections conservatively constrain supported states; identical
shader names do not authorize unknown compiled keyword combinations. Explicit
null references and malformed metadata remain recorded. Inventory counts do not
describe visible draws or an FPS gain. Details are in
[the material catalog](MATERIAL-CATALOG.md),
[the shader contracts](WORLD-MATERIAL-SHADERS.md) and
[the runtime review](WORLD-MATERIAL-RUNTIME.md).

## Review follow-ups

Integration review identified unnecessary Off-path work: material passes still
allocated scopes/cleared caches, native write hooks still read slot arrays without
any owned variant, and scene-load events seeded an inactive owner. Settled Off
now takes a shared empty pass, avoids those reads and skips discovery. Existing
owned references retain transition/consumer reads until restored.

An additive scene unload cleared every candidate, including surviving scenery,
without changing the quality mode. Settings then skipped rediscovery. Active
unload now removes invalid registered roots and seeds the surviving world again;
Off/failure/VR-stop paths do not seed. The focused runtime follow-up records
engine-event and causal evidence separately from inherited passing cases.
It passes543 assertions and four new causal controls, including real Unity
additive scene load/unload with surviving scenery rendered after recovery.
Test-scene cleanup failures are retained and were not counted as passing controls.
The descriptive LOW UV contract now names its actual `_MainTex_ST` transform;
that wording correction changes no shader, native source or generated asset.

The review confirms native provenance exclusions and conditional reverse-map
restoration. Shader review independently verifies the shipped bundle's ordinary
and stereo-instancing compiled variants. Current VR remains MultiPass. There is
no new NPC, Net, input, OpenXR or gameplay controller change.

An additional legacy offered-card render comparison failed because it compared
an already-settled owner image against an observer still interpolating its last
synthetic sample:120ms elapsed against a143ms rotation duration. The exact latest
target was already received; the observer was at27.51 degrees of the owner's31.
After settlement the same received sample reached31 degrees and ring-corner
error fell from25.94mm to at most0.241µm. Neither canvas submission nor the camera
changed the geometry. The fixture now compares settled images, retains its
intermediate geometry checks and unchanged pixel threshold, and tests omission
of that final settle causally. This is a test scheduling repair, not evidence
of a production latency fix. The original635 native140-subframe proof remains
separate and unchanged. The affected fixture passes602 assertions; its single
forward120ms causal control fails for the expected phase/corner mismatch.
An earlier backwards-clock control is retained but superseded for causality.

## Validation and delivery

Worker receipts retain the original full149-suite invocation honestly as
147PASS/2FAIL, followed by successful affected census/help reruns: combined
coverage149/149. It is not a second single green full run. The maintainer asked
not to repeat already passing unaffected suites for bounded fixes. Original
failed/control receipts, exact source hashes, private native audit and package
evidence remain under
`.planning/debug/material635-review-20261006T195527Z/proof/`.

Inherited shader evidence is318 assertions plus25 controls; original runtime
evidence466 plus26 controls, environment11439 and terrain322 with their controls.
GL references reconstruct native equations, not original Windows PBR execution.
Final integration source15/15, golden296633 and bilingual docs5 pairs pass.
The bounded lifecycle follow-up retains unaffected source receipts and reruns
its relevant source guards. Strict Release/Debug and compiled scope are recorded
beside that follow-up under `.planning/debug/material636/`. Full NPC635 lifecycle, rendered
geometry/input and original owner/observer proofs remain source-bound and are
not reclassified as multiplayer hardware acceptance.
Final strict Release and Debug both have zero warnings/errors. Compiled635→636
has1227→1228 types: one new WorldMaterialBudget,13 intended material/configuration
types changed and eight inlined build constants; no removal or changed NPC repair.
Config670/patches221/log tokens4791 retain every634 surface, adding only the new key.

Install the new `prebuilt/ghvr-environment.bundle` with the assembly:
56804117 bytes, SHA256
`c012d139af3a9712e196d4f67f79a72109fbadf333388aa2d9c87ce42e76577d`.
Both environment shaders and the unchanged3170 immutable streams are verified.
Main/town bundles are unchanged. Both peers must install636.

Compare stages0/1/2 after full loading with other settings and room state fixed;
also inspect another biome and DLC content. Check color/UV/fog/fades and complete
geometry. Debug `worldMaterialMode`, `World material coverage` and
`WorldMaterial.PreCull` expose requested/effective scope and CPU work. New material
checks also cost CPU; no headset FPS improvement is asserted. Existing measured
Frame CPU/Terrain work is not attributed to the native game merely because the
material stage changes. Hardware pixels, networked gameplay and Frame gains
remain unverified.
