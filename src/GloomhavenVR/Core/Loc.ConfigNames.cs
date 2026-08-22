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
            ["Rig/SpawnInCircle"] = Pair("Free seat at the board", "Freier Platz am Brett"),
            // "Rig/VoidColor" and "Rig/ForwardRendering" ARE GONE from this table: both keys were
            // UNBOUND at ModBuild 224 (2026-08-22 settings audit, question (a)) and are constants
            // now — Color.black in Rig/VRRigDriver and Plugin.ForwardRendering. Same for
            // "Stereo/RenderMode", "WorldUI/ScreenLayerSplit", "WorldUI/SuppressPhysicalMouse",
            // "RenderQuality/ViewportScaleFallback", "RenderQuality/RebuildRigOnMsaaChange" and the
            // five [HexHighlight] entries further down. Twelve dead names, removed together at the
            // audit's (b)/(c) pass, because a name here is how a reader decides a setting EXISTS.
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
            // 2026-08 naming pass (audit 05 §3): "Haltedauer", not "halten" — the row is a duration.
            ["Comfort/RecenterHoldSeconds"] = Pair("Recenter: hold time (s)", "Zentrieren: Haltedauer (s)"),
            ["Comfort/SavedScaleMultiplier"] = Pair("Saved table scale", "Gespeicherte Tischgröße"),
            ["Comfort/DebugGizmos"] = Pair("Comfort debug overlay", "Komfort-Debug-Overlay"),
            ["Comfort/KeepPlaceOnReorigin"] = Pair("Keep place on re-don", "Platz nach Absetzen"),
            ["Comfort/TableScaleDefault25Applied"] = Pair("Internal marker", "Interne Marke"),
            // The turn stick's forward axis as world up/down (user request 2026-08-15). Named after
            // the STICK rather than after "flight", because that is the thing the player is looking
            // for on the row: which controller grows a new function.
            ["Comfort/TurnStickVertical"] = Pair("Up/down on turn stick", "Hoch/Runter am Drehstick"),
            // THE FLIGHT FAMILY, FOUR NAMES, ADDED BY THE 2026-08-22 SETTINGS AUDIT (§3.4). They
            // had none, on the argument that the four rows carry hand-written curated captions on
            // the Komfort tab — which is true and is exactly why the gap was invisible. THE SAME
            // FOUR ENTRIES ALSO APPEAR ON ERWEITERT ▸ BEWEGUNG & WELT, where nothing supplies a
            // caption, so a German menu captioned them "Flight Enabled", "Flight Direction",
            // "Flight Hand", "Flight Max Speed" — the spaced-out English key, in a German menu, on
            // four rows a player is meant to use. The words below are the curated captions
            // (Loc "vr_o_flight*"), kept identical on purpose: one row, one name, both doors.
            ["Comfort/FlightEnabled"] = Pair("Stick flight", "Stick-Flug"),
            ["Comfort/FlightDirection"] = Pair("Flight direction", "Flugrichtung"),
            ["Comfort/FlightMaxSpeed"] = Pair("Flight speed", "Fluggeschwindigkeit"),
            ["Comfort/FlightHand"] = Pair("Flight hand", "Flug-Hand"),

            // ---- [Hands] — hand models, seat & laser ----------------------------------------
            ["Hands/PrimaryHand"] = Pair("Dominant hand", "Dominante Hand"),
            ["Hands/HandStyle"] = Pair("Hand model", "Handmodell"),
            ["Hands/LaserFingerOrigin"] = Pair("Laser from fingertip", "Laser ab Fingerspitze"),
            ["Hands/ScrollWithStickOnly"] = Pair("Scroll with stick only", "Nur per Stick scrollen"),
            ["Hands/LaserFingerOffsetMeters"] = Pair("Laser start offset (m)", "Laser-Startversatz (m)"),
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
            // gone (retired 2026-08-09, deleted in the 2026-08 dead-settings sweep). Naming
            // them was in fact the thing that made the duplication invisible — the dead row and
            // the live row rendered under the SAME caption, so which of the two a player
            // reached for was luck.
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
            ["Board/TouchTilesWithFingertip"] = Pair("Touch hexes: fingertip", "Feld mit Finger antippen"),
            ["Board/TouchRange"] = Pair("Fingertip pick range (m)", "Fingerreichweite (m)"),
            ["Board/SnapToHexCenter"] = Pair("Snap to hex centre", "Auf Hexmitte einrasten"),
            ["Board/HoverHaptics"] = Pair("Haptics on new target", "Vibration bei Wechsel"),
            ["Board/AutoFocusOnTurn"] =
                Pair("Follow the character at turn", "Automatisch zum Character am Zug"),
            ["Board/AoeFlickThreshold"] = Pair("AoE turn: stick min", "AoE-Drehen: Schwelle"),
            ["Board/AoeRepeatInterval"] = Pair("AoE turn: repeat (s)", "AoE-Drehen: Takt (s)"),
            // FIVE [HexHighlight] NAMES ARE GONE — SwapStableShader, StableZTest, StableDepthBias,
            // KillBorderFlame, KillCrosshair. The KEYS were UNBOUND at ModBuild 224 (the 2026-08-22
            // settings audit, question (a)) and are constants in Board/HexHighlightFix now, so
            // there is nothing left for a name to caption. A name table entry for a key nothing
            // binds is not harmless: it is the one place a reader looks to find out whether a
            // setting exists, and it would answer yes.
            ["HexHighlight/LogMaterialDump"] = Pair("Log hex material", "Hex-Material ins Log"),
            ["SelectionReady/Enabled"] = Pair("Selection reminder pulse", "Auswahl-Erinnerung"),

            // ---- [Rig] — the world frame ----------------------------------------------------
            // The 3D campaign map had NO display name, so the menu fell back to spacing the raw
            // key out to "Experimental 3D Map" and it sat unlabelled among the leftovers under
            // Erweitert. User, 2026-08-20: "Wo finde ich die Einstellung die 3D map zu sehen wie
            // du sie implementiert hast statt die 2D Karte?" A feature nobody can find is off.
            ["Rig/Experimental3DMap"] =
                Pair("3D campaign map (experimental)", "3D-Kampagnenkarte (experimentell)"),

            // ---- [MapRoom] — the 3D map room's own dials -------------------------------------
            // Named "Karte 3D: …" so the three rows read as one family and sort together wherever
            // the catalog puts them (they are pinned to the top of the Bild-&-Welt collector, see
            // ConfigCatalog.Pinned). "Symbole" is the word the user himself used.
            //
            // EACH NAME SAYS WHICH MAP, because that is now the whole distinction (ModBuild 193,
            // user: "Trenne die Größe des Symbole auf der Weltkarte und die Symbole auf der Karte
            // für Gloomhaven. Die müssen separat justiert werden."). Before the split the general
            // dial was called plain "Symbolgröße", which was honest while it governed every map;
            // with two maps that name would be the reason he turned the wrong one. The rows must be
            // tellable apart FROM THE CAPTION ALONE — a player reading only the row list has to see
            // Weltkarte vs Stadtkarte without opening a tooltip.
            ["MapRoom/IconScale"] =
                Pair("3D map: world map icons", "Karte 3D: Symbole Weltkarte"),
            ["MapRoom/CityIconScale"] =
                Pair("3D map: city map icons", "Karte 3D: Symbole Stadtkarte"),
            ["MapRoom/GloomhavenIconScale"] =
                Pair("3D map: Gloomhaven marker", "Karte 3D: Gloomhaven-Marker"),
            // THE TWO THINGS ON THE MAP THAT ARE NOT SYMBOLS (ModBuild 194, user: "Ich will auch die
            // Größe des Markers wo man sich befindet sowie des eingezeichneten Weges von einem zum
            // anderen Punkt einstellen können"). Same "Karte 3D: …" family as the three above, and
            // each name says WHICH THING rather than which map — because that is the distinction
            // here, and a player scanning the row list must be able to tell "the marker for where I
            // am" from "the symbols of the places" without opening a tooltip. "Gruppen-Marker" is
            // the game's own vocabulary for the token (die Gruppe reist); "Wegbreite" says both the
            // object and the property in one word, which the caption column has room for.
            ["MapRoom/PartyMarkerScale"] =
                Pair("3D map: party marker", "Karte 3D: Gruppen-Marker"),
            ["MapRoom/PathWidthScale"] =
                Pair("3D map: route width", "Karte 3D: Wegbreite"),
            // THE MAP ROOM'S CARD HAND (ModBuild ~188). Its config KEY lives in the [WorldUI]
            // section — it is bound on worldui's file by the code that owns it — but the FEATURE
            // is the 3D map room's, so it is named here with its two siblings and carries the
            // same "Karte 3D: …" prefix. Without a line here the menu spaced the raw key out to
            // "Map Room Hand", which is the same gap [Rig] Experimental3DMap had at ModBuild 176.
            ["WorldUI/MapRoomHand"] =
                Pair("3D map: show your card hand", "Karte 3D: Handkarten zeigen"),
            // The map room's travel-confirm PLACEMENT dials (ModBuild 194, user ruling: "Geb mir
            // dann im debug menu die offsets um ihm zu verschieben - ich stell es selber ein").
            // Same [WorldUI]-section / map-room-feature split as MapRoomHand above, so they carry
            // the same "Karte 3D: …" prefix; without a line here the menu would space the raw keys
            // out to "Travel Button Offset X Window Heights".
            ["WorldUI/TravelButtonOffsetXWindowHeights"] =
                Pair("3D map: travel button sideways", "Karte 3D: Reise-Knopf seitlich"),
            ["WorldUI/TravelButtonOffsetYWindowHeights"] =
                Pair("3D map: travel button height", "Karte 3D: Reise-Knopf Höhe"),

            // ---- [FigureGrab] — grabbing miniatures -----------------------------------------
            ["FigureGrab/GrabFigures"] = Pair("Grab figures", "Figuren greifen"),
            ["FigureGrab/PickRadiusMillimeters"] =
                Pair("Figure: grab range at the hand (mm)", "Figur: Greifradius an der Hand (mm)"),
            // ADDED BY THE 2026-08-22 SETTINGS AUDIT (§3.4) — it was the one offered [FigureGrab]
            // row with no line here, so a German menu captioned it "Stretch Reach Millimeters".
            // Named after the GESTURE it opens (the two-hand resize), because "Reichweite" alone
            // reads as the grab radius two rows up.
            ["FigureGrab/StretchReachMillimeters"] =
                Pair("Figure: resize reach (mm)", "Figur: Greifweite Größe (mm)"),
            ["FigureGrab/StretchScaleMin"] =
                Pair("Figure: min size in hand", "Figur: Mindestgröße in Hand"),
            ["FigureGrab/StretchScaleMax"] =
                Pair("Figure: max size in hand", "Figur: Maximalgröße in Hand"),
            ["FigureGrab/StretchLimits"] =
                Pair("Figure: size limits on/off", "Figur: Größen-Grenzen an/aus"),
            ["FigureGrab/HeldFigureInfo"] =
                Pair("Figure: info on pickup", "Figur: Info beim Aufnehmen"),
            ["FigureGrab/HeldUprightAtGrab"] = Pair("Figure: upright on grab", "Figur: aufrecht greifen"),
            ["FigureGrab/HeldUpright"] = Pair("Figure: hold upright", "Figur: aufrecht halten"),
            ["FigureGrab/*HeldOffsetSide"] = Pair("Figure: sideways (m)", "Figur: seitlich (m)"),
            ["FigureGrab/*HeldOffsetUp"] = Pair("Figure: height (m)", "Figur: Höhe (m)"),
            ["FigureGrab/*HeldOffsetForward"] = Pair("Figure: forward (m)", "Figur: vor/zurück (m)"),
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
            // 2026-08 naming pass: a size dial must not read as a switch.
            ["Cards/InspectScale"] = Pair("Close-up: size", "Nahansicht: Größe"),
            ["Cards/HeldFaceBias"] = Pair("Held card: face tilt (°)", "Handkarte: Winkel (°)"),
            // 2026-08 naming pass: one object name for the fallback pair ("Ersatzkarte", like its twin).
            ["Cards/HeldForward"] = Pair("Held (fallback): fwd (m)", "Ersatzkarte: vor (m)"),
            ["Cards/HeldOffPalm"] = Pair("Held (fallback): gap (m)", "Ersatzkarte: Abstand (m)"),
            ["Cards/HeldPinchOffset"] = Pair("Held card: pinch offset", "Karte: Griffversatz"),
            ["Cards/TrayForward"] = Pair("Board spawn: forward (m)", "Brett-Start: vor (m)"),
            ["Cards/TrayDown"] = Pair("Board spawn: lower (m)", "Brett-Start: tiefer (m)"),
            ["Cards/TrayRight"] = Pair("Board spawn: right (m)", "Brett-Start: rechts (m)"),
            // 2026-08 naming pass: the board family on the "Objekt: Wirkung" colon pattern.
            ["Cards/TrayYaw"] = Pair("Board: yaw (°)", "Brett: Drehung (°)"),
            ["Cards/TrayScale"] = Pair("Board: size", "Brett: Größe"),
            ["Cards/TrayFollow"] = Pair("Board: follows you", "Brett: folgt dir"),
            ["Cards/BoardMoveMode"] = Pair("Board: movement", "Brett: Bewegung"),
            ["Cards/TrayPitch"] = Pair("Board pitch (grab, °)", "Brett-Neigung (Griff, °)"),
            ["Cards/CardLerpSpeed"] = Pair("Card flight speed", "Kartenflug-Tempo"),
            ["Cards/SlotCardInset"] = Pair("Slot card: lift out (m)", "Slot-Karte: anheben (m)"),
            // Cards/SlotCardFill ("Slot-Karte: Füllgrad") is GONE — retired 2026-08-11 together with
            // its entry; its successor is Cards/SlotOverlayScale_* below, which names both surfaces.
            ["Cards/WantedSlotHint"] = Pair("Glow on expected slot", "Erwarteter Slot leuchtet"),
            ["Cards/CardDust"] = Pair("Card dust burst", "Karten-Staubwolke"),
            ["Cards/GameCardParticles"] = Pair("Game card particles", "Karten-Partikel (Spiel)"),
            // THE PER-PILE PAIR, SIX ROWS, NAMED ONE BY ONE — and the two wildcards below them are
            // the fallback, not the answer (2026-08-22 settings audit, §3.4).
            //
            // THE BUG THAT HID THIS: both wildcards were written long ago and NEITHER COULD EVER
            // BE REACHED, because Loc.FamilyKey only knew hand-style prefixes and control-board
            // suffixes — "_Items" / "_Discard" / "_Burnt" resolved to nothing, so all six rows fell
            // back to the spaced-out key ("Fan Step Degrees_ Items") in a German menu. FamilyKey
            // knows the pile suffixes now (Loc.ConfigDescriptions.cs), which also makes the two
            // German DESCRIPTIONS behind these keys live for the first time.
            //
            // WHY EXACT NAMES AND NOT JUST THE WILDCARD, unlike every other family in this table:
            // a per-board or per-style wildcard is safe because the pane shows exactly ONE variant
            // and its heading says which. The pile kinds are NOT a player-chosen variant — all
            // three rows of each pair are on screen together (VROptionsTab.7.TopicTrees ▸ "Stapel
            // & Fächer") — so one shared name would print "Stapel: Spreizung (°)" three times in a
            // row with nothing to tell them apart. The exact key is tried first, so these win.
            ["Cards/FanStepDegrees_Items"] =
                Pair("Items pile: spread (°)", "Gegenstände-Stapel: Spreizung (°)"),
            ["Cards/FanStepDegrees_Discard"] =
                Pair("Discard pile: spread (°)", "Ablage-Stapel: Spreizung (°)"),
            ["Cards/FanStepDegrees_Burnt"] =
                Pair("Burnt pile: spread (°)", "Verbrannt-Stapel: Spreizung (°)"),
            ["Cards/FanRadiusFactor_Items"] =
                Pair("Items pile: radius (x)", "Gegenstände-Stapel: Radius (x)"),
            ["Cards/FanRadiusFactor_Discard"] =
                Pair("Discard pile: radius (x)", "Ablage-Stapel: Radius (x)"),
            ["Cards/FanRadiusFactor_Burnt"] =
                Pair("Burnt pile: radius (x)", "Verbrannt-Stapel: Radius (x)"),
            // The fallback, for a pile kind added later: it is named, if generically, from the day
            // it binds rather than showing an English key until someone notices.
            ["Cards/FanStepDegrees_*"] = Pair("Pile fan: spread (°)", "Stapel: Spreizung (°)"),
            ["Cards/FanRadiusFactor_*"] = Pair("Pile fan: radius (x)", "Stapel: Radius (x)"),
            ["Cards/BoardMinWidthMeters"] = Pair("Board: min width (m)", "Brett: min. Breite (m)"),
            ["Cards/BoardMaxWidthMeters"] = Pair("Board: max width (m)", "Brett: max. Breite (m)"),
            ["Cards/SpawnLeftOfHead"] = Pair("Board starts on the left", "Brett startet links"),
            ["Cards/SpawnSideMeters"] = Pair("Board start: left (m)", "Brett-Start: links (m)"),
            ["Cards/SpawnForwardMeters"] = Pair("Board start: forward (m)", "Brett-Start: vor (m)"),
            ["Cards/SpawnDownMeters"] = Pair("Board start: down (m)", "Brett-Start: runter (m)"),
            ["Cards/FaceMipBake"] = Pair("Smooth card textures", "Kartentexturen glätten"),
            ["Cards/Board"] = Pair("Control board", "Kontrollbrett"),
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
            ["Cards/CardSoundsEnabled"] = Pair("Card sounds", "Karten-Geräusche"),
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
            // THE FOUR FOG-OF-WAR BACKING DIALS, NAMED BY THE 2026-08-22 SETTINGS AUDIT (§3.4).
            // Seven [MixedReality] entries had no line here and showed a spaced-out English key in
            // a German menu; three of the seven stopped being offered at all in the same audit
            // (OpaquePreviewTiles, UnseenRegionMembership, UnseenBackingDebugColors — their own
            // descriptions say "not offered in the VR menu"), and these four are what is left.
            //
            // They are millimetre-scale geometry of the dark backing behind the unseen region and
            // are Erweitert-only material, but "Erweitert" is not "unnamed": an offered row shows a
            // caption whatever page it is on, and an English key in a German menu is a defect. The
            // names say WHICH of the three backing pieces each one moves — fill, wafer, curtain —
            // because that is the only thing distinguishing four numbers that all read "MR: …".
            ["MixedReality/UnseenSkirtScale"] = Pair("MR: gap fill width", "MR: Fugenfüllung Breite"),
            ["MixedReality/UnseenWaferDrop"] = Pair("MR: gap backing depth", "MR: Fugenboden Tiefe"),
            ["MixedReality/UnseenRimInset"] = Pair("MR: edge curtain inset", "MR: Randvorhang Versatz"),
            ["MixedReality/UnseenRimTopClearance"] =
                Pair("MR: edge curtain top gap", "MR: Randvorhang Abstand oben"),
            ["RenderQuality/MsaaLevel"] = Pair("MSAA level", "MSAA-Stufe"),
            ["RenderQuality/ForceAnisotropic"] = Pair("Anisotropic filtering", "Anisotrope Filterung"),
            ["RenderQuality/EyeResolutionScale"] = Pair("Resolution per eye", "Auflösung pro Auge"),
            ["RenderQuality/PixelLightCount"] = Pair("Pixel lights (max)", "Pixellichter (max)"),
            ["Sky/Style"] = Pair("Environment", "Umgebung"),
            ["Elements/EnvironmentResponse"] = Pair("Elements affect surroundings", "Elemente wirken auf Umgebung"),
            ["Elements/ResponseStrength"] = Pair("Element effect strength", "Stärke der Elementwirkung"),
            ["Haunt/EasterEggs"] = Pair("Creepy easter eggs", "Grusel-Easter-Eggs"),
            ["Haunt/Frequency"] = Pair("Easter egg frequency", "Häufigkeit der Easter-Eggs"),
            ["EnvSound/Enabled"] = Pair("Environment sounds", "Umgebungsgeräusche"),
            ["EnvSound/Gain"] = Pair("Environment volume", "Lautstärke der Umgebung"),
            // NO "EnvSound/AmbienceBed*" ROWS. Both dials were deleted at ModBuild 223 with the two
            // continuous room tones they switched and scaled — see Core/EnvSound.cs's THE ROOM TONES,
            // DELETED for the user ruling. The feature is down to "Enabled" and "Gain".

            // ---- [WallFade] — see-through wall tuning ---------------------------------------
            ["WallFade/OnFraction"] = Pair("Fade at coverage", "Ausblenden ab Deckung"),
            ["WallFade/OffFraction"] = Pair("Unfade below coverage", "Einblenden unter Wert"),
            ["WallFade/ExitDwellMovedSeconds"] = Pair("Unfade dwell, moved (s)", "Einblende-Wartezeit (s)"),
            ["WallFade/ExitDwellStationarySeconds"] = Pair("Unfade dwell, still (s)", "Wartezeit, ruhig (s)"),
            ["WallFade/StackedShellFade"] = Pair("Fade fort superstructures", "Festungs-Aufbauten ausblenden"),
            // 2026-08 naming pass: "Fades" is jargon; the name now says what the toggle does.
            ["WallFade/SyncPeerFades"] = Pair("Walls: sync with teammates", "Wände: mit Mitspielern synchron"),

            // ---- [PeerBoardFade] — a peer's board yields when it hides the play field --------
            ["PeerBoardFade/Mode"] = Pair("Boards blocking the view", "Boards vor dem Spielfeld"),
            ["PeerBoardFade/OccludedAlpha"] = Pair("Faded opacity (0-1)", "Rest-Deckkraft (0-1)"),
            ["PeerBoardFade/OnFraction"] = Pair("Fade at coverage", "Ausblenden ab Deckung"),
            ["PeerBoardFade/OffFraction"] = Pair("Unfade below coverage", "Einblenden unter Wert"),
            ["PeerBoardFade/ExitDwellMovedSeconds"] = Pair("Unfade dwell, moved (s)", "Einblende-Wartezeit (s)"),
            ["PeerBoardFade/ExitDwellStationarySeconds"] = Pair("Unfade dwell, still (s)", "Wartezeit, ruhig (s)"),

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
            // 2026-08 naming pass: "Physische Oberfläche" was opaque and unrelated to the EN name.
            ["WorldUI/ButtonCluster"] = Pair("Wrist buttons", "Handgelenk-Tasten"),
            ["WorldUI/CombatLog"] = Pair("Show combat log", "Kampflog anzeigen"),
            ["WorldUI/Dialogs"] = Pair("Dialogs in VR", "Dialoge in VR"),
            ["WorldUI/DecisionDock"] = Pair("Decision dock", "Entscheidungsleiste"),
            ["WorldUI/TrayNativeControls"] = Pair("Real buttons on board", "Echte Tasten am Brett"),
            ["WorldUI/BarFixedSize"] = Pair("Health bars: ignore distance", "Balken: Abstand ignorieren"),
            ["WorldUI/BarSizeScale"] = Pair("Health bars: size", "Lebensbalken: Größe"),
            // "WorldUI/BarZoomMinScale" / "WorldUI/BarZoomMaxScale" ("Mindestgröße"/"Maximalgröße")
            // stood here. GONE with their dials (user ruling 2026-08-13, see ActorBars.ZoomFollowMin).
            ["WorldUI/BarsOccluded"] = Pair("Health bars behind walls", "Balken hinter Wänden"),
            ["WorldUI/PanelMipBake"] = Pair("Smooth panel textures", "Tafeltexturen glätten"),
            // ModBuild 191's answer to the window shimmer, and the reason it went untested for a
            // build: both dials were bound and wired, but neither had a name, so the only place
            // they appeared was the raw catalog — under the spaced-out keys "Panel Supersample"
            // and "Panel Supersample Factor", English, among the leftovers. The names say
            // "Fenster", not "Tafel": these act on the FLOATED GAME WINDOWS (menus, story boxes,
            // merchant/character screens), not on the mod's own panels the way PanelMipBake does.
            ["WorldUI/PanelSupersample"] = Pair("Windows: render sharp", "Fenster: scharf zeichnen"),
            ["WorldUI/PanelSupersampleFactor"] = Pair("Windows: sharpness", "Fenster: Schärfegrad"),
            ["WorldUI/NeutraliseGrabPassBlur"] = Pair("Windows: remove blur effect",
                                                    "Fenster: Weichzeichner entfernen"),
            ["WorldUI/PanelMipLodOffset"] = Pair("Windows: filter sharpening",
                                                 "Fenster: Nachschärfen (Filter)"),
            ["WorldUI/CanvasScaleMm"] = Pair("Panel scale (mm/px)", "Tafel-Maßstab (mm/px)"),
            ["WorldUI/InitiativeDepthMaxSpreadPx"] = Pair("Initiative: depth (px)", "Initiative: Tiefe (px)"),
            ["WorldUI/HoverInfoScale"] = Pair("Hover info size", "Info-Karten: Größe"),
            ["WorldUI/WindowLegibility"] = Pair("Window size / legibility", "Fenster: Größe & Lesbarkeit"),
            ["WorldUI/EnemyRevealBoardClearance"] = Pair("Enemy cards: clearance", "Gegnerkarte: Abstand (m)"),
            ["WorldUI/DesktopMirrorLeftEye"] = Pair("Monitor shows left eye", "Monitor: linkes Auge"),
            ["WorldUI/ShowIntro"] = Pair("Show intro in VR", "Intro in VR zeigen"),
            ["WorldUI/ScreenWidth"] = Pair("2D screen: width (m)", "2D-Schirm: Breite (m)"),
            ["WorldUI/ScreenDistance"] = Pair("2D screen: distance (m)", "2D-Schirm: Abstand (m)"),
            ["WorldUI/MapWindOpacity"] = Pair("Map clouds opacity", "Karte: Wolken-Deckkraft"),
            ["WorldUI/DragUnlockDegrees"] = Pair("Unlock click at (°)", "Klick lösen ab (°)"),
            ["WorldUI/DragUnlockSeconds"] = Pair("Unlock click after (s)", "Klick lösen nach (s)"),
            ["WorldUI/PokeClick"] = Pair("Poke to click", "Antippen klickt"),
            ["WorldUI/PokePressDepthMm"] = Pair("Poke depth (mm)", "Antipp-Tiefe (mm)"),
            ["WorldUI/DecisionPokeDeliberate"] = Pair("Decisions: firm press", "Entscheidung: fest"),
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
            ["WorldUI/ManualScreenChordSeconds"] = Pair("Rescue chord: hold (s)", "Notgriff: halten (s)"),
            ["WorldUI/LoadingIndicator"] = Pair("Loading indicator", "Ladeanzeige beim Laden"),
            ["WorldUI/DevShowAllPanels"] = Pair("Dev: show all panels", "Dev: alle Tafeln zeigen"),
            ["WorldUI/DevForceConvert"] = Pair("Dev: force conversion", "Dev: Zwangsumwandlung"),
            ["WorldUI/StereoScreen"] = Pair("Screen with 3D depth", "Bildschirm mit 3D-Tiefe"),
            ["WorldUI/ScreenDepthStrength"] = Pair("3D depth: strength", "3D-Tiefe: Stärke"),
            ["WorldUI/VideoDepthLayer"] = Pair("3D depth for videos", "3D-Tiefe bei Videos"),
            ["WorldUI/VideoDepth"] = Pair("Video: depth offset", "Video: Tiefenversatz"),
            ["WorldUI/ScreenParallaxScale"] = Pair("3D depth: parallax", "3D-Tiefe: Parallaxe"),
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
            // 2026-08 naming pass (audit 05 §3): ONE object name for the gear/pin plate family —
            // it read "Pin-Taste", "Fixiert-Taste", "Zahnrad/Pin" and "Zahnrad/Fixiert" across
            // four rows of the same keycap group. "Zahnrad/Pin" throughout now.
            ["BoardDashboard/PinWidth"] = Pair("Gear/pin: width (m)", "Zahnrad/Pin: Breite (m)"),
            ["BoardDashboard/Height"] = Pair("Gear/pin: height (m)", "Zahnrad/Pin: Höhe (m)"),
            ["BoardDashboard/Depth"] = Pair("Gear/pin: depth (m)", "Zahnrad/Pin: Tiefe (m)"),
            ["BoardDashboard/Travel"] = Pair("Gear/pin: travel (m)", "Zahnrad/Pin: Hub (m)"),
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
            // 2026-08 naming pass: everywhere else the object is a Brett — no Denglisch holdout.
            ["Net/RemoteBoards"] = Pair("Player boards", "Mitspieler-Bretter"),
        };
}
