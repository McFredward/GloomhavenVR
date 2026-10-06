# Frame 631 VR startup and quality-control follow-up

The supplied Frame and PC captures both identify ModBuild 631 / `674ea6bc4`, built
2026-10-06 17:32:41 UTC. The maintainer confirms that only the mod was updated
between the last working Frame run and these failed starts. Hardware inputs were
copied before investigation into the main checkout's ignored
`.planning/debug/frame-startup-regression-20261006T174635Z/inputs/{frame,pc}/`.
`input-manifest.json` records all six original paths, timestamps, lengths and SHA256s.
There is no new screenshot supplied with these startup logs.

All line references below are one-based raw LF rows, including blank rows, within
that immutable snapshot. Do not substitute older captures or folded triage output.

## What the Frame capture proves

Frame `LogOutput.log`17/43 and PC17/71 show the same build. Frame24 includes the
exact `--gloomhavenvr` opt-in argument and D3D11 on Turnip Adreno750. Frame25 has
graphics jobs enabled;26 confirms the installed display/input descriptors. This
is an attempted VR launch, not an opt-in rejection or an absent mod/preloader.

Frame28-31 select the existing `C:\openxr\wineopenxr64.json` default and then
report a null active loader. Frame `Player.log`105-110 records three failed native
extension-enumeration calls and failed instance creation, all with
`XR_ERROR_RUNTIME_UNAVAILABLE`. `LogOutput.log`33/43 then records the flat fallback.
No mod display-quality setup or live resolution change was reached in this run.

The accumulated Frame `openxr-diagnostics.log`3169 records a successful connection
through the same default manifest at 15:52:03;3268/3300 record failures at 19:34:56
and 19:36:05. Its missing `xrInitializeLoaderKHR` entry appears in both successful
and failed reports, so that entry alone does not explain this regression. The
diagnostic history does not include a build banner for every older section.

The loader result does not identify the underlying native failure. The
[Khronos loader implementation](https://github.com/KhronosGroup/OpenXR-SDK-Source/blob/main/src/loader/runtime_interface.cpp)
collapses multiple discovery/library/negotiation failures into this result.
Neither a graphics-memory diagnosis nor a broken Steam shortcut is established
by the supplied reports. The Windows/Unix runtime manifests, Proton stderr and
SteamVR loader log were not included; no remote access to the headset is available.

## Source changes and their limits

The 629-to-631 diff leaves the Frame launcher, opt-in gate, native payload and runtime
candidate policy unchanged. Build 630 newly placed `RenderQuality.PrepareSession`
before runtime selection/loader initialization, including legacy XR allocation,
legacy viewport and native anti-aliasing writes. Although display setup was not
reached, these earlier writes were new startup interactions and therefore remain
suspect. Their contribution to `XR_ERROR_RUNTIME_UNAVAILABLE` is not proven.

The follow-up restores the previous native initialization boundary. Quality
selection runs after successful `InitXRSDK`, beyond the failed-loader return.
Selection itself performs no resource setter. Only initialized stopped displays
receive allocation, viewport and MSAA before `StartSubsystems`. Reused running
displays remain adopted without allocation changes. Failed startup records a
bounded error identifying the failure as preceding mod quality setup.

The independent PC audit finds five profile edits without a subsequent XR MSAA
commit, despite active quality enforcement. The source guards introduced in 630
require null `Camera.current`; existing `VRRigDriver.Update` and
`CanvasConversion.6.Hide` already document stale camera references during valid
MonoBehaviour frame updates. The actual pointer was not sampled in this capture,
so attributing this specific hardware omission to it is an inference.

The unchanged `Rig.RenderQuality` tail step now calls `TickFromUpdate`. This
explicit Update entry can commit coalesced resources despite a stale camera;
unmarked `Tick` callers retain their camera guard. Permission is call-local, with
no phase latch to leak into a later rendering call. The tail's order is unchanged.
Saved controls, common PC/Frame code, 0.35s debounce, 1s MSAA spacing, deferred
viewport refusal and the prohibition of live allocation fallback remain intact.

## Local verification and next hardware run

The complete production RenderQuality lifecycle passes 65 assertions, 17 causal
runtime controls and 2 startup-order controls, with 14 source bindings including
the actual rig Update entry. Controls reintroduce pre-initialization resource
writes, blocked stale-camera Update commits, unguarded rendering calls, allocation
fallback, missing debounce/spacing and lost startup-capacity readback. The boundary
explicitly models an XR provider: it does not reproduce native Wine/OpenXR startup.
Strict Release/Debug builds report 0 errors/0 warnings; 14/14 source checks pass.

The broader worker refactor gate additionally exposed a test-inventory omission
already present at base 366884877: the NPC 632 suites `town-first-picture632` and
`town-quiet-controller` were missing from the expected set. This was handed to
the main agent, who reports its correction at 9903c288d. The worker does not alter
NPC tests, protocol/version or push `dev`. The integrator must validate the final
combined tree; focused evidence is not a complete integration gate.

Next hardware priorities are a cold Frame VR launch from its existing entry,
verified display RUNNING and both eyes; then fully loaded scenario changes among
graphics profiles, actual MSAA/readback transitions and rapid 1.00→0.80→0.85→1.00
viewport changes without resource reallocation or cropped view. If native startup
still fails before quality setup, retain the underlying Proton/SteamVR loader
output rather than changing geometry controls blindly. Startup recovery, headset
pixels, stability and multiplayer headroom remain hardware-unverified.

See [the PC settings audit](FRAME-631-PC-SETTINGS-AUDIT.md) for achieved figure,
effect, scenery and material changes and the limits of the captured A/B timing.
