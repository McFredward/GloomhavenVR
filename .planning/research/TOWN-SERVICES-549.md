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

The original custom-room bundle is unchanged. All three residents occupy the front
semicircle in the canonical map frame, with separate church/enchantment visitor spaces.
The layout clears actual original room/native furniture geometry rather than enlarging
rooms. See [room clearance](TOWN-ROOMS-549.md).

Town materials bind the explicit, stable set of owned practical lights instead of Unity's
per-renderer changing nearest-light selection. The native scene's lights remain untouched.
See [lighting reproduction, rendered controls and limits](TOWN-LIGHTING-549.md).

## Validation status

Integration is in progress. Final imported-motion tests, matching Windows town bundle,
complete required checks, package hashes and CI result will be recorded before handoff.
No paid generation API was used for this revision.

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
