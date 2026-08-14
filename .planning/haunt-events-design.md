# HAUNT EVENTS — the replacement set, designed against what the assets actually are

**Status:** design proposal. Nothing here is implemented. Written 2026-08-15 against `main` @ `2bc8a57`.

**Scope.** Four apparitions are being deleted with the hand-built geometry that played them. This
document designs their replacements out of the game's own enemy models, on the machinery
`HauntFigures.*` already ships, and states exactly where that machinery must be extended.

**What survives untouched and is NOT redesigned here:** cellar 1 (handprints), cellar 3 (the cobweb
tremble), cellar 5 (the toppling bookshelf), forest 1 (the eyeshines), and the rat
(`EnvCritter`, not a card).

**What is already done and is NOT redesigned here:** cellar 0 (the window), cellar 4 (the stair
doorway), forest 2 (the watcher), forest 3 (the crossing). The user's verdict on those four —
*"Die neuen Assets sind von der größe und Animation passend"* — is the reason this document exists,
and their construction is the template everything below follows.

**What is being replaced:** cellar 2, forest 0, forest 4, forest 5. Confirmed from source:

| Card | Bake name | What it is today | Source |
|---|---|---|---|
| Cellar 2 | `Floor` | a head lying on the flagstones at the edge of the moon pool | `BuildEnvironmentRooms.cs:7671-7692` |
| Forest 0 | `Face` | a head easing out from behind a trunk at ~11 m | `BuildEnvironmentRooms.cs:10495-10509` |
| Forest 4 | `Loom` | a 3.05 m hunched body at 10.5 m, back turned | `BuildEnvironmentRooms.cs:10608-10625` |
| Forest 5 | `Hang` | a body hanged by the neck from a branch at 9.4 m, on a rope | `BuildEnvironmentRooms.cs:10643-10656` |

The id tables are quoted verbatim at `BuildEnvironmentRooms.cs:7561-7580` (cellar) and `:10459-10475`
(forest), and mirrored in `Haunt.Schedule.cs:234-235`.

---

# 1. GROUND TRUTH

Everything in this section is read from the hardware log
(`.planning/debug/Player.log`, captured 2026-08-15) or from decompiled source. Nothing is inferred
unless it says so.

## 1.1 The roster that actually resolved

`Player.log:3499`:

> `HAUNT FIGURES roster resolved from the running game — [Cultist = 'Cultist' bundle 'npc_cultist'] [Hound = 'Hound' bundle 'npc_hound'] [LivingBones = 'Living Bones' bundle 'npc_livingbones'] [LivingCorpse = 'Living Corpse' bundle 'npc_livingcorpse'] [LivingSpirit = 'Living Spirit' bundle 'npc_livingspirit'] — 5 of 7 wanted models available.`

**Five of seven.** `BoneRanger` and `HighCultist` did **not** resolve — and note *how* they failed.
`Roster.Resolve` (`HauntFigures.Roster.cs:159-214`) appends a bracket for every failure mode it knows
about (`no bundle config`, `DLC …, excluded`, `threw …`). Neither name produced a bracket at all,
which means the loop never reached them: **no `CMonsterClass` in `MonsterClassManager.Classes`
listed those models in that session** (111 classes loaded, `Player.log:3501`). Yet
`npc_boneranger dlc=None` is right there in the bundle table (`Player.log:3559`).

**This is the single most important fact in the section, and it is a correctness problem, not a
curiosity** — see §1.5.

## 1.2 The per-creature census

Only **two** creatures were ever spawned in that session, so only two have a census. Verbatim from
`Player.log:5784` and `:14025`:

### Cultist (`Player.log:5784`)

- Components after stripping: `Transform`×169, `CharacterManager`×1, `Animator`×1,
  `CapsuleCollider`×6, `DeathDissolve`×1, `FootstepSound`×1, `AnimFXTrigger`×1, `VFXLookup`×1,
  `Outlinable`×1, `DetailsDisabler`×1, `EnemyShadowsDisabler`×1, `SkinnedMeshRenderer`×4,
  `TargetStateListener`×4, `Cloth`×1
- scale 1.000 · playing `Idle-Run` · **`RunBlend` PRESENT**
- States present: `Idle-Run`, `Attack`, `PowerUp`, `Hit`, `Death`, `PushPull`

| Clip | Length |
|---|---|
| `Cultist_Idle_v001` | 1.33 s |
| `Cultist_Walk_v001` | 1.00 s |
| `Cultist_Basic_Melee_Attack_Variant_v001` | 1.93 s |
| `Cultist_Basic_Melee_Attack_v001` | 2.27 s |
| `Cultist_OnDeath_v001` | 1.53 s |
| `Cultist_OnHit_v001` | 1.53 s |
| `Cultist_Summon_Living_Bones_v001` | 3.57 s |
| `Cultist_Explode_OnDeath_v001` | 4.00 s |
| `Cultist_PowerUp_Taunt_v001` | 4.17 s |
| `Cultist_Ranged_Heal_v001` | 4.50 s |

### Living Corpse (`Player.log:14025`)

- Components after stripping: `Transform`×166, `CharacterManager`×1, `DetailsDisabler`×1,
  `EnemyShadowsDisabler`×1, `Animator`×1, `CapsuleCollider`×4, `DeathDissolve`×1,
  `LivingCorpseIdleSelect`×1, `FootstepSound`×1, `AnimFXTrigger`×1, `VFXLookup`×1, `AutomaticLOD`×2,
  `LODGroup`×1, `Outlinable`×1, `SummonAppear`×1, `SkinnedMeshRenderer`×5, `Simplifier`×1,
  `TargetStateListener`×5, `Cloth`×1
- scale 1.000 · playing `Idle-Run` · **`RunBlend` PRESENT**
- States present: `Idle-Run`, `Attack`, `PowerUp`, `Hit`, `Death`, `PushPull`, **`Summoned`**

| Clip | Length |
|---|---|
| `LivingCorpse_Walk_v001` | 0.87 s |
| `LivingCorpse_OnHit_v001` | 2.70 s |
| `LivingCorpse_Spawn` | 2.83 s |
| `LivingCorpse_Standard_Melee_Attack_v001` | 3.33 s |
| `LivingCorpse_OnDeath_v001` | 3.93 s |
| `LivingCorpse_Idle_v001` | 4.00 s |
| `LivingCorpse_Radial_Burp_v001` | 4.53 s |
| `LivingCorpse_PowerUp_Taunt_v001` | 6.40 s |

### Two readings of that component list that change decisions

1. **`Cloth`×1 is a REPORTING ARTEFACT, not a live component.** `Clone.Strip` destroys every `Cloth`
   (`HauntFigures.Clone.cs:445-446`), and `CensusFigure` runs in the same frame
   (`Clone.cs:294`, `:342`, then `:406`); `Object.Destroy` is deferred to end of frame, so a
   pending-destroy component is still non-null to `GetComponentsInChildren`. **From the next frame
   there is no cloth on a haunt figure.** Every cost estimate that budgets for cloth simulation is
   wrong.
2. **`Transform`×169 is an upper bound on the bone count, not the bone count.** No census logs
   `SkinnedMeshRenderer.bones.Length`. The probe in §2.6 adds it.

### The census gap

**No census exists for `Hound`, `LivingBones` or `LivingSpirit`.** They resolved but were never
cast. So for three of the five available creatures we do **not** know: whether they have `Idle-Run`,
whether `RunBlend` exists, what their clips are called, how tall they are, or what is left of them
after `Strip` destroys every `ParticleSystem` and every VFX-shaded `Renderer`
(`Clone.cs:454-459`, `:497-502`). `LivingSpirit` is the one to worry about — a "spirit" is likely to
be mostly VFX, and after the strip it may render as very little. **Every design below that casts one
of those three names is labelled.**

## 1.3 Base game vs DLC — settled, with two independent proofs

**There are exactly three DLC keys** (`decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary/DLCRegistry.cs:13-27`):

| Key | Category | Name | Steam AppId |
|---|---|---|---|
| `EDLCKey.None` | — | base game | — |
| `EDLCKey.DLC1` | `DLC_JoTL` | **Jaws of the Lion** | 1809490 (`PlatformDLC.cs:11,101`) |
| `EDLCKey.DLC2` | `DLC_Solo` | **Solo Scenarios** | 1958560 (`PlatformDLC.cs:13,102`) |
| `EDLCKey.DLC3` | `DLC_4Skins` | Extra Skins — **no NPC models at all** (`DLCRegistry.cs:96-180`) | 2584170 |

