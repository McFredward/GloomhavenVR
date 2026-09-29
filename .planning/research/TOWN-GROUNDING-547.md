# Authored furniture grounding (build 547)

The initial sculpted furniture export joined every object by material. The old
runtime recognized only primitive plinth/leg names, so it sampled no supports and
could not compensate for the actual room floor. Transform mirrors also cannot
reproduce private vertex deformation without adding a separate wire contract.

The authoring export now retains only real floor-contacting parts as separate
`GroundSupportNN_<original name>_<material>` objects. Other decoration still joins
by material. Production grounding reads their actual mesh bounds, finds the FBX
axis that is vertical, and stretches that axis while retaining the exact authored
top vertex in world space. Tables, shelves, card surfaces and the actor seat do
not move. Supports use a common lowest floor sample, matching the existing
published resident FurnitureBottom contract. Extra workspaces and return tables
publish their ordinary local transforms through the existing mirror binding.
No runtime mesh copy, custom mesh packet or viewer-local terrain decision is used.

Return tables resolve their own footprint at construction and whenever their
pose, scale or room changes. Repeated unchanged frames do not resample the floor.
A full grounding resolve reads the original floor arrays once, avoiding one large
mesh-array allocation per support corner. Disposal restores authored support and
actor transforms before their host is destroyed. Unreadable support data produces
a bounded construction warning and does not throw into native map flow.

## Validation

The real Unity workspace fixture uses the final Windows town bundle SHA256
`51791c7d197e6a08526ea21c5e104bafc0c347f93dce60385f16ff589ce71bed`.
Its sloped floor now covers +/-10 m, including the new visitor radius 5.8 m, with
height `.1 + .02*x + .01*z`. The previous +/-5 m fixture incorrectly fell off its
own mesh at the new layout positions.

- Production workspace fixture: 2,557 real Unity assertions, including support
  bottoms on actual imported meshes, fixed top anchors, owner/remote transform
  parity, untouched non-support geometry, exact teardown restoration and return
  table terrain contact. Existing workspace negative controls remain enabled.
- The new removed-grounding negative control is detected by return-table contact.
- Portable setting fixture: 1,569 placement, 71 station lifecycle and 243 grounding
  assertions; all 14 compiled negative controls pass. Its analytic triangle was
  extended to cover the new layout at the largest tested scale. Lightweight Unity
  API boundaries now expose mesh bounds, hierarchical transforms and a unit support
  mesh; they still execute production grounding/placement code.
- The actual mesh workspace test found and prevented a Unity fake-null regression:
  floor sampling uses an explicit Unity-null check for missing MeshFilter components.

Reproduce with `python3 scripts/check-town-service-setting.py` and
`python3 scripts/check-town-service-workspace.py --bundle <final ghvr-town.bundle>`.
A selected negative control can be rerun with `--negative-control ground-support`;
the production run is always included. Geometry proofs do not establish headset
appearance or multiplayer network delivery; hardware observation remains required.
