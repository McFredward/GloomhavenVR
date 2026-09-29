# Build 552: town input and enhancement lifetime review

Evidence: the supplied local LogOutput.log reports ModBuild 551; remote build 500 is stale. All six 20260924 screenshots in debug/npc_probleme were reviewed. The log repeatedly reports the classic enhancement composite waiting while its card chooser remains beside the character panel. It contains no offering/reclaim trace proving the reported pickup cause.

## Source-proven changes

- Removed invisible resident torso pointer/poke boxes. Resident interaction uses proximity; physical cabinet controls and offerings keep their own targets.
- Canvas intersections now require a visible Graphic under the ray. Transparent layout frames, hidden CanvasGroups and disabled nested canvases cannot stop a beam. Visible decorative paper and disabled-but-visible controls still occlude underlying windows. Graphic mask/raycast filters remain authoritative.
- Added offered ability cards and owned item chips to the existing physical far-pickup route. These actual parked objects were absent from the route after removal from their original fans. Ownership, native confirmation reclaim permission, closer UI and trigger-release rules remain intact.
- Walking away from the enchantress closes the native destination using its existing exit path, including when no card has been offered.
- The native enhancement card chooser remains active for its pool and callbacks, but its masked wrapper moves out of the Character UI layout to the station. The old flat composite does not reattach it in immersive mode. Nested ignoreParentGroups no longer escapes the presentation mask; original grouping is restored on teardown.
- Handoff.NativeSlot exposes the actual selected native slot for the integrator's original per-card enhancement-point display. The native chooser's enhancementPointsText is character-wide capacity, not remaining slots for one card.
- Added independent temple fan arbitration and optional physical-token depth/upright-prop parameters for the priest's money bag. Default card behavior is unchanged.

## Verification

- Strict Release build: zero errors and warnings, /tmp/town552-input-build-final.log.
- Actual-production Unity interaction fixture: 1,217 assertions, 44 negative controls; /tmp/town552-interaction-final/run-s6r6pedk.
- Actual-production enhancement handoff fixture: 835 assertions, 18 negative controls, including native walk-away exit; /tmp/town552-enhance-final/run-uqo3u18u.
- Unity painted-UI and physical-pointer fixture: 88 assertions, six negative controls; /tmp/town-visit-9c5pc2hb. It binds production VisibleUiSurface and TownServicePhysicalRay, using real Unity UI/colliders and explicit hand/device/grab boundaries. This does not simulate a tracked headset or the complete ProximityGrabber frame loop. Existing interaction/handoff fixtures separately execute production ForceGrab and ownership gates.
- The visit fixture deliberately replaces obsolete NPC click-box expectations with no-invisible-target expectations. Its earlier disabled-nested-canvas test failed, exposed registry fallback to an ancestor canvas, and passed after the logical-canvas visibility guard.

The small uprightProp extension was added after these runs for the temple worker's money-bag fixture; integrated build/targeted tests must include it. Hardware must confirm laser behavior around handles and UI margins, actual card pickup from both palms with each hand, owner changes, walk-away/re-entry, original Character UI presentation, and the final station point display. Automated success does not establish headset appearance or remote parity.

## Follow-up: visible handles and native point mirrors

The two screenshot handles align with the Character UI and quest list. The Character UI's enhancement chooser extends its painted layout and therefore its handle width. No independent service grab-handle leak was established: ReleaseForTownService synchronously calls ReleaseForComposite, which destroys the previous grab owner; ritual and palm surfaces all supply physical anchors and never call GrabbableModal.Build. Owned service windows are rejected by the catch-all, enrolled-window collection and final conversion paths. Removing the chooser from the Character UI removes the observed width cause. The nested-canvas diagnostic does not establish that a grab handle was rendered; masking pixels and releasing a conversion are separate operations.

TownServiceCardSlots review: cloning each original point subtree through RemoteWidgetMirror does not inherit the hidden chooser wrapper's alpha. Its live-copy Pair uses the point root and descendant local state. The new helper must TickLive every frame for original highlight/enhance animation parity and compare per-index native point identity when a pool reuses a slot with the same point count. These changes were requested from the integrating agent, who owns that helper.

The purse follow-up executes production Token/ForceGrab/held release: world scales 1 and 2, pickup orientation and subsequent hand rotation, physical collider depth, inspect and hand eligibility, deliberate in-bowl release, cancellation, tracking loss and out-of-zone release. Its scale test required InverseTransformVector rather than InverseTransformDirection: the latter would apply map scale twice to the downward hold offset. Validation passes 1,273 assertions plus 48 negative controls at /tmp/town552-purse/run-b_u2m727. The interaction fixture's new BookInk boundary is explicitly visual-only; the actual book geometry is covered by the temple lane's ritual-layout fixture.
