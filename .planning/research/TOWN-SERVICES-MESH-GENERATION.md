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

Results and selected derivatives will be recorded after provider completion and
Blender inspection. No release, runtime code or bundle change is part of this pass.
