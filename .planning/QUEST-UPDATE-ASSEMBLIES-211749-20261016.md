# Quest compact update: admit identical assembly registry duplicates

## Windows evidence

Support211749 identifies Builder `db003f5a5`, runtime B673 and failed session
`e2d5d5c6c4c446e58b9b245e721c9d35`. The selected source APK is still B672,
SHA-256 `191411bf837ced926cd32e2d182e47806aab4a471d213b9696997cbb09fd4ab5`.
The route remains compact code with unchanged art and no rebuild reasons.

The retained native output is reused: the build stage lasts 19.611 seconds,
starts no Unity, weaving or SDK commands, and reaches the new registry check.
Two duplicate MonoScript identities pass exact original-property qualification
and retain original execution orders. The previous unclassified conflict is
therefore now confirmed as order-only for those two identities. No read-only
Windows probe or fresh native build is needed to establish that boundary.

The next gate fails with `Player assembly registry is invalid.` The nested
assembly-map parser rejects any repeated assembly name, even when every repeated
row has the same classification. The retained real local Unity2021 Player
exhibits this exact false rejection: its 4993-byte ScriptingAssemblies.json has
155 rows but 153 unique names. UnityEngine.SpatialTracking.dll occurs at indices
68 and 87 with classification16; UnityEngine.XR.LegacyInputHelpers.dll occurs at
72 and147 with classification16. This local Player is older than the user's
B672 source; the old opaque message does not identify its specific failing row.

## Repair

Validate the ordered names/types arrays, then fold identical repeated name/type
rows only for the compatibility comparison. Conflicting classifications,
malformed rows and original assembly removal/reclassification remain rejected.
Never choose an arbitrary first/last classification. Preserve the compiled
ordered ScriptingAssemblies.json bytes when publishing the matched native pair;
normalization is a comparison view, not a rewritten runtime registry.

Errors retain the stable Player assembly registry prefix plus the original or
compiled role, member and bounded row/value context. Wizard guidance preserves
the APK and completed native output and offers retained retry/diagnostics,
without treating this parser failure as proof of necessary asset migration.

The production change stays inside post-build collection, outside the native
compiler recipe. Existing exact runtime/weaver/native/art inputs, the reviewed
legacy producer bridge and cached native-pair qualification remain unchanged.
The next saved update can recheck the same completed Player without running
Unity or converting original assets again.

## Verification boundaries

Focused fixtures use real tiny APKs, native-pair receipts and synthetic Unity
registries, with positive identical-duplicate cases and negative malformed,
conflicting-classification and original-subset cases. A separate read-only
retained-Player probe measures actual Unity sidecar schemas; its callback JSON
has34rows with expected string/int/bool fields. This does not establish the
contents of the user's compiled Player or the resulting headset picture.

The integrated focused run passes110compact APK/roster/resume/assembly cases;
34failure-guidance cases pass for the unchanged guidance integration. The prior
24compiler-support cases are inherited. Actual shipped-module checks, release
inventories, source/archive hashes and process/cache cleanup receipts are recorded under
`/home/claw/quest3-local/build/evidence/B673-update-211749-20261016/`.
No local full game recovery, shader census, native APK build, device installation
or unrelated complete gate is run. Unchanged earlier evidence is inherited.
Runtime remains B673. Preserve `.ghvrq`, original APK/content banks, compiler
Library and completed code-player.apk plus its compiler receipt; resume Mod Update.
