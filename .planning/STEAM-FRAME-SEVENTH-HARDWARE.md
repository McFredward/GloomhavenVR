# Steam Frame standalone: Build 593 interactive run

Evidence: `.planning/debug/steam_frame/LogOutput.log`, `Player.log`,
`openxr-diagnostics.log`, and `20261001082548_1.jpg` from the same run. Both
game logs identify ModBuild 593. The headset uses a 3408×3408 per-eye target,
MultiPass, MSAA 1, and the native Fastest quality preset. The screenshot shows
SteamVR's flat application panel with the native gamepad-connection dialog and
2D game UI, while the VR town is visible behind it. The panel is not evidence
that the same 2D UI was presented inside the mod's VR windows.

The measured headset result supports the owner's observation that Build 593
has not made the application consistently smooth. Its scenario windows settle
at roughly 41–52 ms median frame intervals, and the interactive town windows
at 47–57 ms, with a 53 ms median/106 ms p95 window later in town. These exceed
the 13.89 ms interval needed for 72 Hz without reprojection. Scene/window
content differs across the samples, so comparing a single Build 592 and Build
593 median is not a controlled before/after benchmark. The mod's named work
remains significant, especially in town:

| Interaction or sustained cost | Build 593 evidence | Consequence |
|---|---:|---|
| Merchant public catalog, first creation | frame 12078: 1839.51 ms in `TownPublicStock.Catalog`, including 1810.77 ms `Census` | Large hitch during map entry; this occurs after the map scene begins but may overlap its loading presentation. |
| Merchant catalog, while walking among NPCs | generally 5.76–6.16 ms per frame; `Cards` 4.06–4.31 ms per frame, individual `Census` calls 14–23 ms | Continuous main-thread cost even away from the merchant. Nested scopes must not be added together. |
| Other steady town presentation | `Rig.Update` 4–5 ms/frame, `WorldUI.HiddenWindowVeil` 2.4–2.6 ms/frame, `CanvasConversion.Late` 4–8 ms/frame in measured windows | These remain separate main-thread targets after catalog refresh is reduced. |
| First priestess approach | frame 12455: 379 ms total, `TownServicePresentation.Visit` 227 ms, `FolioOpen` 193 ms | Visible interaction hitch. |
| Enchantress approach | frame 12600: 981 ms total; `CanvasConversion.Late` 597 ms and `Visit` 289 ms, including `NativeOpen` 207 ms; a nearby line reports 3050 late `New Party display` transforms | Still the largest interactive NPC hitch. |
| Character UI repopulation near the enchantress | frame 12855: 805 ms total, `CanvasConversion.Late` 764 ms; the adjacent log records 2660 late transforms in `New Party display` joining the supersample layer | The repeated capture-layer sweep is a second source of hitch after loading. |
| Map hand reveal after prewarming | 10/10 and later 9/9 card fronts were prepared before reveal with zero synchronous builds; frame 12858 still spends 149 ms in `Net.CardAppearance.Mips` and 42 ms in `ActivateFit` | Prewarming removed clone construction from that reveal, but activation/mip work still blocks a frame. |
| Scenario wall commits | `WallFade.Rescan` reaches 73–171 ms on named spikes; `WallFade.Late` generally about 1–4 ms/frame | Lengthening a rescan interval can reduce frequency, not a single commit's cost. |

The owner explicitly accepts a slower initial load and stalls while the loading
indicator is present. Therefore the 1.84 s merchant catalog build is diagnostic
context, not the immediate optimization target. The repeated 6 ms catalog work
and the approach/fan hitches after loading have priority.

The effective wall settings in this trace are **4.00 s** for
`[WallFade] RescanIntervalSeconds` and **0.250 s** for
`[WallFade] EvalIntervalSeconds`, regardless of the owner's intended 1 s wall
check. The two controls measure different work. The latter is capped at 0.25 s
by the mod; a typed 1 s value is not a valid effective visibility cadence.
Most steady scenario rescan cycles skip their atomic commit after the drift
survey, but committed cycles still have large one-frame costs. In the town,
`WallFade.Late` falls to roughly 0.003–0.005 ms/frame, so wall tuning cannot
explain the town slowdown.

Source review explains the two large `CanvasConversion.Late` spikes: the
supersample layer pass checked each of 3050, then 2660 newly repopulated
Character UI transforms against a linearly scanned restoration ledger. That
is quadratic work during interactive NPC entry. A reference-identity lookup
can keep the same ordered restoration ledger while avoiding repeated scans.

The screenshot's desktop 2D game pass has a separate cost. The log has no
`ITEM9 desktop mirror`/`desktop scrub` line, and the camera census shows both
`ScenarioCamera` and `UI Camera` writing to the backbuffer. This indicates the
saved `DesktopMirrorLeftEye` setting was likely disabled in this run, so the
normal game screen was rendered in addition to the HMD. The scenario camera
submission averages about 2.3 ms/frame and the UI camera about 1.1 ms/frame
in several stable scenario windows. Those are main-thread camera intervals,
not verified GPU busy time; a Frame-only mirror-sink change needs its own
headset measurement. The native gamepad-connection popup is a separate
screen-space overlay that does not contribute to gameplay in VR.

The run contains long stalls that the named mod scopes do not explain, including
menu frames of 7.47 and 6.14 s and scenario frame 8170 of 4.07 s with only
22.53 ms named mod work. These remain unattributed; they cannot be labeled a
GPU bottleneck from the current logs. The XR GPU metric in this player does not
isolate busy time. The Frame's falling presentation rate is consistent with the
large CPU frame intervals, but it is not a substitute for an independent GPU
capture. Memory samples and steady scenario medians do not establish a leak.

Next hardware comparison should hold the same map/scenario, 3408 eye target,
graphics preset and wall settings fixed. Check the enchantress book's upgrade
point count, whether the flat SteamVR panel loses its native gamepad popup and
2D game UI, the first and second approach to each NPC, the first and second
map hand reveal, and merchant browsing after the stand is fully populated.
The worker fixes following this report are source-proven changes; their headset
result remains unverified until that comparison.

Build 594 implements the narrow follow-up: preserve the enchantress's native
capacity label outside the veiled card-list alpha, index the supersample
capture's ordered restoration ledger by transform identity, defer cold merchant
row initialization until before exposure, force the already-existing desktop
sink/draw skip on Frame VR, and suppress the native flat gamepad prompt in VR.
No native UI controller was disabled, and the 2D menu keeps its own source
capture when it is intentionally visible in VR. Actual wall-setting edits were
not present as value logs in Build 593; Build 594 adds Debug `WALL CADENCE EDIT`
lines with stored old/new and effective values so a future run can distinguish
a failed edit from the wrong control or a clamped value. Source review confirms
the two live controls are read by the wall driver: rescan scheduling takes the
live config at each new cycle, and fade evaluation reads its effective interval
at each tick. In this run the visible post-edit values stayed 4.00/0.250 s.
