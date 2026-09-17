# Hardware follow-up — build 516

## Evidence and scope

The supplied local test runs the public 1.0.3 / ModBuild 515 release (`fd76de86b`).
The files under `.planning/debug/remote/` still identify historical build 500 and
are not a capture of this test. The supplied
`too_big_mixed_reality_backgorunds.jpg` was inspected: the party column has a
narrow visible UI and grab bar, but its opaque MR backing extends across the
mostly transparent native layout frame. The quest window is also oversized.

The maintainer reports three issues: a spurious second recess hint on the last
page of a three-card discard, oversized/transient MR window backgrounds, and a
native error dialog after restarting the round. Source and log findings are
recorded separately in the lane reports:

- [TRAY-516.md](TRAY-516.md): completed-page lifetime, confirmation cues and undo.
- [MR-BACKING-516.md](MR-BACKING-516.md): visible bounds, geometry transitions and
  local/remote backing measurement.
- [RESTART-516.md](RESTART-516.md): scene ownership and pooled native card lifetime.

## Integration review

Completed pick pages must remain selected bookkeeping while their visible cards
are already in the pile. The owner wanted-mask is transmitted unchanged; no
observer-side card-count approximation was introduced. Native deselection and
recycling still retire claims, including the locked-prefix index.

MR measurement must not use either a transparent full host rect or the grab
holder's intentionally retained maximum envelope. New bounds are checked before
retargeting; the displayed rectangle uses the grab bar's default 150 ms ease-out. This shared
duration is identical locally and remotely, without reading an observer's live dial.
Remote widget backings require the same content measurement and padding, rather
than keeping their previous full fitted frame after local geometry changes.

Native pooled cards must regain their full hierarchy before scene destruction is
scheduled. The original load iterator and callbacks remain authoritative. The
loading gate excludes the earlier mandatory-decision wait in EndScenarioSafely.
An aborted load must reattach retained selected, fan and short-rest wrappers before
normal input resumes, without losing their identities or page claims.

Version remains the post-release dev version 1.0.4. ModBuild advances once to 516;
no bundle rebuild, release publication, wire-version change or new wire record is
part of this work.

## Validation

All 17 guard checkers and production suites pass; **254,019 wire assertions**.
Strict Release passes with **zero warnings / zero errors**. Bilingual documentation,
shell syntax and whitespace checks pass. The guard's nonzero compiled-difference
verdict is expected after behavioral changes; no checker or test failed in the final run.

Focused coverage:

- Pick tray: 245 runtime assertions, eight source bindings, six runtime negative controls.
- MR backing: 233 runtime assertions, 25 source bindings, ten runtime and three binding
  negative controls. Native ink: 243 assertions, seven runtime and one binding control.
- Card scene lifetime: 35 runtime assertions, 20 source bindings, seven runtime negative
  controls, including the actual production TickGuard handling a failing mod release.
- The new scene hook is classified in `docs/NET-ACTION-SURFACE.md`; construction failure
  preserves the native iterator, and native execution itself is outside the mod guard.

The retained build-502 baseline is unchanged. Its compiled comparison has **76 changed,
45 added, zero removed types**. An additional private comparison with the prior build-515
compiled output has **21 changed, three added, zero removed types**. Thirteen of those
changed files contain only propagated 1.0.3 -> 1.0.4 / 515 -> 516 constants. The eight
behavioral types are CardsDriver, CardsModule, VRCard, VRCardFactory, RemoteWidgetMirror,
GrabbableModal, MrBacking and PanelInkBounds; their diffs and the three new helper/hook
classes were reviewed. Decompiled local-variable renumbering introduces no extra source
change.

Surfaces: **625 config keys / 164 Harmony signatures / 4,729 log tokens**. No removal.
Generated patch inventory: **120 classes / 187 patched methods**, all registered once.
The only new patch is the native scene-load iterator hook. No asset, card-face policy,
wire grammar or release branch changes are included.

Local integration logs: `/tmp/gvr-516-guard.log`, `/tmp/gvr-516-release-build.log`.
The new production harnesses are registered in local required tests and both hosted workflows.

Automated checks establish the covered ownership, geometry and decision paths;
these logs and the screenshot cannot establish the new headset output. A hardware
retest remains required for the last discard page, MR opening/resizing/empty-window
transitions, and round restart, including a current multiplayer observer.
