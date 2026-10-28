# Quest rebase onto dev Build734: shared presentation audit

Date: 2026-10-28. This is a source-obligation review. It does not certify a
Quest headset picture, movement clarity, frame pacing or paired multiplayer.

## Inputs and scope

- Finalized upstream `dev`: `fdbd372e30ed3ab49fb3e3e69af267f181edb800`,
  ModBuild734, including published city/quest Build733 behavior.
- Old Quest tip: `595791b7825c28b87069db8b2037386b755197ef`.
- Old Quest release base: `4f10ddd60524911bf4cf39777df46f95d7691d83`.
- Audit worktree starts directly at finalized upstream `dev`; dependencies were
  initialized by the integrator. Read `AGENTS.md`, `CLAUDE.md`, the current
  `.planning/STATE.md`, newest `NetProtocol.ModBuild` notes and
  `docs/TEST-STRATEGY.md` before review.
- This worker initially owned only this note. The integrator subsequently
  authorized narrowly scoped Quest observation ports in the desktop rendering
  fixture, then the two character reader production files and narrowly needed
  integration fixture coverage after the concrete AOT finding below.
  The integrator owns the final class-C rebase validation, including the complete
  local gate and wire goldens. The documentation review is class A and the
  bounded fixture repair is class B.

The initial 180-file SHA-256 inventory compares the release base, old Quest and
finalized upstream. It is retained at
`/home/claw/quest3-local/build/evidence/quest-dev734-rebase-20261028/parity/phase1-source-inventory.json`
(SHA-256 `d063138334936c25a1de348ff7c199f9d97a46ee4fc185e94275bb80238e9816`).
The inventory is an explicit source-review boundary; it does not establish all
transitive fixture, native asset, toolchain or runtime proof bindings.

## Upstream obligations

### One absolute world frame for shared windows

Retain the complete upstream shared-window chain, including records119–121,
`SharedWindowSpawnAuthority`, `SharedWindowPoseContext`,
`SharedWindowScaleContext`, `SharedWindowSpawnBridge`,
`ModalFallback.SharedWindowSpawn`, `SharedWindowSize`, `GrabbableModal`,
the remote initial-pose consumers and native video/reward placement.

- `SharedWindowSize.ReferenceWorldScale` uses common map parchment or native Hex
  geometry. A viewer's current/saved zoom never determines shared render size.
- The captured `GrabbableModal.SharedReferenceWorldScale` survives author,
  scene and rig transitions. Each posed window transports this rendering reference
  independently of record119's author coordinate scale.
- Host gaze chooses the initial origin and yaw. A native flat host receives one
  compatible VR placement surrogate; followers wait for its accepted current
  opening before reveal. Native continuation remains independent of placement.
- `ComputeSharedHostSpawn` uses the captured common reference for distance,
  clearance and final fitting, including replay of the original gaze. It must not
  reuse a private reward spawn distance or current host zoom.
- Keep synchronized manual move/resize, existing readability/board constraints,
  author/occurrence qualification and convergent browsing dismissal. Incidental
  table/X input cannot privately retire an accepted native quest proposal.

The old Quest delta does not modify these new shared-window production files.
Their upstream versions should survive the rebase unchanged unless a separately
reviewed platform dependency requires an adaptation.

### Native city and quest readiness authority

Retain upstream `MapCityEventReadyUp`, `MapQuestReadyUp`,
`MapQuestReadyRoster`, `ReadyToggleParkClaim`, `LoadoutConfirmPark`,
`MapInputGate`, `NativeMapQuestSelection` and their modal/remote consumers.

- The exact native `SelectCityEvent` occurrence grants a presentation lease for
  the original participant control and existing encounter pose lane. Visibility
  does not invoke the original auto-ready continuation or synthesize a vote.
- Unassigned observers see the common disabled offer. Native interactability,
  visibility blockers, ready/cancel callbacks, participants, ACKs and actor/network
  authority still own progression.
- Quest, city and loadout reuse of the singleton ready toggle is phase- and
  source-qualified. Retiring an old lease cannot steal the next role's control.
- City role identity survives incidental quest focus, ACK waiting and explicit
  original 2D confirmation. Public city decisions fence questlog/table browsing
  and obsolete cached cosmetic openings. Native participant departure validation
  remains enabled for the existing city quorum/ACK path.

