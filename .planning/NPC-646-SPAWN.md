# NPC646 map entry height

## Evidence and cause

Both supplied multiplayer logs report ModBuild645. The frozen host log records a
tracked local head Y of 2.16m and resulting parchment clearance 1.38m; the visitor
records 2.73m and 1.95m. RecenterMap intentionally applied only an upward minimum
clearance correction, so it preserved these elevated tracking origins. The
visitor report of spawning in the cellar ceiling is consistent with that source
and measured entry geometry; no unlogged physical headset tracking mode is
assumed.

## Repair

Map entry and deliberate recenter now apply a signed rig-root correction to keep
the eyes 0.70–1.05m above the parchment. At normal entry scale, this corresponds
to 1.48–1.83m above the map floor. In-range standing poses retain their height.
The table, environment, tracked head/hand transforms, horizontal seat, facing,
world scale and remote avatar convention are unchanged. Each visitor executes
the same local entry solve; no host-only height is broadcast. Later head motion,
flight and world movement remain free, with no continuous clamp. Recenter at a
changed world scale measures table clearance in the actual current rig scale.

## Verification

The existing source-bound Unity map-seat suite compiles actual RecenterMap,
YawOnly and the complete MapRoomSeat. It now includes both measured 645 origins
and additional floor/seated/eye origins, three world scales, three zoom factors
and three local yaws. The production case passes 3267 runtime assertions. Four
compiled controls reject raw low origins, the exact previous upward-only high
origin behavior, double correction and unabsorbed lateral tracking. One initial
run had a wrong expected control-failure message after the new upper-bound check;
production was green. The corrected receipt retains that initial failure and
does not loosen geometric assertions.

Final receipt: main checkout .planning/debug/npc646/spawn/run-u9gkhhhy.
This proves source-owned entry geometry, not headset comfort or all possible
tracking-origin changes after entry.
