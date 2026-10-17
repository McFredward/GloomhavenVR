# Build680 — unconfirmed scenario card selection

Status: implementation, independent source review, fixture integration and scoped
local validation complete. Hosted head status is retained separately in ignored
publication receipts. This document does not certify headset behavior or a
complete local gate.

## Hardware finding and scope

The October17 local inputs identify ModBuild677 in both log banners. Seven
`VRCard.Released` failures abort in `FanOrderMemory.ForCharacter` because the
Guildmaster actor's `CharacterName` is null. Withdrawal already removed the
physical card when optional fan-order lookup threw, before native `UnselectCard`
could be queued. The native round pile therefore retained the card; the missing
wanted highlight and its restoration onto the board after actor refocus follow
from that unchanged authoritative selection.

The nullable-name trigger is demonstrated in Guildmaster. The release path is
shared by all scenario modes, so the repair and regression coverage apply to
Campaign and Guildmaster. Optional ordering uses a separate bounded native-GUID
domain for unnamed actors; missing both identities cannot prevent withdrawal.
Named-character order behavior remains unchanged.

Frozen inputs, hashes and narrow exception excerpts are retained in
`.planning/debug/card-selection680/hardware/`. The supplied remote Player.log is
older October11 vanilla evidence, without a corresponding mod build banner. It
does not establish the picture of a paired ModBuild680 session.

The initially considered hidden-native-hand early return was checked against
`HandSuppression`, which preserves the native manager's `IsShown` contract. It
was not established as a cause and neither that flag nor SRL is patched here.

## Additional general failures reproduced during review

- Replacing an occupied recess queues native unselect and select on separate
  frames. An intervening rebuild could evict the already dropped newcomer to the
  fan before its native selection acknowledgement.
- A queued completion could read another actor's current left recess and change
  the captured actor's initiative, or project an old hand onto the new board.
- A completion for an earlier placement of the same card could consume its newer
  placement marker. Deliberate withdrawal before an earlier select ran could
  briefly reconstruct the withdrawn card from its delayed native selection.
- Scene-load queue cancellation also needs to clear pending presentation markers
  when the old hand survives a failed load.
- Releasing the displayed hand before a queued left-slot selection completed
  could lose the original actor's intended initiative. The repaired completion
  uses the live owned recess while its token survives, otherwise its captured
  native left-card choice in the captured hand's actual round pair. Deliberately
  withdrawn input cannot be revived; later FIFO input still wins.

These findings are independent of nullable character names. The implementation
keeps native callbacks authoritative and retains only bounded pending metadata
for the two physical occupants. It introduces no second action queue, private
card identity transmission, saved-game edits or new configuration.

## Multiplayer and verification boundary

Existing record4 publishes owner wanted, occupied and snap masks. Existing
record18 carries physical slot order, including exchanges when both occupancy
bits remain set. Owner changes preempt ordinary publication cadence; subsequent
complete snapshots retain empty-mask validity and support late observers. No
wire extension is required.

The new remote regression compiles the actual production codecs and source
publication/application kernels. Its hierarchy, clock and activation boundaries
are explicit: this proves state-to-activation behavior, not live transport,
native rendered pixels or headset interaction. Its worker evidence passes836
assertions and11 causal controls. The final integrated rerun also passes836/11.

The new local regression uses original GH.Runtime/SRL selection, deselection and
initiative callbacks in Unity2021.3.5, with a separate exact-source tray/fan
projection check. Geometry admission, save loading, full renderer execution and
actual multiplayer transport remain outside that fixture. Final frozen native
callbacks pass3496 assertions and10 causal controls; the separate portable
projection section passes471 assertions and four causal controls. Evidence is
composed: the final combined command completed the native proof but failed before
projection execution because the new captured-left fallback added two guarded
source reads. Only that source-binding count was repaired; the projection was
rerun and original native fixture/production inputs were verified byte-identical.
The final checker invokes both sections for future changes. No second complete
native run is claimed.

The new suites are local-only because hosted metadata references cannot execute
their original game/Unity dependencies. Inventory becomes223 local /100 CI /16
source. Build678's unchanged composed221-suite coverage and300,518 wire golden
assertions are inherited, not represented as a new complete223-suite pass.

## Integrated evidence and retained failed attempts

The six final followup source files match worker f1ca7e4ab byte for byte in main
integration7e90e1cd3. Independent final source review is archived under
`.planning/debug/card-selection680/workers/remote-review-final/`; its scope is
source review only. The initial null-name fix is29ab1f078; remote regression
fixture f22216482 changes no production receiver code.

- Strict Debug and Release both pass with zero errors/warnings; logs are under
  `build-final/` in the main evidence directory.
- `source-final/results.json`:16/16 source suites pass.
- `local-final/results.json`: seven affected existing suites pass: card bindings
  18206, board refresh1216, loss modal1066, round-card50, pick-tray245,
  scene lifetime36 and pool lifetime169 assertions, with their existing bindings
  and controls. The eighth remote suite refused its first certification because
  the integrator changed NetProtocol build-note comments while it was running.
  Its production836 checks and11 controls completed, but the source-change guard
  correctly failed the wrapper. This failed receipt is preserved.
- `remote-final-rerun/results.json`: only that remote suite is repeated; it passes
  against stable final input,836 checks and11 controls.
- Bundle format/game-compatible figure bank and docs i18n checks pass. Surface
  census remains667 configuration keys /238 patch entries /4798 log tokens.
- `compiled-scope/delta.json`: comparison to the captured pre-task679 Release
  binary limits behavior changes to CardsDriver and PlayTray. Eight other
  decompiled files differ only by inlined679→680 and build metadata. The captured
  binary's stamp is6d44bd3 dirty, so it is not advertised as an exact clean
  dev64a9104e7 baseline. The shared historical guard baseline is untouched.

The premature suite-inventory unit run failed only because the new native checker
had not yet been integrated; its result is retained and the affected inventory
test passes after integration (`suite-inventory-final.log`). Native/projection fixture source-binding
and mutation-development failures are retained with their source snapshots; no
earlier failed attempt is relabelled as a passing final run.

The native fixture commits9b3abe685/12b11a223 are integrated asce8c779ee/92b4e909f.
Both native and projection extraction bindings succeed against the integrated
source. Worker evidence is copied and verified before removing only this round's
three initialized worktrees. Completed generated caches use the shared cleanup
API; hardware inputs, failed evidence and current artifacts remain preserved.
