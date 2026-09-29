# Derived NPC modelling references

Requested by the maintainer on 2026-09-20: submit all three extracted original game
illustrations to **gpt-image-2**, generating several poses and viewpoints for downstream
image-to-mesh tools.

This is a separate deliverable from [original artwork extraction](TOWN-SERVICES-ART.md).
The model synthesizes unseen surfaces and converts the illustration into a proposed
three-dimensional appearance. These images are not original game assets, measured
turnarounds, final meshes or proof of riggable topology.

## Generation specification

- Explicit model: `gpt-image-2`.
- Invocation: the installed imagegen skill's CLI, using its `edit` operation with the
  respective original PNG as the image input. This path exposes the requested model ID.
- Requested resolution: 3840 x 2160, quality `high`, PNG.
- One distinct identity-preserving prompt per NPC; no other NPC's image is included.
- Six panels: front, left side, back, front three-quarter, back three-quarter and
  a character-specific interaction pose.
- The first five panels request the same neutral A-pose and costume, consistent scale,
  near-orthographic projection, neutral studio lighting and plain light-grey background.
- Merchant interaction pose: greeting with open hands. Priestess: original book/staff
  pose, with empty hands in the neutral modelling views. Enchantress: source-inspired
  floating pose, with effects removed to keep geometry visible.

Exact prompts:

- [Merchant](town-service-prompts/merchant.txt).
- [Priestess](town-service-prompts/priestess.txt).
- [Enchantress / Magierin](town-service-prompts/enchantress.txt).

Outputs are kept in `.planning/debug/npc-modeling/`, separate from the untouched originals
in `.planning/debug/npc-references/`. A generation manifest records verified dimensions
and hashes after visual inspection; no original image is overwritten.

## Result and review

All three API calls completed successfully with the explicitly requested `gpt-image-2`
model. Each output is 3840 x 2160 and contains all six labeled views. The inspected sheets
retain the distinctive source silhouettes, costume colours and principal accessories;
the character silhouettes are complete and do not overlap the neighboring panels.
The priestess's book and staff appear in her ritual panel, and the enchantress's hand
effects are absent so they do not conceal her geometry. No corrective generation was
needed for this initial modelling-reference set.

Local collection: `.planning/debug/npc-modeling.zip`, containing the three PNG sheets,
three exact prompts, README and generation manifest. PNG decoding and archive CRC are
checked before delivery. Cross-view inspection is a visual review, not proof that an
image-to-mesh model will reconstruct consistent geometry or animation-ready topology.

## Reproduction

The existing local `.env` supplies the API credential to the CLI environment. Never
copy that credential into prompts, manifests, commits or logs. Example for the merchant:

```bash
uv run --no-project --with openai --env-file .env python \
  /home/claw/.codex/skills/.system/imagegen/scripts/image_gen.py edit \
  --model gpt-image-2 \
  --image .planning/debug/npc-references/merchant-original.png \
  --prompt-file .planning/research/town-service-prompts/merchant.txt \
  --no-augment --size 3840x2160 --quality high \
  --out .planning/debug/npc-modeling/merchant-reference-sheet.png
```

For a revision use a new output filename, keep the original game image as an identity
reference and describe the specific correction. Do not silently change image models.
An identical prompt does not guarantee an identical generated image.

## Mesh workflow

Use the neutral front/side/back views for reconstruction and proportion checks; the last
panel describes intended animation character, not the neutral rigging pose. A downstream
tool that accepts only one subject/view should receive one selected view rather than the
whole multi-character sheet. Check attachment sides, cape volume, face/mask, fingers and
hidden clothing manually before accepting generated geometry. Fine texture detail is not
a substitute for correct silhouette or consistent geometry.

The original illustrations remain the authority when a generated detail differs. Rear
garments and concealed surfaces are proposed additions for the maintainer/artist to judge.
