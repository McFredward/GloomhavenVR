# NPC multiplayer review — Build 614

## Evidence and scope

The paired PCVR run identifies **1.1.0 / Build 612 / a606f9530** on both
machines. Host OpenXR uses SteamVR; the other player uses VirtualDesktopXR.
Both Player.log files and all twelve supplied JPGs were inspected. Frozen inputs,
hashes, screenshot contact sheets and validation receipts are in
`.planning/debug/npc614-review/`. The companion LogOutput files do not contain
all Debug records present in Player.log.

The peer's numeric lane reports an oldest pending update of **6.424 seconds**.
Both endpoints report failed native template or artwork resolution, including
card.322 template aliases, atlas textures whose native wrapper/format differs
between clients, native frame masks and particle materials. Screenshots separately
show a large grey merchant card backing ahead of its smaller readable original
front, an empty enhancement UI, missing card artwork and an incorrectly oriented
ring. These failures are not card concealment: the entire 3D map is public.

## Implementation and 1:1 review

| Surface or event | Owner and observer contract | Change or verified path |
| --- | --- | --- |
| Public merchant categories and pages | Any visitor, including one without an assigned hero, may operate the same cabinet. Button depression and cassette withdrawal/return share one clock. | Adopt validated layout/clock before artwork readiness. Promote authorship only after an accepted change. Retain the last complete original picture while next dependencies assemble. The integration fixture executes actual category input, drawer state, public publisher, codec and original clone playback. |
| Original card artwork | Public map item/ability fronts, identical native appearance, no grey fallback substituted for missing assets. | Register every verified native-template alias even when it identifies an already known original. Resolve UIInfoTools serialized sprite provenance instead of machine-specific atlas names/texture formats. Register original model-dependent material/texture dependencies. Descriptors and state are sent, **not bitmap images**. |
| Item wrist fans and held stock | Native item widgets and physical bodies retain owner geometry and follow the same smoothed avatar hand used by scenario items. | Existing ItemCardUI/CItem provenance remains the source. Attach lifted cabinet mount/face/body to the existing smoothed hand frame. Original hidden fronts prewarm; visibility and numeric changes are independent of immutable artwork. |
| Cold original dependency delivery | Newly visible fan, purse and confirmation content gets a finite turn without starving original board/presence streams. | Cold originals borrow at most six later town turns, at most one of three page turns, and repay them through the original rotation. Continuous numeric deltas cannot keep borrowing. Both original-stream and background-cabinet shares remain bounded. Event cap remains 864 bytes and the existing 50 ms scheduler clock. No separate unbounded sender or catch-up burst. |
| Dense motion, hover, scrolling and facing | Intermediate native state remains lossless and current; rotating confirmations do not await fragmented artwork. | Additive TLV98 packs unchanged numeric records in the original 15 Hz, 864-byte lane. Dirty slots not actually included retain their pending state. Existing native interpolation remains responsible for visible movement. |
| Merchant offered card and backing | One native card geometry, identical size/orientation, no doubled local transform. | Live root extents/pivots/anchors now survive frozen-template and numeric playback. Only layout fields accompany root motion; the header owns its world pose. Descendant sizeDelta retains its existing native contract. Detached-root normalization remains a defensive invariant, not a proven explanation of the hardware slab. Original assets and native rounded backing are independently checked; hardware acceptance remains open. |
| Mage offered card, native regions, hover and menu | Actual owner card/native effect widgets, selected region, native hover/scroll and readable options remain public. | Correct original sprite/template identity and fast root/canvas playback. The existing source publisher includes full card/effect partitions, original region widgets, folio and confirmations; clones retain presentation without gameplay callbacks. |
| Mage return flight | Released actual card/body remain visible through their native return, even after another visitor claims the NPC. | Existing independent returning face/body modules use the same numeric lane; the returning original's identity survives recycling of the selected-card widget. |
| Mage inert Buy label | No misleading non-interactive caption below the options. | Omit native buy/sell mode tabs when native selling is unavailable. Functional mode controls remain when the native game actually supports them. |
| Merchant/mage occupied focus | Only an actual offered card locks the resident. Until removal, gaze and speech respond exclusively to that owner. | Use TransactionOwner, not a native browsing lease. Independent visitors may approach different residents concurrently. No lock exists for the priestess. |
| Shared offered-hand pose | Every observer sees the same resident animation, independently of each visitor's private destination guide. | Numeric mage readiness uses its own motion lifetime, never a private shop session. Additive TLV99 reports merchant readiness without guide artwork. Original shared resident/face/activity clocks still drive poses. |
| Priestess blessing | Accepted native donation alone starts one shared blessing; look at the donor, retain shared effect/foley/voice clocks. | Select the accepted donor before sampling that first pose. Fast committed-donation evidence survives closing a native visit; repeated clocks do not restart the generation. Existing seeded paused native particle systems sample the shared age. |
| Purse wrist/held presentation | The actual purse, wrist appearance, movement and native eligibility remain public; drop feedback is local input feedback. | Original purse asset dependencies and urgent first delivery; existing smoothed avatar hand attachment. Real offer feedback provides visual proximity/release feedback and controller pulses. |
| Speech selection and timing | One resident author selects the exact take or silence; observers use the shared cue/generation/age. | Reactions from a peer are not rejected because another visitor merely opened a native service. Actual merchant/mage transaction ownership still gates unrelated reactions. Optional utterances may remain silent; committed feedback remains dependable. |
| Lifecycle and mixed visitors | Cancel, return, replacement, late join, reopen and author handover preserve native operability without duplicate stands. | Private service, public cabinet and visitor stock retain independent module/session lanes. Stale/reordered state cannot replace newer membership; callbacks run only on originals. |

