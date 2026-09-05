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
    // INFO, not Debug (2026-08-30 re-decision). The mod used to ship at its most verbose
    // because the levels were a blunt re-tiering of severities nobody had judged; they are
    // judged now, so the default is the one an ordinary player should live with. Set Debug
    // to reproduce the old output exactly.
    internal const VRLogLevel LogLevel = VRLogLevel.Info;                    // => [General] LogLevel
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
    // THE 3D MAP ROOM IS THE DEFAULT PRESENTATION FROM ModBuild 230, and the dial that used to
    // turn it ON now turns it OFF (user ruling, verbatim: "Die '3D-Map' Einstellung ist nicht mehr
    // Experimentell und sollte der Standart sein. Nenne die neue Einstellung eher so etwas wie
    // 'Vanilla 2D map' oder etwas ähnliches passendes was standartmäßig aus sein soll.").
    // [Rig] Experimental3DMap (bool, default false, ON = the room) is gone; [Rig] Vanilla2DMap
    // (bool, default false, ON = the game's flat map) replaces it, and the single reader inverted
    // with it (WorldUI/MapRoom/MapRoomDriver.cs:224).
    //
    // PINNED, because the cfg SNAPSHOT under .planning/debug/default/ still carries the tester's
    // pre-230 world: dev.gloomhavenvr.cfg:189 reads "Experimental3DMap = false", and the one-shot
    // in Plugin.cs turns exactly that into "Vanilla2DMap = true" on his machine so his chosen
    // presentation does not change under him. The next snapshot taken from that install will
    // therefore show Vanilla2DMap = true against a shipped default of false, and rebase-defaults
    // would offer to "fix" the default by rebasing it to true — which is the user ruling above,
    // undone by a script. The marker is what stops that: a migrated per-install value is not
    // evidence about what a FRESH install should start with.
    internal const bool Vanilla2DMap = false;                                // => [Rig] Vanilla2DMap  (pinned: user ruling ModBuild 230 — the 3D map room is the default, so the opt-out ships OFF; the cfg snapshot carries a MIGRATED value, not a chosen default)
    internal const bool MapPresentationMigrated230 = false;                  // => [Rig] MapPresentationMigrated230  (pinned: one-shot migration marker — a fresh install must start false, or a returning player's Experimental3DMap choice is never carried over to Vanilla2DMap)
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
    internal const float MapIconScale = 2.29637f;                            // => [MapRoom] IconScale
    internal const float MapGloomhavenIconScale = 1.00027f;                  // => [MapRoom] GloomhavenIconScale
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
    internal const float MapPartyMarkerScale = 2.76815f;                     // => [MapRoom] PartyMarkerScale
    internal const float MapPathWidthScale = 2.72753f;                       // => [MapRoom] PathWidthScale
    // THE MOUSEOVER ANIMATION ON A MAP SYMBOL, OFF BY DEFAULT (user, 2026-09-03: "Bitte
    // deaktiviere die animationen für das mouseover im Kartenraum wenn ich über ein Kartensymbol
    // hovere - an der Stelle möchte ich es nicht."). FALSE IS THE REQUEST, not a conservative
    // guess: he asked for the animations gone, so the shipped build must be the build he asked
    // for and the dial exists only so a taste ruling on a visual can be taken back without a new
    // build. Since ModBuild 425 it suppresses exactly ONE of the four things
    // MapLocation.Highlight does on a hover (decompiled MapLocation.cs:525-555): the
    // NodeHoverIndicator particle effect, which is the only one of the four that actually moves
    // (an EffectAlphaFadeParticles on a 0.6 s alpha-fade coroutine). The 20 % scale step on
    // MeshParent was suppressed with it from 365 to 424 and is NOT any more — user, 2026-09-05:
    // "Wenn du im flat game über ein Symbol hoverest auf der map wird es temporär etwas größer
    // damit es hervorgehoben ist … Das will ich wieder haben, unabhängig der eingestellten
    // Symbolgröße." It is a single assignment with no tween, i.e. a highlight STATE and not an
    // animation, so it runs whichever way this dial is set. The highlight sprite, the highlighted
    // decal material and the quest-preview card were never touched by either round.
    // See WorldUI/MapRoom/MapIconHoverAnimation.
    internal const bool MapHoverAnimation = false;                           // => [MapRoom] HoverAnimation
    internal const float WorldTiltDegrees = 0f;                              // => [Rig] WorldTiltDegrees
    internal const float MaskedReaimHeadRate = 30f;                          // => [Rig] MaskedReaimHeadRate
    internal const float MaskedReaimGain = 0.15f;                            // => [Rig] MaskedReaimGain
    internal const float MaskedReaimDeadband = 5f;                           // => [Rig] MaskedReaimDeadband
    internal const bool DisablePostProcessing = true;                        // => [Compat] DisablePostProcessing
    internal const bool DisableVolumetricFog = true;                         // => [Compat] DisableVolumetricFog
    internal const string DisableComponents = "";                            // => [Compat] DisableComponents
    internal const bool WallFade = true;                                     // => [Compat] WallFade
    internal const bool ControlsLesson = true;                               // => [Compat] ControlsLesson
    // ON by default because it does not add behaviour — it RESTORES the game's own. The door
    // open is one Animator.Play; Unity's CullUpdateTransforms default withholds the transform
    // write while the door is off-camera, a condition a top-down flat camera never produces and
    // a first-person VR camera produces constantly. See Core/Environment/DoorOpenWatch.
    internal const bool DoorAnimateOffscreen = true;                         // => [Compat] DoorAnimateOffscreen
    // [Compat] TutorialVRAdapt is GONE (user ruling 2026-08-13): its OFF restored the vanilla
    // camera-step DEADLOCK the bridge exists to break (Compat/Tutorial/TutorialVR.cs).
    internal const string PrimaryHand = "Right";                             // => [Hands] PrimaryHand
    internal const Hands.HandStyle Hands_HandStyle = Hands.HandStyle.Arcane;  // => [Hands] HandStyle
    internal const bool LaserFingerOrigin = false;                           // => [Hands] LaserFingerOrigin
    internal const bool ScrollWithStickOnly = true;                          // => [Hands] ScrollWithStickOnly
    internal const float LaserFingerOffsetMeters = 0.02f;                    // => [Hands] LaserFingerOffsetMeters
    internal const string HandColor = "D9C9B5";                              // => [Hands] HandColor
    // [Rig] VoidColor and [Rig] ForwardRendering had their lines here. Both were UNBOUND by the
    // 2026-08-22 settings audit ("Etwas was das spiel kaputt macht wenn man es umstellt ist nicht
    // optional") and their values now live as constants on Plugin, where the code that reads them
    // is — a default belongs to a SETTING, and neither is one any more. rebase-defaults.py will
    // list both as UNMAPPED for as long as an old cfg still carries the keys; that is the honest
    // report, not a regression.
    internal const bool Dev_Enabled = false;                                 // => [Dev] Enabled
    internal const bool Overlay = true;                                      // => [Dev] Overlay
    internal const bool SimulateHands = false;                               // => [Dev] SimulateHands
    internal const float InputDeviceDumpInterval = 0f;                       // => [Dev] InputDeviceDumpInterval
    internal const float GloveScale = 1.12f;                                 // => [Hands] GloveScale
    internal const float PlateScale = 0.62f;                                 // => [Hands] PlateScale
    internal const float ArcaneScale = 0.62f;                                // => [Hands] ArcaneScale
}
