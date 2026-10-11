# NPC670: first tutorial merchant confirmation in window mode

## Hardware evidence and cause

The maintainer reports that the first purchase in a new campaign's merchant
tutorial opens a separate confirmation window in non-immersive mode. The
inspected `seperates_fenster.jpg` shows the original merchant beside a large
purchase confirmation hosted by the New Party Display. Immersive mode already
works on the maintainer's hardware and must retain its existing ownership.

Frozen inputs are under ignored `.planning/debug/npc670/inputs/`; the manifest
records all sizes, modification times and SHA256 values. The current local
Player/LogOutput banners identify ModBuild669 and game1.1.8323.0. The remote
Player.log is older evidence from the preceding unmodified-host report, not a
paired observer log for this new tutorial run. The current run enters BuyItem
and shows the native item confirmation under the New Party Display. Its end
does not record an actual shop exit or reproduce the historical tutorial hang.
The shutdown VoiceChat.OnDestroy exception is not evidence of that hang.

The placement code used a process-wide Subscribed flag for an event belonging
to a particular UIItemConfirmationBox. Map reset retained that flag; a new
campaign/HUD could replace the native singleton without subscribing the new
instance. Its Buy/Sell event was then lost, so the confirmation remained in its
native party-display home. There is also a cold-start race: ShowConfirmation
raises its one-shot discriminator before the next modal tick can subscribe.
The native class does not retain that discriminator for later recovery.

## Bounded repair

Track the exact native item-box event target, detach its old subscription and
retire its presentation/request state on replacement or reset. A void Harmony
prefix observes the actual ShowConfirmation instance before its unchanged
event. Buy, Sell and General still come exclusively from that original event;
the mod neither invents a request type nor changes transaction callbacks.
The existing seat then places Buy/Sell in the merchant's converted frame.
Original confirm, cancel, host-close and fade continuations remain authoritative.
Immersive presentation ownership has precedence, including its retirement mask.

No tutorial production change is justified by the supplied evidence. The
shipped FinishEnterShop starts the serialized BuyItem step after its introduction.
Native inventory purchase and confirmation cancellation do not finish that
lesson. The native exit/hidden continuation does; the existing converted-window
close bridge preserves that exact manager, shop, step and pending promise,
finishes once after the shop has closed, and rejects callbacks from an old save.
Tests bind the native lesson and map-listener bodies rather than simulating a
purchase as tutorial completion. Purchase and confirmation boundaries in that
tutorial fixture are explicitly ports, with their shipped bodies separately
checked; the item-seat fixture binds the native confirmation methods themselves.

Build670 retains Version3 and next-free118. There is one additional presentation
observer registration, no asset, configuration, input, wire, stock, gold or save
write. Build669's unmodified-host merchant, accepted loading/flight/overlay
behavior, NPC666 intrinsic effects and Build668 list placement remain unchanged.

## Validation and integration

Workers and integration use separately initialized worktrees from dev
`bdb9206d9140c03bae3e9440caa96cf755da4e4d` with disjoint ownership. The private
compiled baseline copies the verified final Build669 snapshot; its final source
exactly matches that dev commit. The shared baseline symlink is detached before
copying. Every copied type hash is recorded, and the original guard's snapshot
function is reused unchanged for the current compiled comparison.

Final focused results and receipts are recorded below after integration. This
bounded repair reuses Build669's composed216/216 evidence for unchanged areas;
it is not a new complete-gate run. Each completed test run cleans only its owned
generated caches after the process group is reaped, preserving logs, manifests,
native source captures, controls and hardware inputs.

Automated native-continuation and geometry checks cannot establish headset
appearance or an actual multiplayer tutorial outcome. First-open purchase,
confirmation inside the merchant, cancellation, exit to InteractWithMap and a
second new campaign in the same process remain the next hardware checks.
