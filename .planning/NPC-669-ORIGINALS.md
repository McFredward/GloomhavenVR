# Build669 original town-template initialization recovery

## Evidence and scope

This lane starts at dev `ca4f8ff7979f898cc7a209328f326be58170cc64`
(Build668). The maintainer reports an empty immersive merchant for VR visitors
when the native multiplayer host is unmodified. The frozen VR log banner is668;
the supplied host Player.log has no GloomhavenVR banner. Host-only immersive
admission and presentation authority are repaired in the independent authority
lane. The repair here removes a separate original-bank failure that could keep
that admitted merchant empty.

VR LogOutput.log records an empty-message `Original widget publisher unavailable:
prepare:` at line6950, then exact immutable banner Icon texture conflicts at
lines7192 and7473. The empty message does not identify the first exception or its
origin; it is not evidence for a particular native factory failure. The later
key is `native-town|template|banner/SWNvbg==:0|image|texture`.

Native `UIGuildmasterBanner.ShowMode` legitimately assigns `shieldIcon.sprite`
from `UIInfoTools.GetGuildmasterModeSprite(mode)`. NativeTemplates.Initialize
registered original assets before its complete freeze/partition pass. If that
pass failed, the next attempt cleared entries and the inactive bank while
leaving the immutable registry aliases. A changed native mode icon then hit the
old slot key and failed again, before Ready could become true. This failure
class is source-proven and reproduced with exact production lifecycle methods;
the supplied logs alone do not establish that every earlier prepare failure
occurred inside this initialization pass.

## Repair and retained contracts

Move the existing factory body, without content or callback changes, into
InitializeBank. Initialize owns its lifecycle: initialization failure retires
the complete town presentation lifetime through existing TownServiceSync.Shutdown:
partial bank, public catalog, both publisher source dictionaries and mirror,
template and asset registry. It rethrows the original error. The existing caller
supplies bounded two-second retry backoff. A replacement HUD calls that same
centralized shutdown before registering the replacement originals. This is
teardown and reconstruction, never acceptance of different assets under one
live immutable slot.

The ready/same-HUD fast path remains intact. Routine Prepare calls preserve
original object identity, bank copies and asset generation. An externally reset
registry rebinds the existing frozen originals. RegisterOriginal's conflicting
asset refusal and unknown same-name texture refusal remain unchanged. Native
factory/controller calls, localization, card geometry and content, callbacks,
wire format, assets, configuration and input are unchanged.

Normal map retirement already calls TownServicePopulation.Reset ->
TownServiceSync.Shutdown. That resets public catalog and publisher source caches,
then mirror and native bank, before a new native map HUD is acquired. The rare
same-map singleton replacement now calls the same central shutdown itself; old
public catalogs and source dictionaries cannot survive that boundary. Shutdown
does not call Initialize: catalog disposal restores the original inventory
parent and disposes presentation entries; mirror and bank only retire.

## Initial checkpoint validation

The existing asset-identity runtime now includes actual production Initialize,
Ready and Shutdown methods, plus the exact central TownServiceSync.Shutdown
body. Native HUD availability, unrelated factory work, native catalog disposal,
handoff/native-asset availability and bounded enhancement warm-up are explicit
ports. Bank objects, strict original registry, original registration and inert mirror Template lifecycle run in Unity
2021.3.5f1 unchanged. The factory failure is deliberately injected after an
original slot is registered, with another case after ready is set.

- 179 assertions: cold HUD, three failures with changing native icons, original
  exception retention, successful exact-icon retry, twelve unchanged routine
  calls, surviving-bank rebind after external registry reset, HUD replacement,
  real inert template retirement, both actual publisher Sources dictionaries
  cleared through their production ResetCore, public catalog reset/reacquisition
  boundary and strict live-slot collision refusal; all pre-existing asset identity, backdrop and native wrapper cases retained.
- All 12 compiled negative controls pass: original five plus missing failure
  registry retirement, missing partial-bank retirement, retained old-HUD
  registry, retained publisher source dictionaries, skipped public catalog reset,
  lost ready fast path and overwritten immutable original slot.
- Existing preparation658 runtime (7 assertions) and both original controls pass after its
  initial factory-tail extractor is rebound to InitializeBank. The original
  preparation path, assertions and thresholds are retained.
- Worker strict Debug compilation passes with zero errors and warnings; all 16
  source checks pass.

Register `town-native-bank-lifecycle` as a local focused suite using the existing
asset-identity runner, so its original and new controls remain in the standard
local inventory. This is one additional suite; it is not a fresh complete-gate
claim. Worker source checks and exact run receipts are retained privately under
`.planning/debug/npc669-originals/`. The initial negative-control attempt used a
constant-false branch that the strict compiler correctly refused as unreachable;
its binding was corrected to an executable lost-ready condition. The central
shutdown binding also initially handled its compact one-line body and
imports its unchanged registry namespace. The full
asset-identity suite passed with every control. No production assertion or
threshold was relaxed.

## Integration-review lifetime corrections

