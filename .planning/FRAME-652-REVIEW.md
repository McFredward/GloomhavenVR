# Build652: explicit wall visibility and loaded-game Auto

The maintainer's 2026-10-09 request authorizes instant complete wall removal as
a configurable visual compromise. This exception applies only to the new modes;
Regular retains the existing view-dependent fade. All platforms share the same
binary and editable controls. Development starts at `2f6819eee` on current dev.

## Hardware verdict before this change

Both latest Frame build banners identify651. The loaded three-room,
closed-options windows contain358 application frames over40.1s:112.130ms mean,
approximately8.92 application FPS. The qualified first-room window averages
52.49ms, approximately19.05FPS, with a minor options opening at its end.
World material validation costs30.361ms/application frame and the wall parent
12.117ms. Per-eye calls already belong to these totals; do not double them.

The compatible645 capture averages94.107ms and World23.944ms. Thus the650
hardware gain is not visible in this651 run. Different pose, wall activity and
broad clock increases prevent a causal650 regression verdict. No busy-GPU or
headset-presented-FPS measurement is available. The complete read-only evidence,
source counters, exact windows and hashes live in
`.planning/debug/frame652-audit/REPORT.md` and its JSON receipts. The current
three-file Frame folder contains no screenshots.

## Controls and resulting behavior

- Regular: existing wall fading, including its existing see-through switch.
- Hide all: instantly remove eligible walls and their existing collected
  attachments. No new fade animation runs.
- Auto: the common fresh default, including Gaming PC, Frame and Standalone.
  Observe application frame intervals below15FPS over a continuous two-second
  window after full scenario readiness. The threshold is editable from5–90FPS.
  Loading, room preparation, lost focus and open VR Options reset observation.
  Sampling uses `Time.unscaledDeltaTime` once per application frame, with no
  profiler, camera or scene sampling. A250ms contribution cap prevents one long
  hitch from filling the window, and a500ms loading-edge grace precedes it.

Auto latches for the current scenario rather than repeatedly restoring walls
when its own savings improve FPS. Select Regular to restore them earlier.
Changing modes resets the latch. Local quality choices are not broadcast to
peers. Existing wall-fade peer sets are cleared on entry; hidden mode neither
applies peer fades nor walks the full sender table.

Door frames, gate columns, torbogen attachments and shared protected corners
retain the existing exclusions. Floors, figures, water and held props remain
protected. Admission uses the current existing collector across game/DLC themes,
not a catalog tailored to this test scenario. The option does not deactivate
native objects, change gameplay controllers or remove colliders.

## Source-proven work removal and ownership

The hidden branch returns before the original wall tick: no normal decisions,
fade ramps, reclaim, wall diagnostics or recurring collector sweeps. Exact
hidden native sources retire their World material slots once, then skip mesh,
ancestry, material and property-block validation for both eyes. Environment
substitutes retire before mask acquisition and cannot re-adopt or unmask those
sources. Scenery-density changes and restoration revalidate current ownership
rather than restoring stale settings.

A final bounded follow-up also skips exact hidden sources before Terrain's
Update validation/detail/morph and camera native reads. Hidden discovery does
not prepare geometry. Actual final wall release queues that source once through
Terrain.MaterialReady; no deferred hidden-renderer polling set is retained.
Current native mesh replacements replace stale private records on readiness,
so release-time rediscovery is not consumed and then lost. Regular still runs
the full current mesh/material/scope validation. Detail poses are captured
lazily once only if a visible valid source needs them.

The executed release fixture also found an existing queue identity mismatch:
discovery deduplicated GameObject IDs but removed Transform IDs on dequeue.
Removing the same GameObject ID permits later real readiness/release callbacks
to enqueue the source again. The native readiness event originates from asset
completion, not Update; owned World material changes do not invoke this queue.

The settled wall path reads existing room/wall counters and the tiny held-root
registries. Only changed held visual subtrees are visited; a newly held native
visual is rescued before camera culling. Actual native placement/readiness and
room changes coalesce a revision and run the existing sliced collector after
loading. Those necessary lifecycle costs remain. Existing masks are restored
only synchronously within a collector stage and reasserted in `finally`, before
normal rendering. No collector executes `Camera.Render`.

`forceRenderingOff` removes normal renderer color submission. Actual Unity
proves an explicit foreign `CommandBuffer.DrawRenderer` can still draw despite
that flag. The native TilesOcclusionGenerator writes shared room/object masks,
not visible wall color, so its entire floor/room pipeline must remain. Native
fog depth/mask draws are likewise separate consumers; Frame already disables
fog through Compat. Native EPOOutline visibility is driven by renderer visibility;
generic masking can still submit explicitly. This build does not claim zero
arbitrary native command work or globally disable figure/door outlines. Its own
environment batches/instances exclude exact hidden sources.

## Performance and correctness evidence

The actual complete World PreCull benchmark uses440 sources, two eyes,
representative populated property blocks and15 warmed interleaved AB/BA rounds
in one Unity Mono process. The same source inputs remain unchanged throughout:

