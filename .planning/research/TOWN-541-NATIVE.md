# Build 541: original merchant control topology

Read-only inspection of the supplied game data confirms that the build-540 catalog
constructor assumed a controller-only filter exists in mouse-input scenes. This is
not delayed initialization or a missing asset bundle.

| Original scene | Serialized file / UIShopItemInventory path ID | `_ownedFilter` path ID |
| --- | --- | --- |
| `NewAdventureMap` (Guildmaster mouse UI) | `level4` / `13424` | `0` (null) |
| `NewAdventureMap_gamepad` | `level5` / `16442` | `14590` |
| `CampaignMap` (mouse UI) | `level9` / `16473` | `0` (null) |
| `CampaignMap_gamepad` | `level10` / `20211` | `17884` |

All four original components have valid scroll, buy/sell tabs, all/head/body/hands/legs/
small-item filters and item-tooltip references. Each non-null reference was resolved
independently to its original GameObject and script class. The mouse scenes also omit
several unrelated controller-navigation fields; they are valid native scene variants.

`UIShopItemInventory.Awake` explicitly omits `ItemListingType.Owned` when
`!InputManager.GamePadInUse` (decompiled `GH.Runtime/UIShopItemInventory.cs:296–304`).
The same distinction occurs in native filter refresh. The VR catalog must preserve
that optional topology, rather than dereference or synthesize an absent control.

The hardware log completes conversion of merchant surfaces 20 (buy), 21 (sell) and
22 (all), then falls back from the catalog constructor. Its next build-540 expression
is `inventory._ownedFilter.transform`, which dereferences the confirmed null field.

## Evidence method and limitations

The ignored evidence manifest is retained at
`/home/claw/gvr-town541-native/.planning/debug/town-build-541/native/inventory-fields.json`.
It records hashes of all four original scene files, `GH.Runtime.dll` and
`globalgamemanagers`, along with object pointers, resolved names and script classes.
Scene names come from the original `BuildSettings.scenes` array.

UnityPy 1.25.3 reads original base objects. A small explicit little-endian field reader
follows the game's serialized declaration order through `smallItemsSection`; base
pointers are checked against UnityPy, and all non-null control references are resolved
back to original objects. This avoids depending on generated MonoBehaviour base-tree
alignment. The extraction script is retained beside the manifest. No game asset was
modified and no game session or headset rendering was simulated by this inspection.
