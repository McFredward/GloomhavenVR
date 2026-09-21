# Build 543 anatomical town faces

## Hardware evidence and scope

`gesicht1.jpg`, `gesicht2.jpg` and `gesicht3.jpg` show the build-542 photographic eyes offset from the generated eyelid relief, particularly the merchant's second lower-eye ridge. A static projected atlas does not create an eye socket, movable eyeball, sealed eyelid or oral cavity. This work replaces that topology while retaining the reviewed NPC identities, original costume/body, station lighting and floor contracts.

## Authoring source

The anatomical head topology and expression targets come from official MakeHuman/MPFB bundled data, explicitly licensed CC0. Only asset data is used; upstream addon/program code is not copied into the mod or its tools.

- Official repository: https://github.com/makehumancommunity/mpfb2
- Pinned revision: `b58176c661a9680294eb75f127842cb8378e4974`
- Mesh: `src/mpfb/data/3dobjs/base.obj`
- Targets: `src/mpfb/data/targets/expression/units/caucasian/`
- Licensing: https://github.com/makehumancommunity/mpfb2/blob/b58176c661a9680294eb75f127842cb8378e4974/LICENSE.md and `LICENSE.ASSETS.md`.

The original approved build-542 front/side/back portraits remain the identity and texture references. Actual orbital, nasal, lip and chin loops are fitted to measured photographic landmarks. The original CC0 blink and mouth-target deformation is transformed through the same fit; it is not applied to a closed generated shell.

## Runtime contract under construction

- Existing `Head` and `Neck` bones remain.
- `EyeLeft` / `EyeRight` are separate actual eye pivots, anatomical left/right, local +Z optical forward / +Y up.
- All facial LODs carry `BlinkLeft`, `BlinkRight`, `JawOpen`, `MouthWide`, `MouthRound`, `Smile`, `BrowRaise`, `LidUpLeft`, `LidDownLeft`, `LidUpRight`, `LidDownRight` shapes, 0–100.
- Gaze applies after body sampling. Full blink suppresses additive lid-follow influence to retain closure.
- Teeth, tongue and oral cavity must remain behind the lips at rest and be visible only through the true mouth opening.
- Corneal highlights use actual scene/practical lights, including ForceVertex point-light data with the existing zero pixel-light budget. No fixed painted glint or self-illuminated face.

This document currently records authoring intent and prototype evidence only. Final topology counts, neutral/animated image review, Unity validation, source hashes and bundle details will be added after the assets are actually validated.
