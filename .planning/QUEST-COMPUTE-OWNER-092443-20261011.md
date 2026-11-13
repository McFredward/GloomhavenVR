# Independent-export Compute ownership repair — 2026-10-11

## Evidence and cause

`quest-build-support-20261011T092443Z-fb8cf757.zip` identifies the `dcaf2f261`
Builder, new-build Runtime 668, resuming its already compiled Runtime 665 Player.
Alignment and native signing now succeed on Windows. The APK and Shader final
gates also succeed. The run fails only in the final Compute gate, recognizing
the exact witnessed EyeHistogram OpenGL bank hash but rejecting its owner.
All 27 original preparation owners remain closed; the current finish invocation
takes approximately 166 seconds and does not reconvert or import the project.

The maintainer independently reports the actual exported owner:

```json
{"name":"EyeHistogram","guid":"43dced0ca11f5ab4c850ff0e55f341a3","assetPath":"Assets/Resources/shaders/EyeHistogram.compute","kernelCount":1,"originalDxbcSha256":"26239d6030173bc084c84cd719c28e261b31987e1423ebc98cbeded5bd164533"}
```

Its original program and retained native bank match. The failed consumer instead
pins sandbox export GUID `14b830dd8a5381e4399cfe161a01662f`. AssetRipper generates
these GUIDs per export, rather than deriving them from the original object.
The other twelve ancillary postprocess banks also incorrectly pin sandbox GUIDs,
paths and generated source hashes. Paths can undergo recorded case migration;
the original recovery writer hashes actual platform-native LF/CRLF source bytes.
These current export links cannot serve as cross-export original identities.

Primary source: [pinned AssetRipper export collection](https://github.com/AssetRipper/AssetRipper/blob/1ac666f47d8e9dedf96afb0b914c70d7656151ea/Source/AssetRipper.Export.UnityProjects/AssetExportCollection.cs#L70).
Local source evidence is in `quest-recovery/export_identity.py`,
`quest-recovery/case_paths.py`, `quest-compute/recovery.py` and
`quest-compute/references.py`.

## Repair and retained work

All 13 ancillary banks now require their exact original kernel names/order,
DXBC, dispatch sizes, interfaces and output contracts plus exact serialized
bank hashes and renderer/level. Unknown or duplicate native banks still fail.
The actual complete object hashes remain in the origin ledger. Every active
Vulkan program still passes through the unchanged frozen 13-shader/36-kernel
executable validator; this change does not substitute desktop programs.

The unfinished final gate verifies only the 13 small current source/meta pairs:
actual recorded raw hashes, current GUID/importer, class/local ID, ordinary
project-owned paths and unique owner links. Current export GUIDs and Windows
CRLF are valid when their actual local contracts agree. This reads no asset
inventory, does not rewrite sources and invokes neither recovery nor Unity.
The independently measured retained-project owner check takes about 0.02 seconds.

Independent final receipts preserve successful signing, APK and Shader work.
Only the incomplete Compute gate needs retrying. Exact reviewed source aliases
also preserve the completed original preparation scope across this delivery;
unknown Compute consumer changes remain distinct. Compact support metadata now
includes actual owner GUIDs and original kernel hashes for at most two observed
projects, without exporting source programs or opening the assets themselves.

## Validation scope

The integrated functional tree passes 312 focused host checks, including
independent export GUID/CRLF coverage, altered program/interface/dispatch
rejection, source/meta linkage, duplicate/foreign ownership and continued final
gate receipt reuse. Unchanged alignment/signing, Wizard/UI and Runtime 668 checks
remain inherited evidence, not a newly claimed complete gate.

Independent review qualifies all 13 real retained source/meta pairs and a
temporary fixture using real source bodies, independent export GUIDs and CRLF.
The latter includes the maintainer's reported EyeHistogram GUID and is removed
after verification. Exact actual delivered source inventories, retained cooked
native programs and the game-free extracted Linux launcher have separate compact
handoff receipts. Their precise scope and archive hash are recorded beside the
Builder ZIP.

The local native artifact is Runtime 625, not the maintainer's Windows 665 APK.
It can establish the corrected native-program consumer, not Windows end-to-end
completion or headset rendering. No fresh original import, Shader compiler
matrix, Unity invocation or game APK export is performed for this repair.
