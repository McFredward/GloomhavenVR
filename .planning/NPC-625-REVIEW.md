# Build625 paired NPC and map-loading review

## Supplied hardware evidence

Both supplied peers identify **Build624**, version 1.1.0, commit `b123cefc9`.
Six immutable log copies and SHA-256 inputs are retained under
`.planning/debug/npc625/hardware-inputs/` and `inputs.json`. Bounded findings,
including original line numbers, are in `hardware-findings.json`. The October 4
NPC pictures are older than this October 5 paired run; no new visual recording
was supplied for this report. They cannot establish the Build624 picture.

The host records 244.33 ms and 189.11 ms `TownServices.PublishFinal` outliers
(frames 27368 and 27433); capture accounts for nearly all of each named scope.
Repeated module-2 snapshot-limit failures occur at lines 4520, 4741, 5733, 5921,
6084 and 6249. These are actual capture costs/errors, not proof of image pixels
travelling over the network. The item-front warnings at lines 4674–4676 and
4773 explicitly reject the engine's generated `TMPro.TMP_SubMeshUI` output.
The observer rejects exact frozen native-template metadata at line 158.
The host map spinner is armed at line 1144 and released after **20.92 s** at
line 2484. This is the old run's total preparation interval; removing a fixed
half-second hold alone does not explain or promise a 21-second saving.

## Source causes and corrections

1. **Prepared cabinet input still serialized the complete original bank.** The
   publisher retains immutable native dependencies as metadata but immediately
   emits current canonical TLV103 headers. The actual transport constructs based
   property patches from cached canonical keys. Missing or changed originals are
   encoded one per send turn, rather than all in the button/capture frame. Warm
   page changes never rebuild/compress the complete bank. Receiver validation
   reuses only the exact immutable node collection with matching canonical
   headers; altered original properties or canvas state still receive validation.
   Large real property patches preserve current headers and exact target keys,
   then use individual original repairs. They cannot discard a page or display a
   partially populated bank. Final fragment completion acknowledges the actual
   original publisher, keeping later unchanged refresh and loss repair alive.
2. **A valid native item front was rejected as a custom gameplay graphic.** The
   common pooled-front neutralizer now accepts the exact engine TMP fallback
   graphic type. This includes Boots of Striding's original output. Arbitrary
   custom graphics remain rejected. Receiver-generated TMP meshes are regenerated
   through their original text components; no substitute front is introduced.
3. **Sparse offered-card/UI metadata compared overridden template defaults.**
   Every owner layout, active state, transform, text/style, canvas, mask and
   ordinary graphic property is mandatory. The basis compares only genuinely
   omitted immutable material defaults and the exact topology/property census.
   Explicit owner material changes remain authoritative and asset-validated.
   Merchant confirmation originals use this same prepared native metadata path.
   Inactive native mage/card/confirmation templates are prepared in bounded local
   steps. Cold sparse packets are retained per peer/module/session and retried
   against genuine ready originals, instead of disappearing until the ten-second
   full refresh. Newer frames, retirement and session changes prevent stale replay.
   Card, ring and all required original rows still enter as one complete picture.
4. **The purse return used only sampled hand motion.** The original native token's
   actual 0.35-second SmoothStep endpoints, age, revision and subpart offsets now
   accompany numeric motion in additive TLV106. Playback follows the same visible
   interpolated rig holder used by the normal remote hand. Its original enclosing
   canvas remains visible while returning, even when the wrist preview closes.
5. **Blessing had no common native cue.** The serialized
   `UITempleWindow.audioItemBless` is prepared and played at the shared donation
   age, using its first declared original clip/alias consistently across peers.
   Only duplicate playback of that exact sound during the immersive native temple
   transaction is scoped out, with prefix/finalizer restoration on exceptions.
   The transaction and promise still run normally. Flat feedback, unrelated
   rewards, player sound settings and native volume remain intact.
6. **Resident gaze consumed raw 15 Hz head samples.** The existing valid/fresh
   avatar guard remains, but town gaze reads the displayed interpolated head
   holder when available. It uses the raw sample only before that holder is active.
   Existing body/face clocks, locks and other raw-head consumers are unchanged.
7. **Local map loading waited for network presentation readiness.** Local cabinet,
   row, sprite and observer-original preparation now determines its readiness.
   Election, remote bank acknowledgement and loss repair are separate network
   concerns. The arbitrary minimum spinner hold is removed. Actual asset loading,
   unlocked residents and canonical item preparation still keep the spinner while
   useful work remains; existing failure bounds and gameplay loading flags remain.
8. **Final review found reconnect lifecycle gaps.** Removing a peer now removes
   its deferred cold metadata and FIFO positions, preserving other peers' order.
   Reconnecting with the same peer ID cannot revive the obsolete session.
   A network reset advances a separate preparation revision: existing frozen
   originals warm again even if their real asset generation has not changed.
   This preserves actual asset identities instead of clearing useful assets.

