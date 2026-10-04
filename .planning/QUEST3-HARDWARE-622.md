# Quest B622 — actual XR screen samplers and bounded discovery ticks

## Verified B621 hardware evidence

`quest-capture-20261004T201420Z-fc2afb8a.zip` is verified against installed
APK bytes/hash, input, installation receipt and the actual B621 startup banner.
Capture SHA-256 is
`5bcaf8f056adbbf1a65015d36a2fd19b20ecaec41cca4f0b1e7205716dff7abc`.
The reported invisible Intro, absent ambient menu imagery and periodic hitches
persist. The ZIP supplies no new screenshot; the older B619 screenshot is not
attributed to B621. Previously confirmed audio, keyboard, Guildmaster explanation
and Quest options exclusions remain the baseline.

Decoded Intro pixels are nonuniform, and completed native capture and actual
FlatScreen consumer samples have identical hashes. At decoded frame 22, consumer
RGB maximum is 237; at frame 52 it is 209. The real consumer still uses
`Hidden/BlitCopy`. Unity 2021.3.5f1 changes this shader's screen-space source to a
texture-array sampler under the actual Quest instanced stereo configuration,
although FlatScreen owns ordinary left/right Tex2D captures. Plain desktop
sampling tests did not exercise this draw configuration. The original ambient
`Ambient_04_Cave_02.mov` also decodes and reaches the consumer. Its first frames
contain an authored dark fade, verified against original media; early black pixels
are not evidence of a decoder failure. The separate native promotion trailer is
intentionally idle and must not be forced to autoplay.

The latest 24 retained large frame spikes have median duration 102.47 ms and
median spacing 1.009 seconds, without GC increments. Scope discovery measures
81–94 ms during this same menu state, with 14,338 inspected scene behaviours.
This identifies a substantial periodic contributor, without excluding all other
work. Async pixel callback latency is not a main-thread CPU measurement.

The actual hidden Close Area has CanvasGroup alpha zero. B621 UI right-centre
pixels are transparent. A real CanvasRenderer GPU control confirms that alpha
zero suppresses the original blur even with `cullTransparentMesh=false` and
`cull=false`. Those flags therefore do not establish a hidden opaque occluder.
Authentic shader recovery is evaluated separately; native visibility is retained.

## Scoped candidate

The current shared mod selects the Quest world-screen shader only through its
central standalone gate. Both source captures remain plain 2D samplers, while the
vertex/output stages handle instanced or multiview headset layers. The GPU selects
left/right inputs for the same native depth policy. Mono, map and suspended-video
transitions reset both bindings and stereo routing on the persistent material
before released captures can remain referenced. Desktop keeps its existing shader
and per-camera eye swap. No additional full-size targets or copies are introduced
by this screen-draw correction; inherited completed-color restoration cost remains
a hardware measurement.

Actual two-eye GPU controls cover the old array-sampler defect, wrong eye routing,
alpha and persistent-material transitions. Android compiler validation requires
three real banks (mono, instancing, multiview), six vertex/fragment stages, plain
capture samplers and vertex eye selection. This proves the source/compiled path,
not the next visible headset outcome. Debug pixel observations retain bounded
sampling and include the actual URL/right-eye binding; the second far-video sample
waits past the authored initial fade.

Scope discovery must retain inactive, late-created, pooled and persistent native
widgets, including live `DontSave` owners. Exact typed queries preserve those
semantics and avoid repeating managed classification over every scene behaviour.
Seven batched typed queries were slower in local Mono (12.15 ms full scan versus
6.00 ms original), so that burst is not shipped as a demonstrated optimization.
Final scheduling and measured per-tick evidence are recorded with the native
candidate below. Existing tracked widgets retain per-frame enforcement and native
callback/availability behavior. Android frame-time improvement remains a hardware
gate; local timings cannot establish it.

This is still an original startup/menu slice. Full campaign assets, actual DLC
scenario recovery and authenticated Android crossplay remain separate port work.
Jaws of the Lion and Solo Scenarios ownership remains selected for this private
test, with the clearly labelled dummy identity.

## Hardware run

1. Extract the complete B622 Windows archive and run `Install-Quest.cmd`. Confirm
   embedded and installed B622. Retain app data and existing wireless settings.
2. Check Intro picture with sound, then the ambient menu background in both eyes.
   Report orientation, stereo depth, grey output and foreground overlaps separately.
3. Leave the menu visible for at least 60 seconds, then repeat in mixed reality.
   Report the recurring small hitch and collect with `Collect-Quest-Logs.cmd`.
   Supply its ZIP and screenshots of remaining image defects.
4. Repeat a warm launch without clearing app data. Compare native Intro-to-menu
   timing and any genuinely necessary preparation with the first launch.
5. Briefly recheck keyboard input, Guildmaster hint, Quest options exclusions and
   sound. These were already hardware-confirmed and should retain their behavior.

## Native candidate validation

Native build, final focused gates, packaging and cleanup receipts are added after
the clean source freeze completes. No observed B622 headset result is claimed.
