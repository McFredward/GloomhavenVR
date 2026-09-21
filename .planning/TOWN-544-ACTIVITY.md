# Town activity runtime (build 544)

## Implementation contract

A sole resident author selects the stable shared attention target. Active visitors take
priority; other tracked players interrupt work within 2.4 canonical metres and leave at
2.9 metres. Missing flat-player/headset poses are not invented. Activity interruption is
an analytic 0.65-second transition whose starting blend is preserved on reversal. Its
integrated work clock pauses smoothly and resumes the same phase. Original Idle body
sampling continues; deterministic two-bone arm contact correction runs afterwards and
before facial motion. No gameplay controller or action is invoked.

The merchant keeps a native coin in the left grip and an authored small reed pen in the
right grip while alternating counting and writing on the original open-book mesh. The
enchantress alternates studying and a restrained spell gesture. The priestess holds a
prayer pose. Two-bone correction was chosen over additional disconnected animation clips
because the tabletop and tool contact positions must remain stable after terrain grounding.
All fingers/axes/grip searches are cached. Cosmetic failure cannot gate a native visit.

## Network

Version 3 and records 79/80 remain unchanged. Additive81 has 52 payload bytes, 54 including
its TLV header: active flag, epoch, sequence, shared clock and three analytic occupation
states (work clock, transition age, starting blend, engaged flag). Recovery presence has
worst-case 7,136 bytes, below the unchanged 7,168 assembly limit by 32 bytes. The 7,393
allocation still reserves the previous 257-byte largest-record margin.

Message22 atomically carries unchanged record80 followed by81: 156 bytes, 15 Hz, 2,340
raw bytes/second for all three residents from one authority. This replaces the new
publisher's standalone102-byte message21 (increment810 bytes/second), whose reader and
literal compatibility vector remain. Both records must validate and share epoch, sequence
and sample clock before either is committed. Established paired peers reject standalone
message21. Presence distinguishes a malformed81 from absent old-peer81 so a bad body
record cannot advance the face half through legacy fallback.

Each peer interpolates from its actually displayed pose at packet arrival, using the
same reconciliation interval for head and hands. Render time continues between packets.
Sequence wrap, retired epochs and temporary suspension use the existing face protocol
rules: fast packets cannot resurrect a suspended peer; a newer same-epoch presence can.

## Focused validation and remaining limits

- `check-town-activity.py --portable`: real production analytic phase and remote playback
  with explicit scalar/vector math boundaries, 24,031 assertions and five deliberately
  compiled defects. It compiles only phase/handover/network methods and needs no game
  assemblies; scene and rig methods are excluded.
  Registered for hosted CI; this mode makes no Unity scene/rig claim.
- Real Unity optional `--bundle`: imported three-NPC arm chains, continuous target contact
  and body reset/no accumulation. Production terrain grounding is applied at0/±3.5cm;
  writing wrist contact must remain within3mm, and prayer wrist rotations stay continuous
  during interruption. Latest run:92,107 assertions/nine compiled defects against the
  final build544 Linux bundle SHA256
  `e0a1357db551bdbab3995f1ee37c3a728e03e164a284cc57096aa7ea55c0a0f3`.
- Direct wire executable:260,310 assertions passed, including all partial81 tails after
  valid79/80 and invalid full81. Release compile:zero warnings/errors.
- Resident lifecycle:145 assertions/eight negative controls; settings, station and
  grounding:1,581/71/243 assertions and14 compiled negative controls. Missing/failed arm
  rigs withdraw work tools and never block native services. Face regression:2,076 assertions
  and16 compiled negative controls.
- Contact images are diagnostic, not headset evidence. The fixture now CPU-skins the
  current pose into an explicit static snapshot because multiple `Camera.Render` calls
  during one Editor tick can reuse an earlier GPU skinning upload. The native open-book
  geometry has a neutral diagnostic material; its diagnostic coin is labelled non-native.
  Runtime still uses the original game props/materials.

Earlier CPU contact-v8 had writing tip at
(-.0594,.9968,.1806) in station metres; the merchant leans24 degrees while writing
with a smooth45-degree wrist roll, preserving page contact after grounding. The separate
pen-off view2 proves the remaining thumb flap is skinned hand geometry, not tool geometry.
Exact diagnostic local bone rotations are exported alongside each image for asset review.
Final544 automated and diagnostic-image evidence is recorded below. Headset motion,
multiplayer hardware parity and measured headset performance remain unverified.

## Returning-author recovery review

A still-running lower-ID author may return after a network partition with a different
visitor/work phase than the temporary author. Per-peer packet interpolation alone does
not cover this source switch. Each resident now keeps the actually displayed contact
pose and face angles, and reconciles source/epoch changes over a shared0.35-second window.
A second handover during that window starts from the current intermediate pose. Evaluated
hands, grip curl, chest/wrist turn and spell intensity/sway are blended directly; interpolating
work clocks such as1000→8 would incorrectly race through many occupation loops.

