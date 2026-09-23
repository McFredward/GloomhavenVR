# Build 545 portrait identity and anatomical surface correction

## Hardware authority

The eleven screenshots captured on 2026-09-21 at 21:26–21:31 and the matching ModBuild 544 log supersede the previous desktop approvals. They expose elliptical cut-out eyes, long exposed necks under combined bowing, hollow garment joins and faceted/cracked fingers. Original game portraits in `npc-references/*-original.png` remain the identity authority; generated references and a generic anatomical template cannot override them.

## Bounded asset plan

Preserve existing shared facial channels and separate eye components while fitting actual orbital/lid contact and the original facial silhouette. Correct neck/shoulder transitions as continuous surfaces with coherent skinning, using above/rear and combined activity/gaze views. Replace defective generated hand surfaces with the already pinned CC0 anatomical hand topology and original finger weights, fitted to the existing arm endpoints. Retain original costume details and native gameplay ownership.

No paid generation has been requested. First geometry checks use reusable portrait materials and clay under close perspective cameras; final atlases and bundles follow only after those forms are reviewed. Automated shape/rig tests are regression evidence, not visual acceptance.

## Implemented source corrections (desktop review in progress)

- Fit the merchant's rounded dome, broad nose and cheek/jaw silhouette, longer beard and brow angles against the original portrait. The physical jaw/skull rig remains intact. The enchantress retains cool orbital makeup and purple lip material aligned to the anatomical loops.
- Replace the portrait-distorted ellipsoid eyes with 14 mm spherical globes and fit the orbital surface/lids to their actual volume. Blink still closes the original anatomical lid loops; the cornea remains independently lit, with no painted highlight or emission.
- Use the pinned original CC0 lower-neck skin UVs instead of stretching one portrait scanline down the neck. Move the head rotation pivot to the anatomical skull base (1.565 m) without moving the original arm endpoints.
- Replace generated hands with the pinned CC0 hand topology/weights and a shared 2K original-skin atlas. There is one additional hand submesh/material per resident, with one skinned renderer retained. Original generated hand shells are removed at a cuff-hidden plane.
- Hand local Y follows wrist to middle MCP; local Z is palmar and positive local X flexes each digit. `PalmContact.L/R` are actual skin-ray intersections, not guessed wrist offsets. `PalmCentre.L/R` and all five `*Tip.L/R` markers follow the appropriate hand/distal bone.
- Restore imported bones to mesh bind transforms before attaching optical/contact markers. FBX's imported Idle transform differs from marker bind space; direct parenting previously introduced a roughly 16 cm false palm offset in the first prototype. Idle is sampled again after parenting.
- Preserve the complete original hood/scarf silhouette and clean only its existing inner cut edge; an experimental broad aperture cut was rejected because it severed folded source cloth. The priestess's fitted linen coif covers scalp and ears; the original portrait explicitly shows an aged bare neck down to the chain. An experimental full cloth bib was rejected and removed. The anatomical neck continues inside the original blouse.

## Review boundaries

The frozen `Hands545v2/town-hands.bundle` is a contact-calibration artifact, not the final package. Its SHA256 is `e47a57d9891d3517b230ff103448e61849fb98a252e25ebcc985a1a6adf9f530`. It contains the stable new hand frames, but predates the final cuff and hood cleanup. Hardware acceptance remains outstanding. Final orbit, combined work/gaze, three-LOD eye/blink and actual-runtime hand contact evidence must accompany the final bundle.

The optical surface now uses a distinct 7.8 mm anterior corneal curvature instead of reusing the 14 mm scleral radius. Primary measurements report mean radii of 7.86/7.66 mm ([Høvding 1983](https://pubmed.ncbi.nlm.nih.gov/6624411/)); this is an anatomical art-model approximation, not a clinical eye simulation. With the same limbus diameter, the steeper cap includes the actual side-lamp/view half-vector. The lids are fitted over this same bulge in neutral and blink; corneal highlight power is not artificially increased.

## Final authoring costs and scope

No paid generation or additional external asset purchase was used for build 545. The shared hand atlas uses the pinned CC0 skin images recorded in `npc-face-template/PROVENANCE.json`. The source actor triangles (before Unity UV splits) are:

| Resident | LOD0 | LOD1 | LOD2 | Build544 LOD0 |
|---|---:|---:|---:|---:|
| Merchant | 135434 | 49203 | 31395 | 111763 |
| Priestess | 134550 | 47391 | 28851 | 111695 |
| Enchantress | 139754 | 49318 | 29612 | 116552 |

The additional hand material adds one draw per visible resident; one shared 2K atlas serves all three. Existing body/face texture resolution and facial channels are retained. Final hardware timing and visual acceptance remain outstanding.

## Final artifact evidence

- Windows bundle: 100,501,555 bytes, SHA256 `08ff85016de6350533a1540b64591ed1ff4c3555ac6d7986ba79486df0fe597e`; below the 100 MiB GitHub object limit.
- Matching Linux review bundle: SHA256 `add071e017817d75e51eb08851d70c045ea6b57e715dca8c874efe14b2a1fa51`.
- Actual asset gate: 599 render assertions (plus a separately successful bundle build) and six deliberate visual negative controls. This includes true 2K standalone hand imports, three materials per LOD, hand-marker parenting, no animation tracks that overwrite reparented contact frames, facial channels, opaque/dissolved rendering, pose scale and native capped map clip planes.
- Actual Windows shader programs: all four D3D fragment variants of each eye shader bind the real four-light inputs. The final cornea on/off probe under the actual 2.6-power lamps changes 3,088–5,458 pixels across three views, with peak differences of 50–56/255; the glints are produced by the actual lamps.
- Evidence: `.planning/debug/town545-final-evidence-v4`, with a 128-input SHA256 manifest verified against the promoted source tree. `portrait-review` contains the approved geometry/material review; `practical-cornea` contains the isolated optical probe. Static station views intentionally omit runtime native decoration, which has separate integration evidence.
- Canonical packed authoring inputs: `.planning/debug/town545-prototype-final`, `town545-rig-final`, and `town545-hands-v3`; the integrator archives these as `npc-authoring545` in the main checkout.

The final shared hand atlas contributes one additional draw per NPC. Final imported mesh memory and triangle/vertex counts are recorded per NPC and LOD in the `*-facial-metrics.txt` evidence. Close stereo appearance, runtime timing and the lower-detail portrait-style collar/cloth surfaces remain hardware assessment items; no claim of photorealism is made.

The final shader-only correction sets `TownFlame` `DisableBatching=True`, preserving each native flame/glow billboard origin. Both final bundles contain all four shipping shaders; the Linux fixture now loads TownFlame from its actual bundle rather than the editor AssetDatabase. Its compiled tag, flipbook, billboard and dissolve behavior pass. The Windows binary also contains the normalized tag. The manifest differs from the previously approved art inputs only in TownFlame, its validator and Sources.md; no actor mesh, texture, material or pose was regenerated.
