# Static batching — the last untested lever

> Status: **implemented, never run on hardware.** Ships OFF.
> Code: `src/GloomhavenVR/Core/StaticBatch{Config,Interop}.cs`, `StaticBatcher.cs`.
> Menu: Einstellungen ▸ Debug ▸ Leistung & Effekte ▸ **Objekt-Bündelung**.
> Config: `dev.gloomhavenvr.batching.cfg`.

## 1. Why this and not something else

Six hardware sessions removed every other explanation for the VR judder. What survived is one
measurement:

```
HeadCamera 21.59ms (cull 0.08 + submit 21.50) ×2 passes
```

The frame is **main-thread draw-call submission**. Every pixel-side lever was tested and moved
nothing: an 11× cut in the pixel-sample budget, shadows off at minimum quality, MSAA, eye
resolution, the game's own quality preset. The depth prepass was ~6 %, per-pixel lights ~1 %, the
whole mod's CPU 0.47 ms of 21.5.

The scene that produces it (`[Perf] SCENE`, 2026-07 hardware):

| | |
|---|---|
| active renderers | 1678 |
| submitted (enabled + visible + in mask) | **1481** |
| material slots on those | 1603 → ~3206 draw calls under MultiPass |
| **distinct material instances** | **103** |
| renderers per material | **14.4** |
| statically batched | 0 |
| under the `Maps` root | **1440 of 1481** |

14.4 renderers per material is the whole case. There are exactly three ways to collapse it, and two
are closed:

- **Single-pass stereo** halves the *passes*, not the draws — and is impossible here: the game's
  shaders ship with no stereo variants and a Unity player has no shader compiler
  (`Core/StereoModeConfig.cs`).
- **GPU instancing** is *already enabled* on 1568 of the material slots and buys nothing, because
  instancing merges renderers sharing a material **and a mesh**. This dungeon is Apparance
  procedural geometry — nearly every renderer has a mesh of its own.
- **Static batching** does not care that the meshes differ. It concatenates them into one vertex
  buffer, after which consecutive renderers sharing a material collapse into one draw. No shader
  variant, no mesh authoring. It is the one mechanism the shipped assets cannot veto.

**The game's own developers did exactly this.** `decompiled/GH.Runtime/PerformanceUtility.cs`:

```csharp
public static void Combine()
{
    GameObject gameObject = Object.FindObjectsOfType<GameObject>()
        .FirstOrDefault(go => go.name == "Maps");
    if ((bool)gameObject)
        StaticBatchingUtility.Combine(gameObject);
}
```

Wired to **Shift+F2** in `CameraController.CheckPerfTestKeys`, behind `Main.s_DevMode ||
Main.s_InternalRelease`. Same root, same call. This pass is that hotkey, made switchable, bounded,
measured — and undoable, which the hotkey has no answer for.

## 2. The rollback guarantee, and why it is a guarantee

`StaticBatchingUtility` has a `Combine` and **no `Uncombine`**. Restoring each object's mesh is not
enough: combining also writes two pieces of state *into the renderer*. Put the original mesh back
without clearing those and the renderer draws submesh range `[first, first+count)` of a mesh that
has one submesh — at best nothing, at worst the wrong triangles.

Unity's static batching is implemented **in C#**, in `UnityEngine.CoreModule`
(`InternalStaticBatchingUtility.MakeBatch`), and it is readable. Every write it performs is an
`internal` member of a public type, verified present in this build's
`ressources/Managed/UnityEngine.CoreModule.dll`:

| member | kind | used for |
|---|---|---|
| `Renderer.SetStaticBatchInfo(int, int)` | internal method | **undo** — clear the submesh slice |
| `Renderer.staticBatchRootTransform` | internal property (`NativeProperty("StaticBatchRoot")`) | **undo** — clear the batch root |
| `MeshRenderer.enlightenVertexStream` | internal property | undo — `MakeBatch` nulls it |
| `StaticBatchingHelper.IsMeshBatchable(Mesh)` | internal static | probe — Unity's own eligibility test |
| `Shader.disableBatching` | internal property | probe — Unity's other eligibility test |

