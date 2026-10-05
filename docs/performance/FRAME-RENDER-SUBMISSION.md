# Private environment render submission

This worker starts at `01503d4c` (`dev`, Build625) and carries preparation commits
for the earlier camera-local material reads and independent shared-binary config
contracts. It does not integrate or push. The maintainer explicitly requested a
reviewable worker delivery before handover, and later authorized configurable 3D
compromises, with no 2.5D board and no resolution-default change.

## Historical failure is a contract

Read `.planning/static-batching-removed.md`, historical `8e6f8e8`, and the later
hardware ruling before changing this code. Unity's internal static batching
changed the in-scene source metadata/material slots. Apparance subsequently
instantiated those sources with empty material arrays and no usable batch state;
revealed rooms stayed invisible. Re-enabling that mechanism is prohibited.

This implementation never invokes Unity static batching, modifies native mesh or
batch metadata, disables native objects, or changes collision/gameplay. Existing
optional simple material handling retains its prior reversible array ownership.
New geometry comes from private immutable render copies. Temporary source draw
masks end after each camera, on interruption, before native renderer writes, and
before `ApparanceEntity.CreateInstance` actually clones a source. Unknown or
unsupported sources keep native rendering immediately.

## Independently configurable paths

- `ScenarioEnvironmentMeshBank` allows existing bounded exact chunks to read
  verified private copies when native imported meshes are unreadable. Off keeps
  the old readable-original-only path; it does not remove terrain's independently
  selected detail assets.
- `ScenarioExplicitEnvironmentInstancing` groups repeated eligible original
  meshes/material slots by native tile/cell (four native world units), at most
  24 members. A source belongs to at most one combined or instanced substitute.
- `SharedEnvironmentMaterialReads` shares a complete original-material verdict
  inside one synchronous camera callback only. Off executes the earlier complete
  per-surface validation. MPBs and later camera/eye changes always remain live.

The same runtime and assets are used on PC and Frame. Profile detection only
seeds unsaved defaults; lifecycle/config/UI/packaging are the integrator's lane.

Explicit draws use private, reusable, camera-bound `CommandBuffer` objects.
Queued `Graphics.DrawMeshInstanced` would survive a later native pre-cull write;
clearing/removing a command buffer can revoke the obsolete draw before restoring
originals. This is deliberately conservative: forward cameras without native
command-buffer consumers, depth-texture consumers, lightmaps, LOD groups, MPBs,
shadow casting/receiving, local probes, overlapping group bounds, negative transforms, or unsupported render
flags are eligible. All other paths retain originals. Imported shaders must
already advertise native instancing, or use the known instanced simple shader.

`Camera.GetCommandBuffers` returns new managed wrappers on Unity2021.3.5. The
consumer query compares exact nonzero native command-buffer identities against
registered active private buffers, never names or counts alone. An unknown API
layout fails open. Every foreign/native buffer, including another event, remains
a native-renderer consumer. Private buffers detach after each render, remain
bounded to eight camera identities per group, and release when the group retires.

Prepared-group debug lines do not prove applied rendering. With performance
attribution enabled, the existing bounded counter stream additionally reports
`Environment.RenderCameras`, `Environment.ChunkSources`,
`Environment.ChunkGroups`, `Environment.InstanceSources`, and
`Environment.InstanceGroups`. They sample actual surviving masks at camera
completion before restitution; late revoked draws contribute zero source savings.
`Environment.NativeBufferFallback` counts camera invocations which retained
originals because a foreign/native command buffer consumed their identities.
Counters sum camera invocations, including both MultiPass eyes and nested cameras;
they are not unique scene renderer totals or measured GPU draw calls. Zero entries
are omitted by the existing counter stream. No per-frame information log or scene
census is added, and mask-count traversal is skipped when attribution is Off.

## Mesh bank and source provenance

The offline tool reads MeshFilter originals from original PCG database bundles;
`ressources/`, `libs/`, and `decompiled/` are never modified. The manifest rejects
ambiguous metadata identities with different exact geometry. Runtime admission
checks authored name, readability, vertex/submesh/index counts and all six exact
bound values. It also verifies a recorded original bundle's SHA256 against the
actual game's StreamingAssets and SHA256 of each prepared private geometry asset.
A native metadata change is checked even after a mesh has been cached. Geometry
is bounded and decoded without native vertex readback or online simplification.

The process retains shared immutable meshes for extant private proxies. Native
colliders and native material slots never use these geometry assets.

