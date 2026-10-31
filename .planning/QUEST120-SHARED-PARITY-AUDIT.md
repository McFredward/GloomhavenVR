# Quest 1.2.0 rebase: shared behavior parity audit

Date: 2026-10-21. This is a source review, not a headset acceptance claim.

## Reviewed inputs

- Published Quest B713: `f4539f297a948476b593f6fd0ebc561f04e22a8e`.
- Requested release base: `origin/main`, tag `v1.2.0`,
  `4f10ddd60524911bf4cf39777df46f95d7691d83` (distributed PC Build715).
- Common ancestor: `09bbef45e926d984754caeb02b83642eb878015e`.
- Isolated review worktree starts at current dev
  `fd45d606f`; all comparisons use the explicit release/Quest refs above.
- Exact per-file SHA-256 inputs and the scoped shared diff are retained in ignored
  `/home/claw/quest3-local/build/evidence/quest120-rebase-20261021/parity/`.

Read `CLAUDE.md`, `.planning/STATE.md` and the current `NetProtocol` build notes
before review. No production source, game references or other checkout was
modified by this lane. No Unity import, APK build or runtime test was necessary
for the initial source-obligation inventory.

## Shared changes which must survive

### Native Continue permission

`WorldUI/Modal/RewardShowcase.cs` and `AnnouncementContinue.cs` have no Quest
adaptation. Retain the complete release versions, including their diagnostics.

- A separately open ESC window cannot veto an explicit ready pointer Continue.
- `RewardShowcase.ConfirmBlock` follows the running MEC reward process rather
  than requiring `UIRewardsManager.isActiveAndEnabled`.
- Keep native process/input-latch, reveal, button/interactability, actor/network
  authority and campaign/postquest routing. Never auto-confirm or close another
  mandatory window.
- `AnnouncementContinue.CanConfirm` retains native reveal/tracker/character
  permission after removing only the imported ESC gamepad blocker.

### Live figure pickup and pointer arbitration

Retain the release's complete arbitration chain. Testing or merging only
`ProximityGrabber` is insufficient because every world pointer entry must yield
before its callback, while established presses/gestures retain the trigger.

- `ProximityGrabber.FigureGrabOffered` revalidates the same highlighted figure's
  live registration, usable shape, pose, hand filter, native permission and reach.
  Loss revokes the glow immediately. Figures do not use the non-figure visual
  hover tail. A grip-bar pickup also clears the competing figure promise.
- `FigureGrabbable.CanGrab` checks the actual original permission-bearing collider,
  independently of an enlarged mod reach volume. Dead/busy/remote-held/mode gates
  are retained.
- `FigureGrabDriver` only retains a switching-dwell winner when it was eligible
  in the current sweep. Far pickup yields to a live near offer and distinguishes
  a real trigger claim from ordinary hover.
- `FigureStretch` yields on capture entry to a live figure offer; an already
  captured/active gesture publishes a committed claim and excludes a rival offer.
- `RayInteractor.SetWorldUiHit` and `SuppressWorldFarClick` describe hover only;
  `ClaimTrigger` owns the current pull. Unknown legacy `UiHitOverride` writers
  remain conservative. Mode/pose loss clears both hover and trigger provenance.
- Preserve the corresponding yield-before-action and claim-before-callback paths
  in `BoardClickDriver`, `CardsDriver.3.Laser`, `CardsDriver.5.Interactions`,
  `RayGrabDriver`, `RayUguiDriver`, `UguiPointer`, `FlatScreen.6.Pointer`,
  `MapButtonRail`, `MapLocationInteractor`, `CombatLogSurface`,
  `TownServiceCatalogCategory` and `TownServicePhysicalRay`.
- In particular, the flat-screen fingertip entry runs before its laser path, so
  `TryBeginPoke` must check `FigureGrabOffered` itself. An existing fingertip or
  laser press keeps its release/drag lifecycle and committed claim.

Most of those files can remain byte-identical to release main. The reviewed
combined files are `RayInteractor.cs` (Quest marker/geometry),
`FlatScreen.6.Pointer.cs` (Quest coordinate/collider behavior) and
`VRRigDriver.cs` (Quest runtime tracking/scenario hooks). Quest-specific code must
not reintroduce unlabelled hover-as-action writes or a visual-only figure offer.

### Menu rig continuity

`VRRigDriver.UpdateRigLifetime`, `RefreshMenuAnchor`, `IsLiveMenuAnchor` and
`ResolveMenuCamera` retain the release's menu-source continuity. Native intro/menu
anchor disablement, destruction or successor replacement must not destroy the
still-valid owned tracked head/root or reset its physical pose. Rebind only the
native vantage metadata, clip/depth/occlusion bookkeeping and camera policy sweep.

Real kind changes, explicit rebuild requests, owned camera/root loss and VR-off
still tear down. Scenario/map anchor policy remains unchanged. Do not overwrite
live MR-owned clear/HDR/stereo/target state or re-arm automatic recenter on native
menu anchor replacement. Retain Quest scene/tracking callbacks and actual Quest
eye behavior when combining this file.

### Mixed-reality backing depth

Keep these four files byte-identical to release main:

- `WorldUI/Conversion/PanelInkBounds.cs`: the existing admitted/clipped visible-ink
  walk also records the furthest actual painted `BackZ` in host-local coordinates.
