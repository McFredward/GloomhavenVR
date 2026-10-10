# File-level Quest preparation repair after capture055813

Quest work remains on `feature/quest3-standalone`, with published Runtime661.
This handoff supplies the source Builder; the maintainer builds the APK on Windows.

## Observed failure and Editor attribution

`quest-build-support-20261010T055813Z-871f116d.zip` identifies source
`0dbda35b62748ea6b136b032e08fa309cb2830e4`, Runtime661. Preparation rejects
`Assets/Scenes/Bootstrap/LightingData.asset` under its actual final
`script-orders` owner. All 27 preparation checkpoints are closed; neither a
pending conversion nor a metadata refresh exists. The stable project remains
`24f29369a96c0f2d6c9947b8abb5a464e563c9963753cfcd737175c04d6bc1df`.
The attempt ends before a new weave or Unity launch. The imported ff-prefixed
SDK/Player logs in the support archive belong to the preceding attempt.

The original LightingData YAML has no `m_Script` pointer. The inverse SDK-pointer
recognizer therefore cannot explain its Editor serialization. An independent
existing successful project contains 13 scene LightingData files rewritten from
YAML into Unity 2021.3.5f1 serialized binary, with unchanged original GUID metas.
They are included in the original asset census and consequently owned by
`script-orders`. The previous 232406 audit's size-only census already included
these changes but did not classify them; its stronger conclusion about no
additional drift was unsupported.

Opening the generated project in the Editor can trigger this kind of processing.
The report does not contain the changed Windows asset bytes or the GUI process
history, so the maintainer's manual opening is a plausible writer, not an
established cause. The private comparison project has different canonical GUIDs
and cannot reproduce the Windows file's exact fingerprint. The Windows Builder
uses its own already-qualified immutable recovery project instead.

## Repair and continuation

Qualification first retains unchanged witnesses and recognizes the existing
audited Shader, SDK-pointer and PlayerSettings transformations. Remaining damaged
or missing output files are collected as a finite repair set. Each replacement
must reproduce its complete recorded size and SHA-256 before publication. No
unknown binary LightingData is accepted merely because its filename matches.

The repair provider uses selected immutable recovery/template/localization/profile
sources, qualified case-path mappings, existing conversion overlays, a single
existing archive member, and exact small generated recipes. A previously accepted
SDK-pointer variant requires the pinned mapping and the complete expected output
hash. During an input migration the old immutable source snapshot remains available
until the reviewed late Editor overlay runs.

A damaged asset with a missing original GUID sidecar also needs the sidecar from
the already-qualified original inventory. Existing importer metadata is retained;
the repair does not generate replacement GUIDs or invent new preparation owners.
An interrupted publication resumes its bounded durable file transaction.

Completed phases are not reopened. Other generated files, immutable original
inputs and Unity Library remain in place. The next ordinary SDK Unity invocation
performs its usual incremental import of touched files. A small content-addressed
cache preserves otherwise hard-to-reconstruct generated control documents, bounded
to 1 MiB per document and 64 MiB total; it never duplicates native asset banks.

The repair phase reports the complete damaged-file count and the current file,
with byte counters for longer reconstructions. Support exports retain bounded
repair history and whether a repair transaction is pending. If this exact project
is still open in Unity, the Builder waits visibly and resumes when the Editor
closes. It never deletes a Unity lock or terminates the Editor. Windows observes
the live sharing lock; Linux inspects the actual process/project path, including
paths relative to the Editor process's own working directory.

Missing reconstruction sources and unowned/linked paths cannot be turned into
successful output by guessing. Such unreconstructible prerequisites must retain
completed work and identify the particular file; automatic repair is bounded by
the available owned source evidence, not a promise to conceal every build failure.

## Validation boundary

Validation receipts live outside the public handoff at
`/home/claw/quest3-local/build/evidence/B661-targeted-repair-055813-20261010`.
The independent production wiring proof reproduces the previous shipped reader's
failure and repairs all 13 real original/imported LightingData pairs under their
actual owners. It retains 30 actual SDK bindings for two native prefabs and all
three official Shader upgrades. All 27 closed owners and Library remain intact.
Its next warm resume explicitly forbids payload hashing, SDK/Shader/settings
rechecks and repair calls. Healthy outputs and both immutable source trees keep
their bytes and timestamps. The private fixture uses sentinels for unrelated
outputs and is not a literal copy of the Windows workspace.

The integrated root runs 268 focused Python cases: 267 pass and the native
Windows sharing-lock case is skipped on Linux. Portable tests cover repair/publication cuts,
missing files and GUID companions, exact source reconstruction, unchanged warm
witnesses, progress, import compatibility, isolated loaders and support exports.
Three isolated loader states also execute an actual settings reconstruction after
temporary dependency aliases have been removed/restored; this exposed and fixed
a lazy helper import that a direct module test would miss. Both localized UI
labels pass a Node check. The Windows sharing-lock test is not a local Windows pass.

No new complete multiplayer gate, expensive shader matrix, fresh Unity import,
local whole APK or headset acceptance is claimed. The existing Runtime661
Release/AOT/static-weave and published NPC checks remain inherited evidence.
