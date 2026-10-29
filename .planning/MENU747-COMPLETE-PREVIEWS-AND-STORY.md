# Build747 complete More Cards previews and composed story foreground

The maintainer's singleplayer Build746 test reports that a More Cards preview
briefly appears complete, then is cut vertically at the native list boundary.
The subsequent story illustration and text also retain an oversized translucent
backing, although the preceding encounter no longer has it. Both presentation
rules apply equally to singleplayer and multiplayer.

Both current local hardware logs identify746/8e8a6c50d, built
2026-10-29 22:08:45UTC. The remote directory still contains older745 evidence;
it is not a new multiplayer reproduction. No new screenshot accompanies this
report. Immutable supplied logs and their hashes are retained under ignored
`.planning/debug/menu747/hardware-inputs/`.

This is class B: two owner-specific native UI visibility repairs. They change
neither the shared capture/coordinator infrastructure nor native game/network
authority, wire grammar, fixed capture quality, update rate or memory budgets.
The earlier qualified Build746 complete checkpoint supplies unchanged-area
evidence; affected production, native asset and fixture bindings are verified
separately. The Windows installation script builds the archive from dev.

## Causes and implementation

The native Further Ability Cards path deliberately skips
`AbilityCardUI.ChangeFullCardPosition`, the ordinary hover placement hook.
`UILevelUpCard.MouseEnter` then writes another position after the original full
preview toggle. Its transient sorting Canvas initially escapes the list's
`RectMask2D`, but the unchanged conversion adoption clears that escape on its
next sweep. Actual native callbacks and final-eye rendering reproduce the
initial complete picture followed by the vertical cut.

A postfix after the last native row writer uses the existing
`TooltipOnWindow` placement, raise and pool-restoration path. Native highlight,
navigation, preview visibility, selectable/gamepad and converted-owner guards
remain. The existing native hide edge returns the same full card to its original
`AbilityCardUI`; no duplicate card or replacement input handler is created.
An actual pool roundtrip also preserves the native row's world rotation under
a rotated window. Establish the full preview's owner plane before the existing
placement helper drops local Z; otherwise it discards an in-plane component in
the tilted row's basis. Only the preview moves; its native row stays untouched.

The composed story's actual loadout host owns a separate backing outside the
children moved into StoryDock. Hardware log lines4564/4611 and the read-only
level9 export agree: `UILoadoutManager.questInfo.imagePaper` owns the original
full1920x1080 `Black_Backdrop` with alpha0.517647, plus two direct GrabPass Blur
graphics. Build746's narrative fixture omitted this native host and could not
observe its remaining backing.

A scoped lease removes only those three native visual roles while that exact
controller chain participates in the story composite. The native illustration,
Paper, text/dialog, masks, buttons and input roots remain. Original flags restore
before hand-back; already-disabled, foreign or reused content is preserved.
Only three cached graphics are checked during the pose tick. The same lease
executes with singleplayer and multiplayer session inputs.

## Verification and limits

The actual native loadout host reproduces the old source defect with a final-eye
RGB difference2.79355526. The fixed affected narrative scope passes17 variants:
458 positive assertions and16 compiling causal controls, including seven new
host ownership/input/artwork/hand-back controls. All32 begin/steady direct and
captured host comparisons match the independently suppressed reference exactly;
16 explicit SP/MP pixel comparisons also match. Existing story/event/quest
comparisons remain. The primary final narrative scope independently passes
all17 variants after preserving the existing hand-back log token with its
corrected visual scope.

The complete worker cards scope passes36 variants:3207 assertions across six
positive cases and30 compiling controls in205.777s. The original30 cases remain.
The new399-assertion case exercises actual native pointer methods, both original
card sizes, gamepad/pointer guards and six real pool roundtrips. Every first and
all three steady captures retain all four corners, the complete title and both
independently projected edge marks. First-to-steady images are bit-identical.
The independent point-based reference differs only by float roundoff: at most
11 RGB units, one LSB per channel,1.53e-5 native pixels and zero projected pixels.
Independent geometry/ink checks accompany that explicitly bounded comparison.

The primary strict build first exposed Harmony003 analyzer errors on Vector3
member reads in the patch container. The final helper is outside that container;
there is no diagnostic suppression. An equivalent component-local read and two
mutation bindings are the only other changes. Final primary validation reruns
the six affected cases (399 assertions and five controls). The unchanged original
30 cases inherit the completed worker proof:53/55 raw inputs are exact; the
two bounded source/binding differences are normalized explicitly, with original
level-up observer and all guard/placement bodies checked. A superseded root full
cards run was interrupted, reaped and cleaned; it supplies no passing coverage.

Final productionc825d0462 passes strict Debug/Release0W0E in18.81/17.56s and all16
source suites in19.8s. An earlier parallel source attempt records15PASS/1FAIL:
enum-arrays raced strict build cleanup in obj. The successful complete source
rerun is sequential after that cleanup; the failed invocation remains failed.
Surface670 keys/266 Harmony signatures/4837 tokens removes nothing and adds one
exact MouseEnter hook plus bounded FURTHER CARD HOVER. Patch registration is
224 classes/339 methods; all12 frame orders and five EN/DE guide pairs pass.

Fresh Release/ILSpy review finds13 existing types changed: three local UI types,
nine exact746-to747 constant-only types and one branch identity. Two added types
are the narrow hover patch/helper; none disappear. The generated project differs
only in snapshot-relative reference depth. All32 embedded resources (8547877
bytes) are exact746. Existing capture and level-up observer types have no delta.
Of1762 strict source/build inputs,1757 are exact746 and five are the reviewed
bounded files. Wire source is identical after only the build constant/full-line
comment normalization. Inherit the qualified746267 checkpoint and306739 goldens
for unchanged areas; this is not a new complete267 invocation or golden run.
Raw results, failure history and final bindings are recorded under ignored
`.planning/debug/menu747/validation-final.json`.

These are source-bound Unity2021.3.5f1/OpenGL render and lifecycle proofs. Native
session/controller initialization and completed localization remain explicit
fixture inputs. Live transport, original Windows shader fragments, full native
game bootstrap and headset acceptance are not established. The story scope
executes the actual initial no-panel fit and mono capture kernels; it does not
claim the full converted content-fit or stereo story-depth coordinator.

The passive map-stall cause from the preceding multiplayer review remains open
in MP746-PERFORMANCE.md. This repair makes no new frame-pacing claim.

Every run retains source/input manifests, logs, captures and cleanup receipts.
Owned worker evidence is archived and content-hash verified before worktree
retirement; regenerable caches are removed after their processes stop. Hardware
inputs, shared game references and unrelated worktrees remain preserved.
