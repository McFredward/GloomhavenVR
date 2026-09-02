# Does the pre-grab glow work on ALL figures? — the enumeration, the guarantee, and where it stops

ModBuild 341. Written to answer the user's question of 2026-09-02, immediately after ModBuild 340
fixed the Elder Drake:

> "Das Problem mit dem Drachen ist behoben.. Top! Kannst du nun mit sicherheit sagen, dass es mit
> ALLEN Figuren im gesamten Spiel funktioniert?"

The honest answer is not "yes" and not "I don't know". It is: **for every figure whose renderers
all sit under the game's own `m_AnimatedGameObject`, the glow is correct BY CONSTRUCTION and no
assumption is involved; for every figure where they do not, the mod now names that figure in the
log the first time the scenario spawns it.** This document is the evidence for both halves.

---

## 1. The roster — how big is "all figures", really

| Category | Count | Where it is defined |
| --- | ---: | --- |
| Playable hero models | **23** (22 selectable + `DemolitionistMech`) | `ECharacter` — `decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary/ECharacter.cs:6` |
| Non-hero models (monsters, elites, bosses, objects, hero summons, NPCs, escorts, altars) | **215** | `CClass.ENPCModel` — `decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary/CClass.cs:10-228` |
| — of those, elite variants | 44 | names ending `Elite`; linked by `MonsterYMLData.NonEliteVariant` |
| — of those, numbered civilians / escorts | 32 | `Villager01..12`, `Crowd01..08`, `Captive01..06`, `Captive01..06Weapon` |
| **Total distinct figure models** | **~238** | |

**Figures whose glow has EVER been observed at runtime: four.** `HE_Brute`, `HE_Mindthief`,
`MO_SpittingDrake`, `MO_Elder_Drake` (plus `MO_RendingDrake_Elite` in the ModBuild 338 log). That is
four of ~238. The ModBuild 340 log is 13 MB and contains no others.

### What could NOT be enumerated offline, and it matters

- **The monster / boss / object / summon split is not in the code.** It lives in
  `SRLYML.Monsters[].MonsterType` (`EMonsterType { None, Monster, Boss, Object }`) and
  `SRLYML.HeroSummons`, i.e. in `.yml` data files. **Those files are not in this repo and not on
  this machine.** No exact boss count, no exact summon count, no exact objective count is
  obtainable here.
- **The figure PREFABS are not inspectable.** They are Addressables inside per-figure asset bundles
  (`hero_{name}` / `npc_{name}`, `AssetBundleManager.cs:300-328`), loaded from the game's
  `StreamingAssets/AssetBundles/`. `ressources/` on this machine contains **only** `Managed/*.dll`.
  There is no game install. **So the renderer LAYOUT of the other ~233 figures — how many renderers,
  which sit under the animated object, what furniture hangs off the root — cannot be read offline at
  all.** Any claim of completeness that rests on inspecting them would be fabricated.
- Skins multiply prefabs further (`{prefabName}_{skin}.prefab`, `AssetBundleManager.cs:300`); the
  per-hero skin list is YML data, also absent.

### The structurally different categories, and what is known about each

| Category | Verdict |
| --- | --- |
| **Summons** (Bear, Skeleton, RatSwarm, Battlebot, …) | `SummonSMB.cs:59-114` calls `Choreographer.CreateCharacterActor`, which spawns a **top-level** actor parented to the board — never under the summoner. Structurally identical to a monster. |
| **Mounts / riders** | **Do not exist.** No code anywhere parents one character prefab under another. |
| **Boss forms that transform** | `ChangeModelSMB.cs:49-71` **despawns the old figure and spawns a new one** (`DeinitializeCharacter()` then `CreateCharacterActor`). Two animated characters never coexist under one actor. The new figure is re-adopted and therefore re-audited. |
| **Figures with attached effects** | Real and everywhere: ~40 sites in `Choreographer.cs` spawn condition/buff VFX as children of the actor root, and `ActorBehaviour.cs:427-433` parents the invisibility effects under `m_Animator.transform`. These are `ParticleSystemRenderer`s, which the glow's kind filter has always excluded. |
| **Stuck projectiles** | `FireEquippedProjectileSMB.cs:134` parents another actor's arrow to `TargetBoneInstance` **inside** the animated hierarchy — permanently. This is why `HE_Brute` glows a `WP_Bandit_Knife_01` he did not start with. It is inside the mini and glows with it, which is right. |
| **Objective / escort actors** | The sharp case. `CObjectActor.AttachedProp` means a whole class of actors (doors, destructibles) has its **visible geometry outside its own root entirely** — `Choreographer.FindClientActorGameObject(actor, shouldReturnDummyActorsProp: true)` returns a prop from `ObjectCacheService` instead. Their model is `PropDummyObject`. These are the figures most likely to grade anything other than PROVEN. |

---

## 2. Why the ModBuild 340 rule is right, from the game's own code

`ActorBehaviour.SetActor` (`decompiled/GH.Runtime/ActorBehaviour.cs:109-127`):

