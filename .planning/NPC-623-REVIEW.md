# Build 623 paired NPC review

## Hardware evidence

Both supplied peers identify **Build622**, version 1.1.0. Immutable host and
observer logs, with hashes, are retained in `.planning/debug/npc623/inputs.json`.
The NPC screenshot directory still contains the previous October 4 pictures;
there is no newly dated Build622 screenshot set to corroborate this run. The
maintainer explicitly confirms the canonical item fan and held-item rendering
now work. This path is retained, rather than replaced with town-specific artwork.

The host's `TownServices.PublishFinal` cost is 17.223 ms average / 248.64 ms maximum
in the first online window, then 36.974 ms average / 66.63 ms maximum. Subsequent
windows still contain 169–443 ms publication outliers. These are real CPU costs,
not an inference that image pixels are sent over the network. Concurrent native
UI fitting and transport work also remain visible; this change does not claim
to eliminate every cost observed in the old logs. No Build623 headset FPS result
exists yet.

## Causes and changes

1. **Publication sampled more than the transport could deliver.** Final town
   capture ran every render frame even though the existing transport samples at
   15 Hz. It now runs at that clock and at most once per rendered frame. Per-frame
   hand/pose animation, input, gameplay grants and reliable controls still run.
   Received immutable frames are decoded once rather than inflated and parsed
   again during playback. Pose-only catalogue updates reuse their original
   content identity. Canonical hashing emits the same bytes into a reusable
   buffer and hashes once, instead of hashing thousands of tiny writes.
2. **A visitor press changed public authorship and discarded prepared content.**
   Category/page/crank intents now use a small reliable presentation message and
   the common author's original drawer code. The active native host is preferred;
   if it uses 2D/flat town services, a compatible active public author remains
   available. The control executor and displayed author are the same peer.
   Requests are broadcast so the compatible author can execute them; gameplay
   grants remain native-host coordinated. Prepared banks survive ordinary input.
   The common analytic drawer clock preserves intermediate movement and manual
   crank angle. Nonces survive presentation resets; takeover seeks the previous
   accepted clock rather than rewinding the common page.
3. **Large complete shelves exceeded the unchanged 55 KiB packed envelope.** The
   supplied capture reports this exact failure. Oversized shelves repair exact
   individual originals through their existing bounded queues, followed by all
   current owner headers and content references. The receiver requires every
   original before admitting that page. Preparation is never promoted merely to
   fit a packet. Dormant loss repair progresses in short bounded slices instead
   of serializing an entire 32-card batch at once. Wire size/event budgets stay
   unchanged; late join/loss still have complete repair paths.
4. **Pickup replaced the purse's presentation identity.** The actual labelled
   physical purse now retains one original identity through wrist, held, bowl
   and return states. Pickup no longer retires the visible body before a new
   template reaches the observer. Original tooltip counter topology is prepared
   by its actual child count, without running original gameplay callbacks. The
   tooltip remains independent for each visitor.
5. **Native buy/sell refresh rebuilt ability cards over merchant items.** The
   actual committed merchant inspection now reasserts exclusive wrist ownership
   after native updates. Checking whether another station has owned ability
   cards reads the existing loadout without opening/rebuilding that fan. Buying,
   selling, cancelling and taking an item back use the confirmed item path.
6. **Mage presentation repeated native property tables and read empty frozen
   originals.** Each peer already owns the original widgets and artwork. Record105
   sends owner headers/text and actual property differences against the exact
   local original template. Frozen inactive originals are read with their graphics
   intact, without activating their controllers; otherwise they always forced
   the expensive full fallback. Ability originals are prepared from native game
   assets independently of the observer's selected character. The card, original
   ring and required option rows are admitted as one complete picture. Native
   masks, hover, scrolling, styles and original materials are preserved. A genuine
   template mismatch still uses bounded complete repair rather than substitutes.

## 1:1 review

| Owner-visible surface | Shared path and review boundary |
| --- | --- |
| Item fan and items held by a player | Existing avatar/item metadata, rig and resident original renderer; no image transfer or new fan interpolation |
| Cabinet page, categories, crank and buttons | Reliable common intents, original drawer execution, retained complete content and native numeric animation |
| Offered merchant card and decision buttons | Existing original offered modules, hand pose and native interpolation; buy/sell/cancel refresh retains item-only fan |
| Purse preview, held purse, label, donation and return | Stable original identity, existing rig-relative interpolation and actual counter topology |
| Offered mage card, ring and selectable areas | Exact original template plus owner state, complete-picture admission, original highlight geometry and interpolation |
| Mage options and confirmation, hover and scroll | Entire original node/property census, owner text/styles, original clipping and animation; observer clones remain inert |
| NPC body pose, attention, blessing, voice and effects | Existing global motion/voice/effect channels retained; public cabinet authority does not replace private interaction leases |

The only applicable map guide exception is the maintainer-approved visitor-local
card pre-drop guide and purse ghost. Actual NPC poses, items, purses, offerings,
decisions, hover, return flights, blessing and audio remain shared. No new visual
exception is introduced. Scenario concealment and scenario fan/item behavior are
unchanged. Flat/2D service windows and gameplay callbacks are not replaced by the
compact metadata path.

## Measurement and verification limits

The original 25-node native card hash benchmark produces the same content key
`0x1D754360D2F15568`: 1,000 hashes fall from 1,197.725 ms to 157.733 ms (7.59 times
faster). This measures hashing on this development machine, not headset FPS.
The native Unity fixture retains 37 original nodes, masks, styles, materials,
owner text and hover/scroll state. Its unchanged packet falls from 7,172 to
2,654 bytes, and its changed packet to 2,875 bytes (about 60–63% less). These are
uncompressed fixture packets, not a live internet latency benchmark.

The supplied native `UIQuestEnemy.ShowEnemy` destroyed-image exception and shutdown
null references are distinct from the town capture failures; they are not claimed
fixed by this publication change. Retain their original logs for a separate,
reproducible gameplay investigation rather than modifying native continuation.

The complete integrated gate recorded all 134 local scopes: 125 direct passes and
nine retained failures resolved by focused continuations. These include obsolete
fixture boundaries and expected markers, a dependency-loss probe that needed to
withhold both independently usable deliveries, and an isolated native-creation
control previously obscured by unrelated time-bounded discovery. Already passing
unrelated suites were not rerun. All 2,988 runtime/asset inputs are unchanged from
the complete attempt; subsequent changes repair tests only. All 14 source gates,
308,061 canonical golden assertions, strict Release with zero warnings/errors,
native bundle checks and retained public surfaces pass. The actual Build622→623
compiled comparison explains all 24 changed and five added entries, with no
removals. Exact original reports, continuations, hashes and boundaries are retained
in `.planning/debug/npc623/validation-ledger.json`.

Hardware follow-up requires **both peers on Build623**: enable multiplayer while
stationary; alternate category/page/crank input immediately after join; buy/sell/
cancel while watching both fans; take/return/donate the purse; offer/cancel/swap
mage cards while both peers inspect every row, hover, ring and return. Automated
original-runtime evidence cannot establish the final headset picture or FPS.
