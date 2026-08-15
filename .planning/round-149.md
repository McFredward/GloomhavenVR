# ModBuild 149 — the 17-item round

User report, 2026-08-15 (hardware test of ModBuild 148, commit `29d8c3d`). Screenshots in
`.planning/debug/`: `feuer3.jpg`, `feuer4.jpg`, `feuer5.jpg`, `Kugeln.jpg`, `Sichtbarkeit.jpg`,
`kerzen.jpg`, `Kellerfenster_Dunkel.jpg`, `Kellerfenster_figur.jpg`.

## The items, verbatim intent

| # | Item | Lane |
|---|------|------|
| 1 | Fire draws threads far up; fire floats above the tree; wants glut visible on the asset beneath; with wind the rays get extremely long; the cellar shelf fire floats above a board instead of on it, and has no glut on the asset | FIRE |
| 2 | Remove the ice sound completely | SOUND |
| 3 | Menu windows must not jump back into view — spawn once in view, then stay fixed | UI |
| 4 | Trees twitch unnaturally for ~1 s during the Air fade-in / fade-out | ELEM |
| 5 | Remove the permanently glowing spheres in the map — the small blinking ones AND the larger ones | GLOW |
| 6 | Remove the forest "eyes" effect completely, incl. all sounds and assets | FIG |
| 7 | The watching figures are still fully textured and fully visible; they must watch from darkness, barely visible | FIG |
| 8 | Walking figures still teleport — loop the walk animation WITHOUT motion and drive the translation from the mod | FIG |
| 9 | The cellar candles now have a visible sphere around them — make it soft | GLOW |
| 10 | The blue ice patches read as water puddles — give them an ice texture (cellar and forest) | ELEM |
| 11 | Hang something on the cellar window bars (e.g. cobwebs) that blows in the wind | ELEM |
| 12 | Earth in the cellar should also sprout ivy on the walls | ELEM |
| 13 | Light in the cellar must brighten ONLY the moonlight from the window, not the whole room | ELEM |
| 14 | Under Dark there is still something bright above the cellar window — remove it | GLOW |
| 15 | The figure looking in at the cellar window must sit lower so its face is centred; also far too visible | FIG |
| 16 | The "door opens at the top of the stairs" effect is broken (a cylinder rises out of the puddle) — delete it completely | FIG |
| 17 | Still no impact sound when the bookcase falls — EXPLICIT user exception granted for a loud bang at floor contact | SOUND |

## Findings before delegation

**Item 1, the floating hearth on the tree — located.** `BuildEnvironmentRooms.cs:9803-9838`.
The snag carries two fires: `Snag0` at `TrunkAt(snag, 0.10f)` with height 1.05, and `Snag1` at
`TrunkAt(snag, 1.85f)` with height 0.95 and **`bedFrac: 0`**. Snag0's flame ends at ~1.15 m and
Snag1 begins at 1.85 m, so **0.70 m of unlit bark separates them** — a patch of fire two metres up
a bare trunk with nothing under it, which is exactly "die Feuerherde schweben über dem Baum" and
exactly what `feuer4.jpg` shows.

**Item 1, the glut.** The bake log's `glut:` line (`:9645`, `:10058`) describes the *light* core
(`FireCoreK * 1.45 x range`), not anything drawn. There is **no emissive ember geometry on the seat
asset** in either room — no coals on the bark, none on the shelf board. The user has now asked for
it three times; it has to become a visible surface, not a light term.

**Item 4, the Air twitch — a textbook instance of this project's own rule.** `EnvGrowth.cginc:582`
and `:586` use `storm` (= `e.air`) as a **frequency multiplier on absolute time**:
`t * (0.612 + 1.05*storm)` and `- t * (0.075 + 0.085*storm)`. `t` is the shared environment clock and
reaches thousands of seconds, so sweeping `storm` 0→1 across the 1 s ramp (`ElementMood.cs:172`,
`:749-752`) sweeps the wave argument by `1.05 * t` cycles — hundreds to thousands of cycles inside
that second. The phase scrubs chaotically for exactly the ramp duration and then locks. The
amplitude terms (`:587`, `:590`, `:597-599`) are innocent. **An element strength must multiply an
amplitude, never a frequency** — the same rule the wind lane had to learn two rounds ago.

**Item 10, the ice — it has no structure at all.** `EnvGrowth.cginc:315-318`:
`GhvrFrostOn = lerp(alb, float3(0.66,0.76,0.94) * (0.34 + 0.95*lum), m)`. A per-pixel lerp toward a
**constant blue**, modulated only by the surface's own green channel. No texture, no relief, no
normal — and both call sites then *flatten* the normal map (`EnvRoom.shader:370`,
`EnvGround.shader:412`, `n_ts.xy *= 1 - 0.62*frost`). A flat, smooth, uniformly blue patch **is** a
puddle. Meanwhile a genuinely good sheet of ice already exists in the repo, on the cellar puddle
alone: `EnvPuddle.shader:375-449` (`IceH`, `IceN`, `IceGrain`, `IceLip`).

**Item 13, the Light leak — half was already fixed, the other half was never reached.**
`EnvElement.cginc:203-215` already sets `GHVR_AMB_LIFT_IN = 0.00`, so the cellar's *ambient* is
bit-identical under full Light. But the whole gain went into `GHVR_DIR_LIFT_IN = 1.40`, and that
directional lift is applied to **every room surface** (`EnvRoom.shader:444`). The cellar's moon is
modelled as an unoccluded directional (stated as a known limitation at `EnvElement.cginc:216-260`),
so Light brightens masonry the moon cannot possibly reach.

**Item 17, the shelf impact already exists.** `EnvSound.cs:1310-1311` defers
`EnvSoundClip.Fall` at phase 0.180 with `gain: 0.080f`, and `Player.log:21575` shows the scheduler
printing all four contact times. So the sound is scheduled and the user still hears nothing — the
fault is in the deferred queue, the gain, or the synthesis, not in the timing.

**Item 3, the window recall.** `ModalFallback.6.MenuGuard.cs:189-324` `TickMenuRecall()`: out of
view for `RecallOutOfViewSeconds = 6f` → re-placed at the head pose. It already exempts windows the
player has grabbed, which is why it only bites the ones he has not.

**Item 7, the figures' brightness — the numbers are in the log.** `Player.log:7469`: the forest
delivers luminance 0.2126 at the figure's chest, so the albedo is multiplied by **0.321** — a third,
not a tenth. The mapping is `DarkFloor 0.045 + LightGain 1.30 x luminance`, clamped to 0.80.
`_MOD_TINT` is present on 6 of 6 materials (`:7447`), so the lever lands. The cellar's own figure
is at 0.100 and the user still calls it fully visible (`Kellerfenster_figur.jpg`), so the mapping
is not merely mistuned at the top end — the game's own ambient
(`ambientMode=Skybox ambientLight=(0.216,0.200,0.188) intensity=0.600`, `:7471`) lights the figure
underneath whatever the mod writes.
