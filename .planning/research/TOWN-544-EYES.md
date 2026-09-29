# Build 544 eye-lighting correction

## Hardware evidence

Read all six `npc_probleme/VirtualDesktop.Android-20260921-*.jpg` screenshots.
Merchant, priestess and enchantress eye apertures are black while adjacent skin
receives scene light. This is not inferred solely from an unlit studio preview.

The supplied `.planning/debug/LogOutput.log` identifies ModBuild 543 at line 17.
Line 24 identifies Unity 2021.3.5 / Direct3D11 / RTX 4090. The map head-camera
diagnostic at line 1433 explicitly reports Forward, MultiPass and HDR disabled.
The graphics census at line 1537 reports `pixelLights=0`. Deferred rendering is
therefore excluded for this report. No eye-specific material-binding diagnostic
was present; absence of a shader error does not establish correct lighting.

## Proven compiled defect

Inspected the actual shipped Windows bundle, SHA256
`6df700ed2374c7f0f8ab4652363bce54a70ab54e33ee67c6879a889f533a08e3`.
Both `GloomhavenVR/TownEye` and `GloomhavenVR/TownCornea` have eight vertex variants
but only four fragment variants. `VERTEXLIGHT_ON` appears in vertex variants only.
None of their D3D fragment parameter bindings contains `unity_4LightPosX0`,
`unity_4LightPosY0`, `unity_4LightPosZ0`, `unity_4LightAtten0` or `unity_LightColor`.
The entire practical-light loop was compiled out of both fragment programs.

`TownNpc` evaluates practical light in its vertex stage, explaining why the same
station's skin still receives its candles while eye surfaces do not. The ambient
and directional terms cannot substitute for omitted practical light. No claim is
made that every possible future black-eye report shares this cause.

The earlier GL probes could not detect this defect: Unity applies stage-specific
keywords to all stages on OpenGL. See Unity's [stage-specific keyword documentation](https://docs.unity3d.com/2022.3/Documentation/Manual/shader-keywords.html).
The Windows bundle metadata, not that general documentation, proves the actual
missing instructions in this shipped candidate.

## Correction

`EyeVertex` carries the engine's vertex-light enable state in one scalar varying.
The fragment loop branches on that varying instead of a fragment-stage preprocessor
keyword that Unity does not define on D3D. The loop therefore exists in all four
D3D fragment variants and reads the four-light arrays only for a draw which has
vertex lights. The actual Unity light positions, attenuation and colors remain the
inputs. There are no added lights, minimum ambient floor, self-emission, invented
glints, per-frame allocations or runtime material copies.

Shader source checkpoint: `69269d52`. Existing instance/stereo setup, fog, texture
sampling, normals and dissolve remain intact. No Station, Population, facial rig,
face texture, eye geometry or native game file changed in this lane.

## Focused validation

- `uv run scripts/check-town-eye-windows.py prebuilt/ghvr-town.bundle` rejects the
  actual build-543 bundle because its fragment bindings lack all five practical
  inputs. This is an actual compiled negative control, not a source-text test.
- `python3 scripts/check-town-eye-render.py --output /tmp/town544-eye-render`
  passed 36 render assertions. Evidence: `/tmp/town544-eye-render/run-28he51ag`.
  Its compiled Windows bundle passes all five input-binding checks for both eye
  shaders and all four fragment variants. SHA256:
  `9b49d58910a88731c64077c91952328e1675299a376f944026a59eaf67c35e82`.
- The GL render fixture uses a Forward camera, the real eye atlases/tint, mod
  layer 27, `pixelLightCount=0`, black ambient and a ForceVertex point light.
  Brown/green atlases and two offset views remain lit. At 50% visibility, lit
  fragments partially dissolve; at zero visibility, neither globe nor corneal
  glint remains. Disabling the light produces zero bright pixels: no self-light.
- A controlled missing-practical term reproduces a completely black eye with the
  same live point light. This visual negative emulates the missing Windows term;
  it is not represented as execution of D3D on Linux.
- Representative left-view images were visually inspected: lit globe, localized
  corneal reflection and completely black missing-practical control.

The shader-only sphere fixture does not validate authored eye anatomy or NPC LODs.
The asset lane owns final prefab rendering at all three LODs and must run the new
Windows binding check on its final rebuilt bundle. Two camera positions are not
XR stereo execution; headset D3D rendering and comfort still require hardware.
