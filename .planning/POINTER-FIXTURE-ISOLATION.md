# Native pointer760 fixture isolation

Scope: class B, bounded fixture-only repair. Production pointer/capture kernels,
permissions, timing tolerances and the eight existing control oracles are unchanged.
The integrator owns the final aggregate gate and delivery to dev and Quest.

## Failure and bounded correction

The Quest full gate's `native-ui-pointer760-runtime/run-389ovhp7` failed the final
`fullscreen captured native click retains ordinary behavior` assertion. All eight
original causal controls passed. Its production frame delta and final hit list were
not retained, so this repair does not establish the exact original failure cause.

The preceding wheel/drag subfixture has an unmasked ScrollRect and full-card graphic.
Native scrolling can move that later sibling across the separate Options button.
The full card's pointer-only EventTrigger also implements IPointerClickHandler and
can consume a click without invoking Options. The fixture now moves the original
ScrollRect to normalized position0.5, asserts that Full card really is the Options
pixel's top hit, and destroys that completed list before the independent slider,
permission and fullscreen-click checks. No production behavior is changed.

The new `retain-scroll-subfixture` control removes only that fixture teardown. It
must prove Full card is still the final query's actual top hit before it can fail
the unchanged fullscreen-click assertion. Setup errors or unrelated exceptions
therefore do not qualify. All33 prior production checks remain verbatim; the new
adversarial-state check raises the successful production count to34.

Each failing case writes at most one diagnostic record with the wheel delta,
processed mouse position, stored/current Options pixels, capture ownership,
raycaster registration, card bounds, at most eight actual hits and bounded log tails.
Passing production emits no failure witness. The runner retains the exact copied
fixture path/hash for every case, including the compiling teardown mutation.

## Actual containing validation

Worker base: `d4adb82f5`, on the cleanup worker's finished general-development lane.
Fixture commits: `f12b862eb` and `208639bdd`. The actual source root was frozen Quest
`1de408a68`; fixture sources and cleanup helpers came from this isolated worker.

`run-42tnia0v` passed the complete containing inventory in34.312 seconds:

- Ten strict fixture builds, each with zero warnings/errors.
- Production34 assertions, retaining all33 original checks.
- All eight original causal controls and the new retained-list control.
- All27 pinned input files unchanged throughout the run (`source_stable=true`).

The new control's retained witness shows Full card at hit0/depth3 and Options at
hit1/depth0. Stored and current Options pixels agree at(2400,1500); capture ownership,
button permission and raycaster registration are true. The original DirectClick
actually chooses Full card. Native-display call count stays3 before/after the click,
consistent with the admitted captured-coordinate path. Its measured wheel delta
is0.0215051826s; overlap was deliberately supplied through native normalized position,
not attributed to a slow frame in the original failure.

The first attempt `run-1e8hp26z` failed strict compilation before Unity launched:
the new diagnostic accessed the game-private RaycasterManager type. Commit208639bdd
uses failure-only reflection on the executed EventSystem assembly. That failed
attempt, its source and its cleanup receipts remain preserved.

## Input inheritance and retained evidence

Clean main dev8cf and frozen Quest1de generate byte-identical common production
kernels, also exact to the final run's retained generated files. The native display
port is exact to the retained previous main pass `run-9vdnbvgh`. Full Core/CameraStack
files differ for Quest adapters, but the consumed predicate/constants/IsUiCamera
kernels do not. No Quest AOT/Weaver code is executed by this desktop Harmony fixture.

The final edited-file SHA256 pins are:

- `Common.cs`: `9fa777f8f3f6c22932a3cf3101ee94c8f6dad873ab9e924a9dba97fc43868e8c`
- `run.py`: `4079feeceb59da8846d3b3dccf3155e5842e536da671ea9072f1cee97dfeeed1`

The external evidence directory is
`/home/claw/quest3-local/build/evidence/quest-v1.2.4-rebase-20261113/pointer-audit/implementation`.
`final-input-closure.json` binds the remaining fixture/helper pins, all generated
kernels, native/game references, actual runtime DLLs and toolchain binaries.
`fixture-validation-evidence.tar.gz` preserves both owned attempts, including actual
DLLs, native exports, copied original/mutated fixture sources, manifests, witnesses,
logs and cleanup receipts; `evidence-archive.json` records its digest.

Reap/cleanup receipts and a final process audit show no owned editor/compiler left.
The completion API removed bin/obj. It initially protected the generated InputSystem
package's `Devices/Remote` folder by name; all four owned source/meta files were
preserved with exact hashes under Sources, then the same API removed the remaining
Unity Library (40,925,614 additional bytes, no skipped paths). Both private TMPDIRs
were removed after their processes stopped. No shared references/hardware inputs or
other agents' runs were cleaned.

This is complete affected-suite proof, not a new complete local/golden gate. Reuse
for dev and Quest requires the listed fixture/helper/native/toolchain pins and exact
generated production bindings to remain valid. It establishes fixture isolation,
not Windows fullscreen, Android/AOT, Photon or headset acceptance. Unrelated passing
full-gate scopes remain available for input-bound inheritance without repetition.
