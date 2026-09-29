# Final town-service compiled scope audit

Audited integration commit: `9fdc7e16` (dev 1.0.7 / ModBuild 538).
Strict Release compilation against the installed game's real Unity/game assemblies passed
with zero warnings and zero errors. This audit found no unintended legacy runtime-code
change in the town increment. It does not establish headset appearance, load timing or
multiplayer hardware outcomes.

## Isolation and method

The audit ran in `/home/claw/worktrees/town-final-audit`, created from the exact audited
commit and initialized with `scripts/worktree-setup.sh`. The existing shared
`.planning/refactor/.guard/baseline` and `baseline.rev` remained read-only symlinks.
Neither was removed, replaced, refreshed or written through.

The temporary driver extracts the actual `snapshot()` and `classify()` implementations from
`scripts/refactor-guard.sh` at the audited commit. Only ROOT and the private output path change.
It builds Release, temporarily hides the adjacent XML documentation during ILSpy decompilation,
and applies the guard's identical timestamp, commit, dirty-marker and branch masking.
The unchanged classifier compares line multisets to distinguish ordering-only changes.
No source gates, broad harness or wire suite were rerun here; the integrator owns those checks.

A second private source archive of `ff59a14e` (the 1.0.7 development version before town
implementation, still ModBuild 537) was compiled and decompiled through the same functions.
It uses read-only dependency links and its own build outputs. This isolates the town increment
from the much older historical baseline without changing the repository baseline.

## Historical baseline result

Reference: `080c505e9379bbe7df2280217f52ff0e04d328eb`.
The exact guard summary is **0 moved, 105 changed, 90 added/removed entries**.
An expanded C# file census is 845 → 950 files: 104 changed, 105 added, zero removed.
The difference is accounting: the guard includes the synthesized project file among changes
and reports a newly added namespace directory as one NEW/GONE entry. These totals include
already-integrated changes between the historical baseline and the pre-town release.
They must not be represented as changes introduced entirely by town services.

## Town-only compiled difference

Compared with `ff59a14e`: 923 → 950 C# files, **27 added, 16 changed, zero removed**.
The raw guard summary is 0 moved, 17 changed, 12 added/removed entries, including the
synthesized project and the new namespace directory.

Four existing type files are identical after replacing only the embedded build-number
constant 537 with 538: `Plugin`, `RemoteHandFan`, `VersionGuard`, `DesyncWatch`.
Their runtime source files were not modified in this increment.

The twelve substantive existing-type changes were reviewed against the pre-town source:

| Type | Intended compiled change |
| --- | --- |
| `Core.Loc` | One English/German work-tray instruction. |
| `Hands.Interact.ProximityGrabber` | Two cancellation paths call the optional IGrabCancellation interface; existing grabbables retain their original OnRelease fallback. Only TownServiceToken implements the new interface. |
| `Net.ExtrasFragments` | Adds town payload admission without removing existing payload types. |
| `Net.ExtrasSendQueue` | Adds town-frame identity and a read-only sequence accessor. |
| `Net.ExtrasSendScheduler` | Adds the town queue and three explicit transport turns; clears it on teardown. Existing transport types retain their own queues and routing. |
| `Net.FfsNetTransport` | Adds town fragment assembly, send admission, routing and teardown. The original item/native send-condition prefix is retained. |
| `Net.NetAvatarDriver` | Adds town send/apply/forget/reset hooks, message dispatch and full-refresh request on peer creation. |
| `Net.NetProtocol` | Adds message types 19/20 and extension 78; increments ModBuild to 538. Wire version remains 3. |
| `Net.PresentationBatch` | Admits the town fragment type alongside existing child types. |
| `WorldUI.ModalFallback` | Adds explicit native-section release/re-enrollment helpers for town composition. |
| `WorldUI.PanelMipBake` | Adds only a read-only original-texture lookup; existing bake/restore logic is unchanged. |
| `WorldUI.WorldUIModule` | Adds town preparation, presentation, population, late-frame and cleanup hooks. Existing step ordering is retained around the inserted hooks. |

