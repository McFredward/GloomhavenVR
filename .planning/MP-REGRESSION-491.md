# Build 491: card rendering regression and laser ownership

## Hardware evidence and cause

Both supplied `LogOutput.log` banners identify build 490 at line 17. The integrator inspected
`regression_3d_umgebung.jpg` (solid brown local map fan, patterned remote card backs) and
`regression_board.jpg` (incomplete remote scenario card artwork). The last earlier hardware
evidence was build 488; changes in both 489 and 490 were therefore under test for the first time.

`CardAppearanceBindings` assumed that a native card has at most eight CanvasGroups. The
constructor threw on any larger hierarchy. Native action content legitimately contributes
additional groups; the source contains variable Consume/Infuse children and loader groups.
The new appearance transport introduced this assumption in build 489, including construction
on the shared artwork path. Build 490 additionally captured reset defaults before asynchronous
loading finished; that separate source-proven defect is corrected without claiming the screenshots
distinguish it from the construction failure.

The main log contains 19,914 occurrences of the group-bound error, the other player's log 11,242.
Remote lines 2282–2291 show all ten map front builds failing. Line 2293 reports an OPEN gate,
the correct named character and ten resolved cards, but no front widgets. The local map uses
the same `RemoteCardArt` path: failed construction leaves the bare brown VRCard body, while
remote callers retain their backing. These are rendering failures, not sanctioned secrecy.
Scenario native appearance sampling also throws (main line 34477, remote line 14863), so one
oversized hierarchy prevents every other card in that sampling pass from being published.

The previous automated suite supplied only eight-group fixtures. Source-string checks and a
green build did not exercise construction against a larger native hierarchy. They consequently
did not establish that native artwork could actually be created. The new hierarchy harness
executes the production binding/discovery code with a controlled tree; it does not claim to
simulate Unity rendering or actual headset pixels.

## Corrections

- Card artwork construction and native reset discover the full dynamic group hierarchy without
  the transport's eight-group constructor limit. Supplemental state extends replication instead
  of dropping groups. One card's sampling failure no longer aborts unrelated cards.
- Original asynchronous artwork must finish before its reset baseline is captured. A temporary
  loading alpha of zero must never become the permanent restored appearance of a loaded card.
- Local controlled cards stay open. All map cards, including remote fans and held cards, stay
  public. No map privacy predicate needed loosening: the existing predicate was already open.
- Map icons had a separate location-layer raycast that did not include trigger-collider grab
  bars. Map clicks could also dispatch the hover target chosen by the other hand. Foreground
  depth and carry/release ownership now apply before hover and click, using the clicking hand's
  own hit. The same review covers independent map-button, combat-log and flat-screen routes.
  Blocking also immediately parks the flat virtual mouse, removing its two-frame stale hover;
  carry checks precede inactive-ray exits and active fingertip ownership remains intact.

## Protocol and bounded scheduling

GVR1/version 3 and record 58 remain unchanged, including its twelve graphic and eight group
roles. Additive record 69 packs supplemental groups as card index, total and offset followed
by binding/flags/alpha entries. A page holds up to 28 entries; complete pages, contiguous offsets,
consistent totals and unique bindings are required. Snapshot copying and change detection retain
all supplemental state. Existing map provenance and reveal rules are unchanged.

The transport supports 64 groups per card (eight legacy plus 56 supplemental), with 32 cards.
This is a tested transport capacity, **not** a claimed maximum of the unavailable original
prefabs. Native construction and reset are independent of this bound; no group is silently
truncated to make a packet fit. Actual native capacity diagnostics remain necessary on hardware.

Maximum serialized size is 57,766 bytes; buffer 58,368 leaves 602 bytes and stays below the
unchanged unsigned-16-bit fragmentation limit. The stronger saturation fixture initially caught
the expanded appearance stream missing completion at 32 seconds. Giving that stream two weighted
turns restores completion for all thirty streams at 90 and 18 Hz without increasing event size,
event rate or assembly lifetime. No gameplay networking or rules are modified. Record 70 is next.

## Verification

All 17 integrated checkers pass. The wire suite passes 251,752 assertions (+180 against build
490); the production binding harness passes 883 assertions and proves the old eight-group
constructor limit fails at runtime. Strict Release has 0 errors and 0 warnings. Docs i18n and
all 16 metadata-only reference assemblies pass. Config keys (625), patch surface (150), patch
registration (107 classes / 165 methods), log tokens (4,714), instrument baseline (61) and
asset bundle (74,943,763 bytes) remain unchanged.

The compiled comparison against `48330b22` has 18 intended changed types and one added
`LaserPointerPolicy`, none removed. Unedited sender/receiver/version types differ only through
inlined build or appearance-buffer constants. The guard returns 1 for intentional compiled
differences after all checkers pass. Saturation-loop assertion totals vary with the scheduler
weights because completion checks run on each received frame; no vector was unregistered.

Coverage includes independent legacy/69 golden bytes, malformed and incomplete group pages, deep copies,
full-size compression and reordered fragments, thirty-stream saturation, the actual production
hierarchy capture code, and laser depth/hand/gesture arbitration with negative controls.

Lane evidence: [map](MP-491-MAP.md), [artwork](MP-491-ART.md), [laser](MP-491-LASER.md).
Build 491 is a DLL-only update after the full build 483 asset installation. Both multiplayer
clients need the matching build. The next hardware run must confirm the original local and
remote fronts in the map and scenario, subsequent effect/rest transitions, and grab-bar
occlusion of hover/click on the map and other world panels.
