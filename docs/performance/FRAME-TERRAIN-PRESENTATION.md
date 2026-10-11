# Configurable private 3D terrain presentation

The maintainer's October 5, 2026 direction authorizes stronger 3D representation
compromises, individually adjustable in the same PC/Steam Frame binary. A 2.5D
board is explicitly excluded. Resolution remains the maintainer's own next-test
comparison and is not changed by this lane.

The initial lane started at `01503d4c` / Build625; the steady-state extension
starts at `a517e0090` / Build627 (October 6, 2026). The shared configuration contract was
prepared separately (`c101027f`, cherry-picked locally as `4585c163`). Its config,
defaults, localization, lifecycle and asset-builder wiring belong to the parent
integrator; only this lane's subsequent owned files are delivered.

## What changes

`ScenarioCheapWallShading` removes optional normal/MRAO/detail/reflection texture
work on verified static wall/masonry surfaces. It retains the original albedo,
tint, UV/world-projection inputs, native dimming inputs and original native wall
clip channels. Every renderer-wide and material-index MPB remains live. Unknown
shader families, vertex animation, emissive effects, actors, doors, interactable
obstacles, lights and held/grabbable props retain their native rendering.
Live vertex/emissive MPB gates retain originals even if the underlying material's
own gate is disabled.

`ScenarioTerrainDetailPercent` selects a prepared three-dimensional original-mesh
derivative. `100` means the native original; lower percentages choose the prepared
bank's coarser tiers, including its strongest available 3D reduction at zero.
`ScenarioDistantTerrainDetailPercent` independently caps that detail beyond
`ScenarioTerrainDistanceMeters`, measured from the closest renderer bounds point
in actual VR metres. A small hysteresis prevents repeated transitions at a parked
threshold. Leaning within 18 cm or touching within 12 cm with either represented
tracked side restores the exact original geometry.

All floors are excluded from this owner. Their original meshes, materials and
never-fade behavior remain available to the separate exact environment submission
lane. This disjoint ownership is deliberate: enabling cheap wall shading must not
silently cancel the new floor chunks/instancing. Revealed rooms retain every
native floor and tactical object; there is no room deactivation, planar board,
new scenery, MR backing, collider replacement or gameplay-state write.

Admission requires a live `ProceduralWall` ancestor, `Generated Content`, native
scenario ownership, immutable mesh-bank provenance and one of the twenty-five audited
crypt/cave/city wall-body or pillar identities listed in `StructuralIdentity`.
`Floor` anywhere in a mesh name is vetoed, including `EN_CR_FloorHex_Edge_Even2`.
Map-tile ownership alone, foundations/under-wall slabs, top caps, anonymous meshes
and other bank members never authorize simplification. The October 6 extension adds exactly ten CR_INT wall/pillar definitions present
in the three-room Gaming-PC capture, independently byte-verified against the
original `pcg_city.asset.bundle` (SHA-256
`518fc10dce054b1e4d342dbe9f521bac7959b2a5d32d77287b2aaa1a965ee4a0`).
Original prefab references show the same meshes inside under-wall and doorway
templates too, so live under-wall/foundation/slab/top-cap/doorway/entrance/exit
ancestor boundaries retain native rendering regardless of matching mesh identity.
Shelves, candles, tapestries, bases and arbitrary CR_INT names are excluded.
This intentionally keeps unclassified terrain native until its source is reviewed.

## Why the missing-room batching defect cannot be recreated here

The history in [static-batching-removed.md](../../.planning/static-batching-removed.md)
records the actual failure: Apparance cloned scene sources which had acquired
Unity's internal static-batch state, producing children with empty material
arrays. This lane never calls the internal batch APIs, writes a native
`MeshFilter.sharedMesh` or `Renderer.sharedMaterials`, or places a proxy under a
native cloning root.

