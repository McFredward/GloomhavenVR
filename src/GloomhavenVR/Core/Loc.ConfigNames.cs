using System;
using System.Collections.Generic;

namespace GloomhavenVR.Core;

/// <summary>
/// DISPLAY NAMES for the bound config entries — what a settings row is CALLED, in the player's
/// language (user round 2026-08: "alle Einstellungen lokalisiert und selbsterklärend benennen —
/// nie ein nacktes 'Enabled'").
///
/// <para>WHY A NAME TABLE AND NOT THE KEY. The generic config browser (Debug topics) used to
/// caption every row with the config key, camel humps spaced out — "Fan Arc Sweep Degrees",
/// "Cull Submit Split" — which is a programmer's name and is English in a German menu. The
/// curated everyday rows already carry hand-written captions (<see cref="Loc.Mod"/> keys in
/// VROptionsTab.4.Curated.cs); this table is the same idea for the WHOLE catalog: keyed
/// <c>"Section/Key"</c>, resolved by <see cref="ConfigDisplayName"/>, consumed by
/// <c>ConfigCatalog.Describe</c> as the item's <c>Display</c>.</para>
///
/// <para>RULES. Every name states the OBJECT and the EFFECT ("Geisterhand bei Fächer", never
/// bare "Enabled"); units ride along in parentheses where the number is meaningless without
/// them. Names stay SHORT — the row caption column clips with an ellipsis, and ~28 characters
/// is the practical cap before German captions start losing their tails.</para>
///
/// <para>FAMILIES, same convention as the description table (<c>Loc.ConfigDescriptions.cs</c>):
/// a <c>*</c> key covers all members of a per-hand-style (<c>Glove/Plate/Arcane</c> prefix) or
/// per-control-board (<c>_Oak/_Steel/_Bronze</c> suffix) family with ONE variant-free name —
/// the pane's heading already says which variant the rows edit. The exact key is tried first,
/// so a one-off key that merely looks like a family member cannot be mislabeled.</para>
///
/// <para>A MISS IS SAFE. An entry bound tomorrow with no line here degrades to the spaced-out
/// key — readable, complete, never blank — exactly like an untranslated description. Retired
/// entries (LEGACY/RESERVED/DEPRECATED markers) and the not-offered list never reach the UI,
/// so they deliberately have no line here.</para>
/// </summary>
internal static partial class Loc
{
    /// <summary>
    /// Localized display name for a bound config entry, or <c>null</c> when none is written
    /// down — the caller keeps its derived caption (the spaced-out key). Resolution: exact
    /// <c>Section/Key</c>, then the per-style/per-board family wildcard, in the current
    /// language with English fallback (mirrors <see cref="Mod"/>).
    /// </summary>
    internal static string? ConfigDisplayName(string section, string key)
    {
        if (string.IsNullOrEmpty(section) || string.IsNullOrEmpty(key))
            return null;

        Dictionary<string, Dictionary<string, string>> table = _configNames ??= BuildConfigNames();

        if (!table.TryGetValue(section + "/" + key, out Dictionary<string, string> byLang))
        {
            string? family = FamilyKey(key);
            if (family == null || !table.TryGetValue(section + "/" + family, out byLang))
                return null;
        }

        if (byLang.TryGetValue(CurrentLanguage, out string text) && !string.IsNullOrEmpty(text))
            return text;
        return byLang.TryGetValue("English", out string english) && !string.IsNullOrEmpty(english)
            ? english
            : null;
    }

    private static Dictionary<string, Dictionary<string, string>>? _configNames;

