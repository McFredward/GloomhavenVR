# Build760 native confirmation consumer proof

The Build759 hardware report still reproduced a persistent invisible ESC menu
and confirmation at maximum zoom. The source defect is the head capture's plane
normal: crossing two native pixel/world axes before normalizing produces an area
below Unity's absolute normalization cutoff. At pixel scales `.0012` and `.003`,
the old normal is zero; at `.009` and `.026`, it remains valid. The same zero then
reaches both eye resolves and the final carrier's physical depth reconstruction.
This is separate from the older root-center and repeated-blackout defects.

The previous759 desktop fixture already used a realistic `.0012075` native pixel
metric. Its blind spot was its oracle and surrounding scene, not that metric:
it accepted central white pixels over an empty head depth buffer, without checking
the material's plane normal or physical glass depth. It also constructed the
member before the first owner capture and supplied no-op geometry sync ports.
On GL, invalid normal/depth can still leave some color pixels visible. A color
count alone did not establish a usable physical depth surface.

## New source-bound consumer

Fixture commit: `520af9e0502ddc0b050a1f7f82dcc6e0627e6857`.
Production dependency: the shared plane-normal helper from `077afb97c`, integrated
by the primary agent. No production change is included in the fixture commit.

`Popup760.cs` runs the actual converted-panel `.6` head coordinator, `.7` owned
member registry, full `MenuDepthDisplay`, source registry, head AA and final head
carrier. `run.py` extracts the exact production `SyncProjection`, `SyncDisplayPose`,
`ApplyCaptureClear` and component-comparison kernels. Only the new case opts in;
older fixtures keep their supplied geometry inputs.

Each of the four logged scale pairs runs:

| Live rig scale (WU/m) | Native host pixel scale | States, both eyes |
| --- | --- | --- |
| 1.17 | .0012 | Mono ESC, owned confirmation, live move/resize, settled close, reopen |
| 24.88 | .026 | Same |
| 8.92 | .009 | Same |
| 2.77 | .003 | Same |

The native full root remains1920×1080 with the769px offset behind its fitted
412×1080 owner and368px visible ESC column. The tests use a65° perspective head,
800×600 final raster,824×2160 mono target, `.064 × rigScale` eye separation and
the scaled/clamped near-clip policy. Original window pose and metric remain live
through the same-opening move/resize.

The persistent confirmation's original serialized hierarchy, layout, sprites,
fonts and material properties are exported read-only from the installed game.
Its original blue body text is checked by comparing final pixels with a separate
render that changes only that exact TMP component's vertex-color alpha; the
frame, other text and world cannot satisfy that glyph witness. Original yes/no
frames use real Unity buttons and GraphicRaycaster. Exact production laser
`TryIntersect`, hit-corner and visible-UI kernels execute before each explicit
button callback.

Every owned frame must have a finite unit material plane normal with the correct
winding, finite physical glass-depth reconstruction operands, visible owner and
question glyphs, original button hits, and no coordinator fallback. An opaque
world surface behind the window must not cover it; a real opaque partial surface
in front must still cover its part. This covers the depth condition the earlier
empty-background test omitted.

The compiling historical control restores only the old `RenderHead` cross/normalize
expression. It fails the named unit-normal invariant at `.0012` on both backends.
Its saved final GL and Vulkan images also visibly lose the compact original
question/buttons; the fixed images retain them and the correct front occluder.

## Results and elapsed validation

| Backend | Scope | Result | Case execution |
| --- | --- | --- | --- |
| OpenGLCore, llvmpipe LLVM20.1.2 | New consumer | 340 assertions | 5.172s |
| OpenGLCore | Historical normal control | Expected invariant failure | .624s |
| OpenGLCore | Existing Popup759 consumer | 68 assertions | 1.102s |
| Vulkan, Intel Graphics RPL-P | New consumer | 340 assertions | 5.855s |
| Vulkan | Historical normal control | Expected invariant failure | .692s |

Thus748 positive assertions and two compiling causal controls passed. Both final
runs report618 unchanged source/input pins. Final run wall times, measured from
completion-log creation to canonical cleanup receipt, were26.83s (GL) and24.23s
(Vulkan). All nine development attempts, including fixture compilation/oracle and
close-scheduling failures, are retained; their corresponding total is146.93s.
These times describe this focused fixture, not a full gate or hardware frame pacing.

Final GL evidence: `run-q6myj78p`; Vulkan: `run-sj5321ny`. Each backend retains40
final state/eye frames, original-question-muted images, mono targets, source and
executed-assembly hashes, results, timings and cleanup receipts. The primary
agent owns the complete final-union Class C checkpoint.

## Explicit limits

- Native game/session bootstrap, ESC callback authority, confirmation request/lease
  identity, initial panel admission/allocation and the ordinary `.2` capture clock
  are inputs. The actual native confirmation lease is covered separately by the
  earlier native fixture; this consumer assigns its identity and tests the real
  owned-member capture. The source `Apply` placement kernel executes, but this is
  not the full `ConfirmationOnOwner.TrySeat` or `ModalFallback` tick.
- The ESC producer's column and final fit are declared authored/logged inputs.
  The persistent confirmation's static presentation is the original asset; its
  gameplay controllers are not installed. Native yes/no listeners are counter
  callbacks, not actual quit/session transitions. The original root dimmer and
  Blur are excluded as explicit foreground-lease inputs.
- The first close-transition draw in this editor scheduling port is blank: the
  native mono target is already correct, the photograph Canvas is re-enabled,
  and the next normal Unity rebuild restores its visible mono presentation.
  `*-close-first-transition.png` and `close-scheduling-limit760.csv` retain all
  four witnesses per backend. The settled close and reopen are checked after
  that rebuild. **This proof does not certify first-frame close parity of the
  full game player loop and does not justify a production lifecycle change.**
- These are real GL and Vulkan final pixels, not Windows D3D11, an HMD compositor,
  the exact4040px hardware raster, multiplayer timing or headset performance.
  A hardware retest must confirm the reported persistent tiny-zoom failure.
- No minimum font quality at arbitrary absolute `.02` rig scale is claimed;
  the earlier standalone/direct-SDF probe's separate limitations remain.

Every run reaped its editor/compiler processes before the canonical
`cleanup_completed_run` API removed regenerable caches. Source, input manifests,
failures, captures, results and assemblies remain in the evidence archive. No
headset writes or actions were performed by this worker.
