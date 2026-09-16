# Native burn sequencing — build 513

## Evidence and scope

The user reports that playing a lost action from the first round recess lets the second
card slide into that recess while the original is still burning. The requested rule applies
to every burn: the native effect finishes before card movement or replacement, locally and
on remote boards. Latest supplied logs still identify local build 510 and remote build 500;
there is no build-513 headset capture. The defects below are source-proven, not a claimed
reproduction of new hardware evidence.

## Findings and changes

1. `CardsDriver.Rebuild` compacted round slots and changed actor/fan/active layout before
   its park sweep discovered the outgoing card's burn. Flight admission protected the departing
   card but not its replacement. Early discovery now retains all affected card layout and input,
   including character exchange, fan opening/closing and active-grid relayout. Board/head following,
   native loss callbacks and mandatory gameplay input continue. Quiet frames drain the pending
   holds, so completion does not need another model event.
2. Consecutive native burns must all finish. The layout barrier accumulates active artwork
   across the presented cards; it cannot be cleared by the second card being idle. Fresh lost
   membership reserves the old seat during the gap before the native timeline starts.
3. `CardEffects.BurnCard` assigns the result of `StartCoroutine` after its first synchronous
   step. An inactive/no-ramp iterator can finish before that assignment and leave a non-null
   finished handle. Existing spent paint defeats the old zero-paint bail heuristic. The existing
   native iterator wrapper now records actual completion, cancellation and exceptions without
   changing yielded instructions, native exceptions or game state. This prevents a permanent
   layout hold on a completed burn.
4. Remote semantic release and native appearance travel independently. Release can arrive
   before the final shader frame or before model loss. Remote layout retains the old actor and
   original rendered card, holds following flights, and waits for playback of the owner's actual
   completion. Canonical publisher identity is preserved on boards showing someone else's actor.
   Recovery and teardown retire obsolete holds; a timer never shortens a modern native effect.
5. Retain original native final output after the visible wrapper retires. Match by original
   roster provenance, not a mutable lost-pile address or a slot reused by another card. Live
   native output wins over retained frames. Recovery and roster replacement clear stale data.
   Durable terminal release records cover delayed/coalesced transport and successive burns
   from the same recess; old record-62 grammar remains unchanged.
6. Consumed item cards used a fixed unscaled flourish delay, while their native burn uses
   the game clock. A pause could start the return flight before the original burn finished.
   Track the actual `ItemCardEffects.BurnCardTimeline` iterator, retain the clipped original
   and defer repopulation. Native `isBurning` is unused and is not a completion signal.
7. Remote item faces previously applied settled consumed state without the owner's native
   transition. An independent bounded item appearance stream mirrors the original graphic,
   material and group output and retains the terminal frame for release admission.

8. Incoming focus previously inspected only cards already adopted by the VR factory. The
   candidate hand is now checked before committing a character change. Actual native iterator
   start publishes owner progress, including offscreen owned characters; remote and read-only
   local views retain their previous card presentation while that progress is active. Completion
   of an unadopted burn clears the wait without replaying an effect or inventing a flight.

## Producer audit

| Producer | Required retained presentation |
|---|---|
| Lost action in either round recess | Both round seats; no sibling compaction during burn |
| Short rest | Original spent look; no fan replacement before burn completes |
| Long rest | Selected discard card and fan layout; no recovered hand replacement during burn |
| Damage sacrifice, one or two cards | Every live native burn; no last-card-only admission |
| Active card expiry/loss | Original active source retained before grid relayout |
| Park sweep / lost-pile watcher / unavailable-art fallback | Same central native completion and loss gate |
| Consumed item / confirmation flourish | Original clipped item and actual native iterator |
| Remote event-before-model / character switch | Canonical original retained until causal completion or recovery |

Concealment policy is unchanged: local controlled cards are public to their owner; the entire
3D map is public. Scenario selection conceals remote fans, held and placed cards; remote
short-rest burn flights remain concealed. Action-phase sacrifices remain face-up.

## Protocol and resource limits

Additive record 75 carries durable native burn completion and explicit owner progress.
Progress has no flight origin and cannot release a flight. Original roster references carry
no card names. Additive record 76 belongs to the
independent original-item-appearance stream (messages 17/18); it is not added to every presence
packet. Existing protocol version, messages and field layouts remain intact. All VR peers should
use build 513 for complete synchronization.

The transport still sends at most one bounded presentation event per 50 ms and never catches up
with a burst. Weighted scheduling gives ability and item appearance three turns each, presence and
boards two each, and native slots three turns in a seventeen-turn cycle; empty streams cost no turn. Item frames are bounded at 60,000
bytes and share the existing 32-second cosmetic assembly lifetime. Retained data is bounded,
cleared on recovery/teardown and deduplicated while unchanged. Offline item burns retain no
unused network snapshots. Presence has at most 128 completion entries across 11 pages; its
worst-case payload is 6,865 bytes within a 7,168-byte assembly. The unchanged five-second
presence assembly lifetime is exercised under maximum concurrent stream load.

## Validation

Integration checks and final counts are recorded after the complete combined build. New
production harnesses cover local layout, remote causal sequencing, retained completion frames
and native item lifetime. Negative controls restore early layout/flight admission, lost final
frames, missing lifecycle registration and identity mistakes. Existing maximum-stream saturation
checks include the item stream at both 90 Hz and 18 Hz.

Headset acceptance remains open: both recess orders, short/long rest, one/two-card damage,
active expiry, consumed items, pause/resume and character switching on both owner and observer.
Automated checks do not establish that headset pixels are correct.
