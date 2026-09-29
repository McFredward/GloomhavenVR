# Town services 551 — palm decisions, native enchantment and restrained motion

## Evidence and scope

The September 24 local hardware logs identify ModBuild 550. All eight new images
in `debug/npc_probleme/VirtualDesktop.Android-20260924-*` were inspected. They show
the priestess bracing, awkward mage wrists, no cabinet page indication, pale flame
cores and overlapping enchantment samples. Remote logs are older; they do not
establish current observer behavior. No new paid generation was used.

A shutdown exception in `TownServiceRitual.Inscription.Dispose` dereferenced a
Unity root that had already been destroyed. Cleanup now tolerates that lifecycle.
Repeated native-art ambiguity warnings remain diagnostic evidence, not a newly
proven cause of the reported interaction or flame defects.

## Changes

- Reclaim the actual owned/stock/ability card while its own pending decision is open.
  Keep unrelated modal and ownership restrictions. Restore the full grip collider,
  clear obsolete fan-hand filtering, and cancel only the exact transaction callback.
- Original confirm/cancel, text and enhancement identity are freestanding beneath
  the palm, with native hover and callbacks. They have no handle or MR backing.
  Teardown restores native descendants and does not skip the game's continuation.
- Merchant offer intent requires nearby eligible held stock/owned items or an actual
  pending offered card. The same private original-zone membership informs the shared
  resident author, including a parked card whose overlay alpha is zero.
- The enchantress's actual offered card is enlarged with bottom-edge palm clearance.
  Replace overlapping substitute rune cards with the complete original scroll
  inventory and original selected-card ability hotspots, information, points and
  buy/remove controls attached to the stand. No options are omitted to fit a grid.
- Longer varied activity phrases, quieter coordinated body movement, palm-up casting
  and joined lowered prayer replace rapid repetition and table bracing. Details:
  [TOWN-MOTION-551.md](TOWN-MOTION-551.md).
- The original cabinet holders roll vertically and fold backward, with brief rear
  clearance from the fascia. A numeric current/total page indicator stays visible.
  Preserve category-change shutters. TLV87 adds direction/count without changing
  existing 78/85/86 bytes. Details: [TOWN-CABINET-551.md](TOWN-CABINET-551.md).
- Actual asset review caught imported shelf transforms (100x scale / -90-degree
  rotation) being overwritten by runtime motion. Metre-space articulation pivots now
  retain the imported geometry unchanged beneath them; cards attach to those pivots.
- The observer review also restored inspection-only merchant confirmation publication
  and the native enchantment price tooltip, both previously excluded by window gates.
- Use original freestanding RockTemple candles instead of unsupported wall brackets.
  Load only the selected native candle's material bindings. An opaque emissive flame
  core restores visibility; native additive glows, atlas timing and fading remain.

## Validation

Integrated validation is complete. Targeted final evidence:

- Handoff: [TOWN-HANDOFF-551.md](TOWN-HANDOFF-551.md), including actual native
  pointer/scroll handling, callback ownership and destroyed-inscription teardown.
- Motion: 649,733 imported assertions / 24 negative controls; 4,423 sampled full-skin
  poses with zero tested arm/torso or opposite-arm intersections.
- Cabinet: 22,836 assertions / 27 controls; 1,206 actual imported holder poses without
  cabinet, stationary-side or adjacent-holder intersections, with a failing old-path
  control. Fresh report: `/tmp/town551-cabinet-normalized-clearance.json`.
- Flame: 1,026 assertions, six compiled timing controls plus batching and rendered
  transparent-core controls. Native decoration suite and eleven controls pass.
- Final source-asset review: 951,242 assertions / nine rendered controls. The initial
  fixture lacked the newly required TMP dependency; after that correction the actual
  corner-support test caught the imported-transform defect. The unchanged assertions
  then passed with the production pivot fix. This was a resumed source-render review,
  not a relabelled successful initial run or a fresh Linux-bundle review. Evidence:
  `/tmp/town551-assets-fixed/town-assets-likhscsh/evidence-normalized/`, including
  `resume-evidence.json` with exact compiled-source hashes.

All 69 required local suites ran in the integrated checkout (471.8 seconds, eight
workers): 67 passed and two failed fixture setup, not production assertions. The
catalog fixture lacked the new handoff boundary; the new enhancement-confirmation
fixture lacked its original CanvasGroup. Both complete affected suites passed after
repair, including all negative controls. The original failed full-run report is
retained unchanged. Final targeted mirror repeats cover the subsequent imported-row
fix, inspection-only confirmation, native tooltip and offer heartbeat changes.

Final source gates pass 14/14; golden wire vectors pass 286,120 assertions; strict
Release has zero warnings/errors. EN/DE documentation, bundle format and surface
census pass. Guard stages after the failed runtime aggregate were resumed without
repeating unrelated suites. The unchanged historical compiled baseline reports
122 changed types, 165 added/removed and zero order-only differences.

Logs: `/tmp/town551-full-guard.log`, `/tmp/town551-guard-resume.log`,
`/tmp/town551-strict-final.log`, `/tmp/town551-golden.log`. Raw local-suite report:
`debug/test-runs/20260924-153057-14a5ac16/results.json`. A separately started duplicate
runtime invocation was cancelled once the guard's own runner started; its cancelled
report is not counted as passing evidence.

Unchanged parked-offer state now gets its own bounded 0.75-second urgent heartbeat.
The former ordinary five-second refresh conflicted with a three-second freshness
check and could lower the remote palm despite a valid parked card. Actual production
capture and all ten relevant controls pass; sustained packet loss or congestion can
still exceed a freshness deadline, so no hard delivery-time guarantee is claimed.

These checks do not establish headset appearance or live network timing.

## Hardware checklist

Install the complete ModBuild 551 development package on every VR peer, including
its matching `ghvr-town.bundle` (96,164,115 bytes, SHA-256
`e56d6bd3c33a53f568516c7f8e37c5e8c8840849244b6c92b2f1ba6be8a61da9`).
The environment bundle remains byte-identical (`fe1a659c…1693`). The private
`debug/town551-package-verification.json` records the final archive's exact commit,
per-file hashes and CRC result after packaging.

1. Offer and reclaim both stock and owned merchant cards; repeat for an ability card
   at the enchantress. Confirm/cancel, leaving, character switching and service toggle
   must restore usable hands and native flow without duplicate transactions.
2. Confirm controls should float beneath the palm, readable and unobstructed, with
   laser/finger hover. Empty hands and walking away must let the merchant lower his arm.
3. Check readable enchantment options, scroll, ability hotspots, prices, buy/remove
   and cancel. Check the larger actual card above the palm and reclaim it again.
4. Compare cabinet page count, upward/downward roller movement, category changes,
   held cards and native decision widgets with a second VR player.
5. Observe a full quiet/work cycle, approach and departure at each resident. Review
   joined priestess hands, mage wrists/elbows and whole-body transition naturalness.
6. Check standing candles and flame cores in both custom environments and MR.

This remains the feature branch's 1.1.0 development candidate, not a release.
