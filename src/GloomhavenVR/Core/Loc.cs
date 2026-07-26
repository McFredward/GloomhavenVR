using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;

namespace GloomhavenVR.Core;

/// <summary>
/// Central localization helper for all mod-authored VR text. Two sources:
///
/// - <see cref="Game"/>: strings that HAVE a game localization key — delegated to the
///   existing <see cref="CardsGameApi.Localize"/> (which wraps
///   <c>GLOOM.LocalizationManager.TryGetTranslation</c>). This gives FREE coverage in
///   every language the game ships, with an English literal fallback for keys that may
///   not exist / before I2 is loaded.
/// - <see cref="Mod"/>: MOD-SPECIFIC strings that have no game key — looked up in the
///   embedded per-language <see cref="Table"/> keyed by
///   <c>I2.Loc.LocalizationManager.CurrentLanguage</c> (fallback: English, then the id).
///
/// LIVE FOLLOWING: the mod subscribes ONCE (in <see cref="Init"/>, from
/// <see cref="CoreModule"/>) to the engine's <c>OnLocalizeEvent</c> and fans out to the
/// C# <see cref="OnChanged"/> event on an ACTUAL language change (mirrors the game's own
/// <c>LocalizedListener</c> lastLanguage re-check). Consumers rebuild/re-read their
/// static labels there. <see cref="Dispose"/> tears the subscription down for a clean
/// hot-reload (the engine's static event outlives the mod assembly — a leaked handler
/// would fault after F6).
///
/// All I2 access is wrapped in try/catch: at main-menu bootstrap the localization source
/// is not loaded yet (mirrors <see cref="CardsGameApi.Localize"/>'s guard). We NEVER set
/// the language — only read + subscribe.
/// </summary>
internal static class Loc
{
    /// <summary>Raised (main thread) whenever the game's selected language actually changes.</summary>
    internal static event Action? OnChanged;

    private static bool _subscribed;
    private static string _lastLanguage = string.Empty;

    // ---- lifecycle ------------------------------------------------------------------------

    /// <summary>
    /// Subscribe once to the engine's language-change event and log the detected language.
    /// Safe to call before I2 is loaded (subscribing a static delegate never touches the
    /// source data; the language read is guarded).
    /// </summary>
    internal static void Init()
    {
        if (!_subscribed)
        {
            try
            {
                I2.Loc.LocalizationManager.OnLocalizeEvent += OnEngineLocalize;
                _subscribed = true;
            }
            catch (Exception ex)
            {
                VRLog.Warn("Loc", $"I2 localization not ready at init ({ex.GetType().Name}) — " +
                                  "live language following will attach on the first change.");
            }
        }
        _lastLanguage = CurrentLanguage;
        VRLog.Info("Loc", $"Localization helper ready — current game language '{_lastLanguage}'.");
    }

    /// <summary>Unsubscribe and drop all handlers (hot-reload teardown; never throws).</summary>
    internal static void Dispose()
    {
        if (_subscribed)
        {
            try
            {
                I2.Loc.LocalizationManager.OnLocalizeEvent -= OnEngineLocalize;
            }
            catch (Exception)
            {
                // I2 gone / never loaded — nothing to detach.
            }
            _subscribed = false;
        }
        OnChanged = null;
        _lastLanguage = string.Empty;
    }

    private static void OnEngineLocalize()
    {
        string lang = CurrentLanguage;
        if (string.Equals(lang, _lastLanguage, StringComparison.Ordinal))
            return; // a Force refresh at the same language — no mod text depends on it
        _lastLanguage = lang;
        VRLog.Info("Loc", $"Game language changed → '{lang}'; refreshing mod VR text.");

        // Fan out defensively: one faulting consumer must not starve the rest.
        Action? handlers = OnChanged;
        if (handlers == null)
            return;
        foreach (Delegate d in handlers.GetInvocationList())
        {
            try
            {
                ((Action)d)();
            }
            catch (Exception ex)
            {
                VRLog.Error("Loc", $"OnChanged handler threw: {ex}");
            }
        }
    }

    // ---- lookups --------------------------------------------------------------------------

