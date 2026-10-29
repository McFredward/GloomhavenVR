# Custom environment scope in actual Unity

Run `python3 scripts/check-environment-menu-scope.py` from the checkout. This is a
local Editor/OpenGL suite; `--production-only` is an explicitly partial development
run. The full run compiles the complete production `SkyAlternative` class for the
positive case and eight causal controls, then executes them in one Unity session.

The real owner performs its normal prefab instantiation, node split, mod-layer
assignment, collider removal, sky placement, room placement, retained-room tick,
rig-identity handoff and teardown. Menu photographs are rendered by a mod-only
head camera. They must contain the selected room's pixels, including immediately
after a live style switch. Independent floor/pose checks retain a fixed room under
head movement, rig motion, zoom and same-rig recenter. Replacement menu/map/scenario
rigs must build new presentations, and native map/tile positions and normalization
remain unchanged. OffBlack, Default return, MR stand-down/resume and VR stop are
covered without invoking gameplay controllers.

Controls restore the old table-only gate, remove the menu subject, bury its floor,
retain the previous rig's room, make the room follow the rig, or let the menu subject
override a genuine native table. Two more remove intro admission and closed-room
eye centering. Each must reach its named runtime assertion;
compile/import failures are never accepted as controls.

Native menu readiness/config/session/audio/Haunt/MR-owner boundaries are explicit
ports. Small generated prefabs retain the existing native map/scenario reference
checks. The actual `prebuilt/gloomhavenvr.bundle` then supplies both shipped
`RoomGeo`/`PlaySpace` prefabs. Their mesh/transform data execute unchanged, including
the cellar's central `Floor` and `Ceiling`, outdoor woods and seated hardware pose.
The eye must start horizontally centred and midway between the closed room's
surfaces; an open-air room retains its completed tracking floor. Real head pixels
show the retained room through player movement. Only the Windows-authored room
materials are replaced by an explicit unlit GL color witness.

The root-owned native menu/recenter admission producer, original shader
appearance, Default sky, HMD timing, peer synchronization and audio remain outside
this fixture. Original Default rendering has separate coverage.

Every attempt retains source/assembly/input/result/capture evidence and uses
`cleanup_completed_run` after killing and reaping its own editor process group.
