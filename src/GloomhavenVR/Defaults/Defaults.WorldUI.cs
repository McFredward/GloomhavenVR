// GENERATED-AND-HAND-EDITABLE — the single home of every shipped config default.
//
// WHY THIS FILE EXISTS
// --------------------
// The mod binds ~550 config entries across 18 module config classes. Their default values
// were literals at the Bind call, which meant re-basing the shipped defaults onto a tuned
// setup was a hunt through the whole codebase. They all live here now, one per line, each
// tagged with the config identity it feeds:
//
//     internal const float TrayForward = 0.77584f;   // => [Cards] TrayForward
//
// The `// => [Section] Key` annotation is the machine-readable part: scripts/rebase-defaults.py
// finds an entry by it, so a line may move but its annotation must stay exact.
//
// RULES
//   * one entry per line, initialiser a LITERAL (or a `new Vector3(...)`/`new Color(...)` of
//     literals) — never an expression that reads another entry;
//   * `const` wherever C# allows it, so the compiler inlines it and the compiled form is
//     identical to the old literal-at-the-bind;
//   * `static readonly` only for Vector2/Vector3/Color, which cannot be const;
//   * the *_ByBoard / *_ByStyle arrays exist so a per-variant bind inside a loop can index
//     them; they are assembled from the named entries above them and hold no literals.
//
// Editing: change the number, rebuild. Or drop a tuned cfg into .planning/debug/default/ and
// run `python3 scripts/rebase-defaults.py apply`.