**"Forgotten Circles" does not exist in this build.** Zero hits for "Forgotten", "Circles" or
"Diviner" across the whole decompiled tree. The monsters the tabletop game ships in Forgotten
Circles (Blood Horror, Blood Tumor, First of the Order, Sand Devil) are registered here under
**DLC1 / Jaws of the Lion**.

Ownership is per client: `PlatformLayer.DLC.UserInstalledDLC(EDLCKey)` →
`SteamApps.IsDlcInstalled(appid)`, and it returns `false` for everything when `SteamClient.IsValid`
is false (`PlatformDLC.cs:94-108`).

**The proof that matters is empirical.** The mod's own census prints the shipped
`BundleLoadSettings` asset, and the DLC list is **complete**: `Player.log:3583` says
`NPC BUNDLES (DLC — never cast, listed for completeness) — 48:` and 40 are printed. The 29 DLC1
entries are a perfect set-match with the 29 `ENPCModel` values in `DLCRegistry.cs:130-158`; the 11
DLC2 entries match `DLCRegistry.cs:164-175` minus only `WoundedGuard`, cut by the 40-entry cap. The
base-game list (`Player.log:3542`, `194:`) is truncated alphabetically at `npc_cultistandvictim`.

**Therefore: any npc bundle not in the DLC-48 is base game.** Verdict on every name in the brief:

| BASE GAME (safe to cast) | Evidence |
|---|---|
| `npc_cultist` | `Player.log:3582` `dlc=None` + resolved `:3499` |
| `npc_livingbones`, `npc_livingcorpse`, `npc_livingspirit`, `npc_hound` | resolved `:3499` (the resolver excludes DLC at `Roster.cs:206-210`, so a printed bracket *is* the proof) |
| `npc_boneranger` | `Player.log:3559` `dlc=None` |
| **`npc_cultistandvictim`** | `Player.log:3583` `dlc=None`; and `CultistandVictimID` is in the loaded class list, `Player.log:3536` |
| **`npc_captive01…06`** (+ `…weapon`) | `Player.log:3563-3574` `dlc=None`; `IdleCaptiveID` / `CaptiveID` in the class list, `Player.log:3526-3527` |
| `npc_blackimp`, `npc_arcanegolem`, `npc_blackunicorn`, `npc_bloatedregent`, `npc_burningavatar`, `npc_crystalaltar` | `Player.log:3555-3581` `dlc=None` |

| DLC — **NEVER CAST** | Which |
|---|---|
| `blacksludge`, `filthysludge`, `bloodhorror`, `bloodtumor`, `bloodmonstrosity`, `ratmonstrosity`, `chaosdemon`, `entropydemon`, `sanddevil`, `zealot`, `firstoftheorder`, `crowd01…08` | **DLC1 / Jaws of the Lion** (`Player.log:3584-3612`, `DLCRegistry.cs:130-158`) |
| `ghostwolf`, `spiritbear`, `spiritofxorn`, `inoxnecromancer`, `deepearth`, `songofthedeep` | **DLC2 / Solo Scenarios** (`Player.log:3613-3623`, `DLCRegistry.cs:164-175`) |

Nothing in the brief's list is UNKNOWN.

**`npc_cultistandvictim` and the six captives are the find of this investigation.** They are base
game, their monster classes are loaded, and they are tableaux rather than creatures. See §3, bench
item B1.

## 1.4 How to enumerate safely

`BundleConfigForPrefab` really does redden the log: `AssetBundleManager.cs:353` —

```csharp
Debug.LogErrorFormat("[ASSET BUNDLE MANAGER] - Failed to find bundle config for character prefab: \"{0}\"", prefabName);
```

plus `LogWarningFormat` at `:348` (malformed DLC bundle name) and `:359` (unowned DLC prefab). It is
still the only *null-returning* door — `GetBundleLoadConfig` (`:295-303`) dereferences the result at
`:298` with no null check, so `GetCharacterPrefabFromBundle` NREs rather than returning null.

**For enumeration, do not call it at all.** Read the table directly. It is a plain public field
chain with no side effects, it is install-invariant (a shipped `ScriptableObject`), and it carries
the DLC marker:

```csharp
AssetBundleManager abm = AssetBundleManager.Instance;                      // :21, plain static field
BundleLoadSettings s  = abm?.BundleLoadConfigs;                            // :23, public field
List<BundleLoadSettings.BundleLoadConfig> cfgs = s?.BundleConfigs;         // BundleLoadSettings.cs:87
//  … and NOT s.DLCBundleConfigs (:89) — that list is DLC by construction.
foreach (var cfg in cfgs) {
    if (cfg == null) continue;                                             // plain C# class, not a UnityEngine.Object
    if (cfg.BundleConfigType != …EBundleConfigType.NPC) continue;          // :26
    if (cfg.BundleDLC != DLCRegistry.EDLCKey.None) continue;               // :28 — belt and braces
    foreach (var m in cfg.AssociatedNPCModels ?? empty) …                  // :34
}
```

Three things to avoid, all proven from source:

- **Never read `cfg.AssetsBundleLoadPath`** (`BundleLoadSettings.cs:36-53`) — it calls
  `RootSaveData.DLCPackageFolder` and `Path.Combine(Application.streamingAssetsPath, …)`.
- **Never pass `null` into a `List<BundleLoadConfig>` API** — `Equals` dereferences
  `other.AssetBundleName` with no null check (`:60-63`) and `GetHashCode` NREs on a null name (`:65-68`).
- **Never call `GetBundlesNeededForRequirements`** (`:91`) — it walks `DLCBundleConfigs` and
  dereferences unguarded (`:100`, `:120`).

## 1.5 A determinism hole in the current roster, and the fix

The feature's whole guarantee is *"the same creature, in the same place, at the same second, on
every client, with zero wire bytes"*. Three things in the current code can break it, and §1.1 is the
evidence that at least one of them is live.

1. **`MonsterClassManager.Classes` is not install-invariant.** `BoneRanger` has a base-game bundle
   but was absent from the 111 loaded classes. Whatever decides that list (ruleset, campaign,
   scenario) is not the bundle table, so two clients can in principle resolve different rosters.
2. **The roster is cached across scenarios.** `Roster.Forget()` is only reached from
   `HauntFigures.ReleaseAll` (`HauntFigures.cs:386`). A client that resolved its roster in scenario
   A carries it into scenario B; a client that joined at B resolved it there. Same scenario,
   different rosters.
3. **`Roster.Pick` falls back to the next castable entry** (`Roster.cs:96-113`). That is exactly the
   construction that makes two clients show *different creatures for the same slot* when their
   rosters differ by one name. The doc comment calls this "correct behaviour and not a degradation";
   against the stated guarantee it is a divergence.

**The fix, and it is small:**

- Build the *allowed set* from `BundleConfigs` (§1.4) — install-invariant, DLC-free.
- Resolve spaced prefab names from `MonsterClassManager.Classes` as today (the addressable path
  needs the spaced name: `AssetBundleManager.cs:300` interpolates it verbatim, which is why
  `'Living Bones'` and not `LivingBones`).
- **Call `Roster.Forget()` on every style/room change**, not only on full teardown — so the roster
  always describes *this* scenario, which is the same scenario on every client.
- **Hash-index the cast, do not rotate it.** If the indexed creature is unavailable, the slot goes
  **quiet**. A quiet slot is a divergence of *nothing*; a substituted creature is a divergence of
  *content*.

## 1.6 Two bugs found in passing (not this lane's to fix, but they will bite a tester)

- **`EnvSound.cs:515` looks for a node that does not exist.** `Find(room, "Wisp")` is an exact-name
  match (`EnvSound.cs:1291`); the bake names the forest wisps `WispWisp`, `WispLantern`, `WispFar`,
  `WispEyeL`, `WispEyeR` (`"Wisp" + n`, `BuildEnvironmentRooms.cs:10362`). It resolves to null today.
  Same shape at `EnvSound.cs:1283`, which documents `Candles0`/`Candles1` where the bake writes
  `CandlesTable`/`CandlesShelf`/`CandlesCrate` (`:2934`).
