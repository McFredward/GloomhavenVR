# Immersive NPC multiplayer review — Build 602

## Evidence and scope

The supplied PCVR host and peer logs both identify ModBuild 600. Seven screenshots
show missing cabinet cards/holders, beige item fronts, an obscured purse inscription,
and an offered enchantment card/ring without its complete presentation. Build 601
was the scenery checkpoint. This review uses the actual original-widget publisher,
receiver and native asset bindings, not only reconstructed state assertions.

The cabinet's first mechanical sound added a GameObject after its hierarchy was
frozen. Its exact 34-to-35-node mismatch reproduces the recorded binding 8D884334;
the emitter is now allocated before capture. Recorded original font/effect dependency
failures were checked read-only against the complete supplied GH_Data. That evidence
does not authorize arbitrary same-name asset fallbacks.

The resident codec rejected a real 0.95-second transition because its bound was
0.65 seconds. Hidden residents also supplied a zero quaternion/scale, invalidating
the complete resident packet. Both defects explain why local poses could work while
shared performance was rejected. They are source-proven causes; individual still-image
symptoms are not uniquely attributable from a screenshot.

## Review matrix and corrections

| Surface/lifecycle | Reviewed and corrected invariant |
|---|---|
| Residents | One elected body/face author publishes attention, role, transition start, work clock and epoch. Followers do not choose local temple poses or restart breathing/work. Hidden residents have valid geometry. Body/activity and face snapshots remain atomic. |
| Lighting | Native ambient L2 coefficients and the eligible main directional key accompany shared practical/fill state. Owned actor/stand shaders and the translucent purse guide use it without replacing scenery lighting. Mirrored meshes carry original material uniforms; skinned actors use owned property blocks. Capture still rejects undeclared blocks. |
| Speech/contact sounds | The author chooses the exact cue/generation; coin/spell contacts also carry age. Followers seek into the performance rather than choose local random sounds or replay on receipt. Delayed clip readiness retries only the current sound. |
| Temple availability | Availability remains per visitor; the shared bowl stays open if any legitimate visitor can donate. Proximity is not a claim. Parking a purse/card reserves only its specific NPC. |
| Donation | Original commit age accompanies the revision in TLV93. First receipt and late join use it for gesture, blessing particles and audio instead of starting a new blessing. |
| Cabinet contents | Category, page, hover, direction, page count and intermediate turn have one author. TLV94 carries every cold-page placement, including a known-empty layout. Different pool histories cannot reorder a future page on author handover. |
| Cabinet input | Buttons remain available before character assignment. Missing observer artwork cannot disable local categories indefinitely. Persistent native ShopService rebinds on actual MapParty replacement, without opening a shop window or fabricating rows. |
| Cabinet timing | Playback samples current owner age and skips obsolete queued turns. Returning held cards cannot reveal warmed off-page samples on the current page. |
| Original item fronts | Frozen originals bind native font/material/texture dependencies with deterministic source identities. Body-part boundaries do not overwrite shared prefab identities. Original parts, prices, stock details and material animation remain intact. The shelf-only sold-out band is a separate original module, omitted on held cards. |
| Held cabinet sample | An explicitly typed third cosmetic stock lane, TLV95, retains original sample/body/row parents across categories, private NPC focus, session retirement and public-author changes. It has no transaction claim. |
| Shelf duplication | Only a live typed held-stock mount vacates its exact shelf sample and pick collider. An owned fan item with the same ID cannot hide stock. Return removes only its own mask through existing page visibility. |
| Held item/purse motion | Playback covers the actual owner sample interval instead of arriving in 100 ms then stopping until the next 200 ms packet. Native decision facing uses the same finite interval. |
| Enchantress presentation | Offered card, full ring/effects, selectable regions and choice/hover widgets preserve native geometry/dependencies. Observer clones run no gameplay callbacks. Offering does not depend on another NPC's claim. |
| Purse depth | Native purse surfaces/canvases join existing observer-relative ordering. Remote apply reasserts the owned band. Secondary visitor inscriptions remain with that visitor's live purse; no secondary furniture is cloned. |
| Reconnect/late join | Retained local bindings keep parent maps. Private/public/stock manifests, baselines, main-thread queues and fragment assemblers have separate namespaces. Counter rollover cannot change lanes; legacy wire prefixes stay byte-identical. |
| Continuation/authority | Purchase/sale/donation/enhancement retain native authority and per-NPC owner validation. Cosmetic publishing/audio and observer copies cannot execute a transaction, select a character or continue a native dialog. |

An additional gap was found in stock pickup speech: it still required the private
merchant fan. Overlapping gaze volumes with private mage/temple focus are legitimate.
Inspection reactions now require the actual stock source and merchant attention
volume independently of that focus. Only offer/unaffordable/sold-out cosmetics use
the stock lane; transaction reactions retain private-session validation.

## Exceptions and limits

No new 1:1 exception is introduced. The existing map ruling keeps all 3D-map cards
public. Scenario remote-selection concealment and action faces are unchanged. Town
cloth remains static by the explicit Build-582 retirement request. Player audio
mute/volume controls remain available while cue choice and timing are shared.

Source/runtime proofs reject deliberately planted defects. They do not establish
final Windows headset pixels or perceptually simultaneous real-network playback.
Hardware acceptance must cover one-to-four visitors, overlapping stands, first
category press after join, all transactions/effects, and held cards/purses during
page/NPC changes. No headset result is claimed from a green suite.

The Windows town bundle was rebuilt with Unity 2021.3.5 and copied to
`prebuilt/ghvr-town.bundle`. Install it with the DLL: DLL-only installation keeps
the old actor shaders.

## Final integrated validation — 2026-10-02

The corrected final integration tree passes:

- **14/14 source gates and 92/92 local suites**; the local group took 588.7 s
  with eight jobs. Evidence: `.planning/debug/test-runs/20261002-074058-f79cbcea/`
  and `.planning/debug/test-runs/20261002-074119-a6c1d0eb/`. Complete coverage and
  every recorded log checksum were independently checked.
- **286,751 wire/golden assertions**. Three owned NPC shader names now have
  their real town-bank paths registered; packet prefixes and additive records pass.
- Normal strict **Release build: zero warnings and errors**.
- All **three bundle-format checks**, five bilingual document pairs and the source
  surface comparison. No config key, Harmony patch or log marker was removed.
- Compiled comparison against `98fba1a8d`: **46 changed, seven added, zero removed**
  types. Additional handshake/log types change only through the build constant;
  unchanged presentation queues retain their previous optional constructor values.

The final runtime proofs include activity with **181,444 assertions/41 negative
controls**, face with **2,090/20**, station geometry/lifecycle with **1,886/16**,
original shelf band with **147/3**, and the complete original public-catalog
fixtures. The portable phase/network proof separately passes **180,194 assertions**.
No warning was suppressed and no compilation failure counted as a passing control.

An earlier complete invocation ran all 92 suites but failed five obsolete fixture
bindings. Those fixtures were corrected to the actual station/audio/environment
signatures, consumed face types, original owned shader, public lane selection and
shelf-band nullability. Access restrictions after interruption temporarily blocked
final Unity execution; restored access allowed all affected runtime checks and the
complete final gate to pass. The earlier failed evidence remains distinct from the
successful final run. No headset picture/audio acceptance is inferred from these
checks. The town bundle must be installed together with the DLL.