So the undo is the **literal inverse of the forward operation, member for member**, not an
approximation of it.

The contract this buys, enforced in `StaticBatchInterop.Resolve()` once at install:

> **`CanRevert == false` ⇒ the pass refuses to combine anything at all.**

`Mode = On` then degrades silently to `Probe` with a log line. There is no path on which this mod
mutates a game object it cannot hand back — which is the standing rule every game-object mutation in
this codebase lives under.

Two further structural properties:

- **The ledger is written before the combine**, and the array handed to Unity is built *from the
  ledger*, not from the scan list. Several frames pass between scan and combine (one root per
  frame); building one from the other means they cannot disagree, whatever happened in between.
- **Reality gets the last word.** After `Combine`, every ledger entry whose mesh did *not* change is
  removed — Unity applies its own tests inside `Combine` and drops any batch that ended up with
  fewer than two members. This is what makes the mod's own eligibility prediction safe to be
  optimistic.

## 3. What it costs, stated before it is defended

1. **Memory.** The combined mesh is a full copy of every vertex, in system *and* video memory, on
   top of the originals — which stay alive precisely so the pass can be undone. Bounded by
   `MaxVertices` (default 4 M ≈ 170 MB per copy) and reported in MB on the `[Batch] APPLY` line.
   `FreeCombinedCpuCopy` (default on) releases the system-memory half via
   `Mesh.UploadMeshData(true)`; it cannot affect what is drawn and cannot affect the undo, which
   never reads the combined mesh back.
2. **A hitch.** `Combine` is a synchronous copy of millions of vertices. It is scheduled
   `SettleSeconds` (default 4) after a scene load — inside the loading screen — and processes **one
   root per frame**. Its real duration is measured and logged every time.
3. **Combined objects must not move.** Their vertices are baked into the batch root's space, so a
   combined object's own transform stops deciding where it is drawn. This is the one visible failure
   mode, and it has three independent defences:
   - **scan-time exclusion**: `Animator` / `Animation` / `Rigidbody` anywhere on the object or its
     ancestors up to the root (hard-coded, not configurable — it is not a preference);
   - **a watchdog**: samples 64 combined objects per second, comparing each against the pose it was
     combined at **relative to its batch root** (so moving the whole root, and the mod's world grab,
     correctly do not count), and hands everything back on its own;
   - **`ExcludeNames`**: the watchdog names the offender, and a fragment of that name in the config
     lets the rest of the map still batch.

   The mod's own layer is excluded unconditionally and cannot be re-added by config — combining the
   hands, cards or control board would freeze them in place.

## 4. The one thing that could kill it outright

`InternalStaticBatchingUtility.CombineGameObjects` skips any mesh with `!sharedMesh.canAccess` —
**silently, not as an error**. A mesh imported with Read/Write disabled has no CPU-side copy, so
nothing can concatenate it.

This game *does* ship such meshes: `FlatScreenStereo.3.Map.cs:460` documents the world-map parchment
as `isReadable=false`. **But the dungeon's geometry is generated at runtime by Apparance**, and a
mesh built at runtime is readable unless someone called `UploadMeshData(true)` on it.

That is a prediction, not a measurement — which is exactly what `Mode = Probe` is for. The
`[Batch] PROBE` line counts this rejection by name and says outright:

> NOTE: 'unreadable mesh' is the one rejection that cannot be configured away. […] If that count is
> most of the scene, static batching is impossible on this content and no setting changes that.

**One `Probe` run answers the whole question without mutating anything.**

## 5. Multiplayer

Nothing here travels. Static batching rewrites which vertex buffer a *local* renderer draws from; it
changes no game state, no card identity, no wire byte. Two peers with different settings play the
same game — one submits fewer draw calls. Compatible with the standing MP requirement **by
construction** rather than by arrangement, and the only lever in this investigation for which that
is free.

## 6. Hardware test protocol

