# Build723 spatial native menus

The maintainer requested a complete implementation of the reviewed menu-depth
families on 2026-10-26, including prepared depth inside the story illustrations.
Earlier movie integration was explicitly reported working; that report did not
identify a new headset build and does not certify these menu changes.

## Presentation contract

Original character creation, roster, skin, reveal and retirement previews consume
the real `Character3DDisplayManager` camera texture. Each actual RawImage receives
its own eye-relative render of that same native scene. The original model,
animation, effects, companions, materials and native request/visibility authority
remain native. A fixed authored portal distance avoids animation-bounds breathing.
Render leases restore native camera state and texture ownership in `finally`.

`StoryImageViewer.imageHolder` and `UIEventPanel.eventImage` receive shallow
heightfield relief inside the original Image mesh. Original sprite colour and UVs
are retained. Source identity comes from native narrative IDs, exact event default
slots and compendium Addressable GUIDs. Names alone cannot distinguish the PC and
console compendium variants. Async key changes retain the relief of the still
visible old sprite until its actual replacement arrives. Unknown narrative IDs use
the game's own first-config fallback without invoking asset loads or native logs.

Reward and unlock icons, their original glows, quest/class emblems, selected frames,
enhancement headers, tabs and sliced window decoration use shallow separate planes.
Compendium screenshots remain recessed planes: embedded text/diagrams stay planar.
No text, control layout, hit rectangle, callback or save state is extruded or replaced.
There is no new player setting. Presentation follows VR and native UI conversion.

Converted supersampled panels retain their exact orthographic camera sampling for
ordinary glyphs and controls. Only registered spatial content receives analytical
eye projection. Direct world-space UI and the native main-menu screen camera stack
are covered too. Borrowed render state, nested callbacks, source closure, exceptions,
mono fallback and final resource release are explicit lifecycle boundaries.
Additional panel eye targets share the existing per-panel and total memory caps;
reports include both eyes. Shared geometry derives from original window units and
common transforms, without viewer zoom or periodic placement changes. Each viewer
supplies their own eye projection. Wire v3 and next-free123 remain unchanged.

## Prepared illustration corpus and delivery

The native census resolves 448 narrative IDs, 73 compendium asset variants and two
embedded event defaults: 523 asset references, 521 exact Addressable sprite GUIDs,
515 unique original RGBA sources. All source rasters use the complete native texture;
trimmed texture rectangles do not change the original UV-to-heightfield mapping.
The serialized console sprite-size rounding is accepted within .001 native pixel.

Independent single-image CPU FP32 inference uses the already pinned project model
and checkpoint. No inference model or original colour-image duplicate ships.
GMD1 stores bounded 16-bit normalized inverse depth, bottom row first, with source
dimensions, exact payload length and CRC. Fields use a 512-pixel longer axis and
preserve aspect. Runtime bilinear sampling and border feathering form a continuous
mesh. This is relief derived from one original view, with no invented hidden colour
surfaces. Existing native assets and game references are read-only.

The 515 fields total157113440 bytes; the manifest is1910220 bytes
(SHA256 `d376d63ab66813f4baaccd529266e969d7e34b902bd297b70c8062386b30d191`).
`MenuDepth/Story/` accompanies the DLL through the normal installer and ZIP.
Both packagers validate the finite516-file roster, source/header/dimensions/hash
agreement, deployed or staged content and final archive content. Missing/corrupt
inputs fail before replacing a prior archive. Existing22 movie companions remain.
The embedded18483-byte rendering bundle was built with Unity2021.3.5f1 and has
SHA256 `beee5adbf2ebaba133e3e827269b45d267648ba681a465609c73cc51261b3c96`.

## Original dissolve materials

A source audit found all16 native story/event image bindings use `UI/Dissolve mask`:
12 story images and4 event images. Every exact picture slot has a native one-second
`_Cutoff`1-to0 reveal. Narrative `_EdgeWeight` is2.4900000095; events use1.
These are material animations, beyond the vertex/CanvasGroup fades in the initial
GPU fixture. A colour/alpha-only depth shader would have discarded the reveal.

Direct pinned native DXBC SHDR disassembly, corroborated by independent SPIR-V/HLSL
translation, establishes independent main/mask STs, unchanged vertex colour,
strict mask/cutoff discard, straight RGB and unsaturated alpha
`(((mask-cutoff)*sourceAlpha)/mask)*EdgeWeight`. Native RGB and alpha both use
SrcAlpha/OneMinusSrcAlpha. The depth shader preserves those operations and blend
factors, without adding material tint, alpha rounding or premultiplication to this
branch. Its opacity-zero discard prevents an invisible relief from writing depth.
Ordinary UI retains its original premultiplied branch and blend factor.

Native tween initialization clones `Image.material`; subsequent float writes do
not dirty uGUI's rendering-material cache. The owned material therefore refreshes
current `_Mask`, independent texture transforms, `_Cutoff` and `_EdgeWeight` at
both material modification and each eye lease. Native material/controller clocks
and callbacks remain untouched. The mask is the original native256-square BC1
Noise1 texture with nine mips; no duplicate original mask or artwork is shipped.

