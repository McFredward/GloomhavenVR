# Build 654: reliable Auto wall protection and Graphics placement

The maintainer's new Steam Frame report says Auto did not hide walls after all
rooms opened, while manual Hide all made the scenario substantially smoother.
The later instruction places this quality/performance control in Graphics.
Development starts from current dev `c865f4a87` / Build 653. Both supplied build
banners identify that exact build. The Frame folder contains three logs and no
screenshots in this capture.

## Hardware evidence and cause

The complete read-only audit, input hashes, all 21 published FRAME windows and
physical line references are retained in `.planning/debug/frame654-audit/`.
Native room generation/material work was finished by Player.log line 10717:
roomReveal=False, pending generation/material handles zero. Optional background
mask/card/figure preparation nevertheless remained pending until the 90-second
coordinator timeout at lines 11776–11777. The broad IsPreparing flag therefore
vetoed Auto long after the playable room had loaded. That coordinator explicitly
keeps original input available; its progress reports also prove both native
SceneController loading flags were false when those reports executed.

The open VR Options window was a second eligibility veto. It remained open until
Player.log line 12646, after manual Hide all and the final published FRAME
summary. These are source-proven sufficient blockers matching the supplied run.
The original build does not report desktop focus or every rejected Auto sample;
it does not independently prove the initial saved threshold/mode. The maintainer
reports Auto selected; the first wall-mode change in the capture is manual mode 1.

Manual selection at LogOutput.log line 6252 reaches the implementation at line
6268 and masks 845 eligible renderers. The best published comparable windows are
lines 6253–6257 before and 6596–6600 afterwards:

| Application-frame measurement | Before | Hide all |
| --- | ---: | ---: |
| Mean frame interval | 101.81 ms, about 9.82 FPS | 81.57 ms, about 12.26 FPS |
| Median frame interval | 95.54 ms | 63.33 ms |
| WorldMaterial.PreCull | 21.082 ms | 7.811 ms |
| WallFade.Late | 7.445 ms | 1.370 ms |
| ScenarioTerrain.PreCull | 6.206 ms | 3.132 ms |

Material work falls 62.9%, wall scope 81.6%, terrain camera work 49.5%.
The hidden-candidate counter independently records 390610 source skips.
Both windows contain the open options/menu cameras, roughly four callbacks per
application frame. Scope averages already include both eyes; do not double them.
The residual wall scope contains the permitted lifecycle collector, not ordinary
fade decisions. Room-renderer entry count 24 is not a count of player-visible rooms.

Pose changes, tracking interruption and one 4.338-second hitch affect this
comparison. No fully closed-options post-manual FRAME summary exists before EOF.
Reported runtime refresh is not presented FPS; the GPU figures contain invalid
wait/interval values and do not establish GPU busy time. The logs prove useful
manual work removal and corroborate the user's report, not a controlled FPS gain
for the repaired Auto policy or every original/DLC scenario.

## Repair and resulting behavior

Auto and hidden-inventory refresh now wait only for actual native scene loading
and pending room reveal. Optional background preparation no longer vetoes them.
Ongoing open VR Options no longer suspends performance protection. Desktop-window
focus is replaced by the cached current OpenXR input-focus observation; explicit
loss resets the window, while an unknown observation does not create a permanent
veto for a running loaded VR scenario.

The existing half-second loading-edge grace, two-second window, per-frame 250ms
hitch cap, editable 15 FPS default and scenario latch remain. Selecting Regular
restores walls; the latch avoids repeated hide/show caused by its own savings.
Wall/attachment membership, door-frame/arch exceptions, collision, game state,
wire recovery, material/terrain consumers and manual modes are unchanged.
The eligibility path removes the previous cosmetic, UI and desktop polls and
adds only a nullable managed cache read. There is no extra native call, camera,
scene census, timer or recurring log formatting.

The existing enabled OpenXR feature populates the cache through its real session
state callback. Shipped managed/native ABI evidence proves Focused=5 and
Stopping=6. An initial unshipped checkpoint and fixture shared the mistaken value 6
assumption; verification caught it before release. Production now uses the actual
shipped XrSessionState.Focused enum. The corrected tests and four causal mutations
reject the wrong value and stale/unbegun ownership. Session events have no handle,
so this is current-feature/begun-session protection, not a claim to detect arbitrary
misordered callbacks after a replacement session begins. Blend/compositor behavior
is unchanged. Detailed evidence: `docs/performance/FRAME-654-XR-FOCUS.md`.

Both persisted controls now appear first under Graphics → Scene details
(German: Grafik → Szenendetails), beside the geometry compromises. Existing
view-dependent fade controls stay under Comfort. Keys, choice indexes, defaults,
localization and explicit graphics-preset choices remain. In particular the last
manual Hide all choice is preserved: select Automatic again to test the repair.

## Validation and integration scope

Final production checkpoint is `173f4b1ed`; the later focus-proof commit changes
only tests/developer documentation. The primary final affected subset passes all
six scopes: wall-performance-runtime, wall-options, openxr-blend-probe,
frame-mr-presentation, frame-mr-options and mr-scenario. It includes 695 actual
Unity wall assertions/all 22 causal controls, 75 options assertions/all 14 variants,
1304 native-boundary focus/blend assertions, original MR pixel/UI scopes and the
scenario backing regression scope. The focus worker additionally rejects all
four causal source mutations. All 910 older blend assertions remain; the corrected
394 focus/lifecycle assertions cover both profiles and stopping/recreation/ownership.

The wall fixture replays cosmetic preparation=true, options open, desktop focus
false and XR focus=true: representative 19 FPS keeps the first room visible;
100–125ms sustained frame intervals trigger Auto. Actual native loading/reveal
and explicit focus loss still reject samples and require a fresh grace/window.
Dirty hidden inventory can progress during cosmetic preparation. Native renderer
flags, scene handles, MPBs and pixels execute; original controllers, collector,
clock and focus boundaries are explicit models. The native focus fixture compiles
actual production VRSession/feature/probe, with native calls/Unity dispatch modeled.
These tests do not certify headset appearance, connected multiplayer or hardware FPS.

All 16 final source checks pass. Strict Debug/Release both report zero errors and
warnings; the rebuilt direct golden executable passes 299715 assertions. All four
UnityFS7/Unity2021.3.5 banks, 1594 figure derivatives/66 parts and five bilingual
player-document pairs pass. Surface census remains 665 config keys, 235 Harmony-text
entries and 4794 log tokens, with no removals. Private compiled comparison preserves
1247 types: only WallSegmentFade, VRSession, OpenXrEnvironmentBlendFeature and
VROptionsTab change behavior; eight other types differ solely by the 653→654 build
constant. Every changed build-consumer line is token-checked and separately reviewed.
Native MR, NPC, assets and wire-layout behavior outside that scope remain unchanged.

This is a bounded repair with six fresh focused scopes and 16 fresh source scopes,
not a new full 184-suite local gate. Unchanged areas inherit the recorded 652 composite
and 653 integration evidence. Primary receipts and private baselines live under
`.planning/debug/frame654/`. All three worker receipt archives are byte-verified
under `worker-archives/`, retaining failed/unqualified attempts as well. The three
own worktrees and their build caches are removed; other agents' trees, shared
references and supplied hardware logs remain. Repaired Auto activation and the
headset result still require the next Frame run.