- **A forced test of a figure card never shows the whole figure.** `ForceLoopSeconds` is derived from
  `Haunt.CardSeconds` — the *shader* card's length — while the figure runs `HauntEvent.Seconds`.
  Forest card 3: `CardSeconds` 0.34 s → loop floored to 2.50 s, figure 4.20 s. `Player.log:27358`
  onward shows it re-armed every 2.50 s, nine times, never finishing. Cellar card 4 is the same
  (0.70 s vs 2.60 s). The fix is one line: when `IsMine(style, card)`, use the figure's duration.
- **`EnvSound`'s cue is timed to the shader card, not to the figure.** Same root cause. Cellar 0 is
  7.6 s of cue over a 5.5 s figure; forest 3 is 0.34 s of cue over a 4.2 s figure.

---

# 2. THE ANIMATION QUESTION

The brief asked whether `AnimationClip.SampleAnimation` opens the clip library. **It does not, and it
is the wrong tool.** There is a better one, there is a free one nobody needed to ask for, and there
is a hazard in the current code that is larger than the one the idle-only rule was written against.

## 2.1 `SampleAnimation` — NO. Confidence: high.

| Question | Answer | Confidence |
|---|---|---|
| Does it run `StateMachineBehaviour`s? | **No.** SMB callbacks are defined as "invoked when a state machine evaluates this state"; `SampleAnimation` evaluates no state machine. | PROVEN-FROM-DOCS |
| Does it fire `AnimationEvent`s? | **No.** Events are dispatched by the Animator update from clips sampled between the last and current update. | STRONGLY-INFERRED (docs silent) |
| Does it work with `Animator.enabled = false`? | Probably, but no authoritative source either way. | **UNVERIFIED** |
| Does it work on a **humanoid** rig? | **No — silent no-op.** | STRONGLY-INFERRED |

The Unity 2021.3 page for `AnimationClip.SampleAnimation` contains **no** occurrence of "event",
"AnimationEvent", "Animator", "humanoid" or "generic", and no code example. Its entire performance
statement is *"It is recommended to use the Animation interface instead for performance reasons"* —
and "the Animation interface" is the **legacy `Animation` component**, which is a strong hint about
where this API belongs. (The "not efficient" wording in the brief is not on the 2021.3 page.)

The humanoid case is the killer. The canonical report is exactly our setup — humanoid clip,
humanoid avatar, no controller, using Playables — and *"nothing gets sampled on that gameobject"*.
The editor-only workaround (`AnimationMode.SampleAnimationClip`) does not exist in a player build.
The failure mode is not a T-pose but a **silent hold of the previous pose**, which at 13 m in the
dark is precisely the kind of wrong nobody catches.

**Are these rigs humanoid?** Almost certainly **generic**, on four converging lines, all
STRONGLY-INFERRED and none proven:

1. **Zero humanoid API usage anywhere in the game.** `GetBoneTransform`, `HumanBodyBones`,
   `HumanTrait`, `AvatarMask`, `MatchTarget`, `SetIKPosition`, `isHuman` — nothing, across the whole
   decompiled tree.
2. **Bones are addressed by string path.** `CharacterManager.cs:57` —
   `public string TargetBone = "C_spineChestHook_LOC"`, resolved with `FindInChildren`.
   `SpawnObjectAtBone_SMB` uses `animator.transform.Find(hook)`. `_LOC` is a Maya locator suffix.
3. **The roster is not all bipeds.** `Hound` is a quadruped and `LivingSpirit` a floating form, yet
   both share the state table and the `RunBlend` parameter with the Cultist. One pipeline spanning a
   dog and a robed man is a generic pipeline.
4. **No retargeting, IK or masks are used anywhere.**

So `SampleAnimation` is *possibly* viable. It is still last place, because everything it buys is
bought better below.

## 2.2 The real lever: a **null controller** plus Playables

```csharp
animator.runtimeAnimatorController = null;          // ← the whole safety property
var graph = PlayableGraph.Create("GhvrHaunt");
graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
var clip   = AnimationClipPlayable.Create(graph, someClipFromTheOriginalController);
output.SetSourcePlayable(clip);
graph.Play();
```

Every type in that snippet is present in the shipped assemblies. Verified by direct byte search of
the game's own `ressources/Managed/UnityEngine.AnimationModule.dll` and `UnityEngine.CoreModule.dll`:
`AnimationPlayableOutput`, `AnimationClipPlayable`, `AnimationPlayableUtilities`,
`SetSourcePlayable`, `PlayableGraph`, `DirectorUpdateMode`, `PlayableOutput` — all present.
(`GetBehaviours` and `SampleAnimation` are present too.)

**Why this dominates every alternative.** With `runtimeAnimatorController = null`, the SMBs are not
dormant — **they are never instantiated.** No `Awake`, no `OnStateEnter`, no subscription, no
coroutine. That is a structural guarantee rather than a behavioural one, and for an object whose
entire licence to exist is "it can never touch the game", structural is the only kind worth having.

It also: keeps retargeting working whatever the rig turns out to be (Playables is the layer
retargeting is implemented on); keeps the job-system evaluation path rather than dragging curve
evaluation onto the main thread; and replaces the `RunBlend` blend tree with an
`AnimationMixerPlayable` whose weight you set directly, which is strictly better than driving a
float parameter.

**Its one real hazard is a leak.** `graph.Destroy()` must be wired into `Clone.Release` beside the
prefab handle. A leaked `PlayableGraph` outlives the scene.

**Clips are shared assets. Never write to them.** `MF.GameObjectAnimatorInvalidateEvents`
(`decompiled/GH.Runtime/MF.cs:111-132`) mutates `animationClip.events` in place, which proves the
clips reachable through `rac.animationClips` are the game's own live assets. Stripping events off
them would break real monsters for the rest of the session.

**Also note:** `MF.GetGameObjectAnimator` returns the first animator **with a non-null
`runtimeAnimatorController`** (`MF.cs:135-146`). Null the controller and that helper returns null —
grab the `Animator` with `GetComponentsInChildren<Animator>(true)` instead, and grab
`rac.animationClips` **before** nulling.

### The alternatives, briefly, and why each loses

