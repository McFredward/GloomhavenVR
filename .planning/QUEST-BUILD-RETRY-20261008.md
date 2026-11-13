# Quest current-source build retry — 2026-10-08

Quest work remains on `feature/quest3-standalone`; the maintainer explicitly
reconfirmed that exception and requested the latest committed `dev` source in
this handoff. No changes are integrated into `dev`, and no published branch
history is rewritten. The latest mod source is included through a reviewed
integration base; unpublished retry repairs are rebased onto that base. Worker
source snapshots are private dependencies, not commits for integration.

## Captured cause and retained work

`quest-build-support-20261007T215452Z-0272d02d.zip` identifies source
`ac1161904f4c0d849acb4de4d4373ff61789000e`, ModBuild 627, and attempt 5 of the
same owned session as capture 200625. This attempt lasted 101.9 seconds and reused
all 16 raw packages. Catalog staging completed; canonical original UI shader
qualification then failed with `ModuleNotFoundError: No module named 'UnityPy'`.
It never reached the first derived copy. Zero accepted new copies in this
capture is therefore not evidence that completed copies were discarded. No
byte corruption or renewed original export is established by this capture.

The stdlib-only installer venv previously reached conversion in the retained-raw
path without the conversion dependencies. Actual prepare/build CLI work now
hands off to the pinned private build interpreter before the run lock, including
later Python child tools. Provisioning is supervised, failures replace stale
diagnostics, exit status is propagated and cancellation reaps the child.
Already provisioned environments are qualified and reused. Discovery stays
stdlib-only. Direct preparation callers share one lazily activated environment;
raw reuse and complete preparation reuse cannot bypass required packages.

Per-file staging and transformation journals retain compatible accepted work.
Additional mutation guards seal preparation-copy evidence between byte
comparison, metadata publication and rename. Actual fixtures terminate twice
inside partial copies, destructive transformations and preparation. Completed
copies/earlier transforms survive; only the open destructive phase rolls back.
Unity Library and immutable original inputs stay outside rollback. Changed
inputs legitimately invalidate dependent outputs, while the original recovery
recipe and durable staging implementation remain unchanged by these repairs.

## Current attempt and measured progress

Starting or resuming a run clears old failed/blocked rows, waiting actions and
live nested phases before checking prerequisite receipts. Closed compatible
work remains marked retained. Current item/phase fractions and counters are
separate from historical global high-water progress; a later retry cannot show
an old active failure or grant an unfinished later phase current progress.
Changing the real build input key clears incompatible plan evidence. The actual
engine, HTTP server and Chrome workflows exercise retry, a different second
failure, process termination and changed inputs.

The current mod adds an offline original-derived environment bank. Public
Builder ZIPs and Git source snapshots exclude its geometry/index/meta payloads
and original-derived manifests. The builder extracts each original source
bundle from the owner's immutable game snapshot, prepares the same desktop
geometry tiers, checkpoints producers separately and compiles a distinct
Android `ghvr-environment.bundle`. Native and derivative caches are independent
of unrelated mod-code commits. Generated compiler-input receipts bind original
source hashes, producer source, game key and index bytes to the selected bank.
Quest runtime reads that bank-local source witness; desktop retains its existing
on-disk original-source verification and bundle location. The complete four-bank
inventory is carried through Android startup, signed
installation metadata and wireless ADB. The environment header opens asynchronously
before dependent mod assets; mesh payloads remain demand-driven. Existing signed
three-bank hardware packages remain installable. No additional PC bundles or
startup full-game scans are introduced for this adaptation.

Actual source-bundle and mesh counters advance the open mod-resource-banks
checkpoint. Their two measured recipe spans cover its first half; real Unity
compilation and qualification retain the remainder. Geometry 100% cannot close
the checkpoint or whole build. No elapsed-time animation invents progress.

## Qualification and limits

Final source/gate receipts and the exact extracted game-free archive are
qualified before replacing the canonical Builder ZIP. The mod-source merge
already passes strict Release/Debug with zero warnings/errors, the 15 source
checks, current production RenderQuality controls, Quest platform controls and
menu-camera controls. Unchanged current-dev protocol/budget evidence is
inherited, not reported as a fresh complete local or wire gate. The integrated
repair gate at `71d211aeb` passes 717 of 719 cases across 26
affected tooling suites (two existing optional recovery skips), including 29 UI
cases and 12 actual Chrome workflows. Strict Release/Debug have zero warnings
and errors; source checks pass 15/15; Quest platform passes 3,081 assertions and
17 defect controls, and the authored Android recipe passes 111 assertions.
Managed startup passes 313 assertions plus 21 defect controls; bounded startup
logging passes 57 plus 11. Worker provenance checks retain 27 assertions bound
to byte-identical actual source files, with native APIs explicitly modeled.
Subsequent renderer integration retains unchanged tooling/editor evidence and
qualifies the composed renderer/platform sources separately. It does not create
a new whole-repository or native Unity result.

No host Player/APK, original full-game conversion, Unity import or exhaustive
shader compilation is performed. Fixtures and source checks do not establish
a successful complete Windows build, Android shader output or headset picture.
The maintainer's next Windows build and hardware capture remain the practical
validation of those outcomes.

Close the old Wizard before replacing the archive. Retain the existing work
and state directories and resume the same session. Original packages and
compatible converted phases remain reusable. Current-source code, authored
art and changed preparation consumers rebuild when their actual input contracts
change. Completed owned workers are removed only after compact external evidence
and extracted-release qualification have been retained; unrelated worktrees,
supplied captures, active builds and read-only game references remain untouched.

The final reviewed integration base is `43e595220`, containing committed dev
`5c8298918` at ModBuild 640. Current-source retry implementation is `610500015`.
Latest Frame captured-menu feedback exclusion, early sole-camera stereo policy,
late native camera write guards and finite viewport reset repair are composed
with the existing Quest lifecycle, MSAA and menu ownership adapters. Renderer
qualification passes 99 assertions and 24 causal controls plus two startup
controls. Quest menu ownership passes 26 assertions/eight controls. Unchanged
desktop camera guard sources are carried directly from dev; native camera
rendering and Quest picture correctness are not established by these fixtures.

Final 640 strict compilation, source15 and composed Quest platform checks receive
fresh receipts. Unchanged 717 tooling passes and the 111-assertion authored-bank
recipe remain explicitly inherited. The complete suite manifest retains 190 local /
106 CI / 15 source suites. The extracted-release receipt binds the final archive
and Git-free source identity. The source snapshot is fixed before final packaging
while other dev work may continue independently.
