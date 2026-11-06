# Build 763 bounded multiplayer review

Review baseline: `4536289edc4fd1827b288eecb6eece23af2532be` (Build 762).
This lane owns review prose and ignored evidence only. It does not change game,
fixture or transport code and has not run Unity, a headset, or a complete gate.

## Qualification decision

The two paired Build 762 failures have local, source-identifiable causes:

- Results: duplicated native wrappers for the original `3DNoise` texture cause
  the required material lookup to reject the complete replacement picture.
  The exact texture is `Texture2D`, 256 × 256, `RGB24`, nine mip levels. The
  descriptor's numeric `3` is the texture format, not a three-dimensional texture.
- Video: `RemoteVideoPlayback.EnsureMirror` repeatedly calls the initial-pose
  reader before the ordinary shared movement resolver. That reader advances the
  logical baseline without moving the physical window. The subsequent resolver
  therefore sees an already-consumed move and does not place the frame.

A third bounded defect was found during the Results fixture repair: the old
candidate transaction retires the visible picture after validation but before
the first potentially failing material application. This needs a Results-local
inactive preparation/commit repair and its own failure control; it does not
justify changing generic binding, native callbacks or the transport.

Class B remains justified only while the final production delta is confined to
the audited native descriptor clause, the video readiness guard, and the
Results-local candidate transaction. The integrator must qualify the final
union, not assume this review is a green complete-gate verdict. The reusable
baseline is the explicit 292-ID Build 762 aggregate in
`.planning/validation/build762/aggregate-qualification.json`.

Final source review found no remaining blocker in those three bounded methods.
Candidate material/widget application and root pose now run for every module
while the candidate is inactive, before the old picture retires. A failure
disposes only candidate bindings and preserves the previous picture and native
draw lease. This qualifies structural replacement preparation; same-shape
incremental updates are unchanged and are not claimed to be generally atomic.

## Native identity: executed exact-source decision proof

An independent, read-only UnityPy census visited all 28 original `.assets` and
`level*` serialized files in `GH_Data`, recording all 1,396 `Texture2D`
descriptors and the byte length/SHA256 of every containing file. There were no
read failures. Exactly one serialized descriptor is admitted by the new clause:
`sharedassets1.assets`, path ID 801, named `3DNoise`.

The CLR proof compiles the exact old and new `SameKnownNativeIdentity` bodies,
their exact `KnownHeroHighlight` helper and the game's real `TextureFormat`
enum. A declared `Texture2D = Descriptor` alias supplies only the five value
getters; it is an explicit pure-value boundary, not a Unity texture simulation.

- Production: 1,404 assertions pass, including all 1,396 original descriptors.
  The sole old/new decision change is the audited original, from false to true.
- Removing the new clause compiles and fails the expected exact-one-delta
  assertion.
- Broadly admitting identities compiles and fails the unexpected-delta
  assertion.
- Wrong dimensions, format, mip count and unrelated same-name cases remain
  refused. Existing known originals and the pre-existing dynamic atlas exception
  retain their decisions.

The three CLR variants and their control runs took 8.15 seconds in total. Their
executed DLLs, sources, build/runtime logs, input hashes and census are retained.
The canonical `cleanup_completed_run` API removed all six generated `bin`/`obj`
directories after processes exited (4,496,433 bytes; no skipped paths).

This proves the predicate decision delta, not live texture getters, a shader,
GPU output, network timing or a headset picture. The fresh Results Unity fixture
must qualify the actual newly admitted wrapper/material path separately.

## Shared registry dependency closure

The actual common town fixture source binder extracts 94 production members.
The baseline/candidate extraction differs only in `TownServiceAssets.cs`; the
other 93 extracted members are byte-exact. Removing only the predicate body
also makes the complete registry file byte-exact. Thus `Key`, `Register`,
`RegisterOriginal`, `Resolve`, scanning, ambiguity rejection, original aliases,
sprite unpacking, material contracts, header construction and queue/capture
pipelines have no independent implementation delta.

The saved audit inventories 44 direct/common-loader recipe paths, including one
explicit false positive: `town-service-warmup-runtime/Warmup.csproj` compiles the
different `WorldUI.TownServiceAssets` bundle loader. Other consumers bind the
Net registry directly or through the unchanged common source loader. Their old
serialized descriptor decisions are identical by the executed census proof;
they do not acquire a new queue, material-property, card-face, header or native
callback algorithm merely because a containing source file hash changes.

This is semantic evidence reuse, not a claim that the registry's whole-file
hash stayed equal. Existing unrelated NPC fixtures may retain their Build 762
proof with the unchanged recipe/native/toolchain bindings and this explicit
predicate-delta argument. New original noise-material behavior is qualified by
the repaired complete Results container, not by those old fixture results.

## Initial-pose review: all seven shared window kinds

| Kind | Why ordinary ticks do not consume a move as another opening |
|---|---|
| Scenario story | `SharedWindowSpawnBridge.TryReveal` stops after `SharedSpawnPoseReady`; successful `CompleteReveal` disarms `RevealArmed`. |
| Map story | Same ready/disarm gate; the normal follow path does not repeat the initial reader. |
| Quest confirmation | Same gate and current-opening receipt validation. |
| Encounter | Same gate and current-opening receipt validation. |
| Results | Same gate; the initial getter computes its target without clearing the follow baseline. |
| Reward | The repeated publisher getter updates only the initial-pose mailbox. Conversion/reveal adopts it physically; it does not reset the normal follow baseline. |
| Video | Its own native reveal path is guarded correctly, but the extra coordinator call was outside that guard. Guarding `EnsureMirror` with `SharedInitialPoseReady` closes that specific path. |

