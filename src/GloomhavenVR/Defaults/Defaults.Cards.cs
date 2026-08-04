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
using GloomhavenVR.Cards;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- Cards/CardsConfig.cs ------------------------------------------------------
    internal const int DevFakeHand = 0;                                                               // => [Cards] DevFakeHand
    internal const string RevealMode = "tilt";                                                        // => [Cards] RevealMode
    internal const float RevealEnterDegrees = 70f;                                                    // => [Cards] RevealEnterDegrees
    internal const float RevealExitDegrees = 5f;                                                      // => [Cards] RevealExitDegrees
    internal const float FanRadius = 0.16f;                                                           // => [Cards] FanRadius
    internal const float FanArcDegrees = 70f;                                                         // => [Cards] FanArcDegrees  (legacy: read once as the seed for its successor)
    internal const float FanPalmOffset = 0.09f;                                                       // => [Cards] FanPalmOffset
    internal const float CardWidth = 0.0635f;                                                         // => [Cards] CardWidth
    internal const float InspectScale = 1.6f;                                                         // => [Cards] InspectScale
    internal const float Cards_HeldTiltDegrees = 20f;                                                 // => [Cards] HeldTiltDegrees  (legacy: read once as the seed for its successor)
    internal const float HeldFaceBias = 65f;                                                          // => [Cards] HeldFaceBias
    internal const float HeldForward = 0.005f;                                                        // => [Cards] HeldForward
    internal const float HeldOffPalm = 0.0148f;                                                       // => [Cards] HeldOffPalm
    internal static readonly Vector3 HeldPinchOffset = new Vector3(-0.055f, 0.035f, 0f);              // => [Cards] HeldPinchOffset
    internal const float TrayForward = 0.66609f;                                                      // => [Cards] TrayForward
    internal const float TrayDown = 0.0194236f;                                                       // => [Cards] TrayDown
    internal const float TrayRight = -0.131922f;                                                      // => [Cards] TrayRight
    internal const float TrayTilt = 30f;                                                              // => [Cards] TrayTilt  (legacy: read once as the seed for its successor)
    internal const float TrayYaw = -21.8483f;                                                         // => [Cards] TrayYaw
    internal const float TrayScale = 1.5769f;                                                         // => [Cards] TrayScale
    internal const bool TrayFollow = true;                                                            // => [Cards] TrayFollow
    internal const BoardMoveMode BoardMoveMode = Cards.BoardMoveMode.LimitedPitch;                    // => [Cards] BoardMoveMode
    internal const float TrayPitch = 23.324f;                                                         // => [Cards] TrayPitch
    internal const float BoardPitchMinDegrees = -45f;                                                 // => [Cards] BoardPitchMinDegrees  (legacy: superseded by the per-board BoardPitchMin_<board>)
    internal const float BoardPitchMaxDegrees = 45f;                                                  // => [Cards] BoardPitchMaxDegrees  (legacy: superseded by the per-board BoardPitchMax_<board>)
    internal const float CardLerpSpeed = 14f;                                                         // => [Cards] CardLerpSpeed
    internal const float SlotCardInset = 0.004f;                                                      // => [Cards] SlotCardInset
    internal const float SlotCardFill = 1.45f;                                                        // => [Cards] SlotCardFill
    internal const float RoundButtonDiameter = 0.105f;                                                // => [Cards] RoundButtonDiameter  (legacy: read once as the seed for its successor)
    internal const float RoundButtonThickness = 0.012f;                                               // => [Cards] RoundButtonThickness  (legacy: read once as the seed for its successor)
    internal const float RestButtonInsetX = 0.024f;                                                   // => [Cards] RestButtonInsetX  (legacy: read once as the seed for its successor)
    internal const float ConfirmUndoInsetX = 0.014f;                                                  // => [Cards] ConfirmUndoInsetX  (legacy: read once as the seed for its successor)
    internal const float FanStepDegrees_Items = 10f;                                                  // => [Cards] FanStepDegrees_Items
    internal const float FanStepDegrees_Discard = 10f;                                                // => [Cards] FanStepDegrees_Discard
    internal const float FanStepDegrees_Burnt = 10f;                                                  // => [Cards] FanStepDegrees_Burnt
    internal const float FanRadiusFactor_Items = 1.7f;                                                // => [Cards] FanRadiusFactor_Items
    internal const float FanRadiusFactor_Discard = 1.7f;                                              // => [Cards] FanRadiusFactor_Discard
    internal const float FanRadiusFactor_Burnt = 1.7f;                                                // => [Cards] FanRadiusFactor_Burnt
    internal const float BoardMinWidthMeters = 0.18f;                                                 // => [Cards] BoardMinWidthMeters
    internal const float BoardMaxWidthMeters = 1.4f;                                                  // => [Cards] BoardMaxWidthMeters
    internal const bool SpawnLeftOfHead = true;                                                       // => [Cards] SpawnLeftOfHead
    internal const float SpawnSideMeters = 0.45f;                                                     // => [Cards] SpawnSideMeters
    internal const float SpawnForwardMeters = 0.28f;                                                  // => [Cards] SpawnForwardMeters
    internal const float SpawnDownMeters = 0.32f;                                                     // => [Cards] SpawnDownMeters
    internal const bool GameCardParticles = false;                                                    // => [Cards] GameCardParticles
    internal const bool CardDust = false;                                                             // => [Cards] CardDust
    internal const bool WantedSlotHint = true;                                                        // => [Cards] WantedSlotHint
    internal const bool PileViewer = true;                                                            // => [Cards] PileViewer
    internal const bool ActivePile = true;                                                            // => [Cards] ActivePile
    internal const bool FaceMipBake = true;                                                           // => [Cards] FaceMipBake
    internal const ControlBoard Board = ControlBoard.Steel;                                           // => [Cards] Board
    internal static readonly Vector3 ItemUseSlotOffset_Oak = new Vector3(0f, 0f, 0f);                 // => [Cards] ItemUseSlotOffset_Oak
    internal static readonly Vector3 ItemUseSlotOffset_Steel = new Vector3(0f, 0f, 0f);               // => [Cards] ItemUseSlotOffset_Steel
    internal static readonly Vector3 ItemUseSlotOffset_Bronze = new Vector3(0f, 0f, 0f);              // => [Cards] ItemUseSlotOffset_Bronze
    internal static readonly Vector3 ItemCardOffset_Oak = new Vector3(0f, 0f, 0f);                    // => [Cards] ItemCardOffset_Oak
    internal static readonly Vector3 ItemCardOffset_Steel = new Vector3(0f, 0f, 0f);                  // => [Cards] ItemCardOffset_Steel
    internal static readonly Vector3 ItemCardOffset_Bronze = new Vector3(0f, 0f, 0f);                 // => [Cards] ItemCardOffset_Bronze
    internal const float BoardTilt_Oak = 30f;                                                         // => [Cards] BoardTilt_Oak
    internal const float BoardTilt_Steel = 30f;                                                       // => [Cards] BoardTilt_Steel
    internal const float BoardTilt_Bronze = 30f;                                                      // => [Cards] BoardTilt_Bronze
    internal const float BoardPitchMin_Oak = -45f;                                                    // => [Cards] BoardPitchMin_Oak
    internal const float BoardPitchMin_Steel = -31.067f;                                              // => [Cards] BoardPitchMin_Steel
    internal const float BoardPitchMin_Bronze = -45f;                                                 // => [Cards] BoardPitchMin_Bronze
    internal const float BoardPitchMax_Oak = 45f;                                                     // => [Cards] BoardPitchMax_Oak
    internal const float BoardPitchMax_Steel = 54.353f;                                               // => [Cards] BoardPitchMax_Steel
    internal const float BoardPitchMax_Bronze = 45f;                                                  // => [Cards] BoardPitchMax_Bronze
    internal const float BoardYaw_Oak = 0f;                                                           // => [Cards] BoardYaw_Oak
    internal const float BoardYaw_Steel = 0f;                                                         // => [Cards] BoardYaw_Steel
    internal const float BoardYaw_Bronze = 0f;                                                        // => [Cards] BoardYaw_Bronze
    internal const float BoardScale_Oak = 0.4f;                                                       // => [Cards] BoardScale_Oak
    internal const float BoardScale_Steel = 0.42055f;                                                 // => [Cards] BoardScale_Steel
    internal const float BoardScale_Bronze = 0.4f;                                                    // => [Cards] BoardScale_Bronze
    internal static readonly Vector3 AssetRotation_Oak = new Vector3(0f, 0f, 0f);                     // => [Cards] AssetRotation_Oak  (legacy: read once as the seed for its successor)
    internal static readonly Vector3 AssetRotation_Steel = new Vector3(0f, 0f, 0f);                   // => [Cards] AssetRotation_Steel  (legacy: read once as the seed for its successor)
    internal static readonly Vector3 AssetRotation_Bronze = new Vector3(0f, 0f, 0f);                  // => [Cards] AssetRotation_Bronze  (legacy: read once as the seed for its successor)
    internal const float AssetYawDegrees_Oak = 0f;                                                    // => [Cards] AssetYawDegrees_Oak
    internal const float AssetYawDegrees_Steel = 0f;                                                  // => [Cards] AssetYawDegrees_Steel
    internal const float AssetYawDegrees_Bronze = 0f;                                                 // => [Cards] AssetYawDegrees_Bronze
    internal const float AssetRollDegrees_Oak = 0f;                                                   // => [Cards] AssetRollDegrees_Oak
    internal const float AssetRollDegrees_Steel = 0f;                                                 // => [Cards] AssetRollDegrees_Steel
    internal const float AssetRollDegrees_Bronze = 0f;                                                // => [Cards] AssetRollDegrees_Bronze
    internal static readonly Vector3 BoardPosOffset_Oak = new Vector3(0f, 0f, 0f);                    // => [Cards] BoardPosOffset_Oak
    internal static readonly Vector3 BoardPosOffset_Steel = new Vector3(0f, 0f, 0f);                  // => [Cards] BoardPosOffset_Steel
    internal static readonly Vector3 BoardPosOffset_Bronze = new Vector3(0f, 0f, 0f);                 // => [Cards] BoardPosOffset_Bronze
    internal const ButtonShape RestButtonShape_Oak = ButtonShape.Round;                               // => [Cards] RestButtonShape_Oak
    internal const ButtonShape RestButtonShape_Steel = ButtonShape.Round;                             // => [Cards] RestButtonShape_Steel
    internal const ButtonShape RestButtonShape_Bronze = ButtonShape.Round;                            // => [Cards] RestButtonShape_Bronze
    internal const ButtonShape GenericButtonShape_Oak = ButtonShape.Square;                           // => [Cards] GenericButtonShape_Oak
    internal const ButtonShape GenericButtonShape_Steel = ButtonShape.Square;                         // => [Cards] GenericButtonShape_Steel
    internal const ButtonShape GenericButtonShape_Bronze = ButtonShape.Square;                        // => [Cards] GenericButtonShape_Bronze
    internal static readonly Vector2 ActiveGridSpacing_Oak = new Vector2(1.06f, 0.7f);                // => [Cards] ActiveGridSpacing_Oak
    internal static readonly Vector2 ActiveGridSpacing_Steel = new Vector2(1.06f, 0.7f);              // => [Cards] ActiveGridSpacing_Steel
    internal static readonly Vector2 ActiveGridSpacing_Bronze = new Vector2(1.06f, 0.7f);             // => [Cards] ActiveGridSpacing_Bronze
    internal const float PileScale_Oak = 1f;                                                          // => [Cards] PileScale_Oak
    internal const float PileScale_Steel = 1f;                                                        // => [Cards] PileScale_Steel
    internal const float PileScale_Bronze = 1f;                                                       // => [Cards] PileScale_Bronze
    internal const float PileSpacing_Oak = 0.116f;                                                    // => [Cards] PileSpacing_Oak
    internal const float PileSpacing_Steel = 0.116f;                                                  // => [Cards] PileSpacing_Steel
    internal const float PileSpacing_Bronze = 0.116f;                                                 // => [Cards] PileSpacing_Bronze
    internal const float ElementsScale_Oak = 1f;                                                      // => [Cards] ElementsScale_Oak
    internal const float ElementsScale_Steel = 1f;                                                    // => [Cards] ElementsScale_Steel
    internal const float ElementsScale_Bronze = 1f;                                                   // => [Cards] ElementsScale_Bronze
    internal static readonly Vector3 ClusterOffset_Oak = new Vector3(0f, 0f, 0f);                     // => [Cards] ClusterOffset_Oak
    internal static readonly Vector3 ClusterOffset_Steel = new Vector3(0f, 0f, 0f);                   // => [Cards] ClusterOffset_Steel
    internal static readonly Vector3 ClusterOffset_Bronze = new Vector3(0f, 0f, 0f);                  // => [Cards] ClusterOffset_Bronze
    internal const float ClusterScale_Oak = 1f;                                                       // => [Cards] ClusterScale_Oak
    internal const float ClusterScale_Steel = 1f;                                                     // => [Cards] ClusterScale_Steel
    internal const float ClusterScale_Bronze = 1f;                                                    // => [Cards] ClusterScale_Bronze
    internal const float DecisionScale_Oak = 1f;                                                      // => [Cards] DecisionScale_Oak
    internal const float DecisionScale_Steel = 1.6f;                                                  // => [Cards] DecisionScale_Steel
    internal const float DecisionScale_Bronze = 1f;                                                   // => [Cards] DecisionScale_Bronze
    internal const bool BoardScaleDefault04Applied = false;                                           // => [Cards] BoardScaleDefault04Applied  (pinned: one-shot migration marker — a fresh install must start false)
    internal const bool FanCurveByFill = true;                                                        // => [Cards] FanCurveByFill
    internal const int FanMaxHandForCurve = 10;                                                       // => [Cards] FanMaxHandForCurve
    internal const float FanFlatCurvatureFactor = 0.55f;                                              // => [Cards] FanFlatCurvatureFactor
    internal const float FanTiltFactor = 0.85f;                                                       // => [Cards] FanTiltFactor
    internal const float FanSplitMultiplier = 0.02f;                                                  // => [Cards] FanSplitMultiplier
    internal const float FanSplitFalloff = 1.6f;                                                      // => [Cards] FanSplitFalloff
    internal const float FanSelectedPopForward = 0.035f;                                              // => [Cards] FanSelectedPopForward
    internal const CardGrabButton GrabButton = CardGrabButton.Trigger;                                // => [Cards] GrabButton
    internal const float FanFollowSmoothing = 16f;                                                    // => [Cards] FanFollowSmoothing
    internal const float FanFollowDeadzone = 0.004f;                                                  // => [Cards] FanFollowDeadzone
    internal const bool RevealIgnoreWhenGrabbing = true;                                              // => [Cards] RevealIgnoreWhenGrabbing
    internal const float FanOpenDuration = 0.14f;                                                     // => [Cards] FanOpenDuration
    internal const float FanOpenStagger = 0.02f;                                                      // => [Cards] FanOpenStagger
    internal const float FanCloseDuration = 0.12f;                                                    // => [Cards] FanCloseDuration
    internal const string FanRevealSound = "PlaySound_EnemyCardDraw";                                 // => [Cards] FanRevealSound
    internal const string FanHideSound = "PlaySound_UICardTabSelect";                                 // => [Cards] FanHideSound
    internal const string CardGrabSound = "PlaySound_UICardTabSelect";                                // => [Cards] CardGrabSound
    internal const string CardPlaceSound = "PlaySound_CardUI_SelectCard";                             // => [Cards] CardPlaceSound
    internal const string CardTakeBackSound = "PlaySound_UIUndoHex";                                  // => [Cards] CardTakeBackSound
    internal const float FanPerCardStepDegrees = 14f;                                                 // => [Cards] FanPerCardStepDegrees
    internal const float FanArcSweepDegrees = 103f;                                                   // => [Cards] FanArcSweepDegrees
    internal const float FanEffectiveRadius = 0.2192f;                                                // => [Cards] FanEffectiveRadius
    internal const float FanHoverSplitScale = 1.4f;                                                   // => [Cards] FanHoverSplitScale
    internal static readonly Vector3 BrowseFanOffset = new Vector3(0f, -0.37f, -0.15f);               // => [Cards] BrowseFanOffset
    internal const float FanSideDepthCurve = 0f;                                                      // => [Cards] FanSideDepthCurve
    internal const float FanCurvePower = 2f;                                                          // => [Cards] FanCurvePower
    internal const int FanCurveMinCards = 3;                                                          // => [Cards] FanCurveMinCards
    internal const bool FanGazeBias = false;                                                          // => [Cards] FanGazeBias
    internal const float FanFaceViewer = 1f;                                                          // => [Cards] FanFaceViewer
    internal const float FanGazeApexFollow = 1f;                                                      // => [Cards] FanGazeApexFollow
    internal const float FanGazeSmoothing = 6f;                                                       // => [Cards] FanGazeSmoothing
    internal static readonly Vector3[] ItemUseSlotOffset_ByBoard = { ItemUseSlotOffset_Oak, ItemUseSlotOffset_Steel, ItemUseSlotOffset_Bronze };
    internal static readonly Vector3[] ItemCardOffset_ByBoard = { ItemCardOffset_Oak, ItemCardOffset_Steel, ItemCardOffset_Bronze };
    internal static readonly float[] BoardTilt_ByBoard = { BoardTilt_Oak, BoardTilt_Steel, BoardTilt_Bronze };
    internal static readonly float[] BoardPitchMin_ByBoard = { BoardPitchMin_Oak, BoardPitchMin_Steel, BoardPitchMin_Bronze };
    internal static readonly float[] BoardPitchMax_ByBoard = { BoardPitchMax_Oak, BoardPitchMax_Steel, BoardPitchMax_Bronze };
    internal static readonly float[] BoardYaw_ByBoard = { BoardYaw_Oak, BoardYaw_Steel, BoardYaw_Bronze };
    internal static readonly float[] BoardScale_ByBoard = { BoardScale_Oak, BoardScale_Steel, BoardScale_Bronze };
    internal static readonly Vector3[] AssetRotation_ByBoard = { AssetRotation_Oak, AssetRotation_Steel, AssetRotation_Bronze };
    internal static readonly float[] AssetYawDegrees_ByBoard = { AssetYawDegrees_Oak, AssetYawDegrees_Steel, AssetYawDegrees_Bronze };
    internal static readonly float[] AssetRollDegrees_ByBoard = { AssetRollDegrees_Oak, AssetRollDegrees_Steel, AssetRollDegrees_Bronze };
    internal static readonly Vector3[] BoardPosOffset_ByBoard = { BoardPosOffset_Oak, BoardPosOffset_Steel, BoardPosOffset_Bronze };
    internal static readonly ButtonShape[] RestButtonShape_ByBoard = { RestButtonShape_Oak, RestButtonShape_Steel, RestButtonShape_Bronze };
    internal static readonly ButtonShape[] GenericButtonShape_ByBoard = { GenericButtonShape_Oak, GenericButtonShape_Steel, GenericButtonShape_Bronze };
    internal static readonly Vector2[] ActiveGridSpacing_ByBoard = { ActiveGridSpacing_Oak, ActiveGridSpacing_Steel, ActiveGridSpacing_Bronze };
    internal static readonly float[] PileScale_ByBoard = { PileScale_Oak, PileScale_Steel, PileScale_Bronze };
    internal static readonly float[] PileSpacing_ByBoard = { PileSpacing_Oak, PileSpacing_Steel, PileSpacing_Bronze };
    internal static readonly float[] ElementsScale_ByBoard = { ElementsScale_Oak, ElementsScale_Steel, ElementsScale_Bronze };
    internal static readonly Vector3[] ClusterOffset_ByBoard = { ClusterOffset_Oak, ClusterOffset_Steel, ClusterOffset_Bronze };
    internal static readonly float[] ClusterScale_ByBoard = { ClusterScale_Oak, ClusterScale_Steel, ClusterScale_Bronze };
    internal static readonly float[] DecisionScale_ByBoard = { DecisionScale_Oak, DecisionScale_Steel, DecisionScale_Bronze };
    internal static readonly Vector3 RestButtonOffset_Oak = new Vector3(0.008f, 0f, -0.007f);         // => [Cards] RestButtonOffset_Oak
    internal static readonly Vector3 RestButtonOffset_Steel = new Vector3(-0.44f, 0f, -0.047f);       // => [Cards] RestButtonOffset_Steel
    internal static readonly Vector3 RestButtonOffset_Bronze = new Vector3(-0.45f, 0.015f, -0.012f);  // => [Cards] RestButtonOffset_Bronze
    internal const float RestButtonDiameter_Oak = 0.091f;                                             // => [Cards] RestButtonDiameter_Oak
    internal const float RestButtonDiameter_Steel = 0.071f;                                           // => [Cards] RestButtonDiameter_Steel
    internal const float RestButtonDiameter_Bronze = 0.071f;                                          // => [Cards] RestButtonDiameter_Bronze
    internal static readonly Vector3 ConfirmUndoOffset_Oak = new Vector3(-0.008f, 0f, 0.009f);        // => [Cards] ConfirmUndoOffset_Oak
    internal static readonly Vector3 ConfirmUndoOffset_Steel = new Vector3(0.462f, 0.006f, -0.047f);  // => [Cards] ConfirmUndoOffset_Steel
    internal static readonly Vector3 ConfirmUndoOffset_Bronze = new Vector3(-0.014f, 0f, -0.005f);    // => [Cards] ConfirmUndoOffset_Bronze
    internal const float ConfirmUndoSize_Oak = 0.071f;                                                // => [Cards] ConfirmUndoSize_Oak
    internal const float ConfirmUndoSize_Steel = 0.059f;                                              // => [Cards] ConfirmUndoSize_Steel
    internal const float ConfirmUndoSize_Bronze = 0.073f;                                             // => [Cards] ConfirmUndoSize_Bronze
    internal static readonly Vector3 SlotOverlayOffset_Oak = new Vector3(0.002f, -0.002f, 0.004f);    // => [Cards] SlotOverlayOffset_Oak
    internal static readonly Vector3 SlotOverlayOffset_Steel = new Vector3(0.018f, -0.002f, 0.004f);  // => [Cards] SlotOverlayOffset_Steel
    internal static readonly Vector3 SlotOverlayOffset_Bronze = new Vector3(0f, 0f, 0f);              // => [Cards] SlotOverlayOffset_Bronze
    internal const float SlotOverlaySpacing_Oak = -0.008f;                                            // => [Cards] SlotOverlaySpacing_Oak
    internal const float SlotOverlaySpacing_Steel = 0.002f;                                           // => [Cards] SlotOverlaySpacing_Steel
    internal const float SlotOverlaySpacing_Bronze = -0.01f;                                          // => [Cards] SlotOverlaySpacing_Bronze
    internal static readonly Vector3 InitiativeOffset_Oak = new Vector3(0f, 0.17f, -0.048f);          // => [Cards] InitiativeOffset_Oak
    internal static readonly Vector3 InitiativeOffset_Steel = new Vector3(0f, 0.2f, -0.07f);          // => [Cards] InitiativeOffset_Steel
    internal static readonly Vector3 InitiativeOffset_Bronze = new Vector3(0f, 0.1f, -0.004f);        // => [Cards] InitiativeOffset_Bronze
    internal const float DecisionGap_Oak = 0.0125f;                                                   // => [Cards] DecisionGap_Oak
    internal const float DecisionGap_Steel = 0.042675f;                                               // => [Cards] DecisionGap_Steel
    internal const float DecisionGap_Bronze = 0.0125f;                                                // => [Cards] DecisionGap_Bronze
    internal static readonly Vector3 PickBannerOffset_Oak = new Vector3(0f, 0f, 0f);                  // => [Cards] PickBannerOffset_Oak
    internal static readonly Vector3 PickBannerOffset_Steel = new Vector3(0f, 0.095f, 0f);        // => [Cards] PickBannerOffset_Steel
    internal static readonly Vector3 PickBannerOffset_Bronze = new Vector3(0f, 0f, 0f);               // => [Cards] PickBannerOffset_Bronze
    // TOOLTIP AREA offsets (key kept as HoverHintOffset_* for cfg compatibility). ZERO IS THE
    // RE-DERIVED DEFAULT for all three boards on purpose: the area's anchor is COMPUTED per board
    // from the tray's measured renderer extents (PlayTray.MeasureBoardLocalExtents — top-left
    // corner of the VISIBLE board, frame included), so "starts top-left" already holds on Oak,
    // Steel and Bronze without a per-board constant that could drift from the real meshes.
    internal static readonly Vector3 HoverHintOffset_Oak = new Vector3(0f, 0f, 0f);                   // => [Cards] HoverHintOffset_Oak
    internal static readonly Vector3 HoverHintOffset_Steel = new Vector3(0f, 0f, 0f);                 // => [Cards] HoverHintOffset_Steel
    internal static readonly Vector3 HoverHintOffset_Bronze = new Vector3(0f, 0f, 0f);                // => [Cards] HoverHintOffset_Bronze
    internal static readonly Vector3 AssetOffset_Oak = new Vector3(0f, 0f, 0f);                       // => [Cards] AssetOffset_Oak
    internal static readonly Vector3 AssetOffset_Steel = new Vector3(0f, 0f, 0f);                     // => [Cards] AssetOffset_Steel
    internal static readonly Vector3 AssetOffset_Bronze = new Vector3(0f, -0.11f, 0.08f);             // => [Cards] AssetOffset_Bronze
    internal const float AssetPitchDegrees_Oak = 0f;                                                  // => [Cards] AssetPitchDegrees_Oak
    internal const float AssetPitchDegrees_Steel = 0f;                                                // => [Cards] AssetPitchDegrees_Steel
    internal const float AssetPitchDegrees_Bronze = 57f;                                              // => [Cards] AssetPitchDegrees_Bronze
    internal const float RestButtonSpacing_Oak = 0f;                                                  // => [Cards] RestButtonSpacing_Oak
    internal const float RestButtonSpacing_Steel = -0.044f;                                           // => [Cards] RestButtonSpacing_Steel
    internal const float RestButtonSpacing_Bronze = 0.016f;                                           // => [Cards] RestButtonSpacing_Bronze
    internal const float GenericButtonSpacing_Oak = -0.008f;                                          // => [Cards] GenericButtonSpacing_Oak
    internal const float GenericButtonSpacing_Steel = 0.01f;                                          // => [Cards] GenericButtonSpacing_Steel
    internal const float GenericButtonSpacing_Bronze = 0f;                                            // => [Cards] GenericButtonSpacing_Bronze
    internal static readonly Vector3 ActiveOffset_Oak = new Vector3(0f, 0f, 0f);                      // => [Cards] ActiveOffset_Oak
    internal static readonly Vector3 ActiveOffset_Steel = new Vector3(0f, 0f, -0.04f);                // => [Cards] ActiveOffset_Steel
    internal static readonly Vector3 ActiveOffset_Bronze = new Vector3(0f, 0f, -0.02f);               // => [Cards] ActiveOffset_Bronze
    internal const float ActiveCardScale_Oak = 1f;                                                    // => [Cards] ActiveCardScale_Oak
    internal const float ActiveCardScale_Steel = 0.82f;                                               // => [Cards] ActiveCardScale_Steel
    internal const float ActiveCardScale_Bronze = 0.82f;                                              // => [Cards] ActiveCardScale_Bronze
    internal static readonly Vector3 PileOffset_Oak = new Vector3(0f, 0f, 0f);                        // => [Cards] PileOffset_Oak
    internal static readonly Vector3 PileOffset_Steel = new Vector3(0f, 0f, -0.04f);                  // => [Cards] PileOffset_Steel
    internal static readonly Vector3 PileOffset_Bronze = new Vector3(0f, 0f, -0.005f);                // => [Cards] PileOffset_Bronze
    internal static readonly Vector3 ObjectivesOffset_Oak = new Vector3(0f, 0.026f, 0f);              // => [Cards] ObjectivesOffset_Oak
    internal static readonly Vector3 ObjectivesOffset_Steel = new Vector3(0f, 0.032f, -0.042f);       // => [Cards] ObjectivesOffset_Steel
    internal static readonly Vector3 ObjectivesOffset_Bronze = new Vector3(0f, 0.032f, -0.002f);      // => [Cards] ObjectivesOffset_Bronze
    internal const float ObjectivesScale_Oak = 0.95f;                                                 // => [Cards] ObjectivesScale_Oak
    internal const float ObjectivesScale_Steel = 0.95f;                                               // => [Cards] ObjectivesScale_Steel
    internal const float ObjectivesScale_Bronze = 0.95f;                                              // => [Cards] ObjectivesScale_Bronze
    internal const float ObjectivesWidth_Oak = 0.8f;                                                  // => [Cards] ObjectivesWidth_Oak
    internal const float ObjectivesWidth_Steel = 0.8f;                                                // => [Cards] ObjectivesWidth_Steel
    internal const float ObjectivesWidth_Bronze = 0.8f;                                               // => [Cards] ObjectivesWidth_Bronze
    internal static readonly Vector3 ElementsOffset_Oak = new Vector3(0f, 0f, 0f);                    // => [Cards] ElementsOffset_Oak
    internal static readonly Vector3 ElementsOffset_Steel = new Vector3(0f, 0f, -0.04f);              // => [Cards] ElementsOffset_Steel
    internal static readonly Vector3 ElementsOffset_Bronze = new Vector3(0f, 0f, -0.04f);             // => [Cards] ElementsOffset_Bronze
    internal static readonly Vector3 PinOffset_Oak = new Vector3(0f, 0f, 0f);                         // => [Cards] PinOffset_Oak
    internal static readonly Vector3 PinOffset_Steel = new Vector3(-0.02f, -0.022f, 0f);              // => [Cards] PinOffset_Steel
    internal static readonly Vector3 PinOffset_Bronze = new Vector3(0f, 0f, 0f);                      // => [Cards] PinOffset_Bronze
    internal static readonly Vector3 ReadoutOffset_Oak = new Vector3(0.008f, -0.004f, -0.024f);       // => [Cards] ReadoutOffset_Oak
    internal static readonly Vector3 ReadoutOffset_Steel = new Vector3(-0.04f, 0.022f, -0.044f);      // => [Cards] ReadoutOffset_Steel
    internal static readonly Vector3 ReadoutOffset_Bronze = new Vector3(0f, 0f, 0f);                  // => [Cards] ReadoutOffset_Bronze
    internal static readonly Vector3 DecisionOffset_Oak = new Vector3(0f, 0f, 0f);                    // => [Cards] DecisionOffset_Oak
    internal static readonly Vector3 DecisionOffset_Steel = new Vector3(0f, -0.157f, 0f);         // => [Cards] DecisionOffset_Steel
    internal static readonly Vector3 DecisionOffset_Bronze = new Vector3(0f, -0.012f, 0f);            // => [Cards] DecisionOffset_Bronze
}
