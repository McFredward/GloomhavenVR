# Native card ownership across scene reload — build 516

## Evidence and limits

The supplied local `Player.log`/`LogOutput.log` identify release 1.0.3, ModBuild 515,
commit fd76de86b. The retained remote capture is build 500 and is not evidence for this run.
No screenshot is required to identify this exception; the separate MR screenshot belongs to
another lane.

The first relevant native failure occurs during scene teardown, around Player.log line 16376
(line numbers differ slightly when CRLF lines are normalized):

- `FullAbilityCard.SetUnfocused` accesses a destroyed native component through
  `Component.get_gameObject`.
- Called by `AbilityCardUI.OnReturnedToPool`, `ObjectPool.RecycleCard` and
  `CardsHandUI.OnDestroy`.
- After scene reload, `CardsHandUI.SetMode` fails in `AbilityCardUI.ToggleFullCard` (~17330),
  `UpdateView` fails in `FullAbilityCard.UpdateScale` (~17348), and `Hide` fails through
  `FullAbilityCardAction.ToggleSelect`/a destroyed `Selectable` (~17384).
- The native global error window is then converted normally (~17366). It reports the failure;
  it does not originate it.

The read-only native source confirms that `ObjectPool.RecycleCard` puts the widget into its
persistent pool *before* calling `OnReturnedToPool`. A failing child reset can therefore leave
a broken widget available for the next scenario. `SceneController.LoadSceneCoroutine` preserves
the card pool (`ClearAllExceptCards`) and unloads the old scene before loading the new one.
The mod previously waited for native `CardsHandUI.OnDestroy` or VR host destruction to return
borrowed `FullAbilityCard` hierarchies. By then their scene-hosted children can already be gone.

Destroyed pooled native components are directly established by the stack traces. The exact
Unity hierarchy destruction order and first destroyed VR host were not recorded. Early release
closes the source-proven ownership window consistent with these traces; a headset restart
retest is still needed to establish that this was the only contributing defect.

## Change

- Wrap the original native load iterator; release borrowed faces immediately before its first
  `MoveNext`, never merely when an iterator is constructed. Native `DataRestoring` no-op loads
  do not release anything. Yields, normal completion, disposal and native exceptions retain
  their original behavior.
- Return every factory face and its native FX overrides while its full hierarchy is intact.
  Keep VR wrappers and `GameCard` identity alive, preserving selected field references if the
  native load aborts without unloading the original hand.
- Use native `_loadingSceneType`, not a mod-owned latch, to block card presentation/adoption
  until loading ends. Generic `IsLoading` starts before `EndScenarioSafely` waits for mandatory
  damage responses; using it would create a new deadlock. Native gameplay callbacks continue. Wrapper construction is self-guarded and retains the original
  native iterator on failure. Mod release runs through the existing production TickGuard; a cleanup
  failure cannot swallow the native load step. Factory face return precedes separately isolated
  cosmetic hover cleanup.
- Recover *every* retained wrapper before presentation resumes. Confirmed pick cards need this
  explicitly because ordinary layout excludes them from `GetOrCreate`. Failed face attachment
  retains the existing widget identity for retry. Real hand destruction still uses the existing
  authoritative recycle/removal path.
- The adjacent review found a retained-page lifetime edge: recycling a locked prefix card must
  decrement `_pickLockedCount`, otherwise the next visible card becomes incorrectly locked.
  `RetirePickFieldCard` fixes that path; tray-lane production tests cover it in integration.

No native error handler is suppressed, no corrupt native cache is replaced, no game decision
is bypassed, and no new wire format or remote exception is introduced.

## Validation

`scripts/card-scene-lifetime-tests.sh`: **35 runtime assertions**, **20 source bindings** and
**seven runtime negative controls**. The production iterator and extracted production factory
ownership methods execute against narrow native/Unity adapters. Cases include an unstarted
iterator, live admission changes, native failure/disposal, an undisposed abandoned iterator,
nested loads, all retained native faces with mask/Selectable descendants, selected-slot identity,
load-time adoption refusal, aborted-load full recovery, missing-art retry and idempotent adoption.
Negative controls deliberately delay release, bypass DataRestoring, repeat release, retain the
borrowed face, lose selected identity, skip confirmed-slot recovery or remove the production release guard.
The guarded-release test compiles the actual production `TickGuard.Run` method; an injected mod
exception is reported while native iteration continues, whereas native exceptions still propagate.

The simulated destruction tree validates the scheduling/ownership contract; it is not a Unity
scene-unload run or headset visual proof. Source bindings strip comments and establish the actual
Harmony registration, scene-state admission, restore/reacquire sites and native parent restoration.
Strict Release build passes with zero warnings and errors. The integrator owns complete guards,
patch inventory regeneration, the shared build note and the final hardware checklist.
