# Lane `lane/quitdungeon-357` — what this lane needs from files it does NOT own

> ### ✅ CONSUMED — audited 2026-09-08 against `dev` = `49ceab21` (ModBuild 483)
>
> The lane landed and the ModBuild bump it asked for happened 126 builds ago. The 2026-09-03
> "Quest verwerfen" deadlock and the scene-membership change that fixed it are documented in the
> shipped source at `WorldUI/Conversion/CanvasConversion.1.Core.cs:268` and `:519`. Nothing here is
> outstanding.

Base: `ee04024e` (ModBuild 357). Defect: the 2026-09-03 "Quest verwerfen" deadlock.

## 1. `NetProtocol.ModBuild` — INTEGRATOR ONLY

This lane changes shipped behaviour (three new Harmony patch classes, a scene-membership
change in the canvas conversion, a new deferred host-destroy path), so the build number must
go up. The lane brief forbids the lane from touching it.

```
src/GloomhavenVR/Net/NetProtocol.cs   ModBuild 357 -> 358
```

The three new patch classes are already registered and `scripts/patch-inventory.sh check`
passes; `docs/PATCH-INVENTORY.md` is regenerated and committed on the lane branch.

## 2. Nothing else

No other file outside this lane's ownership needs a change. In particular:

* **`decompiled/`, `libs/`, `ressources/`** — read only, untouched.
* **`ScenarioRuleLibrary`, Photon Bolt, `FFSNet.NetworkManager`** — not patched, not read, not
  referenced by any new code. The remedy invokes only the `UnityAction`s the game itself passed
  to `ConfirmationBox.ShowGenericConfirmation`; for "Quest verwerfen" that delegate is the
  game's own `Choreographer.AbandonScenario()` closure, called through the game's own delegate
  object, exactly as the game's own dialog would have called it.
* **`scripts/`** — no gate needed changing. `docs/PATCH-INVENTORY.md` is generated output and is
  committed with the lane.

## 3. One thing the integrator should be aware of (not a change request)

`CanvasConversion.Convert` now puts every float host in the SAME SCENE as the window it adopts
(`DontDestroyOnLoad` for a persistent window, `SceneManager.MoveGameObjectToScene` otherwise).
This is the cause fix, and it means a persistent game window that is floated across a scene load
now SURVIVES that load with its host instead of being deleted. `ModalFallback` already releases
such a window when the game reports it closed, and `CanvasConversion.Tick`'s prune still drops
panels whose target genuinely died, so no new lifetime owner is required — but if another lane
is touching panel lifetime in the same build, this is the interaction to look at.
