# Quest754 shared-source integration review

Reviewed candidate `d578327c9` independently in `worker/quest754-review`, against
exact published `v1.2.3` / `b87477f8a`, old published Quest743 `7ecf6aaea`, and
their comparison base `fc74ee79c`. The maintainer authorized this exact rebase;
the generic dev integration instruction does not apply to this Quest delivery.
Initial combined source is `c7109ac9a`; `d578327c9` additionally retains the
published Quest743 ancestry without another source change.

No shared C# parity repair is identified by this review of the initial candidate.
The integrator separately identified missing old static-weaver support for the
new `ConfirmationIdentityWritePatch` and `ConfirmationDeferredCapturePatch`;
the AOT worker owns its Weaver/test repair. This report does not qualify that
later closure. This is static integration evidence, not a runtime gate, Android
execution result, or headset acceptance. The integrator owns the class C complete
gate once after integrating the AOT repair.

## Complete path comparison

The old Quest delta changes 1,679 paths; published upstream changes 253 paths.
The union contains 1,903 paths, with exactly 29 overlaps. Every one of the 224
upstream-only paths is byte-identical to published v1.2.3. Every one of the 1,650
Quest-only paths is byte-identical to Quest743. No old Quest-delta path vanishes.
All 29 overlaps contain combined content rather than wholesale selection of
either side. Full SHA256 values for all four revisions are retained in:

`/home/claw/quest3-local/build/evidence/quest-v1.2.3-rebase-20261031/parity/review-file-matrix.json`

Of the 82 upstream-changed production paths, 72 are byte-identical. The ten
expected differences below contain the retained Quest seams and the new build
identity. Full upstream hashes accompany these candidate hashes in the matrix.

| Production path under `src/GloomhavenVR/` | Candidate SHA256 | Retained difference |
|---|---|---|
| `Cards/Art/CardFaceMipBake.cs` | `e71fc5b927c31aed456fa8540f400f312e1c3535db324dae083694cc0577aeb4` | Quest exact CPU pixels, generation-aware cache and color-space/mip handling; new upstream read-only pixel receipt remains. |
| `Core/Loc/Loc.PlayerHelp.cs` | `e8c08485a2e359717ad2b4bab2e1d8721e4f00cd5c9921032a003e7f0974e36f` | Quest startup-only eye-allocation help. |
| `Core/Perf/PerfMonitor.cs` | `c50106417c692974f79900de92ca32f8e693515dcee5ba87f06781bcbf71ad5f` | Retain existing Quest summaries; upstream bounded spike interpretation and attribution remain. |
| `GloomhavenVR.csproj` | `bc5584b91b530e304285cd1940b52f7baf61d78edc133eb41be7bd477f64f004` | Existing selected Android bundle/provenance inputs and Quest loading resources. |
| `Hands/Interact/RayInteractor.cs` | `a47458f58fa60dd4af8beb00641c828953c6fa288a3e0888ea56a276e0dfd906` | Quest pointer presentation and bounded Debug evidence; upstream native pick-mask correction remains. |
| `Net/NetProtocol.cs` | `097aed573d35631dbab441d039d7bdf0801413e021ee2cde48aa2295e1fedea2` | Quest754 stamp/notes and historical Quest743 notes; upstream protocol constants and TLV123 remain. |
| `Rig/VRRigDriver.HeadCamera.cs` | `cbfc315651d58b2c9300d7d60ffb879606847a9eb90b59f3449fe8e9e81dfb13` | Existing Quest tracking evidence startup; upstream finite-sky clip budget remains. |
| `WorldUI/MenuDepth/MenuDepthCharacterSource.cs` | `8199778d71e49c312a07b141246ed25f0d5994418a2b7764314a68ad1f6e4e5d` | Android uses the same cached, read-only native field instead of Reflection.Emit. |
| `WorldUI/Sharpness/PanelSupersample.2.Capture.cs` | `4ee44c38883c158e407580ed781b25c49c89062f42ba6e9adeae906c33d1f971` | Two existing ordinary-2D Quest copy calls; desktop helper retains builtin Blit. |
| `WorldUI/WorldUIModule.cs` | `283496b62bf422e1c3fe05f3f0195b8627cbf5531bdb4ecaca8162e02bd2a43f` | Existing Quest tutorial viewport-mask patch registration. |

## Shared behavior and seams

- Builds752/753: `MenuDepthCharacterProjection.cs` is exact published upstream,
  including the exact creator-page detection, 88% quest framing, help-line
  clearance, upright naming metric, five-percent keyboard padding and left tie
  preference. All shared projection callers and the complete original preview
  scene/ground origin remain. The source's only Quest difference is the Android
  original-reference reader at line972. The separate hidden-state reader in
  `MenuDepthCharacters.cs:212` is unchanged from Quest743 and uses the same native
  bool. Neither reader writes original state.