    /// <summary>The engine's current language NAME ("English"/"German"), guarded to "English".</summary>
    internal static string CurrentLanguage
    {
        get
        {
            try
            {
                string lang = I2.Loc.LocalizationManager.CurrentLanguage;
                return string.IsNullOrEmpty(lang) ? "English" : lang;
            }
            catch (Exception)
            {
                return "English"; // I2 source not loaded yet (bootstrap)
            }
        }
    }

    /// <summary>
    /// Localize a string that HAS a game loc key (current language, English literal
    /// fallback). Thin passthrough to <see cref="CardsGameApi.Localize"/>.
    /// </summary>
    internal static string Game(string key, string fallback) => CardsGameApi.Localize(key, fallback);

    /// <summary>
    /// Localize a MOD-SPECIFIC string by <paramref name="id"/> from the embedded table,
    /// keyed on the current language (fallback: English entry, then the id itself).
    /// </summary>
    internal static string Mod(string id)
    {
        if (string.IsNullOrEmpty(id))
            return id;
        if (Table.TryGetValue(id, out Dictionary<string, string> byLang))
        {
            if (byLang.TryGetValue(CurrentLanguage, out string text) && !string.IsNullOrEmpty(text))
                return text;
            if (byLang.TryGetValue("English", out string english) && !string.IsNullOrEmpty(english))
                return english;
        }
        return id;
    }

    // ---- embedded mod translation table (id → language name → text) -----------------------

    private static readonly Dictionary<string, Dictionary<string, string>> Table = Build();

    private static Dictionary<string, string> Pair(string en, string de) =>
        new(2) { ["English"] = en, ["German"] = de };

