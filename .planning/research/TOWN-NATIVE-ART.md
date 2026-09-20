# Native town widget art preload

`TownServiceNativeAssets` independently holds original Addressables sprites needed by public
inert town-widget copies. Native inactive `Show` coroutines are not a reliable loading path;
remote clones must not activate native gameplay or addressable-loader controllers.

## Integration contract

- Before freezing a card source: `PrepareCard(CAbilityCard?, ItemCardUI? = null)`.
- Before freezing a native hierarchy: `PrepareRoot(Transform?)`.
- For a relevant selected visitor/actor: `PrepareCharacter(ECharacter, custom)` or
  `PrepareActorPortrait(model, customPortrait)`. These use native UIInfoTools lookups;
  they do not preload all character/monster catalogues.
- Call `Tick()` at the public map lifecycle gate; call `Shutdown()` when that lifetime ends.
  Every preparation entry point and Tick also checks `MapRoomDriver.Active` itself.
- Mirror.Apply binds the registered original assets to the inert clone. This helper never
  writes source Images, activates a hierarchy, starts a game coroutine, or spends game state.

Ability skins use the same `ClassModel` / `ClassCharacterConfig` lookup as native Init.
All nine ReferenceToSprite fields are loaded: title and both halves' regular, highlighted,
selected and disabled states. Other skin sprites and selectable control states are resident
references. Item background art comes from the fully initialized ItemCardUI.item YML Art;
its valid-owner icon follows the same native first-class entry as ItemCardUI.

PrepareRoot reads Images, RawImages, Selectable sprite states and direct ReferenceToSprite
fields on native-assembly components. Reflection does not traverse arbitrary controller graphs,
read static registries, invoke properties, or mutate fields. SpecialSprite is already resident.

The loader passes an AssetReference's string RuntimeKey (including a subobject suffix), falling
back to AssetGUID, to Addressables.LoadAssetAsync<Sprite>. It never passes the game-owned
AssetReference object and never calls ReferenceToSprite.GetAsyncSprite, GetSprite, Release,
or IsLoaded. The latter APIs depend on or alter the game's private load request.

One owned handle exists per distinct key. Tick polls completion on the Unity thread; failed or
30-second timed-out attempts release that handle and retry with bounded delay, at most three
attempts. Successful handles remain pinned until Shutdown and periodically re-register after
mirror-registry resets. There are no completion callbacks capable of registering stale results
after Shutdown. Warnings are deduplicated and limited to eight contexts per map lifetime.

Sprites normalize through CardFaceMipBake.OriginalFor, RawImage textures through the read-only
PanelMipBake.OriginalFor. Both original sprite and texture are registered through Assets.Key.
No completion-order-dependent GUID primary key is introduced for shared atlas textures; the
existing descriptor aliases remain the wire identity. Unsupported generated textures remain
outside this original-asset loader and require the mirror's explicit provenance handling.

## Validation

Strict Release compilation against the actual game/Unity reference assemblies passed with zero
warnings and errors. Source inspection verifies native field/method signatures and ownership:
ReferenceToSprite.GetAsyncSprite replaces its private request, while the chosen string-key
Addressables call owns a separate handle. No hardware/load-timing claim follows from compilation.
Integration must still exercise first remote opening before local card art has loaded, selected
and disabled state changes, portrait changes, map exit during a pending load, and re-entry.
