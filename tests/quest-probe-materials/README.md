# Real Unity probe material fixture

Run only with a local, owned native asset slice and the game's exact Unity editor:

```sh
python3 tests/quest-probe-materials/run.py \
  --unity /path/to/Unity \
  --probe-assets /private/recovery/probe-assets \
  --reviewed-project /private/build609/project \
  --output-root /private/material-fixtures
```

The fixture creates a separate project and verifies every input file hash before
and after execution. It loads the original figure, atlas, normal maps and material
GUIDs in Unity 2021.3.5f1. Optional build609 materials distinguish missing texture
bindings from dormant orange `_Color` activation on a Standard shader change.
No recovered proprietary content is committed.

The build609 review found that both original Amp materials expose `_Diffuse`,
and the imported atlas resolves at 2048×2048. The reviewed Standard materials
retain that atlas through `_MainTex`; they also activate the previously dormant
orange `_Color` value `(1, 0.44149664, 0.051886797, 1)`. The mapping corrects that
source-proven tint activation. The screenshots alone do not prove missing texture
bindings or establish the resulting headset appearance.

Checks include saved properties hidden by the active shader, repeated conversion,
independent albedo/normal UV transforms, neutral original tint, material/model
build dependencies and explicit approximation receipts. Negative controls remove
albedo and normal bindings, drop the target texture/normal keyword, change target
tint/UVs, introduce a nonneutral unsupported original modifier and select a desktop
texture compression format for Android. These must fail closed.

This is editor/import evidence. The probe uses an opaque matte Standard
approximation and does not reconstruct original packed MRAO, two-sided culling,
outlines, dissolve or character lighting. A passing fixture proves neither visual
parity nor headset performance. The runtime lit/albedo diagnostic battery and the
actual Android build require separate verification.