Each substitute is an inert private `MeshRenderer` under the mod host. It copies
the exact current native matrix and renderer flags; an unsupported shear or
nonidentity host transform fails open to the original. The proxy is enabled only
inside one paired real head-camera rendering invocation. The source's
`forceRenderingOff` is restored after rendering, on disable, scene unload,
teardown, failure and immediately before a native renderer write. Native source
pooling, room reveal, visibility, material edits and mesh replacement remain
authoritative. Foreign masks are retained.

The pre-cull guard requires the driver and its host to be active. Static camera
callbacks continue after `host.SetActive(false)`; they must never mask originals
while the host's private proxies are inactive. Actual camera pixels and live
lease observations cover that disable/reactivate boundary.

Unlike a queued `Graphics.DrawMesh`, a private renderer can be disabled
synchronously when a later pre-cull writer changes native presentation. It has
no controllers, colliders, callbacks, gameplay or native child content to run.

## Continuous geometry and native dissolve

Prepared coarse tiers preserve original vertex indexing and channels. Vertex
clustering moves vertices to retained original positions and removes only
triangles which are degenerate/duplicate at the endpoint. During a 0.35-second
transition the private mesh keeps exact original topology and morphs continuously
between the current and requested vertex positions. At the endpoint, dropping
already-degenerate triangles cannot introduce a silhouette pop. Reverse and
interrupted transitions begin at the actual displayed private geometry.

The visual step is bounded to 1/30 second per rendered update, so a long hitch
cannot skip every intermediate shape. Native shaders remain in use when cheap
wall shading is disabled; the geometry control is independent. Floors do not
participate in this transition.

The bank generator's `tools/environment-mesh/manifest.json` detail receipts
originally covered twelve prepared asset definitions among fifteen admitted identities.
Their aggregate original/50/0 triangle counts are 9,116/3,486/1,186 (38.2% and
13.0% of original). For example, `EN_CR_Pillar_Thin` is 2,040/362/108;
`EN_CR_Wall_Basic_Tall` is 116/116/60, so its unchanged 50 tier retains the
native renderer. The three absent thin-wall identities retain their originals.
The ten newly audited CR_INT definitions add 3,472/2,088/1,180 triangles at
100/50/0, growing bank-covered definitions from twelve to twenty-two without
regenerating any asset. They are present by name in the Build627 log, but that
log does not provide a distinct live eligible-renderer count.

These are prepared asset-definition totals, not scenario instance counts,
vertex-memory savings or measured frame-time gains. Original vertex indexing
and channel counts remain intact to support the continuous morph.

The cheap shader retains the original LOW object's 0.4 foundation band and the
HIGH/N_MRAO map-depth, enable, cutoff, foundation, screen vignette and simplex
branches. Native noise-map textures keep their delivered bilinear sampling;
there is no square/rank/binary transition bank. The HIGH simplex reconstruction
uses the original `6/7/10` world scales, `.02/-.04/.006` time drift, permutation
and gradient constants and `42` contribution. Native global camera uniforms are
not material Properties: defaults must not hide the game's global map/enable.
GL clip depth is normalized for the graphics fixture; original D3D clip depth is
unchanged.

The source evidence is the same original game shader bundle used in the earlier
Frame619 review:

- Bundle SHA-256: `9b01a64659be31bffe4d3ed7c803b4081f901a0c40e578af9f9f64a2f1fad9c7`.
- `Amp_Basic_WallFade`, original DX11 fragment blob216:
  `4d6ae64bfd34ab96f4234f66993e231262cb2359c005b08ef854927897a6bc0b`.
- Disassembly SHA-256:
  `893ad75c5d2d02acac71fa0c03cca8fea930a50e303b40de7689985403954326`.
- Twenty literal noise samples are derived by a separate scalar float32
  evaluation of that original instruction stream, not from the new shader.

Omitted surface-lighting work is an authorized visual compromise. This source
review and fixture do not establish the final native game/HMD picture.

## Settled-board work and coverage diagnostics