These new role/authority files are not changed by the old Quest delta. The
overlapping `TablePanelSurfaces.cs` adds Quest artwork observation only; preserve
upstream initiative lease/lifetime behavior and add the observer at its current
retained panel boundary.

### Complete moving character window, original hints and preview rotation

Retain the complete upstream `WorldUI/MenuDepth` implementation, the native-flat
coordinator, panel coordinator, `CharacterTooltipPlacement`, tooltip consumers
and current rendering assets. Source-only target/rate checks cannot establish
the Build729 requirement for the entire window's final text and figure pixels.

- `MenuDepthHeadRaster` derives a fixed actual-head pixel grid and exact
  format/depth memory cost. Its finite eight-window/four-scratch ledgers are lazy
  admission bounds; head movement cannot lower the complete UI/model quality.
- `PanelSupersample.6.MenuDepth.CaptureMenuDepth` renders the original full UI and
  prepares the second eye even when the coordinator was already visited in the
  current frame. NativeFlat uses its corresponding actual-head carrier path.
- The private cached mono photograph stays retired for the spatial presentation
  lifetime. Release, loss of content, failure and ordinary mono fallback keep
  their native continuation and rendering recovery.
- Original ordinary UI clips to the original window. Exact authored-depth hint
  rectangles and foreground card silhouettes retain their own correct extent.
  Preserve masks, original character framing, original card effects and shaders.
- Original skin, personal-quest and colliding character-creation hints occupy the
  right-side annotation lane without resizing the native window. The serialized
  TMP description child admits the real personal-quest hint independently of
  whether a character model is ready or depth is enabled.
- Laser drag of the exact original private portrait changes a rendering matrix
  about the fixed native main-ground origin. It never changes a native gameplay
  actor, holder, animation transform, camera placement or window pose. Keep the
  native focus callback and identity/visibility/press/drag lifetime checks.

The old Quest branch predates these new MenuDepth/hint/rotation files. Keep them
upstream. The main collision points are coordinator boundary files:

| Combined file | Preserve upstream | Retain Quest adaptation |
|---|---|---|
| `FlatScreen.2.CameraStack.cs` | Current UI split/capture and menu-depth lifecycle | Captured-camera owner, movie prepare/refresh, native screen material/mesh and sampling hooks, receipt release |
| `FlatScreen.7.CaptureRender.cs` | `SetMenuDepthGuard`, borrowed-camera exclusion and current final cull path | Unavailable-boundary subscribe/unsubscribe and post-render menu-producer observation |
| `PanelSupersample.2.Capture.cs` | Head-raster lifetime, grid/sampling/allocation handling and current display policy | The two `QuestTextureCopy.Copy` resolve/carry operations |
| `WorldUIModule.cs` | MenuDepth, hints/rotation and all current module registrations | Existing `QuestTutorialViewportMaskPatch` registration |

The final proof must exercise actual coordinators and final head pixels throughout
motion, including both text and figure. Upstream's GL/render proof does not
certify the Quest Vulkan/headset backend or joystick frame pacing.

### Deliberate movie tests stay behind Cheats

`BuildVideoReviewTriggers` is called only from the gated Cheats builder under
Advanced. It checks `CheatsAvailable` at construction and again inside each play
action; cached actions cannot start a spoiler movie after permission revocation.
The ordinary Test triggers page cannot expose movie names/actions. Close remains
available; normal game-authored playback does not consult this test-only gate.

The intentional old Quest `VROptionsTab.VideoReview.cs` adaptation selects the
authored visible `Title` with `FindPart<TMP_Text>` instead of the inactive template
found by `GetComponentInChildren`. Preserve that single label-lookup repair.
It does not authorize changing Cheats routing or resurrecting obsolete actions.

### Card privacy and UI-only mixed reality

- Preserve `RevealGate`: remote selection fans/held/placed cards and remote
  short-rest burn flights remain covered, action-phase cards are open, and
  damage-sacrifice action choices remain open. Card-name disclosure is a separate
  permission. Local controlled cards always keep their fronts. The entire 3D map
  is public; failed artwork is a rendering defect rather than concealment.
- The Quest card face/material/alpha/fire and mesh repairs remain rendering
  adaptations. They cannot introduce privacy gates into local rendering or
  bypass remote reveal permission. The Quest changes in the shared driver/tray
  paths add bounded guide observation and equivalent cue vertex construction;
  preserve upstream dismissal/flight/seat state and existing wire semantics.
