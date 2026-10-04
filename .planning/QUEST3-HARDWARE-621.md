# Quest B621 — completed movie capture and bounded menu discovery

## Verified B620 hardware evidence

The supplied `quest-capture-20261004T183903Z-38407163.zip` is verified by its
installed APK hash, input manifest, local install receipt and actual B620 startup
banner. Capture SHA-256 is
`190b2daa81d4222c2d7851950acb5241782858a9ccd22df9cdfadbd192a6b27a`.
The maintainer confirms the existing VR keyboard, standalone Guildmaster hint
and Quest option exclusions now work. Intro still has sound without visible
video, the menu ambient 3D preview is absent and periodic small hitches persist.
There is no new screenshot in this ZIP; older screenshots are not attributed to
this exact build.

Native Intro reaches decoded frame 97; the actual menu BackgroundView chooses
`Ambient_04_Cave_02.mov` (30,507,516 original bytes), starts playback and reaches
frame 261. Both B620 output adapters run against the actual FlatScreen capture.
Those counters and hook reports do not establish non-grey decoded/capture pixels
or a correct visible headset result. Native hidden PromotionVideo is a different
player: original scene and final native CIL keep showAtStart=false, playOnAwake=false
and Hidden. Its native manual Prepare/Play/Stop path remains unchanged. The compact
`B621-native-promotion-preview-proof.json` retains 25 checks across nine original,
prepared and stripped native methods. Do not force that trailer to autoplay.

The active-menu state spans 5,936 frames and 126.56 seconds: 160 frames exceed
40 ms and 135 exceed 100 ms, with eight GC collections. Repeated large-frame pairs
occur without a GC increment. Incremental GC is already active; maximum diagnostic
snapshot write is 3.16 ms. Neither GC nor state writing is established as the sole
cause. QuestGameScope actually performs a global loaded-MonoBehaviour scan every
second, including prefab assets later rejected by its scene filter. Other shared
periodic work remains a possible contributor; do not claim it has been excluded.

## Scoped changes and limits

B621 retains current shared mod source and central standalone gating. The movie
adapter finishes the owned camera-capture output immediately before the shared
eye-copy/display path, with bounded Debug pixel evidence. Near-plane movies draw
once at consumption. Far-plane movies retain their native camera depth draw and
snapshot the completed last actual camera sharing the owned target; final
sampling restores those completed pixels without redrawing far depth after the
engine discards its depth attachment. Later native foreground and separate UI
contributions remain covered by the real GPU fixture. The snapshot adds a scoped
color target and copies while a camera-plane movie is active; shipping GPU cost
remains a hardware measurement.
Native camera modes, target identity, movie URL selection, callbacks, playback
speed and audio remain native. Preserve authored alpha, foreground depth, aspect,
mips and native camera/consumer lifetimes. Desktop GPU success cannot establish
Android decoder pixels, GLES camera-plane ordering or the headset image.

QuestGameScope discovery visits roots of loaded scenes and its persistent owner's
scene using reusable lists, including inactive and late-spawned widgets. Exact
component classifications are cached by Type. Existing one-second enforcement,
original native callbacks and future widget discovery remain. Read-only Debug
scalars report scan count, actual scene-component count and last/worst scan costs
through existing startup state. No additional recurring file or trace stream is
introduced. Frame evidence retains the latest 24 spikes in a bounded ring:
`spikeCursor` is the next write slot, and populated timestamps can be sorted for
chronological analysis. Startup spikes no longer crowd out late menu evidence.
Normal logging does not enable frame sampling or expensive pixel readbacks.
Debug movie evidence takes at most two sets of 16×8-pixel observations per bound
movie. Async readback is used where supported; otherwise a bounded synchronous
fallback records actual pixels. `readbackElapsedMs` includes GPU/callback latency
for async requests and must not be interpreted as main-thread CPU work. Initial
one-off synchronous evidence is separate from sustained late-menu hitches.
The production scene collector passes 136 real Unity assertions and four defect
controls, plus 99 managed scope assertions, 17 controls and 17 actual original
SDK checks. With 126 live components and 4,127 loaded components including genuine
prefab assets, local Unity CPU averages are approximately 0.0067 ms scoped versus
0.570 ms globally. This establishes local cost and discovery behavior, not a
Quest frame-time gain or the sole cause of the hardware hitch.

Hardware-confirmed keyboard, tooltip/options and B619 audio repairs are retained.
This remains the original startup/menu slice, not full campaign content or proven
Android crossplay. Owned Jaws of the Lion and Solo Scenarios selection remains;
actual DLC startup asset recovery and genuine GOG/Epic ownership fixtures remain
separate recorded work in B620/B619 notes.

## Native UI blur follow-up

Read-only scene/material tracing identifies an authentic `UI_Blur` material on
MainMenu's right-side Close Area, referring to an unrecovered
`Custom/SimpleGrabPassBlur` shader. Its original bank contains three Grab/Draw pairs
and five distinct D3D11 programs, without a GLES bank. The original shader also
ignores UI vertex color, so absent vertex alpha is not established as a port defect.
The native window begins Hidden but remains active; whether its CanvasRenderer
actually draws is an unanswered runtime question. B621 records bounded actual
material/culling/alpha observations and separate UI/glass/right-center pixel data.
Do not replace the shader with a guessed blur or alter native visibility. A faithful
follow-up needs the original five programs, constants, render states and real UI
GrabPass/capture pixel validation. This candidate does not claim that recovery or
attribute the grey image to a material that might be culled.

## Hardware run

1. Extract the complete Windows archive and run `Install-Quest.cmd`; confirm
   embedded and installed B621. Existing application data and wireless ADB settings
   are retained. Do not clear application data between launches.
2. Check Intro picture with sound, then the ambient menu background with 3D depth.
   Report grey output, orientation, foreground overlap and doubled images
   separately. Existing keyboard, Guildmaster hint and filtered settings should
   retain their B620 behavior.
3. Leave the active menu visible for at least 60 seconds, then repeat with MR
   enabled. Report whether the periodic hitch remains. Collect with
   `Collect-Quest-Logs.cmd` and supply its ZIP plus relevant screenshots.
4. Repeat a warm launch without clearing data. Check native Intro-to-menu flow
   and whether preparation appears unnecessarily. Compare first/warm timings.

New bounded pixel observations and recent spike samples are diagnostics, not
claims that the headset defects are fixed. Build/package verification and measured
cleanup receipts will be recorded after the signed native candidate completes.
