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

## What ships, and what does not

| Device | Profile | Notes |
|---|---|---|
| Meta Quest 3 | `meta-quest-touch-plus` | all keys separate |
| Pico 4 | `pico-4` | all keys separate |
| Valve Index | `valve-index` | **no grip mesh** — the grip is a force sensor in the handle and moves nothing, so it ships as an anchor only |
| anything else | `generic-trigger-squeeze-thumbstick` | fallback; that profile has **no face buttons at all**, not even anchors |

**Valve's Steam Frame is not here, and cannot be.** No openly-licensed model of its controllers
exists — the profiles registry has no Valve entry beyond the Index — and the trademark note above
rules out inventing one. A Steam Frame is therefore shown the generic controller, with its keys in
the right places and named in the text.

## Pipeline

    python3 unity/GloomhavenVR.Assets/Assets/Editor/controllers_pipeline.py [device ...]
    scripts/build-bundles.sh          # after ControllersBuilder has run

`controllers_pipeline.py` downloads each `.glb`, converts it to **one OBJ per key per material**
(that split is what makes "this key, now" a material swap at runtime), exports the base-colour
texture at 1024², and writes `controller.json` with the parts, the materials, each key's anchor,
and every mesh's bounds and signed volume.

`BuildControllers.cs` (menu: *GloomhavenVR ▸ Build Controller Prefabs*) assembles one prefab per
device and hand, with one child per key.

## The importer is checked, not trusted

Unity's OBJ importer treats OBJ as right-handed and **negates X**. That is a defect that looks
almost right — every key in a believable place, on the wrong side of the controller — and the
first build caught it because the manifest carries the bounds the pipeline wrote. The builder
undoes the mirroring and decides *from the imported mesh's own signed volume* whether the winding
also has to be reversed, then re-checks bounds and volume before saving. Both numbers are in the
manifest precisely so neither can be talked out of by the other.
