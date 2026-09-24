# Town revision 549 — integration evidence

Work remains exclusively on `feature/immersive-town-services`, version 1.1.0.
The approved [cabinet concepts](town549-concepts/README.md) and the maintainer's
merchant-palm request govern this revision. Automated and offline render results
are evidence for their measured conditions, not headset confirmation.

## Presentation and interaction

The merchant now has an upright side cabinet with iron fittings, a hanging native
lantern, six original category pictograms on physical buttons, and a physical page
crank. Twelve original item faces sit in its recessed cassette. During a category
or page change the cards withdraw, the folding shutter closes, membership changes
behind it, and the next page emerges. The cabinet exists while browsing the map;
approach and picking up a card do not open the old shop window or spend gold.
Native decorative assets furnish the small ledger/coin work surface beside it.

The selected controlled character's complete owned-item fan replaces their normal
map cards inside the merchant's attention volume. Owned items and cabinet stock use
the existing scenario item grip and hand-transfer machinery. Releasing either into
the actual offered palm requests selling or buying respectively. Both paths open the
original explicit confirmation; only its confirmation callback performs a transaction.
Owner/context changes, leaving, and cancellation retire the request and restore the
normal fan. Nontradeable items remain inspectable. Disabling physical map hands retains
the original merchant/enchantment window path instead of leaving an unusable NPC.
See [handoff contracts and performance](TOWN-MERCHANT-HANDOFF-549.md).

The public cabinet and each owner's inspection/confirmation presentation have independent
publication lifetimes and fragment assemblies. Source item faces, backing geometry,
palm overlays, intermediate cassette motion, original icons and confirmation widgets are
published through the existing native presentation system. Additive TLV86 identifies the
public lane and cassette mechanics; existing record layouts and wire version 3 remain.
The ordinary generic held-map representation excludes these inspection cards so peers
cannot see a duplicate or differently sized card over the original output.

The revised wrist solver distributes forearm pronation through six support bones per
actor. It retains the approved face geometry and original lower-body data. Prop-contact
paths, prayer interruption and both offered hands are checked on the imported deformed
surfaces. See [motion evidence and limits](TOWN-MOTION-549.md).

The original custom-room bundle is unchanged. All three residents occupy the front
semicircle in the canonical map frame, with separate church/enchantment visitor spaces.
The layout clears actual original room/native furniture geometry rather than enlarging
rooms. See [room clearance](TOWN-ROOMS-549.md).

Town materials bind the explicit, stable set of owned practical lights instead of Unity's
per-renderer changing nearest-light selection. The native scene's lights remain untouched.
See [lighting reproduction, rendered controls and limits](TOWN-LIGHTING-549.md).

## Validation status

The final Windows town bundle is 96,167,323 bytes, SHA256
`d5f413b9159c18dd0651ce68e302923c1ac9d72bc2c00ae8d06c79866e9675e5`.
The original environment bundle is byte-identical, SHA256
`fe1a659c17b4151e929691aa070d402b8cd299a462315b1d6691d2622d491693`.
Combined source assets pass 888,814 assertions and nine rendered negative controls
(`/tmp/town549-final-combined-assets/town-assets-kyov0s8q`). Real imported lighting passes
56 assertions and three historical rendered controls on that exact source-review bundle
(`/tmp/town549-final-integrated-lighting/lighting-4p_i_9oo`). The full-capacity light binder
allocates zero managed bytes over 1,000 calls; its measured Editor CPU average is
0.02502 ms, not headset GPU time.

All fourteen source gates passed. The complete local run recorded all 69 suites:
65 passed immediately, four failed on stale fixture dependencies/assumptions, and each
was corrected and repeated with its compiled negative controls. The original failed
report remains `.planning/debug/test-runs/20260924-094239-2b469c4c/results.json`;
it is not relabelled as a successful whole-run report. Targeted final evidence:

- Merchant handoff: 1,186 assertions / seven negatives,
  `debug/town-merchant-handoff/run-o3d7dc35`.
- Station setting/lifecycle/grounding: 1,886 assertions / sixteen negatives,
  `/tmp/town549-root-setting-final.log`.
- Native decoration: 76 assertions / ten negatives,
  `debug/town-decor/run-1wfrqtb3`.
- Actual workspaces: 278,277 assertions / fourteen negatives,
  `/tmp/town549-workspace-final/run-5ffrxzmx`.
- New lazy original-backing partition regression: public-catalog 1,102 assertions /
  seven negatives, `debug/town-service-mirror/run-x49ddlsj`.
- Final measured placement: 454 assertions / six negatives,
  `/tmp/town549-final-clearance-repeat/run-x9lez63l`, and zero original-room/native
  furniture contacts in `/tmp/town549-accepted-scene-audit.json`.
- Compiled wire vectors: 286,103 assertions, `/tmp/town549-compiled-wire-final.log`.

Final imported motion passes 268,749 assertions and seventeen negatives, including the
intermediate forearm-support wrap regression. Exact triangle/seam audits found zero
arm/torso or opposite-arm intersections in 1,943 sampled poses; all six injected surface
faults were detected. Details and sampling limits are in the motion report above.

The hosted-CI portable motion variant initially included the new forearm-skin mutation
although that mode intentionally does not compile a Unity rig. The mutation remains in
the full Unity suite and is now explicitly assigned there; a missing mutation target
also fails immediately. The corrected portable phase/network suite passes all eight
production/negative variants (`/tmp/town549-final-portable/run-6s_28j9n`).

Strict Release has zero warnings/errors. Bundle-format and surface gates pass; the
compiled historical baseline comparison reports implementation changes, not program
equivalence. The complete test ZIP and its DLL/bundle CRC/hash evidence are recorded in
`debug/town549-package-verification.json`; the exact pushed head's CI outcome is checked
at handoff. No paid generation API was used for this revision.

## Hardware checks

1. Inspect cabinet details and original scenery proportions in both custom environments;
   check resident placement, original lantern/candle lighting and moving faces/arms.
2. Approach with different controlled characters: the fan contains all their owned items.
   Pick up and transfer both stock and owned cards between hands. Look at front/back and
   card size, then leave and confirm the ordinary map fan returns.
3. Offer stock to buy and an owned item to sell. Test both confirmation and cancellation,
   insufficient gold, an untradeable item, character switching, and walking away while
   confirmation is pending. Pickup alone must never buy or sell.
4. Use every physical category button and turn the crank through multiple pages. Watch
   withdrawal, shutter closure and emergence, including with another card held.
5. Repeat with another VR player: cabinet, owned fan, held cards, palm marks and native
   confirmations must match the source, including join/leave and simultaneous browsing.
6. Disable immersive services and separately disable physical map hands; verify the native
   window interaction remains available. Test repeated enable/disable and map transitions.
