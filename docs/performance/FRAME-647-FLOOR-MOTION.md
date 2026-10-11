# Private room-floor groups follow board motion

**Historical647 experiment; withdrawn in648 after a measured Frame regression.**
The active renderer and asset bank are restored to646. New647 controls are INERT
saved-value storage. See [rollback review](../../.planning/FRAME-648-REVIEW.md). The following records647's experiment and proof limits.

The new broader floor groups are outside native Tile/scenario cloning roots.
Their old combined coordinates were tied to the private Core host, however, so
moving a Tile changed every source-to-chunk matrix. The group stopped acquiring
camera leases and remained on native draws until an unrelated rebuild event.
This was reproduced against the unchanged integrated source with actual Unity
Camera.Render; the render observer saw both leases initially and neither after
Tile translation. The original reproduction logged cumulative counters, not
per-camera deltas. Its source and failed result are retained.

Room-floor geometry is now combined in the captured native Tile's local frame.
Every camera compares each source's current local ancestor chain against its
captured relative matrix. The private chunk follows the Tile's current position,
rotation and positive uniform scale while staying under Core, outside every
native clone root. Board translation, rotation, recentering and uniform scaling
reuse the exact same combined Mesh. Native transforms, mesh references,
colliders, material slots and internal batching metadata are never written.

Positive uniform board/private-host ancestry is required. Nonuniform scale,
shear, negative ancestry and invalid frames retain native renderers. Individual
source motion, material replacements, property blocks, hides, current floor
endpoint changes and native renderer-write recovery still revoke the group.
The final managed FireOnPreCull boundary follows a later common board movement
before native culling; a newly unsupported frame releases its originals there.
This repair is automatic when the existing optional floor grouping is active;
there is no new setting or additional visual compromise.

## Bounded worker evidence

The worker starts at integrated dev8bf477053, with the primary agent's independent
binding-only e126a85e7 checkpoint inherited in this worktree. This lane changes
only ScenarioEnvironmentBudget, its motion fixture and causal controls, plus this
note. It does not change the frozen complete-gate tree or shared guard baselines.

- Raw unchanged-source reproduction:
  `.planning/debug/frame647-floor-motion-reproduce/run-36oa9jyh`.
- Final production:
  `.planning/debug/frame647-floor-motion-production-3/run-y_sf7el1`,
  **36,880 runtime assertions passed** with the real Unity 2021.3.5f1 llvmpipe
  renderer and pinned production HarmonyX camera boundary.
- Six new and four affected existing negative controls:
  `.planning/debug/frame647-floor-motion-controls-2/run-yq2nu5h5`,
  **10 negative variants passed** at their specified assertions. This selected
  run contains negative variants only; production passed separately above.
  The actual receipt directory is recorded in the command's log/manifest.
- Strict worker Debug passed with zero errors and warnings. Focused source checks
  passed frame-order (11 contracts), partial-order (49 types/300 parts) and
  instrument-writes (579 diagnostic fields/56 load-bearing, baseline unchanged).
  These three source suites are not a new complete source16 pass; the primary
  agent owns later help-table changes and final integration checks.
- The fixed-host reconstruction observes zero additional floor sources/groups
  after translation. The repaired ten supported board/host/source-restoration
  cases each observe two source leases and one group per camera. Every supported
  pose and late movement compares all actual pixels with the native draws and
  verifies the same prepared mesh object. Unsupported frames, individual source
  movement, material/block/visibility edits and a late native writer use native
  fallback. Actual scenario cloning contains only original unmasked floors.
- The new controls independently remove board following, restore the original
  world-matrix guard, ignore source-local movement, bypass safe frames, omit late
  pose following or omit late unsafe-frame restitution. Existing endpoint,
  block, cloning-root and legacy source-transform guards remain causal.

An initial worker compile failure and a repeated-camera fixture counter mistake
are retained as failed attempts. The latter was corrected to measure deltas of
PerfMonitor's cumulative counters; observed leases and pixel requirements were
not relaxed. These focused runs do not establish headset appearance, native game
artwork parity, Steam Frame frame time or a complete local gate. The primary
agent owns the full affected environment suite and final integration checks.
