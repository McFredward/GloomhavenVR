# Scenario card preparation

This addresses the Build616 capture's first Summoner switch: frame 5437 contains
172.37ms in `Cards.Driver` while new class strips and `ConsumeDark` mip resources
appear. The capture does not isolate all 172ms as texture work. Moving actual
resource misses and backing construction before ordinary input is a source-proven
change; the next headset capture must establish the remaining switch cost.

## Existing paths reused

`ScenarioCardPreparation` reads the real `ScenarioManager.Scenario.PlayerActors`
and each native class's selected, hand and ability pools. Every discovered card
resolves its skin through the same `UIInfoTools.GetCardSkin(ClassModel,
ClassCharacterConfig)` lookup used by the native widget. This includes other
players and transferred ability skins; it never changes `CharacterFocus`, invokes
native `ShowCard`/`Awake`, creates a gameplay hand, or assigns a native `Image`.
Loading an asset is independent of permission to show its face: no reveal,
concealment, input or wire rule changes.

The helper prepares the original skin's background/state references, default
action strips, rest/preview sprites, and original common ability/condition/element
icons. It reads direct/private sprite fields and sprite arrays on `UIInfoTools`,
with explicit traversal of its original condition and element configs. It does
not scan all loaded sprites or repeatedly walk the native resource heap. Class
art is processed before general icon fields to preserve priority in the existing
finite mip budget.

`CardArtPin` supplies its existing GUID-keyed Addressables handles. The native
`AssetReference` and per-widget loading context remain untouched; readiness is
observed from the pin's own valid handle or original direct sprite. Release remains
owned by the existing map/scenario teardown. `CardFaceMipBake.WarmSprite` calls the
same original-aware replacement cache used by local and remote adopted faces,
item cards and window images. It never swaps an original `Image`. Existing exact
geometry/readback rules and 384MB mip / 256MB CPU budgets remain unchanged; unsupported
or exhausted entries retain the original asset through the existing fallback.

`VRCardFactory` reserves ordinary blank wrappers under its existing inactive pool,
using the same backing prefab/procedural builder as `CreateBlank`. Unused wrappers
are outside `All` and the widget map, so ordinary card/input maintenance cannot
visit them. `CreateBlank` consumes a reservation before building another slab;
`GetOrCreate` still adopts the actual native widget immediately. No native face is
preconstructed or replaced. Reservations are bounded by the real party loadout,
minus existing live wrappers, with a 64-wrapper safety limit.

## Coordinator API and bounds

The shared loader owns these calls on all platforms:

| API | Meaning |
| --- | --- |
| `Begin()` | Start a fresh preparation pass; wait for the existing factory, native roster and UI resources. |
| `Tick()` | At most one reference observation/start, one mip preparation or one backing build per actual frame. Repeated offers in the same frame are ignored. |
| `IsReady` | This preparation pass completed, including bounded fallback failures. Ordinary native loaders still handle late/unseen content. |
| `CancelPreparation()` | Drain unfinished work; retain valid reserves, shared pins/mips and live faces. |
| `Reset()` | Drop the pass and destroy only unused reservations. Retain live faces, shared mips and shared pins. |

Keep the completed reservations until actual scene/VR teardown: resetting
immediately when `IsReady` becomes true would discard the prepared slabs. Progress
is exposed as `Classes`, `SpritesPrepared`, `SpritesTotal`, `ReferencesPending`,
`BackingsPrepared`, `BackingsTarget` and `Failures`. Prerequisite waiting and pending
addressable waiting each have a 30-second bound; legitimate sprite/backing work is
not canceled merely because processing takes longer. The coordinator also owns its
overall loading safety timeout. Resource enumeration is capped at 2048 distinct
sprite/reference entries; unexpected or modded content still uses the ordinary
lazy path. Factory disposal clears its registration and unused wrappers.

Debug preparation completion reports counts once per pass. Allocation-free Perf
scopes/counters distinguish `Cards.SpriteMipMiss`, `Cards.AtlasMipMiss`,
`Cards.BuildBacking`, `Cards.PrepareBacking`, `Cards.PrepareRoster` and
`Cards.ScenarioPreparation`; inclusive nested times must not be summed. There is
no new per-frame normal-log trace. The shared coordinator reports the overall
preparation lifecycle; an exception retains bounded useful failure context and
releases the preparation gate instead of trapping gameplay.

2D maps, original non-NPC windows and public 3D-map/remote artwork continue to use
their existing presentation paths and the same cache. This helper does not invent
a separate remote sprite/art transfer or alter map rendering. It prepares scenario
resources only when the shared coordinator requests it. Native widget-specific
asynchronous contexts and silhouette/FX/layout adoption still happen through their
existing owners; this is not a claim to eliminate every first-hand cost.

## Focused verification

`python3 scripts/check-scenario-card-preparation.py` runs the full production
preparation, pin, mip/cache/readback and factory files in Unity 2021.3.5 play-mode.
The final focused run has **761 runtime assertions and 10 effective negative
controls**, including 24 real GPU pixel comparisons. It checks:

- three native party classes plus a transferred fourth skin, original default and
  private action icons, native element/condition icons, unchanged focus and source
  `Image` state;
- GUID deduplication, own-handle async readiness, failure/timeout completion and no
  native `AssetReference` operation mutation;
- existing local/remote replacement identity, exact rect/pivot/border/PPU,
  original upright pixels after GPU readback, and native image restoration;
- 21 reserved backings, absent live inventories, same-frame work deduplication,
  immediate ordinary consumption/adoption, repeat preparation and teardown that
  retain active faces/shared artwork.

Every negative control fails at its stated runtime assertion. The game roster,
UI lookup and Addressables service are controlled boundaries. `VRCard.Build` is a
construction counter boundary in this focused fixture, while the tested factory
reservation/consumption/cleanup code is the full production file. The actual
native mesh/material/silhouette and multiplayer picture still require hardware
verification; green fixture pixels establish the mip cache operation, not a
headset performance gain. The separate real net472 Release build has zero warnings
and errors. Full integrated gates remain the primary agent's responsibility.
The existing card-scene lifetime suite also passes 23 source bindings, 36 runtime
assertions and eight effective controls; the card-identity source gate passes
all 11 assertions. Those preserve scene handoff and remote naming/face-policy
boundaries independently of the new preparation fixture.

Retained evidence and production hashes are in
`.planning/debug/frame617-implementation/cards/runtime/run-e2x8sxj1/`. Its generated
Unity `Library` and `Temp` directories were removed automatically. Earlier source
checkpoint receipts remain in the same evidence directory; they are not counted
as additional final-tree validation.
