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

        // ---- SettingsPanel: Leistung — the VR render trade (2026-07 GPU pass) ----
        // Wording rule for this block, from the user ("super verwirrend für den User"): every
        // label names the COMPROMISE, not the mechanism. A row belongs here only if the player
        // has to give something up to gain frames; anything else is config/Debug. And nothing
        // here duplicates the game's own Optionen › Grafik — verified against the decompiled
        // GraphicSettings/DisplaySettings: the game exposes quality preset, post-process AA
        // (FXAA/SMAA/TAA), aniso, shadows, shadow resolution, texture quality, skin weights,
        // v-sync, FPS cap, pixel lights, soft particles, reflection probes and desktop
        // resolution — but NO MSAA (QualitySettings.antiAliasing is never written by the game)
        // and nothing per-eye. Those two gaps are exactly what these rows fill.
        ["perf_render_trade"] = Pair("Sharpness vs. smoothness", "Schärfe gegen Flüssigkeit"),
        ["perf_preset"] = Pair("Graphics preset", "Grafik-Voreinstellung"),
        ["preset_quality"] = Pair("Quality", "Qualität"),
        ["preset_balanced"] = Pair("Balanced", "Ausgewogen"),
        ["preset_performance"] = Pair("Performance", "Leistung"),
        ["preset_minimum"] = Pair("Weak hardware", "Schwache Hardware"),
        ["preset_custom"] = Pair("Custom", "Eigene"),
        ["perf_preset_note"] = Pair(
            "sets the two rows below together — Quality = sharpest and most GPU, Weak hardware = softest and least. Adjust either row afterwards and this reads 'Custom'.",
            "setzt die beiden Regler darunter gemeinsam — Qualität = schärfstes Bild, höchste GPU-Last; Schwache Hardware = weichstes Bild, geringste Last. Danach einzeln nachjustierbar, dann steht hier \"Eigene\"."),
        ["perf_eye_resolution"] = Pair("Render resolution (per eye)", "Renderauflösung (pro Auge)"),
        ["perf_eye_resolution_note"] = Pair(
            "the strongest lever: GPU load rises with the SQUARE of this. Lower = softer picture and less fine texture detail, but far more frames. 1.0x is what your headset software asks for — that is usually already above the panel's own resolution.",
            "der stärkste Regler: die GPU-Last steigt im QUADRAT. Niedriger = weicheres Bild und weniger feine Texturdetails, dafür deutlich mehr Bilder/s. 1.0x ist das, was deine Headset-Software anfordert — das liegt meist schon über der Panelauflösung."),
        ["perf_msaa"] = Pair("Edge smoothing (MSAA)", "Kantenglättung (MSAA)"),
        ["perf_msaa_note"] = Pair(
            "smooths geometry edges (control board, board tiles) — not textures. Costs GPU bandwidth per step. It works on top of the render resolution above, so at a high resolution the difference between 4x and 8x is small while the cost is not.",
            "glättet Geometriekanten (Kontrollbrett, Bodenplatten) — keine Texturen. Kostet pro Stufe GPU-Bandbreite. Wirkt zusätzlich zur Renderauflösung oben: bei hoher Auflösung ist der Unterschied zwischen 4x und 8x klein, der Aufwand nicht."),
        // 2026-07 (user: "Die Erklärtexte sind zu lang, sie da drin stehen zu lassen; mach ein
        // Mouseover-Hinweis oder so etwas stattdessen."): every "…_note" paragraph below is now
        // HOVER text, shown beside the row it explains and nowhere else. Two of them were not
        // attached to a control at all, so they kept a one-line pointer on the panel — the short
        // keys here — with the reasoning behind the hover. The paragraphs themselves are unchanged:
        // they were good text in the wrong place, not bad text.
        ["perf_game_graphics_short"] = Pair("Shadows, textures, lighting → Options › Graphics",
                                            "Schatten, Texturen, Licht → Optionen › Grafik"),
        ["perf_config_short"] = Pair("Measurement + internal switches: config file",
                                     "Messung + interne Schalter: Konfigurationsdatei"),
        ["perf_game_graphics_note"] = Pair(
            "Shadows, textures, lighting and the game's own anti-aliasing stay in Options › Graphics — these two rows only add what VR needs and the game has no control for. Note the game's anti-aliasing is post-processing and is switched off in VR (Display › Disable post-processing).",
            "Schatten, Texturen, Beleuchtung und die spieleigene Kantenglättung bleiben unter Optionen › Grafik — diese beiden Regler ergänzen nur das, wofür das Spiel keinen Regler hat. Hinweis: die Kantenglättung des Spiels ist Post-Processing und ist in VR abgeschaltet (Anzeige › Post-Processing aus)."),
        ["perf_config_note"] = Pair(
            "Measurement logging and the internal A/B switches are not shown here — they change nothing you can see. They live in dev.gloomhavenvr.perf.cfg, the timing levers under Debug.",
            "Mess-Protokollierung und interne A/B-Schalter stehen nicht hier — sie ändern nichts Sichtbares. Sie liegen in dev.gloomhavenvr.perf.cfg, die Zeitgeber-Regler unter Debug."),

        // ---- SettingsPanel: Debug — timing levers moved out of the user-facing category ----
        ["subcat_timing"] = Pair("Timing / CPU", "Zeitgeber / CPU"),
        ["debug_timing_short"] = Pair("CPU intervals — not the stutter lever",
                                      "CPU-Intervalle — nicht der Ruckel-Regler"),
        ["debug_timing_note"] = Pair(
            "CPU-side refresh intervals. Measured at ~2.5% of frame time, so these are for finding defaults, not for fixing stutter — the GPU rows under Performance are the ones that move frames.",
            "CPU-seitige Auffrischungs-Intervalle. Gemessen bei ~2,5 % der Bildzeit — also zum Ermitteln guter Vorgaben, nicht gegen Ruckler; dafür sind die GPU-Regler unter Leistung zuständig."),
        ["debug_skip_scrub_draw"] = Pair("Skip the discarded desktop render", "Verworfenes Desktop-Rendering überspringen"),
        ["debug_skip_scrub_note"] = Pair(
            "In a scenario the game still renders the whole 3D scene a third time, into a texture nothing reads, so the monitor stays clean. This skips only that drawing. Expected to be invisible and to be the single largest GPU saving here — but untested on hardware, which is why it is off. If anything looks wrong in the headset, switch it back.",
            "Im Szenario rendert das Spiel die komplette 3D-Szene ein drittes Mal — in eine Textur, die niemand liest, nur damit der Monitor sauber bleibt. Dies überspringt genau dieses Zeichnen. Sollte unsichtbar sein und ist hier die größte einzelne GPU-Ersparnis — aber ungetestet, deshalb aus. Falls im Headset etwas falsch aussieht: wieder ausschalten."),
        ["debug_stereo_mode"] = Pair("Stereo mode (restart)", "Stereo-Modus (Neustart)"),
        ["debug_stereo_note"] = Pair(
            "MultiPass renders the scene once per eye. Single-Pass Instanced would halve that, but this game's shaders ship with no stereo variants (verified by disassembly), so it renders the right eye black/wrong. Leave on MultiPass; the setting exists for testing a future patched shader bundle. Takes effect on the next game start.",
            "MultiPass rendert die Szene einmal pro Auge. Single-Pass Instanced würde das halbieren, aber die Shader dieses Spiels enthalten keine Stereo-Varianten (per Disassembly belegt) — das rechte Auge bleibt schwarz/falsch. Auf MultiPass lassen; die Einstellung dient dem Test eines künftig gepatchten Shader-Bundles. Wirkt beim nächsten Spielstart."),

        // ---- SettingsPanel: Leistung — remaining row labels ----
        // The measurement labels that used to live here (perf_measurement / perf_enabled /
        // perf_interval / perf_attribution / perf_top_steps / perf_spikes / perf_spike_factor /
        // perf_spike_rate / perf_alloc / perf_xr) and the pure work-removal switches
        // (opt_cache_delegates / opt_map_icons / opt_figure_scan / opt_lean_strings /
        // opt_tooltip_gate / opt_quiet_diag) are GONE, with their rows: they change nothing the
        // player can see, so there is no compromise for a player to make and offering them
        // implied there was. They are config-file entries now (dev.gloomhavenvr.perf.cfg), which
        // is where a debug-phase instrument belongs. The three interval labels below survive
        // because their rows moved to the Debug pane, not because they are user settings.
        ["performance"] = Pair("Performance", "Leistung"),
        ["opt_fan_relayout"] = Pair("Card fan re-layout limit", "Kartenfächer-Neuaufbau max."),
        ["opt_wall_eval"] = Pair("Wall see-through check every", "Wand-Durchsicht prüfen alle"),
        ["opt_remote_content"] = Pair("Player board refresh", "Mitspieler-Board-Auffrischung"),

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
