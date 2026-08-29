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

| Device | Profile | Triangles | Albedo | Normal | Metallic/roughness |
|---|---|---:|:-:|:-:|:-:|
| Meta Quest 3 | `meta-quest-touch-plus-v2` | 4 470 | ✓ | — | ✓ |
| Pico 4 | `pico-4` | 4 525 | ✓ | — | — |
| Valve Index | `valve-index` | 8 933 | ✓ | — | ✓ |
| fallback | `generic-trigger-squeeze-thumbstick` | 5 945 | ✓ | ✓ | ✓ |

**`meta-quest-touch-plus-v2`, not `-plus`.** Identical geometry, but v2 ships a
metallic/roughness map and v1 does not — and a controller with no specular response reads as
matte cardboard under every light. That is most of what "looks low-poly" actually means when the
geometry is fine: at ~4.5 k triangles these meshes are smooth, and a flat-shaded untextured
preview of them is not evidence about the model.

Missing parts, both deliberate and both recorded in the manifest so the runtime can degrade
rather than guess: the **Index's grip** is a force sensor in the handle that moves nothing, so it
ships as an anchor with no mesh and gets a marker instead of a tint; the **generic** profile has
no face buttons at all, not even anchors, so those two steps are text-only on it.

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
