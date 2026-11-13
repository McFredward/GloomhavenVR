# Quest compact update: load OpenXR settings before the build callback

## Observed boundary

Support172343 identifies Builder `9b5ed28ab`, runtime B673 and failed update
session `e7abea8c9a194eeb82febb22422cf4b7`. Its source APK is still B672,
SHA-256 `191411bf837ced926cd32e2d182e47806aab4a471d213b9696997cbb09fd4ab5`.
The compact route has no rebuild reasons and performs no original game recovery.
The build stage lasts 120.675 seconds.

The original roster fix is confirmed on Windows: the helper admits all 2636
rows and unique scripts with zero execution-order repairs. All five preparation
phases finish and call `BuildPipeline.BuildPlayer`. OpenXR1.13 then fails in
`OpenXRPackageSettings.Instance`, called by its runtime-library preprocessor:
`OpenXR Settings found in project but not yet loaded.  Please build again.`
This precedes actual native compilation. The summary's two errors represent
this package exception and the helper's failed-build result, not two independent
game or hardware failures.

The pinned package first tries `EditorBuildSettings.TryGetConfigObject`. If the
container is not registered, it finds/loads its asset and registers it; it then
deliberately throws if `BuildPipeline.isBuildingPlayer` is already true. Merely
having an asset on disk does not satisfy that pre-build registration boundary.

## Repair scope

Admit the package's settings before entering `BuildPipeline`, within the existing
counted preload preparation. Use the pinned package's own feature/settings API,
preserve registered settings on an unchanged retry, and validate the Android
target accessor. The compact compiler does not configure the source APK's XR
loader, feature enablement, scenes or serialized presentation. Keep existing
native/metadata and full serialized-roster publication gates.

The host also recognizes only this exact fresh package exception and permits
one retry of the same compiler command, environment and project. It never
repeats source capture, weaving, SDK compilation or original game conversion
inside this repair. Repeated, stale or unrelated failures cannot create a loop
or an implicit asset rebuild. Exhaustion reports an EN/DE targeted OpenXR
compiler repair with retained retry and diagnostics. The actual current cause
remains visible if a different error appears on the second invocation.

The retry observation has a localized label, no fabricated work percentage and
does not close the native publication operation. Previous host subtasks and the
whole-step progress remain retained.

## Verification scope

Primary focused verification passes 67 compact APK/driver cases, 32 failure
guidance cases, eight compact progress cases and 28 UI/model/localization cases.
Replay of the exact supplied 374520-byte Unity log through the production host
wrapper verifies the one-retry success path and bounded repeated-error path;
its injected compiler process does not establish actual native compilation.
Existing APK and compiler-object fixtures remain byte-identical; the compiler
object timestamp also stays unchanged. Earlier unaffected ADB, controller/runtime
and native publication evidence is inherited, not re-executed or upgraded.

An isolated real Unity/OpenXR preprocessor fixture is used to reproduce the
package registration boundary and validate early settings admission. It has an
explicit earliest callback stop before any APK/native output; its final receipt
records positive/negative controls, idempotence, output absence and cache cleanup.
This is not a completed Android build or Windows/Quest hardware result.

The actual Unity2021/OpenXR1.13 fixture passes in 8.39 seconds. Its unprepared
probe reproduces the exact package exception with `isBuildingPlayer=true`; its
prepared probe passes the same package runtime-library callback. Both probes
intentionally return Failed at the earliest fixture safety stop, with no native
compiler entry, APK or native pair. Sixteen features, enabled flags, registered
loader configuration and preloaded roots remain retained across the production
preparation. Unchanged retry preserves settings bytes and modification times.
Initial admission takes 0.00231 seconds and unchanged retry 0.000157 seconds in
this small fixture; these timings do not predict a complete user build.

The integrated helper also passes the existing real package-free 135-row roster
fixture in 7.51 seconds. Its exact inactive source identities, three batched
order repairs with one DLL import, zero-import unchanged retry and malformed
roster rejection remain intact. This confirms the new package API resolution
does not add a mandatory XR package reference to that bounded fixture.

Runtime remains B673. Preserve `.ghvrq`, source APK/content banks and compiler
Library; resume Mod Update with the corrected Builder. Complete native build,
publication, installation and headset rendering still require the Windows run.
Round evidence is under
`/home/claw/quest3-local/build/evidence/B673-update-172343-20261016/`.
