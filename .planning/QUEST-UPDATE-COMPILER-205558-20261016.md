# Quest compact update: retain native production across collector repairs

## Observed boundary

Support205558 identifies Builder `fe14a58cc`, runtime B673 and session
`725c60b4aa514acc89bd195026599cf7`. The selected source APK remains B672,
SHA-256 `191411bf837ced926cd32e2d182e47806aab4a471d213b9696997cbb09fd4ab5`.
The route is compact code, with unchanged art and no rebuild reasons. Its build
stage lasts 555.66 seconds and starts no new original-game asset conversion.

The previous OpenXR repair has passed its Windows boundary: Unity reports
`Build Finished, Result: Success.`, the helper confirms a matching ARM64 IL2CPP
pair, and the compiler exits zero. The host subsequently throws
`APK has conflicting MonoScript identities.` while collecting that produced
Player. This is a post-build registry rejection, not a failed native compile.

The old message omits the conflicting identity, physical locations, property
hashes and execution orders. The report contains neither this Player nor its
registry members. Therefore it cannot establish which field differs in this
particular Windows output. A read-only local metadata probe has been requested;
do not claim the reported conflict is order-only until its rows are available.

## Repair scope

Close successful native production independently of later compatibility
collection. A retry first considers the completed production receipt or the
single prior output named by the stable compiler project owner. Compare captured
base/game/profile/source inputs, package ABI, exact producer rows and compiler
recipe before adoption. An explicitly reviewed legacy source pair bridges this
reported release, which did not yet record the new recipe. Do not scan arbitrary
historical folders, copy another multi-GB Player, edit compiler receipts, or
silently regenerate game assets.

The recipe includes compiler-driver functions and the complete APK update module
except its four post-build roster/collection functions. Runtime, weaver, package,
native and art producer inputs remain exact. Current native-pair hashes,
serialized types, assembly ownership, startup callbacks and resource sidecars
still qualify the candidate before a successful update receipt. Changed actual
compiler inputs use the ordinary incremental route.

Registry collection now reports bounded exact conflict locations and values.
It may admit an execution-order-only duplicate solely when every physical
property hash equals the retained original and a complete original row exists.
The source APK's MonoManager/MonoScripts remain authoritative and are retained
in the published APK; compact registry files are never transplanted. A differing
property hash or an ambiguous new type remains a real rejection. This policy is
safe independently of whether it explains the reported Windows conflict.

Support export now records the bounded compiler receipt and output presence for
the exact selected update. It explicitly does not assert APK contents or final
publication were verified. EN/DE failure guidance identifies the post-build
registry boundary and retained result instead of suggesting asset conversion.

## Verification scope and remaining evidence

The focused suites use tiny actual APK containers, actual matched native-pair
receipts and real synthetically serialized Unity files. Native resume fixtures
inject the roster decoder, while separate UnityPy fixtures exercise the physical
registry and conflicting hashes/orders. No local full game conversion, shader
census, native APK build or device installation is performed.

The retained real local APK's 2701 physical registrations yield 2632 identities
and 69 identical duplicates; generic and qualified roster results equal its
legacy capsule. Approximately 4.63 MB of registry data is read, with no game-bank
scan or APK modification. This old retained APK is not the failing Windows
Player and does not identify its conflicting rows.

The integrated focused run passes 101 compact update/roster/resume cases, 24
compiler-support cases and 33 failure-guidance cases (158 total). Actual
shipped-module checks, archive/source hashes and cleanup receipts are recorded with this round's evidence under
`/home/claw/quest3-local/build/evidence/B673-update-205558-20261016/`.
Unchanged earlier Unity/OpenXR, runtime and installation evidence is inherited,
not a fresh complete gate. Runtime remains B673. Preserve `.ghvrq`, especially
`updates/5f2aaf8972cbe6d9850a9700f87b07d38ff3557af0be42ee7002df165e9bc10a/code-player.apk`
and its `.code-build.json`; final Windows admission/publication and Quest image
remain unverified.
