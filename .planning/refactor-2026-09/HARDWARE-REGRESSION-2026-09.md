# Hardware-observable changes — refactor 2026-09

One section per lane. Tier 0–2 commits add nothing here. Every Tier 3 fix adds one line: what he should observe, and the log token that proves the code ran.

## worldui-front

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

