# Frame636 atmosphere and steady-frame follow-up

The supplied Steam Frame capture runs ModBuild636, source `de12a1e8f`.
Both Frame banners were checked and all four supplied screenshots were inspected.
The top-level PC capture is634 and the remote capture629; neither is a paired636
performance or multiplayer result. The screenshots retain detailed pale plaster,
wood and stone textures beside dark native masonry. They do not establish missing
texture references.

## Cause and material repair

All 15 native procedural map prefabs and 114 editor map prefabs require the exact
`ApparanceMap`/`ProceduralMapConfig` ancestry components. The native ProcGen Maps
root also carries `ProceduralPlacementNotifierHandler` and
`LightShadowsModifierController`. The material owner's conservative unknown-script
guard rejected those four types, so its scenario candidate and variant counters
remained zero. Terrain's separate variant factory still substituted approximately
61 sources per eye within its64-source geometry budget. Its camera/frustum
selection could switch the same wall between the original dark native lighting
and mode2 raw pale albedo. This matches the reported angle-dependent appearance.

The owner now admits those four exact coordinator types. Their native callbacks
remain active. Actor, door, interactive, animated, particle, UI, unknown-script
and foreign-mask exclusions remain independently enforced on every descendant.
Supported native material coverage does not depend on the terrain geometry cap
or camera frustum. Geometry admission, collision and room visibility are unchanged.

Mode2 now optionally multiplies texture/tint by original scene SH ambient light
evaluated at vertices. It still omits normal/MRAO shading, specular, reflections,
additional-light passes, received-shadow lighting and all per-pixel light work.
Mode1 retains its existing lighting equations. Native UV, color, dissolve, clip,
fog and shadow-caster contracts remain covered by the shader tests.

Live `[Optimize] WorldMaterialAmbientPercent` defaults to100 on every platform:
100 uses original ambient light, 0 retains raw textured color, intermediate values
blend both. It applies only to mode2. Bind preserves saved entries; explicit
graphics profile selection resets it to100, after which individual edits persist.
The shared PC/Frame binary exposes the option in English/German VR settings and
reports it with quality diagnostics. Modes0/1/2 remain independently selectable.

## Additional CPU work removed

Across the five fully settled three-room windows:1,017 frames and67.8 printed
seconds, frame-weighted mean66.563ms, measured depth0 mod35.349ms. Terrain pre-cull
costs13.272ms/frame, environment pre-cull3.968ms and WallFade.Late4.908ms.
Nested wall phases are not additional costs. The GPU field follows frame interval
and does not establish GPU busy time or headroom.

Terrain captures head/hand poses and detail settings once per synchronous Update,
while preserving per-source bounds/mesh validation. Each camera reads native MPB
presence freshly; absent blocks skip copies/effect reads, and previously copied
private overrides are cleared when native blocks disappear. Slot precedence and
late native color/texture/effect changes remain immediate. Both NeverFade channels
are set consistently. See `FRAME-636-TERRAIN-CPU.md` for operation-count evidence.
World material processing reads the renderer-wide MPB once for all its subslots;
slot blocks and all mutable guards remain fresh per eye. Factory refreshes now
have a separate guarded Debug counter instead of disappearing from owner totals.
These are redundant-call reductions, not new appearance compromises.

The requested eye scale0.8 was refused by the provider. The latest run still used
effective1.0 and3408x3408 per eye. This change does not claim resolution savings
or alter XR target allocation. The existing default0.8 remains configured.

## Package and validation

The independent Windows shader bank explicitly compiles in the native game's
Gamma color space. `ShadeSH9` chooses its conversion at compile time; the asset
project's Linear setting cannot be inherited accidentally. The builder restores
its previous setting before exiting. A private compiler callback verified all576
World programs and both cheap-terrain programs use `UNITY_COLORSPACE_GAMMA`.
Final package SHA256:
`1912a73029e6d05878fec56a2354bcd71866bcdac371411fd1e7bdfcf417492a`
(56,781,291 bytes). Main/town bundles and all3,170 immutable geometry streams stay
unchanged. Actual Unity loading verifies both shaders, all streams and lookup paths.

Focused evidence:417 shader assertions with seven addressed original materials
including DLC and native Gamma settings;26 combined shader causal controls;
559 world-runtime assertions and37 combined controls;839 native ancestry assertions
and four source controls;335 terrain assertions and57 combined controls.
Partial reruns replace only affected fixture controls, preserving failed attempts
and distinguishing combined evidence from a fresh complete pass. Root additionally
checks settings/help/UI/diagnostics, integrated source bindings and strict builds.
Successful636 evidence is inherited for unchanged areas; no new149-suite pass is
claimed. Final source surfaces retain every existing config/patch/log token and
add only the ambient setting.

The raw hardware audit and source-bound receipts are archived under
`.planning/debug/frame636-followup/`. Original failed package attempts are retained:
the minimal packing project initially lacked the AssetBundle module/container;
actual package validation caught this before handoff. The final package has all
3,173 named asset paths. The new World shader compiles without warnings/errors;
the unchanged cheap-terrain shader retains one existing D3D warning.

The supported source contracts and engine fixtures do not establish corrected
Frame pixels, a frame-time saving or multiplayer acceptance. The next hardware
run should inspect the same walls from both sides with mode2/ambient100, plus a
different biome/DLC, and compare fully loaded one/three-room windows with fixed
settings. Confirm nonzero native World coverage and both-eye terrain work.
Environment validation and wall reclamation remain measured follow-up targets.
