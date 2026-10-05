# Build 624 merchant onboarding review

## Evidence and cause

The maintainer reports a first-save campaign tutorial that stops after buying
an item and closing the converted merchant window. No new run or affected build
was supplied for this report; the conclusions below are source and fixture
evidence, not a reconstruction of that player's exact session.

The native BuyItem lesson waits for merchant exit through a WorldMap toggle
listener. Its listener clears subscriptions even when that toggle no longer
matches the current lesson. Buying itself does not complete the lesson. A shop
can also be native-hidden while its borrowed VR window remains visible, so a
later VR close cannot depend on another native OnHidden callback. In current
dev, Build622's map-switch preservation additionally intercepted the genuine
home-map close. Switching map surfaces and closing the merchant must remain
separate operations.

## Change and flow review

- An explicit, thread-local close scope accompanies the actual X, close chord
  and second Merchant press. Ordinary City/World map changes still preserve
  converted service windows and cannot consume the tutorial.
- The exact pending native BuyItem promise is captured before selection and
  listener cleanup. Completion follows the original OnHidden cleanup, or the
  final release of an already-hidden borrowed shop. Mandatory/semantic close
  guards run first; a refused close cannot advance the lesson.
- Current manager, shop, serialized step, promise and map identities are
  checked again before completion. Buying, selling, temporary native hides,
  duplicate callbacks and unrelated tutorial steps cannot complete this lesson.
  Reentrancy is guarded; a failed native completion can be retried. Saved FTUE
  flags are never written directly.
- The original BuyItem introduction config is `sharedassets1.assets/2109`,
  phase 10, HelpText row `FTUE_9.3` with controller key
  `Consoles/FTUE_9.3_CONTROLLER`. Only messages originating from that actual
  serialized row receive the EN/DE instruction to close with X or press Merchant
  again. Native queues, keys and conditions remain intact. Language repaint
  revalidates the producer; unrelated messages keep their own text.
- Converted 2D and nonimmersive 3D shops use the same close contract. A retained
  immersive setting cannot skip the lesson in 2D. In immersive 3D, the two
  obsolete flat merchant navigation steps resolve through the original manager
  and promises. Other lessons remain native. Late shop initialization and
  enabling/disabling immersive residents during a delayed native hide retain
  one-shot progression rather than restarting it each frame.

The patch adds no gameplay transaction bypass and no multiplayer presentation
exception. It changes first-save lesson continuation and its actual close hint,
not the public merchant catalogue, item artwork or town transport.

## Verification and limits

The complete integrated attempt recorded **137 local scopes**, with 136 direct
passes. Its only failed scope was an unrelated mirror negative control that
assumed the next changed native packet existed after one editor frame. The
original failure is retained. The fixture now waits at most 1.5 seconds for the
actual module-10 packet, retaining all content and state assertions. That
control reaches its intended heartbeat failure; the focused positive passes
197 assertions. Its other 19 passing controls are retained, not rerun. The
initial capture log also records a bounds exception; its cause is unproven,
and no production repair or hardware outcome is claimed for it.

All 14 source gates pass. The onboarding fixture passes 120 assertions and 15
effective causal controls, including four source-bound native methods. The
actual modal close fixture passes 113 assertions and 13 controls; the native
message/Harmony fixture passes 29 assertions and six controls. Strict Release
has zero warnings/errors, five EN/DE document pairs agree, and the separately
resumed canonical wire executable passes 308,061 assertions. Native bundles and
the existing 1,594 derivatives in 66 parts pass unchanged.

All 2,806 runtime/asset inputs match the complete attempt after the fixture-only
fix. The actual Build623-to-624 compiled comparison has seven added and fourteen
changed types, with no removals. Seven changed types outside the onboarding
implementation contain only the compiler-inlined ModBuild increment; no town
protocol layout or unrelated production behavior changed. Existing config and
diagnostic surfaces are retained. Original reports, hashes, continuations and
the exact compiled comparison are in
`.planning/debug/onboarding624/validation-ledger.json`.

Hardware follow-up: create a campaign save with immersive residents disabled,
buy an item during the merchant lesson, then close once with X and once through
the Merchant button in separate fresh saves. Check immediate close without a
purchase and the same onboarding with immersive residents enabled. Confirm the
following map lesson and quest flow remain usable. Automated checks do not
establish the final headset behavior.