The first checkpoint's centralized cleanup exposed three genuine lifetime
edges during review. These are corrected in source checkpoint `d03472fb5`:

- Recovery and successful HUD replacement now call `Shutdown(resetDiagnostics:
  false)`. They retire the same complete bank, public catalog, publisher sources,
  mirror modules and immutable assets, but retain the existing publisher and
  mirror failure windows. Each publisher's duplicate suppression, eight-report
  cap and single cap marker remain bounded across two-second preparation retries.
  Ordinary map/module teardown defaults to true and resets Private, Public,
  Stock and mirror diagnostics. Stock's ordinary diagnostic reset was previously
  omitted; it now has the same lifetime as the other streams.
- Private publisher reset and mirror ResetNetwork/Shutdown select PrivateLane
  explicitly. Cleanup invoked within a public capture scope retires the actual
  private session, modules and transaction offer, then restores the enclosing
  scope. Public and stock cleanup retain their explicit nested scopes. No native
  gameplay controller or callback changes.
- Native HUD replacement can complete successfully while retiring the frame
  and public catalog held by the publisher's current call. TickCore, TickCatalog
  and TickStockCore now stop after Prepare when their shared-frame lifetime has
  been retired. The next regular tick reacquires fresh originals and continues;
  there is no new generation, readiness delay or retry timer.

The source-bound runner compiles the actual Initialize/Shutdown methods, all
three actual publisher continuation guards, the actual scoped Reset and mirror
cleanup, and the actual publisher/mirror Report/ReportReset bodies. Diagnostics
have separate fixture field/method names solely to coexist with the original
throw-control reporting port; their logic is unchanged. Native HUD/factory
availability, catalog disposal/reacquisition and the grant SetOffer port remain
explicit seams. The grant port observes the actual request/cancellation calls;
the independent authority lane proves the real grant protocol and queued state.

Final focused evidence for `d03472fb5`:

- Asset-identity: 222 assertions and all 21 compiled negative controls, including
  the original 12 controls. Nine added controls cover retry diagnostic resets,
  ordinary diagnostic and stock-only reset omissions, incorrectly scoped
  private reset/mirror shutdown and all three retired-publisher continuations.
  Production verifies duplicate suppression through 13 same-error preparation
  failures, the cap through 12 additional distinct failures, ordinary teardown,
  nested public-scope shutdown/network reset, cancelled private offers, retained
  network-reset assets and fresh next-tick continuation for each publisher.
- Preparation658: all 7 original assertions and both original controls pass;
  only the necessary availability-port extraction binding changes.
- Strict Debug compilation: zero errors and warnings. Source checks: 16/16 pass.

Receipts are under `.planning/debug/npc669-originals/` at
`followup-final-identity/run-mja_65gn`,
`followup-final-preparation658/run-k5xs5171` and
`followup-final-source/results.json`. These are focused checks, not a new complete
local-gate claim. The primary agent runs the integrated gate.

The integrated default mirror suite exposed a historical negative-control
ordering dependency. The new bank probes initially ran before the existing
early-Awake probe, so its executable mutation failed earlier at a different
assertion. Fixture checkpoint `6cb5742d2` keeps asset-identity coverage in its
original location and runs the new probes after the historical cases in the
default suite. All original assertions, thresholds and controls remain intact.
The worker default suite passes 232,405 assertions and all 20 original controls;
the integrated default suite also passes all 20 controls. The 222-assertion
asset-identity production proof passes again after that fixture-only change;
its unchanged 21 controls retain their previous passing evidence.

Completed initial-checkpoint projects and controls are fully preserved in
`initial-complete-evidence.tar.gz` (verified gzip, SHA256
`a7eb759a6080a32dcc0998f48e1ea23ea0a14de416cc80697a035918abd75677`).
Their assertion, source-hash, result and manifest receipts stay available
uncompressed; only archived compiled/project caches are compacted. Hardware
inputs and all other worktrees are untouched.

Two further historical fixture bindings were corrected during integration.
The Build656 latency control still compiles its exact old mirror/receipt source;
new bank probes now have explicit dormant API ports that throw if invoked,
instead of requiring APIs absent from that historical revision. Its original
one-second complete-picture deadline and native originals remain unchanged.
The complete focused historical-control suite passes again.

The NPC666 cold merchant hover proof had used InteractionOwner to infer that
the intrinsic effect was active. Merchant browsing can already select that
visitor while its 0.12-second transaction lease is pending. Both unchanged
dev ca4f and current source reproduced the same 144-micrometre sampling error
with zero admission frames and no hover clock. The fixture now also waits for
the actual TransactionOwner; it retains its four-micrometre threshold, native
waveform and all seven controls. Unchanged ca4f passes 484138 assertions after
this binding repair; current worker coverage passes 484134 assertions/all seven
controls, and Build665 still fails its original cold-wave assertion. Saved
timings document the native lease and render clocks. No product hover code or
tolerance changed. The primary's full focused hover suite also passes.

The native-host merchant outcome, headset pixels and paired/four-player
interactions require hardware validation of the integrated build. A green
lifecycle proof does not establish them.