```csharp
actorBehaviour.m_Animator = MF.GetGameObjectAnimator(actorBehaviour.m_RootGameObject);
actorBehaviour.m_AnimatedGameObject = (actorBehaviour.m_Animator ? actorBehaviour.m_Animator.gameObject : null);
...
actorBehaviour.m_Renderers = actorBehaviour.m_Animator.gameObject.GetComponentsInChildren<Renderer>();
```

Two facts follow, and they are the whole guarantee:

1. **The game itself defines "this actor's renderers" as exactly that subtree** (line 121). It is the
   array the invisibility dissolve walks (`:398-418`). A renderer outside `m_AnimatedGameObject` is
   one the game will not even hide when the figure turns invisible.
2. **The game MOVES that subtree and not the root.** `ActorBehaviour.DoTransform` (`:468-548`) writes
   `m_AnimatedGameObject.transform.position` on every locomotion path and gives `m_RootGameObject`
   only a rotation. A body renderer parented outside it would **stay behind every time the figure
   walks** — a defect the base game would show with no mod installed. That is not a theory about the
   boss's bands; the wall-fade census recorded exactly that behaviour for them ("FLOATING … drifting
   frame to frame").

So the "real body geometry outside `m_AnimatedGameObject`" case (under-glow) is closed for any
figure that moves, by the game's own bookkeeping. What is NOT closed is the second case:
`MF.GetGameObjectAnimator` (`decompiled/GH.Runtime/MF.cs:395-406`) returns the **first** Animator in
`GetComponentsInChildren<Animator>()` order with a `runtimeAnimatorController` — it is not defined to
be the character's own. If a foreign animated object were reached first, the field would point at the
wrong subtree entirely.

Note that this second case would break the base game too: the wrong subtree would be the thing that
walks, the thing `m_Renderers` covers, and the thing `m_Animator.SetFloat(_runBlend, …)` drives. It
is nonetheless the case ModBuild 341 hardens against, because "the vanilla game would also be broken"
is an argument, not a measurement.

---

## 3. The hardening (ModBuild 341)

`FigureHighlight.Judge` grades every figure and `FigureHighlight.GradeOf` is the rule as pure
arithmetic:

| Grade | Meaning | Clone source | Log tier |
| --- | --- | --- | --- |
| `Proven` | the restriction drops NOTHING (all candidates already under the animated object, or there is no animated object) | same set either way | `Note` |
| `RootFallback` | the animated object holds no clonable renderer | whole actor root (over-glow) | `Alert` |
| `Furniture` | drops renderers, and keeps strictly more geometry than it drops | `m_AnimatedGameObject` | `Alert` |
| `Refused` | would drop at least as much geometry as it keeps | whole actor root (over-glow) | `Alert` |
| `Nothing` | no clonable renderer at all | — | `Alert` |

`FigureHighlight.AuditFigure` runs the grade **at adoption**, on every figure the scenario spawns
rather than only the ones a hand reaches, once per distinct `{class id}|{model}` per session. A
played session therefore produces the roster the asset bundles refuse to hand over, with a verdict on
each entry.

The `Refused` guard **cannot fire on any figure ever measured**: the four known-good drop nothing at
all, and the boss drops 120 vertices against 14 846 — the bands would have to grow by a factor of 124.

---

## 4. What the hardware tester should hover, in priority order

Every one of these is answered by a single line, `[FigureGrab] MINIATURE AUDIT n: …`, which is printed
at the default log level and needs no hovering at all — merely loading the scenario produces it.

| Priority | What | Why | The line that answers it |
| ---: | --- | --- | --- |
| 1 | Any scenario with a **door or destructible obstacle** | `CObjectActor.AttachedProp` puts the visible geometry outside the actor root; most likely non-PROVEN grade in the game | `MINIATURE AUDIT … ROOT FALLBACK` or `NOTHING TO GLOW` |
| 2 | A **hero summon** (Beast Tyrant's Bear, Necromancer's Skeleton, a Summoner's minions) | never observed; own prefab family | `MINIATURE AUDIT …` |
| 3 | **Another boss** — anything that is not the Elder Drake | the boss family is the one family with a confirmed non-PROVEN member | `MINIATURE AUDIT … FURNITURE DROPPED` |
| 4 | An **escort / objective NPC** (`Captive*`, `Villager*`, `WoundedGuard`) | a category with 32 members and zero observations | `MINIATURE AUDIT …` |
| 5 | Any **remaining hero class** | 4 of 23 hero models observed | `MINIATURE AUDIT …` |
| 6 | The **Demolitionist mech transformation**, if reachable | the one model swap in the game; the audit is keyed on the model so both forms report | two `MINIATURE AUDIT` lines for one class id |

**What to send back:** every `MINIATURE AUDIT` line in the log. Nothing else is needed. If they all
say `VERDICT: PROVEN`, the answer to the user's question is yes for everything he played, and it is a
measurement, not an opinion.
