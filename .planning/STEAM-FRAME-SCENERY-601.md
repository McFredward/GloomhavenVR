# Build 601: essential scenario decoration profile

## Hardware finding and authorization

The maintainer's Build 600 test found no visible or performance improvement from
the grass control. The matching logs confirm why: zero density masked only **27
of 6,560 active renderers**. The initial discovery pass saw an incomplete generated
hierarchy and masked nothing. The loaded census shows 2,040 visible Foliage
material-slot candidates, indicating a much larger vegetation population than
the narrow floor-grass rule addressed. These are not actual draw-call counters.
See [the hardware evidence](STEAM-FRAME-THIRTEENTH-HARDWARE.md).

On 2026-10-01 the maintainer explicitly requested massive renderer reduction to
essential scenery on standalone Frame. This broadens the authorized decorative
quality compromise. The standing same-day ruling still requires settings on PC
and Frame, saved preferences, reversibility, and platform-specific defaults only.
Gameplay visibility and local/remote cards, boards and windows remain protected.

## User controls

Both controls live in **VR Options → Graphics** and in
`dev.gloomhavenvr.perf.cfg`, section `[Optimize]`:

| Key | Fresh Frame | Fresh PC | Effect |
|---|---:|---:|---|
| `ScenarioDecorationDensityPercent` | 0 | 100 | Generated decorative vegetation and dressing; zero removes eligible detail |
| `ScenarioSceneryDensityPercent` | 25 | 100 | Existing grass key; adds a grass-only density cap |

Grass uses the lower of the two values. Set **both to 100** for original
decoration. The new Frame zero default applies to the newly introduced decoration
key even in an older profile; no existing saved value is rewritten. A PC can use
the same zero profile, and Frame users can restore full detail at any time.

## Implementation and safety criteria

The budget expands from one generator family to verified generated decoration
at mesh level, including floor grass, shrubs, decorative tree parts and wall-side
vegetation. Structural floor and wall bases, game-actor/prop/door ancestry,
pickups, UI, cards and previews are excluded. A wall ancestor does not by itself
make every contained mesh structural: the logged grassy-verge units contain both
structural walls and decorative leaves.

Hide only renderer presentation using owned `forceRenderingOff` writes. Native
GameObjects, enabled flags, materials, property blocks, collision and reveal state
are retained. Collider-bearing visual pieces must remain visible: the current
laser and several independent selection paths do not filter hidden renderers.
An incidental parent wall/floor collider may coexist with removable foliage only
when its visible structural base is preserved.

Discovery must include loading completion and asynchronous generated content,
not only an early scene-entry snapshot. Native placement/reveal events enqueue
bounded work. Loading permits more preparation; gameplay scans and restoration
remain bounded. Reports distinguish unique classified renderers and actual owned
rendering masks from cumulative traversal work. Settings changes split the
measurement windows instead of blending different quality states.

Focused validation must execute the production classifier and lifecycle against
representative native hierarchy shapes. Pure extracted verdict helpers alone
did not catch Build 600's near-empty eligible population. Restore/full detail,
foreign rendering masks, late generation, room reveal, and actor/door/prop/collider
exclusions need explicit cases and meaningful negative controls.

## Source and engine validation

The focused portable check compiles the production classifier and helper bodies,
including the original figure guard, against an explicit component graph: **38
assertions and six negative controls**. Hosted CI can run this fixture without
the game engine; it does not establish pixels or Unity object lifetime.

The separate local suite runs the **complete production classifier and Driver in
Unity 2021.3.5**, with only native game/config/transport boundaries inert. It passes
**50 assertions and eight negative controls**, including 1,200 late generated
mesh renderers, the loading-complete edge, original/foreign mask restoration,
scene-root scenario membership, shared collider composites and late native
material assignment. Negative controls compile successfully and must fail at
their intended runtime assertion; compilation errors cannot count as proof.
Neither fixture measures headset FPS or simulates every authored game prefab.

The final integrated local gate passed once on the complete Build 601 source:
**14 source gates, 87 local suites, 286,621 wire/golden assertions and all three
bundle checks**. A separate strict Release build reports zero warnings/errors.
The compiled comparison against `7198477bf` has the expected 17 changed types and
one added material-readiness patch; network changes outside the build declaration
are inlined ModBuild values only. No config key, patch target or log marker was
removed. Main-checkout evidence: `.planning/debug/test-runs/20261001-203447-d8d52493/`
(source), `.planning/debug/test-runs/20261001-203503-c1180bb0/` (87 local suites),
and `.planning/debug/scenario-scenery-runtime/run-21uyzqww/` (actual Unity).

The native Addressables completion postfix only queues the assigned mesh. It
does not load, rewrite or clone materials. Discovery uses deduplicated tile and
leaf queues, a 96-node steady-state cap (2,048 during preparation), and a 4 ms
slice deadline checked between groups of four nodes. Shared collider facts live
only for that synchronous slice. An individual Unity subtree query can exceed
the deadline; this is not a strict real-time guarantee. Ancestry watches and
density retunes are separately capped at 64 and 96 records per frame.

The budget masks renderer submission, retaining native objects and geometry.
It does not claim to remove renderer components or eliminate native generation
work. A separate read-only asset audit found that the game's Standalone Fastest
preset still references the high Apparance generation profile. The prospective
reversible generation lever, its floor/reveal risks and exact evidence are in
[STEAM-FRAME-NATIVE-DETAIL-601.md](STEAM-FRAME-NATIVE-DETAIL-601.md); that profile is
not modified by this checkpoint.

## Hardware acceptance

After loading the same large scenario at 3408 pixels/eye, set grass to 100 and
compare decoration **0 and 100** at a fixed tracked head pose. Allow preparation
to complete and close VR Options before collecting each full window. Zero should
visibly remove broad decorative vegetation, with a substantially larger actual
masked count than Build 600's 27. The summary's category and unique-renderer
counts are the direct scope check; a percentage setting alone is not evidence.

Check new-room reveal, retry, local and remote cards, shared windows, game
obstacles and laser selection. The existing 2D map and original flat town windows
remain available; a scenario-only budget must not affect either mode.

Build 600's complete post-load means remain about 120–125 ms/frame at changing
views and settings. No matched hardware performance gain is established for
Build 601. The overall goal of fluent standalone play remains open until the
actual scene population and headset timing establish it.
