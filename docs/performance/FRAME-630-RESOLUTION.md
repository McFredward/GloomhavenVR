# Frame630 live resolution and render-resource transitions

The immutable Frame capture is Build628 / `176beb741`, D3D11 MultiPass OpenXR,
3408x3408 per eye. `Player.log` lines 17276 onward record 1.00 -> 0.80, creation of
both 2728x2728 eye resources, then 0.80 -> 0.85 and creation of two further eye
resources immediately before the abrupt end. No managed exception or crash dump
in the supplied input establishes the native failure mechanism. The last
successful resource-create message is not evidence that the next native graphics
operation succeeded. This change removes the repeated live allocation transition
rather than suppressing an exception or claiming a diagnosed driver defect.

## Behavior

`EyeResolutionScale` retains its saved key, default and 0.5–2.0 range. PC and Frame
share every code path and asset. Before loader initialization, the saved value
selects a startup allocation of `max(1, saved value)`. Newly initialized displays
receive that allocation, viewport and MSAA before `StartSubsystems`. Existing
live displays are adopted without allocation writes. Capacity belongs to the XR
session, not to scenario/menu/map rig replacement.

The subsequent Build631 Frame startup report found `XR_ERROR_RUNTIME_UNAVAILABLE`
at native extension enumeration/instance creation. The follow-up restores the
pre630 initialization boundary: selection now runs only after successful loader
initialization, and only initialized stopped displays receive startup setters.
Both early legacy XR setters and early native MSAA assignment are removed. This
removes the newly introduced startup interaction; the supplied capture alone
does not establish why the native runtime was unavailable or prove hardware recovery.

During play, the existing `Rig.RenderQuality` Update tail coalesces resolution
requests after 0.35s without a new slider value. The latest request changes only
`renderViewportScale = min(request / startup allocation, 1)`. No camera projection,
rect, culling mask or field of view is changed. For example, a native 1.0 startup
can change live to 0.8, 0.85 and back to 1.0 without a new XR allocation. A 1.5 startup
returns to native effective resolution using viewport `1 / 1.5`, rather than
incorrectly rendering the entire larger target.

A request larger than the session's capacity remains saved and capped until the
next VR restart. Starting at 1.0 and selecting 1.5 therefore keeps the current 1.0
render while preserving 1.5 for the next start. This is stated in English/German
player help and in ordinary bounded transition logs. Startup does not reserve a 2x
linear/4x-area target merely to make every possible later slider increase live.
The viewport route avoids texture recreation and scales rendered pixel work; it
does not shrink the allocated targets' VRAM footprint during that session.

The live viewport setter waits while `Camera.current` is present. A deferred head
camera receives an explicit bounded refusal, since Unity 2021 does not support
this viewport route there. Provider refusal is reported honestly and never falls
back to live allocation. At startup, only managed `ArgumentException` and
`InvalidOperationException` setter-validation refusals receive an explicit warning
and retain the existing provider setting; native crashes, allocation failures and
arbitrary exceptions are not converted into success. Actual accepted startup
capacity is read back before later viewport ratios are selected. Engine getter acceptance is explicitly distinct from
rendered-pixel proof. Delayed diagnostics also query each native
`XRRenderParameter.viewport` while retaining native projection.

MSAA remains configurable and live. It inherently changes multisample resources,
so its intermediate cycles are coalesced after 0.35s, with at least 1s between
committed resource changes. Native graphics-profile swaps still get corrected
immediately toward the last committed MSAA value while the next request waits.
This limits transition bursts; it does not prove that arbitrary native MSAA
changes are crash-free on the untested provider.

## Evidence and limits

The complete production `RenderQuality.cs` executes in the focused lifecycle
fixture, with source-bound startup-call ordering from `OpenXRBootstrap.cs` and
source-derived PC/Frame defaults. The explicit XR-provider boundary model records
actual allocation/viewport/MSAA calls from that source, models refusal and a
running display, and supplies render-parameter readback separately from a setter.
It is not a native OpenXR runtime or a headset picture.

The initial proof passes 48 production assertions and 13 causal negative controls:
live allocation, missing slider quiet period, wrong capacity ratio, erased saved
request, missing MSAA quiet/spacing, failed native MSAA restoration, ignored camera
boundary/deferred restriction, allocation fallback after provider refusal and
hot-reload allocation, aborted startup after a managed validation refusal and ignored
startup capacity readback. Final production recompilation restores the original source
after those controls and records source/bootstrap/fixture/executable hashes.
Focused strict Release/Debug and option/help/documentation gates are recorded in the
worker's gitignored `frame630-resolution-*` evidence. The primary integration gate
must still run after all lanes merge; this note does not certify the full tree.

Next Frame and PC checks: rapid 1.00 -> 0.80 -> 0.85 changes; 1.0 restoration; scenario
and map transitions retaining effective resolution; a larger saved value on the
next VR start; per-eye native viewport/readback and full uncropped field of view;
rapid MSAA cycles and native game-profile swaps. Verify stable play and stereo
separately from the accepted loading stalls. This package does not establish a
new FPS result or multiplayer headroom.

Unity 2021 documents the allocation setter and recommends viewport scaling for
runtime changes; viewport updates do not reallocate eye textures, apply on the
next frame and reject rendering-time/deferred changes:
[eyeTextureResolutionScale](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/XR.XRSettings-eyeTextureResolutionScale.html),
[renderViewportScale](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/XR.XRSettings-renderViewportScale.html).
The 2021.3 XRDisplay bindings expose startup target scale, viewport scale and native
render parameters:
[UnityCsReference XRDisplaySubsystem](https://github.com/Unity-Technologies/UnityCsReference/blob/2021.3/Modules/XR/Subsystems/Display/XRDisplaySubsystem.bindings.cs).
