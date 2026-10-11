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

Eight full focused suites pass over three targeted runner invocations: native
confirmation lifecycle, seated-subtree input transfer, onboarding, converted
close, map switch, modal close, message continuation and conversion rollback.
The six neighboring suites bind source unchanged by the subsequent seat
integration; the two seat/input suites run after that integration. All saved
suite/log hashes are checked against the unchanged216-suite manifest. This
bounded repair reuses Build669's composed216/216 evidence for unchanged areas;
it is not a new complete-gate run.

Confirmation validation passes all17 variants with114 positive assertions
(51 original,11 original owned-mask,41 item lifecycle and11 prefix-fault
continuation) and all13 executable causal controls. Native Show, confirm,
cancel, reset, OnHidden and UIWindow bodies bind the installed GH.Runtime.
Actual item artwork, transactions and conversion bookkeeping are declared
ports. Both cold first request and Reset -> replacement -> request-before-tick
chronologies reproduce the published defect. General, reset, old-event
detachment, callback identity, fades, host close and immersive ownership remain
covered. A throwing observation port executes the unchanged prefix catch and
proves native Show/event/card/callback continuation twice with one failure
report. Its unguarded compiled control fails the continuation assertion.

Four failed worker attempts remain archived. They exposed a strict compile
warning in a new mutation, shared-process Harmony/type contamination, an
incorrect no-reset historical chronology, and an unused field in the unguarded
mutation. Item variants now run in separate Unity processes; original controls,
expectations and warning policy remain. Timeout/interruption leaves a live
Unity project's caches for the central runner to clean after group reaping.

Integrated onboarding passes250 assertions/all20 controls and converted close
113/all13. The worker also passes the portable250/all20 version. The local
onboarding proof binds18 native bodies and hashes/checks two completion-free
purchase bodies; full inventory/network/save behavior remains outside its
ports. The exact source boundaries and synchronous native introduction
cancellation are recorded in [NPC-670-TUTORIAL.md](NPC-670-TUTORIAL.md).
Actual uGUI input transfer retains all nine controls. The other full focused
suites and all16 source checks pass. Strict Debug/Release builds have0 errors
and0 warnings; direct wire goldens pass300510 assertions; docs and bundles pass.

Public surfaces are666 configuration keys,236 literal patch registrations
(+1 observer) and4795 log tokens, with no removals. Generated inventory records
207 classes/305 methods, all registered once. The private compiled comparison
has1254 ->1255 C# files: two implementation changes (MapDialogSeat and
WorldUIModule), the single new observer, eight ModBuild-only consumers and one
branch-metadata-only BuildInfo change. Removing the new registration block and
normalizing generated Harmony locals makes the entire compiled WorldUIModule
identical to baseline. No unexplained production change or type removal.

Every completed runtime run cleans its owned generated caches after process
group reaping. Strict builds, compiled snapshot and direct goldens also remove
their completed object/test-binary caches after retaining report/source hashes;
current production deliverables remain available. Worker archives are extracted
in memory and checked against every saved member's size/hash. Logs, manifests,
native source captures, failed controls and hardware inputs remain protected.

Automated native-continuation and geometry checks cannot establish headset
appearance or an actual multiplayer tutorial outcome. First-open purchase,
confirmation inside the merchant, cancellation, exit to InteractWithMap and a
second new campaign in the same process remain the next hardware checks.
