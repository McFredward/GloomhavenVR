# Stereo video surface foundation

`StereoVideoSurface` draws explicitly identified full side-by-side video in an
existing world-space `RawImage`. It reads that image's existing texture and never
owns or changes a `VideoPlayer`, source camera, render target, audio or native
completion callback. Production asset selection must call `IsAvailable` before
choosing packed media, retaining the matching 2D source on failure. `TryAttach`
then assigns an owned material. Dispose the returned surface when closing its
window. The image's previous material and UV rectangle are restored.

The running XR mode and the compiled renderer choice must both be MultiPass.
An actual camera `onPreRender` callback selects `camera.stereoActiveEye`; mono
and unknown observations select the left picture. No callback parity, eye pose,
or stereo metadata is inferred. A single decoder supplies both pictures. Paired
pre/post callbacks restore the enclosing eye after a nested auxiliary camera
render. Use
`PerEyeAspect(packedWidth, height)` when sizing the image. This does not enable
unconverted normal movies or single-pass rendering.

The small Windows64 bundle is embedded with logical name
`GloomhavenVR.StereoVideo.ui-sbs.bundle`. Its only presentation asset is a
UI-compatible SBS shader; it also contains the upstream copyright/permission
notice. Its three graphics banks are D3D11, OpenGLCore and Vulkan. The large
existing asset bundle is untouched.

Build the generated artifact explicitly with the game-exact editor:

```sh
bash scripts/stereo-video-render-tests.sh --build-bundle-only
```

Ordinary tests only read the committed production bundle. Mutant shaders build
into the private evidence run, and never replace the shipped production artifact:

```sh
bash scripts/stereo-video-render-tests.sh
bash scripts/stereo-video-render-tests.sh --graphics-api vulkan --no-negative-controls
```

The fixture compiles the whole production class against actual Unity2021.3.5
and native game UI assemblies for net472, embeds the exact shipped Windows
bundle bytes, and renders actual world-space UI into GPU render textures. Only
the eye observation is substituted to select recorded left/right/mono/unknown
inputs without an XR provider. The unsupported-mode case separately substitutes
the observed running mode. Source hashes record the scope. Tests measure eye
pixels, bilinear seam protection, per-eye shape, stencil/rectangle/soft clipping,
texture/tint/group alpha, original material restoration and callback lifetime.
An actual nested mono-camera render must return to the outer right-eye picture.
Deliberate code/shader defects must fail the corresponding pixel/lifetime check.
Missing-resource and unsupported-mode cases verify unchanged 2D bindings.

The independent binary inspector checks real bundle containers, original load
paths, embedded license bytes and all four UI clip/alpha variants in the three
compiled graphics banks. That establishes D3D11 program presence, not D3D11
pixels. GLCore/Vulkan fixture output identifies the actual device/API. No test
here establishes a headset picture, provider eye callbacks, native video decode
performance, sound, skip/continuation or multiplayer behavior.

Every generated run reaps its owned processes and calls `cleanup_completed_run`,
including failures. Captures, logs, manifests, source and current bundle evidence
remain; regenerable managed/Unity caches are removed. The source attribution and
primary Unity shader archive receipt are in `source-provenance.json`; the
unchanged upstream MIT notice is `Unity-LICENSE.txt`.
