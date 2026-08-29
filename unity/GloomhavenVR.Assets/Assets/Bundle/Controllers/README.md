# Controller models

The VR controller a player is actually holding, so the controls lesson in the first tutorial
can point at a key on *their* device instead of describing it in words.

## Where they come from

[`immersive-web/webxr-input-profiles`](https://github.com/immersive-web/webxr-input-profiles),
`packages/assets` — **MIT**, Copyright (c) 2019 Amazon. The full licence text ships next to the
models as `LICENSE-webxr-input-profiles.txt`. These are the vendor-supplied models a browser
already draws for each controller, so they are accurate rather than approximate.

The upstream README carries a trademark note worth repeating: the models depict registered
trademarks in order to portray the physical devices accurately, and the licence does **not**
grant permission to re-skin them or to use the marks for endorsement. Showing a player their own
controller is exactly the permitted use; nothing here is re-skinned or re-branded.

## What ships

| Device | Profile | Triangles | Albedo | Normal | Metallic / roughness |
|---|---|---:|:-:|:-:|---|
| Meta Quest 3 | `meta-quest-touch-plus` | 4 470 | ✓ | — | constant 0 / 0.55 |
| Pico 4 | `pico-4` | 4 525 | ✓ | — | constant 0 / 0.55 |
| Valve Index | `valve-index` | 8 933 | ✓ | — | map (metallic 0.34 avg) |
| fallback | `generic-trigger-squeeze-thumbstick` | 5 945 | ✓ | ✓ | map (0.42 / 0.42 avg) |

**At ~4.5 k triangles these meshes are smooth**, and a flat-shaded untextured preview of them is
not evidence about the model — that was the first thing this folder's renders got wrong. What
*does* make a controller read as cardboard is shipping the albedo alone: BoardLit's specular
branch stays off, and no light ever catches the plastic. So every material now carries a
metallic/roughness pack.

**Every number in it comes from the source material, factors included.** glTF multiplies the pack
by `metallicFactor` / `roughnessFactor`, and that is not decoration — see below. Where a profile
ships no pack at all (Quest, Pico: metallic 0, roughness 0.5528) a 4×4 constant is written from
the factors, because BoardLit's "no map" default is metallic 0 / **roughness 0**, which is a
mirror rather than plastic.

### `meta-quest-touch-plus`, not `-v2`, and the reason is measured

v2 ships a metallic/roughness map that v1 lacks, which looked like a straight upgrade until both
were opened:

* v2's **base colour averages RGB 19** — a near-black texture for a controller that is white.
  v1's averages 167.
* v2's pack reads **metallic = 1.0 on every pixel**, which its own `metallicFactor` of **0**
  cancels exactly.

Taking v2 would have traded a correct albedo for a black mirror. It is also what proved the
factors have to be applied rather than ignored.

## Valve's Steam Frame

**Recognised, named, and wearing the generic model — there is no other honest option.**

* The profiles registry has no Valve entry beyond the Index.
* Valve's own Unity package, [`ValveSoftware/Unity`](https://github.com/ValveSoftware/Unity),
  ships the interaction profile (`/interaction_profiles/valve/frame_controller_valve`) and **no
  art whatsoever**.
* Valve's guidance is to fetch the model from the **runtime** — `XR_EXT_render_model` /
  `XR_EXT_interaction_render_model`, or OpenVR's `IVRRenderModel` — rather than ship one, so that
  future devices work without an update.

Dressing it in a Meta controller because the two are shaped alike would show a Valve owner
somebody else's hardware, which is the one thing the trademark note above asks nobody to do.

Its four top inputs are a **D-pad**, so the lesson words those steps for it: Valve's
Touch-compatibility mapping sends A/X to the *bottom* of the D-pad and B/Y to all three of the
others ([Steamworks](https://partner.steamgames.com/doc/steamhardware/steamframe/controllers)).

The clean upgrade, when a runtime that exposes it is in the loop, is runtime retrieval — which
would cover the Frame and every device after it.

## Pipeline

    python3 unity/GloomhavenVR.Assets/Assets/Editor/controllers_pipeline.py [device ...]
    scripts/build-bundles.sh          # after ControllersBuilder has run

`controllers_pipeline.py` downloads each `.glb`, converts it to **one OBJ per key per material**
(that split is what makes "this key, now" a material swap at runtime), exports the albedo, the
normal map and a metallic/roughness pack **repacked from glTF's B/G into BoardLit's R/G**, and
writes `controller.json` with the parts, the materials, each key's anchor, and every mesh's
bounds and signed volume.

`BuildControllers.cs` (menu: *GloomhavenVR ▸ Build Controller Prefabs*) assembles one prefab per
device and hand, with one child per key.

## The importer is checked, not trusted

Unity's OBJ importer treats OBJ as right-handed and **negates X**. That is a defect that looks
almost right — every key in a believable place, on the wrong side of the controller — and the
first build caught it because the manifest carries the bounds the pipeline wrote. The builder
undoes the mirroring and decides *from the imported mesh's own signed volume* whether the winding
also has to be reversed, then re-checks bounds and volume before saving. Both numbers are in the
manifest precisely so neither can be talked out of by the other.
