# Menu audit 02 — WorldUIConfig.cs + ButtonTuning.cs

Audited 2026-08-11 against origin/main (291d06d). Method: every bound field grepped across the
full `src/` tree (decompiled/ excluded); computed-property indirections
(`ExecuteClicks`/`VirtualMouseButtons`/`ModalWindowStyle`/`HoverInfoScaleLive`/`ConversionActive`
and every ButtonTuning clamped accessor) traced to their real consumers. Curation state read from
`src/GloomhavenVR/WorldUI/VROptionsTab.4.Curated.cs`; display names from
`src/GloomhavenVR/Core/Loc.ConfigNames.cs` (every audited entry has one EXCEPT the six retired
WristHud pose twins — consistent with their retirement); menu exclusion machinery from
`src/GloomhavenVR/WorldUI/ConfigCatalog.cs` (`RetiredMarkers` line 350, `NotOffered` line 364).

Legend: **cur.** = already on the curated everyday list.

## src/GloomhavenVR/WorldUI/WorldUIConfig.cs (73 binds)

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| Master | ACTIVE | POWER | Mod-level kill switch; gates everything via `ConversionActive` (35 consumer sites) + WorldUIModule.cs:75, FlatScreen.4.Lifecycle.cs:198 |
| ButtonCluster | ACTIVE | NORMAL (cur.) | ButtonCluster.cs:224 |
| InitiativeTrack | ACTIVE | NORMAL | Surfaces/TablePanelSurfaces.cs:404; panel visibility toggle, not curated |
| ElementBoard | ACTIVE | NORMAL | Surfaces/TablePanelSurfaces.cs:1429; panel visibility toggle, not curated |
| CombatLog | ACTIVE | NORMAL (cur.) | Surfaces/CombatLogSurface.cs:72 (feature master, kept separate from CombatLogUserClosed) |
| Objectives | ACTIVE | NORMAL | Surfaces/TablePanelSurfaces.cs:1475; not curated |
| Dialogs | ACTIVE | NORMAL (cur.) | Surfaces/DialogSurface.cs:66, ModalFallback.4.Tick.cs:298, FlatScreen.4.Lifecycle.cs:127 |
| StatPanels | ACTIVE | NORMAL | Surfaces/StatPanelSurface.cs:322/420/472/551; not curated |
| PropInfoCards | ACTIVE | NORMAL | Surfaces/PropInfoSurface.cs:145; not curated |
| EnemyReveal | ACTIVE | NORMAL (cur.) | Surfaces/EnemyRevealSurface.cs:265 |
| DecisionDock | ACTIVE | NORMAL (cur.) | ModalFallback.2.DecisionDock.cs:217 + 3 surface gates (DamagePreview/DamageTooltip/DecisionDock) |
| UseBars | ACTIVE | POWER | Surfaces/UseBarsSurface.cs:1636; off = mid-scenario decisions unreachable except via rescue chord — a footgun, not a preference |
| DoomPicker | ACTIVE | POWER | Surfaces/FloatingDecisionSurfaces.cs:126; deadlock insurance, off = rule-engine deadlock risk |
| DistributePanel | ACTIVE | POWER | Surfaces/FloatingDecisionSurfaces.cs:159; same deadlock-insurance class |
| TrayNativeControls | ACTIVE | POWER | Surfaces/TrayControlDockSurface.cs:143; deliberate default-OFF (accepts flicker) — an expert trade-off |
| ActorBars | ACTIVE | NORMAL (cur.) | ActorBars.cs:294 |
| BarFixedSize | ACTIVE | UNCERTAIN | ActorBars.cs:354; comfort toggle with an understandable label, but third-order behavior next to the curated size trio |
| BarSizeScale | ACTIVE | NORMAL (cur.) | ActorBars.cs:360; family head of the bar-size trio |
| BarZoomMinScale | ACTIVE | NORMAL (cur.) | ActorBars.cs:361; Min/Max clamp pair (flagged once) |
| BarZoomMaxScale | ACTIVE | NORMAL (cur.) | ActorBars.cs:362 |
| WristHud | ACTIVE | NORMAL | WristHud.cs:246; panel visibility toggle, not curated |
| FlatScreen | ACTIVE | POWER | FlatScreen.4.Lifecycle.cs:85/198; kill switch — off = menus unreachable |
| Tooltips | ACTIVE | NORMAL | WorldTooltips.cs:940; "Tooltips am Finger" is a plain preference, not curated |
| ActionElementHints | ACTIVE | NORMAL (cur.) | WorldTooltips.cs:946 (the `ActionElementHintsEnabled` helper property itself is unused — cosmetic) |
| PanelMipBake | ACTIVE | POWER | PanelMipBake.cs:107; anti-aliasing bake kill switch, default on |
| ForceMouseMode | ACTIVE | POWER | InputModeGuard.cs:36; input-plumbing safeguard |
| CanvasScaleMm | ACTIVE | POWER | 10+ read sites (CanvasConversion, ActorBars, GrabbableModal, ModalFallback, Net/VersionDialog); global mm/px constant — changing it rescales everything |
| InitiativeDepthMaxSpreadPx | ACTIVE | POWER | Surfaces/TablePanelSurfaces.cs:1191; px fine-tuning of a depth effect |
| HoverInfoScale | ACTIVE | NORMAL | Read via `HoverInfoScaleLive()` → PropInfoSurface.cs:286 + WorldTooltips.cs:993; user-requested size dial, NOT curated yet; also MP-sampled (Net/BoardTuning.cs:334) |
| EnemyRevealBoardClearance | ACTIVE | POWER | Surfaces/EnemyRevealSurface.cs:687; metre offset fine-tuning |
| FlatScreenAutoShow | ACTIVE | POWER | FlatScreen.4.Lifecycle.cs:98; off = main menu invisible until rescue chord (footgun) |
| DesktopMirrorLeftEye | ACTIVE | UNCERTAIN | FlatScreen.1.Core.cs:231; understandable label, but only affects the desktop monitor |
| WristHudPitch | DEAD | — | Retired marker "LEGACY — no effect" at bind (WorldUIConfig.cs:534); zero readers in src/ (grep `WorldUIConfig.WristHudPitch` = 0 hits outside bind); family flagged once for all six |
| WristHudYaw | DEAD | — | Same retired block; zero readers |
| WristHudRoll | DEAD | — | Same retired block; zero readers |
| WristHudOffsetX | DEAD | — | Same retired block; zero readers |
| WristHudOffsetY | DEAD | — | Same retired block; zero readers |
| WristHudOffsetZ | DEAD | — | Same retired block; zero readers |
| ShowIntro | ACTIVE | NORMAL | FlatScreen.1.Core.cs:420; plain preference |
| ScreenWidth | ACTIVE | NORMAL | FlatScreen.5.Placement.cs:106, FlatScreenStereo.2.Compositor.cs:301; "screen size in m" is standard VR-app vocabulary |
| ScreenDistance | ACTIVE | NORMAL | FlatScreen.5.Placement.cs:32 + Compositor; pairs with ScreenWidth |
| ClickLatch | ACTIVE | POWER | FlatScreen.6.Pointer.cs:209/461; input internals, default on for a reason |
| SuppressPhysicalMouse | ACTIVE | POWER | VirtualMouseBridge.cs:421; troubleshooting switch |
| MapWindOpacity | ACTIVE | UNCERTAIN | FlatScreenStereo.3.Map.cs:1228; a 0..1 style dial, but exists to fix a capture artifact |
| DragUnlockDegrees | ACTIVE | POWER | FlatScreen.6.Pointer.cs:107; latch tuning pair with DragUnlockSeconds (flagged once) |
| DragUnlockSeconds | ACTIVE | POWER | FlatScreen.6.Pointer.cs:113 |
| PokeClick | ACTIVE | NORMAL | FlatScreen.6.Pointer.cs:311; "Antippen klickt" — input choice a player understands |
| PokePressDepthMm | ACTIVE | POWER | Hands/Interact/PokeInteractor.cs:121; mm fine-tuning |
| DecisionPokeDeliberate | ACTIVE | NORMAL | Hands/Interact/PokeInteractor.cs:207; accidental-press guard, self-explanatory label |
| ClickMode | ACTIVE | POWER | Via `ExecuteClicks`/`VirtualMouseButtons` (FlatScreen.6.Pointer.cs:127/215/246/365/465/489); "both" is explicitly diagnostic |
| PanelsFollowView | ACTIVE | POWER | PanelLayout.cs:97; works, but description brands it LEGACY pre-test-#8 behavior — retirement candidate rather than menu material |
| HexHintFollowView | ACTIVE | NORMAL | HexHintFacing.cs:132; behavior toggle with a clear label |
| HexHintDistance | ACTIVE | POWER | HexHintFacing.cs:51; offset trio with Drop/Side (flagged once) |
| HexHintDrop | ACTIVE | POWER | HexHintFacing.cs:53 |
| HexHintSide | ACTIVE | POWER | HexHintFacing.cs:55 |
| CombatLogFollowSeat (field `CombatLogFollow`) | ACTIVE | POWER | CombatLogSurface.cs:262/310/505-518; flipped by the panel's own pin button — a menu row duplicates a physical control |
| CombatLogForward | ACTIVE | POWER | CombatLogSurface.cs:270/559; grab-persisted layout STATE, not a setting (family with Right/Up/Scale, flagged once) |
| CombatLogRight | ACTIVE | POWER | CombatLogSurface.cs:268/557 |
| CombatLogUp | ACTIVE | POWER | CombatLogSurface.cs:269/558 |
| CombatLogScale | ACTIVE | POWER | CombatLogSurface.cs:281/386/560; written by two-hand resize |
| CombatLogUserClosed | ACTIVE | POWER | CombatLogSurface.cs:72/99/104; persisted UI state (X button), should never be a menu row |
| ModalStyle | ACTIVE | POWER | Via `ModalWindowStyle` — 7 sites (ModalFallback.4.Tick.cs:494/693, .10.CatchAll.cs:537/544, .11.PreConvertHide.cs:124/328) |
| ManualScreenChord | ACTIVE | POWER | FlatScreen.4.Lifecycle.cs:148; the universal rescue — disabling it is a footgun |
| ManualScreenChordSeconds | ACTIVE | POWER | FlatScreen.4.Lifecycle.cs:163, ModalFallback.7.Close.cs:43 |
| DemoteOverlaySolidClears | ACTIVE | POWER | FlatScreen.2.CameraStack.cs:464; render-pipeline internals |
| ScreenLayerSplit | ACTIVE | POWER | FlatScreen.2.CameraStack.cs:149/250; stereo-compositor internals with auto-fallback |
| LoadingIndicator | ACTIVE | NORMAL | LoadingIndicator.cs:321; comfort toggle, clear label |
| CatchAllModals | ACTIVE | POWER | ModalFallback.10.CatchAll.cs:176; deadlock insurance |
| MenuPopupFloat | ACTIVE | POWER | ModalFallback.10.CatchAll.cs:534; deadlock insurance |
| Keyboard/Enabled (field `KeyboardEnabled`) | ACTIVE | NORMAL (cur.) | VRKeyboard.cs:118/199 |
| Keyboard/AutoCapitalise (field `KeyboardAutoCase`) | ACTIVE | NORMAL (cur.) | VRKeyboard.cs:543 |
| DevShowAllPanels | ACTIVE | POWER | DevPanels.cs:31; gated on Plugin.DevMode |
| DevForceConvert | ACTIVE | POWER | WorldUIConfig.cs `ConversionActive` (gated on Plugin.DevMode); dev-only |

