# UI art the mod owns

## `VRMenuIcon.png`

The VR emblem for the mod's own pause-menu row (`WorldUI/Options/VRMenuEntry.cs`): a gold
woodcut-style headset on transparency, 256×256.

**Generated with `gpt-image-2`**, then trimmed to its drawn pixels and squared — a
mostly-transparent asset aligned to its *frame* instead of its drawn box has shipped 2.35× too wide
in this project once already, so the trim is not tidiness.

It is loaded as a **Texture2D** and turned into a `Sprite` at runtime
(`WorldUIAssets.TryLoadSprite`) rather than imported as a sprite here. A sprite import is a
per-asset setting nothing in the build checks, and one that silently reverts to "Default" ships a
texture no `Image` can draw.

**It is only used if the game's own menu rows carry an icon.** `UIMainMenuOption` declares no icon
field — only a label, a focus mask and a locked mask — and whether the shipped *prefab* puts an
Image beside the text is authored data this repo cannot read. So the runtime looks instead of
deciding: exactly one non-mask sprite image on the row means there is a slot and ours goes in it;
otherwise the row stays text-only like its neighbours, and the log says which happened.