The returning-author regression passes through production math in the portable gate;
a deliberately compiled immediate-snap variant fails the initial hand continuity check.
The production population fixture now covers the delayed gaze convergence after a new
author. No new bytes, gameplay gates or viewer-driven attention election were added.
The final repaired544 bundle was subsequently validated as recorded below; prior543
asset assertions are retained only as historical evidence for the previous geometry.

## Thumb deformation and hosted-CI follow-up

Controlled same-wrist renders with the pen hidden distinguish the original healthy relaxed
hand from new thumb deformation. Holding non-thumb curl unchanged and reducing only the
thumb removes the broad flap. The final thumb factor is5 (maximum2.75 degrees per joint),
and the writing wrist moves(+6,-6,+11)mm from contact-v8. Final contact-v9 pen tip is
(-.059443,.996978,.181965) metres, retaining page contact. Full0/±3.5cm terrain-offset
contact assertions remain strict; the new compiled thumb-overcurl defect is rejected.
Exact final activity bone rotations accompany contact-v9 images and were sent to asset
review. No hand topology or source game data was changed.

The hosted portable gate was additionally run from a source-only checkout with no
`ressources` directory and an explicitly nonexistent Unity path. Only the committed
metadata reference directory was available (unused). All24,031 assertions and five
compiled negative controls passed; the executable dependency manifest contains only
itself and the .NET runtime, no Unity/game assemblies. This is separate from real Unity
scene/rig evidence. The original metadata-only hosted environment cannot execute real
Unity method bodies and is no longer asked to do so.


## Final 544 actual-prefab validation

Both focused gates ran against source commit
`1b138e9d86b06b8a768ca46cb7453b55018d3ae9` and the same immutable Linux bundle:

- Path: `/home/claw/gvr-town544-faces/.planning/debug/town544-final-assets/town-assets-mgsr1d_g/evidence/town-review.bundle`
- Size: 98,331,564 bytes.
- SHA256 before and after both gates:
  `e0a1357db551bdbab3995f1ee37c3a728e03e164a284cc57096aa7ea55c0a0f3`.
- Unity 2021.3.5f1; no runtime source change was needed after these runs.

`check-town-face.py --bundle-only` passed 9,862 production assertions, three compiled
negative controls and nine in-memory anatomical weight-corruption controls. All three
imported prefabs have three facial LODs and all 11 required shapes per LOD, actual Head/Eye
axes, body-reset/no-accumulation behavior and converging eye rotations. Independent
anatomical metadata supplies 144 skull and 144 jaw probes per NPC. Maximum rigid skull/jaw
residual is 1.864 micrometres; maximum eye target error is 0.02798 degrees. Eye separations
are 78.994/64.880/52.286 mm for merchant/priestess/enchantress. Each has four eye renderers,
2,566 vertices, 4,880 triangles and 144,536 bytes of Unity mesh memory. All four original
body clips are exercised; the gate does not enumerate additional animation states.

`check-town-activity.py --bundle --render` passed 92,107 production assertions and nine
compiled negative controls. This includes each final NPC rig, 0/±3.5 cm production terrain
grounding, writing wrist contact below 3 mm, bounded thumb curl, interruption wrist
continuity, body reset and authority-recovery reconciliation. Final writing pen tip is
(-.05944329,.996977746,.181964785) metres in station coordinates.

The final render set includes work, writing/casting, fully attentive and phase 3 normal
work-focus gaze. Phase 3 uses actual production FaceMotion.Aim and FaceRig.Apply after body
posing, rather than an imposed head angle. Its settled work focus reaches the allowed
+22-degree head pitch for all three NPCs, with effectively zero head yaw. Thus the earlier
expectation that ordinary ledger gaze would necessarily bend less than the extreme was
incorrect for these final authored poses. Front views were inspected with that actual
combined pose; no additional head/neck tear was identified. This is a still-image check,
not a claim about headset comfort or motion quality.

Evidence directories in the activity worktree:

- `.planning/debug/town544-final-face/run-b4__jz6h` (JSON anatomy/rig metrics and controls).
- `.planning/debug/town544-final-activity/run-7vtc78vk` (production activity and controls).
- `.planning/debug/town544-final-activity/contact-final` (three NPCs × four phases × three
  views, exact local bone JSON and CSV contact/gaze measurements). View 2 intentionally
  hides the pen to distinguish tool geometry from hand skinning.

The diagnostic book retains the original mesh with a neutral material, and the render
fixture's coin is explicitly non-native. These images do not validate shipping native
prop colors, synchronized multiplayer headset appearance or GPU performance. Those remain
hardware-test checks; actual production rig/contact and protocol gates have passed.