| Workload |651 baseline |652 candidate | Median paired result |
|---|---:|---:|---:|
| No configured predicate |3.6790ms |3.6731ms |−0.004% |
| Configured predicate, no hidden identities |3.6336ms |3.6398ms |+0.09% |
|220 hidden active identities |3.6528ms |1.8511ms |−49.35% |
| All376 active identities hidden |3.6745ms |0.0683ms |−98.14% |

Both hidden workloads win all15 pairs; the half-hidden paired95% bootstrap
interval is−49.46% to−49.11%. The visible-path intervals include zero. The
visible-path difference is tiny timing noise, not a new saving. The hidden
results deliberately remove visible geometry and are a visual compromise, not
a pure redundant-work improvement. They measure this CPU path, not whole-game
Frame FPS/GPU performance or the game's actual wall fraction. Cheap Regular/Auto
entry observations add approximately0.000002/0.00033ms per modeled tick boundary.
Allocation-byte API readings under this Unity Mono are invalid; no allocation
quantity is inferred from them.

The final wall fixture passes675 assertions and all14 causal controls in one
actual Unity process. Whole World passes772 assertions/all69 controls; the
worker composite combines the complete attempt and one corrected expected
predicate, while the primary complete gate separately runs the final full scope.
Whole Environment's worker evidence is11470 assertions/four focused controls,
with its complete old-control scope in the primary gate. These lanes explicitly
model the original collector/config/controller boundaries; they do not certify
every original/DLC room or connected headset session.

Native scene-handle evidence caught a real recovery defect: a valid loaded
scene with handle−1298 was rejected by a sign assumption. The fix checks actual
scene/generator identity without interpreting the handle's sign. Real native
negative-handle and removed-key-rebuild controls reject the respective bugs.

The final full Terrain fixture additionally passes403 actual Unity assertions
and87 causal controls, retaining all79 earlier variants plus nine new ones.
Its exact final CoreModule release lambda executes against the actual Terrain
driver; only the independent scenery callback and external wall membership are
surrogates. Original native geometry verifies10 unchanged structural definitions.
Hardware confirmation of Auto timing, arch coverage, restoration, multiplayer
and Frame FPS remains outstanding.

## Independent read-only startup probe

The already reviewed OpenXR environment-blend probe joins652. It enumerates
bounded supported modes on the existing instance/system lifecycle, never vetoes
VR and neither activates passthrough nor changes render state. Its separate
400-assertion fake-native/lifecycle suite is registered in the complete gate.
Actual standalone MR implementation remains the independent653 worktree.

## Final primary validation and integration

Final runtime source checkpoint is `20ca247e9`; subsequent review/state edits
are documentation only. The complete local gate through `scripts/wire-tests.sh`
attempted all182 catalog suites:178 passed and four failed. The unchanged two
NPC first-render fixtures exceeded their one-second wall-clock boundary during
parallel Unity runs, then passed individually without weakening that deadline
or changing NPC production behavior. Two older harness defects were repaired:
the map audit now executes its explicitly built isolated DLL, and the portable
scenery fixture declares the new inactive external mask-policy boundary.
All original assertions remain.

The sequential repair subset passes both NPC scopes, scenery policy, wall
options and player settings help. A final affected repeat passes the map audit
(eight unchanged native assertions) and the complete final Terrain fixture
(403 assertions/87 controls). The final16 source checks also pass. Their receipts,
the failed complete attempt, and inherited unchanged passing scopes form
**182/182 composite passing coverage**, recorded in
`.planning/debug/frame652/composite-validation.json`. This is not a fresh green
complete-gate rerun. The bounded repair rule permits reusing unchanged evidence.
The golden executable separately passes299715 assertions after a fresh test
build; an initial missing apphost SDK-location attempt is retained alongside
the successful run. The failed catalog attempt did not reach these vectors.

Final strict Debug and Release both report zero errors and warnings. All four
UnityFS format7 / Unity2021.3.5 banks and1594 figure derivatives in66 parts pass;
all five English/German player-document pairs pass. Surface comparison preserves
all previous keys, patches and markers:663→665 config keys,235 Harmony-text
entries unchanged,4790→4794 log tokens. The private compiled comparison covers
1242→1244 types:23 differ, including eight proven build-number-only consumers.
The remaining15 types belong to wall controls/consumers and the independently
reviewed read-only OpenXR probe. The final bounded production followup changes
only CoreModule, Loc and ScenarioTerrainBudget relative to the earlier validated
652 snapshot. NPC behavior, assets and wire layout remain unchanged.

Final primary receipts and private compiled/surface snapshots live under
`.planning/debug/frame652/`. The three earlier worker receipt archives retain
their file-hash manifests under `worker-archives/`; the Terrain worker archive
under `.planning/debug/frame652-terrain-hidden-worker` retains5505 files with
verified hashes, including failed attempts. All four worktrees created for this
integration are removed after committed changes and archived evidence are checked;
two redundant private decompilation caches are removed. Other agents' worktrees,
shared dependencies, hardware logs and the private comparison baselines remain.
