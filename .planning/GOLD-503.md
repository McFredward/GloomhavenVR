# Held combined gold-pile card — build 503

## Report

When several enemies die on one hex, the laser-hover card shows the correct combined gold amount.
Picking up that same gold pile shows only the original single-token amount in the held-prop card.

## Source evidence

`WorldspaceStarHexDisplay.ShowTooltipForTile` iterates `tile.m_Tile.m_Props` and, for every
`MoneyToken`, adds the scenario `GoldConversion`. The VR hover repair in
`WorldUI/Surfaces/PropInfoSurface.cs` preserves this same rule by counting every `MoneyToken` in
the hovered `CTile.m_Props` list.

The held-card path in `Board/FigureGrab/GrabbableProp.BuildInfoText` previously read only
`Scenario.SLTE.GoldConversion`. That describes one token, not the merged pile the player is
holding. `CObjectGoldPile` has no separate aggregate-value field: the authoritative aggregate is
the set of `MoneyToken` props on `CObjectProp.PropTile`.

## Change

`GoldAtCurrentPile` now reads the held prop's native tile list, counts its `MoneyToken` entries,
and multiplies by the same conversion used by native hover. It only reads game state. A missing
tile list during teardown retains the former one-token value so the held card remains available.

## Validation and limit

The strict Release build passes with zero warnings and errors. The native field and hover formula
were inspected directly. Headset validation remains necessary: create a multi-token pile, compare
the laser hover amount to the held card, and repeat with the scenario's non-default conversion.
