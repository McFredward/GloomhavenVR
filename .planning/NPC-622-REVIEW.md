# Build 622 paired NPC review

Status: implementation, causal review and integrated validation complete. The
complete integrated attempt and every failed scope are retained; bounded
continuations reuse unchanged successful coverage. No Build622 headset result.

## Inputs

Immutable hardware inputs and SHA256 hashes are retained in
`.planning/debug/npc622/inputs.json`. Both build banners identify Build620;
the host Player log contains Debug records, the observer is at normal Info.
All nine new NPC screenshots plus `npc_fenster_problem.jpg` were inspected.
The screenshot proves an empty cabinet, malformed fan presentation, an oversized
held purse, an edge-on enchantment aura and absent enhancement rows. Those visual
facts do not prove every transport root cause by themselves.

## Causal findings and corrections

- Merchant inspection bypassed the approved scenario avatar/item-fan transport.
  It published full town modules for each fan face/body, waiting on template and
  immutable presentation delivery before visibility. The canonical metadata/rig
  path is restored, with separate originals only for palm offerings/returns. The
  actual inspection pile now supplies its immutable character key; the previous
  ability-fan owner could be zero while the item fan was visible. Public map item
  metadata is sampled atomically with its held pose, and its native list count,
  seat and item ID reject stale inventory and same-length reorder mismatches.
- The observer reports missing `shader|GloomhavenVR/TownNpc` while applying public
  cabinet modules. Bare Shader.Find cannot load a shader asset in an open bank.
  Resolve now uses the existing BundleShaders original-path loader, and template
  material registration records its original shader before texture properties.
- Current/transition public shelf cards and mechanics were not consistently
  prioritized together. Visible native modules now precede invisible prewarm work;
  same-author page updates retain ready original clones rather than retiring the
  entire cabinet. Future member epochs wait for their matching owner clock; the
  original physical cassette follows that clock during dependency delivery.
  Author changes still use atomic handover and every original card/holder.
- Purse motion was measured against a styled hand root on the sender but consumed
  relative to the unstyled remote holder. The world/style scale matrix must match
  the approved holder frame for both offsets and size.
- Purse input used a label rectangle as its pick target while its PCG body sits
  65 mm below that rectangle. Mesh bounds, grip and bowl-release sampling must
  follow the actual original purse body, leaving text readable.
- Fixed purse template/material preparation was deferred until first temple use.
  Both original wrist/held template keys are now prepared during normal town
  preparation when their native prop is ready; the body publishes before its label.
  The labelled template root and its body now both follow the actual holding
  hand, or no hand while in the bowl. Closed-wrist visibility does not interrupt
  a donation sink or ordinary return animation.
- The native enchantress aura tween writes WORLD Euler-Z, changing the normal of
  a tilted card. The original tween phase now transfers to the actual offered
  card plane after final pose and before publication. Native highlights retain
  their original geometry/callback owner; observer copies stay inert.
- Offered faces depended on NativeSource from an independently pooled/hidden
  card-list widget. Publication now follows the actual offered face and retained
  immutable OfferedCardId, just as original return flights do.
- The actual enhancement rows come from native slotsPool in CollectDynamic,
  not temple book inscriptions. The old coarse root-rectangle visibility test
  discarded complete branches even with visible native child graphics. All
  native enhancement rows publish with priority/prewarm; the original inherited
  viewport/masks still own actual clipping and scrolling.
- Non-immersive town mode changes run Exit, clearing temple rows/selection and
  rewriting the shared borrowed banner. A scoped original map-surface switch
  preserves the live service controller, title, rows and decision state. Native
  map location visibility reads the requested map during the scoped call;
  explicit service close and all non-converted/closed windows retain native flow.

## Explicit visual exception

The maintainer's 2026-10-04 instruction makes the temple purse pre-drop ghost
visitor-local, extending the already approved town CARD pre-drop guide exception.
Actual wrist/held/donated purses, offered NPC poses, cards, widgets, hover effects,
return flights, blessing effects and audio remain shared. Scenario guides are
unchanged. AGENTS.md records the new source; historical Build614 notes are history.

## Story reconnect

The failed join uses `PPF3G8JSD`; the current host room is `PPF3G8JSD6`.
The full code subsequently connects successfully. This specific error is
GameDoesNotExist, not evidence of a native Story admission refusal.

