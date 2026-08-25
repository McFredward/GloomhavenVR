# The options menu is never blocked — ModBuild 289 defect, cause, fix, and the audit of what is left

**User ruling, absolute and pre-existing:** *"Das soll zu keinem Zeitpunkt jemals blockiert sein,
es MUSS immer möglich sein das Optionsmenu zu öffnen."* A standing ruling from 2026-08-02 already
said "the settings menu is never input-blocked" and lives in
`src/GloomhavenVR/WorldUI/Patches/SettingsClickExemption.cs`. This build broke the ruling anyway,
by a route that one does not cover: it does not block a **click**, it blocks the **show**.

Evidence: `.planning/debug/second_logs/LogOutput.log` (ModBuild 289, his hardware, one session,
5694 lines). Stack traces are enabled from ModBuild 136 on, so every claim below is attributable.

---

## 1. Timeline, from the log's own lines

| line | event |
|---|---|
| 769  | `Menu → Scenario` rig kind change — **scenario #1** |
| 2291 | `X tap … -> OPEN` |
| 2292–2312 | ModalFallback floats **`UI Scenario Esc Menu`** — the correct menu, opened fine |
| 2669 | `Scenario → Menu` rig teardown |
| 2684–2694 | campaign map discovered, **MAP rig built** (world scale 198.12) |
| 3242 | `X tap … -> OPEN` |
| 3243–3266 | ModalFallback floats **`UI Map Esc Menu`** — also correct, also fine |
| 3562 | map rig torn down |
| ~3600–3830 | main-menu scene loads; the MenuLogo **"SCENE SWEEP (at Awake)"** census runs |
| **3827** | `census#202 **UI Map Esc Menu**/UI Menu Panel/Header/Title … active=True canvasOn=True` |
| 4296 | `Menu → Scenario` — **scenario #2** |
| 5383 | `X tap … -> OPEN` |
| 5384 | `[Error] Tick 'OptionsToggle' threw and was ISOLATED — NullReferenceException` |
| 5574 | `Tick 'OptionsToggle' is still throwing (12 time(s) so far)` |
| 5383–5687 | ~20 taps, **not one of them opened anything** |

Options worked in scenario #1 — before he had ever been to the map — and failed in scenario #2,
after. That is the shape of a stale map-menu reference, and the rest of this document proves it
is exactly that.

---

## 2. What threw, named to the field

`decompiled/GH.Runtime/UIMapEscMenu.cs:164-197`:

```csharp
protected override bool CheckMultiplayerButton(out string tooltip)
{
    if (!base.CheckMultiplayerButton(out tooltip))
        return false;
    if (!Singleton<MapChoreographer>.Instance.PartyAtHQ)   // <-- here
    ...
```

The stack says `UIMapEscMenu.CheckMultiplayerButton (System.String& tooltip) [0x00010]`. The
release IL of that prologue is:

