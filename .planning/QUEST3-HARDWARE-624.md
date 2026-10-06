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
allocation policy:3081 assertions and17 defect controls. It proves no live
allocation/MSAA setter on Quest Vulkan, unchanged saved values, bounded logging,
viewport-only downscaling and reachable original desktop setters. The affected
Frame materialization suite passes576 assertions and5 negative controls.

All378 affected builder tests pass. The warm build reuses1,614 unchanged staged
original preparation inputs and does not rerun the exhaustive program compiler
audit. Actual delivered Shader gates retain688 Shaders/51,564 original aliases;
all13 original ComputeShaders/36 Vulkan kernels retain their executable bytes.

The signed ARM64 IL2CPP Player contains14 scenes: the Quest bootstrap and all13
original scenes. Independent readback of its actual `globalgamemanagers` confirms
MSAA0 in all six native quality levels; all APK member CRCs pass. APK size is
2,494,104,762 bytes, SHA `7b16dabefd36c1036052f74fa784cf25ad1017c5880f029e40c849c7e47a7d7e`.
The adjacent native bank is10,912,754,848 bytes, SHA
`0831f24bf61cd77bc0ada3a8c8eb41bf4454b826e638b8019686228fbf03e310`.
The frozen runtime commit is `59d787816d55a2664c83e3da188b74c9d7761591`, input
`c85cbfcb18abf929985fb835e0556767c74cc62f481463b9ce3608072a15389e`.

The prior independently accepted whole-bank census is reused only for6,365
byte-identical payloads. One native bundle and its catalog change. Actual native
object comparison retains every public root and all other object bytes; only six
Shader serializations differ. A targeted independent observer verifies4,296
byte-identical executable programs and all their original alias metadata, with
zero compiler queries. Actual catalog comparison retains all17,354 lookup keys'
bucket associations and16,715 location dependency edges, original asset paths,
providers and types. One bundle filename and its derived dependency hashes change.
These proofs are recorded in the private build evidence alongside the accepted
B623 independent census, rather than claiming the entire bank is byte-identical.

Native Build ID `eb8f47caa41067ae` matches both the signed Player and retained
debug symbols. Debug information/line tables survive, and all1,150 actual native
source/managed backup files are hashed.

The verified Windows archive is `GloomhavenVR-Quest-B624-Windows.zip`,
13,408,148,631 bytes, SHA
`659a8d78960f3d3d84f128bd6499fd2e143af551bbac1eb7be749373fad40c54`.
Independent ZIP64/member-CRC verification passes for every actual member,
including exact nested APK/native-bank hashes and all50 current installer source
hashes. The private audit initially assumed a top-level handoff `target`; the
real schema stores it in `buildReport.target`. That read-only audit assumption
is corrected, and its initial failure remains archived. No delivered file or
production schema changes were required.

Headset success is not established by these tests. Confirm B624 in the next
capture, get beyond preparation into Intro/menu, then exercise Campaign/scenario
loading, saves, audio, controllers and passthrough. Stored high eye-resolution
and MSAA choices must not prevent startup; their effective Quest Vulkan values
are viewport scale at most1.0 and shared startup MSAA0. Fresh configurations
use the same shared standalone defaults as Steam Frame. Keep existing native
saves: install the complete Windows package with its adjacent content bank using
`scripts/install-quest-wireless.cmd`; collect evidence with
`scripts/collect-quest-logs.cmd`.

## Cleanup after verified delivery

The main ignored hardware directory now contains only B624 APK/Windows archive,
the current adjacent Campaign bank and current identity/provenance receipts.
Supplied captures remain in `quest3_probleme/`.

19 completed clean Quest worker worktrees and20 reproducible clean historical
mod snapshots are removed after fresh status/commit, active-process and incoming
link checks. Git branches and local source commit refs survive. Unique untracked
dependencies and small historical validation receipts are archived privately.
The dirty worker and eight dirty/unknown snapshots are preserved.

Obsolete B622 project/build outputs, B620/B623 symbol payloads, a rejected partial
bank and superseded hardware packages are removed only after matching B624 symbols
and verified Windows delivery exist. Current original inputs, warm project,
Library/Shader caches, saves, captures and other agents' checkouts are preserved.
Free space increases68,756,516,864 bytes during this operation; this is a measured
shared-filesystem change rather than exclusive attribution of every byte. The
owned16-GiB temporary build swap is disabled and removed separately; existing
system swap is untouched. Private receipts are `B624-cleanup.json` and
`B624-build-swap-cleanup.json` under the build evidence directory.
