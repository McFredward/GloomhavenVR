# Steam Frame standalone: Build 594 interactive run

Evidence: `.planning/debug/steam_frame/LogOutput.log`, `Player.log`, and
`openxr-diagnostics.log`. The two game logs contain the same Build 594 session;
`Player.log` additionally includes the Debug wall-setting edits. No new GPU-busy
capture is available. Loading-indicator and initial scene-construction stalls
are excluded from the interactive priorities below, per the owner's ruling.

## Result

The owner's unchanged subjective performance is consistent with the logs.
After loading, town windows have approximately 42–69 ms median frame intervals;
the later scenario windows settle near 42 ms median and 79–80 ms p95
(`LogOutput.log` lines 5537 and 5933). A 72 Hz presentation interval is
13.89 ms. Build 594's desktop-camera scrub is confirmed active (lines 104–106,
450–451 and 1063–1064), and the earlier 597–764 ms Character UI capture-layer
spikes are not repeated in this route. The residual native scenario/UI camera
passes cost only a few milliseconds on the main thread. There is no controlled
same-route Build 593/594 comparison, so the logs neither prove a net speedup
nor support a claim that the scrub regressed performance. The measured outcome
remains too slow.

The stable scenario split assigns about 24 ms/frame to Update→LateUpdate,
9–10 ms to camera cull/submit, and 12–15 ms to the remaining blocked interval
(`LogOutput.log` lines 5541 and 5937). The blocked interval is **not** an
independent GPU-busy measurement; this player does not supply Unity frame-timing
samples. Frame time also does not track head distance closely in those windows.
Lowering eye resolution is therefore not a supported primary CPU remedy.

## Wall setting: observed edit, no effective change

`Player.log` lines 13279, 13293, 13300 and 13307 record the owner changing
`[WallFade] EvalIntervalSeconds` from approximately 1.00 to 0.99, back through
1.01, and finally 1.00 s. Every edit reports effective occlusion checks at
0.250 s and table rebuilds at 4.000 s. The underlying entry was saved and
displayed above its runtime clamp of 0.25 s. This is an options-range defect,
not evidence that a valid cadence failed to propagate. The 0.25 s cap is
intentional: the fade-in dwell is 0.20 s, and slower checks change its
debouncing behavior. Limit the control to its real range and normalize already
saved out-of-range values.

The table rebuild is a separate control, `[WallFade] RescanIntervalSeconds`.
At the current 4.0 s it can still produce a 280 ms frame after the scenario
transition (`LogOutput.log` line 4808, including 171 ms in `WallFade.Rescan`).
The diagnostic says committed phases still run atomically. Raising this setting
may reduce the number of such frames but cannot shorten one; a new or changed
wall may then use stale classification for longer. It is a compromise, not the
substitute for incremental commits.

## Interactive work to address next

| Priority | Evidence | Proposed work | Verification |
|---|---|---|---|
| Town UI conversion/fallback | One 30 s town window averages 18.79 ms/frame in `CanvasConversion` and 6.18 ms/frame in `ModalFallback`; a later 10 s window averages 8.61 ms/frame in `CanvasConversion.Late` and 10.58 ms/frame in `ModalFallback` (`LogOutput.log` lines 3730–3731 and 4034). These are separate or nested scopes that must not be summed without call-tree attribution. | Add bounded substep attribution for the persistent town path. Move repeated hierarchy discovery and ownership checks to native change events or indexed state where possible; keep the gameplay controllers and original visible widgets intact. | Same idle town and NPC-approach windows; compare substeps and total mod time, then verify native UI and multiplayer parity. |
| Sustained town CPU | Merchant catalog costs about 4.6–5.9 ms/frame in the sampled town windows; `Cards` dominates (`LogOutput.log` lines 2518–2519, 3730–3731, 4033–4034). `TownServiceCatalog.Tick` still visits every entry for navigation, held state, and `Entry.Tick`. | Index entries by rack/page and update cold entries on events. Tick only exposed, moving, and transition-warm cards; keep the native stock/price clone and its intermediate animation current before the page becomes visible locally or remotely. Remove redundant per-frame background/tooltip clone lookups after source identity changes. | Same filled merchant stand, same page transitions and two peers; compare catalog ms/frame and verify 1:1 card/price/stock animation. |
| Approach/reveal hitches | First enchantress approach is a 461 ms frame, with `Visit` 300 ms and native enhancement UI opening 211 ms (line 2270). A map-hand reveal is a 576 ms frame, including card mip work 248 ms and activation/fit 70 ms (line 2409). A second enchantress visit still reaches 242 ms (line 2794). | Prepare the original enhancement controller/UI and visible card artwork/mips while the map loading indicator is present, then publish it only when interaction begins. The existing inactive card-front clones are prepared, but their mip pass and activation still run on the reveal frame; move safe work earlier and budget any required OnEnable/layout work. | First and second approach, first and second fan reveal, no blank face or stale upgrade capacity; compare largest interactive frame. |
| Scenario wall spikes | Wall rebuild after transition: 280 ms total / 171 ms rescan (line 4808). | Stage census/classification and publish an unchanged snapshot without work; split the remaining atomic commit into bounded preparation plus a short final swap, retaining the current visual result. | Same scenario with fresh room reveals and wall fade; compare post-load p95 and worst commit frame, not only commit count. |
| Residual steady scenario CPU | Around 24 ms/frame in main-thread logic, with only part assigned to named mod scopes (`SPLIT` lines 5541, 5937). | Profile selected native `Update`/`LateUpdate` work on a representative post-load Frame run. Separately inspect repeated mod work such as hidden-window veiling and canvas conversion. Avoid adding full-scene scans to every Debug frame. | Attribute the unassigned time before modifying native game loops; compare matched 30 s windows. |

The prior 1.8–2.4 s merchant catalog creation occurs during map entry and is
lower priority while the loading indicator is visible. Further moving only
initialization work to loading will not erase recurring catalog or UI-conversion
costs.

If those changes do not reach an acceptable interactive rate, test optional
Frame-only compromises one at a time: a slower refresh/lower capture scale for
otherwise static VR panels (readability and animation parity cost), reduced
distant NPC ambient animation/effects (visible immersion cost), or a longer wall
table rebuild interval (increased wall-classification latency). Disabling
immersive NPCs or selecting the 2D map can be a diagnostic/fallback, but would
remove desired features and should not silently become a default. Keep the
owner's 3408-per-eye target for CPU A/B runs; prior resolution changes gave
little subjective benefit and these logs do not isolate GPU saturation. Fixed
foveated rendering would require a rendering-pipeline/XR implementation change
and is unlikely to remove the measured main-thread hitches by itself.

Build 595 implements only the wall-options range correction. Local validation:
`scripts/wire-tests.sh` passed 81/81 suites and 286,618 assertions; the strict
Release build had zero warnings and errors; documentation language checks and
the refactor guard's source, bundle and surface checks passed. The guard's final
compiled-form diff still reports differences against its much older baseline;
that expected historical drift is not a failed source checker. Headset speed
after this correction has not been measured.
