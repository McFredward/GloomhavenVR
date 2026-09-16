# Mixed-reality window backing — build 516

## Evidence and scope

The supplied local `LogOutput.log` identifies release build 515, assembly 1.0.3.0,
main `fd76de86b`. Retained `debug/remote/LogOutput.log` identifies build 500 and is
historical, not a second capture of this test. The inspected
`too_big_mixed_reality_backgorunds.jpg` shows a brown backing spanning the transparent
right-hand portion of the party window; its actual character column and grab handle
remain narrow. The quest window likewise has unused backing space.

Source confirms that `MrBacking.TickPanels` seeded every plate from `HostRect.rect`
and wrote its geometry immediately. The grab holder already measured the narrow
visible column. An active converted host could receive a backing even when its
content had never drawn. The enemy-reveal surface has no modal grab holder, so
fixing only modal rectangles would leave the same fallback on that path.

The screenshot establishes excessive size. Logs do not uniquely timestamp the
reported one-off empty map plate or enemy reveal size spike. The corresponding
source paths are fixed; actual headset replay remains necessary.

## Implementation

- Modal plates read the existing holder's latest raw ink sample, before its grab
  envelope/recession policy. No additional modal geometry walk is introduced.
  Missing ink, confirmed empty chrome or an owed initial appearance suppress the
  plate; a known empty owner cannot fall back to its transparent full-screen host.
- Non-modal converted surfaces measure visible ink every four frames, replacing
  the old every-frame glyph walk. Empty ink produces no plate. Actual full-frame
  artwork, its lower overflow and legitimate unclipped text are preserved.
- A small eight-pixel margin surrounds window ink. Plates confirm a target on
  independent geometry samples, then ease their size and centre over 150 ms using
  unscaled time and the handle's default cubic curve. First appearance grows from
  zero at the content centre. A single cached/bad sample cannot confirm itself.
  Continuous native layout eventually advances rather than waiting forever for
  identical rectangles. Hidden/replaced content resets presentation immediately.
- The duration derives from `Defaults.GrabBarTweenMs`, not the observer's live
  setting. Local and remote MR animation therefore do not diverge with viewer
  preferences. No new wire field or player setting is introduced.
- Remote native mirrors use the same ink walker, margin, sample cadence and
  presentation helper. A cached measurement-only descriptor points to the inert
  clone and original-layout pivot; it is never registered as a conversion and runs
  no gameplay controller. An optional frame override separates the owner's fitted
  frame from the native parent frame used by stretched anchors. Original tooltip
  and effect classifications are cached before their stripped clone components
  become unavailable, preventing hover content from inflating the backing.
- Remote MR sampling requires a successful fit and visible host. Clone teardown
  clears bounds, sample identity, exclusions and descriptor references. Sampling
  does not run with MR disabled. Registered tooltip surfaces retain their own
  units/margins; they only share the presentation tween.

No native layout, gameplay state, callbacks, input masks, window pose or card
presentation is changed. Existing backing materials, depth/order and alpha policy
remain in place.

## Validation

- `scripts/mr-backing-tests.sh`: 233 production layout/accessor assertions and
  25 integration binding assertions; ten runtime mutations and three binding
  mutations rejected. Cases include transparent frames, real artwork overflow,
  initial/mid-resize flashes, independent samples, animated growth/shrink and
  reposition, empty/owed windows, continuous layout, clone fit readiness, local/
  remote identical inputs and allocation-free steady presentation.
- `scripts/panel-ink-tests.sh`: 243 assertions; seven production mutations and one
  placement binding mutation rejected. The six additional mirror cases verify
  owner-frame classification, retained full-frame content identity, stripped
  tooltip subtree exclusion, empty content and read-only measurement. Existing
  movie, hint, reward glyph and placement coverage remains intact.
- Strict Release build: zero warnings and zero errors.
- Full integration guard and hardware acceptance are recorded by the integrator.
  Automated results establish source behavior, not the final headset image.
