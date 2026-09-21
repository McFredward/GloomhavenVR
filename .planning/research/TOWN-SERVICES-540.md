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

The asset corrections cover all three NPCs. Temple and enchantress retain their existing
reading surfaces and sample interactions; this change does not pretend to have redesigned
those two stations as merchant racks.

## Validation

- Asset render tests: 44 assertions / six visual negative controls; all three actors,
  automatic LOD, textured no-light rendering, intermediate dissolve, real-frame animation,
  198x map scale and counter geometry. Windows shipping bundle additionally load-tested.
- Item provenance: 19 assertions / four compiled negative controls.
- Physical catalog: 240 Unity assertions / seven compiled negative controls.
- Window/session interaction: 871 Unity assertions / 27 compiled negative controls;
  final integration positive run repeats 871 after the owner-level fade wiring.
- Orphan ownership: six assertions across 13 sweeps / three compiled negative controls.

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
   the tray, then place one on the tray; cancel and confirm native purchase quotes.
4. Repeatedly exit/reopen and toggle immersive visits off/on, including while holding
   a sample. Off must restore the complete original window without accidental purchase.
5. Compare an observer's NPC, card art, prices, pages, highlights, held samples and opening
   motion; repeat with the observer's local immersive setting off. Only the owner can act.

The unchanged main bundle is 74,942,975 bytes, SHA256
`fe1a659c17b4151e929691aa070d402b8cd299a462315b1d6691d2622d491693`.
The new town bundle is 95,865,589 bytes, SHA256
`ddb5bd53b303a050a32214fc8342cea5133ac1e287b41ace60548cb7cfe578be`.
No additional paid generation was used.