## 1:1 review

| Surface | Actual common path and boundary |
| --- | --- |
| Cabinet category, page, buttons and crank | Existing reliable common-author input, original mechanism and analytic intermediate clock; immutable original content is retained across input |
| Cabinet original cards, price, quantity and sold-out state | Canonical exact original dependency bank and genuine owner property patches; no partial-ready shelf or image transfer |
| Visitor item fan and held item, including Boots of Striding | Existing avatar metadata, canonical resident item front and approved rig interpolation; no new town-specific fan |
| Merchant offered card and buy/sell/cancel confirmation | Original owner modules, prepared native confirmation template, existing numeric animation and exclusive interaction claim |
| Mage offered card, original ring, selectable regions and return | Exact native original plus every owner state property; complete-picture admission and retained existing return/pose channels |
| Mage options, confirmation, hover, scroll and clipping | Entire original node/property census, native masks/styles and intermediate owner animation; viewer controllers stay inert |
| Purse wrist preview, held purse, label, return and donation | One labelled native identity, displayed rig attachment, exact owner return clock and original visibility lifetime |
| Blessing, voice, body pose and gaze | Existing shared commit/effect/voice clocks plus exact native blessing cue and interpolated attention target |
| Map loading | Local preparation readiness; never a gameplay lease or network admission substitute |

The only town guide exceptions are the maintainer's **October 3 card pre-drop**
and **October 4 purse pre-drop** rulings: each visitor sees only their own guide.
These exceptions do not cover actual offered cards, bags, UI, poses, flights or
audio. Scenario guides keep their existing synchronization. Public 3D map fronts
remain public. No new privacy or performance exception is introduced.

## Validation boundaries

The public cabinet probe now binds **actual production capture metadata → actual
lane queue → bounded fragments/bundle assembly → receiver admission**. It does
not turn a sparse clock into an invented complete bank to make a test pass.
Cold header clocks reject until exact dependencies arrive; warm ordinary stock
packets are withheld while native intermediate page motion stays complete.
Genuine price changes, unassigned visitor input, later heartbeat completion,
and delayed old clocks with newer owner manifests are covered. Independent
causal controls remove each requirement and must fail for its own reason.

The integrated wire probes cover zero full-original encodes on warm input,
at most one encode per send turn, oversized genuine property repair, actual
publisher completion and literal additive TLV106 bytes. Native item-front proof
checks the real inactive pooled clone before generated text mesh pruning; this
distinguishes harmless engine graphics from unsupported custom graphics.

The merchant timing fixture measures **8.006 ms → 0.291 ms** (27.5×) for its
prepared operation, with a 563-byte canonical clock. This is a fixture CPU scope,
not measured headset FPS. The allocation counter was unavailable and no
allocation claim is made. Robust mage metadata is 6,614 bytes versus 7,172 bytes
for its complete uncompressed fixture frame (7.8% smaller); every owner property
is retained. The previously smaller unsafe packet is not a valid target.

Genuine absent assets, lost packets or genuinely incompatible omitted material
defaults still require bounded preparation/full repair. The ten-second periodic
full fallback remains; no internet delivery deadline or every-peer instantaneous
first-cold picture can be established by an automated local probe. Native audio
audibility, headset images and actual post-change loading time remain unverified.

Final gate counts, actual compiled comparison and unchanged-production hashes
are recorded after integration in `.planning/debug/npc625/validation-ledger.json`.
Original failed attempts are retained; successful unrelated suites are not
repeated to repair a bounded fixture failure.

The complete attempt recorded all **137 scopes**, with **134 direct passes**.
Two old fixture boundaries lacked the newly used real hand-root/TMP engine
members; focused continuations passed all catalog and merchant-handoff cases
after adding only those boundary members. The wrist-board scope rejected the
runtime hash changing during its execution when the three-file reconnect fix
was integrated; that single failed scope is resumed on the final frozen tree.
The original report remains a failure report. Final coverage is composed from
its unchanged successful scopes and explicit affected continuations, never
represented as a second successful full invocation.

Of **3,136** production/asset inputs frozen before the complete attempt, only the
three reviewed reconnect lifecycle files changed. Their actual cold disconnect,
twelve rapid join/leave cycles, lower-sequence reconnect and existing-bank reset
preparation proof passes **30 assertions and two causal controls**. The native
mirror, partial-order and instrument-write source checks are resumed for that
precise change, plus strict build and the actual compiled comparison. No assets,
wire grammar or visible original geometry changed in the continuation.

Both peers must use **Build625** for the next hardware check: alternate cabinet
pages/categories/scroll after join; pick up Boots of Striding; offer and swap
merchant/mage cards while observing every confirmation and option row; take and
release the purse with the wrist preview closed; donate and compare blessing
sound/pose/effects; compare gaze motion and map-loading spinner completion.
