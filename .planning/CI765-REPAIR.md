# Build765 CI inventory repair

Risk B, fixture-only correction on dev46f1ee04b. No product, serializer, shader,
reference, workflow, runner or test-group change; ModBuild stays765. The prior
input-bound Results/runtime proof remains valid. Do not rerun the local gate.

Hosted run42497025344 failed in source_checks before SDK setup/build:
test_original_inventory_and_shards rejected scenario-results-control-runtime.
All four121-suite runtime shards passed. The765 integration registered the new
local suite but omitted the independent fixed inventory and count in
tests/test_parallel_test_suites.py. The failure was therefore real and useful.

Add only multiplayer765_local to the exact local and local-minus-CI expectations
and advance the local count296 to297. CI121/source16 inventories and the four-way
partition stay unchanged. Keep the independent expected set; never derive it
from the same actual manifest being checked or weaken the equality assertion.

The exact inventory test fails before this repair with the hosted missing-item
marker. The complete affected Python container then passes19 tests in2.054s,
with its two existing opt-in real-compiler smoke cases intentionally skipped.
No new skip or disabled assertion is introduced. Temporary fixtures clean
themselves; PYTHONDONTWRITEBYTECODE prevents new repository bytecode caches.
Original hosted/local failure logs and final output are retained under
.planning/validation/ci765. A private worker separately compiles the unchanged
actual solution against the same metadata-only RefAsm contract used by CI,
because the failed hosted run had stopped before that build step. It executes
no reference DLL or runtime/game suite. Its command, outcome and cache cleanup
remain in reference-lane; the hosted successor supplies final CI proof.

When registering a suite, check its fixed inventory/count/partition expectations
with python3 -m unittest discover -s tests -p test_parallel_test_suites.py. Source
gates and a focused runtime pass do not execute that separate CI regression test.
The complete296-ID764 checkpoint and focused765 production qualification are not
relabeled as coverage of this previously omitted manifest consumer.

Push dev only; no release, ZIP, device change or worker push. Preserve logs,
compiled evidence and read-only shared inputs; retire only this round's owned
worktree/caches after its processes have stopped and evidence is verified.
