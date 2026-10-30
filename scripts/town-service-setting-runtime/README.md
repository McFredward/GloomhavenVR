# Production station placement and lifecycle

The runner compiles the complete production `TownServiceLayout.cs`, `TownServicePlacement.cs`,
`TownServiceStation.cs`, and `TownServiceGrounding.cs`, not copied implementations
of their methods.

The geometry cases exercise triangle interpolation, reversed winding and the
read-only local terrain helper, including unreadable mesh fallback. Build746's
shared placement uses the common native furniture floor, with the common map seat
floor as fallback, independently of local room height, mesh readability or MR.
The premeasured furniture height is an explicit `GuildmasterRoomGeometry` boundary;
actual native furniture discovery remains covered by the real Unity workspace audit.
They also bind the production offsets to a reserved clearing envelope at 72 reading
yaws. The analytic radial bound covers intermediate yaws too; final asset meshes
and sampled actor poses must independently remain inside that reserved envelope.
The lifecycle cases additionally execute the real station constructor,
`RefreshEnvironment`, `SetVisibility`, and `Dispose`:

- a follower becomes author without changing room, centre or scale;
- a viewer changes environment while remaining a follower, then becomes author;
- an author changes local scenery without publishing another station pose;
- the current common native floor replaces an old peer's grounding on handover;
- native parchment yaw refreshes placement, while reading-side changes do not;
- an incoming peer scale updates the light range without writing a local pose;
- steady author/follower calls do not repeat pose or lighting updates;
- followers still advance asynchronous decoration;
- only renderers present at station construction receive its material property
  blocks; subsequently attached physical catalogue cards remain untouched;
- invalid interaction anchors are rejected before lighting/decor acquisition;
- owned lighting and decoration are released during disposal.

The optional distance-mesh helper is an isolated dependency double in this
placement/lifecycle fixture. It verifies actor-root admission, author/follower
ticks, release and rejection before resource acquisition. Actual mesh selection,
skinning and visible original NPC renders are covered by
`check-figure-distance-runtime.py`, which compiles the real helper.

All sixteen prior compiled controls remain, including legacy LOD and facial
failure admission. The historical `tracking floor regression` control now
discards the canonical native furniture floor in favor of the map seat floor.
The historical `ignored terrain relief` control directly breaks the retained
read-only mesh helper; local relief is deliberately no longer shared placement.
The other controls retain the unsafe outer ring, stale handover placement and
grounding, stale peer light range, late card property-block corruption, resource
acquisition before anchor validation, lost authored sole correction, root-only
contact sampling, shifted furniture tops, redundant steady-frame transform writes
and support feet above the lowest contact. Three further controls reintroduce a
local scenery floor, scenery-triggered pose publication and stale native parchment
yaw. Every control must compile and fail with its specific causal runtime assertion.

Grounding cases execute the complete production helper against a horizontal
canonical native plane at scales 1 and 198 and multiple yaws. A tilted station
frame keeps the two sole samples and support corners distinct, retaining the
root-only and minimum-footprint controls without importing local terrain.
They verify stable foot samples, actual support footprints, repeated-apply invariance,
authored actor offset preservation, fixed upper support edges, all three support
families and furniture-only workspaces. MR retains the same native plane; missing
native furniture resets contacts to the common map floor. These geometric assertions
use an explicit hierarchical Unity transform double and System.Numerics rotation
arithmetic. Quaternion Euler construction follows yaw/pitch/roll order, while the
`eulerAngles` boundary supplies only the consumed normalized yaw component. General
Euler decomposition, gimbal singularities and real Unity hierarchy behavior are
outside this portable proof. It does not render mesh pixels.

Unity object construction, renderer output, asset loading, lighting and decoration
are explicit dependency doubles. Pose-write counts and rendering requests are
observed; this harness cannot prove final pixels, shader illumination, real mesh
contact or actual resource release within the lighting/decor implementations.
The population harness separately checks election and caller intent; only this
station harness verifies the real station cache invalidation after that election.

Each invocation retains production/fixture input hashes, exact compiled sources,
build logs, raw runtime failures and per-control results under
`.planning/debug/town-service-setting/run-*`. Completion cleanup uses
`cleanup_completed_run`; only regenerable bin/obj caches are removed. Shared
compiler processes are disabled for these managed projects.
