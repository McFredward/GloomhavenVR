# NPC hardware follow-up — ModBuild 576

The supplied `Player.log` and `LogOutput.log` identify ModBuild 575, commit
`e09e458b2`. Three new headset screenshots in the main checkout's
`.planning/debug/npc_probleme/` show cabinet stock projecting ahead of the
carved opening and an enchantment aura compressed around an offered card. No
remote log was supplied for this run. The player also reported intermittent
enchantress offer targets, whispered ordinary speech, raised enhancement
hotspots, an out-of-stock label obscuring an inspected card, and locked town
residents and furniture appearing before their native unlocks.

## Source-grounded findings and corrections

- The stock projection came from the cassette housing as well as the card
  mount. The imported front cheek is at `z=0.0681 m`; the card face previously
  sat ahead of it. The cassette now moves 40 mm into the cabinet and the native
  card another 15 mm toward its leather seat. In the resulting imported-FBX
  side render, the face is at `z=0.0788 m`, ahead of the seat at `z=0.0820 m`
  and behind the cheek. The physical item card, collider and price move as one.
  The sold-out marker is cabinet-only: it clears on the grab's first frame,
  stays clear through held, palm and return-flight states, and reappears only
  after the unavailable card returns to its shelf.
- The prior resident population loop created all three NPCs whenever the
  immersive setting was enabled. The original game instead uses saved
  headquarters flags for the merchant, temple and enhancer, with additional
  first-map tutorial gates. Resident, furniture, public stock and interaction
  are now gated by those same native states in Campaign and Guildmaster. A
  locked station is disposed as a unit; an unlocked neighbor remains live and
  publishable to peers. The setting-OFF path retains the original windows.
- The first campaign-map `VisitMerchant` tutorial step is fulfilled by opening
  the flat shop toggle. Its dependent `BuyItem` step is misleadingly named:
  native completion is the WorldMap toggle on shop exit, with no purchase check
  (`.planning/SAVEGAME-507.md`; `UIShopItemInventory.BuyItem` never completes
  that step). With an immersive resident and no flat toggle, both steps would
  become invisible progression gates. Only on the immersive 3D campaign map,
  their original `MapFTUEManager.StartStep` promises are resolved so the game's
  own completion callbacks and next step run. Pending steps, late shop setup
  and enabling the option mid-step are handled without writing saved FTUE state
  directly. Classic 2D, setting-OFF, Guildmaster and scenario tutorials keep
  their native flow. The integration patch audit caught the first version of
  this change before handoff: both Harmony patches existed but were inert
  because the WorldUI module had not registered them. They are now registered
  exactly once, and the generated patch inventory records both methods.
- The intermittent enchantress cue was a readiness gap. Build-575 Debug traces
  show the native enhancement window open while its input flag was false, then
  later show successful card offers. The previous 350 ms preview could expire
  before native rows finished relocating. A neutral, noninteractive locator now
  remains visible throughout that wait; drop feedback and acceptance still
  require the native slot and input gates. If the offer remains unavailable for
  three continuous seconds, the original controller is restored as an ordinary
  usable VR window for that visit. Closing it clears the fallback latch. The
  bounded recovery reports once at normal log level, while frequent readiness
  details remain Debug-only.
- The enchantress's cyan effect previously measured a zero-size parent rather
  than the rendered native Graphic; its animated scale then produced the narrow
  oval visible in the screenshots. The physical card's rendered bounds now
  determine the one-time aura fit while the original native pulse stays live.
  Native selectable enhancement areas sit on the card face, rather than floating
  ahead of it.
- The original enchantress voice ID now produces both normal spoken English
  reactions and softly whispered spell phrases. The 25 replacement WAV clips
  and matching lip data are in a rebuilt, independently loaded town-voice
  bundle. The art bundle was not altered for this change.

## Validation and headset checks

The merchant catalog runtime fixture passed 22,918 assertions and 41 negative
controls. The FBX side render was reviewed before and after the geometric
change. The native-linked resident/FTUE test passed 28 assertions. The
enchantment handoff fixture passed 1,161 runtime assertions and 36 negative
controls; its layout and voice fixtures passed 253/14 and 3,887/20 respectively.
The window-interaction fixture passed 1,337 production assertions after adding
an actual stalled-offer recovery case; its 56 negative controls had passed with
the same production sources before that fixture-only addition. The isolated
cabinet and interaction fixtures were updated to model the newly required
native availability boundary rather than failing to compile an otherwise
successful production build.
The complete 80-suite local run passed 78 suites directly; its only two failures
were the old isolated resident and interaction fixture boundaries. Both were
repaired and rerun successfully against the unchanged production sources. The
resident suite now includes explicit whole-station retirement on a native lock:
203 production assertions and 15 compiled negative controls passed. Byte-exact
wire tests passed 286,578 assertions. The source guard passed all 14 suites;
the surface census found no removed settings, patches or log markers. A full
80-suite rerun was not necessary after only the two isolated fixtures changed.
Unity 2021.3.5f1 rebuilt the voice bundle as an independent part; its UnityFS
format check passed. The art bundle remained byte-identical. The Release source
build passed with zero warnings and errors after integration. The local package
check passed and included both separate town bundles. The patch inventory now
reports 150 classes and 226 methods, all registered exactly once. A source fix and
these checks do not prove the headset picture, voice quality, or first-save
runtime tutorial flow. Test a fresh Campaign, Guildmaster and classic-setting
save: locked residents and stands should be absent, each native unlock should
reveal only that service, and the Campaign merchant lesson must progress without
opening a flat shop. Test an existing unlocked save, the enchantress's long
loading/offer path, the effect and selectable regions on the physical card,
normal speech versus spells, merchant stock depth and sold-out inspection.
