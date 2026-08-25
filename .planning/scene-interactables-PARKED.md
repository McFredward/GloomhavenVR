# PARKED, SURVEY INCOMPLETE — hand-reactive scene props (nothing was built)

**Status: PARKED by the user mid-survey, ModBuild 287 round.** He asked the question, got the
structural answer, and said *"Ok belassen wir es vorerst bei den Figuren"*. **No code was written.
No census instrument exists.** Read §7 before treating any row below as settled.

**His question:** *"Gibt es neben Characteren noch mehr Objekte in Szenarien die solche
interaktiven Elemente haben, die man auch mit nem Collider ausstatten könnte, damit sie auf meine
Hände in VR reagieren?"*

**Short answer: yes — but not the objects the round went looking for.** The scene dressing (vines,
banners) turned out to be the *worst* candidate. Three better ones surfaced, and one of them needs
no new simulation at all.

---

## 1. Two premises this survey went in with, and what the evidence did to them

### 1a. "`Cloth` is attached by the game at `ActorBehaviour.cs:122`" — WRONG VERB, right conclusion

```
decompiled/GH.Runtime/ActorBehaviour.cs:122
    actorBehaviour.m_Clothes = actorBehaviour.m_Animator.gameObject.GetComponentsInChildren<Cloth>();
```

That is a **query, not an attach**. `AddComponent<Cloth>` appears **nowhere** in the decompiled
game; `Cloth` appears in exactly two files (`ActorBehaviour.cs`, `PhysicsController.cs`). Every
`Cloth` is **authored into a prefab**, and no code path creates, destroys or re-seeds one. The line
proves the game only *manages* cloth under an actor's `Animator`; it does not prove none exists
elsewhere. **Source cannot answer that; only runtime can.**

### 1b. "`PhysicsController`'s scene-wide `FindObjectsOfType<Cloth>()` is the one probe that would see a non-actor cloth" — **FALSIFIED. That code never runs.**

`PhysicsController.cs:53-56` holds the sweep. Its only callers are `SimplifyPhysics()` (:34) and
`RestorePhysics()` (:46), and **both have zero callers in the entire decompiled game.** The only
live entry point is `Setup(bool)` (`SceneController.cs:1069`), which never touches cloth. Dead code.

---

## 2. The fact that decides the *dressing* half of the question

From the **shipped assembly** — `ressources/Managed/UnityEngine.ClothModule.dll`, decompiled with
`tools/ilspycmd`:

```csharp
[RequireComponent(typeof(Transform), typeof(SkinnedMeshRenderer))]
public sealed class Cloth : Component
{
    public extern CapsuleCollider[]          capsuleColliders { get; set; }
    public extern ClothSphereColliderPair[]  sphereColliders  { get; set; }
}
```

1. **A `Cloth` cannot exist on a `MeshRenderer`.** Scene dressing is `MeshRenderer` (his ModBuild
   286 log classifies `CR_RU_Vines (3)` as `[mesh]`). `AddComponent<Cloth>` there **succeeds** —
   Unity silently attaches an empty `SkinnedMeshRenderer` first — so "it didn't throw" proves
   nothing.
2. **"Give the prop a collider" is the wrong direction for cloth.** PhysX cloth does not collide
   against arbitrary scene colliders; it reads **only** the two arrays above. The hand carries the
   collider and **each cloth must register it**, which is what the free-hand lane does for capes.

---

## 3. THE THREE REAL CANDIDATES (this is the answer)

### 3a. `CObjectActor` scenario objects — same population as characters ✅ best cloth candidate

Scenario "objects" (altars, statues, destructible objective objects) are **not props**. They are
full `npc_*` bundle prefabs that go through the *same* actor path as a hero:

```
Choreographer.cs:13440    m_ClientObjects.Add(CreateCharacterActor(cClientTile5, @object));
Choreographer.cs:1458     ... CreateCharacterActorCoroutine(cClientTile6, @object) ...
AssetBundleManager.cs:328 prefabBundleName = string.Format("{0}_{1}", "npc", prefabName...);
```

`CObjectActor : CEnemyActor`, `CObjectClass : CMonsterClass`. `CreateCharacterActor` →
`ActorBehaviour.SetActor` → `m_Clothes` cached. **So any cloth authored on such a prefab is already
being managed exactly like a cape, and the existing FigureCloth machinery would apply unchanged.**
*(All four citations spot-checked verbatim against the decompiled source.)*

### 3b. VFX particle systems — already collision-enabled, needs no new simulation ✅ cheapest

Two shipped scripts mean a hand collider on the right layer would be felt **today**:

