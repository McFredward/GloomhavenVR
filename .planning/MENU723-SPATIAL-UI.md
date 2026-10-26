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
The embedded16920-byte rendering bundle was built with Unity2021.3.5f1 and has
SHA256 `b5ed298ee017172f24f5f7771650e15880b9f33b855ffca48e27ac3ae0890f9d`.

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
- Character portal:51 GPU assertions,6 throwing-render assertions,7 causal controls;
  actual GH.Runtime private-field smoke coverage, strict real-reference compile0W0E.
- Converted/direct capture:42 actual Unity/OpenGL assertions,11 causal controls,
  including exact coplanar glyph sampling, masks, alpha/depth, nested eye scopes,
  mixed capture order and owner closure; strict real-reference compile0W0E.
- Packaging:1667 Python and2634 native PowerShell assertions, plus an actual SDK
  Content/EmbeddedResource assembly/output proof. The standard fixture retains
  all existing movie controls and uses tiny opaque substitutes for non-depth assets.
- Integrated source gates16/16; private complete real-reference plugin compile0W0E.

Actual headset appearance, comfort, performance and a paired native multiplayer
picture remain hardware observations. Automated source, GPU fixture and installer
evidence do not establish those outcomes. GPU fixtures execute actual production
rendering with explicitly supplied eye poses; they do not claim live XR tracking or
the complete game animation/Beautify environment.

Final integration receipts and counts are pending the complete local gate.
