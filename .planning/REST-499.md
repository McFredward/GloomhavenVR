# Build 499: remote long-rest fallback flash

## Finding and fix

During another player's long-rest burn, the game locks its native CardsHandUI. The VR
mode changes to ModalUI, and the empty-modal catch-all incorrectly opens the full desktop
screen. Its opaque background can cover the remote board without deactivating that board.
The existing local BurnActive guard cannot cover a foreign hand: inert remote card copies
correctly do not run local gameplay/animation controllers or register BeginBurn.

Build 499 checks UIManager's actual lock-owner set before allowing this empty fallback.
It suppresses the screen only when the Cards module is active and every current lock belongs
to a live CardsHandUI with AnimatingLostCards set. This covers the first native yield before
the burn effect and the final native layout interval after the effect. Removing the lock ends
the suppression even if cancellation leaves the animation flag set. Multiple foreign/local
hands work without a timer, player-ownership check, focus dependency or network event.

Explicit screen requests, manual rescue and programmatic rescue keep their existing priority.
Unknown/mixed lock owners fall through to the existing policy. The prior local BurnActive
protection is retained. No burn materials, native effects, card faces, flights, board visibility,
fade rules, wire records or authoritative actions change. The small lock scan runs only in
the otherwise unclaimed ModalUI fallback and allocates no collection per frame.

## Evidence and history

- [REST-499-EVIDENCE.md](REST-499-EVIDENCE.md): build banners, exact current 498 event sequence
  and limits; remote/ contains historical 491 logs, not the other side of this session.
- [REST-499-BOARD.md](REST-499-BOARD.md): board remains active, no fade edge; the unwanted
  opaque fallback captures the scene and costs roughly 11–12 ms in the logged spike frames.
- [REST-499-EFFECTS.md](REST-499-EFFECTS.md): native card-loss lock lifetime and local-only burn
  registration; card smoke was explicitly suppressed in this capture.

The older 491 incident already contains the same foreign long-rest/FlatScreen signature.
This predates the 493 multiplayer performance changes and the 498 release-only change.
The first introducing commit is not established. The logs prove unwanted screen activation;
without a captured headset frame they cannot exclude a second simultaneous rendering defect.

## Validation

Independent source review confirms native lock ordering, cancellation and real-dialog priority.
The source-linked regression harness exercises the production guard and verbatim extracted
FlatScreen.WantVisible with controlled native objects. All required local checks passed:

- All 17 refactor-guard checkers; 253,579 wire assertions, unchanged.
- Production capture 18,206; native playback 466; board refresh 1,216 assertions.
- New card-loss modal harness: 1,058 assertions, including a four-hand ownership matrix and
  screen/manual/rescue/confirmation priorities. Five runtime negative controls reject mixed
  lock acceptance, empty lock acceptance, idle hands, missing integration and lost screen
  priority. All 12 existing runtime negative controls also pass.
- The production ownership loop allocates zero managed bytes over 20,000 warmed reads in
  the stub harness. This is not a measurement of Unity's GetComponent implementation.
- Strict Release: zero warnings and errors. Bilingual documentation and git diff checks pass.
- Compiled comparison against 317fc045: 14 changed types and one added helper. Exact normalized
  comparison proves 13 changed types contain only version/build constant substitutions;
  FlatScreen contains the reviewed guard call and edge diagnostic. No other runtime drift.
- Config keys 625 and patch surface 152 are unchanged (109 classes / 167 methods).
  Log tokens 4,716 → 4,717: one CARD LOSS MODAL diagnostic, no removals. Bundle remains
  74,943,763 bytes, Unity 2021.3.5f1.

The guard's exit 1 reports those intentional compiled differences after successful checkers.
These checks establish source behavior, not headset rendering.

## Hardware verification

Install build 499 on both VR clients and repeat the other player's long rest. Observe from
the side that previously flashed, including a different focused character. The burn must keep
its original appearance and completion-to-flight timing with the whole board visible. Repeat
local long rest, short rest and damage sacrifice, then open a real dialog and the manual screen.

The new `CARD LOSS MODAL` line identifies empty fallback suppression. During a remote long-rest
animation, there should be no accompanying `FlatScreen shown` unless an actual screen was
requested. Preserve both fresh logs if any flash remains, ideally with an incident timestamp or
video. The existing remote board visibility and burn/flight diagnostics remain available.

## Version and publication

The maintainer requested this fix within 1.0.0, so the development version is restored from the
pipeline's automatic 1.0.1 bump to 1.0.0 and the compatibility build advances 498 → 499. This is
a DLL-only update relative to the unchanged build-483 bundle. Main and the already published
v1.0.0 tag/assets are not rewritten by this investigation. A subsequent final release must
come from main and account explicitly for that existing release. Visibility remains the
maintainer's responsibility.