Everything below is reachable from inside the headset:
**Einstellungen ▸ Debug ▸ Leistung & Effekte ▸ Objekt-Bündelung.**

### If the root is named differently

`Roots` is free **text**, and free text is the one control shape the in-VR config browser can only
*display* — so a wrongly-named root would be unfixable from inside the headset and the feature would
silently do nothing. Two things close that:

- **`AutoDetectRoots`** (default on, and a plain toggle on the panel: *Karte selbst finden*): when no
  configured name matches, adopt the busiest scene roots instead. Bounded — the mod's own roots are
  skipped, a candidate needs **≥ 200 mesh objects** (deliberately far above `MinRenderers`, so menu
  scenery cannot qualify), at most four are taken, and every choice is logged.
- The **`PROBE` line lists every scene root with its mesh count** whenever nothing matched, so the
  correct value for `Roots` can be read straight off the log.

### Round 1 — is it possible at all? (no mutation)

1. Start a scenario, let it load.
2. Set **Modus = Nur messen**.
3. Play ~1 minute, quit.
4. In the log: `grep '\[Batch\] PROBE'`.

The decisive fields, in order:

- **`N of M MeshFilter(s) can be combined`** — if `N` is near 1440, proceed. If `N` is small, read
  the rejection counts.
- **`… renderer(s) per material`** — the factor draw calls could fall by. Near 1 ⇒ abandon.
- **`rejected: … unreadable mesh`** — if this dominates, **stop**: no setting changes it, and the
  answer is "impossible on this content".
- **`cost if applied: … MB duplicated`** — if this is implausibly large, lower **Speichergrenze**
  before round 2.

The panel's **Zusammengefasst / Meshes / Speicher** row shows the same `N / M` live.

### Round 2 — does it buy frames?

1. Same scenario. Set **Modus = An**.
2. Wait for the loading screen to pass plus ~5 s (the settle delay).
3. Check the panel's **Zeichenaufrufe** row: `before → ~after`.
4. Play 2–3 minutes. **Rock your head back and forth** — the motion that provokes the judder.
5. Set **Modus = Nur messen** (undoes it), play the *same* 2–3 minutes with the *same* motion.
6. Quit, hand over the log.

Every mode change calls `PerfMonitor.MarkChange`, so the log carries **one `[Perf] FRAME`/`SPLIT`
summary per state with nothing straddling the boundary**. The number that settles it is the
`HeadCamera … submit` figure on the two `SPLIT` lines — *not* the `FRAME` line's `gpu` counter,
which reports the frame interval whenever the runtime is rate-locked.

### What to watch for while it is on

- Anything drawn in the **wrong place** → `grep '\[Batch\] WATCHDOG'`, which names the object.
  With the default auto-undo on, the picture repairs itself and the log says why.
- A **hitch** at scenario start → the `[Batch] APPLY` line's ms figure. One-off, not per frame.
- **Missing geometry** → `grep '\[Batch\] REVERT'` for a "could not have their batch state cleared"
  warning.

### Rolling it back

Three ways, in ascending order of finality:

1. **Rückgängig** button — undoes, keeps the mode, so "apply now" still works.
2. **Modus = Aus** — undoes and stops.
3. Delete `dev.gloomhavenvr.batching.cfg` — back to the shipped defaults (`Mode = Off`).

A scenario change undoes it either way.

## 7. Open questions this cannot answer from a desk

- Are the Apparance meshes readable? (§4 — round 1 answers it)
- Do rooms **reveal** (`SetActive`) or **rebuild**? If they rebuild, `IncludeInactive` should be
  off and `RescanSeconds` carries the load.
- Does 103 distinct materials survive as ~103 *batches*, or does the 64 000-vertex batch split
  fragment it? The `APPLY` line's mesh count against the `PROBE` line's material count answers it.
- Is the estimated "after" figure honest? It is a **floor** computed the same way the `[Perf] SCENE`
  line computes its floor. The `SPLIT` line is the arbiter.
