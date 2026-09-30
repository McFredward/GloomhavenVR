# Steam Frame standalone: Build 592 at 3408 per eye

Evidence: `.planning/debug/steam_frame/LogOutput.log` and `Player.log` from the
same run, both identifying `v1.1.0 build 351c101be [dev]`, ModBuild 592. The
OpenXR runtime is SteamVR 2.17.10, MultiPass, with a 3408×3408 eye target,
mod eye scale 1.00, graphics jobs enabled, the Fastest quality preset, no
shadows or MSAA, and no pixel lights. These are CPU main-thread timings, not a
controlled GPU benchmark. The XR GPU counter sometimes follows the entire
frame interval and cannot be read as GPU busy time. The 72 Hz headset target
allows 13.89 ms per application frame.

| Scene/window | Representative frame p50 | Mod work and important peaks | Interpretation |
|---|---:|---|---|
| Menu after loading | 13.9 ms | Usually about 2 ms/frame; isolated 7.46 s and 6.08 s stalls carry only 2.57 and 1.47 ms of named mod work | Startup stalls exist outside the timed mod steps. No persistent menu slowdown is shown. |
| Town/map, after entering | Mostly 41–55 ms; one active window 68 ms | Merchant catalog 5.65–6.84 ms/frame; `CanvasConversion.Late` often 4–7.5 ms/frame | The map has sustained main-thread cost even with few visible world renderers. |
| Scenario | 43–48 ms throughout the sampled play | `WallFade.Late` about 2–4 ms/frame; `Cards.Driver` about 1–4 ms/frame; main-thread render loop about 11–13 ms/frame | Scenario pacing stays far beyond the 72 Hz budget. Later windows do not show a steadily rising median. |

The dominant *mod-attributed* one-off map hitch is frame 3674: 3373 ms total,
3066 ms named mod work, of which `TownPublicStock.Catalog` takes 2648 ms as
the persistent merchant item display is constructed. This is nested inside
`TownServicePresentation.PublicStock` and must not be added to it. Frame 3665
is another 3739 ms total with 2422 ms named mod work, including 1208 ms in
`Rig.MapRoom` and 795 ms in `TownServicePopulation`. Nearby frames show
`ModalFallback.Convert` at 765 ms (frame 3668) and `CanvasConversion` at
305–335 ms (frames 3670 and 5203). Frame 4145 spends 692 ms in
`Net.CardAppearance.Build`; frame 6657 spends 477 ms building VR Options.
These are plausible moments for a visible freeze, not frame-to-frame jitter.

The recurring map cost has a clear first target: `TownPublicStock.Catalog`
continues to run every frame after construction, generally consuming around
6 ms. It includes row refresh and card/interaction updates, and is a nested
scope of the merchant presentation. It should be profiled into change
detection, visible card updates, and hidden page work before changing it;
the merchant's original card appearance, current page and multiplayer
animation must remain 1:1. Next, remove redundant native-window conversions
at their lifecycle source. The 300–765 ms conversion spikes are too large to
hide by lowering rendering quality. Incremental catalog creation might avoid
the 2.65 s first-frame stall, but the stand must never expose incomplete
stock to a player; prewarming or a bounded atomic visual reveal needs a
hardware check.

In the scenario, committed `WallFade.Rescan` calls repeatedly cost about
90–103 ms, with initial wall commits occasionally higher. The live rescan
interval is already 4 s. Lengthening that interval only spreads the hitches
out and delays wall response; it does not shorten a hitch. A safe fix needs a
staged or cached computation with an atomic final visual commit so wall
visibility cannot flicker or disagree between eyes. Card art and card-driver
spikes reach about 160–225 ms in some scenario windows and warrant a separate
cache/lifecycle trace. The scenario census shows roughly 1500 visible
renderers, and the CPU render loop averages 11–13 ms/frame before other
logic; draw submission also needs work to reach 72 Hz.

Some large stalls are not explained by the named mod scopes: frames 1133 and
1215 in the menu take 7.46 s and 6.08 s, but named mod work is below 3 ms;
scenario frames 5456 and 5488 take 1.29 s and 0.93 s with only 25–32 ms of
named mod work. This does not prove a GPU bottleneck: game code, synchronous
loading, compositor waits and uninstrumented work remain possible. Capture
SteamVR compositor/Frame performance data and correlate it with frame numbers
before assigning these stalls to GPU. The run has no fatal exception in the
mod log; one deferred ActorBar zoom warning is recoverable. GC counts and
late heap samples do not establish a growing leak, and the scenario medians
remain broadly stable across the measured windows.

Recommended next comparison: keep 3408 per eye, quality and location fixed;
measure a repeatable pass at the merchant stand and a stationary scenario
view. Record which headset-visible pauses line up with the initial map-entry
spikes, merchant interaction, window conversion, wall changes and card art.
That mapping will decide whether the first implementation pass should target
the merchant's recurring work or the scenario's atomic hitches. Do not use
`[Perf] FRAME`'s XR GPU field as a hardware GPU measurement in this run.
