# Completed Android Player delivery recovery — 2026-10-11

## Evidence and cause

`quest-build-support-20261011T065619Z-2a04bcd6.zip` identifies
`b1cd188611167112feee42d854e31d9ebc86a987`, Runtime665. Its current
`unity-build-3367946efeab.log` reaches the successful Android Player and required
Unity evidence boundary; Gradle completes89/89 tasks. The host then fails in
`add_update_capability -> repack -> _signers` with `KeyError: 'zipalign'`.
The full-build toolchain omitted the alignment command that the update-only
path already supplied. Capsule creation had succeeded before this failure.
This is the first supplied successful Windows Unity/Gradle build, not yet a
completed host delivery or a headset acceptance result. Historical failed logs
in the same archive are not the cause of this final run.

## Repair and immediate continuation

The full toolchain includes the selected SDK's zipalign. Signing also accepts the
legacy map by deriving that exact command beside its selected aapt. It validates
commands before modifying an APK and preserves the caller's toolchain identity.
The failure is exercised with real pinned Android30.0.2/OpenJDK signing tools,
including raw ZIP preservation, certificate continuity and negative controls.

`player_delivery` selects a successful existing Player using bounded JSON,
APK central-directory/identity headers and three indexed original-file metadata
witnesses. It never hashes/enumerates the original installation or project.
A new independent completed-Player checkpoint precedes fallible host delivery;
legacy successful Players can be adopted without that newer checkpoint.
APK capability publication does not invalidate this boundary. Inconsistent
identity is reported and retained, rather than silently starting a full build.

The Wizard detects this boundary before checking any earlier stage outputs,
network, source checkout, Unity installation/licence or asset preparation.
`finish-build` runs current corrected host tooling against the original frozen
input/source/project. It does not inspect, snapshot, prepare, weave or launch
Unity. The ready build-Python environment is reused; its bounded interpreter and
requirement receipt checks are not game/Unity revalidation. Earlier completed
phases stay closed and the saved global frontier remains visible. A later
installation retry also preserves this finished build.

This particular APK remains Runtime665. The delivered Builder contains published
dev667 (`9b0a4f0f5`) for new builds; it does not relabel or silently recompile the
existing Player as667. Final signature/content/native-program delivery gates had
not completed in the capture and remain required once. Their bank byte proof is
shared with Compute evidence and the build receipt, avoiding repeated full-bank
hashes. The Wizard does not create another bank-hashing receipt. The final result
now exposes its inputKey/modBuild to its Wizard consumer.

Exact reviewed late-consumer/Builder profiles preserve all27 original preparation
owners if a new build/update is chosen. Unknown changes to producers still differ.
No Unity shader matrix, original game import or local game APK export is run here.

## Focused validation

- Final-tree Builder243 distinct cases are composed:242 pass initially; one
  fixture still selected the previous AST alias. Its14-case affected module
  passes after binding to the current exact alias; no production rule is relaxed.
- Final-tree Wizard205 cases pass, including12 immediate delivery/installation
  recovery cases, retained progress and existing workflow/receipt controls.
- UI33 cases pass, including actual browser retry coverage and EN/DE string ABI.
- Real SDK signing is covered by two cases within the Builder count. Real
  retained-selector/finish/publication composition uses controlled final binary
  audits; it does not certify a real game's GPU pixels or native behavior.
- Runtime667 Release compiles with zero warnings/errors; current-mod AOT23,
  native-camera/static-weave37 and executable weaver836 assertions pass.
  Published dev667 multiplayer/source/golden evidence is inherited; no new
  complete unrelated game gate is claimed.

The final archive audit and compact private validation receipt record actual
old-release continuation fixtures, extraction/source inventory, Linux launcher
start/restart, archive hash and cleanup measurements. The Windows artifact is
not included in the capture, so its final delivery and hardware result remain
pending the maintainer's next run.