    private static Dictionary<string, Dictionary<string, string>> Build() => new(64)
    {
        // ---- generic (frame pins / gear / rest / active) ----
        ["follow"] = Pair("FOLLOW", "FOLGEN"),
        ["pinned"] = Pair("PINNED", "FIXIERT"),
        ["set"] = Pair("SET", "EINST."),
        ["short_rest"] = Pair("Short rest", "Kurze Rast"),
        ["active"] = Pair("Active", "Aktiv"),

        // ---- SettingsPanel: sections / labels / buttons ----
        ["comfort"] = Pair("Comfort", "Komfort"),
        ["table_scale"] = Pair("Table scale", "Tischgröße"),
        ["turning"] = Pair("Turning", "Drehen"),
        ["table_height"] = Pair("Table height", "Tischhöhe"),
        ["free_movement"] = Pair("Free movement", "Freie Bewegung"),
        ["world_grab"] = Pair("World grab", "Welt greifen"),
        ["recenter_now"] = Pair("Recenter now", "Neu zentrieren"),
        // Escape hatch for a control board the player cannot find any more (walked away, pinned
        // and left behind, stranded by a recentre). The per-frame watchdog recovers it on its own,
        // but the user must never be at the mercy of a timer for their primary control surface.
        ["recall_board"] = Pair("Bring board back", "Board zurückholen"),
        ["modules"] = Pair("Modules", "Module"),
        ["dominant_hand_right"] = Pair("Dominant hand right", "Dominante Hand rechts"),
        ["board_far_ray"] = Pair("Board: far ray only", "Board: nur Fernstrahl"),
        ["world_ui_surfaces"] = Pair("World UI surfaces", "Welt-UI-Flächen"),
        ["disable_post"] = Pair("Disable post-processing*", "Post-Processing aus*"),
        ["applies_next_start"] = Pair("* applies on next VR start", "* wird beim nächsten VR-Start aktiv"),
        ["board"] = Pair("Board", "Board"),
        ["control_board"] = Pair("Control board", "Kontrollbrett"),
        // The three selectable control-board MODELS. The player picks these by name in the normal
        // (non-debug) settings, exactly like the head mask and the hand style, so they must read as
        // materials in the player's language — "Oak" means nothing to a German player, "Eiche" does.
        // The enum member names (Oak/Steel/Bronze) stay the config/log/wire identity.
        ["board_oak"] = Pair("Oak", "Eiche"),
        ["board_steel"] = Pair("Steel", "Stahl"),
        ["board_bronze"] = Pair("Bronze", "Bronze"),
        // Debug pane pointer: the board SELECTION is a user feature and lives in Avatar; the Debug
        // pane only says which board its tuning rows are editing and jumps you to the picker, so
        // there are never two controls writing the same setting from two places.
        ["board_pick_in_avatar"] = Pair("Choose under Avatar ›", "Auswahl unter Avatar ›"),
        ["display"] = Pair("Display", "Anzeige"),
        ["show_combat_log"] = Pair("Show combat log", "Kampflog anzeigen"),
        // Size dial for the mouseover info panels ("2 Gold", "Geschlossene Tür", …) — the German
        // wording mirrors the user's own term ("Infotafeln"), the English one names them as the
        // hover info cards they are.
        ["hover_info_size"] = Pair("Info panel size", "Infotafel-Größe"),
        ["mixed_reality"] = Pair("Mixed Reality", "Mixed Reality"),
        ["key_color"] = Pair("Key color", "Key-Farbe"),
        ["debug_board_tuning"] = Pair("Debug — Board tuning", "Debug — Board-Justierung"),
        ["enable_board_tuning"] = Pair("Enable board tuning", "Board-Justierung aktivieren"),
        ["element"] = Pair("Element", "Element"),
        ["size"] = Pair("Size", "Größe"),
        ["tilt_yaw"] = Pair("Tilt/Yaw", "Neigung/Gieren"),
        ["reset_element"] = Pair("Reset element", "Element zurücksetzen"),
        ["objectives"] = Pair("Objectives", "Aufgaben"),
        // Caption of the (idle) shared decision drawer drawn on a REMOTE player's control board —
        // the reserved strip where their take-damage / dialog prompts dock on their own client.
        ["decision_dock"] = Pair("Decisions", "Entscheidungen"),
        ["elements"] = Pair("Elements", "Elemente"),
        ["vr_settings"] = Pair("VR settings", "VR-Einstellungen"),
        ["pin"] = Pair("Pin toggle", "Pin-Schalter"),
        ["readout"] = Pair("Round readout", "Rundenanzeige"),
        ["cluster"] = Pair("Turn buttons", "Zugleiste"),
        ["hands"] = Pair("Hands", "Hände"),
        ["hand_x"] = Pair("Hand X (lateral)", "Hand X (seitlich)"),
        ["hand_y"] = Pair("Hand Y (up)", "Hand Y (hoch)"),
        ["hand_z"] = Pair("Hand Z (forward)", "Hand Z (vorne)"),
        ["hand_pitch"] = Pair("Hand pitch", "Hand-Neigung"),
        ["category"] = Pair("Category", "Kategorie"),
        ["cat_buttons"] = Pair("Buttons", "Tasten"),
        ["cat_panels"] = Pair("Panels", "Tafeln"),
        ["cat_widgets"] = Pair("Widgets", "Widgets"),
        ["cat_fan"] = Pair("Fan", "Kartenfächer"),
        ["fan_step"] = Pair("Card step", "Kartenschritt"),
        ["fan_arc"] = Pair("Arc sweep", "Bogen"),
        ["fan_radius"] = Pair("Radius", "Radius"),
        ["fan_split"] = Pair("Hover split", "Spreizung"),
        // Ghost hand: the fan-carrying hand fades while the fan is open ([Hands] GhostHandOnFan).
        ["ghost_hand"] = Pair("Ghost hand", "Geisterhand"),
        ["ghost_strength"] = Pair("Ghost strength", "Geist-Stärke"),
        // Card presentation (edge-read fix): per-card toe-in toward the head + the gaze-following
        // depth-bow apex. "Zum Spieler" = how squarely each card faces you; "Blickfolge" = how far
        // the card you look at is brought out of the fan's depth recession.
        ["fan_face_viewer"] = Pair("Face viewer", "Zum Spieler"),
        ["fan_gaze_follow"] = Pair("Gaze follow", "Blickfolge"),
        ["fan_gaze_smooth"] = Pair("Gaze easing", "Blick-Glättung"),
        ["decision"] = Pair("Decision", "Entscheidung"),
        ["avatar"] = Pair("Avatar", "Avatar"),
        ["head_mask"] = Pair("Head Mask", "Kopfmaske"),
        ["mask"] = Pair("Mask", "Maske"),
        ["mask_size"] = Pair("Mask size", "Maskengröße"),
        ["mirror"] = Pair("Mirror", "Spiegel"),
        ["remote_boards"] = Pair("Player boards", "Mitspieler-Boards"),
        ["remote_boards_off"] = Pair("Off", "Aus"),
        ["remote_boards_action"] = Pair("Action phase", "Aktionsphase"),
        ["remote_boards_always"] = Pair("Always", "Immer"),
        // Settings audit 2026-07: the remote-board mode is a purely LOCAL rendering choice that only
        // has anything to render while other players are in the session. Saying so on the panel is
        // what keeps it from reading as a dead control in single player.
        ["remote_boards_note"] = Pair("only affects other players' boards (multiplayer)",
                                      "wirkt nur auf die Boards der Mitspieler (Mehrspieler)"),
        // Settings audit 2026-07: the four wall-fade fractions shape a decision the fade driver only
        // ever evaluates while [Compat] WallFade is on, so they are inert with the "Wände
        // durchsichtig" toggle off. The pane says so instead of offering four steppers that move
        // nothing.
        ["wall_fade_note"] = Pair("needs 'See-through walls' (Display) switched on",
                                  "wirkt nur bei eingeschaltetem \"Wände durchsichtig\" (Anzeige)"),
        ["on"] = Pair("On", "An"),
        ["off"] = Pair("Off", "Aus"),
        ["spacing"] = Pair("Spacing", "Abstand"),
        ["row_gap"] = Pair("Row gap", "Zeilenabstand"),
        ["shape"] = Pair("Shape", "Form"),
        ["round"] = Pair("Round", "Rund"),
        ["square"] = Pair("Square", "Eckig"),
        ["rest"] = Pair("Rest", "Rast"),
        ["generic"] = Pair("Generic", "Generisch"),
        ["overlays"] = Pair("Overlays", "Overlays"),
        ["initiative"] = Pair("Initiative", "Initiative"),
        ["piles"] = Pair("Piles", "Stapel"),
        ["items"] = Pair("Items", "Gegenstände"),
        ["item_use"] = Pair("Use item", "Gegenstand benutzen"),
        ["item_card"] = Pair("Item card", "Gegenstandskarte"),

        // ---- Debug element / sub-category labels (previously hardcoded German) ----
        ["round_buttons"] = Pair("Round-phase buttons", "Rundenknöpfe"),
        ["board_dashboard"] = Pair("Gear & Pin", "Zahnrad & Fixiert"),
        ["wall_fade"] = Pair("Wall see-through", "Wandüberblendung"),
        ["hand_offsets"] = Pair("Hand offsets", "Hände-Offsets"),
        ["figure_offsets"] = Pair("Figure offsets", "Figuren-Offsets"),
        ["button_colors"] = Pair("Button colors", "Knopf-Farben"),
        ["subcat_board_layout"] = Pair("Board & Layout", "Board & Layout"),
        ["subcat_cards_piles"] = Pair("Cards & Piles", "Karten & Stapel"),
        ["subcat_offsets"] = Pair("Hands/Offsets", "Hände/Offsets"),
        ["subcat_wall_seethrough"] = Pair("Wall see-through", "Wand-Durchsicht"),

        // ---- SettingsPanel: Leistung (2026-07 performance pass) ----
        // The category the frame-time instrumentation and every individually switchable
        // optimization live in. Wording rule for this block: each label says WHAT it costs or
        // WHAT it measures, because these are the only settings whose effect the player cannot
        // see directly — they can only be read off the log.
        ["performance"] = Pair("Performance", "Leistung"),
        ["perf_measurement"] = Pair("Measurement (log only)", "Messung (nur Log)"),
        ["perf_note"] = Pair("writes [Perf] FRAME / STEPS / SPIKE lines to the log — changes nothing you see",
                             "schreibt [Perf] FRAME / STEPS / SPIKE ins Log — ändert nichts Sichtbares"),
        ["perf_enabled"] = Pair("Measure performance", "Leistung messen"),
        ["perf_interval"] = Pair("Summary every", "Zusammenfassung alle"),
        ["perf_attribution"] = Pair("Per-subsystem breakdown", "Aufschlüsselung nach Subsystem"),
        ["perf_top_steps"] = Pair("Subsystems listed", "Gelistete Subsysteme"),
        ["perf_spikes"] = Pair("Log frame spikes", "Bildruckler protokollieren"),
        ["perf_spike_factor"] = Pair("Spike threshold", "Ruckler-Schwelle"),
        ["perf_spike_rate"] = Pair("Spike lines max", "Ruckler-Zeilen max"),
        ["perf_alloc"] = Pair("Memory / GC pressure", "Speicher / GC-Druck"),
        ["perf_xr"] = Pair("XR counters (dropped frames)", "XR-Zähler (verworfene Bilder)"),
        ["perf_optimizations"] = Pair("Optimizations", "Optimierungen"),
        ["opt_cache_delegates"] = Pair("Reuse per-frame delegates", "Delegates pro Bild wiederverwenden"),
        ["opt_map_icons"] = Pair("Cache campaign-map icons", "Kampagnenkarten-Symbole zwischenspeichern"),
        ["opt_figure_scan"] = Pair("Cache figure lookups", "Figuren-Suche zwischenspeichern"),
        ["opt_lean_strings"] = Pair("Skip unused log text", "Ungenutzten Log-Text überspringen"),
        ["opt_tooltip_gate"] = Pair("Tooltip scan only when shown", "Tooltip-Suche nur wenn sichtbar"),
        ["opt_fan_relayout"] = Pair("Card fan re-layout limit", "Kartenfächer-Neuaufbau max."),
        ["opt_wall_eval"] = Pair("Wall see-through check every", "Wand-Durchsicht prüfen alle"),
        ["opt_remote_content"] = Pair("Player board refresh", "Mitspieler-Board-Auffrischung"),
        ["opt_quiet_diag"] = Pair("Quiet diagnostics", "Diagnose-Log leise"),
        ["perf_opt_note"] = Pair("switches marked as intervals default to today's behaviour — raise them only to trade freshness for frames",
                                 "Intervall-Regler stehen auf dem heutigen Verhalten — höher heißt weniger Aktualität für mehr Bilder/s"),

        // ---- figure-grab debug tuning (item 2b) ----
        ["cat_figures"] = Pair("Figures", "Figuren"),
        ["fig_upright"] = Pair("Upright", "Aufrecht"),
        ["fig_x"] = Pair("Offset X", "Versatz X"),
        ["fig_y"] = Pair("Offset Y", "Versatz Y"),
        ["fig_z"] = Pair("Offset Z", "Versatz Z"),
        ["fig_tilt"] = Pair("Tilt", "Neigung"),
        ["fig_yaw"] = Pair("Face Yaw", "Drehung"),
        ["fig_scale"] = Pair("Scale", "Größe"),

        // ---- wrist HUD debug tuning (item 10) ----
        ["cat_wrist"] = Pair("Wrist", "Handgelenk"),
        ["wrist_pitch"] = Pair("Pitch", "Neigung"),
        ["wrist_yaw"] = Pair("Yaw", "Drehung"),
        ["wrist_roll"] = Pair("Roll", "Rollen"),
        ["wrist_x"] = Pair("Offset X", "Versatz X"),
        ["wrist_y"] = Pair("Offset Y", "Versatz Y"),
        ["wrist_z"] = Pair("Offset Z", "Versatz Z"),

        // ---- WristHud ----
        ["no_character"] = Pair("no character", "kein Charakter"),
        ["hp"] = Pair("HP", "LP"),
        ["xp"] = Pair("XP", "EP"),
        ["gold"] = Pair("Gold", "Gold"),
        ["level"] = Pair("Level", "Stufe"),
        ["no_conditions"] = Pair("no conditions", "keine Zustände"),
        ["selecting_cards"] = Pair("selecting cards", "wählt Karten"),
        ["current_turn"] = Pair("current turn", "am Zug"),
        ["selected"] = Pair("selected", "ausgewählt"),

        // ---- FlatScreen desktop splash ----
        ["starting_desktop"] = Pair("starting… (intro plays on the desktop)",
                                    "startet… (Intro läuft auf dem Desktop)"),
    };
}
