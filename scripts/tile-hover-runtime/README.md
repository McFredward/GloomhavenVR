# Native laser tile hover admission

Run `python3 scripts/check-tile-hover-runtime.py` with the installed game references and Unity
2021.3.5f1. This is a local fixture; portable CI reference assemblies are insufficient.

The complete production `HoverPickPatch` and `TileHoverAdmission` execute with the original
game's `HoverRegisterer` fields/lists, `IHoverable`, `Controller` identity and rules objects.
The original serialized `Hex` convex mesh and `BearTrap` box are freshly recovered from
read-only `resources.assets`. Real physics reproduces a trap collider hit while the actual
laser points just outside its tile. The nine scale/yaw combinations cover adjacent tiles,
the outer board edge, native edge geometry, UI exit pairing, exact prop identity, rules
replacement, initial authored objects, spawners, pointer loss and unchanged fingertip reach.
An intentionally different active-selection mask proves passive hints retain the native
hex-selection mask used by `WorldspaceStarHexDisplay`.

Five independently compiled causal controls restore collider-only admission, omit an exit,
accept another same-prefab instance, use the active-selection mask or lose spawner membership.
Compilation failures do not count as passing controls.

Hardware input sampling, logging and the native UI input gate are explicit fixture boundaries.
Native gameplay callbacks and tooltip-window pixels are outside this focused proof. The
fixture publicizes its native compile reference to access the internal `IHoverable` interface;
the runtime loads the original native DLL. It does not establish a headset outcome.

Each completed run kills/reaps its owned process group and calls `cleanup_completed_run`.
InputSystem's generated SDK directory named `Remote` is moved verbatim to retained source
evidence first, with hashes, so the shared hardware-evidence guard can remove `Library`
without confusing package source with the other player's logs. Inputs, sources and results
remain available after successful, failed and interrupted execution.