```
RFX4_CollisionPropertyDeactiavtion.cs   OnEnable → collisionModule.enabled = true;
                                        Update   → after DeactivateTimeDelay (1 s), = false;
RFX4_ParticleCollisionHandler.cs        OnParticleCollision(GameObject other)
                                          → part.GetCollisionEvents(other, collisionEvents)
                                          → Instantiate(EffectsOnCollision[j], intersection + normal*Offset, ...)
```

A particle **collision module is switched on at runtime** for a one-second window on these systems,
and the handler spawns impact effects at the contact point. This is the one place where literally
"put a collider on the hand" produces a visible reaction with **zero** cook, zero coefficients and
zero new components on scene objects. *(Both files read in full and verified.)*

Related, unverified: `RFX4_PhysicsForceCurves.cs:60-95` does `Physics.OverlapSphere` and
`AddForce` on any collider that **has a Rigidbody** — a hand collider with an RB would be pushed by
VFX.

### 3c. Vegetation and banners already move — by **vertex-shader wind**, which no collider can touch

```
ThirdParty/NM_Wind.cs:53-63
    Shader.SetGlobalVector("WIND_SETTINGS_WorldDirectionAndSpeed", GetDirectionAndSpeed());
    Shader.SetGlobalFloat ("WIND_SETTINGS_Turbulence", WindSpeed * Turbulence);
    ... FlexNoiseScale, ShiverNoiseScale, GustSpeed, GustScale, GustWorldScale
```

The vines and banners sway from **global shader uniforms**, not bones and not cloth. A collider is
structurally incapable of disturbing that. A mod *can* write those globals — but they are global,
so it is a world-wide wind dial, not a per-hand interaction. *(Verified verbatim.)*
⚠️ Ties into the standing note *"Culling cannot see vertex shaders"*: displaced geometry is culled
against undisplaced bounds.

---

## 4. Candidate table — confidence per row

Counts are occurrences inside **one** wall-fade census in his ModBuild 286 `Player.log`
(`ENTERED (2813 renderer(s) in 417 name group(s))`). **Not scene totals**; the list was elided.

| candidate | class | confidence | count | mechanism |
|---|---|---|---|---|
| `CObjectActor` scenario objects | **NPC actor prefab, `Animator`, cloth cached** | **verified from source** | unknown | **Existing FigureCloth path — nothing new needed** |
| Collision-enabled VFX particles | `ParticleSystem` + collision module | **verified from source** | unknown | **Hand collider on the right layer. No cook.** |
| `CR_RU_Vines (2)/(3)/(5)` | **MeshRenderer**, under `Wall 1/Generated Content/PCG_*` | **verified from his log** | 42 in one group | Shader wind today; a transform bend is the only hand-driven option |
| `EN_CR_Hanging_01_Cloth_Post` | **SkinnedMeshRenderer** (`[skinned→cutoff]`) | **verified from his log** | 7 | Skinned → could take a `Cloth`. **Bone/vertex counts unknown.** |
| `Ribbon`, `Door_Light_*_Mesh` | claimed skinned, **never checked** | **UNVERIFIED** | 57 / 59 / 58 | unknown |
| `Banner` / `Flagge` / `Title_Banner` | **count is contaminated** | **UNRELIABLE** | — | do not act on this row |

**Correction on the banner row.** `Banner toggle` (114) and `UIPhaseBanner` are **UI**; `Flaggen` /
`Flagge inklusive der Stange vollst…` (72) are **German prose from the wall-fade flag-pole rule's
own log text**, not object names. The ~190 in the round brief mixed props, UI and log commentary.

**Also established, and it closes a door:** `grep` for `SkinnedMeshRenderer` across game code shows
**every** hit is on a character; no script anywhere attaches a skinned renderer or bone chain to
dressing, and there is **no IK, no `.bones` write, no `GetBoneTransform`** in the whole game. So
"bone-driven displacement" has no precedent to copy — it would be entirely mod-authored.

---

## 5. Reachability — geometrically fine for the vines

`[Cards] BOARD ANCHOR … world scale 30.848 … against rig ×30.85` → **~30.85 world units per real
tracking metre**. A vine measured in the same log (`unit y[1.2..2.8] over floor 0.0, widest 1.5 wu`)
is **~5 cm tall and ~5 cm wide as perceived**, in the same height band as a figure he already
reaches in and grabs. Palm reach `ProximityGrabber.ReachMeters = 0.13f` **real metres** ≈ 4.0 wu.

**Caveat:** the scenario runs near ×4.4 base, the map room near ×198
(`src/GloomhavenVR/Defaults/Defaults.Rig.cs:46`), and pinch-zoom moves the live value. Any reach
bound must be **real metres × `VRHand.WorldScale`**, never world units.