- Keep UI backing ink/depth and mirror behavior in `PanelInkBounds`, `MrBacking`,
  `MrBackingAnimation` and `RemoteWidgetMirror`. Quest native passthrough changes
  activation/transparent sky clear only. It must not add backing geometry to
  water, fog/unseen terrain, preview meshes or any part of the play area.

## Validation handoff

Phase1 executed source review and a three-ref hash inventory only. No runtime
test, Unity import, package build, APK or ZIP was run or produced by this lane.
There were no test processes or generated test caches to reap/clean in phase1.

### Frozen candidate source comparison before the AOT repair

The reviewed rebase candidate is
`8f3921adc41d7bf2063a5f48d82d20a87a7c4f57`, directly descended from finalized
dev734. ModBuild735 and its new rebase note are intentional integration identity.
`candidate-source-inventory.json` beside the phase1 inventory records all 180
input hashes. Seventy-seven production/art inputs changed upstream since the
old release base are byte-identical to dev734 in the candidate, including all
new shared-window, city, MenuDepth, hint and rotation files and rendering assets.
The 17 differences in the scoped inventory are known Quest presentation hooks,
the native-video script and suite manifest. No obligation rollback was found.

The added/removed source lines of the Quest adaptation are identical before and
after rebase in ten reviewed overlap files: shared card driver, piles, tray,
player help, remote board furniture, camera stack, final capture guard, panel
capture, initiative surface and WorldUI registrations. The additional
`PerfMonitor` retention hook is rebased onto the current upstream signature and
keeps its native-capture conditional/reporting behavior. The single movie-status
Title lookup remains the intentional Quest difference.

### Executed coordinator fixture repair

The first focused `menu-depth-desktop` invocation failed before runtime at five
missing fixture members after 9.9 seconds. This was fixture API drift caused by
importing the retained Quest hooks from the actual camera stack and final guard;
compilation failure was not counted as a causal-control pass. The original failed
report, build log and cleanup receipt remain under the parity evidence directory.

The authorized repair changes only
`tests/GloomhavenVR.MenuDepthDesktopTests/Boundary.cs`. Two unavailable-final-cull
ports throw, just like their existing neighbors; the fixture still deliberately
selects its actual built-in `Camera.Render` fallback. Three documented no-op ports
cover the Android producer observation, producer receipt release and empty
movie-census refresh. They supply no coordinator readiness, UI draw, projection,
texture, raster or movie ownership. Production sources, causal mutations,
expected pixels and tolerances remain unchanged.

The complete containing suite, including all original controls, passes through
the central runner: 16 positive cases, 2,860 assertions and 36 negative controls
(52 cases total), in165.7 seconds. Actual NativeFlat and panel coordinators draw
the final text/figure head pixels over52 poses, both eyes, actual4040 resource
ledgers and annotations. The retained source stability receipt is unchanged.
Sampled final motion captures were also opened beside their independent fixed
world references. This result predates the subsequent character-reader source
repair; the primary's final class-C gate must bind the final union again.

The primary's class-C complete gate must include the existing shared geometry/
pose/reflow, map quest/city readiness and handoff, privacy, MR, video and menu
consumers. In particular, `menu-depth-desktop` includes real NativeFlat and panel
coordinators, final head text/figure motion, both-eye preparation and causal
controls. `character-tooltips`, `character-tooltips-native-flat` and
`menu-depth-rotation-integrated` protect the native hint/creation and preview-only
rotation consumers. A focused pass is not a complete gate; inherited upstream
264-suite aggregate proof is not a new successful full invocation.

Record final SHA, current input hashes, exact executed suites/counts/controls,
failed attempts, inherited input-bound evidence, elapsed validation and completion
cleanup separately. Quest-specific headset/Vulkan motion, right-side readability,
rotation and paired independent-zoom multiplayer acceptance remain hardware work
until actually observed on matching build banners.

### Android resource and native-runtime boundary

The upstream MenuDepth bundle provenance targets `StandaloneWindows64`, even
though that bank includes desktop Vulkan as well as D3D11/GLCore programs. The
runtime loads the embedded `GloomhavenVR.MenuDepth.Rendering.bundle`. Rebuilding
this bank for Android/Vulkan and wiring its current original character, display,
graphic, native card and dissolve programs is a separate required resource-lane
obligation. The old Quest mod-art builder predates this new bank. Current story
depth/catalogue data also need automatic inclusion in the Android delivery.

