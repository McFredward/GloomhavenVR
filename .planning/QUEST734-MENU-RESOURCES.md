# Quest Build734 menu-depth resources

The desktop rendering bundle contains the exact original native card DXBC and
must remain excluded from the game-free Builder. It is serialized for Windows;
the card derivative itself has only Direct3D11 programs. Copying it unchanged
into the Android mod does not supply the current menu rendering contract.

`tools/quest-builder/menu_depth_resources.py` owns one independent producer:

- `plan(source, source_files=None)` binds the three authored shaders, their
  license, the original native-card provenance and the consumed producer sources.
  It excludes build numbers, unrelated runtime code, movies and story fields.
- `produce(builder, source, output, game=None, editor=None, base_apk=None,
  native_cache=None)` returns `bundle`, `provenance`, `producerKey` and `reused`.
  Bundle/provenance are private `Path` values below
  `output/cache/menu-depth-resources/<producerKey>/`.
- Pass the two returned paths to MSBuild as `GloomhavenVRMenuDepthBundle` and
  `GloomhavenVRMenuDepthProvenance`. Their embedded logical names are
  `GloomhavenVR.MenuDepth.Rendering.bundle` and
  `GloomhavenVR.MenuDepth.Rendering.provenance.json`.

The producer first validates its closed cache. Otherwise it reads only the mod's
IL2CPP resource sidecar in the selected installed APK. Matching recipe, bundle
hash, actual Android target13, baked Vulkan platform18, all four runtime shader
roots and the native card alpha-clip/rect variants are mandatory. Matching
installed resources allow future code updates without the PC game or Unity.
A missing/changed recipe requires the exact original PC card bundle once. The
error names that requirement; there is no generic-shader or desktop fallback.

The first generation selects exactly original PathID7314317428712670795 from
`misc_gui_assets_all.bundle` with its pinned bundle/object hashes. Existing native
recovery translates its five exact DXBC programs and restores their original
uniform interfaces. Only the ShaderLab identity and `Default.ZWrite` change in
the resulting source. A private exact-2021.3.5f1 Android project compiles it and
the three authored shaders for Vulkan, Linear and MultiPass. A ShaderVariantCollection
retains all four original UI keyword combinations. The actual serialized bank
is audited, including the executable combined Vulkan vertex/fragment payloads.
No original game scene, campaign bank, media or other shader is imported.

Loose story delivery remains independent. `story_records(source,
source_files=None)` returns the exact numeric manifest+GMD closure with
`StreamingAssets/MenuDepth/Story/` destination paths. `write_story(open_zip,
source, records)` appends those files to the existing mod-content archive. The
current closure has515 GMD files plus its manifest. The Quest catalogue must
read `EnvironmentBundleDirectory/MenuDepth/Story`; ordinary DLL-adjacent Content
copying does not survive IL2CPP assembly deployment.

## Focused evidence

The resource worker starts from rebased candidate `8f3921adc`. This is a bounded
asset/producer lane within the integrator's class-C rebase validation; it does
not establish a new complete-gate pass.

- Eight standard-library host tests pass, including resource-directory bounds,
  corrupt input rejection, stable producer identity, installed-only/cache reuse,
  changed recipe refusal and real ZIP publication of numeric story content.
- Tiny actual Unity generation produces a55295-byte Android/Vulkan bank with
  four shader roots. Its native card has12 native combined stage aliases and
  four distinct executable programs, covering all original alpha-clip/rect
  choices. The exact five original DXBC hashes agree with the desktop handoff.
- Independent actual-byte cache/APK reuse and five causal serialized controls
  pass: Windows target, missing alpha clip, missing native card root, ZWrite off
  and corrupt closed provenance. The second focused audit takes1.69seconds and
  invokes no Unity or original PC game input.
- Preserve the first failed audit separately: its bank compilation/audit and
  reuse pass, then its desktop-target mutation changes UnityPy's public cached
  enum instead of the serialized backing field. The corrected mutation changes
  the actual serialized target and is rejected. No product tolerance changes.

Evidence is retained under
`/home/claw/quest3-local/build/evidence/quest-dev734-rebase-20261028/resources/`.
`run-67f7ajhc` retains the tiny compiler inputs/output and failed control attempt;
`run-ycxpq5e4` retains the passing actual-byte reuse/control proof. Exact project
editor/compiler processes are stopped and reaped before the common completion
cleanup removes the generated Unity Library. Original game inputs and shared
native caches remain unchanged.

This evidence proves resource/platform inclusion and exact native instruction
provenance. It does not execute the Android GPU, prove original effect pixels,
measure headset motion clarity or certify multiplayer hardware appearance.
