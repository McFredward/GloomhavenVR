# Documentation references to symbols that no longer exist

> Produced 2026-08-27, when `GenerateDocumentationFile` was turned on for the first time and the
> compiler could finally resolve crefs (see `LOG-2026-08.md` §3.2).
>
> **What these are.** Each line was a `<see cref="X"/>` naming a symbol that is not declared
> anywhere in `src/`. The tag has been demoted to `<c>X</c>`, so the comment no longer CLAIMS the
> symbol is live — but the sentence around it usually still describes what X did, and that
> sentence is what actually needs checking. Rewriting it means finding out what took the job over,
> which is archaeology per site rather than a rename.
>
> **Why this file exists rather than a silent demotion.** Demoting the tag removes the site from
> CS1574, so without this list the debt would simply become invisible. The compiler check stays
> live for NEW dangling crefs, which is the part that keeps working on its own.
>
> **How to clear a line:** find what replaced the symbol, fix the sentence, and delete the line.

19 references. The 2026-09 refactor cleared seventeen: one in `HandSuppressionPatches.cs`
(lane cards), eight WorldUI rows (lane worldui-front) and eight Net rows (lane net). The
LINE NUMBERS in the rows below are not maintained and several are weeks stale — locate a row
by its symbol, never by its number.

| file | line | symbol | the sentence that may no longer be true |
|---|---|---|---|
| `Board/CharacterFocus.cs` | 935 | `PinRefusal` | /// (<c>PinRefusal</c>, the actor-dependent one, bounded by a live hex pick belonging to |
| `Core/Haunt/Haunt.cs` | 373 | `Schedule` | /// preference: <c>Schedule</c> partitions slots into <c>Groups</c> = 3 and picks within a |
| `Core/Perf/PerfTextureCensus.cs` | 114 | `Append` | /// only between <see cref="Begin"/> and <c>Append</c> inside a single window, and both ends |
| `Core/WallFade/WallSegmentFade.PropUnit.cs` | 255 | `BeginStandingPropScope` | /// <para>LIFETIME. Cleared once per commit in <c>BeginStandingPropScope</c> — the |
| `Core/WallFade/WallSegmentFade.PropUnit.cs` | 267 | `BeginStandingPropScope` | /// <summary>True only between <c>BeginStandingPropScope</c> (the first scope of a |
| `Core/WallFade/WallSegmentFade.PropUnit.cs` | 275 | `BeginStandingPropScope` | /// Called from <c>BeginStandingPropScope</c> at the top of every commit.</summary> |
| `Core/WallFade/WallSegmentFade.PropUnit.cs` | 800 | `BeginStandingPropScope` | /// <c>BeginStandingPropScope</c> at the top of the rescan (the standing rule's |
| `Core/Water/WaterReflectionCaps.cs` | 38 | `Classify` | /// than guessed at — <c>Classify</c> returns <see cref="WaterCapFamily.None"/> and the |
| `WorldUI/Conversion/CanvasConversion.1.Core.cs` | 32 | `UiLockChanged` | /// mirrored here: <c>Core.VREvents.UiLockChanged</c> plus module-side soft locks |
| `WorldUI/Materialise/WindowMaterialiseDebris.cs` | 143 | `Half` | /// <summary>Front half, then behind half. See <c>Half</c>.</summary> |
| `WorldUI/Materialise/WindowMaterialiseField.cs` | 17 | `WindowMaterialiseDebris` | /// <c>WindowMaterialiseDebris</c> builds a shard mesh whose every vertex carries the |
| `WorldUI/Options/ConfigCatalog.cs` | 166 | `ResolveStep` | /// <see cref="Classify"/> — see <c>ResolveStep</c>: the step depends on the largest |
| `WorldUI/Sharpness/PanelSupersample.1.Core.cs` | 421 | `MarkGeometryDirty` | /// instead, which is what <c>MarkGeometryDirty</c> is for. |
| `WorldUI/Sharpness/PanelSupersample.1.Core.cs` | 745 | `CullPairsTruncated` | /// it bites, <c>Entry.CullPairsTruncated</c> says so and every count below it is a LOWER |
| `WorldUI/Sharpness/PanelSupersample.1.Core.cs` | 1015 | `RepairSubMeshCull` | /// the check itself is one boolean compare — see <c>RepairSubMeshCull</c>. |
| `WorldUI/Sharpness/PanelSupersample.1.Core.cs` | 1927 | `RepairSubMeshCull` | /// <c>RepairSubMeshCull</c>. In one sentence: TMP writes a sub-mesh's cull flag ONLY |
| `WorldUI/Sharpness/PanelSupersample.2.Capture.cs` | 2162 | `ScanTmpMesh` | /// the only ones that can see the photograph's fault (see <c>ScanTmpMesh</c>).</item> |
| `WorldUI/Sharpness/PanelSupersample.4.Content.cs` | 137 | `RepairSubMeshCull` | /// larger one, is the TMP sub-mesh cull latch — see <c>RepairSubMeshCull</c>.)</para> |
| `WorldUI/Sharpness/PanelSupersample.4.Content.cs` | 1892 | `ReportSubMeshCull` | /// is complete. Every one of those numbers is printed (<c>ReportSubMeshCull</c>'s |


