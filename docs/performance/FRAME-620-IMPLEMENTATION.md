# Build620: PC graphics follow-up and accurate room loading

The hardware baseline is the supplied Gaming PC Build619/`6e36062e3` capture.
This is a follow-up to that report, not a new Frame FPS benchmark. The original
inputs and hashes are retained in `.planning/debug/frame620/inputs`.

| Report | Implementation and evidence |
| --- | --- |
| Square wall transitions, unchanged by profiles | Exact pre-619 continuous noise and wall/prop cutoff ramps restored; the native enable input is still written. The removed point-filtered rank bank was independent of graphics profiles. [Wall source equivalence and pixel limits](FRAME-620-WALLS.md). |
| Paper/skulls unaffected by decoration slider | Read-only whole-game census: 2,144 PCG/material bundles, 47,754 mesh records and 137 projectors. Separate ornaments within structural prefabs, generic original floor/collider hierarchies, loose clutter and identified paint projections now receive the reversible decoration budget. [Coverage and protected objects](FRAME-620-SCENERY.md). |
| Profile explanation clipped | The original note uses its complete row, wraps at readable authored size, and grows the native layout. Other short notes keep their existing style. Complete production `BuildNote` executes in native extended layouts with actual TMP glyphs and camera pixels: 462 assertions and three causal controls. EN/DE at 760 px use two lines; at 440 px they use four, without clipped ink or overlap with the next row. |
| Simple shading hard to find / darker floors | Ordinary Graphics → Presentation has an explicit bilingual caption. The control simplifies audited opaque floors and eligible static masonry; native fading walls, water, figures and UI retain their shaders. Surface lighting, shadow and texture detail can decrease; this is an optional quality trade, not equal shading everywhere. |
| Legacy map animation control | Removed from curated and advanced menus. The stored key remains bound but INERT for upgrades; active 3D map hover keeps native highlight, 20% growth and selection, with particles suppressed. Original 2D map behavior remains outside that gate. |
| Developer-facing / outdated setting hints | Player-facing help now takes precedence in curated row hints, headings and advanced catalog hints/tooltips. Coverage includes exact and dynamic-family bindings, EN/DE and English fallback. Raw diagnostic config descriptions and saved keys remain. [Help coverage](FRAME-620-TOOLTIPS.md). |
| Revealed-room spinner persists after loading | The observer follows actual unfinished native async operations and generation, not empty cached material slots. It closes on completion without a quiet timer or waiting for background cosmetic/ghost preparation. [Loading cause, replay and hardware limits](FRAME-620-LOADING.md). |

The code does not change native gameplay/load flags, authority, wire grammar or
local/remote card-face permissions. Existing 2D-map and original-window paths
remain intact. Cosmetic budgets stay optional and reversible on PC and Frame;
this round does not replace native walls with the simpler floor shader.

The complete final-tree validation and bounded failure/resume history are kept in
`.planning/debug/frame620/validation-ledger.json`. Only independent source/runtime
checks establish automated outcomes. The isolated note fixture uses the real
layout implementation and editor font glyphs, with explicit row-art/style boundaries;
it does not claim a screenshot of the original game menu or headset. The restored
Windows native wall bytecode is not executed by the Linux pixel surrogate.

The nine Error rows in the supplied log are two pre-bundle shader-resolution
messages and seven bounded original material-healing failures for the same cult
chain material. Overlay resolution later succeeds. These rows are not evidence
of a profile/menu deadlock or proof of the exact old pending generation state.
Further hardware results must keep those concerns separate from FPS attribution.

Next hardware checks: open a new room and verify the spinner ends with native
loading; wall OUT/IN animation on PC and Frame; decoration-zero skull/paper and
composite floors without missing collision; profile note readability; find and
retune simpler environment shading; inspect ordinary and advanced tooltips in EN/DE.
