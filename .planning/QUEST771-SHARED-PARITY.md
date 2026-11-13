# Quest771 independent shared-source and fixture review

Reviewed initial candidate `e19d919f7ac488467f4352125338a09c652447ec` against
exact released v1.2.4 / `113c297357a5bde0a781f0c8995d33c7a052a228`, old
release v1.2.3 / `b87477f8ae6247e524efc6dc128c09a25a6ae55d`, and the prior
published Quest754 / `465f2815a17b06eb945349975745100b9152939b`.
The maintainer explicitly authorized this actual release rebase. The Quest branch
exception applies; this worker does not change dev/main or publish packages.

No lost shared production change or old Quest source delta is identified in this
review. A new initiative-row fixture has one missing passive Quest diagnostic
port; its bounded repair leaves the tested coordinator and every control intact.
Android/AOT adaptation is reviewed independently by the other worker. The
integrator owns final class C validation of the combined candidate.

## Source and asset binding

The comparison covers every tracked path under `src/` and `unity/`: 164 upstream
changes and 216 old Quest changes, with 15 overlaps. All 149 upstream-only paths
are byte-identical to the released commit. All 201 Quest-only paths are
byte-identical to prior Quest754. No old Quest-only path disappears.

Full four-revision Git blob IDs, SHA256 hashes and sizes for all 365 union paths
are retained in `source-inventory.json` under:

`/home/claw/quest3-local/build/evidence/quest-v1.2.4-rebase-20261113/parity/`

The 15 overlap source files were reviewed from their full release-to-candidate
patch, retained as `upstream-shared-diff.patch`. Candidate hashes:

| Path under `src/GloomhavenVR/` | SHA256 |
|---|---|
| `Compat/CompatModule.cs` | `48b6e22872de70c298998e5aa1b55abd97c95ac08d57e84092ac882f50e1eda2` |
| `Core/Loc/Loc.PlayerHelp.cs` | `4a1c0e8f403227fcdd817d4b1c98aa5b856dfb2c834552be757e27c7c5bed484` |
| `GloomhavenVR.csproj` | `f8362ff0f6ff9ef45910be25e39912e2582507b666393ce51815ad4e8917479f` |
| `Net/FfsNetTransport.cs` | `e15760131a84c4dd14bde08060cd1878ca2913b630012d9c9531177be1cbb750` |
| `Net/NetProtocol.cs` | `3b1eb4472535e9a481d589de1a8978614332e3fc8a6980742b1e8da515ce8091` |
| `Net/Remote/RemoteBoardFurniture.cs` | `e457b6241ed47bd3bbe649257e2b75abfeaec99fe8083556675418f56cc3d905` |
| `Rig/VRRigDriver.cs` | `8687f9973e0593f26fa0a9bda91accb9d5d4cc390ea24644d54971e224edb86c` |
| `WorldUI/FlatScreen/FlatScreen.1.Core.cs` | `3b4acf2ce168820fa51879a95d560c1befe1488b572336e8561e90df3a525680` |
| `WorldUI/FlatScreen/FlatScreen.3.Desktop.cs` | `45ba7e6d15384bda56dde0a03ae946f8d71a66aa025815640094fc7c15257222` |
| `WorldUI/FlatScreen/FlatScreen.4.Lifecycle.cs` | `7e4c7f04e315172f9622879fb8e6205cbae550d89ae4933c90611fd5aae17c4f` |
| `WorldUI/MenuDepth/MenuDepthCharacterSource.cs` | `781dc697cb968e39cf7bcdb81d9768c6b3c8d189369f4eeaf02dd966d6f3110b` |
| `WorldUI/Options/VROptionsTab.8.Dependencies.cs` | `22b1457b2fa5763d605c839a8a21a597f2e28c1e156866c1fedb21270c6fe085` |
| `WorldUI/Surfaces/StatPanelSurface.cs` | `d38d47adc647ba87d7fb21e29a908c30d964cf36da09a29f825fd23a3853a21e` |
| `WorldUI/Surfaces/TablePanelSurfaces.cs` | `528c3e02c7d36730c0297f1a4f582e773454e93becfa226bf8a82f45237caa14` |
| `WorldUI/WorldUIModule.cs` | `082568e05b2491a818f893a37fa5a145475aa6900918dee73ca9a109567c519a` |

Retained differences are existing Quest seams: inert free-form component
suppression on Android; fixed startup allocation help and excluded service rows;
selected Android resource inputs and loading artwork; public reflection lookup
for the woven receive hook; explicit Quest card-guide mesh upload; native captured
menu-anchor support and bounded tracking evidence; standalone preparation/mirror/
copy/presentation ownership; Android read-only native reference access; passive
artwork evidence and the existing GPU-only portrait copy handling; and the
Quest tutorial viewport-mask registration. The new build identity is 771 and
historical Quest notes remain. None adds a new disclosure or gameplay authority.

