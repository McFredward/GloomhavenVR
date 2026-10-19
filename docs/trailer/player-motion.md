# Purposeful player motion in the revised film world

`scripts/trailer/render_player_motion.py` supplies a reusable local motion
driver for the corrected trailer review. It loads the revised continuous
full-scale courtyard, preserves source models and replaces the baked pointing
glove with its original live skin and rig. The previous tabletop composition
is not used. This is cinematic reconstruction, not runtime gameplay footage.

The approved production scope is **one corrected review video, followed by a
pause until the maintainer approves it**. A few source stills and private motion
checks support that review; they do not authorize further trailer scenes,
additional generation calls or another automatic video iteration afterward.
This script makes no paid API calls.

## Performance

Grimhorn already watches the living Brute. Its hand immediately begins a curved
reach; the wrist turns while the original thumb and fingers gradually close.
The real thumb/index midpoint approaches the Brute's head contact anchor.
Closure and the beginning of the lift overlap, avoiding a dead hold. During
the lift, the original idle continues and the grip follows its contact anchor.
Grimhorn tracks the rising hero with a small purposeful lean.

Runeveil enters late, watching the held Brute. It turns briefly toward Grimhorn
with a small acknowledgement, then returns its attention to the hero. Both
arcane hands stay low and asymmetric, clear of Runeveil's face. Resting fingers
have gentle differing bends and folded thumbs, with slight settling motion.
The two players always retain their original four hands. No avatar bodies,
extra fingers, artificial random jitter or invented combat actions are added.

The source-local X finger hinges and authored full-curl proportions match
`Hands/FingerCurler.cs`: fingers 75/95/65 degrees, thumbs 25/45/60. Original
skin weights, bind relationships and mesh/UV data remain intact. The source
rig has corrected hinge axes; the cinematic driver does not add historical
pinky counter-abduction that the original asset no longer needs.

The Brute uses the exact native idle samples. If a source still stages an
original attack pose, only the local cinematic copy is reset to the original
idle basis. Triangle order is verified before adding the already tested native
animation helper. Source game banks remain read-only. The contact anchor follows
a stable cohort of original head vertices through that idle; its method and
count are recorded with per-frame targets and actual thumb/index endpoints.

The camera begins at character height. It reveals Grimhorn's face before hand
contact, then pulls back as the reach and lift continue. The full performance
can be rendered at 6–8 seconds. At eight seconds, the intended beats are a
living Brute during the first 1–1.5 seconds, mask discovery around 1.5–2.5,
reach and grip around 2.5–4, lift around 4–5.5, and Runeveil's meaningful entry
and glance during the last 2.5 seconds. A
separate opening shot may establish the pure film world before this reveal.
The confirmed final camera is `(-2.394, -18.370, 8.099)`, aiming at
`(-0.089, 0.789, 4.039)` with a 27.018 mm lens. It retains readable masks and
grip. Resting hands remain low and palm-down outside the courtyard. A modest
wooden edge may enter the last beat; a full overhead/table/gameplay transition
and the broader trailer edit remain outside this review's scope.

## Local review command

All media must be under the main checkout's gitignored
`.planning/debug/trailer/revision-02/motion/`. Use a new version directory for
each run; existing evidence is never overwritten. Pass the newest approved
packed world and its exact manifest, rather than an old lighting study.

```bash
xvfb-run -a /home/claw/blender-4.2/blender -b --python-exit-code 1 \
  --python scripts/trailer/render_player_motion.py -- \
  --world-scene /home/claw/gloomhaven_vr/.planning/debug/trailer/revision-02/world/approved-reference/god_presence_base.blend \
  --world-manifest /home/claw/gloomhaven_vr/.planning/debug/trailer/revision-02/motion/review-source-input/world-manifest-pinned.json \
  --output /home/claw/gloomhaven_vr/.planning/debug/trailer/revision-02/motion/review-source-v1 \
  --duration 8 --width 1280 --samples 16 --animation --save-blend
```

For fast composition checks, omit `--animation` and pass a short frame list,
such as `--frames 1,72,144,192`. Check those images before rendering the one
review source movie. Rendering time is measured per frame; this is a motion
blocking source with modest samples, not a final lighting-quality claim.
The root editor adds audio and the persistent `UNOFFICIAL · FAN-MADE MOD` label
to the sole user-facing review. Internal source movies are silent and unbranded.

`create_player_motion(scene, assets, hero, idle_archive, duration,
grimhorn_position)` is callable from another Blender scene builder. After it
returns, `scene.frame_set(frame)` selects a precise posed source keyframe.
`animate_camera(scene, duration)` optionally applies the blocking camera rail.

## Evidence and cleanup

The manifest records the packed source scene, source manifest and original
asset hashes, executed source snapshot, native idle receipt, timing, all four
hand identities, per-frame gaze targets, source contact anchors, actual fingertip
positions and grip separation. Packed output retains the original live rigs,
textures, source topology and keyframes independently of the worktree.
The current world receipt referenced mutable worker Python files across several
stills. Its input receipt pins those references to byte-identical executed
captures, preserving every original source hash and a hash/link to the original
receipt. This keeps the clean packed world's original provenance valid when
the new motion driver changes. It does not replace source geometry or alter
the original world receipt.

The current 192-frame controller trace keeps the actual thumb/index midpoint
within `7.6e-7 m` of the moving contact anchor after closure. Grip separation
varies by less than `6e-9 m` while held. These values establish source/control
contact stability; they do not independently prove every skin surface touches
the figure or establish headset behavior.

After encoding, FFprobe must confirm the complete frame count. The script then
removes only its own encoding frames while preserving the movie, packed scene,
manifest, logs, selected QC captures and source hashes. Interrupted runs retain
evidence; reap their processes before removing only their owned unused encoding
cache. Python compilation, real Blender pose checks, numerical grip traces and
visual motion review are focused evidence, not a new runtime/headset gate.
