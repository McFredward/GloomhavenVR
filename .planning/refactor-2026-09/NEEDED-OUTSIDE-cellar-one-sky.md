# NEEDED OUTSIDE — lane `cellar-one-sky`

Base `6efa4acf` (ModBuild 482). One finding acted on in-lane (the cellar's `StarDome` is gone from
the bake); three sites outside the lane's file set:

1. `PreviewClouds.cs:653` — **will throw** on the new prefab. Please apply the diff.
2. `NetProtocol.cs:19020` — looks like drift, is a changelog entry. **Leave it.**
3. `BuildEnvironmentRooms.cs` stair alcove — **two genuine holes in the cellar shell**, found while
   proving the shell closed. Not urgent, not caused by this round, and worth a fifth winding gate
   more than it is worth the geometry fix.

(§3 was added after §"Nothing else" was written; that section's sweep is about consumers of the
`StarDome` NODE and remains correct.)

---

## 1. `unity/GloomhavenVR.Assets/Assets/Editor/PreviewClouds.cs:653` — WILL THROW. Please apply.

`PreviewClouds.Cellar()` resolves the cellar's dome and dereferences it without a null check. Every
other station in that file guards the lookup (`:131` throws a named exception, `:655-657` use `?.`);
this one does not, and `Env_Cellar` no longer has the node.

**Symptom after the bundle rebuild:** `NullReferenceException` at `PreviewClouds.cs:654` the next
time the cloud preview station runs. The bundle build itself is unaffected — `build-bundles.sh` runs
`EnvironmentsBuilder.BuildAll`, not this station — so this does not block the rebuild the integrator
owns. It blocks the next cloud round.

```diff
--- a/unity/GloomhavenVR.Assets/Assets/Editor/PreviewClouds.cs
+++ b/unity/GloomhavenVR.Assets/Assets/Editor/PreviewClouds.cs
@@
             Shader.SetGlobalFloat("_GhvrIndoor", 1f);       // the cellar's own ambient rules
-            var dome = _inst.transform.Find("StarDome");
-            _domeRen = dome.GetComponent<Renderer>();
-            _starsRen = dome.Find("StarField")?.GetComponent<Renderer>();
-            _cloudBand = dome.Find("CloudBand")?.gameObject;
+            // NO DOME SINCE 2026-09-08, and that is the SHIPPED state, not a failure: the user
+            // reported two star skies in the cellar ("der gekrümmte reicht") and BuildCellar
+            // stopped calling AddNightSky, so this room's sky is the NightSky patch inside
+            // RoomGeo. The lookup stays — an OLD bundle still has the node and these frames must
+            // keep working against one — but it may no longer be dereferenced blind.
+            var dome = _inst.transform.Find("StarDome");
+            _domeRen = dome != null ? dome.GetComponent<Renderer>() : null;
+            _starsRen = dome != null ? dome.Find("StarField")?.GetComponent<Renderer>() : null;
+            _cloudBand = dome != null ? dome.Find("CloudBand")?.gameObject : null;
```