---

## 6. Why NOT a runtime `Cloth` on dressing

1. **The cook.** `AddComponent<Cloth>` = **19.37 ms at 3721 vertices, ~5.3 µs/vertex, linear**
   (`.planning/perf/CLOTH-COOK.md`). Main-thread, **per prop**, against 11.11 ms. Forty-two vines is
   unshippable however it is sliced.
2. **No coefficients to paint.** A fresh `Cloth` returns `float.MaxValue` (*not* `Infinity`) for
   every vertex = fully unconstrained; the prop would sag off its anchor. Nothing authored to paint
   from.
3. **Apparance rebuild risk — but see the correction below.**

### ⚠️ CORRECTION to my own first draft of this file

I wrote that Apparance "rebirths these subtrees constantly", quoting the mod's *own* comments rather
than the game source. **That is overstated.** From `ProceduralMapTile.cs:148-177` (read verbatim),
room reveal is a **`SetActive` toggle**, not a teardown:

```csharp
public static void ShowContent(GameObject o, bool show_full, bool show_preview = false) {
    GameObject gameObject = o.FindInChildren("Generated Content", includeInactive: true);
    ... gameObject3.SetActive(show_full); ... }
```

and `RoomVisibilityManager.EvaluateRoomsVisiblity` reveals each room **once**, then removes the
tracker — reveal is never reversed. **An attached component survives room reveal and room entry, and
nothing rebuilds on camera movement.** A true teardown happens only when `ProceduralTileObserver`
re-runs `Apply()` (its own `activeInHierarchy` flips, or a `ProceduralTile` in range toggles). And
there is a **documented re-attach hook**: `ProceduralBase.ContentPlacementCompleted`, plus
`ProceduralProp.PlacementCompleteAction`, which `Choreographer.OpenDoor` already uses.

Reasons 1 and 2 still stand on their own and are sufficient.

**Separately:** dressing is owned by `WallSegmentFade.IsWallGeneratedDressing`
(`src/GloomhavenVR/Core/WallSegmentFade.cs:6812`) — anything that moves it must not fight the fade.

---

## 7. What was NOT established

* **No census instrument was built**, no controls run. Nothing in the mod enumerates scene cloth,
  bones or reachability.
* **Whether any non-actor `Cloth` exists at runtime is still open.** §1 shows source cannot answer
  it and the nominated probe is dead code. Needs a scene walk on his hardware.
* **Vertex counts are unknown for every candidate**, so §6's cook figure cannot be turned into a
  per-prop millisecond number for anything real.
* **Three candidate rows unverified** (`Ribbon`, `Door_Light_*`, banners): renderer class, bone
  count, vertex count, per-scenario totals all unread.
* **A prop bench (`PropBench.cs`) was drafted and deliberately deleted rather than committed,
  because it was never run.** It would have measured: what `AddComponent<Cloth>` produces on a
  `MeshRenderer`; static→skinned→cloth conversion cost; the cap on registered `sphereColliders`;
  whether a registered sphere genuinely displaces a cloth against a not-registered control arm; and
  bone-chain vs transform-bend per-frame cost. **None of those numbers exist.** If unparked, that
  bench is the first thing to write, beside `.planning/perf/cloth-cook-harness/`.
* **§3 came from a broad decompiled sweep; five load-bearing citations were spot-checked verbatim**
  (`Choreographer.cs:1458/13440`, `AssetBundleManager.cs:328`, `NM_Wind.cs:53-63`,
  `ProceduralMapTile.cs:148-177`, both RFX4 collision files). Other line references in that sweep
  are **unverified**.
* **`Physics.autoSyncTransforms` hazard, unresolved.** `PhysicsController.Setup` sets it **false**
  when `PlatformLayer.Setting.SimplifyPhysics` is on *and* the scene is literally named
  `"Game_gamepad"` (`SceneController.cs:1069`). On that path a hand collider's transform write does
  not reach PhysX until the next `FixedUpdate` — cloth and particle collision would lag or miss.
  Whether his rig takes that path was never checked.
* **Doors, chests, traps, obstacles are props, NOT actors** — registered in
  `ObjectCacheService._propsCache`, never passed to `SetActor`. A destructible one gets an invisible
  `PropDummyObject` actor with no renderer. They animate via `Animator.Play` state names (`"Open"`,
  `"Trap_Shut"`, `"Pressed"`), driven by game state — **and presentation code must never write game
  state**, so they are not a hand-interaction surface.
* **Reachability is geometric only.** Nothing measured about where his hands actually travel.