- **`Animator.Play` on a state believed SMB-free** (today's design). Works; cannot be *proven* safe,
  because which state carries which SMB lives in the controller asset. The probe converts this from
  faith to fact, and if `Idle-Run` comes back clean, staying put is defensible.
- **`AnimatorOverrideController`** — does not help. It overrides *clips*; the state graph and every
  SMB on it survive. PROVEN-FROM-DOCS.
- **Building a controller at runtime** — impossible. `UnityEditor.Animations.AnimatorController` is
  editor-only.
- **Shipping an SMB-free controller in the mod's own bundle** — possible, but it would have to
  hardcode each creature's state graph. Playables gets there with no asset at all.
- **Legacy `Animation` with `clip.legacy = true`** — **dead.** Setting `legacy` at runtime in a
  player build is not supported (curves are already in runtime format); it works in the Editor and
  silently fails in a build. Do not spend a build on it.

## 2.3 The free lever: **freeze the animator**

Nothing above is needed to hold a body in a single pose. After the figure is live:

```csharp
animator.Play(stateHash, 0, someNormalizedTime);   // pick a pose that is not the bind pose
animator.Update(0f);                               // force one evaluation
animator.enabled = false;                          // and it never moves again
```

A frozen body is a dead body. Three of the four events below want exactly that — a hanged figure
does not breathe, a corpse does not breathe — and it costs nothing, needs no new mechanism, and
removes the per-frame animator cost entirely. The mod already does frozen-pose work in
`FigureOverlay.BuildFrozenGhost`.

Caveat: freezing does not prevent the SMBs on the *entered* state from having already fired. Under
§2.2's null controller it does, because there is no state to enter.

## 2.4 The hazard nobody priced: the SMB surface is much larger than one class

`HauntFigures.Clone.cs:90-97` justifies the idle-only rule with one SMB,
`SetProgressModifierCallbackSMB`. **The game ships 29 `StateMachineBehaviour` subclasses**, and the
list is far worse than the doc suggests. From `decompiled/GH.Runtime/`:

| SMB | What its callback does |
|---|---|
| **`AnimationOffsetSMB`** | `:20` — `anim.Play(…, randomized ? (float)SharedClient.GlobalRNG.NextDouble() : normalizedTime)`. **Draws from the shared seeded RNG stream.** |
| **`DelayedDropSMB`** | `:107-134` — constructs `CObjectGoldPile` / `CObjectObstacle` / `CObjectTrap` onto `ClientTileArray`. **Real scenario-state mutation.** |
| **`SummonSMB`** | `:49-80` — `SummonCharacters()` walks `ScenarioManager.Scenario.Enemies` and summons them. |
| **`UnlockDoorSMB`** | `:18-25` — `Choreographer.OpenDoor(…)` on real scenario doors. |
| **`CollectLootSMB`** | `:71-87` — `ObjectPool.Spawn`, gold arithmetic, `UIManager.Instance.OnGoldValueChanged()`. |
| **`DelayedDestroySMB`**, **`DelayedDeactivatePropAnimSMB`** | static in-progress lists; `LevelEditorController.RemoveProp`; tile mutation; dereference `characterActor.ArrayIndex` — **null on a decorative clone**, so an NRE thrown inside Unity's SMB dispatch, i.e. *outside* `HauntFigures.Tick`'s try/catch. |
| **`SetProgressModifierCallbackSMB`** | `TimeManager.FreezeTime()`, `AttackModBar.s_AttackModifierBarFlowCanBegin`. |
| `CastEffectsSMB`, `FireEquippedProjectileSMB`, `FireSpawnedProjectileSMB`, `Jump*_SMB` | `ObjectPool.Spawn`, static events, `AudioController.Play`. |
| `IdleSMB` | `Awake` subscribes to `SaveData.Instance.Global.GameSpeedChanged`; `OnStateEnter/Exit` write the clone's own `animator.speed`. Harmless in itself. |

**`SharedClient.GlobalRNG` is a seeded shared stream**
(`decompiled/SharedLibrary/SharedLibrary.Client/SharedClient.cs:26,34` —
`GlobalRNG = new Random(SeedYML.LoadedYML.Seed)`), and it feeds
`ScenarioState.cs:1491` (`Seed = … SharedClient.GlobalRNG.Next()`) as well as `CActor.cs:1836`,
`CAbilityCreate.cs:231` and `CharacterManager.cs:342-343`.

`AnimationOffsetSMB` is the SMB you attach to an **idle** state, to desynchronise a crowd of idling
monsters. If it sits on `Idle-Run`, **the shipped implementation already draws from the shared RNG
once per apparition spawn, on one client only.**

**Severity: I rate it LOW but unassessed, and I will not call it zero.** The mitigating evidence is
strong: the base game *itself* advances `GlobalRNG` from purely cosmetic, frame-timing-dependent
code — `LivingCorpseIdleSelect.cs:10` rolls it for an idle twitch, `CharacterManager.cs:342-343`
rolls it for hit-spark scatter. A stream that idle twitches perturb cannot be lockstep-critical. But
"probably not lockstep-critical" is not the standard this feature holds itself to elsewhere.

**Which states carry which SMBs is not visible from decompiled code.** It lives in the controller
asset. Only a runtime probe answers it — and `Animator.GetBehaviours<T>(int fullPathHash, int
layerIndex)` is the API that does, per state. It is present in the shipped
`UnityEngine.AnimationModule.dll` (verified by byte search).

**Consequence for this design.** Every event below is specified so that its *primary* form needs
only `Idle-Run` (or a frozen pose), i.e. nothing beyond what ships today. Where a clip would make an
event better, that is called out as an **upgrade that requires §2.2**, and it is never load-bearing.

## 2.5 The principle that makes the clip library optional

**The mod owns the travel; the clip only has to breathe.**

That is already how all four proven events work: `Drive` writes the anchor's `localPosition` from
the shared clock and the animator only supplies the gait (`HauntFigures.Events.cs:411-426`). Every
motion in §3 — a rise through the floor, a lean out from behind a trunk, a slow turn on a rope — is
a transform the mod writes. None of them needs a clip that does not already exist. That is why this
design does **not** depend on the animation question being settled, and why it should not be blocked
on it.

## 2.6 The one-build probe that settles everything

Gated behind a config flag, on the first figure of the session. Four phases, ~2 s each. **Set
`animator.fireEvents = true` for the whole probe** — `Strip` has destroyed every MonoBehaviour except
`CharacterManager`, so any event that fires hits nothing and Unity emits its own
`"… AnimationEvent has no receiver!"` warning; with stack traces restored (ModBuild 136+) those are
attributable. Restore `fireEvents = false` at the end.

```
PROBE RIG:  avatar=<name|NULL> isHuman=<bool> isValid=<bool>
            bones=<smr.bones.Length> transforms=<n> layers=<animator.layerCount>
            height=<CharacterManager.Height> defaultState=<name of state entered on activation>

PROBE SMB PRESENT (controller-wide): <type names from animator.GetBehaviours<StateMachineBehaviour>()>
PROBE SMB ON STATE '<s>': <types from animator.GetBehaviours<StateMachineBehaviour>(hash, 0)>
        …for every name in Roster.AllStates

PROBE SMB FIRED: <type>.<callback> phase=<A|B|C|D>     ← Harmony prefix on every SMB subclass,
                                                          filtered to the haunt clone's animator

PROBE PHASE A: control — controller intact, Animator.Play("Idle-Run")
PROBE PHASE B: animator.enabled=false; clip.SampleAnimation(go, t)      clip=<Walk>
PROBE PHASE C: runtimeAnimatorController=null; AnimationClipPlayable    clip=<Walk>
PROBE PHASE D: as C, AnimationMixerPlayable(Idle, Walk) at weight 0.5

PROBE PHASE <p>: moved=<max deg deviation of the deepest bone from the phase's first pose>
                 smbFired=<n>  eventWarnings=<n>  cost=<ms/frame over n frames>
```

| Line | What it decides |
|---|---|
| `isHuman=False` | generic → `SampleAnimation` at least possible. `True` → it is dead and needs no further test. |
| `PROBE SMB ON STATE 'Idle-Run'` empty | the shipped idle-only rule is actually safe; `AnimationOffsetSMB` never fires. Non-empty → §2.4 is live and §2.2 becomes required, not optional. |
| `PHASE B: moved=0.00deg` | the humanoid silent no-op. |
| `PHASE C: moved≠0, smbFired=0` | **the thesis**: Playables animates with a null controller and no SMB can run. Closes the question in favour of §2.2. |
| `eventWarnings` per phase | settles the AnimationEvent question empirically. |
| `cost=` A vs B vs C | main-thread comparison at the real bone count, on the real headset. |
| `PHASE D: moved≠0` | `AnimationMixerPlayable` can replace the `RunBlend` blend tree. |

**Phase C is the load-bearing one.** Everything else is corroboration.

**Cost reference from the existing build:** `Env.HauntFigures` appears in exactly one spike frame in
the whole 5.7 MB log — `Player.log:3634`, `16.72 ms`, on a **spawn** frame (`HauntFig.Spawns` total 1
at that point). `Env.HauntFigDrive` never crosses the reporting threshold at all. So the per-frame
cost of a live figure today is under a millisecond and the entire budget is the spawn. Any animation
scheme must be judged against that: spawn is the problem, drive is not.

---

# 3. THE EVENTS

Conventions.

- All coordinates are **room-local metres**, the frame `HauntEvent.From`/`To` already uses. The room
  root is placed once as a yaw + a translation + a **uniform** scale derived from the board
  (`SkyAlternative.cs:1439-1458`, `roomScale = 4.5·boardExtent / authoredPlayExtent`), so a figure
  parented to it is automatically at the room's own scale at every board zoom, with no scale
  tracking. **Author in room-local metres; never compute a world position.**
- **Both rooms are authored in real metres around the world origin, floor at y = 0**, under a node
  named `RoomGeo`. Cellar half-extents `hw = 5.25`, `hd = 4.50`, ceiling `CH = 3.30`
  (`BuildEnvironmentRooms.cs:1858`). Forest ground disc `FR = 30`, clearing radius `ClearR = 5.4`
  (`:7909`, `:7912`).
- The player and the board are at the **origin**. PlaySpace radius is **3.25 m** in the cellar and
  **4.50 m** in the forest (`:54-55`). Nothing below is nearer than **5.4 m** to the room centre.
- Forest bearings use the bake's compass, `p = (sin(θ)·r, cos(θ)·r)` (`:10427-10432`). The moon is
  at **azimuth 40°, altitude 40°** (`BuildEnvironments.cs:97`) and is the only light the wood has.
- **There are no colliders anywhere in either room**, by contract (`BuildEnvironmentRooms.cs:8-9`)
  and by runtime strip (`SkyAlternative.cs:1163`, `:1192-1197`). There is no raycast, no physics and
  no ground query; the forest's ground height is the `ForestY` mirror at `HauntFigures.Events.cs:528`
  and nothing else. Every anchor below is therefore *computed*, never *measured*.
- The figure hangs under a node named **`Haunt<card>`** created per event
  (`HauntFigures.Events.cs:305`); `EnvSound.HauntPosition` finds it by that exact name and plays the
  card's cue from it. Keep the convention — it is the only one the other two lanes understand.

**No user-facing strings are proposed.** `HauntEvent.Name` is documented as English, for the log
only (`HauntFigures.Events.cs:51-52`). The German titles below are internal names for review and are
quoted separately in §3.6.

## 3.0 What the machinery must grow

Four small extensions. Each is named at the events that need it, and none of them is speculative
architecture — each one exists because a specific event cannot be expressed without it.

| # | Extension | Needed by | Size |
|---|---|---|---|
| **X1** | **Return path.** `PathAt` currently lerps `From → To` monotonically (`Events.cs:457-465`). Add a per-event `Return` flag + `HoldFraction` so `u` drives a plateau'd triangle: out over `[0, a]`, still over `[a, 1-a]`, back over `[1-a, 1]`. | E1 | ~8 lines |
| **X2** | **Lean.** A per-event `LeanAxis` (room-local), `LeanAngle` (radians) and `LeanPivotY`, applied on the same plateau'd triangle as X1 and composed with `_standRot`. This is the same shape the bake already uses (`rotAxis`/`rotAngle`/`pivotY`, e.g. `BuildEnvironmentRooms.cs:10504`). | E2 | ~10 lines |
| **X3** | **Freeze.** A per-event `FreezeAtNormalizedTime` (or −1 for "keep animating"): `Play(state, 0, t)`, `Update(0f)`, `animator.enabled = false`. See §2.3. | E4, B1, B3 | ~5 lines |
| **X4** | **Orbit yaw.** A per-event `SpinDegrees` applied about the room-local vertical through the anchor over the event, composed with `_standRot`. The bake's own Hang card does exactly this (`rotAxis = Vector3.up, rotAngle = 0.42f`, `:10648`). | E4 | ~5 lines |

One rule from the bake's own gate is worth carrying over even though it does not apply to runtime
figures. `AssertHauntCards` (`BuildEnvironmentRooms.cs:5118`) checks **both ends of any travel**
against the play space, and requires an apparition's facing to dot **≥ 0.55 toward the room centre**.
The second half is a design rule, not a safety one — a figure turned away from the board is a figure
the player sees the back of — and every event below either faces the clearing or is deliberately,
explicitly turned away with a reason given.

Two of the four also want a **bake-side** change; both are called out at their event and both are
one line.

---

## 3.1 E1 — CELLAR CARD 2 · *"Was unter den Fliesen liegt"*

**Replaces** the head lying on the flagstones (`BuildEnvironmentRooms.cs:7671-7692`).

**Room** Cellar.

**The place, and how it was derived rather than chosen.** The figure comes **up through the cellar
floor**, in the far south-west corner, **directly behind Barrel0**.

- Barrel0 is at room-local `(-3.70, 0, -3.20)` — `BuildEnvironmentRooms.cs:2811`, verbatim:
  `Prop(root, "Barrel0", "wine_barrel_01", "wine_barrel_01", new Vector3(-3.7f, 0, -3.2f), 15, 1f, "C");`
  It is 4.89 m from the room centre.
- The event sits on the ray from the room origin through Barrel0, 0.6 m further out:
  **`(-4.35, y, -3.35)`**, **5.49 m** from centre. Barrel0 then lies **0.28 m off that sightline at
  4.88 m**, with the figure at 5.49 m — i.e. the barrel is squarely between the player and the
  figure's waist. (Derivation: unit vector `(-0.7924, -0.6101)`; Barrel0's projection onto it is
  4.884 m; its perpendicular offset is 0.278 m. The ray differs from the ray *through* Barrel0 by
  0.5°.)
