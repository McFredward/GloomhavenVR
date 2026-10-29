# Build746 passive map stalls: measured limits

The maintainer reports repeated severe lag on both players' 3D maps, including
ordinary movement without opening windows, using NPCs or changing surroundings.
The cause of those stalls is still unproved. This change repairs diagnostic
coverage and an incorrect exclusion claim; it does not establish a lag fix.

The main checkout's ignored `.planning/debug/mp746/hardware-inputs/` contains
pinned `LogOutput.log` and `remote/LogOutput.log` plus their `Player.log` copies.
All identify Build745 / `c3bb18117`, built
2026-10-29 17:06:49 UTC. The host uses Debug; the peer's normal log contains no
Perf samples and cannot establish peer frame times. Host SPIKE evidence:

| Host LogOutput line | Historical reporting frame | Frame interval | Named scopes |
| --- | --- | --- | --- |
| 4088 | 34370 | 2251.24 ms | 6.42 ms |
| 4119 | 34477 | 3221.24 ms | 7.35 ms |
| 4218 | 35073 | 1280.72 ms | 10.82 ms |
| 4277 | 35796 | 1143.60 ms | 12.61 ms |

Each reports zero GC collections since the preceding frame. This rules out a
recorded collection in those samples, not unmeasured engine, driver or runtime
work. The surrounding SPLIT maxima are about 48 ms logic and 11 ms render. These
brackets do not account for the multi-second remainder. Presence loss follows
the first SPIKE at line 4089; chronology does not establish it as the cause. The
earlier startup spike with substantial named map/hand/card work is separate.

## Source-proven diagnostic defects

`MenuDepthSources.BeforeDirectCamera` invokes the actual offscreen coordinators
inside head pre-cull. Depending on callback subscription order, this work can
precede the first PerfFrameSplit camera timestamp. Its PSS coordinator,
source preparation, final head target work, Canvas rebuild, UI Camera.Render
and resolve were absent from named-scope attribution. Their inclusive wall
intervals now have Debug-only scopes using the existing allocation-free API.
Synchronous engine work or waits inside a scope belong to its interval; this
does not turn that interval into exclusive mod CPU time or GPU busy time.

PerfFrameSplit previously held one open camera id. A nested Camera.Render
overwrote it and discarded the outer pass. A fixed 32-slot stack now retains
both, reports unmatched/orphaned callbacks and rejects cross-frame intervals.
Parent and child times are inclusive and overlap; they must never be summed.
No camera callback order, render state, quality, target policy or rate changes.

Rate-limited Debug SPIKE details now retain the same completed frame's host
wall interval, logic bracket, interval before the first observed camera,
camera bracket and interval after the last observed camera callback. Missing
or skipped frame boundaries remain n/a. Unmeasured gaps remain unassigned.
The historical `NOT the mod` words are retained only in an explicit statement
that this exclusion is not established by a small named-scope share.

## Validation and limits

The final capture container runs the real coordinator, source borrowing,
Canvas, Camera.Render and shipping shaders in Unity 2021.3.5f1/OpenGLCore.
The added controls cover both callback subscription orders, missing child
post-render, stale camera/frame samples, scope double counting, absent
head/coordinator scopes and an incorrectly active normal-level scope.
An actual second-eye source exception restores ownership and closes scopes.
Normal and Debug final head images are pixel-identical; disabled attribution
records none of the new scopes. The production Debug/Normal captures were
also inspected. These generic UI/head witnesses do not establish HMD pacing.

Final worker validation:

| Scope | Actual result | Elapsed |
| --- | --- | --- |
| Strict Debug / Release | 0 warnings, 0 errors each | 11.09 / 10.01 s |
| Source suites | All16 pass | 68.394 s |
| Complete capture container | 451 assertions, 41 negative controls | 101.6 s |
| Complete character container | 7144 assertions, 50 controls, 76 variants | 311.9 s |
| Complete native material container | 94 assertions, 13 controls | 30.3 s |
| Complete discovery/performance container | 1133 assertions, 11 controls | 58.3 s |
| Figure measurement adapter | 48 assertions, 7 controls | 16.4 s |
| XR counter adapter | 12 assertions, 4 controls | 15.0 s |
| Public surface against exact worker base | Config670/Harmony265/log4836, unchanged | bounded source census |

The six local scopes are two containing runs: the final capture run and the
five-scope dependency run (432.081 s). The final Debug-storage guard changes
only PerfFrameSplit; the unchanged dependency results retain their bindings.
Evidence is under the worker's ignored `.planning/debug/mp746-performance/`
(`capture-final`, `source-final`, `dependencies` and strict build logs), with
per-container source hashes/captures and cleanup receipts in their run folders.

The integrated candidate requires the class C union gate. This is focused worker
coverage, not a complete-gate pass. Character/material consumers use
explicit timing ports; the actual Scope implementation executes in capture.
Original native assets/model loading, D3D11, physical XR, peer frame times and
headset appearance/performance remain hardware limits. No wire, graphics
default, shader, asset, native callback authority or gameplay state changes.

Completed runs stop/reap their editor/compiler processes and use
`cleanup_completed_run`; generated caches are removed while logs, source/input
manifests, assemblies and pixel captures remain. The initial fixture compile
failure is retained separately and is not counted as a runtime control pass.