- Fixed capture quality: upstream `MenuDepthHeadCarrier`, `MenuDepthHeadRaster`,
  display, source registry, sharpness core/content/menu-depth/owned-member files
  remain exact. `PanelSupersample.2.Capture.cs:1914` still synchronizes admitted
  confirmation members, and line2871 releases them. Its two Quest substitutions
  at lines588/2715 call the unchanged Quest743 `QuestTextureCopy` recipe. No new
  motion-dependent rate/resolution policy is introduced by the rebase.
- Builds748/749/751: physical/menu ray geometry, original native coordinates,
  More Cards row ownership, nested native input priority and destroyed-plane
  liveness are exact upstream. In particular `MenuDepthCardPlane.cs:153` checks
  Unity liveness before Behaviour readiness. Character ray mesh/cull receipts
  survive in `MenuDepthCharacterSource.cs:413`; its Android reader does not
  replace the hit/readiness implementation.
- Builds744–746/750: all shared opening/claim/order/reflow/reward/video state,
  released facing, finite sky, town canonical layout/grounding/lighting/contact
  owners, original card-list leasing, confirmation navigation/outcome/reentrant
  Hide ownership and native window close permissions are exact upstream. The
  integrated `WorldUIModule.cs:92` onward registers all added confirmation
  observers and `:161` registers the added Further Ability hover observer.
  Source equality establishes retention, not observed network convergence.

## Fixture and authored-resource review

`scripts/test-suites.json` retains all 285 upstream entries unchanged and all
312 old Quest entries unchanged. Final inventory 313 contains the 28 existing
Quest-only entries plus upstream's new `menu-depth-laser`. No suite command,
group, weight or dependency field is lost. The parsed comparison is retained as
`parity/test-inventory-comparison.json` beside the full matrix.

The 16 overlapping scripts/fixture files retain upstream additions while adding
the existing Quest compile dependencies: actual `QuestTextureCopy` plus explicit
desktop capability boundaries for capture/cards/performance/native level-up;
actual `FlatScreen.NativeTargets` for desktop capture; the actual current screen
material factory in default-sky; explicitly empty Quest-only producer/movie
observations in the ordinary desktop fixture. The mono-resolve negative control
targets the current helper call. Integrated rotation still limits its yaw/ground
mutations to the real Capture method after upstream added a second ray consumer,
and retains the five Android AOT cases. No runtime result is inferred from this
inspection. The old Quest slow-observer retry fixture change remains explicitly
separate from its first-picture wall-clock latency proof.

An additional static execution of only the real capture script's source-building
statements reconstructs 20 production sources. These include the complete new
`PerfFrameSplit.CameraPasses.cs` and `PerfFrameSplit.Spike.cs`, extracted real
PerfScopes/camera callbacks, and actual `QuestTextureCopy` with its explicit
desktop boundary. All 42 upstream cases remain, including the eight new nested
camera/completed-frame/scope/Debug controls. Each of the 41 mutation targets occurs
exactly once in its current reconstructed production or fixture source. Both new
partials and their original PerfMonitor/PerfFrameSplit parents also remain in
the input-pin list at `scripts/menu-depth-capture/run.py:332`. This establishes
source/control retention and target uniqueness only; it does not establish a
compiling or causally failing control. The reconstructed source hashes and every
target are recorded in `parity/capture-fixture-static-audit.json`.

Upstream744–753 changes no file under `src/GloomhavenVR/Assets/` or `unity/`.
Those paths and the Quest menu-depth/default-sky resource producers are unchanged
from Quest743 in this candidate. The Android menu-depth recipe still names all
five runtime shaders, including head resolve and native card alpha/rect variants;
its immutable producer closure includes current authored shader text. Default
sky retains the same seven native assets/provenance and Android recipe. Selected
bundle resource names remain the production loaders' names. Thus no new authored
shader/property ABI drift is identified; this does not qualify an unexamined
cached APK or prove its final XR draw.

## Validation and limits

Test class: A for this optional review report; the reviewed integration is C.
Executed: complete four-revision SHA256/path matrix, parsed inventory comparison,
static diff/caller/fixture/resource review, capture-source reconstruction and
41 unique mutation-target checks, and clean `git diff --check`.
Inherited: no runtime pass is claimed by this worker. Root runs the complete
required gate and Android closure checks on its final candidate.
Failed/pending: no shared C# parity finding; separately identified static-weaver
support requires the AOT worker's repair and validation. Windows/native Android/
headset pixels, actual startup, paired multiplayer and hardware motion remain
unverified here.
Elapsed validation: tool execution was bounded; source-review wall time was not
separately instrumented. No Unity/editor/test fixture was launched by this lane.
Cleanup: no generated project caches or persistent test processes were created;
the hash/inventory JSON evidence is retained outside the worktree. Shared game
references and other workers' active inputs were untouched.