Native GHNetworkCallbacks.ConnectRequest already accepts Campaign/Guildmaster
story connections and sends GameToken.WaitUntilSavePoint. Native client handling
keeps the connection pending until SavePointReachedEvent, then requests a fresh
host save. Clearing that wait would load an old save: GHHostCallbacks only writes
a new campaign/guildmaster save immediately for MapHQ. Do not change Photon,
NetworkManager, gameplay actions or save-phase safety merely to bypass that gate.
The native join-window and NetworkManager front doors have no Story-phase gate;
checks there concern code, privilege/crossplay and an already active connection.
The native continuation is also checked: SavePointReachedEvent requests a fresh
host save; the host waits for its SaveQueue before streaming it. Pending connections
remain JoiningPlayers until PlayerEntityInitialized and do not enter the AllPlayers
story action target. The native city-event save completion notifies joining players;
round-start notifications provide the scenario equivalent. The MapHQ lobby keeps
ready controls gated while the actual player initializes. No synthetic completion,
ready/assignment change or forced old-save load is introduced.

This is a verified existing story connection plus safe-checkpoint wait, not newly
implemented immediate story viewing. The supplied failed attempt used an incomplete
code, so it does not justify changing native admission rules. If a later run with the
complete code receives an actual admission rejection, retain that exact native error
and phase for a distinct investigation.

## Proof limits and repeat policy

Earlier probes used an empty purse rectangle/styleScale=1 and an axis-aligned
card aura; neither represents the supplied headset geometry. Corrected checks
exercise original PCG mesh bounds, world/style combinations, native world-Z writes
and pitched/rolled cards. Source tests alone cannot prove a headset picture.
Retain original failures; after a limited fix, repeat only its affected scope.
The complete integrated attempt executed all 129 scopes: 119 passed directly and
ten failed. Retained focused resumes distinguish obsolete fixture bindings and
one overloaded Unity import from production behavior. Final review subsequently
found two production gaps below; their affected checks must run on the corrected
tree. Unaffected passing scopes must not be repeated merely because another scope
failed. Golden vectors, final source/compiled/surface/bundle checks remain required.

## Why the first cabinet and pose corrections were insufficient

Canonical avatar artwork preparation alone does not prepare a cabinet page. The
catalogue constructs original widgets for all entries, but hidden price/body/card
partitions and original module snapshots remained lazy. The public publisher
registered only the current/transition warm entries and retired unseen entries.
At a new-category replacement boundary, the receiver correctly hid the outgoing
page while the target's original dependencies were still absent. Seeking the real
owner clock, retaining same-author clones and prioritizing visible cards therefore
could not guarantee a complete target page. The corrective boundary is the actual
prepared original catalogue and atomic clock/content admission, not a button gate,
an invented placeholder or pausing the observer's animation.

The native publication schedule also captured town services from the use-bar step
before TownServicePresentation.LateTick finished handoff/card/ring/widget writes.
Isolated probes arranged those writes before capture, so they could pass while
the actual registered LateUpdate order still sampled the previous owner pose.
The corrected schedule retains the existing board/use-bar publishers and places
only the read-only town capture after all original town/layout writers. A focused
probe binds the real registered step list, not a rearranged simulation.

The final golden-vector run also caught a transport boundary missed by the local
widget probes: rounding the expanded presence envelope to 8192 required eleven
existing datagrams, exceeding its unchanged five-second deadline under saturated
traffic. The legal 7700-byte presence and 7957-byte writer now fit an 8000-byte
envelope and ten datagrams. All 32 simultaneous streams complete again without a
scheduler/deadline change. The original six failed vectors and subsequent limited
repairs are retained; that full vector run passed 307957 assertions. The final
full run including independent prepared-clock and retirement vectors passes
308039 assertions.

Original bank bounds are based on actual asset measurements, not the earlier
five-node fixture. The real inactive/inert native ItemCard has 25 nodes and its
complete codec module is 17933 bytes. Twelve/24/36/48 metadata-distinct faces need
215244/430488/645732/860976 bytes before outer bank stamps; already 36 exceed 512 KiB.
The bank therefore uses a bounded 4 MiB inflation limit, unchanged 55 KiB compressed
limit and unchanged 60000-byte outer packet/32-frame bundle limits. Exact bank
round trips preserve every measured original node/material/text. These are
card-face lower bounds: original price/body/holder partitions and three editor-
unbound packed atlases are explicitly outside the measurement, so 4 MiB is a
conservative chosen bound, not a proved whole-game worst case.

## Additional 1:1 review

