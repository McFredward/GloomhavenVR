# Native card art regression, build 491

Base: `48330b22` (`dev`, build 490); isolated worktree `mp491-art`. Shared additive69
DTO/codec dependency integrated from the root's `4a4b2115` checkpoint.

## Hardware evidence

Both main and remote `LogOutput.log` banners at line 17 identify build **490**.
`regression_3d_umgebung.jpg` shows an unprinted brown local fan and patterned remote backs;
`regression_board.jpg` shows an incomplete card presentation on the remote scenario board.

The logs identify a concrete construction failure, not a delayed texture download:

- Main: **19,876** occurrences of `RemoteCardArt clone failed (Original card group count
  exceeds appearance bound.)`; first occurrence at line 12756.
- Remote: **11,209** occurrences; first occurrence at line 2282, immediately before the
  map fan's census reports backs even though its gate is open.
- Both logs: **zero** `Remote card FACE path =` and **zero** `REMOTE FRONT: applied`.
- Native owner sampling also fails on this bound: 38 aggregated error reports on the
  main client and 33 on the other client. These are reporting windows, not failure counts.

`RemoteCardArt.ShowFront` constructed `CardAppearanceBindings` before skin repair and host
activation. That constructor rejected more than eight descendant CanvasGroups. The catch
then destroyed the clone before `OnEnable` could start its own artwork loads. Local map
cards use this same renderer and therefore retained their bare brown VRCard bodies; remote
map cards retained their patterned bodies. `RemoteBoardCard.Set` fell back to its old
name/initiative panel after both real-front paths failed. Its front census counts permission
and fallback state and must not be interpreted as proof that original artwork was drawn.

The bound was introduced with the untested build 489 native appearance path; the previous
hardware evidence was build 488. Build 490 was the first supplied hardware test of that path.
It is inaccurate to attribute all these failures only to build 490's caching changes.

## Repair

Binding discovery and local reset capture now accept the game's complete dynamic hierarchy.
Discovery refreshes before capture and playback so added or removed action-content groups
cannot leave stale bindings. Existing record58 still carries its original eight group roles;
additive69 carries the remaining groups in the same atomic appearance snapshot. Reset,
interpolation and local frozen-flight output include those additional groups too.

The 64-group total is a **wire capacity**, not a claim about the native prefab's maximum.
`CreateLayout` adds per-action consume/infusion widgets with their own groups; only the game's
managed references are available here, so an asset-wide maximum cannot be measured. Local
construction and capture also accept 65 groups and beyond, without truncation. A card that
cannot be published logs its actual group count and fails independently; it cannot destroy
original artwork or prevent other cards from publishing their appearance. The next hardware
logs can establish whether any original card actually exceeds the transport capacity.

A separate source-proven reset hazard is fixed: build 490 captured reset defaults immediately
after `OnEnable`, when `ImageAddressableLoader` temporarily sets content groups to alpha zero.
Reapplying that initial snapshot after artwork arrived could hide it permanently. Defaults and
first owner playback now wait for the header/action backgrounds and zero outstanding loads.
This hazard is verified from the loader/reset code; the supplied screenshots do not distinguish
it independently from the earlier construction exception.

Original artwork loading remains functional: the clone keeps its own `FullAbilityCard`, action
halves and addressable loaders; skin repair restores their nonserialized runtime references
before activation. Its `OnEnable -> ShowCard` starts independent header and action loads.
Recycling the borrowed source cannot recycle those clone loaders. The retained clone continues
polling sprite arrivals and the existing 250 ms art-heal path; no gameplay callback is added.

## Validation and the previous blind spot

Strict Release: **0 errors, 0 warnings**. Root integration runs the complete required gates.

`scripts/card-bindings-tests.sh` compiles the **production** `CardAppearanceBindings.cs`
against a minimal tree/material API. It executes 883 assertions covering 9, 64 and 65 groups,
dynamic additions/removals, every group's binding/alpha/flags, inactive intermediate holders
and detached local roots. The script also injects the former eight-group exception into a
private temporary copy; the ninth-group runtime test must fail with that exact exception.
This is a real failing negative control, not a source-text assertion of the intended fix.

The tree harness does not emulate Unity rendering, Addressables, shader output or headset
pixels. Root wire tests separately exercise complete 64-group frames and malformed additive69
records. The earlier suite only created legal eight-group DTOs and checked source literals;
it never executed binding discovery on a hierarchy outside that assumed bound. Those green
checks therefore missed the actual constructor failure.
