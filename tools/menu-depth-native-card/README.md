The optional native card-depth shader preserves the game's original
`GUI/AbilityCard_Shd` programs, properties, dependencies and render states.
Only its identity and literal `ZWrite` state change. During a live card head
capture, the material owner selects the shader's existing alpha-clip variant;
its original fragment program discards alpha below 0.001 after native effects.
The original game assets and materials remain unchanged.

Rebuild the ordinary rendering bundle with the exact Unity editor first. Then
run `derive.py` with the original `misc_gui_assets_all.bundle` as
`--native-bundle`, the ordinary mod bundle as `--rendering-bundle`, and new
distinct paths for `--output` and `--report`. The tool rejects changed native
input pins and verifies every original compiled byte and other shader state.
Adopt the resulting mod bundle and report into the rendering asset directory,
and update `provenance.json` with the final bundle hash and size.

The original card shader has Direct3D11 programs only. Its support is optional
and cannot disable the three ordinary menu shaders on another graphics API.
GL fixture stand-ins verify routing, lifecycle, native-property copying and
pixel clipping; they do not execute or certify the original native card FX.
The byte/state audit is separate evidence. Final headset appearance remains
subject to the Windows hardware test.