The offered-face/aura path is sampled after the final owner-facing pose. The
native enhancement rows retain the original masks, scroll and hover animation;
observer copies do not run gameplay callbacks. Existing TownServiceMotion
interpolates shared head-facing rotations at render cadence, including modules
whose position is rigidly attached to an interpolated avatar hand. No second
rotational smoothing path or observer-facing decision window is introduced.

The canonical map item renderer borrows the original ItemCardUI from the native
pool with activation disabled, uses resident original background/class icon
sprites and retains its initialized native text/layout. Pending original fronts
do not draw gray public placeholders. Preparation warms the same original pool
before normal town loading completes, including the public stock and real party
items. Lazy recovery remains when a pool or late item is replaced.

The standing mixed-language ruling is explicit, not a new exception invented for
this review: Core/Loc/Loc.cs records the maintainer's 2026-08-28 acceptance. Text
transmitted as rendered text retains the owner's words; key-derived originals
use the receiving game's localization. No new language divergence policy is added.

The complete existing scenario item/fan path remains covered by the integrated
gate. Public map metadata is guarded out of scenario sampling and reception;
selection secrecy and local controlled-card visibility are unchanged.

## Additional causal review of the prepared bank

The first prepared-bank fixture was invalid: its nested iterator was included but
never executed. Running the four causal controls exposed that omission. The actual
Editor driver also ignored yielded wait objects. It now executes nested iterators,
CustomYieldInstruction and native AsyncOperation waits, disposes nested routines,
and fails on an unsupported yield. Each run first checks nested entry, waiting,
completion and cancellation. The cold-bank proof verifies actual native unload
completion. Old apparent positives and escaped controls are retained as failures.

Once the bank case really executed, it found another production defect: registering
all dormant originals once meant a category change no longer registered new modules.
The current advertised member list could therefore remain obsolete until the
five-second manifest heartbeat. Rack membership changes must publish their matching
census immediately, rather than relying on initial registration.

Native motion is part of parity. A changed original price/availability target is
admitted atomically, but its existing 0.1-second colour transition is retained.
The proof checks the actual target immediately and rendered colour after that native
transition; requiring a snap would have incorrectly removed approved presentation.

Preparing hidden originals also exposed a lifetime defect. Native pools can lend
the same card GameObject to a different catalogue mount. Replacing its SourceEntry
without retiring the prior resident partitions left old modules registered and
outside the ownership census. The replacement now unregisters those old resident
modules before publishing the new borrower. Ordinary non-bank/private lifetime
handling is unchanged. A real mount change proves the old ID is gone and the
registered count stays constant; its causal control reproduces the leak.

The dormant ownership walk is bounded to a half-second census. Current or newly
seen sources and destroyed native objects still retire immediately. Stable hidden
originals no longer traverse their parent hierarchy every rendered frame. This is
an implementation access reduction, not a measured headset FPS improvement.

The actual original-card transport measurement is retained in
`.planning/debug/npc622/bank-transport622/`. With representative competing production
streams, 12/24/36/48 original card faces assemble in 700/850/900/950 ms after a
category change. Those are lower bounds, excluding network transit, physical
holder/price/control partitions and rendering. They are not headset results or
proof of an immediate cold-category response. Applying a bank's sampled animation
age only at receipt also replays an obsolete transition; the corrected playback
uses a newer, matching native session clock when one has already arrived.

## Prepared originals and measured warm response

The remaining transport delay was measured with the actual original housing,
not a minimal synthetic clock. Its capture has 34 nodes and 7561 bytes; original
ItemCard captures remain 17933 bytes each. Sending all original properties again
for every category clock retains correctness but wastes the prepared receiver bank.

The receiver now retains validated complete originals independently of manifest
membership and visible clones. The publisher sends genuine dormant originals in
bounded background rounds, without constructing dormant observer hosts. Each
module keeps at most three revisions, including its earliest complete basis;
peer removal, session replacement and template-object replacement are explicit
lifetime boundaries. A higher claim may reauthorize exact content only for the
same actual sender, service and session, never another visitor's originals.

Additive record103 carries every current owner header and, where needed, native
property changes against an explicitly named original content key. Visibility,
parent, pose, canvas and original rack-member state are authored values, not
observer guesses. Exact target hashes, complete membership, original templates
and actual parent bindings are validated before clock/pending admission. Missing
bases or changed topology reject the reference atomically. A complete record102
repair remains queued on every original heartbeat; local completion is not an
acknowledgement. Reference and complete packets with the same source sequence
survive main-thread coalescing independently.

