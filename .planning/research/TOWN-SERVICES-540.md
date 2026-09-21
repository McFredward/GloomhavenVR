# Town services: first hardware corrections

Development build 540, 2026-09-21. This supersedes the merchant layout and asset hashes
in the first-variant record. It does not claim hardware validation or announce a release.

## Hardware evidence

The current local `LogOutput.log` and `Player.log` identify build 539 / `ba0888776`.
The files under `remote` identify historical build 500; they cannot validate this session.
No new town-service screenshot was supplied.

- `Player.log:8490` starts the native item-ID-zero error; the following stack runs through
  `CItem.YMLData`, `TownServiceNativeAssets.PrepareItem`, `NativeTemplates.Freeze` and
  `TownServiceSync.Publish`. Native error reporting opens `GlobalErrorMessage` before
  throwing, so catching the exception cannot prevent the blocking dialog.
- `LogOutput.log:2841` confirms successful merchant creation, not a missing bundle.
  Renderer inspection and pixel negative controls reproduce a 100x undersized NPC LOD
  volume. Automatic LOD culls the 1.75 m body using a roughly 0.0175 m group.
- The scene reports zero enabled lights. The old shader renders black with black ambient
  and no lights. Self-contained textured lighting now follows the existing board approach.
- Repeated `ORPHAN CHROME` entries include live town surfaces and the error window.
  These holders were outside the ordinary converted-window registry. Their actual owners
  are now included in the sweep; genuinely retired holders are still collected.
- The two late `NullReferenceException` stacks occur during voice/XR teardown, after the
  original modal error. They are not evidence of the merchant's initial failure.

## Merchant and restoration

The native filtered inventory drives a bounded rack of six original `ItemCardUI` cards.
Their original row graphics provide price and stock. Original buy/sell/filter controls
and exit sit on the counter; page arrows select subsequent groups. Laser selection and
physical sample placement both invoke native row selection. No cosmetic code writes
currency, inventory or other gameplay state. Sample cancellation does not transact.

Original windows and scroll viewports remain active behind mod-owned transparent wrappers.
Their native alpha and permissions are never overwritten. Counter controls have separate
reversible conversions. Turning the setting off restores parent, sibling and layout while
preserving current native state; closure and failed construction follow the same rollback.
Borrowed card graphic/raycaster input flags are restored before returning them to the pool.
The merchant's new rack and tray share a short opening fade. Publication sends the real
counter cards, prices, controls and navigation, including inherited alpha, rather than the
obsolete full inventory. Observers remain noninteractive.

Native hover details are parented under the original inventory row. They therefore need
their own visible counter copy after that row is masked. Original item art, modifiers,
binding/discount information and paired native AllHints inspection populate separate
reading areas; no substitute description text is authored. The ordinary price mirror
excludes the tooltip branch. A reversible wrapper suppresses only the shared native hint
whose target this inspection owns, and releases it when another native target takes over.
Nested pooled cards retain their original provenance when these copies are published.

One shared NPC/counter pose would put all visitors' different catalogues on top of each
other. Additional connected visitors now receive furniture-only extensions from the same
Counter prefab, with full-size cards. The primary visitor/SP keeps the original counter.
Extra centres are (-1.8, 0, 2.2), (0, 0, 2.2), (1.8, 0, 2.2) in station space, outward
from the map. Native connection IDs are unbounded, so neither modulo four nor a cached
local rank is safe. Owners resolve their ordinal in the complete current connected roster
(including flat peers), animate changes over .22 seconds and publish the actual pose and
owned material values. Receivers apply no private offsets. Unexpected fifth ordinals fall
back to the ordinary window. A manually moved tray detaches from subsequent rearrangement.
The back row is wider than the original counter; room-wall clearance remains a hardware check.

The asset corrections cover all three NPCs. Temple and enchantress retain their existing
reading surfaces and sample interactions; this change does not pretend to have redesigned
those two stations as merchant racks.

## Validation

- Asset render tests: 44 assertions / six visual negative controls; all three actors,
  automatic LOD, textured no-light rendering, intermediate dissolve, real-frame animation,
  198x map scale and counter geometry. Windows shipping bundle additionally load-tested.
- Item provenance: 19 assertions / four compiled negative controls.
- Physical catalog/details/hints: 282 Unity assertions / 15 compiled negative controls.
- Window/session interaction: 876 Unity assertions / 28 compiled negative controls,
  including manual tray placement during workspace movement.
- Workspace geometry/roster/lifetime: 163 Unity assertions / six compiled negative controls.
- Orphan ownership: six assertions across 13 sweeps / three compiled negative controls.
- Multiplayer: 428 Unity assertions / 14 compiled negative controls. Production publisher
  routing excludes the obsolete window, retains price provenance and removes old pages;
  capture/codec/playback render comparisons cover six cards at intermediate inherited alpha.
  Template creation and native catalogue inputs remain explicit fixtures in that suite.
- Follow-up preview/furniture mirror checks: 84 assertions / three negative controls;
  real publisher recursion, original pooled-card provenance, actual TownNpc shader dissolve
  and two distinct owner workspace poses. This is scoped coverage after the full suite.
- Existing movie ownership tests: 66 assertions / six compiled negative controls, with the
  real sweep and unchanged movie owner. Only the extraction fixture needed adaptation to
  recognize the additional town/error owners.

These harnesses bind production code but use explicit adapters for game-controller state.
Asset pixels use a same-source Linux review bundle; Windows stereo output remains a
hardware check. See [art evidence](TOWN-SERVICES-540-ART.md) and
[provenance evidence](TOWN-SERVICES-540-PROVENANCE.md). Additional integrated validation
and final package identity are recorded below once complete.

## Required hardware pass

1. Install the entire development package, including both bundles; all VR peers need 540.
2. Open the merchant: visible animated NPC and textured counter, six original readable
   cards, original prices, no duplicate floating inventory and no error modal. Check the
   other two NPCs too, in the ordinary environment and mixed reality.
3. Browse pages/categories; switch buy/sell and character. Pick up a card, cancel outside
   the tray, then place one on the tray; cancel and confirm native purchase quotes. Hover
   cards with modifiers/rules text and inspect both detail areas, including repeated changes.
4. Repeatedly exit/reopen and toggle immersive visits off/on, including while holding
   a sample. Off must restore the complete original window without accidental purchase.
5. Compare an observer's NPC, card art, prices, pages, highlights, held samples and opening
   motion; repeat with the observer's local immersive setting off. Only the owner can act.
   Browse concurrently with two to four connected users: additional visitors need distinct
   furniture/workspaces. Check the back row's room clearance, join/leave rearrangement and
   that a carried or manually placed tray stays under the player's control.

The unchanged main bundle is 74,942,975 bytes, SHA256
`fe1a659c17b4151e929691aa070d402b8cd299a462315b1d6691d2622d491693`.
The new town bundle is 95,865,589 bytes, SHA256
`ddb5bd53b303a050a32214fc8342cea5133ac1e287b41ace60548cb7cfe578be`.
No additional paid generation was used.
