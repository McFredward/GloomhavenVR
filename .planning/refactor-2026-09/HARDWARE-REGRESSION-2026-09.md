# Hardware-observable changes — refactor 2026-09

One section per lane. Tier 0–2 commits add nothing here. Every Tier 3 fix adds one line: what he should observe, and the log token that proves the code ran.

## worldui-front

**T1 — the flat 2D campaign map stopped sweeping the whole scene once per frame.**
`FlatScreenStereo.DetectActiveMap` opened with a bare
`Object.FindObjectOfType<MapChoreographer>()`, and its caller chain runs every frame while that
path is engaged (`EndStackSync` → `EnsureAlbedoReady` → here). It now reads the cache
`TickFastMapEngage` has kept since the fast-engage round, with the same 10-frame throttle and the
same Unity-fake-null re-arm; the COLD path is unchanged.

*What to do:* only with **`[Rig] Vanilla2DMap = true`** (the flat 2D map — the 3D map room does
not take this path at all). Open the campaign map, leave it open a few seconds, switch
**world ↔ city**, and open a scenario from it.

*What he should observe:* **nothing different.** The map appears when it appeared before, the
world↔city switch re-gathers as before. If anything is smoother on the map screen that is the
per-frame sweep being gone.

*The log token that proves it ran:* `MAP SELECT (ISSUE 3)` — it must still print on every
world↔city switch, naming `active map = CITY` / `= WORLD`. Its ABSENCE on a switch is the
regression: it would mean the cached choreographer went stale instead of re-arming.
`MAP RENDER detection (FAST positive)` must still print once when the map opens.

## worldui-frame

## net

## core

## cards

**Nothing to re-test on the headset.** Every source change in this lane is Tier 0-2 and the
compiled-form guard says so: the four `CHANGED` types are exactly the ones the commits name, and
the three surfaces the guard cannot see are unmoved (config keys, harmony patches and log tokens
all diff to 0 removed).

- Tier 0 (`2db74932`) removed four members nothing read (`VRCard._heldRot`,
  `ItemsPile.ItemChip._heldRot`, `PlayTray._itemUseSlotGlow`, `PlayTray.SquareCapThickness`) and
  corrected seven comments. A field with no reader cannot change a picture.
- Tier 1 (`f3aa55fc` + `350a33f2`) cut `CardsConfig.Bind` into thirteen slices in the same order.
  The one thing to notice is a NON-event: **his `dev.gloomhavenvr.cards.cfg` must be unchanged.**
  The 156 keys were extracted in source order before and after and are identical, so BepInEx
  rewrites the same file in the same order. If a key ever moved, its tuned value would revert
  silently — which is why the order was proved rather than assumed.
- Tier 2 (`601bbdd8`) made the owner's active-half pulse call the same expression every peer's
  mirror already calls. Same inputs, same outputs; the only difference is that a throw out of the
  game's `FindCasterActiveBonuses` now answers "whole card lit" instead of reaching the driver's
  per-tick guard — the mirror's long-standing behaviour, and invisible unless the game throws.

**What he WILL notice, all of it off-headset:** a red build now stops `refactor-guard.sh` with
the compiler's own error lines instead of printing a green verdict (`e464f786`); the guard line
reads `config keys 625` instead of `412` (`07c383ce` — the census can now see the `[Comfort]`
and per-board keys, so the next `baseline` records the larger number); a pull request runs nine
more gates on GitHub (`dca03c86`); and `log-triage.py` marks debug-tier tokens in its SILENT
list instead of listing them bare (`7587e1e5`), which matters the next time a default-level drop
is read.