- Clearances: **0.90 m** from the west wall (`x = -5.25`), **1.15 m** from the south wall
  (`z = -4.50`), **0.78 m** from the Bucket at `(-4.00, 0, -4.05)` (`:2846`), **0.67 m** from
  Barrel0, and **0.55 m** from the permanent rat eyeshines — see below. The wall skirt is ≤0.36 m
  deep and the moss cushions ≤0.38 m in from the face (`:3322`, `:7075-7081`), so both are cleared.
- **2.24 m outside the 3.25 m PlaySpace radius.**
- It is the darkest corner in the room: the moonbeam lands at `(-3.27, ~, 2.22)`
  (`BuildEnvironmentRooms.cs:7676-7679`) — the opposite side — and the nearest light is the **crate
  candle** at `(-1.55, crateTop + 0.20, -3.95)` with a **3.00 m range** (`:2979`), which puts the
  figure **2.86 m** away, i.e. just inside it. That is the one thing lighting this event, it comes
  from the side, and it flickers at rate 1.19 / phase 4.4 (`:309-310`). Good: a rim, not a wash.

**⚠ The permanent rat eyeshines are 0.55 m away and this must be checked in a preview render.**
`EyeL`/`EyeR` sit at `(-4.86, 0.115, -3.55)` ± `(0.028, 0, -0.010)`, scale 0.021, blinking on a 4.3 s
period (`BuildEnvironmentRooms.cs:7377-7380`). They are **11.5 cm off the floor**, i.e. far below the
`y ≈ 0.76 m` line where the barrel stops cropping, so from a 1.6 m eye at the room centre they are
already behind Barrel0 and the two never share a pixel. **But that is a computed claim, not a
measured one** — and if it holds it also means the cellar eyeshines are currently invisible from the
play position, which is a separate finding the environment lane may want.

**The occlusion is the floor, and that is the whole event.** The cellar floor at `y = 0` is opaque
geometry that writes depth. A figure below it is *not* faded out, it is *behind* something. Nothing
about this event is an effect.

**Motion (the mod drives all of it).**
`From = (-4.35, -2.05, -3.35)` → `To = (-4.35, -0.50, -3.35)`, and **back** (X1).
A 1.55 m rise. With the eye at ~1.6 m and the barrel top at ~0.85 m at 4.88 m, everything below
`y ≈ 0.76` at the figure's distance is behind the barrel; a figure whose crown stops at
`y ≈ 1.45` therefore shows **its chest and head and nothing else**, ever — a 0.69 m band.

**Creature** `LivingCorpse` (first — it is the one that already looks half-buried, it is
proven-resolvable, and it is the only creature with a census that also has a `Spawn` clip), then
`LivingBones`, then `Cultist`.

**Animation** `Idle-Run` at `RunBlend = 0`. **Upgrade (needs §2.2):** play `LivingCorpse_Spawn`
(**2.83 s**) once across the rise and then hold the last pose. Do **not** play the `Summoned` state —
`SummonSMB.OnStateExit` calls `SummonCharacters()` (`SummonSMB.cs:49`).

**Envelope** `reveal 0.15, hold 8.00, fade 0.15` = 8.30 s. The dissolve is a safety net for an
oblique sightline only; the presence is 1 for essentially the whole event. Path plateau `a = 0.40`:
rise 3.3 s, still 1.7 s, sink 3.3 s.

**Visible** roughly 4 s of 8.3, and never more than chest-up.

**Why it is frightening, in one sentence.** Something comes up through the floor of the room you are
standing in, stops when its shoulders clear the barrel, and goes back down.

**Cost** one clone, one prefab load — unchanged from today. Needs **X1**.

**Risks** the barrel's real silhouette is a photoscan and its radius is not typed anywhere; the
implementation should confirm the occlusion with `Renderer.bounds` on `Barrel0` at spawn rather than
trusting the 0.24 m figure. If the barrel is narrower than assumed, move the anchor along the same
ray, not sideways.

---

## 3.2 E2 — FOREST CARD 0 · *"Wer sich hinter dem Baum hervorlehnt"*

**Replaces** the head easing out from behind a trunk (`BuildEnvironmentRooms.cs:10495-10509`).

**Room** Night forest.

