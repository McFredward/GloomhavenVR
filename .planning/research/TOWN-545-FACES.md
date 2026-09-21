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
- Preserve the complete original hood/scarf silhouette and clean only its existing inner cut edge; an experimental broad aperture cut was rejected because it severed folded source cloth. The priestess's continuous fitted linen coif covers scalp and under-chin/neck surfaces instead of leaving hair and a mismatched exposed chest patch.

## Review boundaries

The frozen `Hands545v2/town-hands.bundle` is a contact-calibration artifact, not the final package. Its SHA256 is `e47a57d9891d3517b230ff103448e61849fb98a252e25ebcc985a1a6adf9f530`. It contains the stable new hand frames, but predates the final cuff and hood cleanup. Hardware acceptance remains outstanding. Final orbit, combined work/gaze, three-LOD eye/blink and actual-runtime hand contact evidence must accompany the final bundle.

The optical surface now uses a distinct 7.8 mm anterior corneal curvature instead of reusing the 14 mm scleral radius. Primary measurements report mean radii of 7.86/7.66 mm ([Høvding 1983](https://pubmed.ncbi.nlm.nih.gov/6624411/)); this is an anatomical art-model approximation, not a clinical eye simulation. With the same limbus diameter, the steeper cap includes the actual side-lamp/view half-vector. The lids are fitted over this same bulge in neutral and blink; corneal highlight power is not artificially increased.
