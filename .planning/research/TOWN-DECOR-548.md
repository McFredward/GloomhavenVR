# Build 548: recover the original native work/donation coin

Build-547 logs contain two exhausted loads of GUID
`2c309731defe50f4d84721fd7f50c5c4`, followed by failure of
`Treasure.Clutter.Shelf.Individual#1`. These are the merchant's work coin and the
priestess's donation template. Their absence can make a correctly posed counting
motion appear to grasp empty air; it does not explain all animation complaints.

Read-only inspection of the actual game PCG database and catalog establishes:

- The resource selects `PCG_coin_heads`, whose `coinsingle` renderer uses
  `GoldCoinSingle` (64 vertices). The renderer has no saved materials.
- Its MaterialLoader references the missing GUID. That GUID has no catalog key;
  repeatedly retrying it cannot recover the prop.
- The containing `PCG_Treasure.asset` explicitly depends on the original
  `coinpile.fbx` bundle. That bundle exports the `GoldCoinMat` subasset, including
  the original 1024 x 512 `GoldenCoin` two-sided coin atlas.
- Catalog entry 13308 registers the key `coinpile` with resource type
  `UnityEngine.Material` and internal ID
  `Assets/Content/VFX/Environment/Loot/coinpile.fbx`. Adjacent entries register the
  same key with GameObject and Mesh types. The internal path itself is not the
  registered key; a typed `LoadAssetAsync<Material>("coinpile")` selects the
  original embedded material.

Resolve only this exact resource, renderer name and obsolete GUID to that typed
original key. The loaded material must still be named `GoldCoinMat`; an unrelated
future catalog result fails with the existing bounded retry and diagnostic path.
No game prefab, loader, material or file is modified. No procedural gold fallback
or replacement texture is introduced. Rendering-only clones retain the existing
owner/remote texture provenance and cleanup behavior.

Source fingerprints (SHA256):

- Catalog: `9c76d2e44fb76b81420379bd3a9b71dec569be27fcb9d296956d9e3a3d82e4bf`
- PCG treasure bundle: `8572412873a2585f8edfff90502498c21961114d5447374e1e52ef11e6b50093`
- Native coinpile bundle: `225e6528d30b3396e7efc5b1e422d76ebab93781fe3f16a9d9827521e9ae9fdf`

The Unity fixture reproduces the missing GUID and actual typed alias, verifies
merchant and hidden donation coin construction, original atlas retention,
independence from unrelated missing props, bounded rejection of a changed
material identity, and release of success/failure handles. Ten compiled negative
controls include restoring the obsolete key, removing material identity checks
and broadening the alias to unrelated resources. Actual game rendering remains
part of the next hardware test; the fixture is not a live Addressables capture.
