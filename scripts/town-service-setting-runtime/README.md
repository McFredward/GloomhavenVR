# Production station placement and lifecycle

The runner compiles the complete production `TownServicePlacement.cs`,
`TownServiceStation.cs`, and `TownServiceGrounding.cs`, not copied implementations
of their methods.

The geometry cases exercise triangle interpolation, reversed winding, terrain
height, the actual placed-room plane, unreadable meshes and MR/default fallback.
They also bind the production offsets to a reserved clearing envelope at 72 reading
yaws. The analytic radial bound covers intermediate yaws too; final asset meshes
and sampled actor poses must independently remain inside that reserved envelope.
The lifecycle cases additionally execute the real station constructor,
`RefreshEnvironment`, `SetVisibility`, and `Dispose`:

- a follower becomes author without changing room, centre or scale;
- a viewer changes environment while remaining a follower, then becomes author;
- an incoming peer scale updates the light range without writing a local pose;
- steady author/follower calls do not repeat pose or lighting updates;
- followers still advance asynchronous decoration;
- only renderers present at station construction receive its material property
  blocks; subsequently attached physical catalogue cards remain untouched;
- invalid interaction anchors are rejected before lighting/decor acquisition;
- owned lighting and decoration are released during disposal.

Thirteen compiled mutations recreate tracking-floor use, the unsafe outer station
ring, ignored relief, stale handover placement, stale peer light range, late card property-block corruption,
resource acquisition before anchor validation, retained follower grounding after
handover, lost authored sole correction, root-only terrain sampling, shifted
furniture tops, redundant steady-frame transform writes and support feet above the
lowest terrain. Each must fail at runtime.

Grounding cases execute the complete production helper against the real floor
interpolation method on an inclined mesh at scales 1 and 198, and multiple yaws.
They verify stable foot samples, actual support footprints, repeated-apply invariance,
authored actor offset preservation, fixed upper support edges, all three support
families, furniture-only workspaces and MR/default reset. These geometric assertions
use an explicit hierarchical Unity transform double; they do not render mesh pixels.

Unity object construction, renderer output, asset loading, lighting and decoration
are explicit dependency doubles. Pose-write counts and rendering requests are
observed; this harness cannot prove final pixels, shader illumination, real mesh
contact or actual resource release within the lighting/decor implementations.
The population harness separately checks election and caller intent; only this
station harness verifies the real station cache invalidation after that election.
