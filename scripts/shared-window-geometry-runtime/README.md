# Shared-window Unity geometry proof

`python3 scripts/check-shared-window-geometry-runtime.py` compiles the original
shared window size resolver, grab Build/Update/LateUpdate writers, host-gaze spawn,
first-reveal bridge, reward initial-pose cache, video initial-spawn kernel and
PanelPlacement spawn/facing kernels into a Unity2021.3.5f1 fixture. Actual Unity
transforms, camera forward vectors, RectTransform corners and canvas scales execute.
Native session/roster discovery, window identities and original Hex asset lookup
are explicit inputs at the fixture boundary.

The distance regression compares absolute world positions and head-to-window
distances at fixed head/gaze across host zoom0.25..900. It never divides these
measurements by host zoom. All six shared kinds are exercised: the five modal
kinds use their production shared host spawn, and movie placement executes its
actual dedicated initial-spawn method and the actual PanelPlacement methods.
Both map and native Hex render domains are included. Existing windows keep their
captured reference when the ambient map reference changes. Final-fit replay also
keeps the original opening head and repairs earlier private replay anchors whose
raw position/scale were derived from private zoom. Initial conversion before a
grab exists uses the common scene reference. Private movie placement retains its
existing player-scale behavior.

The board floor and downward/straight-down gaze cases execute the actual
ClampSpawnPose, SeatCentreAtEyeLevel, HeightDecision and TryGetBoardPlaneY methods.
The table focus and presence are fixture inputs; there is no replacement clamp.
The reading-distance constant is extracted from ModalFallback.1.Core.cs, currently
1.40, rather than copied into a test constant. The real replay caller's captured
reference/half-size wiring and the reward clamp's captured half-size wiring are
source-pinned. Their full native conversion lifecycle is not modeled here.

Production sources are hashed and checked for changes during each run. Each
negative control must compile and fail its named behavioral assertion; compile
errors are failures of the proof. Controls restore private zoom, ambient reference,
stale private replay placement or shortened downward-gaze placement. Existing
controls retain source reference, renderer/collider fallback, synchronized manual
resize/drag, first-reveal adoption and private-window coverage.

The portable `shared-window-pose-runtime` suite separately owns actual samplers,
record119/121 codecs, initial/live peer adoption, movement election and the native
opening ledger. These two proofs complement each other; neither establishes a
headset picture or complete native multiplayer progression. Generated logs,
source snapshots, hashes and results are retained; the canonical completion API
removes regenerable build/Unity caches after processes stop.