## src/GloomhavenVR/WorldUI/ButtonTuning.cs (46 live binds + 8 one-time migration sources)

Every live entry is read through a clamped accessor in the same file; all accessors have live
consumers (PlayTray.1.Core/.6.Build/.7.Nested, ButtonCluster, RestControls, NativeButtonSkin,
Net/RemoteBoardFurniture) and nearly every entry is additionally MP-wire-sampled by
Net/BoardTuning.cs (extension record 28) — so the whole file is ACTIVE. Families are flagged once.

| Key | Status | Audience | Evidence/Note |
|---|---|---|---|
| [RoundButtons] OffsetX/OffsetY/OffsetZ | ACTIVE | POWER | Family: skip-key position; `TransientOffset` → ButtonCluster.cs:749; wire-sampled Net/BoardTuning.cs:252-256 |
| [RoundButtons] Shape | ACTIVE | POWER | `TransientRound` → ButtonCluster (3 sites); wire-sampled :459 |
| [RoundButtons] CapSize | ACTIVE | POWER | `TransientCapRadius` → ButtonCluster (2 sites) |
| [RoundButtons] Width/Height/Depth | ACTIVE | POWER | `RoundCapWidth/Height/Depth` → ButtonCluster (3 sites each) |
| [RoundButtons] Travel | ACTIVE | POWER | `RoundCapTravel` → ButtonCluster.cs:998 |
| [BoardButtons] Width/Height/Depth/Travel | ACTIVE | POWER | Family: Confirm/Undo caps; `BoardCap*` → Cards/PlayTray.6.Build.cs:321-323 |
| [BoardDashboard] PinWidth/Height/Depth/Travel | ACTIVE | POWER | Family: gear+Fixiert plates; `Dashboard*` → Cards/PlayTray.1.Core.cs:950-953 |
| [RestButtons] Width/Height/Depth/Travel | ACTIVE | POWER | Family: rest keycaps; `RestCap*` → RestControls (2-5 sites each) |
| [ButtonColors] LabelR/G/B | ACTIVE | POWER | `LabelColor` → NativeButtonSkin (5 sites); user framed it as "a debug option"; wire-sampled :175 |
| [ButtonColors] LabelOutline, LabelOutlineR/G/B, LabelOutlineWidth, LabelUnderlay | ACTIVE | POWER | `LabelOutlineColor/Width/Enabled`, `LabelUnderlayEnabled` → NativeButtonSkin.cs:234 etc.; wire-sampled :178/:419/:464/:466 |
| [ButtonColors] BoardCapTintR/G/B | ACTIVE | POWER | `BoardCapTint` via `CapTint()` → PlayTray.7.Nested.cs:836; wire-sampled :181 |
| [ButtonColors] DashCapTintR/G/B | ACTIVE | POWER | `DashCapTint`; wire-sampled :184 |
| [ButtonColors] ClusterCapTintR/G/B | ACTIVE | POWER | `ClusterCapTint` → ButtonCluster (3 sites); wire-sampled :187 |
| [ButtonColors] RestCapTintR/G/B | ACTIVE | POWER | `RestCapTint`; wire-sampled :190 |
| [ButtonAnim] Enable | ACTIVE | NORMAL | `ButtonAnimEnabled` (5 consumer sites: BoardButton, ButtonCluster, Net/RemoteCapFx); "Tasten-Animation" is a plain style toggle |
| [ButtonAnim] AppearParticles | ACTIVE | POWER | `AppearParticlesEnabled` (3 sites); second-order detail under Enable |
| [ButtonAnim] DisappearSeconds/AppearSeconds | ACTIVE | POWER | `DissolveSeconds`/`AppearSeconds` (7/8 sites); duration fine-tuning |
| [TransientButtons] OffsetX/OffsetY/Shape/CapSize | LEGACY-SEED | — | Family: bound only inside `MigrateLegacy` (ButtonTuning.cs:407-411) when the legacy section exists; values copied into [RoundButtons] once, entries removed from cfg; on ConfigCatalog `NotOffered` list |
| [SquareCaps] Width/Height/Depth/Travel | LEGACY-SEED | — | Family: same one-time migration (ButtonTuning.cs:412-415 → Copy into Round/Board/Dash); removed after seeding; `NotOffered` |

