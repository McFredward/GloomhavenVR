# Custom environment scope in actual Unity

Run `python3 scripts/check-environment-menu-scope.py` from the checkout. This is a
local Editor/OpenGL suite; `--production-only` is an explicitly partial development
run. The full run compiles the complete production `SkyAlternative` class for the
positive case and six causal controls, then executes them in one Unity session.

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
override a genuine native table. Each must reach its named runtime assertion;
compile/import failures are never accepted as controls.

Native scene/config/session/audio/Haunt/MR-owner boundaries are explicit ports.
Small generated prefabs implement the existing `RoomGeo`/`PlaySpace` contract.
This suite proves current production lifecycle and placement plus actual Unity
pixels for that geometry; it does not certify bundled environment art, the original
Default sky, headset appearance/frame pacing, peer synchronization or sound.
Original Default rendering has separate coverage.

Every attempt retains source/assembly/input/result/capture evidence and uses
`cleanup_completed_run` after killing and reaping its own editor process group.