This desktop coordinator test exercises GLCore with explicit XR observation
ports. It cannot certify Android AssetBundle compatibility, IL2CPP reflection or
dynamic-method availability, native Vulkan shader/copy behavior, mobile memory
admission or a Quest headset picture. The primary owns their platform proof and
records hardware limits separately; a retained fallback alone cannot satisfy the
requested whole-window stable-depth behavior.

The source audit found a concrete new AOT obligation in the otherwise unchanged
upstream code. `MenuDepthCharacters.CreateHiddenReader` constructs a
`DynamicMethod`/`ILGenerator`; failure returns null and `Discover`/`TryAdd` then
reject character source enrollment. `MenuDepthCharacterSource` initializes its
static `ReadOriginal` through a second `DynamicMethod`, without an exception
fallback. Runtime code generation is therefore on the production readiness path,
not merely a diagnostic optimization. No corresponding Quest builder rewrite was
found. The integrator explicitly extended this worker's ownership to
`MenuDepthCharacters.cs` and `MenuDepthCharacterSource.cs` to correct this path.

### AOT repair and executable coverage

After the existing exact field-name, visibility and type checks, the Android
platform returns a read-only delegate over the cached `FieldInfo`. No native
field is written or rescanned per eye. The original reference identity and hidden
bool still supply production readiness; normal desktop retains its original
`DynamicMethod` fast path. No UI layout, allocation, sampling, actor, window or
rotation behavior changes.

The existing `menu-depth-rotation-integrated` suite adds one Android execution
and four causal controls, preserving all original cases and thresholds. Only the
platform observation is supplied; any reached dynamic-method constructor throws
`NotSupportedException`. Actual source enrollment, original identity replacement,
native hidden readiness, uGUI drag and rendered native scene still execute.
Removing either Android accessor, hiding the hidden-field value, or replacing the
original character reference each fails its own expected behavior. The containing
suite passes312 assertions and8 negative controls in22.8 seconds (previous
desktop156/4 retained, Android156/4 added), with15 stable pinned inputs.

The strict complete production Release build against real native references passes
0 warnings/0 errors in17.5 seconds. Both projects use private artifacts; current
DLLs, build/input logs and hashes are retained. All16 source gates execute in70.6
seconds:15 pass; `patch-inventory` fails because the rebased candidate's generated
`docs/PATCH-INVENTORY.md` is stale. Its own9 checker tests pass. This reader patch
adds no Harmony patch or registration; the integrator owns regeneration on the
final union. The failed source-group result stays failed until that proof runs.

Final reader source SHA-256 inputs:

| File | SHA-256 |
|---|---|
| `MenuDepthCharacters.cs` | `96b8f1663e9044e83391cb945aeee1750b12e40e106c3d9b4774d1d1d3ebb063` |
| `MenuDepthCharacterSource.cs` | `f8da42aba005e8fecdc36629580f0ef29e8293ec3010fa9ede4786795d9fb2f3` |

`aot-final-inputs.json` retains all six changed production/fixture input hashes;
the executed integration, strict build and source-group reports live in
`parity/{aot-integrated,aot-integrated-executed,aot-release,aot-source}`.
The coordinator proof is in `parity/{coordinator-final,coordinator-executed}`;
the initial failed fixture report remains in separate `coordinator-first` and
`coordinator-failed-fixture` evidence. No complete local gate is claimed here.

After every run, owned processes were reaped and common completion cleanup ran.
The two desktop runs remove73,535 and37,805,803 bytes of generated project caches;
the AOT integration removes8,469,025 bytes. The private strict build's first
cleanup deliberately refused its copied movie/content reports above the retention
limit. Each copied asset was then verified byte-identical to its retained canonical
source before pruning only those duplicate output copies (1,055,761,910 bytes);
the common API removed31,133,714 intermediate and20,472,832 binary cache bytes.
Complete receipts, source hashes, logs, captures and current DLLs remain retained;
no private build caches remain. These byte counts describe removed files, not
filesystem block reclamation or shared extents.

The AOT path is executed with an explicit Android/codegen-refusal observation
boundary on desktop Mono. It still does not certify a real Android IL2CPP launch,
the Android shader bank or headset appearance. Those platform/hardware checks
remain separate, as does the primary's final class-C integrated gate.
