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
        ["seated_mode"] = Pair("Seated mode", "Sitzmodus"),
        ["table_height"] = Pair("Table height", "Tischhöhe"),
        ["vignette"] = Pair("Vignette", "Vignette"),
        ["free_movement"] = Pair("Free movement", "Freie Bewegung"),
        ["world_grab"] = Pair("World grab", "Welt greifen"),
        ["recenter_now"] = Pair("Recenter now", "Neu zentrieren"),
        ["modules"] = Pair("Modules", "Module"),
        ["dominant_hand_right"] = Pair("Dominant hand right", "Dominante Hand rechts"),
        ["board_far_ray"] = Pair("Board: far ray only", "Board: nur Fernstrahl"),
        ["world_ui_surfaces"] = Pair("World UI surfaces", "Welt-UI-Flächen"),
        ["disable_post"] = Pair("Disable post-processing*", "Post-Processing aus*"),
        ["applies_next_start"] = Pair("* applies on next VR start", "* wird beim nächsten VR-Start aktiv"),
        ["board"] = Pair("Board", "Board"),
        ["control_board"] = Pair("Control board", "Kontrollbrett"),
        ["display"] = Pair("Display", "Anzeige"),
        ["show_combat_log"] = Pair("Show combat log", "Kampflog anzeigen"),
        ["mixed_reality"] = Pair("Mixed Reality", "Mixed Reality"),
        ["key_color"] = Pair("Key color", "Key-Farbe"),
        ["debug_board_tuning"] = Pair("Debug — Board tuning", "Debug — Board-Justierung"),
        ["enable_board_tuning"] = Pair("Enable board tuning", "Board-Justierung aktivieren"),
        ["element"] = Pair("Element", "Element"),
        ["size"] = Pair("Size", "Größe"),
        ["tilt_yaw"] = Pair("Tilt/Yaw", "Neigung/Gieren"),
        ["reset_element"] = Pair("Reset element", "Element zurücksetzen"),
        ["copy_oak"] = Pair("Copy Oak→active", "Oak→aktiv kopieren"),
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
