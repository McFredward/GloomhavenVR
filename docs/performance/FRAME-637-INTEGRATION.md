# Build637: native world coverage, atmosphere and steady-frame work

The Frame636 follow-up candidate `5abc3dda7`, based on published636
`de12a1e8f`, is integrated after the original NPC635 repairs. Exact native map
coordinator admission restores supported static-world material coverage without
depending on the terrain geometry cap. Mode2 retains original scene SH ambient
at vertices through live `WorldMaterialAmbientPercent` (default100;0 raw color).
Modes0/1, original geometry, UV/tint, visibility and interaction authority remain.
The independent shader bank explicitly uses the native game's Gamma color space.
See [the source/log diagnosis](FRAME-636-ATMOSPHERE-REPAIR.md) and
[the terrain operation review](FRAME-636-TERRAIN-CPU.md).

Current head/hand/config values are captured once per synchronous terrain Update.
Every eye still reads current native MPB presence; absent blocks skip copies,
effect reads and unchanged private writes. Removal clears old renderer/index
overrides. World reads one renderer-wide block for its current slot loop.
No mutable eligibility, source geometry or native-write guard is cached across
camera callbacks. These reduce redundant operations; they do not establish a
measured headset millisecond or FPS saving.

## Independent integration review

The exact four coordinator types and129 map ancestry tuples are pinned to the
actual supplied bundle bytes and native source identities. World rejects child
actor/UI/interactive/animated/unknown Behaviour ancestry independently. Native
callbacks remain active. The runtime fixture uses explicit controller surrogates;
native839/4 checks establish byte/script provenance, not original controller or
generated-game-scene execution. Terrain's unchanged classifier has its listed
exclusions but not World's universal unknown-Behaviour veto.

The package review found an active delivery gap: keyword declarations and
`m_HasInstancingVariant` do not prove a compiled `INSTANCING_ON` program. The private
packing project stripped ordinary instancing/fog variants; broader world coverage
could feed those materials to explicit environment instancing. The builder now
retains the relevant compiled variants while restoring its prior editor settings.
Final package evidence belongs to `.planning/debug/frame637/`; the historical
candidate bank1912… is retained as the rejected delivery input.

The final bank is56,858,683 bytes, SHA256
`f64bd5813d4c587ae848d6291d6de4d0a3b66ce15babab0953cfbf45119895f9`.
Actual compiled World programs contain2048 forward variants per stage and64
caster variants per stage; half retain ordinary instancing. CheapTerrain retains
8 forward variants per stage. Each forward stage contains fog Off/Linear/Exp/Exp2
with and without instancing. All4240 compiled programs use Gamma. Four stripped
instancing/fog controls and the rejected previous bank fail the strengthened
validator. Real Unity2021.3.5 loads all3170 production-decoded streams and3173
container paths; all3171 TextAsset payloads remain byte-identical. The private
builder restores its previous color-space, instancing and fog settings. The null
graphics loader establishes delivery/decoding, not Windows/HMD shader pictures.

Current OpenXR startup enforces MultiPass, with no configurable SPI path.
The candidate's private repack also stripped old SPI programs; that reduction
must not be described as preserved stereo variant coverage. Original main/town
banks remain untouched; unchanged main-shader keyword declarations likewise do
not certify their compiled ordinary-instancing delivery. Editor render fixtures
and actual Windows shader-bank records are separate evidence.

## Validation and handoff limits

Source-bound candidate evidence is417 shader assertions/26 combined controls,
559 world assertions/37 combined controls,839 native assertions/4 controls,
17 wiring contracts/9 controls and335 terrain assertions/57 combined controls.
Six affected settings/UI/diagnostic local suites pass. Already passing unaffected
checks are inherited; this is not another full149-suite run. Raw failed attempts
and source hash dictionaries remain in the verified archive under
`.planning/debug/frame636-followup/`.

The main integration source15/15,296633 golden assertions, bilingual docs5 pairs
and strict Release/Debug builds pass with zero warnings/errors. The full-wrapper
149-suite repetition was cancelled before any suite completed; its cancelled
report is retained and is not counted as passing evidence. The already-built
golden executable was run separately with the correct local runtime environment.
Compiled636→637 retains1228 types:11 intended material/terrain/configuration
types change plus8 inlined build constants, with no added/removed type or changed
NPC behavior. Config671/patches221/log tokens4791 retain every636 surface and
add only the ambient setting.

Install the final updated environment bundle with the assembly. Both peers
must install637. Compare mode2/ambient100 after full loading, moving around the
same walls; also inspect another biome/DLC and one/three-room windows. Debug
coverage must become nonzero for supported native world sources. Keep other
settings fixed where possible; requested eye0.8 in the prior capture was refused
and remained effective1.0/3408 per eye. These changes do not alter XR allocation.
Actual Frame appearance, both-eye output, performance and multiplayer acceptance
remain hardware-open.