All28 inspected native source files remain byte-identical. Full source/material,
shader, raw-program, signature, colour-space, mask and16-slot bindings are retained
in ignored `debug/menu-depth723/material-audit/run-ohye58v4/`. Runtime renderer
provenance is committed beside the shader. The private shader builder declares its AssetBundle module explicitly and refuses
to publish before loading the actual bundle and verifying both shader objects in
the exact editor. A wrapper/version-only check missed a bundle without its native
AssetBundle container; retained failing runs and the corrected load prove this boundary.
The native shader contains D3D11 code
only: an OpenGL reference transcribed from the exact native program and an
independent scalar/blend oracle are scoped evidence, not native D3D11 execution.

## Evidence and limits

Worker evidence is retained under the main checkout's ignored
`.planning/debug/menu-depth*` and the final integration record below. Corpora and
actual production source inputs are hash-bound; compiling causal controls must
fail at their specified witness, rather than treating a build error as proof.

Completed focused evidence before final integration:

- Media:13795 assertions,16 malformed controls; independent actual native RGBA,
  neural-output and1545 trimmed-UV correspondence verification.
- Actual C# GMD reader:5763 assertions,70 refusal cases,7 compiling causal controls,
  including every delivered field and an independent byte-exact golden.
- Character portal:85 GPU/lifecycle/throwing-render assertions,13 causal controls;
  actual GH.Runtime private-field smoke coverage, strict real-reference compile0W0E.
- Converted/direct capture:42 actual Unity/OpenGL assertions,11 causal controls,
  including exact coplanar glyph sampling, masks, alpha/depth, nested eye scopes,
  mixed capture order and owner closure; strict real-reference compile0W0E.
- Native main-menu stack:45 GPU assertions and4 causal controls; real camera-stack
  routing, unchanged glyph raster, masks, native alpha and partial-eye mono recovery.
- Illustration mesh/typed driver:37 GPU assertions and10 causal controls; complete
  actual Mono catalogue/field pass521 assertions with all515 real fields loaded.
  The test caught Unity JsonUtility silently omitting nested DTO arrays; production
  now uses explicit DataContractJsonSerializer, already used by native mesh banks.
  A compiling old-reader control reproduces that original runtime failure.
- Packaging:1778 Python,209 existing movie and2634 depth PowerShell assertions, plus an actual SDK
  Content/EmbeddedResource assembly/output proof. The standard fixture retains
  all existing movie controls and uses tiny opaque substitutes for non-depth assets.
  The focused SDK follow-up passes519 assertions. Shared private-project compiler
  teardown passes11 real-process assertions; it targets exact owned project PWDs,
  preserves other compilers and runs before cache cleanup.
- Integrated source gates16/16; private complete real-reference plugin compile0W0E.

Actual headset appearance, comfort, performance and a paired native multiplayer
picture remain hardware observations. Automated source, GPU fixture and installer
evidence do not establish those outcomes. GPU fixtures execute actual production
rendering with explicitly supplied eye poses; they do not claim live XR tracking or
the complete game animation/Beautify environment.

Final integration covers248 distinct local suites. The complete247-suite invocation
retains244 passing records and three honest failures: desktop/movie fixtures lacked
new ordinary-menu boundary ports, and a wrist run refused source edits during execution.
Bounded repairs/reruns pass desktop145+33 controls, movie193+21 controls across38
variants and the unchanged complete wrist suite. Default capture/graphic suites were
rerun after the shader update:42+11 and37+10 plus521 complete-corpus assertions pass.
The missing-AssetBundle-container attempt remains retained as a failed render check.

The added native material proof passes94 production GPU/CPU-oracle assertions and13
causal controls. Its saturation mutation was detected at an earlier native-pixel
witness than specified; only that expectation was corrected and the affected control
rerun. No product/tolerance change or new whole-matrix pass is claimed. Existing
unchanged passing coverage is reused under the maintainer's bounded-repair ruling.

Main receipts: ignored `debug/menu-depth723/{full-local,final-affected,
effects-render-final,material-final,source-effects}/results.json`,
`material-saturation-final.log`, `final-coverage.json`, strict real/metadata build
logs and `goldens-final.log` (306431 unfiltered assertions). Source gates16/16,
12 locked frame orderings,214 registered patch classes/313 methods,669 unchanged
configuration keys and no removed patch/log/config surfaces pass. Compiled comparison
against retained722 source/DLL/snapshots finds3 changed existing UI implementations,
14 added types and only version/stamp differences elsewhere, with no removed types.
Current rendering bundle format7/Unity2021.3.5f1 and all other committed bundle and
figure-part format checks pass. Player documentation agrees in English/German.

Verified worker archives, per-file indices and source/capture/assembly/log evidence
remain under ignored `debug/menu-depth-*-handoff-20261026/`,
`debug/menu-depth-window-worker-20261026-6df513cb2-963caf959*` and the material audit.
Completed clean owned worktrees are removed only after these independent byte checks;
shared game references, other worktrees, hardware logs and current deliverables remain.
The normal Debug installer archive is `dist/GloomhavenVR-1.2.2.zip`, built through
`scripts/package-release.sh`. Its actual22 movie files,516 depth companions and
Debug DLL are byte-identical to their respective source/build inputs. Reflection
inspection verifies ModBuild723, the exact18483-byte embedded rendering bundle
and absence of embedded movie/GMD resources. Normal text/Frame setup checks pass.
The private verifier and archive JSON receipts live under ignored
`debug/menu-depth723/`; documentation-only bookkeeping is followed by fresh clean
Debug/Release identities and the same normal packaging path. Native material tests
accept `--native-evidence` for the pinned external DXBC/BC1 audit input directory;
those publisher binaries are retained as ignored evidence and never shipped.
