# Production station placement and lifecycle

The runner compiles the complete production `TownServicePlacement.cs` and
`TownServiceStation.cs`, not a copied implementation of their methods.

The geometry cases exercise triangle interpolation, reversed winding, terrain
height, the actual placed-room plane, unreadable meshes and MR/default fallback.
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

Six compiled mutations recreate tracking-floor use, ignored relief, stale
handover placement, stale peer light range, late card property-block corruption,
and resource acquisition before anchor validation. Each must fail at runtime.

Unity object construction, renderer output, asset loading, lighting and decoration
are explicit dependency doubles. Pose-write counts and rendering requests are
observed; this harness cannot prove final pixels, shader illumination, real mesh
contact or actual resource release within the lighting/decor implementations.
The population harness separately checks election and caller intent; only this
station harness verifies the real station cache invalidation after that election.
