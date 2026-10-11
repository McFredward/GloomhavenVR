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

## Validation

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
shutdown binding also explicitly handles its existing compact one-line body and
imports its unchanged registry namespace. The full
asset-identity suite passed with every control. No production assertion or
threshold was relaxed.

The native-host merchant outcome, headset pixels and paired/four-player
interactions require hardware validation of the integrated build. A green
lifecycle proof does not establish them.
