# Quest compact update: conditional source identities

## Evidence

Support170547 identifies Builder `cd71dd316`, runtime B673 and update session
`50bd029f7b44479d8d90c28a99dd4fa3`. The existing APK remains B672 with SHA-256
`191411bf837ced926cd32e2d182e47806aab4a471d213b9696997cbb09fd4ab5`.
The compact route admits the original APK and local game, completes weaving,
standalone adaptation, Android SDK compilation and package binding, then stops
after 118.838 seconds in the build stage. No original asset recovery or full
rebuild starts.

The actual failure is the retained row
`Unity.InputSystem.ForUI.dll|UnityEngine.InputSystem.Plugins.InputForUI|InputSystemProvider`.
The helper reaches row 1888 of 2636 before failing roster admission. Its source
file is present in the pinned Input System package but is conditional on
`UNITY_2023_2_OR_NEWER`. Unity2021 retains its MonoScript even though `GetClass()`
is null. The filename fallback invented an empty namespace and therefore failed
to find the real original identity. Other affected package source files include
editor views and platform/engine-version conditional input and OpenXR types.

## Repair

For source MonoScripts without a reflected class, read the pinned Editor's
native `GetAssemblyName` and `GetNamespace` methods through cached reflection,
and retain the actual script asset name. Keep the serialized-property and
reflected-class paths intact. Do not parse namespace text, enable inactive code,
skip package rows, add a type-name allowlist or manufacture a replacement type.
If the pinned native identity API is missing, report that specific preparation
failure.

The host no longer treats a pre-compilation missing catalog entry as proof of
asset migration. It preserves the APK and compiler results and offers retained
retry/diagnostics for targeted compiler repair. Existing fresh-log ownership,
plugin-failure specificity and stale-log rejection remain enforced. Actual
compiled-player compatibility differences still require an explicit rebuild
decision; matched native/metadata and complete serialized roster equality gates
remain unchanged.

## Verification and limits

The actual Unity2021 fixture retains 135 rows over 128 synthetic plugin classes
and four conditionally disabled source files. It checks the reported exact
namespace, two same-named source assets in different namespaces, an empty
namespace and another inactive type. All four have null `GetClass()` and exact
roots. Three actual execution-order repairs cause one DLL import; unchanged
retry causes zero imports and leaves all five metadata files' bytes and mtimes
unchanged. Missing, conflicting and invalid rows still fail before mutation.
Seventy counted progress records pass validation. The full invocation takes
7.52 seconds; preparation timings do not predict a full Windows native build.
The previous helper, with only a test-access modifier change, reproduces the
empty-namespace failure in a negative control.

An independent tiny real Unity project imports only the pinned packages and
reads 845 exact script identities in 9.25 seconds. Comparing with the prior
2632-row APK capsule resolves all 34 non-engine entries missing from the earlier
catalog, including the exact reported InputForUI row with null `GetClass()`.
This is a package-catalog audit against that retained capsule, not an audit of
the unavailable current Windows 2636-row input or of a new native output.

Focused Python verification passes 62 compact APK/driver/handoff cases and 31
failure-guidance cases. Previous unaffected progress, browser, ADB lifetime and
device checks remain inherited from the `cd71dd316` handoff. Counts overlap;
this is not a new full multiplayer gate. Final archive provenance, extracted
launcher/API checks and tests against the actual delivered modules are recorded
in the handoff receipt.

No game scenes/textures are converted and no APK is built locally. The runtime
remains B673. Preserve `.ghvrq`, the existing APK/content banks and Unity/Bee
Library; resume the retained Mod Update using the corrected Builder. Full native
publication, Windows execution and the next Quest picture remain unverified.
Evidence is under
`/home/claw/quest3-local/build/evidence/B673-update-reject-170547-20261016/`.
All completed fixtures use central cache cleanup after process reaping; the
integrated worker checkout and duplicate delivery ZIP are removed at handoff.