The first warm implementation also failed its real timing test: a fragmented
clock yielded to full repair per fragment, and an empty background turn left
the priority counter permanently above its threshold. The full bank then finished
before the quick clock. Both defects are corrected: clock completion owns the
next repair turn, unfinished assemblies keep their namespace, and an empty
background turn resets arbitration debt. The original global event cap and
two-urgent/one-background share remain.

With 12/24/36/48 actual ItemCard captures and the complete housing, final warm
clock assembly takes 100/150/150/150 ms with idle presence and 250/250/300/300 ms
with representative competing presence, animation and three widget streams.
The clocks occupy 3/4/4/4 datagrams under the unchanged 864-byte / 50-ms cap.
The rejected implementation took 900–1700 ms. These deterministic scheduler
measurements exclude network transit and headset rendering. First cold admission
still requires complete originals; the measured cold boundary is 850–1550 ms,
and loss repair remains necessary. No zero-latency cold join is claimed.

The final affected mirror and avatar receive probes pass 233363 and 345
assertions. A fresh stronger public-bank probe passes 14389 assertions, retaining
actual root/parent admission, changed original prices, populated far-page body/
pose, peer lifetime and delayed matching-session playback. Its obsolete
"no dormant packet" expectation was replaced by an actual complete original
prewarm check. Earlier causal controls retain their exact unchanged triggering
paths; failed originals are preserved rather than relabelled as successes.

The new clock proof exercises original template lifetime, exact content keys,
native property changes, higher same-author claims, missing bases, malformed and
nested packets, and atomic rejection before pending state mutation. Independent
literal/CRC vectors and deliberately broken CRC, nested-clock and empty-background
arbitration controls are included. The complete native bank remains the loss
repair path; neither transport completion nor a cached key is a remote receipt.

The final lifetime review found another long-session failure: genuinely retired
native IDs remained in the sender queues and receiver cache. At the 4096-module
bound new dependencies could be refused until session reset. Explicit unregister,
native root replacement and lane destruction now publish scoped local retirement
through fixed 24-KiB bitmaps. The sender removes those exact queues/bases while
preserving fragment counters and completing an already-started immutable bundle.
Ordinary off-page visibility does not retire prepared cards.

At receiver capacity, only the oldest unused immutable module is reclaimed;
current pending, living original clones and active prepared-bank dependencies are
protected. The sender proof fills actual slots, retires 4000 source IDs and delivers
a genuine replacement through the assembler. Receiver capacity, actual native
unregister/replacement/clear hooks, and disabled-retirement controls are exercised.
Final warm lifetime and receiver probes pass 147 and 144 assertions; independent
wire checks pass 82. The final changed mirror scope passes 233471 assertions.

The deliberately saturated loss model retains progress and complete repair, but
its final clock takes 1500 ms and complete loss repair 31 s. It continuously emits
large original visitor and background traffic plus twelve page interruptions;
this does not model canonical held-item101 motion or ordinary unchanged stock.
Earlier stress failures lost the local-only scheduling flag in a test copy and
reported last heartbeat instead of first arrival. Both invalid measurements are
retained with the corrected observer. No low saturated-latency claim is made.

## Integrated validation and hardware boundary

The initial complete local attempt is retained with all 129 scopes, including
its ten original failures. Focused continuations resolve the affected failures;
unchanged successful scopes are retained with source/runtime binding evidence.
The current inventory additionally includes final capture order and prepared
catalogue clocks. The complete local wrapper stopped on the original failures
before goldens; its remaining phases were resumed explicitly. This is not a
claim that the original wrapper returned success.

Final source coverage passes all 14 scopes. Strict Release has zero warnings and
errors; all five EN/DE player-document pairs, original bundle/figure banks, and
the config/patch/log surface are checked. Full byte/transport vectors pass
308039 assertions, and both send-allocation causal controls fail at their
intended assertions. The compiled audit explains every changed production type,
with no unexplained removal or behavior change. Compact evidence and the exact
continuation are retained in `.planning/debug/npc622/validation-ledger.json`.

The next paired hardware test must use Build622 on both peers. Check repeated
category/page changes including an early joiner, item fan and held-card motion,
wrist/held purse scale and grip, visitor-local drop guides, original enhancement
rows/scroll/hover, tilted card aura and returned cards. Check ordinary service
windows across City/World switching and reconnect with the full room code.
Automated transport/Unity evidence is not a headset picture or measured internet
latency; first cold dependency delivery remains an explicitly measured boundary.
