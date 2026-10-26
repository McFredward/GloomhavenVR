# Level-up and confirmation containment — Build722

## Report and evidence

The maintainer reported an independently floated ability-selection list during level-up,
a list surviving completion and character changes, and an independently floated confirmation.
A separate party reported the rightmost fifth of a preview card covered by the comparison list.
No new screenshot or remote log was provided for this round; the third-party geometry report
is not independently reproduced on a headset.

The current hardware inputs are `.planning/debug/LogOutput.log` and `Player.log`, Build719,
assembly1.2.1. The current development baseline is Build721 (`d4a888357`); its intervening
runtime changes concern accepted item surrender and release identity, not this containment path.
Bounded original log matches are retained in ignored `debug/level722/hardware-evidence.json`.

- Log5369/5370: `Hero Level Up Panel` is correctly recognized as a child of `New Party display`.
- Log5455/5456: `LevelUpInventory (1) Variant` has IDNone and lives directly below Campaign Canvas,
  with no ancestor UIWindow. Catch-all therefore enrolls it as an independent window.
- Log5476/5477: the inventory floats independently without an X. It remains in the session's
  float registry until shutdown (8824), even after ActiveDisplay returns to CARDS.
- Native `UILevelUpWindow.StopAnimations`, `Close` and `ForceClose` hide that exact serialized
  inventory. Map-room sticky presentation can survive native Hide, so native lifecycle cannot
  remove the separately converted sibling from VR.
- Native `IsShowing` ends after card reveals, before card selection. It is not the picker lifetime.
- Native `NewPartyDisplayUI._ignoreDisplayTypes` includes CARDS/LEVELUP: the existing deck remains
  visible during level-up. It needs a shared preview envelope as well as the new-choice list.
- `DialogSurface.ConvertNow` independently placed every generic confirmation at the HMD without
  considering the exact native source which requested it.

## Implementation

`LevelUpComposite` carries the original inventory below its actual level-up owner. During
explicit browse/reset it belongs to the original character-card view instead. Dedicated mount
transforms separate the placement from native inventory slide animations. Original callbacks,
pooled card objects, readiness and character/network authority remain untouched.

The current deck is mounted left of the full-card preview; new upgrade choices are mounted to
its right, with an authored24px clearance. The actual native full-card rect establishes the
opening's preview envelope, including both world-scale and rotation transforms. Each original
view returns to its original parent, sibling order, anchors, pivot, size, position, rotation,
scale and layer ownership on close, replacement, manual-screen mode, disabled conversion and
module teardown. A rejected reparent never destroys a mount containing an original game object.

All three fixed-fit populations include the same original level-up roots: layout collection,
arrival signature and reveal veil. Nested roots are still transformed once, as a single group.
This prevents the level-up view from being measured as part of the permanent character column.
The existing capture/hit-rect rules retain visible overspill rather than clipping full cards.

Generic confirmations record a causal producer scope at their native Show call. A proved live
containing panel can carry that same original prompt; no focused/topmost/open-window guess is
used. Unowned global notices retain standalone fallback. No presentation teardown confirms,
cancels or hides a native decision. The companion audit records the caller inventory and any
lifetime fallback limits.

## Multiplayer and compatibility boundaries

No gameplay action, character ownership rule, native callback payload, wire record or saved
configuration is changed. Native card-choice transactions remain native, including their
joining-player restrictions. Each VR client applies identical containment to its own actual
native source hierarchy; unrelated menus and other actors are not selected as substitutes.
Flat/unmodded clients see their original UI and receive unchanged native game actions.
Existing common-world placement, dragging and resizing laws are unchanged for main windows.
This source review is not a new hardware or unmodded-crossplay acceptance run.

## Validation

Final integrated receipts and focused test counts are recorded here after execution. The
bounded change is validated with the affected presentation suites, source checks, strict
Release compilation and complete unfiltered golden vectors. Unchanged unrelated runtime
suites inherit the published721 evidence; this is not a new full local-suite sweep.
