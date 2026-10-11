# Steam Frame standalone: Build 586 at 1728 per eye

Evidence: `.planning/debug/steam_frame/Player.log` and `LogOutput.log` now both
identify ModBuild 586. The earlier Build 585 raw files were replaced; their
3408-per-eye measurements and screenshot observations were preserved in
`STEAM-FRAME-THIRD-HARDWARE.md` and `STEAM-FRAME-FOURTH-HARDWARE.md`. The two
Build 586 log files cover the same run, not two independent trials.

The OpenXR runtime requested 1728x1728 pixels per eye at Build 586 startup,
compared with 3408x3408 in the Build 585 run. The mod eye scale was 1.00 in
both. A brief in-menu mod-row test changed it to 0.95 (1642x1642) and then
back to 1.00, which confirms that the mod's scale lever can update the eye
target during a session. The SteamVR per-application resolution change was
already reflected at startup. These logs do not contain a controlled test of
whether SteamVR applies its own slider while the application is running.

| Run and eye target | Two late map windows: frame mean / p50 / p95 | Main-thread logic | Named mod work | Main-thread render loop |
|---|---:|---:|---:|---:|
| Build 585, 3408x3408 | 62.33 / 53.78 / 104.50 ms; 60.82 / 51.88 / 102.44 ms | 41.54; 40.46 ms/frame | 30.92; 30.39 ms/frame | 8.73; 8.52 ms/frame |
| Build 586, 1728x1728 | 60.14 / 51.76 / 105.90 ms; 59.41 / 51.88 / 104.38 ms | 42.08; 41.31 ms/frame | 34.22; 33.64 ms/frame | 5.88; 6.04 ms/frame |

The target has 74.3% fewer submitted pixels per frame, but the representative
map windows remain around 17 application FPS. The two runs changed both the
build and the headset setting and did not hold gaze and UI state identical, so
they cannot isolate Build 586's small optimizations. They do rule out eye-target
pixel count as the dominant cause of this map interval. The named mod work did
not show a measurable aggregate improvement. The graphics CPU submission lane
is somewhat shorter, but main-thread logic remains the wall. The XR GPU metric
continues to track frame interval; GPU busy time is still unmeasured. A lower
pixel target can leave GPU work in some views, and 3408 will need a new test
after CPU reductions.

At 1728, the sustained map cost is about 59-62 ms/frame with 40-43 ms/frame in
main-thread logic, 32-35 ms/frame of named mod work, 6-7 ms/frame in the
main-thread render loop, and 10-13 ms/frame of unclassified blocking/waiting.
Recurring named map work: `TownServicePresentation` 7.6-8.9 ms/frame,
`TownServicePresentation.Late` 3-5.7 ms/frame, `CanvasConversion.Late`
4-5.6 ms/frame, and `WorldUI.HiddenWindowVeil` around 2.5-2.7 ms/frame.
These scopes may nest, so they must not all be added to the mod total.
`ModalFallback.Convert` also spikes by hundreds of milliseconds when a native
window is re-converted. The scenario continues to have atomic wall-cache
commit hitches of about 84-261 ms; the live wall rescan setting is already
4.0 seconds, above the shipped 2.0-second default. Raising that setting only
reduces the *number* of hitches and increases reaction latency. It does not
shorten a hitch or solve steady map cost.

The current graphics settings are already near their lowest-cost values:
`Fastest`, shadows disabled, 0 pixel lights, MSAA 0, no soft particles,
automatic LOD idle skip enabled. Pixel count is therefore not the next useful
configuration lever. Keep 3408x3408 and mod eye scale 1.00 fixed for the next
CPU comparison. During an A/B benchmark, keep the same map location, visible
NPCs/windows, graphics settings and SteamVR per-app frame limit/motion-smoothing
setting. `[Perf] SceneProfile = false` can remove its occasional expensive
diagnostic scene census *after* the needed renderer evidence is captured; it
does not address the persistent 40+ ms logic lane. A short 4.0-versus-8.0-second
wall-rescan A/B can measure hitch frequency in a scenario, but 8.0 is not a
quality-preserving shipping recommendation without observing wall updates.

Steamworks declares a title VR-capable through a VR launch option owned by the
game publisher. A BepInEx mod running inside the original flat Gloomhaven app
does not own that Steamworks metadata. SteamVR recognizes the OpenXR session
after the mod starts, but the mod cannot make the original app appear as a VR
title in Steam's library before launch. Per-app SteamVR settings persist after
they have been opened; configuring them during one run and relaunching is the
practical route. Valve's source:
https://partner.steamgames.com/doc/features/steamvr/settings.

Build 587 removes hidden merchant-page mirror work, retains full veil discovery
while dropping an impossible ancestor test, and adds bounded Debug timing for
town, veil and slow modal conversion stages. These are source-proven reductions
and attribution, not a measured headset gain. The next run at 3408 should
compare matching map windows and show which conversion stage repeats. Preventing
redundant native-window conversion and shortening wall commits remain separate
follow-up work because their current lifecycle is input-critical. Native 72 Hz requires a 13.89 ms
application frame budget, far below the present map median; no available
configuration change alone establishes that goal.
