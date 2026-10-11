# NPC660 exact original material identity

Base: Build659 `c0b5e9a1b`. The new paired hardware inputs actually identify
Build658 `e92ef8e53`, built2026-10-09 18:45:20 UTC, on both peers. The current
regression therefore exercised the last NPC changes. It is not explained by
659's unrelated pillar follow-up. Both Player logs retain Debug records; the
normal LogOutput files do not contain the same detailed census.

## A complete transfer can still be permanently undisplayable

The frozen host Player log reports23 required/23 received originals while
validation refuses `texture|T_sphere_norm|512|512|4|10`. Later transactions have
30 required/30 received originals and refuse
`texture|Default-Particle|64|64|4|7`. Retrying the exact full frame cannot repair
this: `TownServiceAssets.Scan` has already marked the descriptor ambiguous and
`Resolve` refuses it before binding. This is independent of transport bandwidth,
the first-picture deadline, native-basis receipts and the remote's local quest.

Broad discovery can expose different Unity object wrappers for one serialized
original. The old registry correctly rejected unaudited same-name textures,
but these two dormant FX dependencies were not in its finite audited set.
Build658's complete enhancement property capture makes those dependencies
relevant even when their native branches are hidden. Removing hidden native
material fields would weaken original parity and cannot be the repair.

Read-only inspection of **every** original `GH_Data/*.assets` found exactly one
serialized Texture2D for each dependency:

| Native original | File/pathID | Exact descriptor | Original object SHA256 |
| --- | --- | --- | --- |
|Default-Particle|resources.assets/207|64×64,RGBA32,7mips|`158aa1994ac5bea51f5d10867238a1200ab1494e8adf7a72b6b6568d800931ca`|
|T_sphere_norm|sharedassets1.assets/364|512×512,RGBA32,10mips|`db03bb0e2c267283e52cbf30648751ce52ebea370774fa28adb9c98319fd7a70`|

The production registry now recognizes only those full audited descriptors,
using the existing native identity contract. Unknown collisions and wrong
geometry/format/mips remain refused. Native template registration consequently
uses each canonical original descriptor regardless of model borrowing order.
No game reference, texture pixels, original material or controller is modified.

## The original procedural body has a separate canonical factory identity

The actual local `VRCard.Build(null)` builds a valid procedural backing. The
delivery lane now reproduces that exact factory and the source's retained size,
instead of requiring an optional bundled prefab that the local card never used.
Its back-pattern material references one cached original
`CardMesh.GetBackTexture()` across ability/item consumers and owner sizes.

Previously `TownServiceTemplateAssets.Texture` gave this shared texture the
first borrowed template's path. An owner starting with a small map card and an
observer starting with a different body/inspection card therefore hashed
different keys for the same omitted immutable material. This is a real source
identity defect; the supplied basis-rejection lines do not identify every
individual property responsible for each rejected module.

`CardMesh.IsOriginalBackTexture` checks reference identity against the actual
cached source object, without creating a material, texture or new wrapper.
Template registration assigns that object the stable factory key
`native-town|procedural|CardMesh/GetBackTexture` before material sampling.
An arbitrary texture with the same name receives no such identity. All actual
pixels, native body contours and owner dimensions remain unchanged.

## Runtime evidence and its boundaries

`python3 scripts/npc660-assets-runtime/run.py` exports the two unchanged native
Texture2D objects into four distinct outer Unity AssetBundles. It retains their
serialized bytes and external compressed pixel streams; no texture import,
repack or generated surrogate runs. The manifest records original asset/object
and generated outer-container hashes. Unity loads genuinely different object
wrappers for each original, preserving their real non-readable/GPU-only state.

The final `proof/run-tl27l9uq` passes27 assertions using the actual registry,
original template registration and `TownServiceMaterial.Read/Validate/Apply`:
duplicate discovery remains resolvable, reverse model borrow order retains the
same key, and GPU-read original/copy pixels are identical. Independently removing
only either audited identity reproduces its exact hardware `Resolve` failure.
Wrong geometry and unrelated same-name textures remain refused.

The proof declares an ordinary Unlit material and a no-boundary single-root
surface. It proves the actual native dependency failure and material binding,
not the full enchantress gameplay constructor, original FX shader, network
deadline or headset output. The delivery lane separately exercises the complete
production CardMesh factory, reverse owner-size order and actual transport;
the integration retains the existing native45/82 enhancement scopes.

The first proof failed strict compilation on an unchecked nullable material.
The next negative control failed at `Key` before its intended `Resolve` assertion.
Both receipts remain; the final control reaches `Resolve` first, reproducing the
reported failure without accepting an unrelated exception as its expected result.

The already hardware-confirmed stable enhancement overlay/depth implementation
is unchanged. This patch adds no timer, diagnostic stream, wire record, gameplay
callback, substitute art or new local visual exception.
