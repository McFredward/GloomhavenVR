# Doorway/archway ("Torbogen") fade experiments — PARKED

**Status (user ruling, 2026-08-02 — supersedes all previous doorway rulings):** doorway
handling in `src/GloomhavenVR/Core/WallSegmentFade.cs` is reduced to RECOGNITION only.
Archway/doorway segments NEVER fade — no open/closed differentiation, no hard hide.
They are always solid. What remains in the code is the per-door spatial linkage
(`FindDoorwayRoot` + `Segment.DoorRoot` keying) so archway renderers can never merge
into a fadeable wall segment.

This file documents what was tried across three rounds so a later version can pick the
feature back up without re-running the failed experiments.

## Round 1 — ancestor-walk sibling adoption (FAILED)

Idea: from each fade-capable archway frame renderer, walk up to a prefab-ish "asset
root" and hide the non-fade siblings (wooden wings, trim) with the wall's fade.

Result: hardware log showed "+0 siblings" for the doorways. Root cause: archway frames
do NOT parent under a common per-doorway asset root — they parent FLAT under the
'L :' Apparance section/layer container, so the ancestor walk's renderer-count cap
(container detection) tripped before finding anything, and correctly attached nothing.

## Round 2 — per-door spatial linkage + door-state gating (PARTIAL)

- Linkage: a fade renderer whose AABB lies within 2.2 wu XZ of a
  `UnityGameEditorDoorProp` root ('ThinDoor : (guid)') is that doorway's frame/pillar;
  all renderers of one archway grouped into ONE per-door segment keyed by the door
  root. (2.2 wu mirrors the game's own `FindWallsNear(pos, 2.2f)` door-wall search.)
  This part WORKED and is what survives today as the recognition layer.
- Door state: animator "Open" state via
  `MF.GameObjectAnimatorControllerIsCurrentState(root, "Open")` (the game's own check
  from `Choreographer.OpenDoor` / `UnityGameEditorDoorProp.OnCursorEnter`); fallback
  the rules-side `CObjectDoor.DoorIsOpen` via `UnityGameEditorObject.PropObject`; any
  throw ⇒ closed (fail-safe solid).
- Behaviour: door CLOSED → archway held solid regardless of coverage — WORKED,
  user-confirmed. Door OPEN → shader fade (MPB) of segment + door subtree — FAILED
  partially: only fade-capable (`Amp_Basic_WallFade` family) renderers can fade; the
  wooden wings, glow lights and the doorway entity's non-WallFade stone stubs carry no
  fade path and survived every MPB (torbogen.png / torbogen2.png: wings + two glows +
  a floating wall chunk).

## Round 3 — hard hide of the whole assembly (WORKED MECHANICALLY, REJECTED)

Open doorway → hard hide (renderer/light disable + particle Stop+clear) of:
1. the archway's own fade renderers,
2. the door prop root's COMPLETE subtree (any Renderer type + Lights + ParticleSystems),
3. a conservative AABB sweep for container-mates (the doorway entity's stone stubs,
   candles, banners generated flat into the same 'L :' container): enabled, non-fade
   shader, AABB wholly inside the archway box expanded by 1.0 wu, above the ground
   band, provably same container (under door root / sharing a fade renderer's parent /
   same `ProceduralMapTile` ancestor), no wall/actor/tile-logic ancestry.

Full ownership/restore discipline (one owner per component, leavers restored, exact
restore on unfade/door-close/teardown).

REJECTED by the user (torbogen3.png round): it "pops" abruptly — renderer `enabled`
toggles have no dissolve ramp — and the light sweep swallowed neighbouring light
sources. Log line: `doorway assembly 'ThinDoor …': 13 renderer(s) … 5 light(s),
6 particle system(s)` — some of those lights/particles were the room's, not the
door's.

## Key source facts worth keeping

- The game's own door hide unit is `GetComponentsInChildren<Renderer>` +
  `enabled=false` on the door-prop subtree (`ApparanceLayer.Create`,
  `UnityGameEditorRuntime.MakeDoor`).
- `ProceduralDoorway` synthesizes per-door wall-stub geometry flat into the section
  container (NOT under the door prop root) — that is where the floating stone chunk
  came from and why a subtree sweep alone is incomplete.
- Door state is readable via the animator "Open" state (entered when the opening
  animation starts, never left); `CObjectDoor.DoorIsOpen` is the rules-side mirror.
- Entrance/exit doors lose their `UnityGameEditorDoorProp` component at spawn
  (`ApparanceLayer.Create` destroys procDoor) — they never open and are not
  recognized as doorways.
- The flat game merely swings the wings open (`Choreographer.OpenDoor` plays "Open";
  nothing sinks or hides) — there is no native "hide open doorway" behaviour to
  mirror.

## Revival sketch

A correct version would need:
- a per-renderer DISSOLVE for arbitrary (non-WallFade) shaders — not an
  `enabled` toggle — so the assembly ramps out with the wall instead of popping;
- a strictly door-OWNED light/particle set: subtree only, no AABB sweep (the sweep is
  what took the room's lights). If the stone stubs must go too, they need positive
  identification (e.g. matching `ProceduralDoorway` output naming), not proximity.

## Git history

- `75f84f0` (and the round-2 merge around it): per-door linkage + door-state gating,
  full implementation.
- `25dd7ec`: round-3 hard hide of the complete archway assembly, full implementation.
