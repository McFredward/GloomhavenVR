# Configurable pillar near viewing distance

## Request and source cause

The maintainer reports that Build656's radial metric still restores detailed
pillars only when almost touching them; figures have a satisfactory near range.
The radial fix retained an18cm full-detail guard. Frame's saved terrain near/far
caps are both0%, so the existing75cm near/far boundary selected0% on either side
and could not improve nearby pillars. This is a source-proven quality defect;
the available Frame log banners still identify654, not a new656 hardware run.

Figure distance LOD uses head-to-union-centre distance divided by the renderer
union's bounding radius, with8/7 and20/18 entry/exit bands. There is no single
physical distance shared by differently sized figures. A read-only audit of10
original hero/monster prefabs finds authored union radii1.127–1.702 world units.
At the logged rig scale11.08WU/m, the8× entry is approximately0.81–1.23m to the
centre, or0.71–1.08m after subtracting the miniature radius. These are authored
prefab bounds, not live animated actor measurements. Frame's figure cap0 already
uses tier20 nearby; only the far band reduces it further to tier5. No figure
policy, actor scan or live figure renderer is changed by this repair.

## Behavior and cost

Admitted pillars now retain their exact original geometry throughout the existing
configurable `ScenarioTerrainDistanceMeters` radius (saved/default0.75m), even
with both detail caps at0. Distance remains radial from the enclosing pillar
surface, with the existing4cm hysteresis; head direction cannot change it.
Outside this radius the original near/distant cap minimum still applies. The
same original-detail endpoint is retained through the camera-budget fallback.
Ordinary walls keep their18cm leaning guard and previous near/far semantics;
tracked-hand proximity, floors, doorway exclusions, native visibility, foreign
masks and continuous mesh morphs retain their contracts. EN/DE configuration
and curated help describe the existing control's effective pillar near radius.

No setting or default is added, removed or renamed. The common PC/Frame binary
keeps saved choices. Changing this radius is live and remains a quality choice:
a larger protected area can draw more nearby original triangles. This is an
explicit visual correction, not an FPS optimization or a prediction of zero GPU
cost. It adds no new discovery, actor census, sorting, matrix/bounds read, mesh
generation or per-eye proximity walk to the existing prepared-source Update.

## Validation and handoff

The isolated worker starts at committed dev7f9f34ba0; the ongoing657/658
integration and build stamps remain owned by the primary NPC agent. Production
and tests are delivered as separate commits for integration after that work.
Private evidence uses `.planning/debug/pillar-distance/` and the independent
audit worker's corresponding ignored directories. Native radii receipts include
original bundle hashes, prefab names, mesh sets and authored union dimensions.

The actual Unity2021.3.5 production probe passes2669 assertions with real meshes,
transforms, camera submissions and nonempty software-GL readback pixels. All10
audited pillar definitions execute eight-angle near/far cases at0.50/0.65/0.85/1m,
including saved0/0 caps, head yaw, hysteresis, live radius edits, board/head scale,
tilted/nonuniform/offset sources and native budget fallback. Native game scene,
configuration and bank delivery controllers remain explicit harness boundaries.
The full frozen focused run passes the production assertions and all94 causal
controls (95 compiled variants), including all91 existing controls plus the old
18cm-only guard, a near0%-cap override and ignoring live radius edits. Receipt:
`.planning/debug/pillar-distance-full/run-332m4_sb/`. The final source-stability
receipt is unchanged; the primary reuses this exact composed-source proof after
verifying that the committed fixture/production hashes match, without another
unnecessary full run.

Source16 and strict Debug/Release pass with zero errors/warnings. Direct wire
goldens pass299961 assertions. These are bounded affected checks, not a fresh
complete local gate. Unchanged figure/NPC scopes inherit their recorded evidence;
no new headset appearance or Frame FPS acceptance is established here.

The private committed-baseline comparison covers1247 production type files and
changes only `Loc`, `PerfConfig` descriptions and `ScenarioTerrainBudget`.
Config keys665, Harmony-text patches235 and log tokens4795 are unchanged. An
initial concurrent Release build recreated XML documentation during ILSpy, making
that attempted snapshot invalid; the final serialized snapshot strips it correctly
and confirms the three-type scope. The final receipt records the invalidation;
the initial build/snapshot logs remain in the archive.

Source/test commits are `8bc1fe735` and `d86617c1e` (the latter is the reviewed
cherry-pick of worker `d03f92d6e`). The NPC primary owns658; this disjoint follow-up
is handed off for659 after658 finishes. Do not overwrite its main checkout or
build stamp. Integrate the source, tests and this review, add659 metadata, and run
the affected terrain/source/build checks against that integrated tree. The worker
proof does not cover subsequent unrelated NPC source changes.

Compact receipts and verified worker archives are retained in the main checkout's
`.planning/debug/frame-pillar-distance/`. The two owned worktrees and generated
caches are removed after their archive contents/hashes and clean commits have
been verified; no parallel NPC/Quest worktree is touched.


## Final Build659 integration

Integrated after pushed NPC658 `e92ef8e53` from the exact private committed658
baseline. All61 production, fixture and original-asset inputs match the worker's
complete95-variant proof, so its2669 assertions and94 causal controls are reused.
The final integrated production probe repeats2669 assertions and all three new
pillar-radius controls. Focused scenario-scenery, wall-performance and player-help
checks pass, as do final source16, strict Debug/Release0 warnings/errors and300297
portable assertions. The initial player-help check rejected the worker's long
EN/DE radius description; both now retain the same behavior explanation within
the existing limit. The original failure and passing rerun remain separate.

Private surface/compiled comparison and final main Debug receipts live in the
main checkout's gitignored `.planning/debug/frame-pillar-distance/integrator/`.
The private658→659 comparison retains1,248 types and changes only three
intended runtime types plus eight numeric build consumers. All665 config keys,
235 patches and4,795 log tokens remain.
This limited follow-up inherits unchanged NPC658 composed197-scope evidence;
it does not claim a second uninterrupted complete local gate. Matching-headset
pillar appearance and NPC acceptance remain pending; no Frame FPS gain is claimed.
