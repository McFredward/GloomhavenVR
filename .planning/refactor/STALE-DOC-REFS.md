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
| `Net/Board/BoardTuning.cs` | 730 | `RoundCapShape` | /// pair. Resolved through the same KNOWN-MEMBER test as <c>RoundCapShape</c>.</summary> |
| `Net/NetFigures.cs` | 166 | `Clear` | /// <c>Clear</c>), so it cannot grow.</para> |
| `Net/NetModule.cs` | 111 | `SettingsPanel` | /// entries always exist for <see cref="LocalRigSampler"/>, <c>SettingsPanel</c> and |
| `Net/NetProtocol.cs` | 15036 | `BoardUiRecordBytesWithCap` | /// <c>BoardUiRecordBytesWithCap</c> uses. No new record id, no wire-version bump.</para> |
| `Net/PresenceState.cs` | 654 | `DecisionRoleNo` | /// <c>NetProtocol.DecisionRoleNo</c>). May be longer than |
| `Net/Remote/RemoteBoardFurniture.cs` | 2214 | `renderedHeight` | /// scale — <c>TextMeshPro.renderedHeight</c> off a forced mesh update, i.e. what the |
| `Net/Remote/RemoteBoardVisibility.cs` | 40 | `Off` | /// classes never learned about the mode (the FX class only ever checked <c>PeerBoardFade.Off</c>), so with |
| `Net/Remote/RemoteItemFan.cs` | 1211 | `CollapseSeconds` | /// <c>CollapseSeconds</c> instead of blinking the fan out — the replay of |
| `WorldUI/Buttons/SoftCueArt.cs` | 502 | `MaxAlpha` | /// <c>MinAlpha</c> and <c>MaxAlpha</c> while the frame scales by up to |
| `WorldUI/Buttons/SoftCueArt.cs` | 502 | `MinAlpha` | /// <c>MinAlpha</c> and <see cref="MaxAlpha"/> while the frame scales by up to |
| `WorldUI/Conversion/CanvasConversion.1.Core.cs` | 32 | `UiLockChanged` | /// mirrored here: <c>Core.VREvents.UiLockChanged</c> plus module-side soft locks |
| `WorldUI/MapRoom/MapIconLayer.cs` | 1037 | `_bakeAsked` | /// <c>_bakeAsked</c> exists only so the per-frame cap is spent on FIRST asks.</para> |
| `WorldUI/MapRoom/MapTravelConfirm.cs` | 2075 | `AppendRecommendation` | ///   <c>AppendRecommendation</c>. THIS IS A NUMBER TO TYPE IN, NEVER A POSE THAT IS |
| `WorldUI/Materialise/WindowMaterialiseDebris.cs` | 143 | `Half` | /// <summary>Front half, then behind half. See <c>Half</c>.</summary> |
| `WorldUI/Materialise/WindowMaterialiseField.cs` | 17 | `WindowMaterialiseDebris` | /// <c>WindowMaterialiseDebris</c> builds a shard mesh whose every vertex carries the |
| `WorldUI/MrBacking.cs` | 251 | `Label(TMP_Text?, bool)` | /// (see <c>Label(TMP_Text?, bool)</c>) — the only labels whose plate follows that |
| `WorldUI/Options/ConfigCatalog.cs` | 166 | `ResolveStep` | /// <see cref="Classify"/> — see <c>ResolveStep</c>: the step depends on the largest |
| `WorldUI/Sharpness/PanelSupersample.1.Core.cs` | 421 | `MarkGeometryDirty` | /// instead, which is what <c>MarkGeometryDirty</c> is for. |
| `WorldUI/Sharpness/PanelSupersample.1.Core.cs` | 745 | `CullPairsTruncated` | /// it bites, <c>Entry.CullPairsTruncated</c> says so and every count below it is a LOWER |
| `WorldUI/Sharpness/PanelSupersample.1.Core.cs` | 1015 | `RepairSubMeshCull` | /// the check itself is one boolean compare — see <c>RepairSubMeshCull</c>. |
| `WorldUI/Sharpness/PanelSupersample.1.Core.cs` | 1927 | `RepairSubMeshCull` | /// <c>RepairSubMeshCull</c>. In one sentence: TMP writes a sub-mesh's cull flag ONLY |
| `WorldUI/Sharpness/PanelSupersample.2.Capture.cs` | 2162 | `ScanTmpMesh` | /// the only ones that can see the photograph's fault (see <c>ScanTmpMesh</c>).</item> |
| `WorldUI/Sharpness/PanelSupersample.4.Content.cs` | 137 | `RepairSubMeshCull` | /// larger one, is the TMP sub-mesh cull latch — see <c>RepairSubMeshCull</c>.)</para> |
| `WorldUI/Sharpness/PanelSupersample.4.Content.cs` | 1892 | `ReportSubMeshCull` | /// is complete. Every one of those numbers is printed (<c>ReportSubMeshCull</c>'s |
| `WorldUI/Surfaces/TablePanelSurfaces.cs` | 556 | `Place` | /// rect is re-sized and the content re-centred inside it mid-hover. <c>Place</c> then |
| `WorldUI/Surfaces/TablePanelSurfaces.cs` | 569 | `Place` | /// rest of the time. Between those windows the host rect is LATCHED, so <c>Place</c> |
| `WorldUI/WorldUIConfig.cs` | 330 | `Panels` | /// (Panels ▸ Shared — <c>[WorldUI]</c> maps to <c>ConfigTopic.Panels</c> and the group is |

35 references (the `HandSuppressionPatches.cs` row was cleared 2026-09-08: the symbol is `VREvents.HandShown`, the sentence was true, only the qualification was wrong).