Generated receipts: **1,139 exact originals, 3,170 total assets, six ambiguous
identities rejected, 167,299,844 uncompressed geometry bytes**. Only currently
requested assets are loaded/decoded. A separate `ghvr-environment.bundle` is the
intended package to keep the primary bundle below its hosting limit.

Prepared 50 and 0 tiers use offline boundary-preserving vertex clustering. Every
original open boundary position stays fixed. All original vertex indices and
attribute channels remain present; positions move to bounded cluster centres,
and only collapsed triangles are removed. Material submeshes and original bounds
remain. Terrain can morph using original triangles until the endpoint; no runtime
mapping, game-source mutation or 2.5D approximation is required. No useful reduction
means the exact tier remains available. Across admitted generated reductions:
50 tiers retain 459,403 of 930,807 original triangles; strongest tiers retain
228,897 of 951,106. These are asset counts, not measured scene/FPS savings.

For terrain's final 15-name wall/pillar allowlist, 12 verified asset definitions are
available: 9,116 original triangles, 3,486 at tier50 and 1,186 at the strongest tier.
The three absent thin-wall identities remain native. These totals count model
definitions once each, not instances in the tested scenario.

Generation:

```bash
python3 scripts/generate-environment-meshes.py
```

`--only` writes a private proof bank; it cannot overwrite the complete tracked
package. Input hashes, ambiguity exclusions, deterministic importer GUIDs, binary
Git attributes and detailed triangle receipts are generated together.

## Integration contracts

Wire `ConfigureTerrainIntegration(queue, ready, beforeWrite, beforeContent, owns)`
to the terrain lane. Terrain has priority when it wants a tracked surface;
environment grouping and each real pre-cull validation reject that source.
`OwnsRenderSubstitute(Renderer)` exposes this lane's registry to other independent
consumers. `HasNativeCommandBufferConsumers(Camera)` is the shared conservative
consumer query; pass it to terrain/idle so a known mod buffer does not veto their
unrelated presentation.

Configure `ConfigureAssetPreparation(Func<bool>)` with the shared loader and load
`ghvr-environment.bundle` before optional scenario preparation. `IsReady` warms
the index and `IsUnavailable` reports corrupt-index/loader-exception terminal
states. The parent loader additionally reports terminal missing-file failure;
terrain discovery must settle to native originals rather than remain preparing
forever. `ConfigureBeforeNativeContentChange(Action)` releases the idle lane's
independent masks in addition to terrain and environment before source cloning.

Load `ghvr-environment.bundle` before optional scenario preparation. It contains
`Assets/Bundle/EnvironmentMeshes/index.json`, the generated `.bytes` TextAssets,
and the terrain lane's cheap shader. The runtime looks up those exact asset paths
in loaded banks. No new network records, multiplayer cadence, secrecy rules,
scenario activation, or game-data writes are involved.

## Validation boundaries

Focused runtime validation uses the complete production budget, bank metadata,
SHA verification and decoder, actual Unity geometry, camera rendering, native
cloning and cancellable instanced command buffers. Original native geometry is
extracted losslessly from read-only bundles. The fixture's native scene classes,
configuration, material shader artwork and controllers are explicit surrogates.

Two freshly generated fixture banks using `ChunkBasedCompression` failed their
immediate load with "not compatible with this newer version" despite format7,
Unity2021.3.5f1 and serialized format22 (Linux and Windows targets). Those retained
receipts do not establish a general Linux/Windows incompatibility. The integrator
independently built the actual `ghvr-environment.bundle` using the exact editor,
Windows target and `BuildAssetBundleOptions.None`; an empty exact Unity Linux
editor loaded all 3,170 geometry TextAssets, the index and cheap shader. That
separate production package/load receipt is in the integrator's
`.planning/debug/frame-packaged-environment-load/load.log`.

This behavioral fixture still binds only asset lookup to actual imported Unity
TextAssets and Application's StreamingAssets location to the original read-only
game directory; all other production bank code executes unchanged. Original
native material lighting, HMD images, Frame FPS and multiplayer headroom remain
hardware-open. Counts and final focused evidence follow in the
worker's delivery message and gitignored validation ledger. The complete focused
suite passed **11,226 production assertions plus all 52 causal negative controls**
(53 variants total). It proves real clone-only pixels between cameras, inside
pre-cull and after nested-camera source restitution, and same-frame original
fallback after newly overlapping instance bounds. The strict Release build passed
with zero warnings/errors. The structural-instancing suite and four focused
source checks also passed; the integrator still owns the complete final-tree gate.
The subsequent applied-counter change passed the affected production fixture
with **11,230 assertions**; the final complete-tree gate includes it.
