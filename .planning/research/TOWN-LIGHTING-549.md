# Town lighting continuity — build 549

## Hardware evidence and scope

The supplied `LogOutput.log` identifies build 548 at line 17. The merchant pair
`VirtualDesktop.Android-20260924-082136.jpg` / `...082141.jpg` shows a large change
in body/face illumination while both visible lanterns remain present. The user also
reports particularly severe changes on the moving priestess. The eleven supplied
screenshots show merchant/enchantress, so the priestess-specific observation is the
user's report, not independently established by those still images.

The log's light census at lines 1401–1407 records six town point lights, each at
intensity 2.6 and range 525 world units, plus the room's directional light. The
`LIGHT WATCH` quiet result observes only three native lights. Owned town lights are
explicitly excluded from `LightStabiliser`; that result never proved town lighting
continuity. The previous asset rendering gate used a single point light per actor
and therefore never exercised competing-light ranking.

## Reproduction and correction

The new render fixture loads the actual build-548 Linux source-review bundle,
substitutes the production shaders, and surrounds each original skinned actor with
six real, equal-strength point lights. Their radius is chosen to create a ranking
tie. Translating the actor and camera together by only 0.1 mm per frame removes
camera/silhouette motion as the cause of a large pixel change. This is a controlled
reproduction of the light-list mechanism, not a recreation of every hardware lamp
coordinate or a claim to have recorded the user's exact flicker frame.

The archived build-548 `TownNpc` shader reproduces discontinuities on all three
actors. In the same sequence the revised shader remains continuous:

| Actor | Original peak mean RGB delta / 255 | Revised peak |
| --- | ---: | ---: |
| Merchant | 11.48855 | 0.00257 |
| Priestess | 31.18498 | 0.00159 |
| Enchantress | 12.87395 | 0.00183 |

The historical peak varies with Unity's tie ordering between runs; every measured
run exceeded the visual-regression threshold of five RGB levels. The corrected
values remain stable.

Unity's Built-in Forward path selects four vertex lights and approximates remaining
lights using SH, per object. The renderer's moving bounds can therefore change the
representation discontinuously even if no light component itself flickers. See the
[Unity Forward rendering documentation](https://docs.unity3d.com/es/2019.3/Manual/RenderTech-ForwardRendering.html).

`TownServiceLightList` supplies fixed slots for the actual registered town lamps.
Positions, intensity, colour, physical range, activation and visibility come from
those existing lights. The fixed capacity is 32; the shader only iterates the live
high-water extent (normally six, twelve with three additional visitor workspaces).
Empty slots and points outside their physical range are rejected before reciprocal
square-root work. Slot identity is retained until disposal, and peer creation order
does not change which lamps illuminate a surface. Lamps contribute with the former
vertex-light attenuation near the station and fade smoothly over the final 20% of
their actual range, so renderer bounds no longer produce a range cutoff.

Town skin/furniture calculates these contributions per vertex. The very small eye
and corneal surfaces calculate them per fragment to preserve real highlights.
ForwardBase `OnlyDirectional` retains Unity's directional/environment ambient and
probe data while excluding its additional point-light/SH terms, avoiding double
counting the owned lamps. There is no studio fill, new shadow pass, native-light
edit, global pixel-light budget change, or renderer MaterialPropertyBlock write.
The lamp array is refreshed after animation/workspace LateUpdate, before camera
culling, without temporary managed arrays. Last-owner teardown zeroes the shader
count and removes the callback. Unexpected capacity overflow produces one bounded
warning rather than an unbounded diagnostic stream.

## Integration

`TownServiceLighting.SetFlame(worldPosition, slot)` remains the anchor API. Slots
0/1 should identify the actual visible lantern flames after cabinet/layout changes.
Existing owned-light visibility and visitor-practical registration feed the same
list. Rebuild and distribute the town bundle alongside the DLL: old shaders do not
consume the new explicit arrays.

The existing single-lamp asset fixture must also publish its actual lamp to these
globals (and refresh its position/range at the 198× test scale), or compile the
production binder against its fixture layer. An arbitrary unregistered light is
deliberately not interpreted as an owned town practical.

## Validation and limits

Run `python3 scripts/check-town-lighting.py --bundle <Linux source-review bundle>`.
The isolated Unity 2021.3.5 fixture compiles the production binder and lighting
owner, with an explicit queued-destruction boundary because native `Destroy` is
not permitted in Editor mode. Registration/disposal is checked before that queue
is flushed. It covers the three real actors, historical rendered regression
controls, eye/corneal layers, 198× world scale, translated eye viewpoints, peer
lamp-registration order, pixel-light caps 0/4, physical anchor/visibility updates,
retained material properties, inactive/destroyed lights, bounded capacity, native
light isolation, disposal and steady-state binding cost/allocation.

This Linux/GL test does not establish D3D driver behavior, actual headset stereo
comfort, or headset GPU time. Windows bundle import/compilation and the next
hardware round remain necessary. The strict Release DLL build passes with zero
warnings and errors. The final optical/performance suite passes 56 assertions and
three visual regression controls. A 32-live-lamp binding averaged 0.01848 ms of
Editor CPU time, with zero managed bytes allocated over 1,000 binds. This is not
a headset GPU performance measurement. The integration handoff records the final
evidence directory and matching source hashes. This completed run is
`/tmp/town549-lighting-verified/lighting-tchqhqvg`; its log contains no compilation,
shader, rendering or Editor-destruction errors.
