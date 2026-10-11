# Frame616: original immersive NPC meshes

The maintainer reported holes and damaged geometry in the simplified immersive
residents and requested removal of both the slider and these derivative meshes.
This change affects the three map residents only. Scenario hero/enemy detail and
distance controls, their original gameplay objects and ghost mirrors remain.

`TownNpcSkinningQuality` replaces `TownNpcDistanceDetail`. It owns only the separate
per-renderer influence limit; it never reads or prepares the scenario mesh bank,
substitutes geometry, alters bones/materials or evaluates distance. Original body,
face/eye surfaces and authored pose/audio clocks remain unchanged. The existing
legacy town LOD compatibility path still locks old prefab LOD groups to their
original high-detail surface.

The old `Optimize/TownNpcDetailPercent` key stays bound for saved-config
compatibility, with an INERT/DEPRECATED description. Its runtime accessor and
curated row are removed; the actual catalog retirement predicate hides it from
advanced options as well. No migration rewrites its stored value.

The three rejected source signatures are `figure-08fde96f1cd755f1`,
`figure-2c6503b1e156226f` and `figure-a0057e45d5165bcf`. Their twelve 75/45/20/5
records and dedicated distance parts17–28 are removed: **53,762,395 bytes**.
The remaining **1,594 scenario derivatives in 66 parts** retain every original
part byte and hash. The twelve removed parts are absent from both the release
packager's glob and index. Original town, voice and main asset bundles are unchanged.
The town exporter and facial hybrid generator are removed; reused export directories
explicitly skip old `npc` rows, even when extraction is skipped.

Fresh or missing-key Steam Frame standalone profiles now bind
`WorldUI/ImmersiveTownServices=false`; PC profiles still bind true. BepInEx Bind
preserves explicitly saved true and false values. An existing Frame installation
with saved true needs to disable the option once under Environment; it is not
silently overwritten. When off, the original service-window path remains active.

## Focused evidence

- Original Unity distance/body suite: **2,611 assertions plus five causal controls**.
  All three shipped NPCs retain exact mesh, material and bone identities at near,
  mid, far and return, with old cap values0/45/100. The real scenario-bank cache
  gains no NPC source identity or resident part. Original bodies render across
  actual player-loop boundaries. Linux previews use an explicitly documented
  Standard shader surrogate for Windows-only material programs; they do not prove
  headset shader or frame-time parity.
- Options: **169 assertions plus nine controls**. The extracted production Bind,
  curated tree and retirement predicate prove defaults, saved-key retention and
  slider exclusion. Unity controls and BepInEx persistence are explicit fixture
  boundaries here; the fixture does not claim to reimplement a full menu.
- Station geometry/lifecycle: **1,896 production assertions and sixteen controls**.
  Fifteen controls passed in the initial run; the last control needed its renamed
  skinning-dependency extraction anchor corrected and passed on a bounded resume.
  Compile failure was retained as failure, not counted as a successful control.
- Native mesh bank/Powershell packaging preflight: **20 tests**, including the actual
  Windows installer block accepting all1,594 entries and rejecting corrupt sets.
- Strict plugin and offline generator builds: **zero warnings and zero errors**.

Source hashes, retained-part hashes, original failed and resumed receipts are under
`.planning/debug/frame616-review/npc-originals/`. A green Unity check proves original
geometry retention; the next headset test establishes the final appearance and
performance. The main integrator runs the complete required gate after merging.