    private static Dictionary<string, Dictionary<string, string>> BuildConfigNames() =>
        new(512, StringComparer.Ordinal)
        {
            // ---- [General] / [Core] / [Dev] — system & start-up -----------------------------
            ["General/Enabled"] = Pair("Enable VR mod", "VR-Mod aktivieren"),
            ["General/LogLevel"] = Pair("Log detail level", "Protokoll-Detailstufe"),
            ["General/RuntimeOverride"] = Pair("OpenXR runtime file", "OpenXR-Runtime-Datei"),
            ["Core/RuntimePriority"] = Pair("Runtime try order", "Runtime-Reihenfolge"),
            ["Core/SkipRuntimeCandidates"] = Pair("Single runtime attempt", "Nur Standard-Runtime"),
            ["Core/EnableGraphicsJobs"] = Pair("Threaded submission", "Parallele Bildabgabe"),
            ["Core/AutoRestartForGraphicsJobs"] = Pair("Restart automatically", "Automatisch neu starten"),
            ["Core/InitDelayFrames"] = Pair("Start delay (frames)", "Startverzug (Frames)"),
            ["Dev/Enabled"] = Pair("Developer mode", "Entwicklermodus"),
            ["Dev/Overlay"] = Pair("Dev overlay at start", "Dev-Overlay beim Start"),
            ["Dev/SimulateHands"] = Pair("Simulate hands (desktop)", "Hände simulieren (PC)"),
            ["Dev/InputDeviceDumpInterval"] = Pair("XR device log (s, 0=off)", "XR-Geräte-Log (s, 0=aus)"),

            // ---- [Rig] / [Compat] — picture & world -----------------------------------------
            ["Rig/MenuRig"] = Pair("Main menu in VR", "Hauptmenü in VR"),
            ["Rig/SpawnInCircle"] = Pair("Free seat at the board", "Freier Platz am Brett"),
            ["Rig/VoidColor"] = Pair("Void colour around menus", "Leerraum-Farbe um Menüs"),
            ["Rig/ForwardRendering"] = Pair("Forward rendering", "Forward-Rendering"),
            ["Compat/DisablePostProcessing"] = Pair("Disable post-processing", "Post-Processing aus"),
            ["Compat/DisableVolumetricFog"] = Pair("Volumetric fog off", "Volumennebel aus"),
            ["Compat/DisableComponents"] = Pair("Disabled components", "Deaktivierte Teile"),
            ["Compat/WallFade"] = Pair("See-through walls", "Wände durchsichtig"),

            // ---- [Comfort] — movement & world -----------------------------------------------
            ["Comfort/WorldGrabEnabled"] = Pair("World grab", "Welt greifen"),
            ["Comfort/FreeMovement"] = Pair("Free movement", "Freie Bewegung"),
            ["Comfort/VerticalDrag"] = Pair("Drag vertically", "Senkrecht ziehen"),
            ["Comfort/RotateEnabled"] = Pair("Rotate the world", "Welt drehen"),
            ["Comfort/ScaleEnabled"] = Pair("Resize the world", "Welt skalieren"),
            ["Comfort/ScaleMin"] = Pair("Zoom-out limit", "Zoom-Untergrenze"),
            ["Comfort/ScaleMax"] = Pair("Zoom-in limit", "Zoom-Obergrenze"),
            ["Comfort/TurnMode"] = Pair("Turning", "Drehen"),
            ["Comfort/SnapTurnDegrees"] = Pair("Snap angle", "Sprungwinkel"),
            ["Comfort/SmoothTurnSpeed"] = Pair("Turn speed", "Drehgeschwindigkeit"),
            ["Comfort/TurnHand"] = Pair("Turning hand", "Dreh-Hand"),
            // Comfort/TableHeightOffset ("Tischhöhe") is GONE — the setting was removed by user
            // ruling 2026-08 (free locomotion replaced it), so nothing binds this key any more.
            ["Comfort/RecenterHoldSeconds"] = Pair("Recenter hold (s)", "Zentrieren halten (s)"),
            ["Comfort/SavedScaleMultiplier"] = Pair("Saved table scale", "Gespeicherte Tischgröße"),
            ["Comfort/DebugGizmos"] = Pair("Comfort debug overlay", "Komfort-Debug-Overlay"),
            ["Comfort/KeepPlaceOnReorigin"] = Pair("Keep place on re-don", "Platz nach Absetzen"),
            ["Comfort/TableScaleDefault25Applied"] = Pair("Internal marker", "Interne Marke"),

            // ---- [Hands] — hand models, seat & laser ----------------------------------------
            ["Hands/PrimaryHand"] = Pair("Dominant hand", "Dominante Hand"),
            ["Hands/HandStyle"] = Pair("Hand model", "Handmodell"),
            ["Hands/LaserFingerOrigin"] = Pair("Laser from fingertip", "Laser ab Fingerspitze"),
            ["Hands/ScrollWithStickOnly"] = Pair("Scroll with stick only", "Nur per Stick scrollen"),
            ["Hands/LaserFingerOffsetMeters"] = Pair("Laser start offset (m)", "Laser-Startversatz (m)"),
            ["Hands/RayAlwaysOn"] = Pair("Laser always on", "Laser immer an"),
            ["Hands/ModalRayConeDegrees"] = Pair("Laser cone (°)", "Laser-Kegel (°)"),
            ["Hands/GlovePinkyCounterAbduction"] = Pair("Glove: pinky angle (°)", "Kleinfinger-Winkel (°)"),
            ["Hands/GhostHandOnFan"] = Pair("Ghost hand on open fan", "Geisterhand bei Fächer"),
            ["Hands/GhostHandOnHeldCard"] = Pair("Ghost hand on held card", "Geisterhand bei Karte"),
            ["Hands/GhostHandStrength"] = Pair("Ghost hand strength", "Geisterhand-Stärke"),
            ["Hands/TestFist"] = Pair("Debug: force fist", "Debug: Faust erzwingen"),
            ["Hands/CurlProximal"] = Pair("Finger curl: knuckle (°)", "Krümmung: Wurzel (°)"),
            ["Hands/CurlMiddle"] = Pair("Finger curl: middle (°)", "Krümmung: Mitte (°)"),
            ["Hands/CurlTip"] = Pair("Finger curl: tip (°)", "Krümmung: Spitze (°)"),
            ["Hands/CurlInputFullAt"] = Pair("Full curl at grip value", "Vollgriff ab Griffwert"),
            // Per-style families (the heading names the worn style).
            ["Hands/*Scale"] = Pair("Hand size", "Handgröße"),
            ["Hands/*GripPitchDegrees"] = Pair("Hand seat: pitch (°)", "Handsitz: Neigung (°)"),
            ["Hands/*LateralOffset"] = Pair("Hand seat: sideways (m)", "Handsitz: seitlich (m)"),
            ["Hands/*VerticalOffset"] = Pair("Hand seat: height (m)", "Handsitz: Höhe (m)"),
            ["Hands/*ForwardOffset"] = Pair("Hand seat: forward (m)", "Handsitz: vor/zurück (m)"),
            ["Hands/*GripRollDegrees"] = Pair("Hand seat: roll (°)", "Handsitz: Rollen (°)"),
            ["Hands/*GripYawDegrees"] = Pair("Hand seat: yaw (°)", "Handsitz: Gieren (°)"),
            ["Hands/*SpreadOffset"] = Pair("Hand spacing (m)", "Handabstand (m)"),

            // ---- [WristHud] — the wrist HUD's per-style palm pose ---------------------------
            // The six [WorldUI] WristHud{Pitch..OffsetZ} twins that used to be named here are
            // gone: they were retired in 2026-08-09 ("LEGACY — no effect" at their bind site),
            // and a retired entry never reaches the UI. Naming them was in fact the thing that
            // made the duplication invisible — the dead row and the live row rendered under the
            // SAME caption, so which of the two a player reached for was luck.
            //
            // The three offsets are named for the DIRECTION they move the plate, not for their
            // axis letter — in the KEY as well as here. "X/Y/Z" is the same programmer's name the
            // whole table exists to stop showing, and here it was actively misleading: these are
            // the wrist ANCHOR's axes, which is not a frame anyone would assume from a letter.
            ["WorldUI/WristHud"] = Pair("Wrist status display", "Handgelenk-Anzeige"),
            ["WristHud/*PalmPitch"] = Pair("Wrist HUD: pitch (°)", "Arm-HUD: Neigung (°)"),
            ["WristHud/*PalmYaw"] = Pair("Wrist HUD: yaw (°)", "Arm-HUD: Gieren (°)"),
            ["WristHud/*PalmRoll"] = Pair("Wrist HUD: roll (°)", "Arm-HUD: Rollen (°)"),
            ["WristHud/*PalmSideOffset"] = Pair("Wrist HUD: across (m)", "Arm-HUD: quer (m)"),
            ["WristHud/*PalmFingerOffset"] = Pair("Wrist HUD: to fingers (m)", "Arm-HUD: zu den Fingern (m)"),
            ["WristHud/*PalmLiftOffset"] = Pair("Wrist HUD: off palm (m)", "Arm-HUD: Abstand Hand (m)"),

            // ---- [Board] / [HexHighlight] / [SelectionReady] — board & targeting ------------
            ["Board/ForceFarMode"] = Pair("Far-ray picking only", "Nur Fernstrahl-Auswahl"),
            ["Board/TouchTilesWithFingertip"] = Pair("Touch hexes: fingertip", "Feld mit Finger antippen"),
            ["Board/TouchRange"] = Pair("Fingertip pick range (m)", "Fingerreichweite (m)"),
            ["Board/SnapToHexCenter"] = Pair("Snap to hex centre", "Auf Hexmitte einrasten"),
            ["Board/HoverHaptics"] = Pair("Haptics on new target", "Vibration bei Wechsel"),
            ["Board/AoeFlickThreshold"] = Pair("AoE turn: stick min", "AoE-Drehen: Schwelle"),
            ["Board/AoeRepeatInterval"] = Pair("AoE turn: repeat (s)", "AoE-Drehen: Takt (s)"),
            ["HexHighlight/SwapStableShader"] = Pair("Stable hex shader", "Stabiler Hex-Shader"),
            ["HexHighlight/StableZTest"] = Pair("Hex decal: ZTest", "Hex-Dekal: ZTest"),
            ["HexHighlight/StableDepthBias"] = Pair("Hex decal: depth bias", "Hex-Dekal: Tiefen-Bias"),
            ["HexHighlight/KillBorderFlame"] = Pair("Fallback: flame off", "Fallback: Randflamme aus"),
            ["HexHighlight/KillCrosshair"] = Pair("Fallback: crosshair off", "Fallback: Fadenkreuz aus"),
            ["HexHighlight/KillBorderLine"] = Pair("Fallback: line off", "Fallback: Randlinie aus"),
            ["HexHighlight/KillFill"] = Pair("Fallback: fill off", "Fallback: Füllung aus"),
            ["HexHighlight/LogMaterialDump"] = Pair("Log hex material", "Hex-Material ins Log"),
            ["SelectionReady/Enabled"] = Pair("Selection reminder pulse", "Auswahl-Erinnerung"),

            // ---- [FigureGrab] — grabbing miniatures -----------------------------------------
            ["FigureGrab/GrabFigures"] = Pair("Grab figures", "Figuren greifen"),
            ["FigureGrab/PickRadiusMillimeters"] =
                Pair("Figure: grab range at the hand (mm)", "Figur: Greifradius an der Hand (mm)"),
            ["FigureGrab/HeldUprightAtGrab"] = Pair("Figure: upright on grab", "Figur: aufrecht greifen"),
            ["FigureGrab/HeldUpright"] = Pair("Figure: hold upright", "Figur: aufrecht halten"),
            ["FigureGrab/*HeldOffsetSide"] = Pair("Figure: sideways (m)", "Figur: seitlich (m)"),
            ["FigureGrab/*HeldOffsetUp"] = Pair("Figure: height (m)", "Figur: Höhe (m)"),
            ["FigureGrab/*HeldOffsetForward"] = Pair("Figure: forward (m)", "Figur: vor/zurück (m)"),
            ["FigureGrab/*HeldScale"] = Pair("Figure: zoom in hand", "Figur: Zoom in Hand"),
            ["FigureGrab/*HeldRotPitch"] = Pair("Figure: pitch (°)", "Figur: Neigung (°)"),
            ["FigureGrab/*HeldRotYaw"] = Pair("Figure: yaw (°)", "Figur: Drehung (°)"),
            ["FigureGrab/*HeldRotRoll"] = Pair("Figure: roll (°)", "Figur: Rollen (°)"),

            // ---- [Cards] — fan, held cards, control board -----------------------------------
            ["Cards/DevFakeHand"] = Pair("Debug: test cards (n)", "Debug: Testkarten (n)"),
            ["Cards/RevealMode"] = Pair("Fan opens by", "Fächer öffnen"),
            ["Cards/RevealEnterDegrees"] = Pair("Fan opens at roll (°)", "Fächer öffnen ab (°)"),
            ["Cards/RevealExitDegrees"] = Pair("Fan closes at roll (°)", "Fächer schließen ab (°)"),
            ["Cards/FanRadius"] = Pair("Fan: radius (m)", "Fächer: Radius (m)"),
            ["Cards/FanPalmOffset"] = Pair("Fan: above palm (m)", "Fächer: über Hand (m)"),
            ["Cards/CardWidth"] = Pair("Card width (m)", "Kartenbreite (m)"),
            ["Cards/InspectScale"] = Pair("Close-up size", "Nahansicht"),
            ["Cards/HeldFaceBias"] = Pair("Held card: face tilt (°)", "Handkarte: Winkel (°)"),
            ["Cards/HeldForward"] = Pair("Held (fallback): fwd (m)", "Karte (Ersatz): vor (m)"),
            ["Cards/HeldOffPalm"] = Pair("Held (fallback): gap (m)", "Ersatzkarte: Abstand (m)"),
            ["Cards/HeldPinchOffset"] = Pair("Held card: pinch offset", "Karte: Griffversatz"),
            ["Cards/TrayForward"] = Pair("Board spawn: forward (m)", "Brett-Start: vor (m)"),
            ["Cards/TrayDown"] = Pair("Board spawn: lower (m)", "Brett-Start: tiefer (m)"),
            ["Cards/TrayRight"] = Pair("Board spawn: right (m)", "Brett-Start: rechts (m)"),
            ["Cards/TrayYaw"] = Pair("Board yaw (°)", "Brett-Drehung (°)"),
            ["Cards/TrayScale"] = Pair("Board size", "Brettgröße"),
            ["Cards/TrayFollow"] = Pair("Board follows you", "Brett folgt dir"),
            ["Cards/BoardMoveMode"] = Pair("Board movement", "Brett-Bewegung"),
            ["Cards/TrayPitch"] = Pair("Board pitch (grab, °)", "Brett-Neigung (Griff, °)"),
            ["Cards/CardLerpSpeed"] = Pair("Card flight speed", "Kartenflug-Tempo"),
            ["Cards/SlotCardInset"] = Pair("Slot card: lift out (m)", "Slot-Karte: anheben (m)"),
            // Cards/SlotCardFill ("Slot-Karte: Füllgrad") is GONE — retired 2026-08-11 together with
            // its entry; its successor is Cards/SlotOverlayScale_* below, which names both surfaces.
            ["Cards/WantedSlotHint"] = Pair("Glow on expected slot", "Erwarteter Slot leuchtet"),
            ["Cards/CardDust"] = Pair("Card dust burst", "Karten-Staubwolke"),
            ["Cards/GameCardParticles"] = Pair("Game card particles", "Karten-Partikel (Spiel)"),
            ["Cards/FanStepDegrees_*"] = Pair("Pile fan: spread (°)", "Stapel: Spreizung (°)"),
            ["Cards/FanRadiusFactor_*"] = Pair("Pile fan: radius (x)", "Stapel: Radius (x)"),
            ["Cards/BoardMinWidthMeters"] = Pair("Board: min width (m)", "Brett: min. Breite (m)"),
            ["Cards/BoardMaxWidthMeters"] = Pair("Board: max width (m)", "Brett: max. Breite (m)"),
            ["Cards/SpawnLeftOfHead"] = Pair("Board starts on the left", "Brett startet links"),
            ["Cards/SpawnSideMeters"] = Pair("Board start: left (m)", "Brett-Start: links (m)"),
            ["Cards/SpawnForwardMeters"] = Pair("Board start: forward (m)", "Brett-Start: vor (m)"),
            ["Cards/SpawnDownMeters"] = Pair("Board start: down (m)", "Brett-Start: runter (m)"),
            ["Cards/PileViewer"] = Pair("Discard pile stacks", "Ablagestapel anzeigen"),
            ["Cards/ActivePile"] = Pair("Active-cards column", "Aktive-Karten-Spalte"),
            ["Cards/FaceMipBake"] = Pair("Smooth card textures", "Kartentexturen glätten"),
            ["Cards/Board"] = Pair("Control board", "Kontrollbrett"),
            ["Cards/GrabButton"] = Pair("Grab button", "Greif-Taste"),
            ["Cards/BoardScaleDefault04Applied"] = Pair("Internal marker", "Interne Marke"),
            ["Cards/DecisionOffsetYRebased"] = Pair("Internal marker", "Interne Marke"),
            // Fan behaviour & shape.
            ["Cards/FanCurveByFill"] = Pair("Fan: curve by hand size", "Fächer: Bogen je Anzahl"),
            ["Cards/FanMaxHandForCurve"] = Pair("Fan: full curve at N", "Fächer: Bogen voll ab N"),
            ["Cards/FanFlatCurvatureFactor"] = Pair("Fan: arch factor", "Fächer: Bogenfaktor"),
            ["Cards/FanTiltFactor"] = Pair("Fan: card tilt factor", "Fächer: Kartenneigung"),
            ["Cards/FanSplitMultiplier"] = Pair("Fan: gap width (m)", "Fächer: Lückenbreite (m)"),
            ["Cards/FanSplitFalloff"] = Pair("Fan: gap falloff", "Fächer: Lücken-Abklang"),
            ["Cards/FanSelectedPopForward"] = Pair("Fan: card pop-out (m)", "Fächer: Karte hervor (m)"),
            ["Cards/FanFollowSmoothing"] = Pair("Fan: follow smoothing", "Fächer: Folge-Glättung"),
            ["Cards/FanFollowDeadzone"] = Pair("Fan: follow deadzone (m)", "Fächer: Totzone (m)"),
            ["Cards/RevealIgnoreWhenGrabbing"] = Pair("Fan: not while grabbing", "Fächer: nicht im Griff"),
            ["Cards/FanOpenDuration"] = Pair("Fan: open time (s)", "Fächer: Öffnen (s)"),
            ["Cards/FanOpenStagger"] = Pair("Fan: per-card delay (s)", "Fächer: Kartenverzug (s)"),
            ["Cards/FanCloseDuration"] = Pair("Fan: close time (s)", "Fächer: Schließdauer (s)"),
            ["Cards/FanSwapDuration"] = Pair("Swap: flight time (s)", "Tausch: Flugdauer (s)"),
            ["Cards/FanSwapStagger"] = Pair("Swap: wipe delay (s)", "Tausch: Wischverzug (s)"),
            ["Cards/FanSwapOverlap"] = Pair("Swap: overlap", "Tausch: Überlappung"),
            ["Cards/FanSwapTravel"] = Pair("Swap: gather point (m)", "Tausch: Sammelpunkt (m)"),
            ["Cards/FanSwapArc"] = Pair("Swap: depth bow (m)", "Tausch: Tiefenbogen (m)"),
            ["Cards/FanSwapSpinDegrees"] = Pair("Swap: counter-roll (°)", "Tausch: Gegendrehung (°)"),
            ["Cards/FanSwapSeedScale"] = Pair("Swap: start size", "Tausch: Startgröße"),
            ["Cards/FanSwapSettleOvershoot"] = Pair("Swap: settle overshoot", "Tausch: Nachschwingen"),
            ["Cards/ItemFanOpenDuration"] = Pair("Items: open time (s)", "Gegenstände: Öffnen (s)"),
            ["Cards/ItemFanOpenStagger"] = Pair("Items: deal delay (s)", "Gegenstände: Kartenverzug (s)"),
            ["Cards/ItemFanOpenArc"] = Pair("Items: flight bow (m)", "Gegenstände: Flugbogen (m)"),
            ["Cards/ItemFanOpenSpinDegrees"] = Pair("Items: unfold roll (°)", "Gegenstände: Aufklapp-Drehung (°)"),
            ["Cards/ItemFanSeedScale"] = Pair("Items: start size", "Gegenstände: Startgröße"),
            ["Cards/ItemFanSettleOvershoot"] = Pair("Items: settle overshoot", "Gegenstände: Nachschwingen"),
            ["Cards/ItemFanCloseDuration"] = Pair("Items: close time (s)", "Gegenstände: Schließdauer (s)"),
            ["Cards/ItemFanCloseStagger"] = Pair("Items: close delay (s)", "Gegenstände: Schließverzug (s)"),
            ["Cards/ItemCueBeatSeconds"] = Pair("Item cue: heartbeat (s)", "Gegenstands-Hinweis: Herzschlag (s)"),
            ["Cards/ItemCueRingReach"] = Pair("Item cue: ring reach", "Gegenstands-Hinweis: Ringweite"),
            ["Cards/ItemCueRingAlpha"] = Pair("Item cue: ring opacity", "Gegenstands-Hinweis: Ring-Deckkraft"),
            ["Cards/ItemCueEmberRate"] = Pair("Item cue: embers/s", "Gegenstands-Hinweis: Funken/s"),
            ["Cards/ItemCueEmberSize"] = Pair("Item cue: ember size", "Gegenstands-Hinweis: Funkengröße"),
            ["Cards/ItemBerthRingThickness"] = Pair("Use berth: outline (m)", "Ablage: Umriss (m)"),
            ["Cards/ItemBerthGlow"] = Pair("Use berth: inner glow", "Ablage: Innenleuchten"),
            ["Cards/ItemBerthPingSeconds"] = Pair("Use berth: ping (s)", "Ablage: Ping (s)"),
            ["Cards/ItemBerthPingReach"] = Pair("Use berth: ping reach", "Ablage: Ping-Weite"),
            ["Cards/ItemBerthRevealSeconds"] = Pair("Use berth: appear (s)", "Ablage: Einblenden (s)"),
            ["Cards/FanRevealSound"] = Pair("Sound: fan opens", "Klang: Fächer öffnen"),
            ["Cards/FanHideSound"] = Pair("Sound: fan closes", "Klang: Fächer schließen"),
            ["Cards/CardGrabSound"] = Pair("Sound: card grabbed", "Klang: Karte greifen"),
            ["Cards/CardPlaceSound"] = Pair("Sound: card placed", "Klang: Karte ablegen"),
            ["Cards/CardTakeBackSound"] = Pair("Sound: card taken back", "Klang: Karte zurück"),
            ["Cards/FanPerCardStepDegrees"] = Pair("Fan: step per card (°)", "Fächer: Winkel/Karte (°)"),
            ["Cards/FanArcSweepDegrees"] = Pair("Fan: total arc (°)", "Fächer: Gesamtbogen (°)"),
            ["Cards/FanEffectiveRadius"] = Pair("Fan: radius (m)", "Fächer: Radius (m)"),
            ["Cards/FanHoverSplitScale"] = Pair("Fan: hover gap scale", "Fächer: Hover-Lücke"),
            ["Cards/BrowseFanOffset"] = Pair("Browse fan: offset (m)", "Stapel: Versatz (m)"),
            ["Cards/FanSideDepthCurve"] = Pair("Fan: depth bow (m)", "Fächer: Tiefenbogen (m)"),
            ["Cards/FanCurvePower"] = Pair("Fan: bow exponent", "Fächer: Bogen-Exponent"),
            ["Cards/FanCurveMinCards"] = Pair("Fan: flat up to N cards", "Fächer: flach bis N"),
            ["Cards/FanGazeBias"] = Pair("Fan: gaze-follow yaw", "Fächer: Blick-Drehung"),
            ["Cards/FanFaceViewer"] = Pair("Fan: cards face you", "Fächer: Karten zu dir"),
            ["Cards/FanGazeApexFollow"] = Pair("Fan: gaze lifts bow", "Fächer: Blick hebt Bogen"),
            ["Cards/FanGazeSmoothing"] = Pair("Fan: gaze smoothing", "Fächer: Blick-Glättung"),
            // Per-board geometry families (Debug ▸ Brett-Geometrie; heading names the board).
            ["Cards/RestButtonOffset_*"] = Pair("Rest buttons: position", "Rast-Tasten: Position"),
            ["Cards/RestButtonDiameter_*"] = Pair("Rest buttons: size (m)", "Rast-Tasten: Größe (m)"),
            ["Cards/RestButtonSpacing_*"] = Pair("Rest buttons: gap (m)", "Rast-Tasten: Abstand (m)"),
            ["Cards/RestButtonShape_*"] = Pair("Rest buttons: shape", "Rast-Tasten: Form"),
            ["Cards/ConfirmUndoOffset_*"] = Pair("Confirm/Undo: position", "Best./Zurück: Position"),
            // Cards/ConfirmUndoSize_* ("Best./Zurück: Größe") is GONE — retired 2026-08 with the
            // entry itself (user report: the dial had no effect; [BoardButtons] Width/Height is
            // the Confirm/Undo size for both cap shapes now).
            ["Cards/GenericButtonSpacing_*"] = Pair("Confirm/Undo: gap (m)", "Best./Zurück: Abstand"),
            ["Cards/GenericButtonShape_*"] = Pair("Confirm/Undo: shape", "Bestätigen/Zurück: Form"),
            ["Cards/ItemUseSlotOffset_*"] = Pair("Item-use slot: position", "Item-Slot: Position"),
            ["Cards/ItemCardOffset_*"] = Pair("Item cards: position", "Item-Karten: Position"),
            ["Cards/SlotOverlayOffset_*"] = Pair("Slot glow: position", "Slot-Glühen: Position"),
            ["Cards/SlotOverlaySpacing_*"] = Pair("Slot glow: spacing (m)", "Slot-Glühen: Abstand (m)"),
            ["Cards/SlotOverlayScale_*"] = Pair("Slot glow + card: size", "Slot-Glühen + Karte: Größe"),
            ["Cards/InitiativeOffset_*"] = Pair("Initiative: position", "Initiative: Position"),
            ["Cards/PickBannerOffset_*"] = Pair("Status placard: position", "Statustafel: Position"),
            // Config KEY kept for cfg compatibility; the DISPLAY name follows the unified
            // tooltip-area concept (2026-08-04): one fixed area, top-left, per-board adjustable.
            ["Cards/HoverHintOffset_*"] = Pair("Tooltip area: position", "Tooltip-Bereich: Position"),
            ["Cards/BoardTilt_*"] = Pair("Board: base tilt (°)", "Brett: Grundneigung (°)"),
            ["Cards/BoardPitchMin_*"] = Pair("Tilt limit down (°)", "Neigungslimit unten (°)"),
            ["Cards/BoardPitchMax_*"] = Pair("Tilt limit up (°)", "Neigungslimit oben (°)"),
            ["Cards/BoardYaw_*"] = Pair("Board: extra yaw (°)", "Brett: Zusatzdrehung (°)"),
            ["Cards/BoardScale_*"] = Pair("Board: size factor", "Brett: Größenfaktor"),
            ["Cards/BoardPosOffset_*"] = Pair("Board: position offset", "Brett: Positionsversatz"),
            ["Cards/AssetOffset_*"] = Pair("Board mesh: position", "Brett-Mesh: Position"),
            ["Cards/AssetPitchDegrees_*"] = Pair("Board mesh: pitch (°)", "Brett-Mesh: Neigung (°)"),
            ["Cards/AssetYawDegrees_*"] = Pair("Board mesh: yaw (°)", "Brett-Mesh: Drehung (°)"),
            ["Cards/AssetRollDegrees_*"] = Pair("Board mesh: roll (°)", "Brett-Mesh: Rollen (°)"),
            ["Cards/ActiveOffset_*"] = Pair("Active cards: position", "Aktive Karten: Position"),
            ["Cards/ActiveCardScale_*"] = Pair("Active cards: size", "Aktive Karten: Größe"),
            ["Cards/ActiveGridSpacing_*"] = Pair("Active cards: grid step", "Aktive Karten: Raster"),
            ["Cards/PileOffset_*"] = Pair("Piles: position", "Stapel: Position"),
            ["Cards/PileScale_*"] = Pair("Piles: size", "Stapel: Größe"),
            ["Cards/PileSpacing_*"] = Pair("Piles: gap (m)", "Stapel: Abstand (m)"),
            ["Cards/ObjectivesOffset_*"] = Pair("Objectives: position", "Aufgaben: Position"),
            ["Cards/ObjectivesScale_*"] = Pair("Objectives: size", "Aufgaben: Größe"),
            ["Cards/ObjectivesWidth_*"] = Pair("Objectives: width", "Aufgaben: Breite"),
            ["Cards/ElementsOffset_*"] = Pair("Elements: position", "Elemente: Position"),
            ["Cards/ElementsScale_*"] = Pair("Elements: size", "Elemente: Größe"),
            ["Cards/PinOffset_*"] = Pair("Pin button: position", "Fixier-Taste: Position"),
            ["Cards/ReadoutOffset_*"] = Pair("Round readout: position", "Runden-Anzeige: Position"),
            ["Cards/ClusterOffset_*"] = Pair("Button cluster: position", "Tastengruppe: Position"),
            ["Cards/ClusterScale_*"] = Pair("Button cluster: size", "Tastengruppe: Größe"),
            ["Cards/DecisionOffset_*"] = Pair("Decision dock: position", "Entscheidung: Position"),
            ["Cards/DecisionScale_*"] = Pair("Decision dock: size", "Entscheidungsdock: Größe"),
            ["Cards/DecisionGap_*"] = Pair("Decision: text gap (m)", "Entscheidung: Textlücke"),

            // ---- [MixedReality] / [Stereo] / [RenderQuality] — picture ----------------------
            ["MixedReality/Enabled"] = Pair("Mixed Reality", "Mixed Reality an"),
            ["MixedReality/KeyColor"] = Pair("Key colour", "Key-Farbe"),
            ["Stereo/RenderMode"] = Pair("Stereo render mode", "Stereo-Rendermodus"),
            ["RenderQuality/MsaaLevel"] = Pair("MSAA level", "MSAA-Stufe"),
            ["RenderQuality/ForceAnisotropic"] = Pair("Anisotropic filtering", "Anisotrope Filterung"),
            ["RenderQuality/EyeResolutionScale"] = Pair("Resolution per eye", "Auflösung pro Auge"),
            ["RenderQuality/ViewportScaleFallback"] = Pair("Viewport-scale fallback", "Viewport-Ersatzskala"),
            ["RenderQuality/RebuildRigOnMsaaChange"] = Pair("Rebuild rig on MSAA", "Rig-Neubau bei MSAA"),
            ["RenderQuality/PixelLightCount"] = Pair("Pixel lights (max)", "Pixellichter (max)"),

            // ---- [WallFade] — see-through wall tuning ---------------------------------------
            ["WallFade/OnFraction"] = Pair("Fade at coverage", "Ausblenden ab Deckung"),
            ["WallFade/OffFraction"] = Pair("Unfade below coverage", "Einblenden unter Wert"),
            ["WallFade/ExitDwellMovedSeconds"] = Pair("Unfade dwell, moved (s)", "Einblende-Wartezeit (s)"),
            ["WallFade/ExitDwellStationarySeconds"] = Pair("Unfade dwell, still (s)", "Wartezeit, ruhig (s)"),
            ["WallFade/StackedShellFade"] = Pair("Fade fort superstructures", "Festungs-Aufbauten ausblenden"),
            ["WallFade/SyncPeerFades"] = Pair("Sync teammates' wall fades", "Wand-Fades der Mitspieler"),

            // ---- [Perf] / [Optimize] — measurement & optimizations --------------------------
            ["Perf/Enabled"] = Pair("Enable measurement", "Messung aktivieren"),
            ["Perf/SummaryIntervalSeconds"] = Pair("Summary interval (s)", "Messintervall (s)"),
            ["Perf/Attribution"] = Pair("Measure mod steps", "Schritte einzeln messen"),
            ["Perf/TopSteps"] = Pair("Top steps in log (N)", "Top-Schritte im Log (N)"),
            ["Perf/SpikeLines"] = Pair("Log frame spikes", "Spike-Zeilen loggen"),
            ["Perf/SpikeBudgetFactor"] = Pair("Spike threshold (factor)", "Spike-Schwelle (Faktor)"),
            ["Perf/SpikeMaxPerSecond"] = Pair("Spike lines per second", "Spike-Zeilen pro Sekunde"),
            ["Perf/Allocations"] = Pair("Memory sampling", "Speicher-Messung"),
            ["Perf/XrStats"] = Pair("Query XR statistics", "XR-Statistik abfragen"),
            ["Perf/FrameSplit"] = Pair("Frame decomposition", "Frame-Zerlegung loggen"),
            ["Perf/SceneCensus"] = Pair("Renderer census", "Renderer-Zählung"),
            ["Perf/SceneProfile"] = Pair("Scene profile lines", "Szenen-Profil loggen"),
            ["Perf/CullSubmitSplit"] = Pair("Cull/submit split", "Cull/Submit-Aufteilung"),
            ["Optimize/CacheTickDelegates"] = Pair("Cache tick delegates", "Tick-Delegates cachen"),
            ["Optimize/MapIconCache"] = Pair("Cache map icons", "Karten-Icons cachen"),
            ["Optimize/FigureScanCache"] = Pair("Cache figure scans", "Figuren-Scan cachen"),
            ["Optimize/LeanLogStrings"] = Pair("Skip unused log strings", "Log-Strings sparen"),
            ["Optimize/TooltipScanGate"] = Pair("Tooltip scan on demand", "Tooltip-Scan bei Bedarf"),
            ["Optimize/FanRelayoutMinInterval"] = Pair("Fan relayout min (s)", "Fächer-Relayout (s)"),
            ["Optimize/WallFadeEvalInterval"] = Pair("Wall check interval (s)", "Wand-Prüfintervall (s)"),
            ["Optimize/InitiativeDepthEvalInterval"] = Pair("Row depth interval (s)", "Reihen-Tiefenintervall (s)"),
            ["Optimize/QuietDiagnostics"] = Pair("Quiet diagnostics", "Diagnose-Zeilen dämpfen"),
            ["Optimize/RemoteContentInterval"] = Pair("Remote board scan (s)", "Mitspieler-Scan (s)"),
            ["Optimize/HeadDepthPrepass"] = Pair("Keep depth prepass", "Tiefen-Prepass behalten"),
            ["Optimize/HeadCullingMaskDrop"] = Pair("Camera: skip layers", "Kamera: Ebenen aus"),
            ["Optimize/HeadMaskFromScenarioCamera"] = Pair("Camera mask from game", "Kameramaske vom Spiel"),

            // ---- [WorldUI] — panels, screen, input ------------------------------------------
            ["WorldUI/Master"] = Pair("All world panels", "Physische Oberfläche"),
            ["WorldUI/ButtonCluster"] = Pair("Wrist buttons", "Handgelenk-Tasten"),
            ["WorldUI/InitiativeTrack"] = Pair("Initiative track", "Initiative-Leiste"),
            ["WorldUI/ElementBoard"] = Pair("Element board", "Elemente-Tafel"),
            ["WorldUI/CombatLog"] = Pair("Show combat log", "Kampflog anzeigen"),
            ["WorldUI/Objectives"] = Pair("Objectives panel", "Aufgaben-Tafel"),
            ["WorldUI/Dialogs"] = Pair("Dialogs in VR", "Dialoge in VR"),
            ["WorldUI/StatPanels"] = Pair("Stat panels", "Statustafeln"),
            ["WorldUI/PropInfoCards"] = Pair("Hover info cards", "Info-Karten (Hover)"),
            ["WorldUI/EnemyReveal"] = Pair("Enemy cards", "Gegnerkarten"),
            ["WorldUI/DecisionDock"] = Pair("Decision dock", "Entscheidungsleiste"),
            ["WorldUI/UseBars"] = Pair("Bonus/use bars", "Bonus-Leisten"),
            ["WorldUI/DoomPicker"] = Pair("Doom choices in VR", "Verhängnis-Wahl in VR"),
            ["WorldUI/DistributePanel"] = Pair("Distribute panel in VR", "Punkteverteilung in VR"),
            ["WorldUI/TrayNativeControls"] = Pair("Real buttons on board", "Echte Tasten am Brett"),
            ["WorldUI/ActorBars"] = Pair("Health bars", "Lebensbalken"),
            ["WorldUI/BarFixedSize"] = Pair("Health bars: ignore distance", "Balken: Abstand ignorieren"),
            ["WorldUI/BarSizeScale"] = Pair("Health bars: size", "Lebensbalken: Größe"),
            ["WorldUI/BarZoomMinScale"] = Pair("Health bars: minimum size", "Lebensbalken: Mindestgröße"),
            ["WorldUI/BarZoomMaxScale"] = Pair("Health bars: maximum size", "Lebensbalken: Maximalgröße"),
            ["WorldUI/BarsOccluded"] = Pair("Health bars behind walls", "Balken hinter Wänden"),
            ["WorldUI/FlatScreen"] = Pair("Floating 2D screen", "Schwebender 2D-Schirm"),
            ["WorldUI/Tooltips"] = Pair("Tooltips at fingertip", "Tooltips am Finger"),
            ["WorldUI/ActionElementHints"] = Pair("Element hints", "Element-Hinweise"),
            ["WorldUI/PanelMipBake"] = Pair("Smooth panel textures", "Tafeltexturen glätten"),
            ["WorldUI/CatchAllModals"] = Pair("Catch unknown windows", "Unbek. Fenster fangen"),
            ["WorldUI/MenuPopupFloat"] = Pair("Float error popups", "Fehler schwebend"),
            ["WorldUI/ForceMouseMode"] = Pair("Force mouse mode", "Maus-Modus erzwingen"),
            ["WorldUI/CanvasScaleMm"] = Pair("Panel scale (mm/px)", "Tafel-Maßstab (mm/px)"),
            ["WorldUI/InitiativeDepthMaxSpreadPx"] = Pair("Initiative: depth (px)", "Initiative: Tiefe (px)"),
            ["WorldUI/HoverInfoScale"] = Pair("Hover info size", "Info-Karten: Größe"),
            ["WorldUI/EnemyRevealBoardClearance"] = Pair("Enemy cards: clearance", "Gegnerkarte: Abstand (m)"),
            ["WorldUI/FlatScreenAutoShow"] = Pair("2D screen automatic", "2D-Schirm automatisch"),
            ["WorldUI/DesktopMirrorLeftEye"] = Pair("Monitor shows left eye", "Monitor: linkes Auge"),
            ["WorldUI/ShowIntro"] = Pair("Show intro in VR", "Intro in VR zeigen"),
            ["WorldUI/ScreenWidth"] = Pair("2D screen: width (m)", "2D-Schirm: Breite (m)"),
            ["WorldUI/ScreenDistance"] = Pair("2D screen: distance (m)", "2D-Schirm: Abstand (m)"),
            ["WorldUI/ClickLatch"] = Pair("Freeze click position", "Klickpunkt einfrieren"),
            ["WorldUI/SuppressPhysicalMouse"] = Pair("Disable real mouse", "Echte Maus deaktivieren"),
            ["WorldUI/MapWindOpacity"] = Pair("Map clouds opacity", "Karte: Wolken-Deckkraft"),
            ["WorldUI/DragUnlockDegrees"] = Pair("Unlock click at (°)", "Klick lösen ab (°)"),
            ["WorldUI/DragUnlockSeconds"] = Pair("Unlock click after (s)", "Klick lösen nach (s)"),
            ["WorldUI/PokeClick"] = Pair("Poke to click", "Antippen klickt"),
            ["WorldUI/PokePressDepthMm"] = Pair("Poke depth (mm)", "Antipp-Tiefe (mm)"),
            ["WorldUI/DecisionPokeDeliberate"] = Pair("Decisions: firm press", "Entscheidung: fest"),
            ["WorldUI/ClickMode"] = Pair("Click delivery", "Klick-Übermittlung"),
            ["WorldUI/CombatLogFollowSeat"] = Pair("Combat log follows you", "Kampflog folgt dir"),
            ["WorldUI/CombatLogForward"] = Pair("Combat log: forward (m)", "Kampflog: vor (m)"),
            ["WorldUI/CombatLogRight"] = Pair("Combat log: right (m)", "Kampflog: rechts (m)"),
            ["WorldUI/CombatLogUp"] = Pair("Combat log: height (m)", "Kampflog: Höhe (m)"),
            ["WorldUI/CombatLogScale"] = Pair("Combat log: size", "Kampflog: Größe"),
            ["WorldUI/CombatLogUserClosed"] = Pair("Combat log closed by you", "Kampflog vom Nutzer zu"),
            ["WorldUI/PanelsFollowView"] = Pair("Panels follow view (old)", "Tafeln folgen Blick"),
            ["WorldUI/HexHintFollowView"] = Pair("Hex hint follows view", "Feld-Hinweis folgt Blick"),
            ["WorldUI/HexHintDistance"] = Pair("Hex hint: distance", "Feld-Hinweis: Abstand"),
            ["WorldUI/HexHintDrop"] = Pair("Hex hint: height", "Feld-Hinweis: Höhe"),
            ["WorldUI/HexHintSide"] = Pair("Hex hint: sideways", "Feld-Hinweis: seitlich"),
            ["WorldUI/ModalStyle"] = Pair("Window style", "Fenster-Stil"),
            ["WorldUI/ManualScreenChord"] = Pair("Rescue chord: 2D screen", "Notgriff: 2D-Bildschirm"),
            ["WorldUI/ManualScreenChordSeconds"] = Pair("Rescue chord: hold (s)", "Notgriff: halten (s)"),
            ["WorldUI/DemoteOverlaySolidClears"] = Pair("Demote overlay clears", "Overlay-Clears mildern"),
            ["WorldUI/ScreenLayerSplit"] = Pair("Screen in two layers", "Bildschirm zweilagig"),
            ["WorldUI/LoadingIndicator"] = Pair("Loading indicator", "Ladeanzeige beim Laden"),
            ["WorldUI/DevShowAllPanels"] = Pair("Dev: show all panels", "Dev: alle Tafeln zeigen"),
            ["WorldUI/DevForceConvert"] = Pair("Dev: force conversion", "Dev: Zwangsumwandlung"),
            ["WorldUI/StereoScreen"] = Pair("Screen with 3D depth", "Bildschirm mit 3D-Tiefe"),
            ["WorldUI/ScreenDepthStrength"] = Pair("3D depth: strength", "3D-Tiefe: Stärke"),
            ["WorldUI/VideoDepthLayer"] = Pair("3D depth for videos", "3D-Tiefe bei Videos"),
            ["WorldUI/VideoDepth"] = Pair("Video: depth offset", "Video: Tiefenversatz"),
            ["WorldUI/ScreenParallaxScale"] = Pair("3D depth: parallax", "3D-Tiefe: Parallaxe"),
            ["WorldUI/MapAlbedoRender"] = Pair("Map fix (unlit render)", "Karten-Fix (unbel.)"),
            ["Keyboard/Enabled"] = Pair("On-screen keyboard", "Bildschirmtastatur"),
            ["Keyboard/AutoCapitalise"] = Pair("Capitalise words", "Wörter großschreiben"),

            // ---- [RoundButtons] / [BoardButtons] / [BoardDashboard] / [RestButtons] ---------
            // THE [RoundButtons] ROWS SAY "SKIP" / "Überspringen" NOW (user, hardware ModBuild 96:
            // "Weiterhin vermisse ich die Einstellungen im Debug Menu für genau diese
            // 'Überspringen'-Tasten (offsets, Form, Größe, etc..)"). They were captioned after the
            // config SECTION — "Rundentasten", the transient round-phase button GROUP — and that
            // group has exactly one visible member on a docked board: the skip cap. Every twin it
            // could have shared the name with is forced permanently off (ButtonCluster.Tick calls
            // MirrorReady(null)/MirrorUndo(null) so the board never shows a duplicate Fortfahren or
            // Undo beside the right-hand pads). So the section name described an internal grouping
            // and the row said nothing about the button the player was looking at. Naming the
            // CONTROL rather than the container is what the sibling families already do
            // ("Best./Zurück", "Rast-Tasten"), and it is the whole of the user's complaint.
            ["RoundButtons/OffsetX"] = Pair("Skip key: sideways (m)", "Überspringen: quer (m)"),
            ["RoundButtons/OffsetY"] = Pair("Skip key: up-board (m)", "Überspringen: hoch (m)"),
            ["RoundButtons/OffsetZ"] = Pair("Skip key: proud (m)", "Überspringen: heraus (m)"),
            ["RoundButtons/Shape"] = Pair("Skip key: shape", "Überspringen: Form"),
            ["RoundButtons/CapSize"] = Pair("Skip key: cap size (m)", "Überspringen: Größe (m)"),
            ["RoundButtons/Width"] = Pair("Skip key: width (m)", "Überspringen: Breite (m)"),
            ["RoundButtons/Height"] = Pair("Skip key: height (m)", "Überspringen: Höhe (m)"),
            ["RoundButtons/Depth"] = Pair("Skip key: depth (m)", "Überspringen: Tiefe (m)"),
            ["RoundButtons/Travel"] = Pair("Skip key: travel (m)", "Überspringen: Hub (m)"),
            ["BoardButtons/Width"] = Pair("Confirm/Undo: width (m)", "Best./Zurück: Breite"),
            ["BoardButtons/Height"] = Pair("Confirm/Undo: height (m)", "Best./Zurück: Höhe"),
            ["BoardButtons/Depth"] = Pair("Confirm/Undo: depth (m)", "Best./Zurück: Tiefe"),
            ["BoardButtons/Travel"] = Pair("Confirm/Undo: travel (m)", "Best./Zurück: Hub"),
            ["BoardDashboard/PinWidth"] = Pair("Pin plate: width (m)", "Pin-Taste: Breite (m)"),
            ["BoardDashboard/Height"] = Pair("Pin plate: height (m)", "Fixiert-Taste: Höhe (m)"),
            ["BoardDashboard/Depth"] = Pair("Gear/pin: depth (m)", "Zahnrad/Pin: Tiefe (m)"),
            ["BoardDashboard/Travel"] = Pair("Gear/pin: travel (m)", "Zahnrad/Fixiert: Hub (m)"),
            ["RestButtons/Width"] = Pair("Rest keys: width (m)", "Rast-Tasten: Breite (m)"),
            ["RestButtons/Height"] = Pair("Rest keys: height (m)", "Rast-Tasten: Höhe (m)"),
            ["RestButtons/Depth"] = Pair("Rest keys: depth (m)", "Rast-Tasten: Tiefe (m)"),
            ["RestButtons/Travel"] = Pair("Rest keys: travel (m)", "Rast-Tasten: Hub (m)"),

            // ---- [ButtonColors] / [ButtonAnim] ----------------------------------------------
            ["ButtonColors/LabelR"] = Pair("Key label: red", "Tastenschrift: Rot"),
            ["ButtonColors/LabelG"] = Pair("Key label: green", "Tastenschrift: Grün"),
            ["ButtonColors/LabelB"] = Pair("Key label: blue", "Tastenschrift: Blau"),
            ["ButtonColors/LabelOutline"] = Pair("Key label: outline", "Tastenschrift: Umriss"),
            ["ButtonColors/LabelOutlineR"] = Pair("Label outline: red", "Schrift-Umriss: Rot"),
            ["ButtonColors/LabelOutlineG"] = Pair("Label outline: green", "Schrift-Umriss: Grün"),
            ["ButtonColors/LabelOutlineB"] = Pair("Label outline: blue", "Schrift-Umriss: Blau"),
            ["ButtonColors/LabelOutlineWidth"] = Pair("Label outline: width", "Schrift-Umriss: Breite"),
            ["ButtonColors/LabelUnderlay"] = Pair("Key label: shadow", "Tastenschrift: Schatten"),
            ["ButtonColors/BoardCapTintR"] = Pair("Confirm/Undo tint: red", "Bestätigen-Ton: Rot"),
            ["ButtonColors/BoardCapTintG"] = Pair("Confirm/Undo tint: green", "Bestätigen-Ton: Grün"),
            ["ButtonColors/BoardCapTintB"] = Pair("Confirm/Undo tint: blue", "Bestätigen-Ton: Blau"),
            ["ButtonColors/DashCapTintR"] = Pair("Gear/pin tint: red", "Zahnrad-Ton: Rot"),
            ["ButtonColors/DashCapTintG"] = Pair("Gear/pin tint: green", "Zahnrad-Ton: Grün"),
            ["ButtonColors/DashCapTintB"] = Pair("Gear/pin tint: blue", "Zahnrad-Ton: Blau"),
            ["ButtonColors/ClusterCapTintR"] = Pair("Round-btn tint: red", "Rundenknopf-Ton: Rot"),
            ["ButtonColors/ClusterCapTintG"] = Pair("Round-btn tint: green", "Rundenknopf-Ton: Grün"),
            ["ButtonColors/ClusterCapTintB"] = Pair("Round-btn tint: blue", "Rundenknopf-Ton: Blau"),
            ["ButtonColors/RestCapTintR"] = Pair("Rest-key tint: red", "Rast-Tasten-Ton: Rot"),
            ["ButtonColors/RestCapTintG"] = Pair("Rest-key tint: green", "Rast-Tasten-Ton: Grün"),
            ["ButtonColors/RestCapTintB"] = Pair("Rest-key tint: blue", "Rast-Tasten-Ton: Blau"),
            ["ButtonAnim/Enable"] = Pair("Key animation", "Tasten-Animation"),
            ["ButtonAnim/AppearParticles"] = Pair("Dust on appear", "Staub beim Erscheinen"),
            ["ButtonAnim/DisappearSeconds"] = Pair("Disappear time (s)", "Verschwinden (s)"),
            ["ButtonAnim/AppearSeconds"] = Pair("Appear time (s)", "Erscheinen (s)"),

            // ---- [Net] — multiplayer --------------------------------------------------------
            ["Net/Enabled"] = Pair("Multiplayer sync", "Mehrspieler-Abgleich"),
            ["Net/MaskId"] = Pair("Head mask", "Kopfmaske"),
            ["Net/MaskSize"] = Pair("Mask size", "Maskengröße"),
            ["Net/NameTags"] = Pair("Name tags", "Namensschilder"),
            ["Net/MirrorEnabled"] = Pair("Mirror", "Spiegel"),
            ["Net/VersionGuard"] = Pair("Version handshake", "Versionsabgleich"),
            ["Net/RemoteBoards"] = Pair("Player boards", "Mitspieler-Boards"),
        };
}
