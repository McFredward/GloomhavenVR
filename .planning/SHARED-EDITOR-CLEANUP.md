# Shared editor fixture completion

Test class: B, bounded to the Results and environment fixture launch/cleanup
lifecycle. Base: `8cf9028c9ef4cf9e1131257d632d85fe7614324d` (`dev`).

The retained Quest771 runs show Unity continuing to write results after the
launcher timed out and project caches were deleted. Unity can escape xvfb-run's
process group, so reaping that launcher does not establish native editor exit.

Both runners now use the existing exact executable/project `cleanup_project_editors`
before `cleanup_completed_run`. An uncertain or remaining editor pins the run with
`.keep` and refuses cache deletion, including a later outer-suite cleanup. The
environment runner supplies a small explicit completion log because its native
warning stream exceeded retention's bounded locator size. Results required-child
exit codes and earlier child receipts survive nonzero completion and timeout.
Existing timeouts, oracles, shader ABI and production sources are unchanged.

The existing `scripts/menu-depth-character/test_project_editors.py` now executes
the actual runner functions with real escaped editor/child processes. Coverage
includes successful no-op teardown, native exit 7, timeout, remaining-editor
refusal, two causal omitted-teardown controls, cache/evidence ordering and Results
required-followup failure/timeout receipts. Original exact-owner, foreign-process,
forced-kill, premature-reaping and idempotency controls remain.

Final cheap process proof: **81 assertions pass in 15.56 seconds**, with every
direct child reaped, completion cleanup receipts and no remaining generated caches
or `.keep` pins. The preceding 81-pass run before final input-pin additions is
also retained. Evidence is copied to
`/home/claw/quest3-local/build/evidence/quest-v1.2.4-rebase-20261113/editor-cleanup/`.
`git diff --check` passes. No Unity, C# build or complete runtime suite ran in
this worker; complete affected Results/environment qualification remains with
the integrator after the frozen integration gate. This record claims process
unit proof only, with no native-game or headset acceptance.

The patch belongs on both `dev` and `feature/quest3-standalone`. When resolving the
environment manifest hunk on Quest, retain its existing Quest source/fixture
bindings and add the runner/helper/retention fingerprints. Do not replace the
Quest production inputs with the desktop roster. Cross-platform runtime evidence
reuse still follows the separately recorded input-closure audit.