`_domeRen` and `_starsRen` are only ever used through `if (_domeRen != null)` (`:273`) and the
equivalent guard on the star renderer, so null is already a supported value for both. The
`_cloudBand == null` branch at `:659` — which the cellar has always taken and which the file calls
"THE EXPECTED OUTCOME" — still runs and still shoots its two frames; its log sentence ("the cellar's
sky is bit-identical to the build before this feature existed") is now stale in a second way and
could say "the cellar has no dome for a band to hang under" instead.

---

## 2. `src/GloomhavenVR/Net/NetProtocol.cs:19020-19022` — LEAVE IT. Recorded here so the next lane
does not "fix" it.

```
// (3) CELLAR NIGHT SKY ("sonst ist der Himmel einfach nur schwarz"): Env_Cellar gains the
//     same StarDome node as the swamp (shared mesh/material by GUID, no texture duplication).
//     Bundle rebuilt — 130 NEEDS it.
```

This reads like doc-drift and is not. It is the **ModBuild 130 entry of the build changelog**, and it
was true of ModBuild 130: at 130 the cellar was an authored game map (`Map ABHM`) with no roof, the
sky above it really was black, and adding the dome really was the fix. The stone cellar with its
closed plank ceiling did not exist until the 2026-08-13 custom-asset round. Rewriting a changelog
entry to match today's tree would destroy the only record of why the dome was ever there — which is
exactly the evidence this round needed to retire it. A changelog is a statement about a past build,
not a claim about the present.

If anything is added, it belongs in the entry for the build that CARRIES this change, written by the
integrator when he bumps `ModBuild` — one sentence, e.g. *"CELLAR: one sky. `Env_Cellar` no longer
carries a `StarDome`; the room is roofed and its sky is the curved `NightSky` patch at the window.
Bundle rebuilt."* That is a net-lane / integrator edit, not a `cellar-one-sky` one.

---

## Nothing else

Swept for other consumers of the node:

- `src/` — no site resolves `"StarDome"` by name; the five mentions are all doc comments. The split
  (`SkyAlternative.RoomBoundShellChildren`) is an allow-list of ROOM names, so the dome's absence
  routes nothing wrongly.
- `unity/.../Editor/PreviewEnvironments.cs:3188` and `PreviewClouds.cs:131` both throw on a missing
  `StarDome`, but both load **`Env_Swamp.prefab`**, which keeps its dome. Unaffected.
- `unity/.../Editor/BuildEnvironments.cs` `PruneUnreferenced()` removes nothing: `Env_Dome.asset`,
  `Env_StarField.asset`, `Swamp_StarDome.mat` and `Sky_StarPoints.mat` are all still referenced by
  `Env_Swamp`. The bundle therefore does not shrink by those bytes — only the cellar's per-frame cost
  falls.
- `MapTableBuilder.Build()`'s assert (the map table is in neither room prefab) does not read the sky.

---

## 3. `unity/GloomhavenVR.Assets/Assets/Editor/BuildEnvironmentRooms.cs` — the stair alcove is authored
against the UNSNAPPED hole, and `BuildShaft` has no floor. Two real holes in the shell.

Found while proving the cellar shell closed for this round, i.e. it is a by-product of the
investigation and NOT caused by it. It is filed rather than fixed because the file is outside this
lane's set and because a geometry change to the doorway is not the one change this round was asked
for. **It is not urgent** — see the magnitude at the bottom.

**Root cause, one sentence:** the W wall is CUT to `SnappedHole(StairHole, CD, CH, WallCell)`
(`:5866` — the same snapping the ModBuild 134 floating-window-bars round introduced), but the alcove
that is supposed to back that cut is placed from the **authored** `StairHole.xMin`/`.width`
(`:6761`, `:6770-6772`, `:6777-6780`). The two rectangles do not coincide.

The arithmetic (`SnappedHole` `:4122-4135`, which is `WallMesh`'s own cell-centre test `:2721-2723`):
W wall `len = CD = 9.0`, `nx = ceil(9 / 0.16) = 57`, cell `= 0.157895`. Cell centres inside the
authored `x ∈ (6.2, 7.8)` are `i = 39…48`, so

```
cut   z 1.65789 … 3.23684   (width 1.5789),  y 0 … 2.35714
shaft z 1.700   … 3.300     (authored 1.6 wide)
steps z 1.750   … 3.250     (slab length 1.5)
```

**LEAK A — 42.1 mm of cut wall with nothing behind it, down the SOUTH jamb.** `cut.zMin = 1.65789`,
the shaft's south side wall stands at `z = 1.700`. The only thing over that strip is
`AddStairArch`'s flat dressing, `Jamb()` `:8752-8770`, which reaches `cut.xMin + inset`; and
`Courses()` `:8659-8661` deliberately forces ONE stone per side to `inset = 0.018f` (the "missing"
course). So a **≥24 mm wide, full-course-height slot is guaranteed by construction**, whatever the
hash — plus more wherever an ordinary course's `Lerp(0.030, 0.1708, hash)` lands under 42.1 mm.
The bore return (`:8772-8797`) stands AT the jamb face and does not close it. A ray through the slot
with a −z component (any head at z > ≈1.66) exits west of the wall: the shaft wall at z = 1.7 faces
+Z so it never intercepts, and the wall mesh's own back faces are culled.

**LEAK B — the un-floored strip beside the bottom step.** `BuildShaft` (`:11411-11431`) builds two
side walls and a ceiling and **nothing else** — no floor, no back. `Step0`'s riser covers only
z 1.750…3.250, so the band `z 1.700…1.750 × y 0…0.19` is inside the shaft with no step and no floor
under it, and jamb course 0 reaches only `cut.xMin + inset₀` (≥ 0.030). A descending ray through it
passes under the stairs and out of the open bottom of the shaft.

**Proposed fix** (two changes, both small, both in this one file):

```diff
@@ ~:6761  Step placement
-                    new Vector3(-hw - 0.17f - 0.30f * i, 0.19f * i, -hd + StairHole.xMin + StairHole.width / 2f),
+                    new Vector3(-hw - 0.17f - 0.30f * i, 0.19f * i, -hd + door.xMin + door.width / 2f),
@@ ~:6770  Shaft placement
-            var shaftMesh = SaveMesh("Env_C_Shaft.asset", BuildShaft(2.2f, 2.6f, StairHole.width));
+            // SNAPPED, not authored: the wall is CUT to the snapped rect (:5866) and an alcove
+            // built to the authored one leaves 42.1 mm of cut wall backed by nothing.
+            var shaftMesh = SaveMesh("Env_C_Shaft.asset", BuildShaft(2.2f, 2.6f, door.width));
-            var shaftGo = Place(root, "StairShaft", shaftMesh,
-                new Vector3(-hw, 0, -hd + StairHole.xMin), Vector3.zero, Vector3.one, null);
+            var shaftGo = Place(root, "StairShaft", shaftMesh,
+                new Vector3(-hw, 0, -hd + door.xMin), Vector3.zero, Vector3.one, null);
@@ ~:6777  Cap
-            var capMesh = SaveMesh("Env_C_ShaftCap.asset", BoxMesh(StairHole.width, 2.6f, 0.05f, 1f));
+            var capMesh = SaveMesh("Env_C_ShaftCap.asset", BoxMesh(door.width, 2.6f, 0.05f, 1f));
             Place(root, "ShaftCap", capMesh,
-                new Vector3(-hw - 2.15f, 0.6f, -hd + StairHole.xMin + StairHole.width / 2f),
+                new Vector3(-hw - 2.15f, 0.6f, -hd + door.xMin + door.width / 2f),
```

plus a floor quad in `BuildShaft` (`:11411`) — the shaft already builds two walls and a ceiling, so
a fourth face wound +Y is the same three lines. `door` is `SnappedHole(StairHole, CD, CH, WallCell)`,
which `:5866` already computes; it would have to be hoisted or recomputed at `:6756`.

**A GATE IS THE MORE VALUABLE HALF.** This file already has four winding/plug gates — the reveal
(`:7890`), the rat holes (`:8457`), the stair arch (`:8799`) and the night ground (`:4620`) — each
one written after a bug of exactly this shape. **None of them covers the shaft, the cap or the
steps.** Whoever takes this should add the fifth: assert that every wall cut is fully backed, i.e.
that the alcove rect CONTAINS the snapped rect on both jambs and that the shaft has a floor. A
sliver is not findable by looking at a render; it is findable by asserting the rectangles.

**Magnitude, so this is prioritised honestly:** each leak subtends roughly 0.2° × 2° at 6 m, at the
dark south jamb of an unlit doorway — which is why no preview station and no hardware round ever
caught it. Before `08499af2` those pixels showed the star dome (`EnvStars` draws at
`Queue Background+5`, ahead of the opaque room, so it survives exactly where the room fails to write
depth). **After it they show the `[Rig] VoidColor` clear.** Black-on-black at that size is a
non-event, so this change does not make the leaks worse in any way a player can see — it changes
what they leak ONTO. It should be fixed because a hole in the shell is a hole, not because it looks
wrong today.
