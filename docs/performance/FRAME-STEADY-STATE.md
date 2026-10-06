# Loaded scenario performance follow-up

This worker package starts from `dev` at `a517e0090` (ModBuild627). It targets
continuous work after scenario preparation, following the maintainer's October6
clarification that loading hitches are acceptable. It does not assign a new
shared build number, merge the NPC integrator's branch or publish a release.

## Evidence and priorities

The paired local sinks in the supplied PC capture identify Build627, an RTX4090,
MultiPass SteamVR, three revealed rooms and unchanged resolution. This is an
offline capture; the supplied remote evidence is an older build. Fastest and
Fantastic change several settings together and have different viewpoint/activity
windows. Their frame-time difference is not an isolated effect estimate or a
Frame/multiplayer outcome.

Three actionable admission gaps are source-bound: terrain substitutes cover
only three admitted surfaces despite many captured Crypt walls/pillars; safe
environment groups stay at zero; visible idle sampling stays at zero for rigs
with original CullUpdateTransforms, native LOD and captured disabled Cloth.
The companion terrain, submission and idle documents describe the remedies and
the exact native boundaries that must survive them.

All measures use the common PC/Frame code and assets. Existing independent
Optimize settings remain live and reversible. Frame detection seeds only fresh
defaults. No resolution default or gameplay/network cadence is changed.

## Debug work accounting

Existing timing summaries cannot identify whether shared reads actually remove
work in a hardware run. New diagnostic rows quantify executed work:

| Counter | Meaning |
| --- | --- |
| `Mirror.NativeSourceReads` | Executed immediate nonvirtual source reads in the optimized subset |
| `Mirror.NativeSourceReadsReused` | Actual second observations avoided when a changed field needs copying |
| `Mirror.ReadReuseOnNodes`, `Mirror.ReadReuseOffNodes` | Visible Apply visits using each mode; includes offline native clones |
| `UI.WindowRegistrySharedScans`, `UI.WindowRegistrySharedMembers` | Executed shared registry enumerations and membership visits |
| `UI.WindowRegistryIndependentScans`, `UI.WindowRegistryIndependentMembers` | Executed independent/fallback enumerations and membership visits |
| `UI.WindowRegistryReusePanels` | Panel reads reusing the current validated distribution |

These are not peer counts, all getters, unique renderers, GPU draw calls or saved
milliseconds. An unchanged field saves no second observation and contributes no
hypothetical benefit. The reuse fraction for the measured subset is
`reused / (reads + reused)`, rather than a reduction applied to all mirror work.
Source getters remain at their original callback positions; virtual text/color
getters are outside the optimized subset.

Counters flush once per Sync/complete LateTick, retaining nested callback work.
The read instruments require both attribution and Debug. Diagnostic rows are
registered to show zero in Debug; normal logs exclude them before formatting,
while existing useful lifecycle/anomaly counter rows retain their behavior.
Real production counter formatting is exercised in the mirror fixture, including
normal-level silence and an unchanged useful row. Existing graphics/camera rows
also display zero in Debug so a refusal cannot look like a missing instrument.

## Hardware comparison

Load the same three-room scenario completely before comparing settings. Keep the
viewpoint and activity comparable and let each unchanged setting window finish.
Loading/provisioning intervals do not decide the steady-state result. Compare
terrain detail/shading, grouping and visible idle independently before measuring
the combined Frame settings. Resolution can be varied separately by the
maintainer; this package preserves its current default.

Read final completed-camera terrain/environment counters and actual idle bakes,
then frame mean/p95/p99, dropped frames and the reported display budget. Prepared
membership or fewer asset triangles alone cannot establish a render saving.
Verify all room/floor/doorway content in both eyes, wall fades, native targeting,
held/action transitions, settings reversal and clone/reveal behavior.

A paired same-build multiplayer run is still required. Compare host and client
at the same player count and scenario state; also check town/window animation
and geometry while the owner manipulates them. Local/offline mirror counts do
not establish network cost. These changes do not throttle shared callbacks or
alter synchronized presentation/state. Frame FPS, close-view idle quality,
original shader lighting and multiplayer headroom remain hardware outcomes.