## DEAD (evidence)

- `[WorldUI] WristHudPitch/Yaw/Roll/OffsetX/OffsetY/OffsetZ` (6) — bound with the project's own
  "LEGACY — no effect" retirement prefix (WorldUIConfig.cs:534-549); full-src grep for each field
  finds no reader; superseded by `[WristHud] {Style}Palm*` in hands.cfg; ConfigCatalog's
  `RetiredMarkers` already keeps them out of every menu view, and Loc.ConfigNames deliberately
  dropped their captions. Nothing for the menu overhaul to do except never resurrect them.

That is the complete DEAD list — every other bind in both files has a live runtime reader.
No SUSPECT entries: the one candidate, `PanelsFollowView`, is genuinely read and functional
(PanelLayout.cs:97); it is listed ACTIVE with a legacy note instead.

## NORMAL candidates (reachable outside the debug view)

Already curated (12 — no action): ButtonCluster, CombatLog, Dialogs, EnemyReveal, DecisionDock,
ActorBars, BarSizeScale, BarZoomMinScale, BarZoomMaxScale, ActionElementHints, Keyboard/Enabled,
Keyboard/AutoCapitalise.

Not yet curated (16 — promotion candidates):

- `WorldUI/InitiativeTrack`, `ElementBoard`, `Objectives`, `StatPanels`, `PropInfoCards`,
  `WristHud` — the remaining panel-visibility toggles; same class as the curated ones.
