# Build649: position controls below wrist attachment

The maintainer's 2026-10-09 request exposes the existing wrist-local X/Y/Z
offsets directly below the wrist-board switch in Board & Cards. The existing
dependency and menu rebuild show or hide all three rows immediately when the
switch changes. Advanced retains hand, position, angles and size tuning.

The rows reuse the normal hybrid slider/arrow controls and the existing saved
`Cards/WristBoardOffsetMeters` Vector3. Each control edits one component only;
all handles and readouts repaint their own components. Dragging covers -0.5 to
+0.5 metres with the existing 1 mm step. Opening or rebuilding the menu never
clamps a saved calibration outside that gesture range. Captions and player help
are available in English and German and have the same name in both menu views.

The original live board placement, wrist tracking, docks and remote board pose
transport are unchanged. No new config key, network record, asset or Frame
quality change is introduced. Build648's rollback and Build646's NPC fixes are
retained.

## Validation scope

The existing actual-source Unity wrist suite now exercises the real curated
menu, toggle/rebuild callbacks, component slider writes, arrows, readouts,
sibling handles, independent saved axes and automatic original board movement.
It also checks the unchanged scalar hybrid control and original owner-pose
serialization/remote furniture paths. Tracked XR inputs and donor row controls
are explicit fixture boundaries; this does not establish headset comfort or
the final headset appearance of the settings window.

Validation receipts are retained in the main checkout's
`.planning/debug/wrist649/`:

- Real Unity wrist/menu suite: 269 assertions and 3 original causal controls.
- VR Options: 7,339 assertions; close/reopen: 1,796, with their negative controls.
- Bilingual player help: 2,296 production lookup assertions and coverage controls.
- Source group: 15 of 16 passed initially. The remaining caption consistency
  failure was fixed by aligning the ordinary/Advanced name; its bounded repeat
  passes. The original failed attempt remains in the receipts.
- Direct wire goldens: 299,714 assertions. This invokes the vectors only, not
  the complete-suite wrapper.
- Strict Debug/Release builds: zero warnings/errors. All four bundle headers,
  the 66-part figure bank and player-document bilingual check pass.
- Exact648/649 surface comparison retains all 663 config keys, 232 patch
  registrations and 4,790 log tokens, with no additions or removals.

This bounded UI change uses focused checks and inherits unchanged Build648
evidence; it is not a new complete 176-suite pass. No headset test of649 is
claimed. Production changes are limited to the existing options kit/catalog,
the curated row, bilingual captions/help and build number.