Results presentation, delta/pose grammar, independent motion delivery, accepted
scroll projection, queue progress/ordering and complete native asset catalogue
are exact released production. The new creation frame, native 16:9 viewport,
window frame, input coordinate/scroll helpers, native figure-audio ownership,
campaign-creation guard, card cancellation/progress help, public forced-hand pick
and critical-story recovery implementations also remain exact released files.
The overlapping rig keeps safe scenario-arrival zoom as well as the retained
Quest anchor seam. Character source keeps the new starting-card suppression and
native lifecycle while preserving its Android read-only reference accessor.

The only new upstream authored assets are the SteamLink `scene.bundle` and its
`provenance.json`, both exact released bytes. Their SHA256 values are respectively
`6a51e25b930377e8599306ad5c6c812b293840009fe90fe31a71a0162eabb781` and
`a9d15a62869b2531c33c913db8a3c2189ac0de653968c29e583e761701d7da4f`.
No source equality claim certifies an Android shader bank or headset rendering.

## Suite and fixture integration

Parsed suite metadata preserves all 297 released local entries and all 296 old
Quest local entries, with no modified command/group/weight/dependency record.
Current inventory is 325 local, 141 CI and 16 source suites. Full old-release,
release, prior-Quest and candidate counts plus exact record comparisons are in
`test-inventory-comparison.json`. No registration or negative control is removed.

The complete scripts/tests comparison has 281 upstream changes, 725 old Quest
changes and 16 overlaps. All 265 upstream-only and 709 Quest-only paths remain
byte-identical to their respective inputs. The overlap patch retains new native
viewport/frame dependencies, genuine Quest copy helpers with explicit desktop
boundaries, capture target ownership and Android AOT controls. Static AST review
also finds no missing release tuple labels or unit-test methods in the overlapping
Python fixtures. This establishes source retention, not causal-control execution.
Hashes, reviewed patches and label details are in `fixture-path-comparison.json`,
`fixture-overlap-diff.patch` and `fixture-control-label-audit.json`.

The new initiative-row source extraction includes the original Quest
`QuestArtworkEvidence.Raw` observation in its production LateTick, but its fixture
boundary lacked that passive diagnostic class. The initial strict build fails
CS0103, retained in `compile-only-initial-initiative-row.log` and the owned run's
build log. The repair adds only an explicitly documented empty Android
observation endpoint to `scripts/initiative-row-runtime/Boundary.cs`. Actual row
coordinator, layout, slide, input, native LeanTween and final-pixel oracles remain
unchanged. This endpoint is not the production behavior under test.

A temporary external Python subprocess guard aborts before any Unity launch.
It preserves each normal source-generation/compiler path and the runner's
failure/completion cleanup. Those intentional exit-1 runs are compile closure
evidence only, never runtime passes. Initial and second attempt summaries/logs
are retained. Strict production compilation succeeds for figure audio, desktop
menu depth including actual NativeUiViewport/CharacterCreationFrame, Results
consumer, shared scroll, static-copy admission, initiative popup/binding, story/
progression, Results window, desktop capture, menu rig, depth graphic and rotation.
The initiative-binding entrypoint compiles all eight existing variants before
its Unity boundary. The repaired initiative-row production also compiles 0W0E.

Results transport production runs without Unity and passes 2,438 assertions;
Results control production passes its original native-control fixture. These
partial development invocations do not include their causal controls and are
not complete suite or full-gate passes.

The complete affected initiative-row suite then executes through the unchanged
central runner at worker checkpoint `dffe08bfb`: all seven production cases pass
1,055 actual row/clock/geometry/pixel assertions, and all eight compiling causal
controls fail at their intended oracles. Unity2021.3.5f1 uses llvmpipe
(LLVM20.1.2,256bits); elapsed69.646seconds. All19 source/native/toolchain pins
remain unchanged. The suite report/log hash is retained in
`initiative-row-suite/results.json`, with independently checked counts/pins in
`initiative-row-qualified.json`. This is one complete affected suite, not a
complete local gate or Android/headset acceptance.

## Validation record

Test class: B for the bounded fixture dependency repair; reviewed rebase is C.
Final production binding: initial e19d919f7 SHA and the four-revision source matrix.
Executed: full source/asset hash matrix, 15-overlap review, parsed suite inventory,
16 fixture-overlap/control-retention review, strict fixture compile closure, two
partial non-Unity production proofs, and complete initiative-row 1,055/8controls.
Inherited: no full runtime/golden proof claimed by this worker.
Failed/pending: initial missing diagnostic-port compile failure and intentional
compile-only Unity stops preserved; integrator class C gate remains pending.
Native Android startup, paired multiplayer, audible
figure timing and final headset pixels remain unverified.
Elapsed validation: per-attempt seconds in `compile-only-initial.json` and
`compile-only-second.json`; source matrix took 7.65 seconds and affected Unity
suite69.646seconds.
Cleanup: every owned runner calls `cleanup_completed_run` after its children are
reaped; retained report trees contain source/log/manifest evidence, not usable
bin/obj caches. No default config is synthesized and read-only references remain
unchanged. The actual affected run removes400,249,189bytes without skipped paths;
the final owned-process audit is empty. All owned logs/results/source/assemblies/
captures/receipts are moved intact under `parity/fixture-evidence`; the original
and archive paths are bound by `archive-location.json`. The single private Unity
slot is released and the AOT worker notified before final handoff.
