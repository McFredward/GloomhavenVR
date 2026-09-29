# Town NPC mesh generation

Requested 2026-09-20. The maintainer authorized responsible paid fal API use for
the three NPCs, with deliberate inputs/parameters before every request. The goal
is strong close-range VR source assets for the proposed town interactions.
Runtime integration, rigging and animation readiness require separate validation.

## Credential handling and cost discipline

- Read only `FAL_AI_API_KEY` from the process environment. Do not inspect `.env`,
  print keys, place them in command arguments, or send them to asset download hosts.
- `uv run --env-file .env` loads credentials into the child process; the generation
  script does not open that file. Only the fal queue origin receives authorization.
- Six deliberate initial requests, one per NPC/model combination. No blind seed
  search, automatic resubmission, paid upscaling or unrelated generation.
- Persist a submission intent before POST, and the queue receipt immediately after.
  An uncertain submission without a receipt blocks resubmission until manually
  reconciled with provider history. Poll/download operations do not regenerate.
- Keep original references, actual submitted crops, raw provider GLBs, request
  receipts, result metadata and hashes under gitignored `.planning/debug/npc-meshes/`.
- Published prices are an estimate, not evidence of the account's final bill.

## Initial comparison

| Candidate | Deliberate parameters | Estimated USD per NPC |
|---|---|---:|
| `fal-ai/trellis-2` | Neutral front; resolution 1536; 4096 texture; seed 20260920; 500,000 target vertices; remesh on; documented 12-step defaults for all three stages | 0.350 |
| `fal-ai/hunyuan3d-v3/image-to-3d` | Neutral front, left and back; `Normal`; PBR enabled; default face count (omit custom-count surcharge) | 0.675 |

Three NPCs × both candidates = **USD 3.075 estimated** before tax. Higher Trellis
resolution costs only USD 0.05 more than 1024, so this comparison uses the highest
offered resolution directly. Keep default sampling/guidance until actual defects
justify changing them; increasing every setting is not evidence of better quality.
Keep the generated high-detail source before producing any runtime LOD derivatives.

Pricing and schema checked on 2026-09-20:

- [TRELLIS.2 price](https://fal.ai/models/fal-ai/trellis-2) and
  [schema](https://fal.ai/models/fal-ai/trellis-2/api).
- [Hunyuan3D V3 price](https://fal.ai/models/fal-ai/hunyuan3d-v3/image-to-3d) and
  [schema](https://fal.ai/models/fal-ai/hunyuan3d-v3/image-to-3d/api).

## Reference preparation

The existing gpt-image-2 sheets provide six panels each. Only neutral front, left
and back are used; interaction poses are excluded. The generated sheets are
artistic proposals, not calibrated multi-camera captures. Do not mirror missing
views of asymmetrical costumes. Original game art remains the identity authority.

`scripts/generate-npc-meshes.py prepare` makes deterministic panel extracts and
stores their exact crop rectangles and hashes. Existing paid-request inputs remain
immutable. Reviewed front images retain whole silhouettes. The enchantress's first
crop retains a narrow fragment of the bottom view label: explicitly inspect its
result for unwanted text/base geometry before acceptance, and use a separately
named tighter crop for any corrective generation rather than rewriting provenance.

## Commands

```bash
uv run --no-project --with pillow python scripts/generate-npc-meshes.py prepare
uv run --no-project --env-file .env python scripts/generate-npc-meshes.py submit --npc merchant --candidate trellis
uv run --no-project --env-file .env python scripts/generate-npc-meshes.py collect --npc merchant --candidate trellis
```

`submit` is the paid operation. `collect` is safe to repeat and downloads only once.
Equivalent combinations cover priestess/enchantress and the hunyuan candidate.

## Acceptance review

Inspect real mesh renders from front, sides and back, plus face and hand close-ups.
Check identity, separated fingers, palm volume, facial planes, cape undersides,
accessory attachment, foot contact, silhouette and inconsistent painted lighting.
Inspect topology, texture dimensions/material channels, triangle counts and
unwanted disconnected geometry. An attractive front render alone is insufficient.
Do not describe static generated triangles as an animation-ready character or
claim headset quality from offline renders.

## Completed comparison and bounded follow-up

All six initial requests completed and their GLBs passed local header/hash checks.
Blender 4.2.22 imported every model and rendered full front/side/back/three-quarter
views plus face and hand evidence. These are real mesh renders, not provider
thumbnails or generated pictures. Detailed review tooling and controls:
[mesh review](TOWN-SERVICES-MESH-REVIEW.md).

| NPC | TRELLIS.2 triangles | Hunyuan triangles | Initial finding |
|---|---:|---:|---|
| Merchant | 493,781 | 499,470 | Trellis face/beard/garment fragmentation; Hunyuan intact but glossy |
| Priestess | 488,749 | 499,374 | Trellis face/neck/dress cracks; Hunyuan intact, visible hand texture seams |
| Enchantress | 493,437 | 499,768 | Trellis rear hood/hair differs from reference; Hunyuan rear silhouette closer |

All initial models contain 4096 x 4096 textures. The merchant Trellis defects
remain with constant opaque clay material and recalculated normals: this is not
fixed by simply wiring texture alpha or changing the studio lighting. Hunyuan
oblique hand controls show four fingers plus a thumb on each hand for all three;
frontal overlap alone would have incorrectly suggested merged fingers.

One targeted additional comparison was justified by the merchant's smooth skin
and beard: `fal-ai/hyper3d/rodin/v2.5`, `Gen-2.5-High`, 500K triangles, PBR,
high texture mode, HD texture, delight, A/T-pose and HighPack 4K. Same three
neutral references; seed 20920; an explicit prompt preserves identity/costume
and asks for weathered skin, matte cloth and no scenery. The complete prompt and
parameters are retained in `merchant/rodin/plan.json` before submission.

[Rodin pricing](https://fal.ai/models/fal-ai/hyper3d/rodin/v2.5) is USD 0.40 plus
USD 0.80 HighPack; its request completed, producing 500,000 triangles. The result
has attractive cloth/beard detail and less glossy skin, but weaker facial likeness
and nearly closed eyes. This did not justify repeating it for the other NPCs.

**Seven submitted/completed generations; estimated total USD 4.275 before tax.**
The tool enforces an internal USD 8 estimate ceiling for this comparison; this is
a conservative agent spending bound, not a user-requested budget or authorization
to exhaust it. No repeat generation is needed for offline material, seam or LOD work.

Provisional selection is Hunyuan for all three, subject to material/seam inspection
and derivative validation. The enchantress's small input-label fragment did not
produce visible text or a pedestal in either inspected reconstruction. Preserve
that actual input and its provenance; do not disguise the preparation defect.

No release, runtime code or bundle change is part of this pass. Static source meshes
are not yet retopologized for facial animation, skinned, rigged or headset-approved.
