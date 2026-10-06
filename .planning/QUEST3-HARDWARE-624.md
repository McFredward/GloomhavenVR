# Quest B624 — retain the initial Vulkan eye allocation

## B623 headset evidence

`quest-capture-20261006T063227Z-c64d41c6.zip` identifies installed B623,
APK SHA `ef954d63eb7f12eb284205817ac170c0c7559092ca18ee4f297100be5d6268f9`.
Fresh startup and Android logs agree. Older B609/B611 hardware-state records
and the previous B621 startup log are historical, not the current executable.

At 08:32:05 local time the current mod requests MSAA 0→4 and eye resolution
1.0→1.5. Unity replaces the 1680×1760 swapchains with 2520×2640 images, then
recreates the same external XR image ID4/native pointer after old images are
destroyed. At 08:32:07 UnityMain crashes in Adreno `vkCreateImageView`, through
`XRTextureManager.ProcessPendingTextureRequestsSynchronized`.

Original bootstrap has not started. Content checking is contemporaneous;
no managed asset-load error precedes this native crash. Exact ownership inside
Unity/OpenXR/Adreno remains unproven. Earlier menu-only GLES tests do not validate
the full Campaign Vulkan path. No new screenshot was supplied with this capture.

## Changes

Configured Quest players activate the current shared Steam Frame standalone
defaults through `FrameDefaults.Active`. Desktop opt-in and persisted BepInEx
values retain their existing behavior; there is no copied Quest option allowlist.
The current defaults include MSAA off, eye scale1.0, native900-MB streaming floor,
streaming allowed and the same scenario detail controls.

Quest Vulkan keeps the initially negotiated eye allocation. Resolution values
below1.0 use only the viewport setter; values above1.0 currently have effective
scale1.0. Live display MSAA requests are blocked. The Player sets all original
quality levels to the shared standalone MSAA default before XR starts, avoiding
sample-count changes at later native quality switches. Existing stored choices
are not erased, and English/German descriptions explain the temporary limitation.
Quest GLES and ordinary desktop rendering retain their original setters.

The complete Campaign/DLC asset scope is preserved. The original import,
native content and Shader caches are reused; exhaustive Shader compilation is
not required for this allocation-policy fix.

## Verification and next headset test

Release mod compilation passes with zero warnings/errors. The platform suite
executes the actual current render-controller methods and the production Quest
allocation policy:3078+ assertions and17 defect controls. It proves no live
allocation/MSAA setter on Quest Vulkan, unchanged saved values, bounded logging,
viewport-only downscaling and reachable original desktop setters. The affected
Frame materialization suite passes576 assertions and5 negative controls.

Full signed Player build and delivery verification are pending. Headset success
is not established by these tests. Next test: confirm B624 in the capture,
get beyond preparation into Intro/menu, then exercise Campaign/scenario loading.