## Explicit current exception

On 2026-10-03 the maintainer requested **town card pre-drop guides to be local to each
visitor**. They no longer publish remote guide geometry or elect a guide owner.
Their haptic/input eligibility remains local. Shared NPC offered-hand poses,
actually offered cards, native regions/options, confirmations, item fans, actual
purse props and ghost/drop-feedback presentation, return flights and audio remain public. Older queued map guide
modules are also retired. Scenario board guides and remote scenario concealment
rules are unchanged. This supersedes the earlier shared mage-guide treatment.

## Validation boundaries

The controlled four-peer transport probe uses real captured Unity UI snapshots
(13 native binding nodes per front, 34,512 raw bytes for eight fronts) alongside
320 queued cabinet rows and six original presentation streams. Complete cold fan
transport changes from **2.244 s to 0.561 s** after the new finite urgent turn;
the older queue/scheduler control takes **6.936 s** and fails the subsecond bound.
Original streams still get their turn (0.612 s in this fixture). This is a
controlled native binding/transport test, not a measured live headset fan or a
claim about a full live game's CItem widget complexity.

Lossless packed numeric contention reaches 160 continuously dirty original
bindings with a 0.133 s maximum update gap in the representative test. No packet
exceeds 864 bytes. Independently specified TLV97/99 golden bytes and a fixed
Python-struct/zlib/CRC TLV98 receive vector guard grammar changes independently
of the production writer. Reordered, corrupted, truncated and wrong-affinity
packets fail atomically. Per-property heartbeat recovery remains bounded.

The separate-process native peer fixture checks exact original texels and exact
observer-native versus playback pixels, opposite lazy-template borrow orders,
native frame-mask lookup, rounded production CardMesh geometry and live root
resizes without artwork resend. It passes 45,862 assertions plus seven effective
causal controls. Original independently packed atlases already differ at 813
bilinear edge pixels before playback; the observer's own original and replay
match exactly. The editor shader boundary and small rasterization differences
in geometry pictures are documented in scripts/town-native-peer-runtime/README.md.
This does not isolate the hardware grey slab's exact original cause.

Final integrated gate receipts and assertion counts are recorded in STATE.md.
Automated Unity pictures prove the controlled hierarchy/material/geometry paths;
they do not establish that the next headset picture is correct. Both players
must install the same Build614. Check cabinet changes in both directions,
first fan/purse appearance, held/offered card faces and smoothness, enhancement
regions/hover/scroll/return, donor-facing blessing, transaction-exclusive gaze and
speech, and local-only personal guides in the next hardware run.
