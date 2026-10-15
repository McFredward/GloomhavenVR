# Steam Link manual-only source test, 2026-10-15

The maintainer explicitly rejects the30-second automatic source-test timeout.
This primary reserves ModBuild676, STATE's new676 entry, Core/SteamLink, their
focused runtime controls, the SteamLink EN/DE help/config strings and
STEAM-LINK-MASK.md. The existing manual menu escape and switch are preserved;
no new Frame mutation/capture permission is requested or implied. No automatic
time-limit setting will replace the removed limit.

Worker uses a separate initialized worktree from current dev01ab8fdb9. Its scope
is policy/lifecycle/callback clock removal plus production controls. Primary owns
strings/docs/version/integration, focused affected checks, commit and dev push.
Existing unrelated trailer and CI-preflight test repairs remain untouched. The675
CI owner may commit its test-only followup independently; please preserve this
integration and do not overwrite its version or source. No repeat of unchanged
221-suite/native/golden gates is planned for this narrow request.

The675 CI-preflight repair01ab8fdb9 is included in the worker base. No changes
to tests/test_parallel_test_suites.py or675 fixtures are made by this primary.
The new source push will naturally supersede the older dev CI run; please keep
any additional CI-only fixes disjoint and do not reserve676 for another build.

Integration is ready: worker94cc36ba3 was cherry-picked as bc889f46b. Its1,225
production-policy controls and Unity-reference compile pass. Primary actual
Debug/Release builds pass with zero warnings/errors, player-help lookup passes
2,308 assertions, options coverage and docs i18n pass, and config/Harmony/log
surfaces remain667/236/4,797. Independent source review has no blockers. Test
caches are cleaned; retained evidence lives under `.planning/debug/steamlink676`.
No full-suite replay or hardware/device proof is claimed. This primary owns the
final676 commit and dev push; unrelated trailer files remain outside that commit.