using UnityEngine;
using GloomhavenVR.WorldUI;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- WorldUI/ActorBars.cs ------------------------------------------------------
    internal const bool BarsOccluded = true;  // => [WorldUI] BarsOccluded

    // ---- WorldUI/ButtonTuning.cs ---------------------------------------------------
    internal const float RoundButtons_OffsetX = -0.045f;                                // => [RoundButtons] OffsetX
    internal const float RoundButtons_OffsetY = 0.26f;                                  // => [RoundButtons] OffsetY
    internal const float OffsetZ = 0.025f;                                              // => [RoundButtons] OffsetZ
    internal const Cards.ButtonShape RoundButtons_Shape = Cards.ButtonShape.Square;     // => [RoundButtons] Shape
    internal const float RoundButtons_CapSize = 0.042f;                                 // => [RoundButtons] CapSize
    internal const float RoundButtons_Width = 0.089f;                                   // => [RoundButtons] Width
    internal const float RoundButtons_Height = 0.035f;                                  // => [RoundButtons] Height
    internal const float RoundButtons_Depth = 0.014f;                                   // => [RoundButtons] Depth
    internal const float RoundButtons_Travel = 0.008f;                                  // => [RoundButtons] Travel
    internal const float BoardButtons_Width = 0.063f;                                   // => [BoardButtons] Width
    internal const float BoardButtons_Height = 0.065f;                                  // => [BoardButtons] Height
    internal const float BoardButtons_Depth = 0.014f;                                   // => [BoardButtons] Depth
    internal const float BoardButtons_Travel = 0.004f;                                  // => [BoardButtons] Travel
    internal const float PinWidth = 0.068f;                                             // => [BoardDashboard] PinWidth
    internal const float BoardDashboard_Height = 0.035f;                                // => [BoardDashboard] Height
    internal const float BoardDashboard_Depth = 0.014f;                                 // => [BoardDashboard] Depth
    internal const float BoardDashboard_Travel = 0.004f;                                // => [BoardDashboard] Travel
    internal const float RestButtons_Width = 0.105f;                                    // => [RestButtons] Width
    internal const float RestButtons_Height = 0.105f;                                   // => [RestButtons] Height
    internal const float RestButtons_Depth = 0.012f;                                    // => [RestButtons] Depth
    internal const float RestButtons_Travel = 0.004f;                                   // => [RestButtons] Travel
    internal const float LabelR = 0.984f;                                               // => [ButtonColors] LabelR
    internal const float LabelG = 0.953f;                                               // => [ButtonColors] LabelG
    internal const float LabelB = 0.878f;                                               // => [ButtonColors] LabelB
    internal const bool LabelOutline = true;                                            // => [ButtonColors] LabelOutline
    internal const float LabelOutlineR = 0.5f;                                          // => [ButtonColors] LabelOutlineR
    internal const float LabelOutlineG = 0.5f;                                          // => [ButtonColors] LabelOutlineG
    internal const float LabelOutlineB = 0.5f;                                          // => [ButtonColors] LabelOutlineB
    internal const float LabelOutlineWidth = 0.20f;                                     // => [ButtonColors] LabelOutlineWidth
    internal const bool LabelUnderlay = true;                                           // => [ButtonColors] LabelUnderlay
    internal const float BoardCapTintR = 0.5f;                                          // => [ButtonColors] BoardCapTintR
    internal const float BoardCapTintG = 0.5f;                                          // => [ButtonColors] BoardCapTintG
    internal const float BoardCapTintB = 0.5f;                                          // => [ButtonColors] BoardCapTintB
    internal const float DashCapTintR = 0.5f;                                           // => [ButtonColors] DashCapTintR
    internal const float DashCapTintG = 0.5f;                                           // => [ButtonColors] DashCapTintG
    internal const float DashCapTintB = 0.5f;                                           // => [ButtonColors] DashCapTintB
    internal const float ClusterCapTintR = 0.5f;                                        // => [ButtonColors] ClusterCapTintR
    internal const float ClusterCapTintG = 0.5f;                                        // => [ButtonColors] ClusterCapTintG
    internal const float ClusterCapTintB = 0.5f;                                        // => [ButtonColors] ClusterCapTintB
    internal const float RestCapTintR = 0.5f;                                           // => [ButtonColors] RestCapTintR
    internal const float RestCapTintG = 0.5f;                                           // => [ButtonColors] RestCapTintG
    internal const float RestCapTintB = 0.5f;                                           // => [ButtonColors] RestCapTintB
    internal const bool Enable = true;                                                  // => [ButtonAnim] Enable
    internal const bool AppearParticles = true;                                         // => [ButtonAnim] AppearParticles
    internal const float DisappearSeconds = 0.16f;                                      // => [ButtonAnim] DisappearSeconds
    internal const float AppearSeconds = 0.15f;                                         // => [ButtonAnim] AppearSeconds
    internal const float TransientButtons_OffsetX = 0f;                                 // => [TransientButtons] OffsetX
    internal const float TransientButtons_OffsetY = 0f;                                 // => [TransientButtons] OffsetY
    internal const Cards.ButtonShape TransientButtons_Shape = Cards.ButtonShape.Round;  // => [TransientButtons] Shape
    internal const float TransientButtons_CapSize = 0.042f;                             // => [TransientButtons] CapSize
    internal const float SquareCaps_Width = 0f;                                         // => [SquareCaps] Width
    internal const float SquareCaps_Height = 0f;                                        // => [SquareCaps] Height
    internal const float SquareCaps_Depth = 0f;                                         // => [SquareCaps] Depth
    internal const float SquareCaps_Travel = 0f;                                        // => [SquareCaps] Travel

    // ---- WorldUI/FlatScreenStereo.2.Compositor.cs ----------------------------------
    internal const bool StereoScreen = true;               // => [WorldUI] StereoScreen
    internal const float ScreenDepthStrength = 1.0f;       // => [WorldUI] ScreenDepthStrength
    internal const bool VideoDepthLayer = true;            // => [WorldUI] VideoDepthLayer
    internal const float VideoDepth = 0.8f;                // => [WorldUI] VideoDepth
    internal const float ScreenParallaxScale = 6.0f;       // => [WorldUI] ScreenParallaxScale
    internal const bool ScreenLeftMirrorFallback = true;   // => [WorldUI] ScreenLeftMirrorFallback
    internal const bool MapAlbedoRender = true;            // => [WorldUI] MapAlbedoRender
    internal const bool MapAlbedoOriginalMaterial = true;  // => [WorldUI] MapAlbedoOriginalMaterial
    internal const float MapAlbedoAmbient = 4.0f;          // => [WorldUI] MapAlbedoAmbient
    internal const bool MapAlbedoLight = true;             // => [WorldUI] MapAlbedoLight
    internal const int MapCaptureMode = 1;                 // => [WorldUI] MapCaptureMode
    internal const bool MapStripBeautify = true;           // => [WorldUI] MapStripBeautify
    internal const bool MapStripVolumetricFog = true;      // => [WorldUI] MapStripVolumetricFog
    internal const bool MapStripSSAO = true;               // => [WorldUI] MapStripSSAO
    internal const bool MapStripPostProcess = true;        // => [WorldUI] MapStripPostProcess
    internal const bool MapTexFlipX = false;               // => [WorldUI] MapTexFlipX
    internal const bool MapTexFlipY = true;                // => [WorldUI] MapTexFlipY
    internal const bool MapTexSwapDiag = false;            // => [WorldUI] MapTexSwapDiag
    internal const bool MapStripAllImageEffects = false;   // => [WorldUI] MapStripAllImageEffects
    internal const int MapUvSource = 0;                    // => [WorldUI] MapUvSource
    internal const bool MapUvSwapUV = false;               // => [WorldUI] MapUvSwapUV
    internal const bool MapUvFlipU = false;                // => [WorldUI] MapUvFlipU
    internal const bool MapUvFlipV = false;                // => [WorldUI] MapUvFlipV
    internal const int MapUvChannel = 0;                   // => [WorldUI] MapUvChannel
    internal const int MapUvComponent = 0;                 // => [WorldUI] MapUvComponent

    // ---- WorldUI/WorldUIConfig.cs --------------------------------------------------
    internal const bool Master = true;                       // => [WorldUI] Master
    internal const bool ButtonCluster = true;                // => [WorldUI] ButtonCluster
    internal const bool InitiativeTrack = true;              // => [WorldUI] InitiativeTrack
    internal const bool ElementBoard = true;                 // => [WorldUI] ElementBoard
    internal const bool CombatLog = true;                    // => [WorldUI] CombatLog
    internal const bool Objectives = true;                   // => [WorldUI] Objectives
    internal const bool Dialogs = true;                      // => [WorldUI] Dialogs
    internal const bool StatPanels = true;                   // => [WorldUI] StatPanels
    internal const bool PropInfoCards = true;                // => [WorldUI] PropInfoCards
    internal const bool EnemyReveal = true;                  // => [WorldUI] EnemyReveal
    internal const bool DecisionDock = true;                 // => [WorldUI] DecisionDock
    internal const bool DoomPicker = true;                   // => [WorldUI] DoomPicker
    internal const bool DistributePanel = true;              // => [WorldUI] DistributePanel
    internal const bool TrayNativeControls = true;           // => [WorldUI] TrayNativeControls
    internal const bool ActorBars = true;                    // => [WorldUI] ActorBars
    internal const bool BarFixedSize = true;                 // => [WorldUI] BarFixedSize
    internal const bool WristHud = true;                     // => [WorldUI] WristHud
    internal const bool FlatScreen = true;                   // => [WorldUI] FlatScreen
    internal const bool Tooltips = true;                     // => [WorldUI] Tooltips
    internal const bool ActionElementHints = true;           // => [WorldUI] ActionElementHints
    internal const bool ForceMouseMode = true;               // => [WorldUI] ForceMouseMode
    internal const float CanvasScaleMm = 1.0f;               // => [WorldUI] CanvasScaleMm
    internal const float InitiativeDepthMaxSpreadPx = 15f;   // => [WorldUI] InitiativeDepthMaxSpreadPx
    internal const float DecisionRowGapPx = 24f;             // => [WorldUI] DecisionRowGapPx
    internal const float HoverInfoScale = 0.6f;              // => [WorldUI] HoverInfoScale
    internal const float EnemyRevealBoardClearance = 0.10f;  // => [WorldUI] EnemyRevealBoardClearance
    internal const bool FlatScreenAutoShow = true;           // => [WorldUI] FlatScreenAutoShow
    internal const bool DesktopMirrorLeftEye = true;         // => [WorldUI] DesktopMirrorLeftEye
    internal const float WristHudPitch = -102f;              // => [WorldUI] WristHudPitch
    internal const float WristHudYaw = -180f;                // => [WorldUI] WristHudYaw
    internal const float WristHudRoll = 0f;                  // => [WorldUI] WristHudRoll
    internal const float WristHudOffsetX = 0.02f;            // => [WorldUI] WristHudOffsetX
    internal const float WristHudOffsetY = -0.143f;          // => [WorldUI] WristHudOffsetY
    internal const float WristHudOffsetZ = 0.05f;            // => [WorldUI] WristHudOffsetZ
    internal const bool ShowIntro = true;                    // => [WorldUI] ShowIntro
    internal const float ScreenWidth = 2.2f;                 // => [WorldUI] ScreenWidth
    internal const float ScreenDistance = 1.6f;              // => [WorldUI] ScreenDistance
    internal const bool ClickLatch = true;                   // => [WorldUI] ClickLatch
    internal const bool SuppressPhysicalMouse = true;        // => [WorldUI] SuppressPhysicalMouse
    internal const float MapWindOpacity = 0.3f;              // => [WorldUI] MapWindOpacity
    internal const float DragUnlockDegrees = 2.0f;           // => [WorldUI] DragUnlockDegrees
    internal const float DragUnlockSeconds = 0.15f;          // => [WorldUI] DragUnlockSeconds
    internal const bool PokeClick = true;                    // => [WorldUI] PokeClick
    internal const float PokePressDepthMm = 12f;             // => [WorldUI] PokePressDepthMm
    internal const bool DecisionPokeDeliberate = true;       // => [WorldUI] DecisionPokeDeliberate
    internal const string ClickMode = "execute";             // => [WorldUI] ClickMode
    internal const bool CombatLogFollowSeat = false;         // => [WorldUI] CombatLogFollowSeat
    internal const float CombatLogForward = -0.234156f;      // => [WorldUI] CombatLogForward
    internal const float CombatLogRight = 0.941762f;         // => [WorldUI] CombatLogRight
    internal const float CombatLogUp = 0.458258f;            // => [WorldUI] CombatLogUp
    internal const float CombatLogScale = 0.67339f;          // => [WorldUI] CombatLogScale
    internal const bool CombatLogUserClosed = true;          // => [WorldUI] CombatLogUserClosed
    internal const bool PanelsFollowView = false;            // => [WorldUI] PanelsFollowView  (legacy: read once as the seed for its successor)
    internal const bool HexHintFollowView = true;            // => [WorldUI] HexHintFollowView
    internal const string ModalStyle = "window";             // => [WorldUI] ModalStyle
    internal const bool ManualScreenChord = true;            // => [WorldUI] ManualScreenChord
    internal const float ManualScreenChordSeconds = 2f;      // => [WorldUI] ManualScreenChordSeconds
    internal const bool DemoteOverlaySolidClears = true;     // => [WorldUI] DemoteOverlaySolidClears
    internal const bool ScreenLayerSplit = true;             // => [WorldUI] ScreenLayerSplit
    internal const bool LoadingIndicator = true;             // => [WorldUI] LoadingIndicator
    internal const bool Keyboard_Enabled = true;             // => [Keyboard] Enabled
    internal const bool AutoCapitalise = true;               // => [Keyboard] AutoCapitalise
    internal const bool DevShowAllPanels = false;            // => [WorldUI] DevShowAllPanels
    internal const bool DevForceConvert = false;             // => [WorldUI] DevForceConvert
}
