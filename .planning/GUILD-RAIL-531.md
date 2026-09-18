# Guildmaster controls — build 531

## Evidence and native behavior

The supplied local logs identify build 530 / assembly 1.0.5.0. Remote logs identify build 500
and cannot establish the current observer picture. The maintainer reports no tabletop buttons;
other build-530 changes appear fixed. Existing `schwebende_buttons.jpg` and `zu hoch.jpg` are
older geometry references, not screenshots proving the build-530 missing-control cause.

These controls are needed in Guildmaster. The read-only game sources show:

- `MapChoreographer.PartialUILoad` activates the serialized Guildmaster HUD in both modes.
  `UIGuildmasterHUD.Awake` creates every destination and binds its original button.
- `MerchantMode`, `TrainerMode`, `EnchantressMode` and `TempleMode` depend on headquarters
  unlocks/tutorial progression, not a campaign-only condition. Their windows remain needed.
- `WorldMapMode` unlocks after the Guildmaster trainer. `CityMapMode` and `MercenaryLogMode`
  are campaign-only. This does not justify removing the entire rail in Guildmaster.
- Native `GuildmasterMode.RefreshUnlocked` changes each button object's active flag. The mod
  discovery includes inactive descendants and has an inactive-inclusive scene fallback;
  ordinary locked/hidden bars therefore do not remove candidates. Native click availability,
  unlock state, quest commitment and the existing visual sampling remain unchanged.

There are no rail build/order lines in the current local logs, nor a native button hierarchy
census. The old `SameSet` check returned early for two empty sets, masking the existing missing-HUD
report. Consequently the logs alone cannot distinguish no candidates from a rejected furniture
fit. A normal initialized map should have candidates according to the native path above, but
that is source evidence, not a measured candidate count from this hardware run.

## Source repairs

Build 530 silently skipped the whole rail if the optional tabletop fit failed. It now preserves
the necessary actions on a deterministic right-side fallback and emits one warning per rail
instance/session. The fallback clears any measured tabletop knife; it is explicitly not a claim
that a support slab was measured, and does not move native furniture. Normal campaign placement
is unchanged. Missing native HUD discovery now reaches its existing once-only report instead of
being mistaken for an unchanged empty rail. Empty scans retain their ordinary cadence.

The preferred supported fit projects each non-static-batched mesh's authored bounds directly
into the seat frame, rather than projecting an already expanded world AABB a second time.
Static-batched renderers retain their valid world bounds. A combined native table/leg mesh can
serve as support even if the optional leg system's thin-slab heuristic rejects it. Weapons off
the tabletop's height and disabled renderers cannot consume the free strip. These are concrete
failure classes in the old measurement; the supplied logs do not identify which one happened.
The native cap glow now uses the fitted cap size during live sampling as well as at creation.

The hardware-confirmed floor placement is unchanged. Successful/fallback fit measurements are
Debug-only. No new polling stream or per-frame scene survey is introduced.

## Validation and headset limits

`scripts/guildmaster-room-tests.sh`: 7,011 production layout assertions, 19 integration bindings,
and five rejected runtime mutations. Covers support extents, button order, floor arithmetic,
missing support, finite fallback placement and knife clearance for one through twelve buttons.
Strict Release build: zero warnings and errors. Whitespace check passes.

Verify right-side buttons on a progressed Guildmaster save, merchant/trainer opening and closing,
locked destinations staying inert, and ordinary campaign placement. The fallback protects access,
but exact native mesh support and knife clearance still require a headset view. If the next log
reports an absent HUD rather than an unsupported fit, that is a distinct acquisition failure and
must be investigated as such; the current evidence does not prove one.