**The place.** The trunk the bake already picks for this card:
`var faceTree = HauntPickTree(250f, 9.5f, 13.0f, 0.24f);` (`:10410`) →
`var facePos = TrunkAt(faceTree, 1.60f);` (`:10411`),
`float faceR = HauntTrunkRadius(faceTree, 1.60f);` (`:10412`),
`var faceOut = new Vector3(-facePos.x, 0f, -facePos.z).normalized;` (`:10413`).

**The runtime cannot compute that**, and this is the one hard dependency in the whole design. It is a
*search* over the generated forest, at 9.5–13.0 m on bearing 250°, for a trunk with at least 0.24 m
of base radius. Two ways to get it:

- **(a) RECOMMENDED — one line in the bake.** Emit an empty `GameObject` named `HauntTree_Face` at
  `facePos`, oriented along `faceOut`, as a child of the forest root. The runtime finds it by name,
  reads its position and forward, and is immune to a reseed. One line, zero mirror debt.
- **(b) Mirror `ForestTrees()` (`:7983-8021`), `TrunkAt` (`:8025`), `HauntTrunkRadius` (`:8048`) and
  `HauntPickTree` (`:8075`) into `HauntFigures.Events.cs` alongside the existing `ForestY` mirror.**
  It is the same class of debt the `Ground` block already documents (`Events.cs:484-511`) and it
  would give the runtime the whole wood — 106 trunks in four bands, with the trunks already pushed
  clear of the path — which is worth having for occlusion-aware placement generally. But it is four
  more mirrors, `HauntPickTree` is a *search* whose result changes whenever a band is reseeded, and
  the bake lane is editing this file right now.

**Take (a).** ⚠ It needs the bake lane, so co-ordinate.

**The figure and the motion.** A whole body stands **behind** the trunk, offset sideways so that at
rest its head is ~0.05 m *inside* the trunk's silhouette:

```
side = normalize(cross(up, faceOut))
at   = facePos - faceOut * 0.30 + side * (faceR - 0.05)
```