- `WorldUI/Tooltips` — fingertip tooltips on/off.
- `WorldUI/HoverInfoScale` — the user explicitly asked for this dial ("Größe der Infotafeln …
  einstellen können"); it sits only in the debug view today.
- `WorldUI/ShowIntro` — intro in VR yes/no.
- `WorldUI/ScreenWidth`, `ScreenDistance` — floating-screen size/distance, standard VR vocabulary.
- `WorldUI/PokeClick` — poke-to-click on the flat screen.
- `WorldUI/DecisionPokeDeliberate` — firm-press guard on decision buttons.
- `WorldUI/HexHintFollowView` — hover hint follows gaze (its three offsets stay POWER).
- `WorldUI/LoadingIndicator` — loading spinner in the HMD.
- `ButtonAnim/Enable` — keycap dust animation on/off (its particles/durations stay POWER).

## UNCERTAIN (one German question each)

- `WorldUI/BarFixedSize` — Soll "Lebensbalken: Abstand ignorieren" neben Größe/Min/Max im
  Alltagsmenü stehen, oder reicht dir diese Feinheit im Debug-Bereich?
- `WorldUI/DesktopMirrorLeftEye` — Ist "Monitor zeigt linkes Auge" für dich eine Alltagsoption
  (Zuschauer/Streaming am Monitor) oder ein Technik-Detail fürs Debug-Menü?
- `WorldUI/MapWindOpacity` — Soll die Wolken-Deckkraft der Weltkarte eine normale Anzeigeoption
  werden oder als Feintuning im Debug-Menü bleiben?

## Side observations for the overhaul

- `WorldUIConfig.ActionElementHintsEnabled` (helper property, line 733) has zero consumers —
  WorldTooltips reads the entry directly; trivially deletable.
- ButtonTuning.cs:519 carries an orphaned doc comment for a deleted `DashboardGearWidth` accessor
  (`/// [BoardDashboard] settings-gear plate width (gear ONLY)`) with no member under it.
- Loc.ConfigNames.cs names seven `WorldUI/*` keys that WorldUIConfig does not bind (BarsOccluded,
  StereoScreen, ScreenDepthStrength, VideoDepthLayer, VideoDepth, ScreenParallaxScale,
  MapAlbedoRender) — they are bound elsewhere (FlatScreenStereo binds into the same cfg via
  `WorldUIConfig.Master.ConfigFile`, FlatScreenStereo.2.Compositor.cs:35) and belong to a
  different audit slice.
