// Shipped config defaults — one entry per line. The full rules, and the reason this family
// exists, live at the top of Defaults.Core.cs; they are deliberately not restated here.
//
// The trailing `// => [Section] Key` annotation is the machine-readable part:
// scripts/rebase-defaults.py finds an entry by it, so a line may move but its annotation
// must stay exact.


using UnityEngine;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;

namespace GloomhavenVR;

internal static partial class Defaults
{
    // ---- Plugin.cs -----------------------------------------------------------------
    internal const bool General_Enabled = true;                              // => [General] Enabled
    internal const VRLogLevel LogLevel = VRLogLevel.Trace;                   // => [General] LogLevel
    internal const string RuntimeOverride = "";                              // => [General] RuntimeOverride
    internal const string RuntimePriority = "auto";                          // => [Core] RuntimePriority
    internal const bool SkipRuntimeCandidates = false;                       // => [Core] SkipRuntimeCandidates
    internal const bool EnableGraphicsJobs = true;                           // => [Core] EnableGraphicsJobs
    internal const bool AutoRestartForGraphicsJobs = true;                   // => [Core] AutoRestartForGraphicsJobs
    internal const int InitDelayFrames = 0;                                  // => [Core] InitDelayFrames
    // [Rig] MenuRig is GONE (user ruling 2026-08-13): its OFF built NO rig outside a scenario
    // at all — no head tracking, no hand anchor, no anchor for the 2D screen that carries the
    // main menu. The menu rig is unconditional (Rig/VRRigDriver.cs).
    internal const bool SpawnInCircle = true;                                // => [Rig] SpawnInCircle
    internal const bool Experimental3DMap = false;                           // => [Rig] Experimental3DMap
    // All THREE map-room icon dials ship at 1 on purpose: 1 reproduces the pre-dial draw matrix
    // exactly, so the build that introduces them changes nothing until a slider is moved.
    //
    // ONE DIAL PER ICON POPULATION THE ROOM CAN DRAW (user report against ModBuild 192: "Trenne
    // die Größe des Symbole auf der Weltkarte und die Symbole auf der Karte für Gloomhaven. Die
    // müssen separat justiert werden."). MapChoreographer toggles exactly two map GameObjects,
    // worldMap and cityMap (decompiled MapChoreographer.cs:64/:70, switched in OpenWorldMap
    // :3727-3731 and OpenCityMap :3754-3755), and hides every location that does not belong to
    // the one on screen (RefreshShownLocationsByMap :3761 → MapLocation.HideLocation :931
    // SetActive(false)). So the drawn icons are always exactly one map's population:
    //   IconScale           — the WORLD map's location icons (villages, scenarios, bosses)
    //   GloomhavenIconScale — the capital's own marker, WORLD map only (hidden on the city map
    //                         at MapChoreographer.cs:3839)
    //   CityIconScale       — the GLOOMHAVEN CITY map's icons (the stores in m_CityLocations
    //                         :107 plus the City-type quests, MapChoreographer.cs:3873)
    internal const float MapIconScale = 1f;                                  // => [MapRoom] IconScale
    internal const float MapGloomhavenIconScale = 1f;                        // => [MapRoom] GloomhavenIconScale
    internal const float MapCityIconScale = 1f;                              // => [MapRoom] CityIconScale
    // THE TWO NON-ICON THINGS ON THE MAP (ModBuild 194, user: "Ich will auch die Größe des Markers wo
    // man sich befindet sowie des eingezeichneten Weges von einem zum anderen Punkt einstellen
    // können"). Both ship at 1 for the same reason as the three above: 1 reproduces exactly what the
    // previous build drew.
    //   PartyMarkerScale — MapChoreographer.m_PartyToken. CORRECTED AT ModBuild 195: this DOES write
    //                      the game transform's localScale. The ModBuild 194 draw-matrix route was
    //                      refused by its own guard on the only draw there is ("1 of which could NOT
    //                      take it"), and CommandBuffer has NO DrawRenderer overload taking a matrix
    //                      in Unity 2021.3.5f1 — verified against the shipping CoreModule assembly.
    //                      Nothing in the decompile writes or reads that token's scale. Authored
    //                      scale recorded once by transform identity, level-triggered write (at 1 it
    //                      writes nothing, ever), restored on Release and only while the live value
    //                      is still what we last wrote.
    //   PathWidthScale   — the route LineRenderers MapLocation builds from m_NodeLineRendererPrefab
    //                      (decompiled MapLocation.cs:435-438 active path, :1015-1054 village roads).
    //                      Applied through widthMultiplier, which multiplies the game's own width
    //                      CURVE and which the game itself never writes on a map line; original
    //                      recorded, write level-triggered, restored on stand-down.
    internal const float MapPartyMarkerScale = 1f;                           // => [MapRoom] PartyMarkerScale
    internal const float MapPathWidthScale = 1f;                             // => [MapRoom] PathWidthScale
    internal const float WorldTiltDegrees = 0f;                              // => [Rig] WorldTiltDegrees
    internal const float MaskedReaimHeadRate = 30f;                          // => [Rig] MaskedReaimHeadRate
    internal const float MaskedReaimGain = 0.15f;                            // => [Rig] MaskedReaimGain
    internal const float MaskedReaimDeadband = 5f;                           // => [Rig] MaskedReaimDeadband
    internal const bool DisablePostProcessing = true;                        // => [Compat] DisablePostProcessing
    internal const bool DisableVolumetricFog = true;                         // => [Compat] DisableVolumetricFog
    internal const string DisableComponents = "";                            // => [Compat] DisableComponents
    internal const bool WallFade = true;                                     // => [Compat] WallFade
    // [Compat] TutorialVRAdapt is GONE (user ruling 2026-08-13): its OFF restored the vanilla
    // camera-step DEADLOCK the bridge exists to break (Compat/Tutorial/TutorialVR.cs).
    internal const string PrimaryHand = "Right";                             // => [Hands] PrimaryHand
    internal const Hands.HandStyle Hands_HandStyle = Hands.HandStyle.Plate;  // => [Hands] HandStyle
    internal const bool LaserFingerOrigin = false;                           // => [Hands] LaserFingerOrigin
    internal const bool ScrollWithStickOnly = true;                          // => [Hands] ScrollWithStickOnly
    internal const float LaserFingerOffsetMeters = 0.02f;                    // => [Hands] LaserFingerOffsetMeters
    internal const string HandColor = "D9C9B5";                              // => [Hands] HandColor
    internal static readonly Color VoidColor = new Color(0f, 0f, 0f, 1f);    // => [Rig] VoidColor
    internal const bool ForwardRendering = true;                             // => [Rig] ForwardRendering
    internal const bool Dev_Enabled = false;                                 // => [Dev] Enabled
    internal const bool Overlay = true;                                      // => [Dev] Overlay
    internal const bool SimulateHands = false;                               // => [Dev] SimulateHands
    internal const float InputDeviceDumpInterval = 0f;                       // => [Dev] InputDeviceDumpInterval
    internal const float GloveScale = 1.12f;                                 // => [Hands] GloveScale
    internal const float PlateScale = 0.62f;                                 // => [Hands] PlateScale
    internal const float ArcaneScale = 0.62f;                                // => [Hands] ArcaneScale
}
