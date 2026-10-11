# Build 607: attributable figure measurements and optional window materialization

Internal engineering notes, 2026-10-02. Build 606 proved native mesh replacement
and exact restoration but its hardware slider changes fell inside mixed FRAME
windows. Those old timings cannot be made attributable by relabeling them.
See [the Build 606 analysis](FRAME-606-ANALYSIS.md) for the measured geometry and
package cost, and the limits of that evidence.

## Measurement boundaries

The early PerfMonitor Update observes four scalar choices: player detail, enemy
detail, figure effect density and figure cloth simulation. At a change it closes
the completed old samples with the **old latched settings**, discards the single
mixed transition frame (including pending step/camera/counter work), and starts a
`preparing` window. The existing minimum of 120 samples still applies to an
early summary; a shorter interval is omitted rather than presented as sufficient
evidence. A normal timed summary can still have fewer samples at very low FPS.
Counters now join the summary only at a completed frame roll, as timings already
do. Boundary summaries omit unfinished native callback/profiler captures because
their immediate accumulation can include the discarded callback frame; completed
earlier captures remain available.

`steady` starts only after the figure driver's completed native LateUpdate pass,
no pending figure discovery/loading, closed VR Options, and two quiet seconds
after the last unready observation. The driver exposes a scalar readiness stamp;
the monitor does not census meshes or allocate scene snapshots each frame.
Opening VR Options invalidates steady measurement even without a value change,
so an existing zero setting can be used as a fresh baseline after closing it.
This instrumentation neither changes a figure mesh nor schedules asset loading.

Each FRAME ends with an explicit tag, for example:

```text
| figure players=0 enemies=0 fx=0 cloth=False state=steady revision=3
```

An Info-level `FIGURE-MEASURE begin` marker at each settled boundary resets
the offline reader's accumulated window evidence, including short preparation
intervals omitted from FRAME. Preparation traces are Debug-only and limited to
one per second; no per-frame log stream is added. Existing scene/loading,
tracking, resolution and graphics evidence still determine whether a tagged
window is comparable. `steady` is a figure/application classification, not a
guarantee of an idle scene or unchanged headset view.

`scripts/frame-perf-report.py` preserves this state in JSON and accepts only
steady tagged windows for a controlled comparison. Known mixed legacy windows
are rejected; legacy unknown state is explicitly marked unverified. FX/cloth
changes are confounds, not figure-only improvements. Missing actual GPU busy
times stay unavailable; XR wait or camera callback spans do not become GPU time.

## Hardware test

Use Build 607 and Debug logging. Keep the same loaded scenario, position,
headset view, zoom and eye resolution. Close the pause menu and VR Options
during each measured interval, and leave all other quality choices unchanged.
Do not include movement, combat, grabbing or a new room in the controlled sweep.

1. Set both figure detail sliders to 0; close the menus and wait **40 seconds**.
2. Set both to 100; close the menus and wait **40 seconds**.
3. Return both to 0; close the menus and wait **40 seconds**.

The logger records preparation separately, so the reader can exclude first bank
loads and menu work without requiring a manual timestamp. For individual player
and enemy contributions, repeat with only one slider changing and the other
fixed. Retain both Player.log and LogOutput.log from that run; a recording CSV
is useful additional evidence but not required for this comparison. Forty
seconds accommodates the default 30-second summary cadence and the quiet guard;
increase it if the summary interval was customized.

This measures the combined slider behavior: authored native LOD caps plus body
derivatives. It does not isolate derivative-only benefit or prove that the
294 MB installed mesh banks are worth their download cost. Those conclusions
still require matched hardware timings and, if needed, a separate controlled
derivative-only experiment. No new measured FPS improvement is claimed by 607.

## Window materialization switch

The existing `[WorldUI] WindowMaterialise` key is now a localized boolean in
**VR Options → Graphics → Windows/panels**. New standalone Frame profiles use
`false`; PC retains `true`. BepInEx binding preserves every existing saved
choice, including a saved `true` on Frame. For an existing test profile, turn
the toggle off once or set that key to `false` with the game stopped.

OFF bypasses the mod's window/surface dust and associated animated MR backing.
Switching off mid-effect synchronously restores presentation, removes dust and
finishes outstanding close callbacks exactly once. Window interaction and
native continuation remain available. Native window movement, grab handles,
card burn/flight effects, NPC effects and remote native widget playback are
separate and retain their behavior. Re-enabling restores later effects. No
asset bank needs rebuilding for this change.

## Automated evidence and limits

The measurement suite executes the production state policy and boundary adapter,
including old-state retention, delayed native readiness, rapid reversals,
options-open exclusion and mixed-frame disposal; deliberate runtime defect
controls check that these failures are actually detected. The real Unity figure
suite additionally checks readiness before and after the actual LateUpdate.
The existing MR backing suite exercises production binding, effect entry points
and teardown with saved/default choices and a switch during close.

These checks establish source/runtime contracts. They do not establish headset
appearance, a smooth frame cadence, or actual GPU cost. The subsequent
[Build607 hardware analysis](FRAME-607-HITCH-ANALYSIS.md) confirms slider
application and residual hitches; the supplied sweep does not contain a settled
100% counterpart and cannot establish an isolated figure-detail FPS gain.

The final integrated runtime tree `38eeb1e4` passed 14 source suites, 102 local
suites and 286,760 wire/golden assertions, strict Release with zero warnings or
errors, and five bilingual document pairs. Local coverage took 627.7 seconds
with eight jobs. Independent manifest/log-hash verification and a compiled
comparison against the preserved Build606 snapshot restrict behavior changes
to the intended measurement, window switch, options/localization and defaults.
Retained evidence is under `.planning/debug/frame607-final-validation/`.