Then it **leans** (X2): about the horizontal axis `faceOut`, pivot at its own feet
(`LeanPivotY = 0`), `LeanAngle = 12°`. A 2.05 m figure leaning 12° moves its head
`2.05·sin12° ≈ 0.43 m` sideways — enough that **about half the head and one shoulder clear the
bark**, and no more. That is what *hervorgucken* means, and it is the picture the bake's own comment
argues for (`:10414-10420`: "The first bake cleared the whole head, which reads as a mask hanging
beside a tree").

**This is the answer to "a sub-decimetre translation that no walk cycle can express"**
(`HauntFigures.cs:90-92`). It is not a translation at all. It is a body leaning, with its feet
planted, driven by the mod — and an idle clip is exactly the right thing to be playing underneath it.

**Occlusion** the trunk itself: real geometry, ≥0.24 m of base radius at 1.60 m, at 9.5–13.0 m.

**Creature** `Cultist` (hooded, robed, upright, proven-resolvable, and the closest thing in the
roster to a face you cannot quite see), then `LivingSpirit` ⚠ *no census*, then `LivingCorpse`.

**Height** 2.05 m. Slightly too tall and not grotesquely so — behind a tree there is no scale
reference but the ground, so the wrongness registers without being measurable.

**Animation** `Idle-Run` at `RunBlend = 0`. Nothing else. No upgrade needed and none proposed —
this event is *better* with a body that is only breathing.

**Envelope** `reveal 1.60, hold 5.40, fade 0.00` = 7.00 s. The dissolve-up happens while the figure
is still entirely behind the trunk, so it is never seen to arrive; the vanish is instant and happens
after it has leaned back, so it is never seen to leave. Lean schedule on the same clock: out over
`t ∈ [1.8, 3.6]`, held to 5.0, back by 6.4, gone at 7.0.

*(Note: `Haunt.CardSeconds(SwampNight, 0)` is 8.6 s. Either keep 3.4/2.4/2.8 for the figure so the
mirror and `EnvSound`'s cue stay in step, or fix the mirror. See §1.6 — the cue timing is already
wrong for two figure cards and this is the moment to fix it once.)*

**Visible** ~3.2 s, and never more than a head and a shoulder.

**Why it is frightening, in one sentence.** It is not a face floating beside a tree — it is a body
that leaned out to look at you and then leaned back.

**Cost** one clone. Needs **X2** and the bake-side anchor.

---

## 3.3 E3 — FOREST CARD 4 · *"Das Tier an der Lichtungskante"*

**Replaces** the 3.05 m hunched mass (`BuildEnvironmentRooms.cs:10608-10625`).

**Room** Night forest.

**The place.** The bake puts the Loom at `OnGround(138f, 10.5f, 0f)` (`:10450`), using its own
compass convention `x = sin(bearing)·r, z = cos(bearing)·r` (`:10427-10432`). **Move it out to
14.0 m on the same bearing**:

```
x = sin(138°) · 14.0 = +9.368
z = cos(138°) · 14.0 = -10.404
y = Ground.ForestY(9.368, -10.404)       ← the runtime already has this mirror, Events.cs:528
```

Three reasons for 14.0 rather than 10.5, and all three come out of the bake's own numbers:

1. At 10.5 m the bake **guarantees** no trunk is within 0.55 m (`:10676-10679`) — i.e. it guarantees
   the thing is *not* occluded. That is the opposite of what this design wants. At 14.0 m it sits
   inside the **second** trunk band (10.0–15.5 m, 22 trunks of 0.18–0.32 m base radius, `:7989`)
   with the **first** band (6.2–10.0 m, 16 trunks of 0.23–0.40 m, `:7988`) between it and the player.
2. 14.0 m is well past the knee of the ground-darkness curve
   (`SmoothStep(1, 0.015, InverseLerp(5.2, 11.5, r))`, `:9297`), so the ground under it is
   essentially black.
3. **It has to clear the permanent eyeshine pair.** `WispEyeL`/`WispEyeR` stand at
   `eyeDir·12.5 + (0, 1.55, 0)` with `eyeDir = (sin 2.35, 0, cos 2.35)` — i.e. **≈(8.894, 1.55,
   −8.784)**, bearing 134.6° at 12.5 m (`BuildEnvironmentRooms.cs:10373-10378`). Bearing 138° at
   12.5 m would have put the animal **0.73 m** from them. At 14.0 m the separation is **1.69 m**,
   about 6.9° apart in the view — visibly two different things.

**9.5 m outside the 4.50 m PlaySpace radius.**

Two other fixed features are nearby and both are benign: `WispLantern`, an orange glow of radius
0.75 at `(9.20, 1.45, -7.40)` (`:10367`), is **3.01 m** away and gives the animal a warm edge from
behind on the moon side; and `FireSnag0`/`FireSnag1` are chosen by search among dead band-0 trunks
furthest from the moon bearing (`:6714-6728`) and are only visible with Fire up.

**A variant worth one preview render before deciding.** Putting the animal at **12.5 m on bearing
134.6°** would place it exactly where the eyeshines already are, so that on the one slot in eleven
that this card fires, the pair of eyes the player has been walking past all evening turns out to
belong to something. That is a better idea than the one above and a riskier one — the eyes are fixed
at `y = 1.55` and do not move with the figure, so the alignment has to be right in the vertical too.
Render both.

**The figure.** `Hound`, scaled to **2.30 m**, standing **broadside** — `Face = cross(up,
ToClearing(at))`, so its body lies across the sightline and you see its length rather than its front.
It does not move at all.

**Why a dog and not a bigger man.** Everything else in that wood is a tree or a person. The Loom's
brief was "person-shaped and far too big", and the honest read of the last round's failure is that a
person-shaped thing at any size still reads as a person. A quadruped the size of a bear, side-on,
motionless, at 14 m, is legible in one glance as *not a person* — which is the thing the card was
actually reaching for.

**Animation** `Idle-Run` at `RunBlend = 0`. **Let it breathe** — an animal that breathes is alive,
and this is the one event of the four where being alive is the point. Do **not** freeze it.

**Envelope** `reveal 4.20, hold 2.60, fade 0.00` = 6.80 s — **the bake's own Loom envelope**
(`:10617`), so `Haunt.CardSeconds(SwampNight, 4) = 6.8` (`Haunt.Schedule.cs:249`) stays correct and
`EnvSound`'s cue keeps its timing for free.

**Visible** ~4 s, in pieces, between trunks.

**Why it is frightening, in one sentence.** It is a dog the size of a bear, it has not moved, and it
is facing the clearing.

**Also true of E3 and of nothing else in the set: it needs no bake change and no extension.** If one
event ships this round, ship this one.

**Cost** the cheapest of the four: one coordinate, one scale, one facing, zero extensions. **Build it
first** — it proves the pattern on new ground before anything harder is attempted.

**Risks — and they are the largest in the set.** `Hound` has **no census**. Unknown: whether it has
an `Idle-Run` state, what `CharacterManager.Height` means for a quadruped (if it is the standing
height, 2.30 m is right; if it is a bounding-box diagonal, the scale will be wrong), and what
survives `Strip`. **Fallbacks, in order:** `LivingCorpse` at 3.05 m with its back turned (the bake's
own reading of this card), then `Cultist` at 3.05 m. Both are proven-resolvable and both have a
census. If the probe in §2.6 is run on a Hound first, all three unknowns close in one build.

---

## 3.4 E4 — FOREST CARD 5 · *"Der Strick, und was daran hängt"*

**Replaces** the hanged figure (`BuildEnvironmentRooms.cs:10643-10656`). **This is the one the user
asked for by name** — *"dass man jemanden/eine Silhouette erkennt von jemandem der sich erhängt hat
an einem Baum"* — and there is **no hang animation anywhere in the roster.** What follows is the
closest honest thing the assets can do, in two parts.

### Part (a) — the rope becomes permanent scenery. ⚠ Bake-side, one change.

The bake already builds a real rope: 12 mm of hemp from the branch to the neck, with the sag a rope
under a body has (`:10719-10729`). Today it exists only while the card runs.

**Make it permanent, and make it empty.** A noose hanging from the branch at

```
hangAt   = OnGround(96f, 9.4f, 0f)                                    // :10451
         → x = sin(96°)·9.4 = +9.348,  z = cos(96°)·9.4 = -0.983
branchAt = hangAt + up·2.86 + ToClearing(hangAt)·(-0.30)              // :10455-10456
```

is there every time the player looks at that quarter of the wood, in every scenario, forever. It is
not an event, it never fades, it never appears. It is furniture.

**This is the load-bearing idea of the whole event.** A noose in a wood is the most legible horror
object available to any asset set, it needs no animation, it is unmistakable at 9.4 m in the dark,
and — crucially — **the player stops seeing it.** That is what makes part (b) work.

### Part (b) — one evening in eleven, it is not empty.

On the card's slot, a figure is suspended under it:

- Position `at = (9.348, ForestY(9.348, -0.983) + 0.29, -0.983)` — feet **0.29 m clear of the
  ground**, the bake's own number (`:10637`).
- Height **1.72 m** (the bake's own, `:10647`). Crown at ~2.01 m; `branchAt` at 2.86 m; the rope
  covers the 0.85 m between. That matches the bake's own neck attachment, which puts the neck at
  `c.at + up·(height·0.86)` (`:10727`).
- **FROZEN** (X3): `Play(state, 0, 0.35f)`, `Update(0f)`, `animator.enabled = false`. **A hanged
  body does not breathe**, and the idle's weight-shift is the single thing that would give this away.
  This is why §2.3 matters more to this design than §2.2 does.
- **Turning** (X4): `SpinDegrees = 24°` over the event, about the room-local vertical through the
  anchor — the bake's own `rotAxis = Vector3.up, rotAngle = 0.42f` (`:10648`), which is 24.1°. A
  hanged body turns; that is the detail that says it was put there, and recently.

**Clearances** at `(9.348, ·, -0.983)`: **9.4 m** from the room centre, i.e. 4.9 m outside the
PlaySpace radius; **1.99 m** from `Rocks1` at `(7.40, ·, -1.40)` (`:10322`); **3.53 m** from `Shrub0`
(`:10329`); **6.42 m** from `WispLantern` (`:10367`); **6.46 m** from `Log1` (`:10242`). Nothing is
in the way and nothing is close enough to read as touching.

**Creature** `LivingBones` (⚠ *no census*) — a skeleton on a rope, and the one creature for which a
frozen pose is not a lie. Then `LivingCorpse`, then `Cultist`. All three are proven-resolvable.

**Occlusion** bearing 96° at 9.4 m sits in the outer half of the first trunk band, and the bake's
clearance assert guarantees ≥0.55 m from any trunk — so it is **not** hidden. Accept that: the rope
has to be seen for the event to mean anything. What hides it instead is darkness and stillness. It is
the only event in the set that trades occlusion for legibility, and it does so deliberately.

**Envelope** `reveal 2.60, hold 2.00, fade 2.20` = 6.80 s — matching
`Haunt.CardSeconds(SwampNight, 5) = 6.8` (`Haunt.Schedule.cs:250`) so the mirror and the cue stay
correct.

**Visible** all 6.8 s, and it never does anything except turn.

**Why it is frightening, in one sentence.** The rope is always there and you stopped seeing it.

**Cost** one clone plus one permanent bake mesh that already exists. Needs **X3**, **X4**, and the
bake-side move.

**Upgrade (needs §2.2), and it is the strongest use of the clip library in the whole design.**
Instead of a frozen body under the noose, a figure **stands** under it, and then
`Cultist_OnDeath_v001` (**1.53 s**) — or `LivingCorpse_OnDeath_v001` (**3.93 s**) — plays once and
the body drops. Under no circumstances play the `Death` **state**: `DelayedDropSMB` creates real gold
piles, obstacles and traps on real tiles (`DelayedDropSMB.cs:107-134`), and `DeathDissolveSMB` and
`DelayedDestroySMB` are on that branch too. The clip, through a null controller, is safe; the state
is not.

**Honesty note.** This is a hanging told sideways: a rope, a tree, a body that is off the ground. It
is not a body hanged by the neck with a hang animation, and nothing in this asset set can be. See §5.

---

## 3.5 Ranking

The lane will not build all of these at once. Two orders, because they differ:

**Build order** — cheapest first, so the pattern is proven on easy ground:

| # | Event | Extensions | Bake change | Cast confidence |
|---|---|---|---|---|
| 1 | **E3** Das Tier | none | none | ⚠ low (`Hound` uncensused) — but the fallbacks are proven |
| 2 | **E1** Was unter den Fliesen liegt | X1 | none | ✅ high (`LivingCorpse` censused) |
| 3 | **E2** Wer sich hervorlehnt | X2 | ⚠ one line | ✅ high (`Cultist` censused) |
| 4 | **E4** Der Strick | X3, X4 | ⚠ one move | ⚠ medium (`LivingBones` uncensused) |

**Artistic order** — best first, if only two get built:

1. **E4** — the user asked for it by name and it is the only one that leaves something behind when
   it is over.
2. **E2** — the strongest single picture, and the one that needs the least new anything.
3. **E1** — the only event in either room that happens *inside* the room the player is standing in.
4. **E3** — the cheapest and the least surprising.

## 3.6 The German names, quoted for review

These are **internal** names only. `HauntEvent.Name` is documented English-for-the-log
(`Events.cs:51-52`), the player never sees it, and this design proposes **no user-facing German
string of any kind**. Quoted here so the user can rule on the *ideas* by their names:

> - „Was unter den Fliesen liegt" — cellar 2
> - „Wer sich hinter dem Baum hervorlehnt" — forest 0
> - „Das Tier an der Lichtungskante" — forest 4
> - „Der Strick, und was daran hängt" — forest 5
> - „Der Kultist und sein Opfer" — bench B1
> - „Die Beschwörung" — bench B2
> - „Der Gefangene" — bench B3

## 3.7 The bench — three events the roster affords that nothing currently uses

Not replacements. Offered because §1.3 turned up material the existing design never knew was there.

### B1 · *"Der Kultist und sein Opfer"* — the best unused asset in the game

`npc_cultistandvictim` is **base game** (`Player.log:3583`, `dlc=None`) and its class **is loaded**
(`Player.log:3536`: `CultistandVictimID: default 'CultistandVictim' models [CultistandVictim]`). It
is a **two-figure tableau in one prefab** — a cultist and a victim. It is not a creature, it is a
scene.

Place it in the **third** trunk band (15.5–21.5 m, 34 trunks, `:7990`) at ~16 m on a bearing away
from the moon, **frozen** (X3), half behind trunks, `reveal 3.5 / hold 5.0 / fade 0`. It does nothing
at all. Somebody is doing something to somebody, sixteen metres away, and it does not know you are
there.

This is the single highest-value new event available and it needs only **X3**. It would make a better
forest card 4 than E3 does, *if* the user prefers a story to a silhouette — but it belongs at 16 m in
the third band, not at 10.5 m where the Loom stood, so it is not a drop-in.

⚠ Unknowns: no census; the prefab's scale relative to `CharacterManager.Height` with two bodies in it
is unknown; and whether the victim survives `Strip` is unknown.

### B2 · *"Die Beschwörung"* — requires §2.2

`Cultist_Summon_Living_Bones_v001` (**3.57 s**) played once, at ~16 m in the third band, arms up.
Then instant vanish. A cultist summoning something in the dark, at the limit of sight, and you never
find out what.

Requires the null-controller Playables route. The `PowerUp` **state** carries SMBs (`CastEffectsSMB`
is on that family) and the `Cultist_Summon_*` clip's own animation events would call into
`AnimFXTrigger`, which `Strip` has already destroyed. Clip-only, controller-null, `fireEvents=false`.

### B3 · *"Der Gefangene"*

`npc_captive01…06` are base game (`Player.log:3563-3574`) and `IdleCaptiveID` is loaded
(`Player.log:3527`: `default 'Captive 01' models [Captive 01|…|Captive 06]`). A captive — an
ordinary bound person, not a monster.

Standing at the clearing edge, **frozen** (X3), **facing away**, at 11 m. The only human-scaled,
non-monstrous figure in the entire wood. It is worse than a monster precisely because it is a person,
and because it is not looking at you.

⚠ Note the model string has spaces (`'Captive 01'`), which is exactly the case
`GetNPCModelEnumFromSpaceName` exists for (`MonstersYML.cs:808-813`) and exactly why the addressable
path must use the spaced name.

---

# 4. WHAT I COULD NOT SETTLE

| # | Open question | Why it is open | The probe |
|---|---|---|---|
| 1 | **Which animator states carry which SMBs** — specifically whether `Idle-Run` carries `AnimationOffsetSMB` and therefore whether the *shipped* build already draws from `SharedClient.GlobalRNG`. | It lives in the controller asset, invisible to decompiled code. | §2.6, line `PROBE SMB ON STATE 'Idle-Run'`, via `Animator.GetBehaviours<T>(hash, 0)` (present in the shipped dll). |
| 2 | **Whether the rigs are humanoid or generic.** | No avatar asset or bundle in the repo; §2.1's four lines of evidence are circumstantial, though they converge hard. | §2.6, line `PROBE RIG: isHuman=`. |
| 3 | **Whether Playables animates with `runtimeAnimatorController = null` on these rigs.** Docs and community sources say yes; nothing in this repo proves it. | Cannot be tested without the game running. | §2.6, PHASE C. This is the load-bearing probe. |
| 4 | **`Hound`, `LivingBones` and `LivingSpirit` have no census.** Unknown: states, clips, `RunBlend`, `CharacterManager.Height` semantics, and what survives the `ParticleSystem`/VFX strip. `LivingSpirit` is the one likely to come back as almost nothing. | They resolved but were never cast. | Cast each once — even in a throwaway build — and `Roster.CensusFigure` writes the line for free (`Roster.cs:444-500`). E3 does this for `Hound` as a side effect. |
| 5 | **Whether the `GlobalRNG` divergence has any peer-visible consequence.** | Reasoned, not measured. The mitigating evidence (the base game perturbs the same stream from idle twitches, `LivingCorpseIdleSelect.cs:10`) is strong but not proof. | Two clients, same scenario, one with the feature on and one off; compare `ScenarioState.Seed` after N spawns. |
| 6 | **Barrel0's real silhouette** in the cellar, which E1's occlusion depends on. | It is a photoscan; no radius is typed anywhere. | Read `Renderer.bounds` on the `Barrel0` transform at spawn and log it once. |
| 7 | **Whether `AnimationClipPlayable` fires `AnimationEvent`s.** | Unresolved in the docs. | §2.6, `eventWarnings` per phase — and it is moot either way, `fireEvents = false` is already set (`Clone.cs:386`). |
| 8 | **The cellar window's geometry.** ⚠ **A bake lane is changing it right now — bigger and deeper.** Everything in `HauntFigures.Events.cs:99-144` (the `CellarWindow` event) is quoted against the *current* `WindowHole = new Rect(3.32f, 2.15f, 1.15f, 0.7f)` and `RevealDepth = 0.34f` (`BuildEnvironmentRooms.cs:1916-1918`). **No event in this document depends on the window** — that was deliberate — but `CellarWindow`'s sill height and walk line must be re-read at implementation time. | Another lane owns the file. | Re-read `:1916-1918` and `SnappedHole` (`:2178`) before touching cellar card 0. |
| 9 | **Whether `MonsterClassManager.Classes` differs between two clients in the same scenario.** §1.5 shows it differs between *sessions*; whether it can differ between *peers* is unproven. | Needs two machines. | Log the class count and the resolved roster on both; compare. |

---

# 5. WHAT IS LOST

Stated plainly, because a design that overstates what the assets can do produces a fourth round of
*"das gefällt mir nicht"*.

**1. There is no hanged body.** This is the real loss and it is the one the user will notice. There
is no hang-by-the-neck animation, no upside-down animation, and no way to pose a skinned game
character into a hang: the bones are reachable only by string path (`C_spineChestHook_LOC`,
`CharacterManager.cs:57`), there is no humanoid rig to drive, and hand-posing 160-odd transforms into
a convincing hang is the same hand-built-figure work that was rejected three times. E4 gives back the
*rope*, the *tree*, the *feet off the ground* and the *slow turn* — four of the five things that make
the picture — and it does not give back the neck. **It should be presented to the user as exactly
that, not as "the hanged figure".**

**2. There is no face.** Forest card 0 was a head, alone, with the moon down one side of it, and its
whole content was the *internal contrast* of a face at 11 m. E2 replaces it with a body that leans
out from behind a trunk — which is a better *event*, but the user will not get the pale face he asked
for, because the only faces available belong to monsters and they come attached to bodies.

**3. There is nothing at floor level in the cellar any more.** Card 2 was a head lying on the
flagstones at the edge of the moon pool, looking up. E1 is at the opposite corner of the room, in the
dark, behind a barrel — because the moon pool is at `(-3.27, 2.22)`, which is **3.94 m from the room
centre against a 3.25 m PlaySpace radius**, and a whole body there would be 0.7 m outside the play
space rather than the 2.6 m E1 gets. A head can lie half a metre from where a person's feet will be.
A body cannot. **The moonlit floor is lost to this feature, and it is lost to geometry, not to
taste.**

**4. Nothing is lit the way the old cards were.** Each hand-built apparition carried its own `key`
colour and `keyDir`, measured against the room's 95th-percentile brightness
(`BuildEnvironmentRooms.cs:7613-7624`). A game monster is lit by the room's baked light rig and by
nothing else — there is no per-apparition value pass. Under a full **Dark** element the room dims and
the figures dim with it, and whether they stay legible is a hardware question no amount of source
reading answers. The mitigation available is placement: prefer anchors where the figure is *between*
the eye and something lighter (a moonlit trunk, the shaft's own grey) so it reads as a silhouette
rather than as a lit object. E2 and E3 are placed that way; E1 and E4 are not, because their rooms do
not offer it.

**5. Idle VFX are gone, permanently.** `Strip` destroys every `ParticleSystem` and every VFX-shaded
renderer (`Clone.cs:454-459`, `:497-502`), and the reason is sound — those materials have no
`_InvisibilityControl`, so they would pop while the body dissolved. The cost is that any creature
whose *look* is its VFX comes back as much less than it is. `LivingSpirit` is the name to watch, and
it is why no event above casts it first.

**6. The apparitions can no longer be smaller or larger than a creature.** The Loom was 3.05 m and
1.35× as wide as that height wants; the Watcher is 2.70 m and 0.62× as wide. `HauntEvent.Height`
scales **uniformly** (`Clone.cs:352-358`), so a figure can be too tall or too short but never the
wrong *shape*. Every "person's proportions, and all of them wrong" idea in the old catalogue is
unavailable, and E3 answers it by changing species rather than proportion.
