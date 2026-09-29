# Native service backdrop identity audit and correction

The build-543 hardware log (`.planning/debug/LogOutput.log:1265`) contains one
`TOWN NATIVE ART: resident image` warning about ambiguous `Black_Backdrop`.
This comes from the broad, read-only Image preload in `PrepareRoot`, including
inactive descendants. It is caught per image. It is not a ReferenceToSprite
reflection failure or evidence that this recorded session actually lost a remote
module: the supplied log contains no corresponding capture/apply failure.

The warning nevertheless exposes a real capture defect when those original
backdrops become visible. Their descriptor identity is insufficient.

## Original game evidence

Read-only inspection of `GH_Data/sharedassets1.assets` found:

| Texture path ID | Sprite path ID | Descriptor | Decoded RGBA SHA256 |
| --- | --- | --- | --- |
| 62 | 904 | `Black_Backdrop`, 1920×1080, format 12, one mip | `f05ef8e873dafb34b18f1f0bd5e745ebff410b83b7ada948f1d6f7615fdcaa2e` |
| 282 | 1120 | Same descriptor | `f71d2a0357769b09d9b25bdcfeaef0102f0d51126931781530bba40fb80eb7f9` |

Both compressed payloads are 2,073,600 bytes but differ. Both decoded images differ
too: texture 62 has RGB ranges roughly 8–33; texture 282 roughly 11–13. These are
not interchangeable copies. Both retain native alpha ranges 197–238.

Scene `level9` (Campaign) references these originals in Image components:

| Component path ID | Native object | Sprite |
| --- | --- | --- |
| 14603 | `Campaign Canvas/UI Guildmaster HUD/UI Shop Item Window` | 904 |
| 16976 | `Campaign Canvas/UI Guildmaster HUD/Enhancements Window Variant` | 904 |
| 17053 | `Campaign Canvas/UI Guildmaster HUD/UI Temple Window` | 1120 |
| 15891 | `Campaign Canvas/New Party display/UI Item Confirmation Box` | 1120 |
| 15981 | `Campaign Canvas/UI Guildmaster HUD/Enhancements Window Variant/UI Enhancement Confirmation Box` | 1120 |

The corresponding script pointer resolves to `globalgamemanagers.assets:6946`,
`UnityEngine.UI.Image` in `UnityEngine.UI.dll`. Guildmaster levels 4/5 use the same
two backdrop families. MonoBehaviour custom field trees were unavailable in this
offline parser; the component audit used serialized sprite PPtrs, the readable
MonoBehaviour header/script pointer and the original GameObject/Transform hierarchy.
It did not infer all custom controller fields from a partial type tree.

## Failure path and correction

`NativeTemplates` prepares the merchant before the temple/confirmations. The
registry previously used `texture|Black_Backdrop|1920|1080|12|1` for both originals,
then refused the second texture. Visible `TownServiceBinding.Read` reaches the same
texture via `Key(image.sprite)` and aborts that module's capture. Catching the preload
warning does not repair that later identity collision.

The fix registers only the five verified immutable root backdrop images through
their original native-template provenance before any template preload. It registers
both Sprite and texture, replacing even a previously cached generic sprite key.
A fixed template order makes keys independent of visit order or dictionary order.
Shared original objects retain their first canonical identity; later windows do not
rename them. The two different originals remain separate on both owner and viewer.

A registry generation change rebinds the surviving native template bank after a
network reset. Dynamic card/portrait art and arbitrary duplicate texture descriptors
retain their existing identity rules and collision rejection. No warning is hidden,
and no native Image, pixel payload or transaction state is modified.

## Validation

`python3 scripts/check-town-service-mirror.py --suite asset-identity` passed 43
assertions in real Unity 2021.3.5 plus four compiled negative controls. Evidence:
`/tmp/town544-backdrop-identity/run-te0aedxv`.

The fixture uses different pixel payloads under identical texture names and metadata,
separate owner/viewer Unity objects and reversed template lookup insertion order.
It exercises cached descriptor discovery before binding, all five visible module
captures, wire encode/decode, observer validation/apply, exact original pixel retention,
shared aliases, registry reset/rebind and rejection of unrelated ambiguous assets.
Negative controls remove registration, restore last-window renaming, collapse both
original keys, and retain stale canonical caches through reset; each fails as intended.

Strict Release build: zero warnings/errors. Native hardware multiplayer confirmation
is still required; the supplied warning alone was not called a recorded mirror failure.