The new type files are confined to the town implementation and its optional cancellation
interface. No existing type was removed. Full list:

- `GloomhavenVR.Hands.Interact.IGrabCancellation`
- `GloomhavenVR.Net.TownServices.TownServiceAssets`
- `GloomhavenVR.Net.TownServices.TownServiceBinding`
- `GloomhavenVR.Net.TownServices.TownServiceCodec`
- `GloomhavenVR.Net.TownServices.TownServiceDelta`
- `GloomhavenVR.Net.TownServices.TownServiceFragments`
- `GloomhavenVR.Net.TownServices.TownServiceFrame`
- `GloomhavenVR.Net.TownServices.TownServiceMaterial`
- `GloomhavenVR.Net.TownServices.TownServiceMirror`
- `GloomhavenVR.Net.TownServices.TownServiceMotion`
- `GloomhavenVR.Net.TownServices.TownServiceNeutralize`
- `GloomhavenVR.Net.TownServices.TownServiceNode`
- `GloomhavenVR.Net.TownServices.TownServiceProperty`
- `GloomhavenVR.Net.TownServices.TownServiceSendQueue`
- `GloomhavenVR.Net.TownServices.TownServiceSessionInfo`
- `GloomhavenVR.Net.TownServices.TownServiceValue`
- `GloomhavenVR.Net.TownServices.TownServiceValueComparer`
- `GloomhavenVR.WorldUI.NativeTemplates`
- `GloomhavenVR.WorldUI.TownServiceAssets`
- `GloomhavenVR.WorldUI.TownServiceNativeAssets`
- `GloomhavenVR.WorldUI.TownServicePopulation`
- `GloomhavenVR.WorldUI.TownServicePresentation`
- `GloomhavenVR.WorldUI.TownServiceStation`
- `GloomhavenVR.WorldUI.TownServiceSurface`
- `GloomhavenVR.WorldUI.TownServiceSync`
- `GloomhavenVR.WorldUI.TownServiceToken`
- `GloomhavenVR.WorldUI.TownServiceTray`

The synthesized project differs partly because ILSpy emits reference paths relative to each
private snapshot and because assembly-reference ordering changes. Comparing actual reference
names finds exactly one addition, `UnityEngine.TextCoreFontEngineModule`, used by the original
TMP font descriptor in town asset identity. No assembly reference is removed. The preloader
source and existing legacy card, figure, options and map-behavior source remain unchanged in
the town increment, apart from the explicit integration/cancellation hooks listed above.

## Evidence

Private evidence resides under `.planning/debug/town-final-audit/` in the audit worktree:
`strict-build.log`, `snapshot.sh`, `snapshot.log`, `classify.sh`, `historical-summary.txt`,
`snapshot-pre-town.sh`, `snapshot-pre-town.log`, `classify-pre-town.sh`,
`pre-town-summary.txt`, and `report.json`. The `current/` and `pre-town/` directories retain
the masked decompilations. The JSON report enumerates every historical and town-only added,
removed and changed C# file. SHA-256 values identify this exact run:

- Final DLL (build timestamp makes later builds differ): `9593d217cda1a98dd84ded106af95ae6cafcb5288705da0cc1fdcadea3287617`
- Guard implementation: `fc3e1c1806f7fd2a9ce047e0b065a151db8820feb754b1f940c044cacfb6e204`
- Historical baseline tree (relative path + NUL + file bytes + NUL, sorted paths): `f3e554bbe9a6e429ecf500bc5105dcf4da27b55ae431f7de8ff3c79b8745401e`
- Final masked snapshot tree: `42f184f09244c1809dc6d70a16f5e1389fe9bbf022cf01c62b1fc8ce3dd5d603`
- Pre-town masked snapshot tree: `1a7c6c93ae5e1f1f099386f929f9a7dc5b3ca380c5f4119495983e380784d936`