```
IL_0000  ldarg.0
IL_0001  ldarg.1
IL_0002  call     instance bool ESCMenu::CheckMultiplayerButton(string&)
IL_0007  brtrue.s IL_000b
IL_0009  ldc.i4.0
IL_000a  ret
IL_000b  call     class MapChoreographer Singleton`1<MapChoreographer>::get_Instance()
IL_0010  callvirt instance bool MapChoreographer::get_PartyAtHQ()      <-- offset 0x00010
```

Offset `0x00010` is the `callvirt` on the value `get_Instance()` returned, and a `callvirt` NREs
on a null `this`. `Singleton<T>.Instance` is a bare `=> _instance` with no guard
(`decompiled/GH.Runtime/Singleton.cs:7`), and inside a scenario there is no `MapChoreographer`.

**The null field is `Singleton<MapChoreographer>._instance`, dereferenced through
`MapChoreographer.PartyAtHQ`.** Not "some singleton". Corroboration that the base call at
`IL_0002` returned true (so `0x0010` really is past the branch): the stack has no
`ESCMenu.CheckMultiplayerButton` frame of its own, so the base returned normally.

---

## 3. Which of the two causes it was — the mod's stale cache

The brief offered two candidates and said guessing costs a build. It was **the mod's stale
`_menu` cache**. The game's own `Singleton<ESCMenu>.Instance` was correct and pointed at
`UIScenarioEscMenu`.

**The load-bearing fact: `UIMapEscMenu` survives scene loads.** Log line 3827 is a
`[MenuLogo] SCENE SWEEP (at Awake)` census — it runs inside `MainMenuLogoSwap.Awake`, i.e. during
the **main-menu scene's** Awake phase, after the campaign map had been left. With
`LoadSceneMode.Single` the outgoing scene's objects are destroyed before the incoming scene's
`Awake` runs, so anything the census can see is not in the destroyed scene. It sees
`UI Map Esc Menu/UI Menu Panel/Header/Title … active=True … canvasOn=True`, and the path starts at
`UI Map Esc Menu` — a scene ROOT, i.e. `DontDestroyOnLoad` or a persistent additive UI scene.

From that one fact everything else follows:

* `OptionsToggle._menu`'s doc comment claimed *"The cache self-invalidates on scene unload (the
  object is destroyed → Unity-null)"*. That premise is **false for one of the two menus**.
  A Unity fake-null test only clears a reference to a **destroyed** object. `UIMapEscMenu` is
  never destroyed, so `_menu` never cleared, so scenario #2 kept calling `Show()` on the
  campaign map's pause menu inside a dungeon.
* The game's singleton, by contrast, was fine. `Singleton<T>.Awake` sets `_instance = this` and
  `OnDestroy` sets it to null. `UIScenarioEscMenu.Awake` ran at the scenario #2 load (line 4296),
  which is strictly the last `Awake` of any `ESCMenu` in the session — `UIMapEscMenu` awoke back at
  the map load (~2684) and, being persistent, never awoke again and never destroyed itself. So at
  line 5383 `Singleton<ESCMenu>.Instance` was `UIScenarioEscMenu`.
* The falsifier for the other hypothesis: if the game's singleton had been the map menu, the mod
  would have had to have adopted the map menu in scenario #1 too (its `Awake` would have preceded
  the scenario menu's), and scenario #1 would have crashed identically. Line 2292 shows it opened
  `UI Scenario Esc Menu` cleanly. The singleton was never wrong.

**A third defect fell out of the same fact.** The old fallback scan took `found[0]`:

```csharp
ESCMenu[] found = FindObjectsOfType<ESCMenu>(includeInactive: true);
if (found.Length > 0) _menu = found[0];
```

Once the player has visited the map, **both** menus are alive at once for the rest of the session,
so `found[0]` is a coin flip between the right menu and a guaranteed crash. That path never fired
in this log, but it was one singleton-null away from firing.

---

## 4. The fix, both halves

### Half 1 — open the RIGHT menu (`src/GloomhavenVR/WorldUI/OptionsToggle.cs`)

`ResolveMenu()` replaces the "cache-or-singleton, then `found[0]`" block, in priority order:

1. **The game's live registration wins.** `Singleton<ESCMenu>.Instance` whenever it is
   initialized — a static field read, so it is free every frame. This alone answers ModBuild 289.
2. **The cache answers only inside its own scene.** `_menuSceneHandle` records the active scene's
   handle at adoption; a cached menu that outlived that scene is dropped with a log line naming
   why. This is the invalidation Unity was wrongly assumed to be doing for free. The cache still
   exists for its original purpose (the mod's own close deactivates the menu and it can drop out of
   the singleton — the old reopen bug), and that purpose never spans a scene change.
3. **The scan is RANKED, not `found[0]`.** `RankedCandidates()` scores each live `ESCMenu`:
   `+4` in the active scene, `+2` active in the hierarchy (tie-break only — the mod's own close
   deactivates the menu, so "inactive" must never disqualify), `-8` for a `UIMapEscMenu` with no
   `Singleton<MapChoreographer>` (the exact object that threw). The `-8` outweighs the other two
   combined so it always loses to any alternative, and it is still only a ranking: with no other
   candidate it is picked anyway, because a partially-working menu beats no menu.
4. **`Adopt()`** re-bases `_open` from the NEW window rather than carrying it over, so a menu swap
   cannot present as a phantom "opened/closed externally" edge. It deliberately does **not** spend
   the in-flight press: adoption can land on the very tap the player made, and spending it would
   trade "the menu never opens" for "the first tap after every context change never opens it".
5. **Every open and every tap logs `menu.GetType().Name` and the resolution source.** ModBuild
   289's log could only name the map menu because the game happened to throw. It now says so
   without a crash: `[OptionsToggle] X tap on UIScenarioEscMenu (source: Singleton<ESCMenu> …)`.

### Half 2 — a game-side throw can no longer block the menu (`…/Patches/EscMenuShowSafety.cs`, new)

The brief's mechanism was right but understated. `decompiled/GH.Runtime/UnityEngine.UI/UIWindow.cs:540`:

```csharp
protected virtual void EvaluateAndTransitionToVisualState(VisualState state, bool instant)
{
    if (onTransitionBegin != null)
        onTransitionBegin.Invoke(this, state, ...);   // 545 — a listener THROWS
    OnTransitionStarted(state, instant);              // 547
    m_CurrentVisualState = state;                     // 548 — NEVER RUNS
    ...
```

`UnityEvent.Invoke` has no per-listener try/catch, so one throwing listener amputates every
listener after it — but the **decisive** damage is that the unwind skips
`m_CurrentVisualState = state` at line 548. `UIWindow.IsOpen` is
`m_CurrentVisualState == VisualState.Shown`, so the window does not merely fail to draw: by its own
accounting it **never opened**, and the next `Show()` repeats the identical failure forever. That
is why twenty consecutive taps produced twenty identical stacks and zero menus.

Two finalizers, innermost first:

* **`EscMenuMultiplayerCheckFinalizer`** — all three `CheckMultiplayerButton(out string tooltip)`
  declarations (`ESCMenu` and its only two subclasses; verified that is the complete set). On a
  throw it forces `__result = false` and `tooltip = null`. Safe by construction: the caller
  `ESCMenu.RefreshMultiplayerButton` does exactly
  `multiplayerButton.IsInteractable = <verdict>` then
  `multiplayerButton.SetTooltip(tooltip.IsNOTNullOrEmpty(), tooltip)`; `IsNOTNullOrEmpty()` is an
  extension method and null-safe. One button greys; Options, Compendium, Continue, Main Menu and
  Exit are untouched. This lets `OnShow` keep running **past** the throw instead of merely
  surviving it.
* **`EscMenuTransitionFinalizer`** — the catch-all, and the one that carries the ruling.
  `ESCMenu.OnTransitionBegin` **is** the registered `onTransitionBegin` listener
  (`ESCMenu.cs:90`), i.e. the exact frame in the ModBuild 289 stack where the exception crossed
  into `UnityEvent.Invoke`. It is `protected` and **non-virtual**, so one target covers both
  subclasses, and it is only ever reached through the registered `UnityAction` delegate, so there
  is no JIT-inlining hazard. Swallowing here makes `Invoke` return normally whatever `OnShow` did,
  so line 548 always runs and the window always opens (and always closes). It covers every present
  and future unguarded dereference in that family, of which the decompiled source has many:
  `UIScenarioEscMenu.OnShow` opens on `Singleton<UINavigation>.Instance.StateMachine`;
  `ESCMenu.OnShow` ends on `Singleton<UIReadyToggle>.Instance.CanBeToggled`;
  `UIMapEscMenu.OnShow` reads `Singleton<MapChoreographer>.Instance.PartyAtHQ` **again** just past
  the point that threw, and then `NewPartyDisplayUI.PartyDisplay.TabInput`. None are null-checked.

**Finalizer semantics, reasoned to the boundary that matters.** In HarmonyX the finalizer's
**return value** decides: returning the incoming `__exception` rethrows it (byte-identical
vanilla); returning `null` suppresses it and the patched method returns normally to its caller.
The caller here is `InvokableCall`3.Invoke`, which then walks on to the next listener and lets
`EvaluateAndTransitionToVisualState` finish. A `void` finalizer that merely "returns normally"
would **not** suppress anything — the null return is the whole mechanism. This is not asserted
from documentation: `SettingsClickExemption` already ships the identical pattern on the same
Harmony instance and its doc records the same contract.

A prefix that skipped the original was rejected: it would silence the game's own bookkeeping on
**every** open, not only the broken ones — no HIGHLIGHT-input disable, no button focus, no
`EscMenuStateChanged`. The finalizer is byte-identical to vanilla on the clean path.

### Half 3 — verify the outcome instead of asserting it

The old success line was `OPTIONS TAP: pause menu OPENED (X tap) … (activeInHierarchy=…)` — it
asserted an outcome it never checked, and printed a different question's answer beside it. Now
`OpenMenu` returns the verdict:

* `m_CurrentVisualState` is assigned **synchronously** inside `EvaluateAndTransitionToVisualState`
  (only the alpha tween that follows is asynchronous), so reading `IsOpen` straight after `Show()`
  is a verdict, not a race.
* On failure `DescribeShowBlocker` names the blocker: the `UIWindow` component disabled; an
  inactive **ancestor** (walked and named — `OpenMenu` only re-activates the window's own
  GameObject, which cannot reach a parent); or "the window was active and `Show()` ran but
  `m_CurrentVisualState` is still Hidden", with the instruction to look for an
  `ESC MENU SHOW SAFETY` line and, if there is none, conclude the throw came from a listener the
  finalizers do not cover.
* **Second chance:** if the first-choice menu refuses to open, `TryFallbackMenu` tries every other
  `ESCMenu` in the scene on the same tap. This can never produce two open menus, because it only
  runs when the first demonstrably did not open.
* `_open` now records what **happened**, not what was asked for. Recording `true` after a failed
  open produced a phantom "pause menu closed externally" on the next tick and left the user
  alternating between a dead open and a dead close; with the truth in the latch, a hammered button
  retries the OPEN every time.

### The tap-frame cost

`FindObjectsOfType<UIWindow>()` in `FindOpenCompendiumWindow` is gone. `UIWindow` keeps its own
static registry `_uiWindows`, added to in `OnEnable` and removed in `OnDisable`
(`UIWindow.cs:69/398/404`), exposed as `UIWindow.GetWindows()` and used by the game itself in
`UIWindowManager.HideOrShowWindows`. It holds exactly the **enabled** windows, a superset of the
open ones, so it answers the same question with the same result and no scene sweep.

The remaining cost is now **measured** rather than asserted: `LogTapCost` splits the tap frame into
PROBE (everything the mod does before touching the game) and ACTION (the game's own `Show()`/
`Hide()` cascade running on our call stack) and prints both against the 11.11 ms budget. See §6 for
why the split is only two terms.

---

## 5. AUDIT — every other route by which the options menu could still become unopenable

The ruling is "never", so this is an explicit list, checked one by one. **Status** is one of
FIXED / GUARDED / DETECTED (the mod now names it in the log but cannot repair it) / OPEN.

| # | Route | Status | Notes |
|---|---|---|---|
| 1 | Mod drives the wrong `ESCMenu` after a context change | **FIXED** | `ResolveMenu` prefers the live singleton; scene-scoped cache; ranked scan. This was the ModBuild 289 defect. |
| 2 | `ESCMenu.OnShow`/`OnHide` throws → transition abandoned before `m_CurrentVisualState = state` | **FIXED** | `EscMenuTransitionFinalizer`. |
| 3 | `UIMapEscMenu.CheckMultiplayerButton` NREs on `MapChoreographer` | **FIXED** | `EscMenuMultiplayerCheckFinalizer`; `OnShow` also continues past it. |
| 4 | A throw in `UIWindow.Show(bool)` **before** the transition — `Focus()`, `onShown.Invoke()`, `OnShow?.Invoke()`, `OnActivityChanged?.Invoke(true)` (UIWindow.cs:475-490) | **DETECTED, not fixed** | `EvaluateAndTransitionToVisualState` is then never called at all, so a finalizer on the transition cannot help and a finalizer on `Show` would leave the window closed anyway. The mod's `DID NOT OPEN … REASON` line names it and explicitly distinguishes it from route 2 ("look for an `ESC MENU SHOW SAFETY` line: if there is none…"). No mod code subscribes to the ESC window's `onShown` (swept: the only mod `onShown` listeners are `PropInfoSurface` and `StatPanelSurface`, on other windows). A real fix means a transpiler or a reimplementation of `Show`; not worth it until a log shows it. |
| 5 | `UIWindow.Show` early-returns because `IsActive()` is false — an inactive **ancestor** or a disabled `UIWindow` component | **DETECTED, not fixed** | `OpenMenu` re-activates the window's own GameObject; `DescribeShowBlocker` walks up and names the first inactive ancestor. Force-activating an ancestor is deliberately **not** done: the game deactivates whole UI roots on purpose and the mod would be fighting a writer it does not own. |
| 6 | No `ESCMenu` object exists at all (main menu, loading, between scenes) | **OPEN by construction** | There is nothing to open. The mod already logs `no ESCMenu object exists …`. Not a mod block. |
| 7 | The mod removed the game's own redundancy: `ShowUIWindowSuppressor` blocks `UI_PAUSE → ESCMenu.ShowUIWindow` while VR runs, so if `OptionsToggle` is dead there is **no** second opener | **ACCEPTED, documented** | Un-suppressing is **not** safe: both menus register that handler in `Awake` and only unregister in `OnDestroy`, and the map menu is never destroyed — so `UI_PAUSE` in a scenario would show BOTH menus. The redundancy is instead provided in-mod by route-1's ranked resolution and by `TryFallbackMenu`. If a future build wants the game path back it must first make `UIMapEscMenu` unregister on scene change, which is game state and out of scope. |
| 8 | `NonDominantHold.ShortTapThisFrame` never fires — another consumer sets `Consumed` first, hold thresholds mis-tuned, tracking lost | **OPEN, not owned** | Input layer (`src/GloomhavenVR/Hands/NonDominantHold.cs`). The symptom is total silence: no `X tap` line at all, which is itself the diagnostic. Worth an integrator check that nothing else in the frame order consumes the non-dominant tap before `OptionsToggle`. |
| 9 | `_spentPressId` latches forever if `NonDominantHold.PressId` ever stops incrementing | **OPEN, not owned, visible** | The press-identity gate is only as good as the id source. A stuck id makes every tap after the first print `OPTIONS TAP: ignored — this press already served …`, so the log names it immediately. Sentinel is `-1`, which matches no real press (ids start at 1). |
| 10 | `OptionsToggle.Tick` never runs because `WorldUIModule.Update` throws **outside** a `TickGuard.Run` | **OPEN, not owned** | `TickGuard` isolates each registered step, so a sibling step's throw cannot starve this one; an unguarded throw in the driver's own `Update` body still can. `WorldUIModule.cs` is not owned by this change — integrator should confirm every statement in that `Update` is inside a guard. |
| 11 | `EscMenuInputBlock.EnsureRegistered` never runs (it is called from `InputModeGuard.Tick`), so the safety net is never installed | **PARTLY GUARDED** | Registration is idempotent and retried every frame until `VRSession.Harmony` exists. `RegisterShowSafety` is deliberately in its **own** try/catch outside the input-block's, so the input-block patches failing cannot take the safety net down with them — the input block is a convenience, the safety net is the ruling. Still depends on `InputModeGuard.Tick` running at all. |
| 12 | The window opens (`IsOpen == true`) but `ModalFallback` never floats it, releases it, or leaves it invisible — open on paper, unopenable to the player | **OPEN, not owned** | `ModalFallback.*` is outside this change's file ownership. The ModBuild 289 log shows a real liveness rule here: `MODAL LIVENESS ARMED … if it stops drawing for 2.0 s the whole float is released`. The `MODAL PRE-CONVERT BLACKOUT` path also switches the window's canvas off for a frame before conversion. Recommend the integrator audit those two against the ruling — this is the most likely remaining route to "I pressed X and nothing appeared". |
| 13 | The finalizer does not actually suppress at the `InvokableCall` boundary | **GUARDED + DETECTED** | Reasoned through above and matched against the shipped `SettingsClickExemption`. If it is nevertheless wrong, the failure is not silent: `DID NOT OPEN … REASON` fires and explicitly tells the reader to check for the absence of an `ESC MENU SHOW SAFETY` line. |
| 14 | A game update renames `ESCMenu.OnTransitionBegin` or `CheckMultiplayerButton` | **GUARDED** | Both resolve through `AccessTools.DeclaredMethod` before `PatchAll`, degrade to a strict no-op with one warning, and never throw during registration (`PatchAll` throws on an empty target set — hence the eager resolve). |
| 15 | Another listener on `onTransitionBegin`, registered **before** `ESCMenu.OnTransitionBegin`, throws | **OPEN** | The finalizer covers `ESCMenu.OnTransitionBegin` only. No mod code adds a listener to the ESC window's `onTransitionBegin` (swept). A game-side one would amputate before ours is reached. Same detection path as route 4. |
| 16 | Resolved menu has no `UIWindow` component | **FIXED** | `OpenMenu` returns false with that exact reason instead of throwing an NRE inside our own tick. |
| 17 | The escape chord / laser `ModalCloseButton` closes the menu on the same press that opened it | **PRE-EXISTING, GUARDED** | The press-identity gate (`_spentPressId`) and the one-shot reconcile already own this; unchanged by this build. |

Routes 4, 5, 12 and 15 are the honest residue. Three of the four are now **named in the log the
moment they happen**, which is the difference between "twenty dead taps and a guess" and "one line
naming the blocker".

---

## 6. Performance — the tap frame

ModBuild 289 `[Perf] SPIKE`:

| frame | total | mod | worst step |
|---|---|---|---|
| 8957  | 23.23 ms | 20.05 ms | `OptionsToggle 11.22 ms`, `ModalFallback 5.69`, `ModalFallback.Convert 5.58` |
| 13406 | 23.28 ms | 11.49 ms | `OptionsToggle 10.60 ms`, next step 0.21 ms |
| 14908 | 22.89 ms | 11.48 ms | `OptionsToggle 10.49 ms`, next step 0.22 ms |

Against an 11.11 ms budget that is one dropped frame per tap. Frames 13406 and 14908 are inside
the throwing window (5383–5687) — `OptionsToggle` is essentially the **entire** mod cost on those
frames and `ModalFallback.Convert` is absent, i.e. **10.5 ms was spent to accomplish nothing**.

The doc comment at the probes claimed they "run ONLY on the tap frame … so the singleton lookups
and the one compendium scene scan are cheap at human tap cadence". A comment is not a measurement,
and this one was wrong twice over: the frame is not cheap, and "one dropped frame per tap" is not
an acceptable definition of cheap in VR.

What was done:

* **Removed** the one avoidable mod sweep: `FindObjectsOfType<UIWindow>()` → `UIWindow.GetWindows()`
  (the game's own static registry).
* **Measured** the rest instead of guessing. `LogTapCost` prints
  `TAP COST X ms = PROBE Y ms (… N registered UIWindows walked …) + ACTION Z ms (the game's own
  Show/Hide cascade …)`. Two terms, because two terms settle the question: if ACTION dominates the
  10 ms is the game's window machinery running on our call stack (`Focus()`, `onShown`, the whole
  `OnShow` cascade, analytics, localization, layout) and no amount of probe tuning touches it; if
  PROBE dominates it is ours. It is not honest to claim a number I could not measure on his
  hardware, and it is not useful to ship a third round of tuning against a term nobody has isolated.

The prior on the evidence is that ACTION dominates: frame 8957 lists `ModalFallback.Convert` as a
**separate** 5.58 ms step, so the 11.22 ms attributed to `OptionsToggle` is time spent inside our
own call, and the only thing inside it heavy enough to cost 10 ms is the game's `UIWindow.Show`
cascade on a 1920×1080 uGUI menu. The next log will say so or falsify it in one line.

---

## 7. Files

* `src/GloomhavenVR/WorldUI/OptionsToggle.cs` — Half 1, Half 3, the perf work.
* `src/GloomhavenVR/WorldUI/Patches/EscMenuShowSafety.cs` — **new**, Half 2.
* `src/GloomhavenVR/WorldUI/Patches/EscMenuInputBlock.cs` — registers the two new patch classes in
  its own try/catch, independent of the input-block registration.
* `docs/PATCH-INVENTORY.md` — regenerated; the patch surface moves 78/130 → **80/132**.

Multiplayer: nothing here sends, receives, gates on network role, or writes game state. The
finalizers only decide whether an exception on the **local presentation** path is rethrown, and the
resolution change only decides which local `MonoBehaviour` the mod calls `Show()` on. Both peers
run the identical code with no shared state, so the build is MP-compatible by construction and
`NetProtocol.ModBuild` is untouched.