- `WorldUI/MrBacking.cs`: `IDepthBacking`, depth sampling/reporting and
  `SeatBehindContent` preserve the original physical gap behind real content.
- `WorldUI/MrBackingAnimation.cs`: materialisation samples and seats using the same
  current depth; it cannot revert to an old host-plane assumption during opening.
- `Net/Remote/RemoteWidgetMirror.cs`: converts the retained content depth into the
  mirror host's local frame and exposes it through `IDepthBacking`; reset paths
  clear it with bounds/source state.

This is UI backing only. No board/environment MR geometry, native graphic
transform/order, resolution, shader or wire change is authorized by the rebase.

### Movie test permission and existing Quest UI repair

`VROptionsTab.VideoReview.BuildVideoReviewTriggers` must remain reachable only
from Advanced > Cheats and the existing default-off `CheatsAvailable` gate.
Each action rechecks that gate when pressed; stale cached buttons cannot start a
spoiler movie after revocation. Close remains available. Ordinary game-authored
movie playback is independent of this test permission.

The label lookup is unchanged between the common ancestor and release main.
Quest B713 deliberately uses `FindPart<TMP_Text>(status.transform, "Title")`
instead of `GetComponentInChildren<TMP_Text>(true)`: the preceding Quest review
identified an inactive template label being selected before the authored visible
Title. Preserve that existing Quest adaptation. A Quest-to-main difference alone
does not identify a new upstream repair. Release removal of the obsolete SteamLink
projection-probe action is retained; it must not be resurrected by Quest's
cheat-page delta.

## Merged tree review

First frozen integration: `f5a60cef7`, based directly on the requested release
main. Of the 27 explicit file inputs, 24 match release main byte-for-byte;
`VROptionsTab.VideoReview.cs` matches Quest B713 (the intentional authored-Title
repair), and `RayInteractor.cs` / `VRRigDriver.cs` combine the two branches.
`merged-source-comparison.json` retains every exact hash.

The combined `RayInteractor` diff adds the existing Quest pointer renderer,
marker and bounded evidence code outside the shared arbitration logic. The new
claim/hover separation and its mode-reset state match release main. No loss of
the figure arbitration, Continue permission or MR backing-depth changes was found
in those reviewed source sections.

One concrete integration defect was reported before testing: both the new main
`VRRigDriver.cs` and retained Quest `VRRigDriver.MenuCamera.cs` define
`ResolveMenuCamera`, causing a duplicate-member compilation failure. Removing the
duplicate must combine main's `IsLiveMenuAnchor` and render-budget-owned suspended
camera handling with Quest's owned presentation-target acceptance and native UI
camera fallback. The old Quest immediate return of any `NativeCameraRenderBudget.Main`
must not overwrite the new liveness policy. The primary integrator owns this repair.

The primary integrator repaired the duplicate in `6be38d94e`: one resolver in
`VRRigDriver.cs` retains main's live-anchor predicate and budget-owned enumeration,
plus Quest's exact owned-target admission and last-choice UI fallback. The obsolete
partial file is removed. Independent review found no remaining shared-behavior
loss in the scoped source.

## Focused executable qualification

The integrator explicitly delegated test-only harness adaptations after the
source review identified the obsolete resolver fixture path.

- `quest-menu-presentation-tests.py` now extracts the actual single resolver and
  liveness predicate from `VRRigDriver.cs`, with a selectable frozen source root.
  Updated external camera boundaries expose real liveness and budget ownership.
  Its 26 production assertions and all eight compiling causal controls pass
  against the repaired integration. Actual capture-target predicates and routing
  are compiled; this managed seam does not execute Unity or claim headset pixels.
- `tests/menu-rig-lifecycle/run.py` now accepts a frozen production source root.
  Actual Unity also executes the complete production native-target predicate and
  exact capture-routing method, beside the production lifetime/anchor methods.
  New Quest cases prove UI fallback, world priority, no self-anchor, budget-owned
  suspension, inactive exclusion, foreign same-name/reassigned targets and
  unchanged desktop eligibility using real camera/texture objects.
- The repaired production Unity run passes **1,314 assertions / 126 actual
  Camera.Render callbacks / twelve native anchor replacements**. All three
  compiling causal controls reject independently: anchor teardown closes the real
  coverage source at the first gap; rebind motion changes pose generation; clear
  owner mutation violates retained camera policy. Strict fixture builds report
  zero warnings/errors.
- One first extended-fixture attempt expected loss of candidate target permission
  to clear a still-live existing native reference. The release intentionally keeps
  that reference when no successor exists; the final test queries actual resolver
  eligibility directly while independently proving tracked head/source continuity.
  No production behavior was changed to satisfy that overstrong test. Failed
  evidence and its source pins remain retained.
- All fixture children were reaped; `cleanup_completed_run` removed their owned
  Library/bin/obj caches while retaining source, manifests, logs, assemblies,
  results and causal receipts. Initial external-output runs were moved into the
  owned debug area for the same cleanup API, then returned as retained evidence.

Evidence is under the ignored external `parity` directory named above, including
`managed-resolver`, `menu-lifecycle` (first failed attempt) and the final retained
`menu-lifecycle-qualified` run. The whole MR owner, native receiver, actual XR
tracking and headset picture remain outside this fixture. The source audit and
focused fixture results do not certify a full gameplay gate or hardware outcome.
