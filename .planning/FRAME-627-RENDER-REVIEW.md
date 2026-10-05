# Frame627 environment and terrain source review

Worker base: `19256ed12`. The candidate is reviewed against the original read-only
`ressources/GH_Data` game sources; no NPC or Quest standalone work is included.
This document records source and native-engine evidence, not headset FPS.

## Proven fixes

- Exact chunks and explicit instance submissions now retain native renderers with
  supplementary vertex streams or existing Unity internal static-batch geometry.
  Admission, queued chunk publication and per-camera validation all apply the
  source-geometry gate. Late supplementary streams revoke a prepared substitute.
  No native mesh, collision, material slot or batch metadata is rewritten.
- Exact chunks retain separate original draws when their renderer asks for
  per-object light/reflection probes. Copying flags to a combined renderer is not
  sufficient: its aggregate bounds can sample different lighting. The actual
  simplified shader uses `ShadeSH9`, so selecting it cannot bypass that safeguard.
  Material simplification remains independent of combining those draws. Material
  and probe admission precede readable/bank mesh preparation, so rejected unreadable
  surfaces do not hash original provenance or decode private geometry needlessly.
- Terrain proxies now copy the live `renderingLayerMask` for every camera, along
  with the already-preserved sorting, lightmaps, shadows and probe flags.
- Original `MaterialLoaderData.LoadMaterials` hides its renderer before the
  asynchronous completion callback. Its new prefix restores shared idle,
  environment and terrain draw leases plus original source material references
  before that hide; completion performs the same shared lease recovery before
  its native material/enabled writes. A late pre-cull load cannot leave stale
  combined or queued instanced geometry in the frame.
- The environment runner snapshots and hashes its fixture before compiling any
  variants. Previously an edit during compilation could bind later assemblies to
  different test bodies. Source mutations still compile independently and must
  fail the specifically named assertion.

## Read-only source and asset evidence

The original `ApparanceEntity.CreateInstance` in `Apparance.Unity.dll` calls
`Object.Instantiate(template, position, rotation, parent)` before applying scale.
The existing restoration prefix therefore precedes actual native source cloning.
`ProceduralMapTile.ShowContent` toggles native generated/preview child visibility;
its existing prefix restitutes camera leases before those changes. Original
`MaterialLoaderData.CheckAllMaterialLoaded` replaces `sharedMaterials` then enables
the renderer; its start method disables the renderer before addressable requests.
All three bodies were decompiled read-only into the local evidence directory.

The source bank remains immutable and SHA-bound. Exact metadata admission rejects
unknown/ambiguous meshes, changed bounds and invalid geometry/provenance. Prepared
private geometry uses original channel/index correspondence, and terrain morphing
keeps original topology until its endpoint. Floors, native colliders, actors,
doors, held/interactable objects and UI retain their native ownership. Source
property blocks, live wall channels and foreign command-buffer consumers retain
native rendering; buffer ownership compares actual native handles, not names.

An offline Crypt bundle census finds 1,132 of 1,134 renderers requesting native
light probes and all 1,134 requesting reflection probes. Those flags are largely
authored defaults: a census of all 13 native levels, sharedassets and global
files finds no `ReflectionProbe` objects. Levels 6/7/8 refer to the sole
`LightProbes` object, sharedassets6 path 141, whose baked-coefficient and baked
occlusion arrays are empty; other levels have null probe references. This does
not establish live absence after procedural generation or other plugins run.
The conservative flag gate can therefore refuse many otherwise eligible chunks;
no real-game draw-call saving is claimed for refused groups. An absence-aware
extension would need proof of live common/absent inputs at each camera, without
reintroducing per-object lighting or source changes.

## Executed focused evidence

All renderer objects, mesh channels, masks, command buffers, cloning, camera
callbacks and sampled pixels below execute in Unity 2021.3.5f1 on GL llvmpipe.
Native scene/controllers/config/addressable requests are documented boundaries.
Original Windows shader bytecode and headset images do not execute here.

- Terrain original scope: 103 production assertions and 31 required controls
  (32 variants), `terrain/run-nl686wob`.
- Terrain rendering-layer follow-up: 105 assertions and one new control,
  `layers/run-bfups_b9`. Only this affected follow-up was repeated.
- Environment supplementary/probe follow-up: 11,251 assertions and three new
  controls, `environment-reviewed/run-pasc42du`.
- Simplified-probe guard plus native loader registration and idle-seam controls
  fail at their intended assertions in `native-load-final/run-6g79afwm`.
  That run's renderer-specific restoration control initially escaped because
  global mask recovery already restored geometry; the assertion was strengthened
  to require original material-array ownership too.
- Final native loader restoration follow-up: 11,265 assertions and its corrected
  precise control, `load-restoration-final/run-lfcpll_i`.
- Early unreadable probe-refusal follow-up: 11,271 production assertions and its
  precise eager-bank-access control, `early-probe-refusal/run-6a3ktir0`. The source
  binder counts only the complete original `TryGetExact` entry; rejected native
  floors make zero requests, probe-free floors make two requests and form their
  real chunk, and camera pixels/masks retain the original refused picture.
- Final bounded refusal-summary follow-up: 11,266 production assertions,
  `refusal-report/run-fmo9ckgx`. The only new effect is the existing Debug summary
  reporting preparation refusals; all required causal controls remain recorded
  above and are included in the integrator's final complete gate.

Earlier failed fixture runs are retained as evidence, never counted as passed
controls. The original broad environment scope was stopped after its fixture was
edited; the integrator runs the complete registered gate once on the final tree.
The actual native supplementary-stream pixel comparison runs with native automatic
instancing disabled. This GL backend's automatic-instancing path does not provide
a stable supplementary-position pixel oracle, so that separate branch checks
native source references/masks and absence of private commands instead.

The existing one-shot preparation Debug summary now reports the number of
probe-enabled original candidates refused at chunk preparation. Existing applied
camera counters still count only surviving source masks, never those refusals.
Normal logging gains no per-frame diagnostic streams. Optional failures keep
existing once-per-session fail-open context. Runtime morph/fade curves, asset
loading and floor/door ownership are source-verified; OpenXR output, live shader
lighting, Frame CPU/GPU cost and FPS still require the hardware test.
