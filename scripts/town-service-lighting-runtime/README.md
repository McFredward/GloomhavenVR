# Town light ownership and stabilizer boundary

`check-town-service-lighting.py` executes the complete production
`TownServiceLighting.cs`. It also extracts and compiles the unmodified production
`LightStabiliser.AdoptLights`, `ClassifyExclusion`, and `IndexOfLight` methods.
The record storage and Unity objects are explicit test boundaries.

Twenty create/dispose cycles cover the five-light maximum, the shared environment
light, original practical-light activation and visibility, range scaling, and MR
without an artificial directional fill. Actual adoption code must still record
native lights, including another light on the mod layer and one with a town-like
name, while never recording any explicitly owned town light.

The Unity boundary simulates deferred destruction and destroyed-object null
semantics separately from managed reference nullness. Assertions run before each
Destroy request, after duplicate Dispose, after external destruction, and after a
destroyed environment light is replaced. Registry size is inspected to detect
retained references that `Owns` alone would hide after Unity fake-null destruction.

Five compiled runtime mutations remove ownership exclusion, replace it with a
broad layer exclusion, omit practical registration, skip removal of a destroyed
managed reference, or destroy before releasing ownership. Each must fail.

This proves ownership eligibility and helper lifecycle, not final shader lighting,
Unity's vertex-light selection, illumination pixels, or a headset result. The full
stabilizer damping loop is outside the fixture; because owned town lights never
enter its production record list, that loop has no town light to modify.
