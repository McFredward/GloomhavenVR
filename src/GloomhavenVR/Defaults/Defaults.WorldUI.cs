// Shipped config defaults — one entry per line. The full rules, and the reason this family
// exists, live at the top of Defaults.Core.cs; they are deliberately not restated here.
//
// The trailing `// => [Section] Key` annotation is the machine-readable part:
// scripts/rebase-defaults.py finds an entry by it, so a line may move but its annotation
// must stay exact.


using UnityEngine;
using GloomhavenVR.WorldUI;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- WorldUI/ActorBars.cs ------------------------------------------------------
    internal const bool BarsOccluded = false;  // => [WorldUI] BarsOccluded

    // ---- WorldUI/ButtonTuning.cs ---------------------------------------------------
    internal const float RoundButtons_OffsetX = -0.045f;                                // => [RoundButtons] OffsetX
    internal const float RoundButtons_OffsetY = 0.26f;                                  // => [RoundButtons] OffsetY
    internal const float OffsetZ = 0.005f;                                              // => [RoundButtons] OffsetZ
    internal const Cards.ButtonShape RoundButtons_Shape = Cards.ButtonShape.Square;     // => [RoundButtons] Shape
    internal const float RoundButtons_CapSize = 0.042f;                                 // => [RoundButtons] CapSize
    internal const float RoundButtons_Width = 0.089f;                                   // => [RoundButtons] Width
    internal const float RoundButtons_Height = 0.035f;                                  // => [RoundButtons] Height
    internal const float RoundButtons_Depth = 0.015f;                                   // => [RoundButtons] Depth
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

    // ---- WorldUI/ModalFallback.9.Spawn.cs ------------------------------------------
    // ModBuild 190. THIS CONSTANT HAS TO LIVE HERE, not beside its code, and the reason is a
    // workflow and not a style rule: dropped .cfg values are always read against the NEWEST build
    // (standing user practice), and `scripts/rebase-defaults.py` maps a tuned key back to its
    // shipped default THROUGH THIS FILE. A default declared anywhere else is reported UNMAPPED and
    // the tuned value is silently not applied.
    internal const float WindowLegibility = 1.25f;          // => [WorldUI] WindowLegibility

    // ---- WorldUI/FlatScreenStereo.2.Compositor.cs ----------------------------------
    internal const bool StereoScreen = true;               // => [WorldUI] StereoScreen
    internal const float ScreenDepthStrength = 1.0f;       // => [WorldUI] ScreenDepthStrength
    internal const bool VideoDepthLayer = true;            // => [WorldUI] VideoDepthLayer
    internal const float VideoDepth = 0.8f;                // => [WorldUI] VideoDepth
    internal const float ScreenParallaxScale = 6.0f;       // => [WorldUI] ScreenParallaxScale

    // ---- WorldUI/WorldUIConfig.cs --------------------------------------------------
    // ROUND 2 OF THE SETTINGS AUDIT (user ruling 2026-08-13) removed sixteen defaults from this
    // file with their dials: InitiativeTrack, ElementBoard, Objectives, StatPanels,
    // PropInfoCards, EnemyReveal, ActorBars, Tooltips, ActionElementHints (readouts the flat
    // game always shows, whose OFF released the panel to its 2D home = invisible in VR),
    // ClickLatch, ClickMode, ForceMouseMode (the documented no-click failure modes),
    // DemoteOverlaySolidClears, ScreenLeftMirrorFallback, MapAlbedoRender (each OFF = a black
    // campaign map) and Keyboard_Enabled (a text field with no way to type). See the tombstone
    // in WorldUI/WorldUIConfig.cs.
    // Master, UseBars, DoomPicker, DistributePanel, FlatScreen, FlatScreenAutoShow,
    // CatchAllModals, MenuPopupFloat and ManualScreenChord are GONE (user ruling 2026-08-11:
    // essential — those features are unconditional now; see WorldUIConfig.cs).
    internal const bool ButtonCluster = true;                // => [WorldUI] ButtonCluster
    internal const bool CombatLog = true;                    // => [WorldUI] CombatLog
    internal const bool Dialogs = true;                      // => [WorldUI] Dialogs
    internal const bool DecisionDock = true;                 // => [WorldUI] DecisionDock
    internal const bool TrayNativeControls = true;           // => [WorldUI] TrayNativeControls
    internal const bool BarFixedSize = true;                 // => [WorldUI] BarFixedSize
    internal const float BarSizeScale = 0.70899f;            // => [WorldUI] BarSizeScale
    // BarZoomMinScale (0.7) and BarZoomMaxScale (1.5) stood here. GONE (user ruling 2026-08-13:
    // "Mindest und Maximalgröße der Lebensbalken haben keinen sehbaren einfluss … ziemlich
    // unintuitiv"). The two numbers survive as the CONSTANTS ActorBars.ZoomFollowMin/Max, so the
    // shipped look is bit-identical; only the two menu rows and the two cfg keys are gone.
    internal const bool WristHud = true;                     // => [WorldUI] WristHud
    internal const bool PanelMipBake = true;                 // => [WorldUI] PanelMipBake
    // ---- WorldUI/PanelSupersample.cs -----------------------------------------------
    // DEFAULT OFF for this build, deliberately: it is the largest rendering change in the mod and
    // it lands on a symptom that has survived nine hardware rounds, so the user has to be able to
    // A/B it against today's behaviour inside ONE session. OFF is byte-for-byte today's rendering
    // — no camera, no render target, no layer is touched while the switch is false.
    internal const bool PanelSupersample = false;            // => [WorldUI] PanelSupersample
    // ModBuild 199 RAISED THIS FROM 1.0, AND 1.0 WAS NEVER A SETTING — IT WAS A NO-OP.
    // At factor 1.0 the capture target is allocated at exactly the window's authored resolution, i.e.
    // one target texel per authored pixel, and nothing is band-limited. ModBuild 198 shipped a floor
    // that raised it to 2.0 — but ONLY when the live value was `Mathf.Approximately` this constant,
    // on the reasoning that a value the user set must be taken verbatim. All 153 RESAMPLE VERDICT
    // lines of the next hardware log then read `config 1.00, taken verbatim — this is a value the
    // user set`: his cfg carries a hand-written 1.0 that is not bit-equal, so THE FLOOR NEVER RAN AND
    // THE EXPERIMENT NEVER EXECUTED. The verbatim rule is right for a TUNED value; it must not
    // preserve an inherited default that provably does nothing.
    internal const float PanelSupersampleFactor = 2.0f;      // => [WorldUI] PanelSupersampleFactor
    // ModBuild 203 — THE LAST LEVER ON "die Auflösung kommt mir immer noch etwas gering vor bei den
    // Sub-Menus", and the mod's own instrument named it: the ModBuild 202 log reads the party window
    // MINIFIED 1.58x (peak 1.86x, 16 of 19 measurements minified at all) at `mipMapBias 0.00`, with
    // the capture factor confirmed at asked 2.00 / ACHIEVED 2.00. A minified window's sampler already
    // selects a mip level at or below authored resolution, so every level the FACTOR adds above it is
    // one the hardware never reads — no capture factor can reach this complaint. The two levers left
    // are the window's size in the eye (WindowLegibility) and this one.
    //
    // WHY -0.5 AND WHY IT SHIPS ON. The quantity is a MIP LEVEL offset, and after the offset the
    // sampled level carries 2^-value texels per rendered pixel: 1.41 at -0.5, 2.00 at -1.0 (which is
    // twice what a pixel grid can hold — ModBuild 192's shimmer, rebuilt by hand). -0.5 is half an
    // octave of trilinear's deliberate over-blur handed back, measured twice in this project before
    // (ModBuild 194: at LOD 0.42, 58 % of samples came from level 0; ModBuild 200: worth ~25 % of one
    // blur term) and deliberately never shipped either time so the next report stayed attributable.
    // The user has now asked about the symptom directly, so it ships, at the conservative end.
    //
    // ModBuild 204: SHIPPED VALUE TAKEN BACK TO 0.00, AND THE PARAGRAPH ABOVE IS WHY IT HAD TO BE.
    // The dial ran on hardware exactly once, and the log confirms it ran ("asked -0.50 … and the LIVE
    // display render target reads -0.50 back — asked and in force AGREE"). What nobody had checked
    // is what it does to the BAND LIMIT. Unfiltered mip level 0 re-enters the blend below
    // 2^(1-bias) texels per rendered pixel — 2.83 at -0.5, not the 2.00 the ModBuild 198 floor was
    // built to guarantee — and 34 of that session's 47 RESAMPLE VERDICT readings sit below 2.83
    // (range 1.73 … 3.03). So this dial handed back roughly half of the one fix that closed the
    // STILL case, in the middle of the round investigating the MOVING case.
    // WORSE, THE INSTRUMENT COULD NOT SEE IT: SamplingSentence computed its LOD without adding the
    // bias and then printed "BAND-LIMITED: the eye reads no unfiltered level 0 at all" on a build
    // where most samples carried level 0. That verdict was quoted to the user as evidence. The
    // arithmetic is fixed in PanelSupersample.3.Report.cs this build, and the DEFAULT goes to 0.00
    // until the drag defect is closed — attribution first, sharpness second. The dial stays, its
    // range stays, and a user-set value is still taken verbatim; only the shipped value changes.
    // This is NOT a statement that the dial is wrong. It is a statement that a filtering change and
    // a filtering investigation must not run in the same build.
    //
    // RANGE -2.0 .. 0.0. Above 0 is BLUR, which is what the mip chain already does correctly and what
    // this dial exists to undo — a positive value would only re-buy the complaint. Below -1.0 the
    // sampled level carries more detail than the pixel grid can show, so -2.0 is a floor and not a
    // recommendation: it is there so a player experimenting can reach the failure and SEE it rather
    // than wonder whether the dial does anything.
    //
    // THE KEY IS NAMED FOR ITS STEPPER, and that is not cosmetic. `ConfigSteps` deliberately does NOT
    // recognise "Bias" (its own table says so: HeldFaceBias is an angle and StableDepthBias is 0.0002,
    // one word and two units three orders of magnitude apart), and a key with no recognised unit word
    // falls through to a derivation that has shipped unusable steppers twice. "Offset" IS recognised
    // and is literally what this number is — an offset added to the computed mip LOD — and it
    // resolves to 0.05 per press, i.e. ten presses from 0 to the shipped -0.5.
    //
    // LOCAL RENDERING ONLY, so no wire field and no EXEMPT line: [WorldUI] is not one of
    // check-wire-coverage.py's BOARD_SECTIONS, and an EXEMPT entry outside those sections is reported
    // STALE by that same script. Nothing a peer can see from their side of the table changes.
    internal const float PanelMipLodOffset = 0.0f;          // => [WorldUI] PanelMipLodOffset
    internal const bool PanelRepairInheritedAlpha = true;   // => [WorldUI] PanelRepairInheritedAlpha
    internal const bool NeutraliseGrabPassBlur = true;      // => [WorldUI] NeutraliseGrabPassBlur
    internal const float CanvasScaleMm = 1.0f;               // => [WorldUI] CanvasScaleMm
    internal const float InitiativeDepthMaxSpreadPx = 15f;   // => [WorldUI] InitiativeDepthMaxSpreadPx
    internal const float HoverInfoScale = 0.6f;              // => [WorldUI] HoverInfoScale
    internal const float EnemyRevealBoardClearance = 0.10f;  // => [WorldUI] EnemyRevealBoardClearance
    internal const bool DesktopMirrorLeftEye = true;         // => [WorldUI] DesktopMirrorLeftEye
    internal const bool ShowIntro = true;                    // => [WorldUI] ShowIntro
    internal const float ScreenWidth = 2.2f;                 // => [WorldUI] ScreenWidth
    internal const float ScreenDistance = 1.6f;              // => [WorldUI] ScreenDistance
    internal const bool SuppressPhysicalMouse = true;        // => [WorldUI] SuppressPhysicalMouse
    internal const float MapWindOpacity = 0.3f;              // => [WorldUI] MapWindOpacity
    internal const float DragUnlockDegrees = 2.0f;           // => [WorldUI] DragUnlockDegrees
    internal const float DragUnlockSeconds = 0.15f;          // => [WorldUI] DragUnlockSeconds
    internal const bool PokeClick = true;                    // => [WorldUI] PokeClick
    internal const float PokePressDepthMm = 12f;             // => [WorldUI] PokePressDepthMm
    internal const bool DecisionPokeDeliberate = true;       // => [WorldUI] DecisionPokeDeliberate
    internal const bool CombatLogFollowSeat = false;         // => [WorldUI] CombatLogFollowSeat
    internal const float CombatLogForward = -0.234156f;      // => [WorldUI] CombatLogForward
    internal const float CombatLogRight = 0.941762f;         // => [WorldUI] CombatLogRight
    internal const float CombatLogUp = 0.458258f;            // => [WorldUI] CombatLogUp
    internal const float CombatLogScale = 0.67339f;          // => [WorldUI] CombatLogScale
    internal const bool CombatLogUserClosed = true;          // => [WorldUI] CombatLogUserClosed
    internal const bool PanelsFollowView = false;            // => [WorldUI] PanelsFollowView  (legacy: read once as the seed for its successor)
    internal const bool HexHintFollowView = true;            // => [WorldUI] HexHintFollowView
    internal const float HexHintDistance = 0.6f;             // => [WorldUI] HexHintDistance
    internal const float HexHintDrop = 0.12f;                // => [WorldUI] HexHintDrop
    internal const float HexHintSide = 0f;                   // => [WorldUI] HexHintSide
    internal const string ModalStyle = "window";             // => [WorldUI] ModalStyle
    internal const float ManualScreenChordSeconds = 2f;      // => [WorldUI] ManualScreenChordSeconds
    internal const bool ScreenLayerSplit = true;             // => [WorldUI] ScreenLayerSplit
    internal const bool LoadingIndicator = true;             // => [WorldUI] LoadingIndicator
    internal const bool AutoCapitalise = true;               // => [Keyboard] AutoCapitalise
    // ---- WorldUI/MapRoom/MapRoomHand.*.cs ------------------------------------------
    // DEFAULT ON by explicit user ruling (2026-08-21, ruling 4: "Das Feature soll deaktivierbar
    // sein" — deactivatable, i.e. on until switched off). Its OFF makes only OPTIONAL content
    // optional: no loadout fan and no map-room wrist plate are built, and nothing else in the map
    // room changes by so much as a transform write.
    internal const bool MapRoomHand = true;                  // => [WorldUI] MapRoomHand

    // ---- WorldUI/MapRoom/MapTravelConfirm.cs ---------------------------------------
    // BOTH ZERO IS THE POINT, not a placeholder. User ruling 2026-08-21: "Mach die Position des
    // Quest Buttons ganz rückgängig wie es das erste mal war als du den button im window hinzugefügt
    // hast. Geb mir dann im debug menu die offsets um ihm zu verschieben - ich stell es selber ein."
    // The applied offset is (X, Y) x the quest window's height, so 0 and 0 write Vector2.zero — the
    // exact anchoredPosition ModBuild 190 wrote, after the exact same anchors and pivot. Three solved
    // placements (191, 192, 193) were rejected in a row; do not "improve" either number.
    internal const float TravelButtonOffsetXWindowHeights = 0f;  // => [WorldUI] TravelButtonOffsetXWindowHeights
    internal const float TravelButtonOffsetYWindowHeights = 0f;  // => [WorldUI] TravelButtonOffsetYWindowHeights

    internal const bool DevShowAllPanels = false;            // => [WorldUI] DevShowAllPanels
    internal const bool DevForceConvert = false;             // => [WorldUI] DevForceConvert
}