No second matching baseline-reset defect was found. Video owner/token changes
stop the old mirror and construct another decoder. `NativeVideoWindow.Tick`
closes/rebuilds on source or URL change, and `PrepareSharedPlacement` resets
readiness on the new physical window. A same-clip new opening therefore cannot
inherit the old ready frame. Decoder selection, review/native priority, current
gesture authority, size encoding, interpolation and ordinary movie completion
remain existing behavior.

## Fixture gap and final affected matrix

The previous Results exporter retained custom shader provenance but dropped
serialized custom `Image.material` PPtrs on import. Such images silently used
`UI/Default`. That old fixture could not exercise the material's `_AngularNoiseMask`
dependency or reproduce the host's Continue-to-statistics stage accurately.
Fresh proof must restore every exported original image material/property/
texture contract, activate the native Results/statistics stages, and compare
the resulting observer pixels. Recompiled GL fragment programs are declared
ports; compressed non-RGB24 textures are decoded RGBA uploads. The reported
noise itself retains its exact RGB24 descriptor and complete raw mip payload.
These declared uploads/programs are not original Windows shader or HMD acceptance.

The smallest sufficient final runtime selection is:

| Fresh containing suite | Required obligation |
|---|---|
| `results-presentation-runtime` | Full repaired original material/stage fixture, new duplicate-wrapper/control, inactive first-apply failure and complete final Results pixels, plus all existing controls. |
| `shared-window-pose-runtime` | Complete real coordinator-before-resolver cases for normal/review movies, map/scenario, host/client movement, resize/release and opening replacement; all controls. |
| The pose fixture's actual Unity physical-frame companion | Author/observer frame geometry and final pixels after move and resize, using the actual affected coordinator path. |
| `video-playback` | Its project compiles the entire changed coordinator, so its complete playback/election/audio/close container and 17 controls remain affected. |

The readiness stub changes only in `VideoPlaybackTests`, not `NativeVideoTests`.
The production native readiness property and reveal code are unchanged, and the
new Unity companion exercises that boundary with the changed coordinator and
real frame pixels. The complete Build 762 `native-video` and associated movie
packaging proof may therefore be inherited with exact source/project/toolchain
bindings. An interrupted current native-video run's passing native assertions
are additional partial evidence, never a claim that its whole container passed.
The existing `stereo-video-render` picture/decoder proof may also be inherited:
neither its stereo surface/shader nor projection/decoder algorithms change.
Results DTO/codec/delta/queue, native readiness and shared scrolling likewise
remain unchanged. The Results transaction's only production caller is
`NetAvatarDriver.Results.cs`; its only dedicated rendering consumer is the
full Results presentation container. No generic compositing/lifecycle change
or new serialization is authorized by this closure.

The integrator additionally owns strict Release/Debug builds, all 16 source
gates, the final compiled-scope comparison, and the build-metadata wire golden
obligation. Fresh worker evidence can be inherited only if final source,
fixture, native inputs, constant projections and toolchain bindings match.
Unchanged unrelated passing containers need not be rerun. Any broader final
production or fixture delta requires revisiting this decision.

## Recorded fresh worker evidence

The repaired Results container reports 391 production assertions. Its full
13-case run (`run-7vw0z5ue`) passed production and 11 controls; the remaining
root-pose mutation correctly failed at the later motion assertion because the
new inactive preparation already establishes its initial root pose. A focused
rerun (`run-4r5na81l`) passed that control with its updated expected marker.
All extracted C# source-content hashes are identical between these runs; the
only changed producer input is exactly that one expected-marker tuple, whose
reverse substitution reproduces the previous producer SHA256. This qualifies
the entire 13-case union, not a new single all-green invocation.

The earlier `run-fz9krdf3` failure is retained rather than counted as passing.
It exposed cross-case fixture resources kept alive by separate test assemblies;
guaranteed fixture-owned cleanup corrected that isolation boundary. No broad
texture identity admission was used to make the controls pass. The new old-image
import, exact-noise-clause removal and no-inactive-preparation controls each
reach their intended causal assertion in the corrected complete container.

Video worker checkpoint `ea51b8fa1` reports the complete playback container
(83 assertions and 17 controls), complete portable pose container (52 cases,
5,142 assertions and 51 controls), and the registered actual-Unity movie
companion (148 assertions and its compiling mutation). The companion binds
the complete coordinator and actual native readiness/reveal methods before the
real physical frame writers; decoder/discovery and bar/handle presentation are
declared ports. Its 32 captures prove geometry/pixels at that explicit boundary,
not decoding correctness, native movie appearance or headset output. The final
integrator must preserve these exact imports and source bindings.

## Evidence and practical limits

Review evidence is archived under
`.planning/validation/build763/review-lane`; its member SHA256 manifest binds
the exact-source inputs, full descriptor census, source closure, seven-caller
audit, compiler/runtime outputs and cleanup record. This lane did not change
another worktree, shared references, the refactor baseline or a production file.

The reported Build 762 symptoms and source paths are consistent, but automated
qualification cannot establish a correct headset picture. Paired hardware still
needs to confirm immediate complete Results after Continue and fluid identical
video move/resize/content for both users. The source proof identifies why these
two failures escaped the earlier fixtures; it does not reuse that incomplete
fixture as proof of the newly repaired behavior.