Only source preparation changes during loading; the optimization target is the
fully loaded board. Unsupported floor/mesh identities are refused before any
bank decode. Native renderer flags, material routes and renderer-wide/per-slot
MPBs remain live per camera. Shared material route reads are reused only inside
that camera. Identical private transform, renderer-state, mesh and material-array
writes are skipped; native matrix/host changes still immediately rebuild and
validate the private pose. No native transform/material/filter is changed.

The original triangle/cheap counter tokens now report only leases surviving
to their paired PostRender callback after late native-write revocation. They
do not count frustum/occlusion results or GPU executions. Bounded Debug
`Scenario terrain coverage` summaries report current prepared membership and
distinct discovery visits/refusals, including zero membership, after preparation
and live option changes. Refusal counts describe discovery since reseeding,
not an instantaneous renderer census. Ordinary useful fallback/build context
and the existing `Scenario terrain budget` token remain available.

## Integration contract

Configure providers before installing this owner:

```csharp
ScenarioTerrainBudget.ConfigureMeshBank(
    ScenarioEnvironmentMeshBank.IsTerrainEligible,
    ScenarioEnvironmentMeshBank.TryGetDetail);
ScenarioTerrainBudget.ConfigureNativeCameraConsumers(
    ScenarioEnvironmentBudget.HasNativeCommandBufferConsumers);
ScenarioTerrainBudget.ConfigureCanonicalMaterial(
    ScenarioEnvironmentBudget.CanonicalMaterial);
ScenarioTerrainBudget.ConfigureAssetPreparation(
    () => ScenarioEnvironmentMeshBank.IsReady,
    () => ScenarioEnvironmentMeshBank.IsUnavailable || ScenarioEnvironmentAssets.IsUnavailable);
ScenarioTerrainBudget.Install(host);
```

Install terrain before environment camera callbacks. Environment grouping and
pre-cull validation must exclude `ScenarioTerrainBudget.OwnsRenderSubstitute(r)`.
Terrain has priority only for tracked non-floor surfaces currently requesting
cheap shading or private geometry. It never waits for an environment lease, so
the two owners cannot mutually block each other. Its native-camera query excludes
only the exact known environment command-buffer instance; actual native
`DrawRenderer` consumers retain original source identity and geometry.

Forward existing placement/material-readiness/native-write/native-content hooks
to `Placed` or `QueueRoot`, `MaterialReady`, `BeforeNativeRendererWrite` and
`BeforeNativeContentChange`, respectively. Invoke `Shutdown` during teardown.
Scene callbacks and registry seeding cover already-existing tiles/walls;
placement hooks cover later Apparance output and reveal/pooling.

Temporary asset preparation retains the bounded discovery queue while original
rendering continues. A terminal missing/incompatible bank clears that queue and
reports a bounded ordinary-level fallback; it never holds a native spinner or
continuation. A later ready provider reseeds originals. A tier with no triangle
reduction remains on the original native renderer instead of adding an identical
private draw.

Include `Environments/ScenarioCheapTerrain.shader` in the same Windows bundle
as the prepared original mesh bank. The bundle must be generated with Unity
2021.3.5f1 and validated by the parent integrator. This lane does not edit shared
builders, build number, network data, integration branch or release assets.

## Validation and next hardware evidence

The focused checker is `scripts/check-terrain-budget-runtime.py`; the fixture
README records its native boundaries and causal controls. It executes actual
Unity renderer leases, native cloning, shader pixels, three-dimensional morphs,
independent near/far detail controls, hand fallback and foreign-state retention.
Native original controllers, original artwork, Windows shader bytecode execution,
HMD images, network behavior and FPS remain outside this fixture.

The parent must register the suite and run the complete final integration gate.
The next hardware run must compare each switch independently, then the combined
Frame defaults with all three rooms and two/four players as host and client.
Check every floor/room, wall fade in both directions, doors, obstacles, targeting,
native shadows and lighting, hand proximity, settings reversal and MP animations.
Keep mean/p95 app time separate from unknown GPU/wait time; a passing source or
graphics fixture does not demonstrate fluid headset rendering.
