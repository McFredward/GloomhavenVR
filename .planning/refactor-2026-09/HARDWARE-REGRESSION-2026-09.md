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

Nothing in this lane changes a picture, a sound, a pose, a cadence or a wire byte. The compiled form
differs in nine files and every difference is a comment, a string literal, a renamed private method,
one added `Detach()`, one moved window reset, one unreachable branch removed, or a const that moved
into `Defaults` with its value unchanged. There is no Tier 3 behaviour change to observe.

Three things a tester WOULD see, none of which needs a test pass of its own:

1. **`[Haunt] EasterEggs`'s description text, EN and DE** (HT-6). The English no longer promises
   "eyes that open in the undergrowth" or "a dim warm door opening at the top of the stair" —
   both apparitions were deleted at ModBuild 149 and their cards are inert placeholders. The German
   was two rounds further behind and also promised the rejected window walk-past ("von dem du nur
   die Beine siehst"), the rejected stair walk and a face behind a tree. Visible in the settings
   browser and in `BepInEx/config/…Rig.cfg`. The KEY, the DEFAULT and the wire are untouched, so no
   tester's cfg value moves.
2. **`[Comfort] SavedScaleMultiplier`'s description text** (RV-2): "after each two-grip scale
   gesture" → "two-stick-click", which is what the gesture has been since the P8 rebind.
3. **Two log lines read differently** — `ENV SOUND up` no longer names the four night-call voices
   ModBuild 246 withdrew (SD-6), and `DOOR OPEN CLIP SAMPLE`'s two READING: tails no longer promise
   a wall-shader fallback that ModBuild 429 removed (DW-1). Every SHOUTED grep token on both lines
   survives — `AND FIVE MORE ANIMALS`, `AND THREE EERIE ONES SINCE`, `DOOR OPEN CLIP SAMPLE`,
   `PLAYABLE GRAPH`, `CLIP AND ITS PATHS` — and the guard confirms 0 tokens removed.

The one fix with a runtime consequence, RV-1 (`ComfortSettings.Unbind` now detaches the 22nd
wrapper), is reachable only through `RigModule.Shutdown()`, i.e. a hot reload. Nothing to observe in
a normal session.

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

