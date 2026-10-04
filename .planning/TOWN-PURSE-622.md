# Temple purse geometry and motion, Build 622

The purse now uses its original mesh body for trigger contact, tracked pinch
placement, bowl release and seating. Fast hand-relative capture uses the same
world-scale holder as the approved remote hand renderer. It does not normalize
the observer's view or change the existing hand interpolation.

## Evidence and causes

The immutable input is the main checkout's `.planning/debug/npc622/inputs.json`.
Both players ran Build 620, commit `46b5c5da4`; the host Player log is Debug and
the remote logs are Info. All nine Android screenshots were inspected.
`VirtualDesktop.Android-20261004-211958.jpg` shows an oversized remote held purse.
Six host purse releases report `bowl=False` despite valid pose/tracking/trigger
state. These establish symptoms, not a measured headset scale or a complete
causal explanation.

The source provides three independent geometric causes:

- `CaptureMotion` previously sampled against local `hand.Rig.Root`. That root
  includes visual hand-style scale. The remote smoothed holder carries
  `WorldScale`; its style lives on a separate visual child. Plate/Arcane style
  scale `.62` therefore multiplied the remote size and offset by `1/.62`.
  `ReadMotionHandPose` now samples position, rotation and world size in that
  remote holder's exact basis. The same rule covers an enclosing hand canvas.
- A purse token owns the labelled `Piece.Root`. The actual normalized `Body` is
  its child at `y=-.065`. The old contact rectangle covered inscriptions, and
  fixed root-at-bottom grip/drop arithmetic sampled the wrong origin. The token
  now caches original mesh bounds in its owned root's coordinates. Contact fits
  the body; the pinch meets its neck; release tests its visible midpoint.
- The former `Body.parent == DropFrame` seating branch could never run, since
  `Body` stays under the labelled root. Accepted release now aligns the original
  body's bottom with `TownServiceTempleBowl.PurseSeat` once, before the existing
  native confirmation and sink clock.

## Native provenance and fixture limits

`scripts/town-purse-runtime/export-native.py` reads the actual Treasure.Bay purse
from the immutable PCG bundle, never a cube approximation. The selected object
is `CR_ST_Shelf_KitchenItems_Bag_01 (3)`, path ID `2243915112399515331`; its mesh
path ID is `4229546086787866523` (386 vertices, 342 triangles).
The bundle SHA-256 is
`8572412873a2585f8edfff90502498c21961114d5447374e1e52ef11e6b50093`.
Each run retains the export and its provenance/hash record. `NativePurse.cs`
reconstructs that renderer data and the production normalization/holder/body
hierarchy. The fixture uses a stock Standard material; it proves geometry and
retained renderer tables, not original shader/artwork loading or headset pixels.

The complete normalized native bounds have maximum extent `.125 m`. This is
the authored template normalization, not the former comment's assumed `.15 m`
height. The interaction matrix covers map scales `.1`, `1`, and `198.12` and
controller pitch/roll. It checks every original vertex against the body collider,
actual neck against the tracked pinch, body midpoint against the drop callback,
and body bottom against the actual bowl seat.

The motion matrix crosses those world scales with style scales `.62`, `1.12`,
and `1.7`. Each fresh service lifetime delivers a hidden original baseline,
reveals through the independent numeric lane, grabs, follows the approved
remote hand between arrivals, and returns while preserving native body size.
This is warm baseline/reveal evidence. Cold preparation and earliest body
publication are integrated separately in `NativeTemplates`/`TownServiceSync`;
this worker's matrix must not be cited as proof of cold first appearance.

## Focused validation

- `check-town-service-interaction.py`: 7,767 runtime assertions and five causal
  controls (`purse-visible-body`, `purse-depth`, `purse-label-pick`,
  `purse-double-scale`, `purse-labelled-root-seat`), all passed in
  `purse622/interaction/run-e_nc909y`.
- `check-town-service-mirror.py --suite motion-fast`: 767 production assertions
  and all five controls passed in `purse622/motion-verified/run-b2j9r8nc`,
  including the former `.62` hand-style basis and render-cadence failures.
- `check-town-visitor-motion.py`: 39 assertions and three motion controls,
  passed in `town-visitor-motion/run-eznymdk7`.
- `check-town-ritual-transactions.py --no-negative-controls`: 247 runtime
  assertions plus the two existing temple-guide source controls, passed in
  `purse622/ritual/run-9deezal7`. This is positive focused evidence, not the full
  transaction mutation gate.
- `check-town-motion-wire.py`: 14,900 assertions passed in
  `town-motion-wire/run-mj1k2c6r`.
- `scripts/build.sh Release`: zero warnings and errors; `git diff --check` and
  Python compilation passed.

All paths above are below the worker's gitignored `.planning/debug/`. Early
failures are retained: ambiguous mutation binding, an incorrect fixture yaw
expectation, a missing boundary `VRLog.Error` method, and a timing-sensitive
generic artwork assertion. The latter allowed the real `.75 s` artwork
heartbeat to expire during software rendering. An ordinary artwork capture
before the synchronous numeric mutation now isolates that assertion without
resetting or bypassing the fast lane's cadence. A later combined control run
passed production, then Unity crashed natively with SIGSEGV/exit 139 before
controls; it is retained as failed execution, never counted as a gate pass.
Focused controls were rerun independently.

`check-town-service-sync.py` hit the pre-existing merchant face-gaze/speech
source-contract failure (`TownVoiceScheduleCases.cs:48`); it was reported to the
integrator and is outside this worker's changes. The complete final-tree gate
belongs to integration. No green automated result establishes the headset
picture; Build 622 hardware appearance and perceived pinch comfort remain
unverified.

The user's Build 622 clarification makes only the temple donation ghost/overlay
visitor-local. The original wrist/held purse, payment animation and audio remain
shared. The integrator removes guide publication and records the exception in
the repository contract.
