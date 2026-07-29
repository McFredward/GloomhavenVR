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
///
/// <para>PARTIAL: the CONFIG-DESCRIPTION table (the ~490 bound settings' explanations, shown by
/// the in-VR config browser) lives in <c>Loc.ConfigDescriptions.cs</c> — same class, same
/// language resolution, just kept out of this file because it is long. See
/// <see cref="ConfigDescription"/>.</para>
/// </summary>
internal static partial class Loc
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
        // ---- 2026-07 menu restructure: the five sidebar tabs + the section headers ----------
        // NAMES ARE ONE WORD ON PURPOSE. The sidebar column is a PINNED 132 px (SettingsPanel
        // .BuildColumn) and its buttons render at fontSize 15 in a 30 px row, so a two-word tab
        // name wraps and clips. Every tab therefore gets a single noun that says what the player
        // is trying to DO: sit comfortably (Komfort), make the picture right (Grafik), deal with
        // the mod's own panels (Tafeln), decide how they look (Avatar), tune (Debug).
        // (The "Komfort" tab reuses the long-standing ["comfort"] key further up; "Tafeln" reuses
        // ["cat_panels"], "Avatar" reuses ["avatar"]. The two keys the restructure retired —
        // ["display"] and ["performance"] — are gone with the tabs they named, so a dead key cannot
        // suggest a tab that no longer exists.)
        ["cat_graphics"] = Pair("Graphics", "Grafik"),
        ["cat_debug"] = Pair("Debug", "Debug"),
        // The caption of the mod's tab in the GAME's own options window. Two words here, unlike the
        // one-word sidebar tabs above: this row is the game's full-width option list, not the
        // mod's pinned 132 px column, so it has the room the others did not.
        ["vr_options"] = Pair("VR Options", "VR Optionen"),
        // The sub-tabs of that tab. Two or three words each: the column is 210 px and the captions
        // shrink to fit rather than being cut, so they can afford to say what they mean.
        ["vr_cat_comfort"] = Pair("Comfort & movement", "Komfort & Bewegung"),
        ["vr_cat_hands"] = Pair("Hands & pointing", "Hände & Zeigen"),
        ["vr_cat_view"] = Pair("Appearance", "Darstellung"),
        ["vr_cat_table"] = Pair("Cards & board", "Karten & Brett"),
        ["vr_cat_net"] = Pair("Multiplayer", "Mehrspieler"),
        ["vr_cat_system"] = Pair("System & start", "System & Start"),
        ["vr_cat_advanced"] = Pair("Advanced", "Erweitert"),
        // ---- captions for the curated rows of the game-options VR tab -------------------------
        // A config key is a programmer's name: left alone these rows read "Actor Bars", "Masked
        // Reaim Deadband" — in German too, since the catalog's display name is only the key with
        // its camel humps spaced out. Rows whose caption already existed above reuse it.
        ["vr_o_snapdeg"] = Pair("Snap angle", "Sprungwinkel"),
        ["vr_o_smoothspeed"] = Pair("Turn speed", "Drehgeschwindigkeit"),
        ["vr_o_turnhand"] = Pair("Turning hand", "Dreh-Hand"),
        ["vr_o_vdrag"] = Pair("Drag vertically", "Senkrecht ziehen"),
        ["vr_o_rotate"] = Pair("Rotate the world", "Welt drehen"),
        ["vr_o_scale"] = Pair("Resize the world", "Welt skalieren"),
        ["vr_o_recenterhold"] = Pair("Recenter hold", "Zentrieren halten"),
        ["vr_o_worldtilt"] = Pair("World tilt", "Weltneigung"),
        ["vr_o_primaryhand"] = Pair("Dominant hand", "Dominante Hand"),
        ["vr_o_rayalways"] = Pair("Laser always on", "Laser immer an"),
        ["vr_o_laserorigin"] = Pair("Laser origin", "Laser-Ursprung"),
        ["vr_o_raycone"] = Pair("Laser cone", "Laser-Kegel"),
        ["vr_o_fog"] = Pair("Volumetric fog off", "Volumennebel aus"),
        ["vr_o_forward"] = Pair("Forward rendering", "Forward-Rendering"),
        ["vr_o_menurig"] = Pair("Main menu in VR", "Hauptmenü in VR"),
        ["vr_o_circle"] = Pair("Seat in a circle", "Im Kreis sitzen"),
        ["vr_o_hidesky"] = Pair("Hide the sky", "Himmel ausblenden"),
        ["vr_o_gfxjobs"] = Pair("Threaded submission", "Parallele Bildabgabe"),
        ["vr_o_autorestart"] = Pair("Restart automatically", "Automatisch neu starten"),
        ["vr_o_actorbars"] = Pair("Health bars", "Lebensbalken"),
        ["vr_o_buttoncluster"] = Pair("Wrist buttons", "Handgelenk-Tasten"),
        ["vr_o_dialogs"] = Pair("Dialogs in VR", "Dialoge in VR"),
        ["vr_o_decisiondock"] = Pair("Decision dock", "Entscheidungsleiste"),
        ["vr_o_enemyreveal"] = Pair("Enemy cards", "Gegnerkarten"),
        ["vr_o_trayscale"] = Pair("Board size", "Brettgröße"),
        ["vr_o_trayfollow"] = Pair("Board follows you", "Brett folgt dir"),
        ["vr_o_inspectscale"] = Pair("Close-up size", "Nahansicht"),
        ["vr_o_revealmode"] = Pair("Fan opens by", "Fächer öffnen"),
        ["vr_o_grabbutton"] = Pair("Grab button", "Greif-Taste"),
        ["vr_o_handcolor"] = Pair("Hand colour", "Handfarbe"),
        ["vr_o_handfwd"] = Pair("Hands forward / back", "Hände vor / zurück"),
        ["vr_o_grippitch"] = Pair("Grip angle", "Griffwinkel"),
        ["vr_o_netenabled"] = Pair("Multiplayer sync", "Mehrspieler-Abgleich"),
        // Boolean rows say what they ARE, the way the game's own settings do.
        ["vr_on"] = Pair("On", "Ein"),
        ["vr_off"] = Pair("Off", "Aus"),
        // ---- one-line hints for the curated rows (the config descriptions are written for
        // whoever reads the config FILE — hundreds of words with measurements in them, which a
        // tooltip could only clip mid-sentence. These say the one thing a player needs.) -------
        ["h_free_movement"] = Pair("Walk with the thumbstick instead of only teleporting.", "Mit dem Stick laufen statt nur zu teleportieren."),
        ["h_turning"] = Pair("Snap turns in fixed steps; smooth turns continuously.", "Sprung dreht in festen Schritten, weich dreht gleitend."),
        ["h_vr_o_snapdeg"] = Pair("How far one snap turn rotates you.", "Wie weit eine Sprungdrehung dich dreht."),
        ["h_vr_o_smoothspeed"] = Pair("How fast smooth turning rotates you.", "Wie schnell weiches Drehen dich dreht."),
        ["h_vr_o_turnhand"] = Pair("Which controller's stick turns you.", "Mit welchem Controller-Stick du dich drehst."),
        ["h_world_grab"] = Pair("Grip empty air to pull the whole table towards you.", "Ins Leere greifen, um den Tisch zu dir zu ziehen."),
        ["h_vr_o_vdrag"] = Pair("Let a world grab also move the table up and down.", "Erlaubt, den Tisch beim Greifen auch zu heben und zu senken."),
        ["h_vr_o_rotate"] = Pair("Let a two-handed grab turn the table.", "Erlaubt, den Tisch mit beiden Händen zu drehen."),
        ["h_vr_o_scale"] = Pair("Let a two-handed grab resize the table.", "Erlaubt, den Tisch mit beiden Händen zu skalieren."),
        ["h_table_height"] = Pair("Raises or lowers the whole table.", "Hebt oder senkt den ganzen Tisch."),
        ["h_vr_o_recenterhold"] = Pair("How long to hold B+Y before the view recentres.", "Wie lange B+Y gehalten wird, bis die Ansicht neu zentriert."),
        ["h_table_scale"] = Pair("How large the board is in front of you.", "Wie groß das Brett vor dir ist."),
        ["h_vr_o_worldtilt"] = Pair("Tips the table towards you so far edges are easier to see.", "Neigt den Tisch zu dir, damit ferne Ränder besser zu sehen sind."),
        ["h_vr_o_primaryhand"] = Pair("Which hand holds the laser and plays cards.", "Welche Hand den Laser führt und Karten spielt."),
        ["h_vr_o_rayalways"] = Pair("Keep the laser visible instead of only when aiming.", "Laser dauerhaft zeigen statt nur beim Zielen."),
        ["h_vr_o_laserorigin"] = Pair("Whether the laser leaves from the fingertip or the controller.", "Ob der Laser an der Fingerspitze oder am Controller ansetzt."),
        ["h_vr_o_raycone"] = Pair("How forgiving the laser is when aiming at menus.", "Wie großzügig der Laser Menüs trifft."),
        ["h_disable_post"] = Pair("Turns off the game's screen effects. Sharper, and cheaper.", "Schaltet die Bildschirmeffekte des Spiels ab. Schärfer und günstiger."),
        ["h_vr_o_fog"] = Pair("Removes the haze in rooms. Clearer view, less atmosphere.", "Entfernt den Dunst in Räumen. Klarere Sicht, weniger Stimmung."),
        ["h_wall_see_through"] = Pair("Fades walls that stand between you and the board.", "Blendet Wände aus, die zwischen dir und dem Brett stehen."),
        ["h_vr_o_forward"] = Pair("A cheaper render path. Helps weak hardware, changes lighting slightly.", "Günstigerer Renderpfad. Hilft schwacher Hardware, ändert die Beleuchtung leicht."),
        ["h_vr_o_menurig"] = Pair("Show the main menu as a screen in VR instead of flat.", "Zeigt das Hauptmenü als Fläche in VR statt flach."),
        ["h_vr_o_circle"] = Pair("Seats several players around the table instead of side by side.", "Setzt mehrere Spieler um den Tisch statt nebeneinander."),
        ["h_vr_o_gfxjobs"] = Pair("Spreads drawing over several CPU threads. The single biggest performance gain.", "Verteilt das Zeichnen auf mehrere CPU-Threads. Der größte Leistungsgewinn."),
        ["h_vr_o_autorestart"] = Pair("Restarts the game by itself the one time the setting above needs it.", "Startet das Spiel einmal selbst neu, wenn die Einstellung darüber es braucht."),
        ["h_mixed_reality"] = Pair("Clears the sky to one colour so your room can show through it.", "Färbt den Himmel einfarbig, damit dein Zimmer durchscheinen kann."),
        ["h_key_color"] = Pair("The colour your headset replaces with the room. Black keeps it simply dark.", "Die Farbe, die dein Headset durch das Zimmer ersetzt. Schwarz lässt es einfach dunkel."),
        ["h_vr_o_hidesky"] = Pair("Also removes the sky scenery, not just its colour.", "Entfernt auch die Himmelskulisse, nicht nur ihre Farbe."),
        ["h_show_combat_log"] = Pair("A floating panel listing what just happened.", "Eine schwebende Tafel mit dem, was gerade passiert ist."),
        ["h_vr_o_actorbars"] = Pair("Health and condition bars above figures.", "Lebens- und Zustandsbalken über den Figuren."),
        ["h_element_hints"] = Pair("Marks which elements an action would use or create.", "Zeigt, welche Elemente eine Aktion nutzt oder erzeugt."),
        ["h_vr_o_buttoncluster"] = Pair("Buttons on your wrist for the things you press most.", "Tasten am Handgelenk für das, was du am häufigsten drückst."),
        ["h_vr_o_dialogs"] = Pair("Show the game's dialogs as panels in front of you.", "Zeigt die Dialoge des Spiels als Tafeln vor dir."),
        ["h_vr_o_decisiondock"] = Pair("A bar within reach for choices you have to make.", "Eine Leiste in Reichweite für Entscheidungen."),
        ["h_vr_o_enemyreveal"] = Pair("Lays revealed enemy cards out where you can read them.", "Legt aufgedeckte Gegnerkarten so aus, dass du sie lesen kannst."),
        ["h_control_board"] = Pair("Which control board model sits in front of you.", "Welches Kontrollbrett-Modell vor dir steht."),
        ["h_vr_o_trayscale"] = Pair("How large the control board is.", "Wie groß das Kontrollbrett ist."),
        ["h_vr_o_trayfollow"] = Pair("The board stays in front of you instead of fixed in the room.", "Das Brett bleibt vor dir statt fest im Raum."),
        ["h_vr_o_inspectscale"] = Pair("How large a card gets when you hold it up to look at it.", "Wie groß eine Karte wird, wenn du sie zum Ansehen hochhältst."),
        ["h_vr_o_revealmode"] = Pair("Whether turning your wrist opens the card fan, or it is always out.", "Ob ein Drehen des Handgelenks den Fächer öffnet oder er immer offen ist."),
        ["h_vr_o_grabbutton"] = Pair("Which button picks a card up.", "Mit welcher Taste du eine Karte aufnimmst."),
        ["h_hands"] = Pair("How your hands look — gloves, bare, or a simple shape.", "Wie deine Hände aussehen — Handschuhe, bloß oder einfache Form."),
        ["h_vr_o_handcolor"] = Pair("The colour of your hands.", "Die Farbe deiner Hände."),
        ["h_vr_o_handfwd"] = Pair("Moves your hands forward or back if they sit wrong.", "Verschiebt die Hände vor oder zurück, wenn sie falsch sitzen."),
        ["h_hand_y"] = Pair("Moves your hands up or down if they sit wrong.", "Verschiebt die Hände hoch oder runter, wenn sie falsch sitzen."),
        ["h_hand_x"] = Pair("Moves your hands left or right if they sit wrong.", "Verschiebt die Hände nach links oder rechts, wenn sie falsch sitzen."),
        ["h_vr_o_grippitch"] = Pair("Tilts your hands to match how you hold the controller.", "Neigt die Hände passend dazu, wie du den Controller hältst."),
        ["h_head_mask"] = Pair("The mask other players see on your face.", "Die Maske, die andere Spieler in deinem Gesicht sehen."),
        ["h_mask_size"] = Pair("How large your mask is.", "Wie groß deine Maske ist."),
        ["h_mirror"] = Pair("A mirror in front of you so you can see your own mask and hands.", "Ein Spiegel vor dir, damit du Maske und Hände selbst siehst."),
        ["h_vr_o_netenabled"] = Pair("Send your head and hand movement to the other players.", "Sendet deine Kopf- und Handbewegung an die anderen Spieler."),
        ["h_remote_boards"] = Pair("Show the other players' control boards as well as your own.", "Zeigt auch die Kontrollbretter der anderen Spieler."),
        ["vr_needs_restart"] = Pair("Takes effect at the next game start.", "Wirkt erst beim nächsten Spielstart."),


        // Section headers inside the mod's tab of the game options window.
        ["vr_sec_performance"] = Pair("Performance", "Leistung"),
        ["vr_sec_panels"] = Pair("Panels & readouts", "Tafeln & Anzeigen"),
        ["vr_sec_cards"] = Pair("Cards & board", "Karten & Brett"),
        // Section headers WITHIN a tab — one navigation level cheaper than another tab (24 px per
        // group instead of a sidebar entry), which is why the restructure uses them for grouping
        // and keeps the tab count at five.
        ["sec_table_world"] = Pair("Table & world", "Tisch & Welt"),
        ["sec_movement"] = Pair("Movement & turning", "Bewegung & Drehen"),
        ["sec_hands_aim"] = Pair("Hands & aiming", "Hände & Zielen"),
        ["sec_presentation"] = Pair("Presentation", "Darstellung"),
        ["sec_appearance"] = Pair("Appearance", "Aussehen"),
        ["sec_multiplayer"] = Pair("Multiplayer", "Mehrspieler"),
        // Rows whose captions used to be German literals in SettingsPanel.3.Content.cs. They move
        // between tabs in this restructure, so they are localized on the way (the whole mod is
        // localized — a moved row must not arrive as a hardcoded string).
        ["world_tilt"] = Pair("World tilt", "Welt-Neigung"),
        ["wall_see_through"] = Pair("See-through walls", "Wände durchsichtig"),
        ["element_hints"] = Pair("Element hints", "Element-Hinweise"),

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
        // The Debug pane's FIRST navigation level. It was a hardcoded German literal — the one
        // caption in the panel that an English player got in German, and the caption that has to be
        // read to discover that the sub-category list expands at all.
        ["area"] = Pair("Area", "Bereich"),
        ["size"] = Pair("Size", "Größe"),
        ["tilt_yaw"] = Pair("Tilt/Yaw", "Neigung/Gieren"),
        ["reset_element"] = Pair("Reset element", "Element zurücksetzen"),
        ["objectives"] = Pair("Objectives", "Aufgaben"),
        // Caption of the (idle) shared decision drawer drawn on a REMOTE player's control board —
        // the reserved strip where their take-damage / dialog prompts dock on their own client.
        ["decision_dock"] = Pair("Decisions", "Entscheidungen"),
        ["elements"] = Pair("Elements", "Elemente"),
        // The two board DASHBOARD keys. Both name BUTTONS, and the 2026-07 restructure moved them
        // out of Debug ▸ "Board & Layout" into Debug ▸ "Tasten" — the user's literal complaint was
        // that buttons were not to be found under buttons. The captions say (position) because the
        // very same two caps carry their SIZE under "Zahnrad & Fixiert (Größe)"; the pair of
        // qualifiers is what keeps the two rows from reading as duplicates of each other. German
        // "Fixiert" is the word engraved on the plate itself (Loc "pinned"), not a new term.
        ["vr_settings"] = Pair("Gear button (position)", "Zahnrad-Knopf (Position)"),
        ["pin"] = Pair("Pin button (position)", "Fixiert-Knopf (Position)"),
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
        ["wall_fade_note"] = Pair("needs 'See-through walls' (Graphics) switched on",
                                  "wirkt nur bei eingeschaltetem \"Wände durchsichtig\" (Grafik)"),
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
        ["board_dashboard"] = Pair("Gear & Pin (size)", "Zahnrad & Fixiert (Größe)"),
        ["wall_fade"] = Pair("Wall see-through", "Wandüberblendung"),
        ["hand_offsets"] = Pair("Hand offsets", "Hände-Offsets"),
        ["figure_offsets"] = Pair("Figure offsets", "Figuren-Offsets"),
        ["button_colors"] = Pair("Button colors", "Knopf-Farben"),
        ["subcat_board_layout"] = Pair("Board & Layout", "Board & Layout"),
        ["subcat_cards_piles"] = Pair("Cards & Piles", "Karten & Stapel"),
        ["subcat_offsets"] = Pair("Hands & offsets", "Hände & Offsets"),
        // 2026-07 restructure: the wall-fade fractions used to be a sub-category of their OWN with
        // exactly one element — a navigation level with nothing to choose. They now share this
        // sub-category with the timing/CPU levers, which is honest about what both are: dials you
        // turn to FIND a good default, not settings a player decides between. The single-element
        // name stays as the ELEMENT caption (Loc "wall_fade").
        ["subcat_perf_effects"] = Pair("Performance & effects", "Leistung & Effekte"),

        // ---- Debug ▸ Alle Einstellungen — the generic config browser (2026-07) ----------------
        // User: "Alle config einstellungen sollen im VR Menu anpassbar sein!" — nothing may be
        // config-file-only any more. These are the browser's OWN chrome; the entry DESCRIPTIONS
        // it shows on hover are localized separately, keyed by section/key, in
        // Loc.ConfigDescriptions.cs (user 2026-07: "Mach alles auf die jeweilige lokalisierte
        // Sprache" — a tooltip must never be half English, half German).
        ["subcat_all_settings"] = Pair("All settings", "Alle Einstellungen"),
        ["cfg_group"] = Pair("Group", "Gruppe"),
        ["cfg_page"] = Pair("Page", "Seite"),
        ["cfg_step"] = Pair("Step size", "Schrittweite"),
        ["cfg_step_fine"] = Pair("Fine", "Fein"),
        ["cfg_step_normal"] = Pair("Normal", "Normal"),
        ["cfg_step_coarse"] = Pair("Coarse", "Grob"),
        ["cfg_default"] = Pair("Default", "Standard"),
        ["cfg_range"] = Pair("Range", "Bereich"),
        ["cfg_empty"] = Pair("(empty)", "(leer)"),
        ["cfg_none"] = Pair("no entries", "keine Einträge"),
        ["cfg_group_misc"] = Pair("General", "Allgemein"),
        ["cfg_live_note"] = Pair(
            "Applies live for every consumer that re-reads the entry each frame/tick — which is "
            + "most of them. A few only take effect when the thing they describe is next rebuilt "
            + "(rig, control board, a panel); if nothing changes, close and reopen that thing.",
            "Wirkt sofort bei allen Verbrauchern, die den Wert pro Frame/Tick neu lesen — das ist "
            + "die Mehrheit. Einzelne greifen erst, wenn das betroffene Ding neu aufgebaut wird "
            + "(Rig, Kontrollbrett, eine Tafel); passiert nichts, schließe und öffne es erneut."),
        ["cfg_needs_restart"] = Pair(
            "NOT live: this entry is read once at startup. The value is saved immediately, but it "
            + "takes effect on the next game/VR start.",
            "NICHT sofort: dieser Wert wird nur beim Start gelesen. Er wird sofort gespeichert, "
            + "greift aber erst beim nächsten Spiel-/VR-Start."),
        ["cfg_readonly_note"] = Pair(
            "Free text with no fixed set of valid values, so the panel shows it instead of "
            + "pretending to edit it. Change it in the config file named above.",
            "Freier Text ohne feste Auswahl — deshalb zeigt die Tafel den Wert an, statt eine "
            + "Bearbeitung vorzutäuschen. Änderbar in der oben genannten Konfigurationsdatei."),
        ["cfg_readonly_short"] = Pair("file only", "nur Datei"),
        ["cfg_footer_note"] = Pair(
            "Entries in this group / entries the whole mod binds · how many of them are free text "
            + "with no fixed set of values, which the panel shows but does not edit.\n\n"
            + "Every setting the mod binds is listed here, grouped by what it is about rather than "
            + "by which file it happens to live in. An entry added to the mod later shows up by "
            + "itself — this pane reads the bound entries, it does not hold a copy of them.",
            "Einträge in dieser Gruppe / Einträge im ganzen Mod · davon freier Text ohne feste "
            + "Auswahl, den die Tafel anzeigt, aber nicht bearbeitet.\n\n"
            + "Hier steht jede Einstellung, die der Mod bindet — gruppiert nach Thema statt nach "
            + "Datei. Ein später hinzugefügter Eintrag erscheint von selbst: diese Tafel liest die "
            + "gebundenen Einträge, sie führt keine eigene Liste."),
        ["cfg_topic_diagnostics"] = Pair("Measurement & diagnostics", "Messung & Diagnose"),
        ["cfg_topic_visual"] = Pair("Picture & rendering", "Bild & Darstellung"),
        ["cfg_topic_movement"] = Pair("Movement & world", "Bewegung & Welt"),
        ["cfg_topic_hands"] = Pair("Hands & figures", "Hände & Figuren"),
        ["cfg_topic_cards"] = Pair("Cards & fan", "Karten & Fächer"),
        ["cfg_topic_panels"] = Pair("Menus & panels", "Menüs & Tafeln"),
        ["cfg_topic_board"] = Pair("Board & targeting", "Brett & Zielen"),
        ["cfg_topic_board_geometry"] = Pair("Board geometry (per board)", "Brett-Geometrie (pro Brett)"),
        ["cfg_topic_network"] = Pair("Multiplayer", "Mehrspieler"),
        ["cfg_topic_system"] = Pair("System & start-up", "System & Start"),
        ["cfg_topic_other"] = Pair("Other", "Sonstiges"),
        ["cfg_sec_perf"] = Pair("Measurement [Perf]", "Messung [Perf]"),
        ["cfg_sec_optimize"] = Pair("Optimizations [Optimize]", "Optimierungen [Optimize]"),
        ["cfg_sec_dev"] = Pair("Dev harness", "Entwickler-Werkzeuge"),
        ["cfg_sec_comfort"] = Pair("Comfort", "Komfort"),
        ["cfg_sec_settingspanel"] = Pair("This panel", "Diese Tafel"),
        ["cfg_sec_buttonanim"] = Pair("Button animation", "Knopf-Animation"),
        ["cfg_sec_transient"] = Pair("Transient buttons", "Kurzzeit-Knöpfe"),
        ["cfg_sec_squarecaps"] = Pair("Square caps", "Eckige Kappen"),
        ["cfg_sec_hexhighlight"] = Pair("Hex highlight", "Feld-Hervorhebung"),
        ["cfg_sec_selectionready"] = Pair("Selection reminder", "Auswahl-Erinnerung"),
        ["cfg_sec_renderquality"] = Pair("Render quality", "Bildqualität"),

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
            "Shadows, textures, lighting and the game's own anti-aliasing stay in Options › Graphics — these two rows only add what VR needs and the game has no control for. Note the game's anti-aliasing is post-processing and is switched off in VR ('Disable post-processing', further up on this tab).",
            "Schatten, Texturen, Beleuchtung und die spieleigene Kantenglättung bleiben unter Optionen › Grafik — diese beiden Regler ergänzen nur das, wofür das Spiel keinen Regler hat. Hinweis: die Kantenglättung des Spiels ist Post-Processing und ist in VR abgeschaltet (\"Post-Processing aus\", weiter oben auf dieser Seite)."),
        ["perf_config_note"] = Pair(
            "Measurement logging and the internal A/B switches are not shown here — they change nothing you can see. They live in dev.gloomhavenvr.perf.cfg, the timing levers under Debug.",
            "Mess-Protokollierung und interne A/B-Schalter stehen nicht hier — sie ändern nichts Sichtbares. Sie liegen in dev.gloomhavenvr.perf.cfg, die Zeitgeber-Regler unter Debug."),

        // ---- SettingsPanel: Debug — timing levers moved out of the user-facing category ----
        ["subcat_timing"] = Pair("Timing / CPU", "Zeitgeber / CPU"),
        ["debug_timing_short"] = Pair("CPU intervals — not the stutter lever",
                                      "CPU-Intervalle — nicht der Ruckel-Regler"),
        ["debug_timing_note"] = Pair(
            "CPU-side refresh intervals. Measured at ~2.5% of frame time, so these are for finding defaults, not for fixing stutter — the GPU rows under Graphics are the ones that move frames.",
            "CPU-seitige Auffrischungs-Intervalle. Gemessen bei ~2,5 % der Bildzeit — also zum Ermitteln guter Vorgaben, nicht gegen Ruckler; dafür sind die GPU-Regler unter Grafik zuständig."),
        ["debug_depth_prepass"] = Pair("Depth pass for VFX fading", "Tiefenpass für Effekt-Überblendung"),
        ["debug_depth_prepass_note"] = Pair(
            "On, the mod draws the whole scene a second time per eye just to build a depth image. That image is what makes torch glow and smoke fade correctly at walls instead of shining through them. Off removes that second drawing — the largest single piece of drawing work the mod itself adds — and brings the glow-through-walls artefact back. On by default; switch it off to see what it is worth in frames.",
            "An zeichnet der Mod die gesamte Szene ein zweites Mal pro Auge, nur um ein Tiefenbild zu erzeugen. Dieses Bild sorgt dafür, dass Fackelschein und Rauch an Wänden korrekt ausblenden statt hindurchzuscheinen. Aus entfernt dieses zweite Zeichnen — der größte einzelne Zeichenaufwand, den der Mod selbst verursacht — und bringt den Durchschein-Fehler zurück. Standardmäßig an; zum Ausprobieren ausschalten und die Bildrate vergleichen."),
        ["debug_head_mask_scenario"] = Pair("Draw only what the game draws", "Nur zeichnen, was das Spiel zeichnet"),
        ["debug_head_mask_scenario_note"] = Pair(
            "In a scenario the VR camera currently draws every one of the 32 object layers, because the camera it copies its settings from does. The camera the flat game really renders the dungeon with skips thirteen of them. On, the VR camera copies that shorter list instead — the mod's own objects and the interface are always kept. If something disappears in the headset, switch it back off. Off by default until it has been measured.",
            "Im Szenario zeichnet die VR-Kamera derzeit alle 32 Objektebenen, weil die Kamera, von der sie ihre Einstellungen übernimmt, das ebenfalls tut. Die Kamera, mit der das flache Spiel den Dungeon tatsächlich rendert, lässt dreizehn davon aus. An übernimmt die VR-Kamera diese kürzere Liste — die Objekte des Mods und die Oberfläche bleiben immer erhalten. Falls im Headset etwas verschwindet: wieder ausschalten. Standardmäßig aus, bis es gemessen wurde."),
        ["debug_stereo_mode"] = Pair("Stereo mode (restart)", "Stereo-Modus (Neustart)"),
        ["debug_stereo_note"] = Pair(
            "MultiPass renders the scene once per eye. Single-Pass Instanced would halve that, but this game's shaders ship with no stereo variants (verified by disassembly), so it renders the right eye black/wrong. Leave on MultiPass; the setting exists for testing a future patched shader bundle. Takes effect on the next game start.",
            "MultiPass rendert die Szene einmal pro Auge. Single-Pass Instanced würde das halbieren, aber die Shader dieses Spiels enthalten keine Stereo-Varianten (per Disassembly belegt) — das rechte Auge bleibt schwarz/falsch. Auf MultiPass lassen; die Einstellung dient dem Test eines künftig gepatchten Shader-Bundles. Wirkt beim nächsten Spielstart."),

        // ---- SettingsPanel: Debug ▸ Leistung & Effekte ▸ Bündelung (2026-07, experimental) ----
        // WORDING RULE, same as the render-trade block above: name the EFFECT and the RISK, never
        // the mechanism. A player has never heard of static batching and does not need to — what
        // they need to know is "this merges the dungeon's objects so the graphics card is asked
        // fewer times", "it costs memory", and "it can be undone". The word "Batching" survives
        // only in the config file and the log, where it has to match Unity's own vocabulary.
        ["batching"] = Pair("Object merging", "Objekt-Bündelung"),
        // 2026-07-28: this block used to say "never been through a hardware round" and to present
        // merging as THE answer to the judder. Both became false the same evening — it ran, it
        // worked, and it was then superseded by graphics jobs (see .planning/perf/FINDINGS.md).
        // User-facing text that oversells a setting is worse than none, so it now says what the
        // measurement says.
        ["batch_experimental_short"] = Pair("Experimental — rarely needed, reversible at any time",
                                            "Experimentell — selten nötig, jederzeit umkehrbar"),
        ["batch_experimental_note"] = Pair(
            "This is the only setting in the mod that changes the game's own objects rather than how they are drawn. It merges the dungeon's many separate pieces into a few large ones so the graphics card is asked to draw far fewer times. It works — measured, it merged 1666 objects and cut the draw requests ninefold — but it was overtaken by a much bigger fix: threaded render submission (Core › EnableGraphicsJobs), which removed the bottleneck this was fighting. So you probably do not need this. It may still help on weak hardware. Costs about 100 MB and a brief pause when a scenario loads. Everything it does is recorded and undoable: set it back to off, press Undo, or load another scenario.",
            "Dies ist die einzige Einstellung des Mods, die die Objekte des Spiels selbst verändert statt nur ihrer Darstellung. Sie fasst die vielen Einzelteile des Dungeons zu wenigen großen zusammen, sodass die Grafikkarte deutlich seltener zum Zeichnen aufgefordert wird. Sie funktioniert — gemessen wurden 1666 zusammengefasste Objekte und neunmal weniger Zeichenaufrufe —, wurde aber von etwas viel Größerem überholt: der parallelen Bildabgabe (Core › EnableGraphicsJobs), die den Engpass beseitigt hat, gegen den diese Funktion ankämpfte. Du brauchst sie also vermutlich nicht. Auf schwacher Hardware kann sie trotzdem helfen. Kostet rund 100 MB und eine kurze Pause beim Laden eines Szenarios. Alles ist protokolliert und umkehrbar: wieder auf Aus stellen, „Rückgängig\" drücken oder ein anderes Szenario laden."),
        ["batch_mode"] = Pair("Mode", "Modus"),
        ["batch_mode_probe"] = Pair("Measure only", "Nur messen"),
        ["batch_mode_on"] = Pair("On", "An"),
        ["batch_mode_note"] = Pair(
            "Off changes nothing and hands back anything already merged. 'Measure only' looks at the scene and writes one line to the log saying how much COULD be merged and what it would cost — it touches nothing at all, so it is the safe first step. On does the merging. Switching back down to 'Measure only' or Off undoes it immediately.",
            "Aus ändert nichts und gibt bereits Zusammengefasstes wieder frei. „Nur messen\" untersucht die Szene und schreibt eine Zeile ins Protokoll, wie viel zusammengefasst werden KÖNNTE und was das kosten würde — es wird nichts angefasst, also der sichere erste Schritt. An führt es aus. Zurück auf „Nur messen\" oder Aus macht es sofort rückgängig."),
        ["batch_status"] = Pair("Merged / meshes / memory", "Zusammengefasst / Meshes / Speicher"),
        ["batch_status_note"] = Pair(
            "Before a merge this reads 'suitable objects / objects examined' — the answer 'Measure only' produces. After one it reads how many objects were merged, into how many large meshes, and how much extra memory that costs. The memory is real and it is the price of the feature: the merged copy exists alongside the originals, which are kept precisely so this can be undone.",
            "Vor einem Durchlauf steht hier „geeignete Objekte / untersuchte Objekte\" — das Ergebnis von „Nur messen\". Danach steht dort, wie viele Objekte zu wie vielen großen Meshes zusammengefasst wurden und wie viel zusätzlichen Speicher das kostet. Dieser Speicher ist real und der Preis der Funktion: die zusammengefasste Kopie existiert zusätzlich zu den Originalen, die genau deshalb erhalten bleiben, damit sich alles rückgängig machen lässt."),
        ["batch_drawcalls"] = Pair("Draw requests", "Zeichenaufrufe"),
        ["batch_drawcalls_note"] = Pair(
            "How often the graphics card is asked to draw something, counting only the objects that were merged — before, against the estimate after. This is the number the whole experiment is about, and it is doubled in VR because each eye is drawn separately. It is an ESTIMATE; the honest answer is the head-camera figure in the performance log after the change.",
            "Wie oft die Grafikkarte etwas zeichnen soll, gezählt nur über die zusammengefassten Objekte — vorher gegen die Schätzung danach. Um diese Zahl geht es bei dem ganzen Versuch, und in VR zählt sie doppelt, weil jedes Auge einzeln gezeichnet wird. Es ist eine SCHÄTZUNG; die ehrliche Antwort steht im Leistungsprotokoll als Wert der Kopfkamera nach der Umstellung."),
        ["batch_apply_now"] = Pair("Apply now", "Jetzt anwenden"),
        ["batch_revert"] = Pair("Undo", "Rückgängig"),
        ["batch_actions_note"] = Pair(
            "'Apply now' re-examines the scene and merges again — useful after opening new rooms, or to re-measure while in 'Measure only'. 'Undo' hands every object its own shape back and frees the extra memory, without switching the mode off, so the next 'Apply now' still works. Both are safe to press at any time.",
            "„Jetzt anwenden\" untersucht die Szene erneut und fasst wieder zusammen — nützlich nach dem Öffnen neuer Räume oder zum erneuten Messen in „Nur messen\". „Rückgängig\" gibt jedem Objekt seine eigene Form zurück und den Zusatzspeicher frei, ohne den Modus abzuschalten — „Jetzt anwenden\" funktioniert danach weiter. Beides ist jederzeit gefahrlos drückbar."),
        ["batch_max_vertices"] = Pair("Memory limit", "Speichergrenze"),
        ["batch_max_vertices_note"] = Pair(
            "The ceiling on the extra memory the merge may use. Objects are taken until this is spent and the rest are left alone, so raising it merges more of the map and lowering it merges less. A scenario is unlikely to need more than a few hundred MB; the log always says how much was actually used and how many objects were left out.",
            "Die Obergrenze für den Zusatzspeicher der Zusammenfassung. Objekte werden aufgenommen, bis sie aufgebraucht ist, der Rest bleibt unangetastet — höher fasst mehr von der Karte zusammen, niedriger weniger. Ein Szenario braucht selten mehr als ein paar hundert MB; das Protokoll nennt immer den tatsächlichen Verbrauch und die Zahl der ausgelassenen Objekte."),
        ["batch_settle"] = Pair("Delay after loading", "Verzögerung nach dem Laden"),
        ["batch_settle_note"] = Pair(
            "How long to wait after a scenario loads before merging. The dungeon is built piece by piece over several frames, and a piece that does not exist yet cannot be merged — so the wait is what makes one pass cover the whole map instead of racing the builder. The merge itself is a brief one-off pause; this delay puts it inside the loading screen.",
            "Wie lange nach dem Laden eines Szenarios gewartet wird. Der Dungeon wird über mehrere Bilder hinweg Stück für Stück aufgebaut, und ein noch nicht existierendes Stück kann nicht zusammengefasst werden — die Wartezeit sorgt dafür, dass ein Durchlauf die ganze Karte erfasst, statt dem Aufbau davonzulaufen. Das Zusammenfassen selbst ist eine kurze einmalige Pause; diese Verzögerung legt sie in den Ladebildschirm."),
        ["batch_rescan"] = Pair("Re-check for new rooms", "Auf neue Räume prüfen"),
        ["batch_rescan_note"] = Pair(
            "How often to look for parts of the map that have appeared since the last merge — rooms you have opened, props that spawned. The check itself is cheap; it only merges again once enough new pieces have turned up. Off means one merge per scenario and anything opened later simply stays unmerged, which is correct, just not faster.",
            "Wie oft nach Kartenteilen gesucht wird, die seit dem letzten Durchlauf hinzugekommen sind — geöffnete Räume, neu erschienene Objekte. Die Prüfung selbst ist günstig; zusammengefasst wird erst wieder, wenn genug Neues aufgetaucht ist. Aus bedeutet einen Durchlauf pro Szenario; später Geöffnetes bleibt dann einfach unzusammengefasst — korrekt, nur nicht schneller."),
        ["batch_min_renderers"] = Pair("Minimum objects", "Mindestanzahl Objekte"),
        ["batch_min_renderers_note"] = Pair(
            "A part of the map offering fewer suitable objects than this is left alone. Merging a handful of things costs memory and buys nothing measurable, and every merged object is one more thing that has to be handed back on undo.",
            "Ein Kartenteil mit weniger geeigneten Objekten als hier angegeben bleibt unangetastet. Eine Handvoll Dinge zusammenzufassen kostet Speicher und bringt nichts Messbares — und jedes zusammengefasste Objekt ist eines mehr, das beim Rückgängigmachen zurückgegeben werden muss."),
        ["batch_auto_roots"] = Pair("Find the map on its own", "Karte selbst finden"),
        ["batch_auto_roots_note"] = Pair(
            "If the part of the scene named in the settings does not exist in this scenario, look for the parts holding the most objects and use those instead. On by default, because that name is free text and free text can only be READ in this menu, not typed — without this, a scenario that names its map differently would leave the whole feature quietly doing nothing with no way to fix it from in here. It is not a shot in the dark: the mod's own objects are skipped, at most four parts are taken, and the log names every one it picked and every one it considered — so the right name can simply be read off it.",
            "Existiert der in den Einstellungen genannte Teil der Szene in diesem Szenario nicht, nach den Teilen mit den meisten Objekten suchen und diese stattdessen verwenden. Standardmäßig an, denn jener Name ist Freitext, und Freitext lässt sich in diesem Menü nur LESEN, nicht eingeben — ohne dies würde ein Szenario mit anders benannter Karte die ganze Funktion still ins Leere laufen lassen, ohne Korrekturmöglichkeit von hier aus. Es ist kein Schuss ins Blaue: die Objekte des Mods werden übersprungen, höchstens vier Teile werden genommen, und das Protokoll nennt jeden gewählten und jeden geprüften Kandidaten — der richtige Name lässt sich also einfach ablesen."),
        ["batch_include_inactive"] = Pair("Include unopened rooms", "Unentdeckte Räume einbeziehen"),
        ["batch_include_inactive_note"] = Pair(
            "Also merge rooms you have not opened yet. On, a single pass at the start of a scenario covers the whole map and opening a room does not drop it back out. Off restricts the merge to what is on screen at the time — the cautious reading, worth trying if a scenario turns out to rebuild its rooms on reveal rather than just show them.",
            "Auch noch nicht geöffnete Räume zusammenfassen. An erfasst ein einziger Durchlauf zu Szenariobeginn die ganze Karte, und das Öffnen eines Raums nimmt ihn nicht wieder heraus. Aus beschränkt die Zusammenfassung auf das gerade Sichtbare — die vorsichtige Lesart, einen Versuch wert, falls ein Szenario seine Räume beim Aufdecken neu aufbaut statt sie nur einzublenden."),
        ["batch_free_cpu"] = Pair("Release the spare copy", "Zweitkopie freigeben"),
        ["batch_free_cpu_note"] = Pair(
            "After the merged shapes have been handed to the graphics card, throw away the copy in main memory. This halves what the feature costs and cannot change anything you see — the graphics card's copy is what gets drawn — and it does not affect the undo, which restores the originals and deletes the merged shapes without ever reading them back. Leave on unless the merged shapes need inspecting.",
            "Nachdem die zusammengefassten Formen an die Grafikkarte übergeben wurden, die Kopie im Arbeitsspeicher verwerfen. Das halbiert die Kosten der Funktion und kann nichts Sichtbares ändern — gezeichnet wird die Kopie der Grafikkarte — und es beeinträchtigt das Rückgängigmachen nicht, das die Originale wiederherstellt und die zusammengefassten Formen löscht, ohne sie je zu lesen. An lassen, außer die zusammengefassten Formen sollen untersucht werden."),
        ["batch_watchdog"] = Pair("Movement watch", "Bewegungswächter"),
        ["batch_watchdog_note"] = Pair(
            "Watch merged objects for movement. This is the one way the feature can go visibly wrong: a merged object's shape is baked in place, so if the game ever moves one it is drawn where it used to be. The watch samples merged objects a few dozen times a second, names any offender in the log, and moving the whole table (world grab) is correctly not counted as movement.",
            "Zusammengefasste Objekte auf Bewegung überwachen. Das ist der eine Weg, auf dem die Funktion sichtbar schiefgehen kann: die Form eines zusammengefassten Objekts ist ortsfest eingebacken — bewegt das Spiel es doch, wird es an seiner alten Stelle gezeichnet. Der Wächter prüft einige Dutzend Objekte pro Sekunde und nennt den Verursacher im Protokoll; das Verschieben des ganzen Tisches (Welt-Griff) zählt korrekterweise nicht als Bewegung."),
        ["batch_watchdog_auto"] = Pair("Undo automatically", "Automatisch rückgängig"),
        ["batch_watchdog_auto_note"] = Pair(
            "When the watch sees a merged object move, undo everything on the spot instead of only writing it to the log. On is the safe choice — the alternative is a correct log entry next to a wrong picture. Turn it off only to keep the wrong picture long enough to photograph the object that caused it.",
            "Wenn der Wächter ein zusammengefasstes Objekt in Bewegung sieht, alles sofort rückgängig machen statt es nur zu protokollieren. An ist die sichere Wahl — die Alternative ist ein korrekter Protokolleintrag neben einem falschen Bild. Nur ausschalten, um das falsche Bild lange genug zu behalten, um das verursachende Objekt zu fotografieren."),
        ["batch_auto_exclude"] = Pair("Learn from what moved", "Aus Bewegtem lernen"),
        ["batch_auto_exclude_note"] = Pair(
            "When the watch catches something moving, remember its name and try again without it, instead of just giving up. This is what makes the feature usable: on the first hardware run it merged 1666 objects and cut the draw requests by a factor of nine — and was then correctly undone one second later because a few torch flames move. Names are remembered in the config file, so the next scenario starts out already knowing them, and the log names every one it adds. On by default; a name that turns out to exclude too much can simply be deleted from the file.",
            "Erwischt der Wächter etwas in Bewegung, dessen Namen merken und es ohne dieses erneut versuchen, statt einfach aufzugeben. Erst das macht die Funktion brauchbar: im ersten Hardware-Lauf wurden 1666 Objekte zusammengefasst und die Zeichenaufrufe um den Faktor neun gesenkt — und eine Sekunde später korrekt wieder zurückgenommen, weil sich ein paar Fackelflammen bewegen. Die Namen werden in der Konfigurationsdatei gemerkt, das nächste Szenario startet also bereits mit diesem Wissen, und das Protokoll nennt jeden neuen Eintrag. Standardmäßig an; ein Name, der sich als zu weit gefasst erweist, lässt sich einfach aus der Datei löschen."),
        ["batch_unavailable"] = Pair("Unavailable on this build — measuring only",
                                     "Auf diesem Build nicht verfügbar — es wird nur gemessen"),
        ["batch_unavailable_note"] = Pair(
            "The merge can only run when the mod can prove it is able to undo it, and on this build it cannot: one of the engine functions the undo needs was not found. So nothing is merged and nothing is changed — 'Measure only' still works and still writes its log line. The exact missing piece follows.",
            "Zusammengefasst wird nur, wenn der Mod nachweisen kann, dass er es auch rückgängig machen kann — und auf diesem Build kann er das nicht: eine der dafür nötigen Engine-Funktionen wurde nicht gefunden. Es wird also nichts zusammengefasst und nichts verändert; „Nur messen\" funktioniert weiterhin und schreibt weiterhin seine Protokollzeile. Das genaue fehlende Teil folgt."),
        ["batch_suspended"] = Pair("Paused for this scenario — something in it moves",
                                   "Für dieses Szenario ausgesetzt — hier bewegt sich etwas"),
        ["batch_suspended_note"] = Pair(
            "Something in this scenario moves after being merged, so the movement watch has undone the merge several times. Rather than merge and undo over and over, it has stopped for this scenario. The log names the object that caused it: put a piece of its name into the exception list (Debug › All settings › Picture › Object merging) and switch the mode off and on again to try once more. Loading another scenario also clears this.",
            "In diesem Szenario bewegt sich etwas, nachdem es zusammengefasst wurde, weshalb der Bewegungswächter die Zusammenfassung mehrfach rückgängig gemacht hat. Statt endlos zusammenzufassen und zurückzunehmen, ist die Funktion für dieses Szenario ausgesetzt. Das Protokoll nennt das verursachende Objekt: einen Teil seines Namens in die Ausnahmeliste eintragen (Debug › Alle Einstellungen › Bild › Objekt-Bündelung) und den Modus einmal aus- und wieder einschalten, um es erneut zu versuchen. Ein anderes Szenario zu laden setzt es ebenfalls zurück."),
        ["batch_more_short"] = Pair("Areas and exceptions → Debug › All settings",
                                    "Bereiche und Ausnahmen → Debug › Alle Einstellungen"),
        ["batch_more_note"] = Pair(
            "Which parts of the scene may be merged, and which layers or object names to leave out, are free text — they are edited under Debug › All settings › Picture › Object merging, or directly in dev.gloomhavenvr.batching.cfg. They are only needed when the log names an object that has to be excluded.",
            "Welche Teile der Szene zusammengefasst werden dürfen und welche Ebenen oder Objektnamen auszunehmen sind, ist Freitext — bearbeitbar unter Debug › Alle Einstellungen › Bild › Objekt-Bündelung oder direkt in dev.gloomhavenvr.batching.cfg. Nötig nur, wenn das Protokoll ein Objekt nennt, das ausgenommen werden muss."),

        // ---- SettingsPanel: Leistung — remaining row labels ----
        // The measurement labels that used to live here (perf_measurement / perf_enabled /
        // perf_interval / perf_attribution / perf_top_steps / perf_spikes / perf_spike_factor /
        // perf_spike_rate / perf_alloc / perf_xr) and the pure work-removal switches
        // (opt_cache_delegates / opt_map_icons / opt_figure_scan / opt_lean_strings /
        // opt_tooltip_gate / opt_quiet_diag) are GONE, with their rows: they change nothing the
        // player can see, so there is no compromise for a player to make and offering them
        // implied there was. They are config-file entries now (dev.gloomhavenvr.perf.cfg), which
        // is where a debug-phase instrument belongs. The three interval labels below survive
        // because their rows moved to the Debug pane, not because they are user settings. The tab
        // NAME key ["performance"] is gone too — the 2026-07 restructure folded that tab into
        // "Grafik" (["cat_graphics"]), where the render trade opens the page.
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
