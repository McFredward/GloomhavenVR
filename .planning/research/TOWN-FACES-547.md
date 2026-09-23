# Build 547 — face and scalp authoring

## Hardware evidence and changes

The maintainer supplied `debug/glatze.jpg` and `debug/händler_gesicht2.jpg` but no
new logs. Original game portraits in `debug/npc-references` remain the identity
reference. Their small painted images inform proportions; the reviewed 718 px
head sheets supply the close-view skin/hair albedo.

- Priestess: removed the `CoifCloth` projection and cloth-shaped deformation of
  the anatomical scalp. That mask replaced real hair with a smooth grey patch
  extending around the ears and looked like an empty/bald cap beneath the hood.
  The complete head now retains its grey-brown swept hair, with bounded 0.65 mm
  geometric scalp relief. The existing original hood remains separate.
- Merchant: removed cumulative broadening of the cheeks, mouth and lower beard;
  narrowed the head by 10% while fitting both real eye pivots/orbits to the same
  proportions. Jaw/skull skin weights, facial shape keys and body rig remain.
- All three: registered photographed nostril centers to the actual alar recesses.
  This is a local UV fit, independently measured on the source head sheets, not
  another painted cavity or a change to the source images. Existing anatomical
  cavities now receive the corresponding photograph features.
- All three: constrain frontal projection to the actual reference silhouette,
  including its narrowing below the ears. Previously it sampled the neutral
  studio background into grey wedges on the side of the jaw.

## Reproduction and evidence

The worker's private authoring root is
`/home/claw/worktrees/town547-faces/.planning/debug/town547-faces`.
Inputs stay read-only in the integration checkout:

- `npc-authoring542/town542-heads/<npc>/inputs` (head references)
- `npc-authoring542/town542-rig-normal/<npc>/rig-source.blend` (costume/body rig)
- `npc-authoring545/hands` (anatomical hands and hand contract)

Run Blender 4.2 with `--python-exit-code 1`:

1. `author-town-facial-topology.py` using the tracked `npc-face-template` data.
2. `assemble-town-facial-rig.py` with `--hands` pointing to the **folder**, not
   `hand-source.blend`; write fresh `rigged/<npc>` output.
3. `review-town-facial-authoring.py` on each assembled `rig-source.blend`.
4. Unity 2021.3.5 `GloomhavenVR.TownServicesBuilder.RefreshFacialRig`, with
   `-townFaceRoot` pointing to the assembled `rigged` folder.

`check-town-facial-landmarks.py` passes 18 assertions including eight negative
registration controls. It measures reference-pixel alignment and plausible fitted
eye spacing; it does not establish final visual quality.

Neutral front/oblique and left/right 45-degree head-turn views are rendered from
the actual assembled meshes, separate from prototype front/blink/jaw/clay views.
Final Unity/D3D/stereo appearance, final activity poses and the rebuilt Windows
bundle remain the integration/hardware validation responsibility. No paid API was
used and no original game asset was edited.
