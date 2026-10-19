# Current-eye scene atlas output

`SceneEyeOutput` is the reusable production packing helper for
`scene-contribution-atlas-v1`. It records a GPU blit of the current source directly
to the caller's destination. It allocates no atlas RT, adds no scene render,
readback or CPU texture upload, and has no preceding-frame/stereo-pair queue.
Material preparation happens outside callbacks. This file does not attach a
camera hook or modify configuration, camera state, the test inventory or gameplay.
Root owns that integration and the separately audited coverage producer.

## Interface and ownership

```csharp
var output = new SceneEyeOutput(shader); // Cold setup, exactly the frozen shader name/pass.
SceneEyeResult result = output.TryRender(source, destination, actualEye);
if (result != SceneEyeResult.Rendered)
    Graphics.Blit(source, destination); // Caller restores ordinary output exactly once.
```

The caller must establish the actual head camera, built-in XR MultiPass,
left/right callback and current coverage authorization. This helper accepts only
Left/Right values; it cannot authenticate a caller-supplied eye label or declare
an arbitrary framebuffer alpha to be scene coverage. The coverage/opaque-depth
producer must already have made that same source alpha meaningful. No public
boolean here grants such authority.

The initial transfer scope is Gamma working space and alpha-capable SDR
R8G8B8A8_UNorm/B8G8R8A8_UNorm sources. Linear working space, HDR/sRGB formats,
arrays/double-wide admission, mipmapped/dynamically scaled unknown routes and
explicit unresolved multisample bindings refuse without drawing. The owning
camera hook remains responsible for rejecting double-wide source provenance;
width alone cannot identify that layout. Valid positive dimensions up to the
provider limit are accepted, including the tested 2048/3408-square sources.

Ordinary MSAA1/2/4/8 sources with `bindTextureMS=false` use Unity's normal implicit
resolve, as the ordinary Graphics.Blit path does. The helper does not reject an
image-effect source merely because its descriptor still says MSAA. Ordinary
MSAA destinations are also supported. An explicitly unresolved sampler binding
is a distinct refused route; no scratch resolve RT is silently allocated.

Source and destination may not alias. A null destination retains the ordinary
Unity image-effect destination convention; the helper neither invents its
extent nor claims to enlarge the XR carrier. It restores `RenderTexture.active`
and `GL.sRGBWrite` and clears the material's borrowed `_MainTex` binding after
recording. The GPU draw captures its source at the actual call; no temporary
source RT is kept for a later eye/frame. Dispose closes admission before normal
deferred material destruction. No GPU wait or automatic timer is introduced.

## Frozen geometry and signal semantics

`SceneAtlasProfile` binds the exact manifest at
`tests/steamlink-scene-payload/profile.json`, SHA256
`04b47a78192aa883c264b1e04b88d0e17e7d373ab8ed1f1f6b15ee1a20601904`.
The source shader is `unity/Assets/Shaders/SteamLinkSceneAtlas.shader`.

The logical canvas is2112x2752, contribution region(32,352,2048,2048), and
coverage is1024-square in four1024x256 row tiles. Sixteen-pixel halos continue
global neighboring rows; only the complete mask's outer edges clamp. The twelve
32-square neutral guard cells start at(64,2400), stride160. **GuardY is2400, not
the historical draft footer2720.** Codes are111,208,128,104,227,251,255,193,102,
168,15,90; signature SHA256 is
`6fd08068e3fbffc166a80f5a1044d4c8135b37087441aac85f6223c121fccc5e`.

RGB is the current contribution E and is never multiplied by coverage A again.
Positive RGB at A=0, including additive emission, remains positive. Coverage
comes only from the same source's alpha. Each1024-grid cell averages four fixed
2048-scene-grid samples, then the complete coverage field is bilinearly
reconstructed. For a destination whose pixels do not match logical atlas texels,
this requires up to16 source-alpha fetches per mask fragment (four samples for
each of four neighboring mask cells). Continuously sliding a four-fetch box
would be a different filter and is not substituted. Contribution fragments need
one source fetch. A shader-owned linear-clamp sampler avoids changing or relying
on the borrowed source's filter/wrap settings.

The pass paints normalized layout into the actual destination. Allocating no
large atlas is intentional; a3408-square destination and a2112x2752 destination
have different actual payload pixel densities. Profile geometry does not turn
either into a larger XR carrier. Guard recognition is format recognition only,
not body authentication, eye provenance or liveness.

## Focused evidence and limits

`tests/steamlink-scene-output/run.py` pins the new helper/profile/shader and uses
actual Unity2021.3.5/net472 references. The final helper type-check passes with
zero warnings/errors. No old full suite or wire gate is repeated.

Actual Intel Vulkan OnRenderImage controls exercise2048/3408-square sources at
MSAA1/2/4/8, differently labeled left/right inputs, current-frame repaint,
ordinary resolved RGB, fixed-grid A, guard cells, unknown eye, disabled/menu and
unknown-frame fallback. All eight positive camera cases and four direct-MSAA
cases completed before an unrelated final negative-fixture assumption failed:
changing `useDynamicScale` after Create did not establish that flag. A focused
repair uses the actual reported property; this provider still reports the
dynamic-scale control unavailable. No successful dynamic-scale GPU control is
claimed. The source guard remains closed when that property is true.

The final Windows shader bank is also loaded as an actual AssetBundle, rather
than substituting source-import shader code. Four small shipping-bank MSAA routes
passed their per-case RGB/A,48 guard and16 ordinary-fallback assertions; their
aggregate then correctly refused a missing geometric-partial witness. That
fixture had not fixed the order of two ZTestAlways objects. The repair changes
only their explicit fixture render queues and reruns one256-square MSAA4 case.
Its202 assertions pass, including27 original-RGB probes, seven truly partial
geometric pixels, one A probe,12 exact guard cells, two A0-emission probes and
four fallback paths. The sampled RGB error is below0.002, not a full-image or
bit-identity claim. A separate actual Linear camera case passes75 assertions
and five original-output fallbacks; Linear never receives the Gamma profile.

The shipping bank's SHA256 in those checks is
`b4aab0c2045d7140f37c43c1d86cf1a2de5d531e37e9b501a7aec2411fbcf38e`.
An earlier bank was actually refused at load; the builder had omitted its
AssetBundle object when the module was disabled. Root repaired the builder and
validated the container before the successful trials. The fixture explicitly
enables assetbundle/jsonserialize modules and retains the old failure evidence.

These are composed focused results, not a new complete gate. Camera callbacks
are real, but the label controls are owned Mono fixtures, not live XR. No Windows
D3D11 draw, null-backbuffer pixel comparison, native codec/foveation picture,
game-wide material coverage, physical-eye association or passthrough output is
established by this lane. Root's coverage and runtime-hook evidence is separate.
All stopped processes are reaped before shared completion cleanup; source,
results, shipping-input pins and failed runs remain private under
`.planning/debug/steamlink-scene-output-worker-20261017/`.

Primary API basis: Unity's
[RenderTexture MSAA contract](https://docs.unity3d.com/kr/2021.3/ScriptReference/RenderTexture.html)
distinguishes automatically resolved sources from `bindTextureMS=true`;
[Graphics.Blit](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Graphics.Blit.html)
retains the standard current-source image-effect route and requires explicit
care for source/destination aliasing and sRGB write state.
