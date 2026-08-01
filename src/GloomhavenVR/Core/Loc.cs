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
        ["short_rest"] = Pair("Short rest", "Kurze Rast"),
        ["active"] = Pair("Active", "Aktiv"),

        // ---- event-outcome card picks ("Begegnungen" pre-scenario mali + every modal pick) ----
        // The pick banner is composed as "<Charakter>: <pick_status[_step]>[ — <pick_progress>]".
        // The verb slot ({1}/{2}) is a nominal phrase so it composes grammatically in both
        // languages ("Wähle 2 Karten zum Abwerfen" / "Choose 2 cards to discard").
        ["pick_verb_discard"] = Pair("to discard", "zum Abwerfen"),
        ["pick_verb_lose"] = Pair("to lose", "zum Verlieren"),
        ["pick_verb_recover"] = Pair("to recover", "zum Zurückholen"),
        ["pick_verb_select"] = Pair("to select", "zur Auswahl"),
        ["pick_status"] = Pair("Choose {0} card(s) {1}", "Wähle {0} Karte(n) {1}"),
        ["pick_status_step"] = Pair("Choose {0} of {1} cards {2} — step {3}/{4}",
                                    "Wähle {0} von {1} Karten {2} — Schritt {3}/{4}"),
        ["pick_progress"] = Pair("{0}/{1} placed", "{0}/{1} gewählt"),
        // Shown while the game's own confirm popup is open — the tray CONFIRM/UNDO keycaps
        // carry the popup's own option labels, the banner explains where to press.
        ["pick_confirm_hint"] = Pair("All cards placed — commit with the board button (or take a card back to swap)",
                                     "Alle Karten liegen — mit der Board-Taste abschließen (oder eine Karte zum Tauschen zurücknehmen)"),
        // The intermediate batch confirm for >2-card requirements (batches of two).
        ["pick_batch_next"] = Pair("NEXT", "WEITER"),
        // Item-surrender pick (event ConsumeSmallItem mali / refresh picks): the item-slot
        // confirm must NEVER read like the normal "USE" — the player is GIVING an item UP
        // (consumed as a malus), so the keycap says "abgeben" (surrender); the refresh
        // variant is a positive pick and says so. Banner fallbacks used only when the
        // game's own picker hint title is empty.
        ["item_surrender"] = Pair("SURRENDER ITEM", "ITEM ABGEBEN"),
        ["item_refresh_confirm"] = Pair("REFRESH ITEM", "ITEM AUFFRISCHEN"),
        ["item_surrender_demand"] = Pair("Surrender an item", "Gib einen Gegenstand ab"),
        ["item_refresh_demand"] = Pair("Choose an item to refresh", "Wähle einen Gegenstand zum Auffrischen"),

        // ---- use-slot bars dock (UseBarsSurface): pending-decision hints on the pick banner ----
        // Shown while a docked bar carries a choice the game is WAITING on — an unanswered
        // element/ability pick (the end-of-ability infusion blocks the turn outright) or a
        // pending MANDATORY active bonus (the confirm is refused until it is toggled/picked).
        ["bars_waiting_element"] = Pair("Element/ability choice pending — use the bar below the board",
                                        "Element-/Fähigkeitswahl offen — Leiste unter dem Board nutzen"),
        ["bars_waiting_bonus"] = Pair("Mandatory bonus needs a pick — use the bar below the board",
                                      "Pflicht-Bonus braucht eine Auswahl — Leiste unter dem Board nutzen"),

        // ---- SettingsPanel: sections / labels / buttons ----
        ["comfort"] = Pair("Comfort", "Komfort"),
        ["table_scale"] = Pair("Table scale", "Tischgröße"),
        ["turning"] = Pair("Turning", "Drehen"),
        ["table_height"] = Pair("Table height", "Tischhöhe"),
        ["free_movement"] = Pair("Free movement", "Freie Bewegung"),
        ["world_grab"] = Pair("World grab", "Welt greifen"),
        // Escape hatch for a control board the player cannot find any more (walked away, pinned
        // and left behind, stranded by a recentre). The per-frame watchdog recovers it on its own,
        // but the user must never be at the mercy of a timer for their primary control surface.
        ["disable_post"] = Pair("Disable post-processing*", "Post-Processing aus*"),
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
        // ---- 2026-07 menu restructure: the sidebar tabs + the section headers ---------------
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
        ["vr_var_copy"] = Pair("Take all settings from {0}", "Alle Einstellungen von {0} übernehmen"),
        ["vr_o_stickscroll"] = Pair("Scroll with the stick only", "Nur mit dem Stick scrollen"),
        ["vr_o_laserorigin"] = Pair("Laser origin", "Laser-Ursprung"),
        ["vr_o_raycone"] = Pair("Laser cone", "Laser-Kegel"),
        ["vr_o_fog"] = Pair("Volumetric fog off", "Volumennebel aus"),
        ["vr_o_forward"] = Pair("Forward rendering", "Forward-Rendering"),
        ["vr_o_menurig"] = Pair("Main menu in VR", "Hauptmenü in VR"),
        ["vr_o_circle"] = Pair("Seat in a circle", "Im Kreis sitzen"),
        ["vr_o_gfxjobs"] = Pair("Threaded submission", "Parallele Bildabgabe"),
        ["vr_o_autorestart"] = Pair("Restart automatically", "Automatisch neu starten"),
        ["vr_o_actorbars"] = Pair("Health bars", "Lebensbalken"),
        ["vr_o_buttoncluster"] = Pair("Wrist buttons", "Handgelenk-Tasten"),
        ["vr_o_dialogs"] = Pair("Dialogs in VR", "Dialoge in VR"),
        ["vr_o_decisiondock"] = Pair("Decision dock", "Entscheidungsleiste"),
        ["vr_o_enemyreveal"] = Pair("Enemy cards", "Gegnerkarten"),
        ["vr_o_trayscale"] = Pair("Board size", "Brettgröße"),
        ["vr_o_trayfollow"] = Pair("Board follows you", "Brett folgt dir"),
        // Item 12: the control-board movement scheme row + its three dropdown choices. The
        // choice labels are what the preset row shows INSTEAD of the raw enum members
        // (Free/Limited/LimitedPitch stay the config/log identity, exactly like the board
        // materials above).
        ["vr_o_boardmove"] = Pair("Board movement", "Brett-Bewegung"),
        ["boardmove_free"] = Pair("Free", "Frei"),
        ["boardmove_limited"] = Pair("Limited", "Begrenzt"),
        ["boardmove_pitch"] = Pair("Limited + tilt", "Begrenzt mit Neigung"),
        ["vr_o_inspectscale"] = Pair("Close-up size", "Nahansicht"),
        ["vr_o_revealmode"] = Pair("Fan opens by", "Fächer öffnen"),
        ["vr_o_grabbutton"] = Pair("Grab button", "Greif-Taste"),
        // ONE caption for all three per-style size rows: the pane shows only the style you are
        // wearing, and its heading already says which that is, so the row itself has no reason to
        // repeat it.
        ["vr_o_handscale"] = Pair("Hand size", "Handgröße"),
        // The per-variant families and their members. Shown in a heading over the rows that belong
        // to them ("Appearance — Hand style: Gauntlet"), which is the only place left that says
        // which board or hand the rows below are editing.
        ["vr_var_board"] = Pair("Control board", "Kontrolltafel"),
        ["vr_var_hand"] = Pair("Hand style", "Handstil"),
        ["vr_board_oak"] = Pair("Oak", "Eiche"),
        ["vr_board_steel"] = Pair("Steel", "Stahl"),
        ["vr_board_bronze"] = Pair("Bronze", "Bronze"),
        ["vr_style_glove"] = Pair("Leather glove", "Lederhandschuh"),
        ["vr_style_plate"] = Pair("Gauntlet", "Panzerhandschuh"),
        ["vr_style_arcane"] = Pair("Mage glove", "Magierhandschuh"),
        ["vr_o_netenabled"] = Pair("Multiplayer sync", "Mehrspieler-Abgleich"),
        ["vr_o_nametags"] = Pair("Name tags", "Namensschilder"),
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
        ["h_vr_o_stickscroll"] = Pair("Stops a trigger press from panning the list under it, so options are easier to hit.", "Verhindert, dass ein Trigger-Druck die Liste darunter verschiebt — Optionen lassen sich leichter treffen."),
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
        ["h_vr_o_boardmove"] = Pair("What grabbing the handle bar may do: keep the board level, let it tilt within limits, or move it freely in all axes.", "Was der Griff an der Leiste darf: Brett waagerecht halten, begrenzt neigen oder frei in alle Richtungen bewegen."),
        ["h_vr_o_inspectscale"] = Pair("How large a card gets when you hold it up to look at it.", "Wie groß eine Karte wird, wenn du sie zum Ansehen hochhältst."),
        ["h_vr_o_revealmode"] = Pair("Whether turning your wrist opens the card fan, or it is always out.", "Ob ein Drehen des Handgelenks den Fächer öffnet oder er immer offen ist."),
        ["h_vr_o_grabbutton"] = Pair("Which button picks a card up.", "Mit welcher Taste du eine Karte aufnimmst."),
        ["h_hands"] = Pair("How your hands look — gloves, bare, or a simple shape.", "Wie deine Hände aussehen — Handschuhe, bloß oder einfache Form."),
        ["h_vr_o_handscale"] = Pair("How large the hand model you are wearing is.", "Wie groß das Handmodell ist, das du trägst."),
        ["h_head_mask"] = Pair("The mask other players see on your face.", "Die Maske, die andere Spieler in deinem Gesicht sehen."),
        ["h_mask_size"] = Pair("How large your mask is.", "Wie groß deine Maske ist."),
        ["h_mirror"] = Pair("A mirror in front of you so you can see your own mask and hands.", "Ein Spiegel vor dir, damit du Maske und Hände selbst siehst."),
        ["h_vr_o_netenabled"] = Pair("Send your head and hand movement to the other players.", "Sendet deine Kopf- und Handbewegung an die anderen Spieler."),
        ["h_vr_o_keyboard"] = Pair("Appears by itself when a text field is active, so you can type without a real keyboard.", "Erscheint von selbst, wenn ein Textfeld aktiv ist — Tippen ohne echte Tastatur."),
        ["h_vr_o_keyboardcase"] = Pair("Turns MY PARTY into My Party. The game's keyboard only produces capitals.", "Macht aus MEINE GRUPPE Meine Gruppe. Die Spiel-Tastatur liefert nur Großbuchstaben."),
        ["h_remote_boards"] = Pair("Show the other players' control boards as well as your own.", "Zeigt auch die Kontrollbretter der anderen Spieler."),
        ["h_vr_o_nametags"] = Pair("Show each player's name and Steam picture above their mask.", "Zeigt Name und Steam-Bild der Mitspieler über ihrer Maske."),
        ["vr_needs_restart"] = Pair("Takes effect at the next game start.", "Wirkt erst beim nächsten Spielstart."),


        // Section headers inside the mod's tab of the game options window.
        ["vr_sec_performance"] = Pair("Performance", "Leistung"),
        ["vr_sec_panels"] = Pair("Panels & readouts", "Tafeln & Anzeigen"),
        ["vr_sec_cards"] = Pair("Cards & board", "Karten & Brett"),
        ["vr_sec_keyboard"] = Pair("Text entry", "Texteingabe"),
        ["vr_o_keyboard"] = Pair("On-screen keyboard", "Bildschirmtastatur"),
        ["vr_o_keyboardcase"] = Pair("Capitalise words", "Wörter großschreiben"),
        // Section headers WITHIN a tab — one navigation level cheaper than another tab (24 px per
        // group instead of a sidebar entry), which is why the restructure uses them for grouping
        // and keeps the tab count at five.
        ["sec_table_world"] = Pair("Table & world", "Tisch & Welt"),
        ["sec_movement"] = Pair("Movement & turning", "Bewegung & Drehen"),
        ["sec_visibility"] = Pair("Visibility", "Sichtbarkeit"),
        ["sec_hands_aim"] = Pair("Hands & aiming", "Hände & Zielen"),
        ["sec_presentation"] = Pair("Presentation", "Darstellung"),
        ["sec_appearance"] = Pair("Appearance", "Aussehen"),
        // The Multiplayer TAB and its sections. Multiplayer used to be a section inside Avatar,
        // which filed "how much of other players' boards you see" under how your own hands look.
        ["cat_multiplayer"] = Pair("Multiplayer", "Mehrspieler"),
        ["vr_sec_mp_presence"] = Pair("Playing together", "Zusammen spielen"),
        ["vr_sec_mp_avatar"] = Pair("What others see of you", "Was andere von dir sehen"),
        ["vr_sec_mirror"] = Pair("Check yourself", "Dich selbst sehen"),
        // Rows whose captions used to be German literals in SettingsPanel.3.Content.cs. They move
        // between tabs in this restructure, so they are localized on the way (the whole mod is
        // localized — a moved row must not arrive as a hardcoded string).
        ["wall_see_through"] = Pair("See-through walls", "Wände durchsichtig"),
        ["element_hints"] = Pair("Element hints", "Element-Hinweise"),

        ["show_combat_log"] = Pair("Show combat log", "Kampflog anzeigen"),
        // Size dial for the mouseover info panels ("2 Gold", "Geschlossene Tür", …) — the German
        // wording mirrors the user's own term ("Infotafeln"), the English one names them as the
        // hover info cards they are.
        ["mixed_reality"] = Pair("Mixed Reality", "Mixed Reality"),
        ["key_color"] = Pair("Key color", "Key-Farbe"),
        // The Debug pane's FIRST navigation level. It was a hardcoded German literal — the one
        // caption in the panel that an English player got in German, and the caption that has to be
        // read to discover that the sub-category list expands at all.
        ["size"] = Pair("Size", "Größe"),
        ["objectives"] = Pair("Objectives", "Aufgaben"),
        // Caption of the (idle) shared decision drawer drawn on a REMOTE player's control board —
        // the reserved strip where their take-damage / dialog prompts dock on their own client.
        ["decision_dock"] = Pair("Decisions", "Entscheidungen"),
        // The two board DASHBOARD keys. Both name BUTTONS, and the 2026-07 restructure moved them
        // out of Debug ▸ "Board & Layout" into Debug ▸ "Tasten" — the user's literal complaint was
        // that buttons were not to be found under buttons. The captions say (position) because the
        // very same two caps carry their SIZE under "Zahnrad & Fixiert (Größe)"; the pair of
        // qualifiers is what keeps the two rows from reading as duplicates of each other. German
        // "Fixiert" is the word engraved on the plate itself (Loc "pinned"), not a new term.
        ["hands"] = Pair("Hands", "Hände"),
        ["cat_buttons"] = Pair("Buttons", "Tasten"),
        ["cat_panels"] = Pair("Panels", "Tafeln"),
        // Ghost hand: the fan-carrying hand fades while the fan is open ([Hands] GhostHandOnFan).
        // Card presentation (edge-read fix): per-card toe-in toward the head + the gaze-following
        // depth-bow apex. "Zum Spieler" = how squarely each card faces you; "Blickfolge" = how far
        // the card you look at is brought out of the fan's depth recession.
        ["avatar"] = Pair("Avatar", "Avatar"),
        ["head_mask"] = Pair("Head Mask", "Kopfmaske"),
        ["mask"] = Pair("Mask", "Maske"),
        ["mask_size"] = Pair("Mask size", "Maskengröße"),
        ["mirror"] = Pair("Mirror", "Spiegel"),
        ["remote_boards"] = Pair("Player boards", "Mitspieler-Boards"),
        // Settings audit 2026-07: the remote-board mode is a purely LOCAL rendering choice that only
        // has anything to render while other players are in the session. Saying so on the panel is
        // what keeps it from reading as a dead control in single player.
        // Settings audit 2026-07: the four wall-fade fractions shape a decision the fade driver only
        // ever evaluates while [Compat] WallFade is on, so they are inert with the "Wände
        // durchsichtig" toggle off. The pane says so instead of offering four steppers that move
        // nothing.
        ["on"] = Pair("On", "An"),
        ["off"] = Pair("Off", "Aus"),
        ["spacing"] = Pair("Spacing", "Abstand"),
        ["round"] = Pair("Round", "Rund"),
        ["square"] = Pair("Square", "Eckig"),
        ["rest"] = Pair("Rest", "Rast"),
        ["generic"] = Pair("Generic", "Generisch"),
        ["items"] = Pair("Items", "Gegenstände"),

        // ---- Debug element / sub-category labels (previously hardcoded German) ----
        ["round_buttons"] = Pair("Round-phase buttons", "Rundenknöpfe"),
        ["board_dashboard"] = Pair("Gear & Pin (size)", "Zahnrad & Fixiert (Größe)"),
        ["wall_fade"] = Pair("Wall see-through", "Wandüberblendung"),
        ["figure_offsets"] = Pair("Figure offsets", "Figuren-Offsets"),
        ["button_colors"] = Pair("Button colors", "Knopf-Farben"),
        // 2026-07 restructure: the wall-fade fractions used to be a sub-category of their OWN with
        // exactly one element — a navigation level with nothing to choose. They now share this
        // sub-category with the timing/CPU levers, which is honest about what both are: dials you
        // turn to FIND a good default, not settings a player decides between. The single-element
        // name stays as the ELEMENT caption (Loc "wall_fade").

        // ---- Debug ▸ Alle Einstellungen — the generic config browser (2026-07) ----------------
        // User: "Alle config einstellungen sollen im VR Menu anpassbar sein!" — nothing may be
        // config-file-only any more. These are the browser's OWN chrome; the entry DESCRIPTIONS
        // it shows on hover are localized separately, keyed by section/key, in
        // Loc.ConfigDescriptions.cs (user 2026-07: "Mach alles auf die jeweilige lokalisierte
        // Sprache" — a tooltip must never be half English, half German).
        ["cfg_default"] = Pair("Default", "Standard"),
        ["cfg_range"] = Pair("Range", "Bereich"),
        ["cfg_empty"] = Pair("(empty)", "(leer)"),
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
        ["preset_quality"] = Pair("Quality", "Qualität"),
        ["preset_balanced"] = Pair("Balanced", "Ausgewogen"),
        ["preset_performance"] = Pair("Performance", "Leistung"),
        ["preset_minimum"] = Pair("Weak hardware", "Schwache Hardware"),
        ["preset_custom"] = Pair("Custom", "Eigene"),
        // 2026-07 (user: "Die Erklärtexte sind zu lang, sie da drin stehen zu lassen; mach ein
        // Mouseover-Hinweis oder so etwas stattdessen."): every "…_note" paragraph below is now
        // HOVER text, shown beside the row it explains and nowhere else. Two of them were not
        // attached to a control at all, so they kept a one-line pointer on the panel — the short
        // keys here — with the reasoning behind the hover. The paragraphs themselves are unchanged:
        // they were good text in the wrong place, not bad text.

        // ---- SettingsPanel: Debug — timing levers moved out of the user-facing category ----

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
        ["batch_mode_probe"] = Pair("Measure only", "Nur messen"),
        ["batch_mode_on"] = Pair("On", "An"),

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

        // ---- figure-grab debug tuning (item 2b) ----

        // ---- wrist HUD debug tuning (item 10) ----
        ["cat_wrist"] = Pair("Wrist", "Handgelenk"),

        // ---- WristHud ----
        ["no_character"] = Pair("no character", "kein Charakter"),
        ["hp"] = Pair("HP", "LP"),
        ["xp"] = Pair("XP", "EP"),
        ["gold"] = Pair("Gold", "Gold"),
        ["no_conditions"] = Pair("no conditions", "keine Zustände"),
        ["selecting_cards"] = Pair("selecting cards", "wählt Karten"),
        ["current_turn"] = Pair("current turn", "am Zug"),
        ["selected"] = Pair("selected", "ausgewählt"),

        // ---- FlatScreen desktop splash ----
        ["starting_desktop"] = Pair("starting… (intro plays on the desktop)",
                                    "startet… (Intro läuft auf dem Desktop)"),
    };
}
