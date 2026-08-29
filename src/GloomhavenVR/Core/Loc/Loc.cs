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
/// <para>WHICH LANGUAGE A *PEER'S* BOARD READS IN — A STANDING RULING, NOT A DEBT (user,
/// 2026-08-28). A mirrored control board is deliberately MIXED, and this is the decided answer
/// rather than an unfinished one. Two classes of text reach a peer's board and they resolve
/// differently by construction:</para>
///
/// <para>THE SENDER'S LANGUAGE, because the TEXT ITSELF travels. Extension records 7 (pick
/// banner), 9 (board tooltip), 12 (decision lines) and 13 (cap labels) carry capped UTF8 bytes the
/// sender already rendered — see the record catalogue in <c>Net/PresenceState.cs</c>. The receiver
/// draws those glyphs verbatim and has no key to re-localize.</para>
///
/// <para>THE VIEWER'S LANGUAGE, because only a KEY travels. <c>Net.Remote.RemoteDecisionPrompt</c>
/// localizes each synced key here, <c>Net.Remote.RemoteMapRoom</c> puts its YML id through the
/// game's own translator, and <c>Net.Remote.RemoteStatusReadouts</c> formats the round banner off
/// <c>Loc.Game</c> against <see cref="CurrentLanguage"/>. That is the mod's own "no picture
/// travels" principle applied to text: send the smallest metadata and let the receiver's game
/// produce the words.</para>
///
/// <para>THE RULING, verbatim: <i>"Das die Sprache gemixed ist finde ich OK, respektiert sogar
/// noch mehr die 1:1 Regel, daher finde ich FAS sogar gut. lass es so."</i> The reasoning is worth
/// keeping because it is the opposite of the intuition that filed this as a gap: text that
/// TRAVELS is the owner's own rendered words, so it honours the 1:1 rule MORE strictly than a
/// re-localization would — a re-translated tooltip is a different string from the one its owner is
/// looking at, however well translated. Uniformity here would have cost fidelity.</para>
///
/// <para>SO DO NOT "FIX" THIS. Neither direction is an improvement: forcing the key-borne text to
/// the sender's language would need the language on the wire and would then show a viewer words
/// they cannot read, and forcing the byte-borne text to the viewer's language is impossible
/// without sending keys the game does not expose for these strings — and would break 1:1 even if
/// it were. If a future review lists "the language on peer boards is mixed" as a finding, this
/// paragraph is the answer.</para>
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
        // ---- the controls lesson (Compat/Tutorial/Controls) ----
        // Each taught control is a {id}_t title and a {id}_b body. The wording names the KEY
        // in words as well as lighting it, because two of the shipped controller models cannot
        // light every key: the Valve Index's grip is a force sensor that moves nothing, and the
        // generic fallback device has no face buttons at all.
        ["ctl_next"] = Pair("NEXT", "WEITER"),
        ["ctl_cant"] = Pair("NOT NOW", "GEHT GERADE NICHT"),
        ["ctl_skip"] = Pair("SKIP", "ÜBERSPRINGEN"),
        ["ctl_good"] = Pair("Done.", "Erledigt."),

        // KEY NAMES, substituted into {0} of the bodies below. The _dpad variants exist for the
        // Steam Frame: its four top inputs are a D-PAD, and Valve's own Touch-compatibility
        // mapping sends A/X to the BOTTOM of it and B/Y to all three of the others
        // (partner.steamgames.com/doc/steamhardware/steamframe/controllers).
        ["ctl_key_primary"] = Pair("A (X on the left controller)",
                                   "A (auf der linken Hand X)"),
        ["ctl_key_primary_dpad"] = Pair("the BOTTOM of the D-pad",
                                        "UNTEN auf dem Steuerkreuz"),
        ["ctl_key_secondary"] = Pair("B and Y — the upper button on BOTH controllers",
                                     "B und Y — die obere Taste auf BEIDEN Controllern"),
        ["ctl_key_secondary_dpad"] = Pair(
            "the top of BOTH D-pads — left, up or right all count",
            "oben auf BEIDEN Steuerkreuzen — links, oben und rechts zählen alle"),

        ["ctl_welcome_t"] = Pair("Your controllers", "Deine Controller"),
        ["ctl_welcome_b"] = Pair(
            "Your hands have become your {0} controllers, in the place they really are. From here "
            + "on a key lights up on them — do that, and the step moves on by itself.\n\n"
            + "NEXT passes a step you cannot do right now. SKIP ends the lesson.",
            "Deine Hände sind jetzt deine {0}-Controller, genau dort, wo sie wirklich sind. Ab "
            + "hier leuchtet jeweils eine Taste darauf auf — mach das, und der Schritt geht von "
            + "selbst weiter.\n\nWEITER überspringt einen Schritt, der gerade nicht geht. "
            + "ÜBERSPRINGEN beendet die Erklärung."),

        ["ctl_laser_t"] = Pair("Point and click", "Zeigen und auswählen"),
        ["ctl_laser_b"] = Pair(
            "A laser comes out of each hand. Point it at something — a hex, a figure, a button — "
            + "and pull the TRIGGER.",
            "Aus jeder Hand kommt ein Laserstrahl. Ziel damit auf etwas — ein Feld, eine Figur, "
            + "eine Taste — und drück den ABZUG."),

        ["ctl_grab_t"] = Pair("Reach out and grab", "Zugreifen"),
        ["ctl_grab_b"] = Pair(
            "You do not have to point at everything. Move your hand to the thing itself and "
            + "squeeze the TRIGGER. Figures, chests, gold piles, traps and cards all work this way.",
            "Du musst nicht auf alles zeigen. Fahr mit der Hand direkt hin und drück den ABZUG. "
            + "Figuren, Truhen, Geldhaufen, Fallen und Karten gehen alle so."),

        ["ctl_drag_t"] = Pair("Move the table", "Den Tisch verschieben"),
        ["ctl_drag_b"] = Pair(
            "Press ONE thumbstick straight down, hold it, and move your hand: the whole table "
            + "comes with you.",
            "Drück EINEN Stick gerade nach unten, halte ihn und bewege die Hand: Der ganze Tisch "
            + "kommt mit."),

        ["ctl_zoom_t"] = Pair("Zoom in and out", "Heran- und wegzoomen"),
        ["ctl_zoom_b"] = Pair(
            "Press BOTH thumbsticks down. Pull your hands apart to zoom in, bring them together "
            + "to zoom out.",
            "Drück BEIDE Sticks nach unten. Zieh die Hände auseinander, um heranzuzoomen, und "
            + "führ sie zusammen, um wegzuzoomen."),

        ["ctl_rotate_t"] = Pair("Turn the table", "Den Tisch drehen"),
        ["ctl_rotate_b"] = Pair(
            "With both thumbsticks still held down, turn your hands around each other — the "
            + "table turns with them.",
            "Mit beiden Sticks weiterhin gedrückt: Dreh die Hände umeinander — der Tisch dreht "
            + "sich mit."),

        ["ctl_card_take_t"] = Pair("Take a card", "Eine Karte nehmen"),
        ["ctl_card_take_b"] = Pair(
            "Turn a palm up towards you and your hand of cards fans out in front of it. Take one "
            + "with the TRIGGER.",
            "Dreh eine Handfläche zu dir, dann fächert sich deine Kartenhand davor auf. Nimm eine "
            + "mit dem ABZUG."),

        ["ctl_card_hold_t"] = Pair("Hold a card properly", "Eine Karte richtig halten"),
        ["ctl_card_hold_b"] = Pair(
            "A card you take floats so that you can read it. Hold the GRIP button as well and it "
            + "sits in your hand instead, between thumb and finger — turn it round, show it to "
            + "the others. Let the grip go and it floats again.",
            "Eine genommene Karte schwebt so, dass du sie lesen kannst. Hältst du zusätzlich die "
            + "GREIFTASTE, liegt sie stattdessen wirklich in deiner Hand, zwischen Daumen und "
            + "Zeigefinger — dreh sie um, zeig sie den anderen. Lässt du die Greiftaste los, "
            + "schwebt sie wieder."),

        ["ctl_fly_t"] = Pair("Move yourself", "Dich selbst bewegen"),
        ["ctl_fly_b"] = Pair(
            "Push a thumbstick to move through the room. This moves YOU, not the table.",
            "Drück einen Stick in eine Richtung, um dich durch den Raum zu bewegen. Das bewegt "
            + "DICH, nicht den Tisch."),

        ["ctl_turn_t"] = Pair("Turn round", "Dich umdrehen"),
        ["ctl_turn_b"] = Pair(
            "Flick a thumbstick left or right to turn on the spot.",
            "Tipp einen Stick nach links oder rechts, um dich auf der Stelle zu drehen."),

        ["ctl_fingertip_t"] = Pair("Pick with a fingertip", "Mit der Fingerspitze auswählen"),
        ["ctl_fingertip_b"] = Pair(
            "Hold the GRIP button and touch the board with a fingertip. The grip is required on "
            + "purpose: a hand that merely sweeps across the board must never select anything.",
            "Halte die GREIFTASTE und tipp das Spielfeld mit einer Fingerspitze an. Die "
            + "Greiftaste ist Absicht: Eine Hand, die nur über das Feld streicht, soll nie "
            + "versehentlich etwas auswählen."),

        ["ctl_reel_t"] = Pair("Pull a window closer", "Ein Fenster heranziehen"),
        ["ctl_reel_b"] = Pair(
            "Point the laser at a window's bar and hold the trigger to carry it. While you do, "
            + "push the thumbstick forward to send it away and pull back to reel it in.",
            "Ziel mit dem Laser auf die Leiste eines Fensters und halte den Abzug, um es zu "
            + "tragen. Dabei schiebst du es mit dem Stick nach vorn weg und ziehst es nach "
            + "hinten heran."),

        ["ctl_ping_t"] = Pair("Mark a hex", "Ein Feld markieren"),
        ["ctl_ping_b"] = Pair(
            "Point at a hex and press {0} to mark it for everyone.",
            "Zeig auf ein Feld und drück {0}, um es für alle zu markieren."),

        ["ctl_recenter_t"] = Pair("Re-seat yourself", "Dich neu setzen"),
        ["ctl_recenter_b"] = Pair(
            "Hold {0} for a second and you are put back at the table.",
            "Halte {0} eine Sekunde lang, dann sitzt du wieder am Tisch."),

        ["ctl_done_t"] = Pair("That is the lot", "Das war alles"),
        ["ctl_done_b"] = Pair(
            "Everything else the game explains as you go, and every control you have just "
            + "learnt can be adjusted or switched off under VR Options.",
            "Alles Weitere erklärt dir das Spiel unterwegs. Jede Steuerung, die du gerade "
            + "gelernt hast, lässt sich in den VR-Einstellungen anpassen oder abschalten."),

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
        // The engraved caption UNDER the board's item-use recess — a ZONE NAME in the same voice as
        // the pile captions beside it ("ABGEWORFEN" / "VERBRANNT" / "GEGENSTÄNDE"), not a button.
        // It used to be pulled from the GAME key GUI_USE, which does not exist in this build: the
        // English fallback shipped, so a German player read a bare "USE" engraved on their board
        // (user report 2026-08-08). A mod string is the only honest source for a caption the game
        // itself has no word for — the game's use-item widgets carry no label at all (see
        // NetProtocol's note on UIUseSlot<T>).
        ["item_use_area"] = Pair("USE", "BENUTZEN"),
        ["item_surrender"] = Pair("SURRENDER ITEM", "ITEM ABGEBEN"),
        ["item_refresh_confirm"] = Pair("REFRESH ITEM", "ITEM AUFFRISCHEN"),
        ["item_surrender_demand"] = Pair("Surrender an item", "Gib einen Gegenstand ab"),
        ["item_refresh_demand"] = Pair("Choose an item to refresh", "Wähle einen Gegenstand zum Auffrischen"),
        // Goal-chest forfeit (flow 1, ItemRewardLosePicker): the game demands an EARNED reward
        // item back — the confirm cap says the player GIVES A REWARD UP ("abgeben", the
        // item_surrender wording family, never "USE"); the banner fallback is used only when
        // the picker's own GUI_CHOOSE_ITEM_TO_LOSE hint text is absent.
        ["item_lose_reward"] = Pair("FORFEIT REWARD", "BELOHNUNG ABGEBEN"),
        ["item_lose_reward_demand"] = Pair("Choose an earned item to forfeit",
                                           "Wähle eine verdiente Belohnung zum Abgeben"),
        // Floating-panel decisions (flows 2-4): wayfinding on the board banner while the game's
        // doom picker / distribute-points popups float pokeable in front of the HMD; the commit
        // is the board CONFIRM (the game's own ReadyButton, mirrored live on the keycap).
        ["doom_pick"] = Pair("Doom choice: pick on the floating panel, then commit with the board button",
                             "Verhängnis-Wahl: im schwebenden Fenster wählen, dann mit der Board-Taste bestätigen"),
        ["panel_float_hint"] = Pair("choose on the floating panel, then commit with the board button",
                                    "im schwebenden Fenster wählen, dann mit der Board-Taste bestätigen"),

        // ---- use-slot bars dock (UseBarsSurface): pending-decision hints on the pick banner ----
        // Shown while a docked bar carries a choice the game is WAITING on — an unanswered
        // element/ability pick (the end-of-ability infusion blocks the turn outright) or a
        // pending MANDATORY active bonus (the confirm is refused until it is toggled/picked).
        ["bars_waiting_element"] = Pair("Element/ability choice pending — use the bar below the board",
                                        "Element-/Fähigkeitswahl offen — Leiste unter dem Board nutzen"),
        ["bars_waiting_bonus"] = Pair("Mandatory bonus needs a pick — use the bar below the board",
                                      "Pflicht-Bonus braucht eine Auswahl — Leiste unter dem Board nutzen"),

        // ---- tutorial VR adaptation (Compat.TutorialVR / TutorialHintPatches) ----
        // Replaces the tutorial's flat camera-controls hint (mouse/WASD/edge scroll) at display
        // time, keyed by the hint's localization key. The body's closing line is a PROMISE the
        // camera-step bridge keeps: one deliberate drag/turn/zoom posts the game's own completion
        // event (TutorialVR.NotifyLocomotion), so "move the table once" literally advances the
        // tutorial. Bullet phrasing mirrors the settings panel's control names ("Stick-Klick",
        // snap turn) so the hint and the config UI never disagree on what a control is called.
        ["tut_vr_move_title"] = Pair("Moving in VR", "Bewegen in VR"),
        ["tut_vr_move_body"] = Pair(
            "This step normally explains the desktop camera — in VR you move the world directly:\n\n" +
            "• Hold one thumbstick CLICKED (press it in) and drag your hand — the table follows.\n" +
            "• Hold BOTH thumbstick clicks — turn your hands around each other to rotate the " +
            "table, spread or pull them together to zoom.\n" +
            "• Flick a thumbstick left/right for a snap turn.\n\n" +
            "Move the table once and the tutorial continues.",
            "Dieser Schritt erklärt normalerweise die Desktop-Kamera — in VR bewegst du die Welt direkt:\n\n" +
            "• Halte einen Daumenstick GEDRÜCKT (hineindrücken) und ziehe die Hand — der Tisch folgt.\n" +
            "• Halte BEIDE Stick-Klicks — drehe die Hände umeinander, um den Tisch zu drehen; " +
            "ziehe sie auseinander oder zusammen, um zu zoomen.\n" +
            "• Stick kurz nach links/rechts = Schnelldrehung.\n\n" +
            "Bewege den Tisch einmal, dann geht das Tutorial weiter."),

        // ---- tutorial VR step instructions (TutorialHints exact-key table) --------------------
        // One id per ACTION the tutorial demands — each pinned HelpText strip instructs, in VR
        // terms, exactly the action its dismiss trigger waits for (structural classification —
        // see the TutorialHints header; the flat wording is not readable headless). Strips are
        // ONE-LINERS: keep ≤ ~110 chars incl. the German. Controls are the mod's real bindings
        // (WorldGrab stick-click, ProximityGrabber trigger grab, board keycaps, laser/fingertip
        // picks — cross-checked against the input code).
        //
        // NAMED VARIANTS ("…_named", {0} = the card/item): every step that demands ONE SPECIFIC
        // card names it, because in VR the player opens the hand fan themselves and "the
        // requested card" would mean brute-forcing (user ruling 2026-08). {0} is filled at
        // DISPLAY time from the game's own data — the step's trigger carries the card's
        // localization TERM and Loc.Game renders exactly the title printed on the card (see
        // Compat.TutorialCardNames). The un-named ids below stay as the fallback for steps
        // whose card cannot be resolved.
        ["tut_vr_pick_card"] = Pair(
            "Palm up: fan out your cards, grab the requested card (trigger) and place it in a board card slot.",
            "Handfläche nach oben: Fächer öffnen, die verlangte Karte greifen (Trigger) und in einen Brett-Slot legen."),
        ["tut_vr_pick_card_named"] = Pair(
            "Palm up: fan out your cards, grab \"{0}\" with the trigger and place it in a board card slot.",
            "Handfläche nach oben: Fächer öffnen, \"{0}\" mit dem Trigger greifen und in einen Brett-Slot legen."),
        ["tut_vr_pick_card2"] = Pair(
            "Pick the second card the same way: grab it from the fan and drop it into the free board slot.",
            "Wähle die zweite Karte genauso: aus dem Fächer greifen und in den freien Brett-Slot legen."),
        ["tut_vr_pick_card2_named"] = Pair(
            "Second card: grab \"{0}\" from the fan and drop it into the free board slot.",
            "Zweite Karte: \"{0}\" aus dem Fächer greifen und in den freien Brett-Slot legen."),
        ["tut_vr_confirm_cards"] = Pair(
            "Lock in your cards with the board's CONFIRM keycap.",
            "Bestätige deine Kartenwahl mit der BESTÄTIGEN-Taste am Brett."),
        ["tut_vr_half_bottom"] = Pair(
            "Your played cards sit in the board slots — poke the BOTTOM half of the requested card (or laser + trigger).",
            "Deine Karten liegen in den Brett-Slots — tippe die UNTERE Hälfte der verlangten Karte an (oder Laser + Trigger)."),
        ["tut_vr_half_bottom_named"] = Pair(
            "Your played cards sit in the board slots — poke the BOTTOM half of \"{0}\" (or laser + trigger).",
            "Deine Karten liegen in den Brett-Slots — tippe die UNTERE Hälfte von \"{0}\" an (oder Laser + Trigger)."),
        ["tut_vr_half_top"] = Pair(
            "Poke the TOP half of the requested card in its board slot (or laser + trigger).",
            "Tippe die OBERE Hälfte der verlangten Karte im Brett-Slot an (oder Laser + Trigger)."),
        ["tut_vr_half_top_named"] = Pair(
            "Poke the TOP half of \"{0}\" in its board slot (or laser + trigger).",
            "Tippe die OBERE Hälfte von \"{0}\" im Brett-Slot an (oder Laser + Trigger)."),
        // FINGERTIP TILE TOUCH — the direct touch only commits WHILE THE GRIP BUTTON IS HELD
        // (fist with an extended index finger; the grip requirement is what keeps a hand that
        // merely sweeps over the board from selecting tiles). The hints below therefore never
        // say "touch it" on its own: every tile hint names BOTH commit routes and states the
        // grip condition, so the strip stays self-explanatory without a preceding box.
        ["tut_vr_hex"] = Pair(
            "Choose a highlighted hex: laser + trigger — or hold the GRIP button and touch it with a fingertip.",
            "Wähle ein markiertes Feld: Laser + Trigger — oder GRIFF-Taste halten und mit der Fingerspitze antippen."),
        ["tut_vr_hex_target"] = Pair(
            "Choose the target: laser + trigger on the marked enemy hex — or hold GRIP and touch it with a fingertip.",
            "Wähle das Ziel: Laser + Trigger auf das markierte Gegnerfeld — oder GRIFF-Taste halten und antippen."),
        ["tut_vr_execute"] = Pair(
            "Execute: pick the chosen hex again (laser + trigger, or GRIP + fingertip) — or press CONFIRM on the board.",
            "Ausführen: Feld erneut wählen (Laser + Trigger oder GRIFF-Taste + Fingerspitze) — oder BESTÄTIGEN am Brett."),
        ["tut_vr_confirm"] = Pair(
            "Continue with the board's CONFIRM keycap.",
            "Weiter mit der BESTÄTIGEN-Taste am Brett."),
        ["tut_vr_initiative"] = Pair(
            "Point the laser at the enemy's portrait on the initiative track to preview its turn.",
            "Zeige mit dem Laser auf das Gegner-Porträt in der Initiativleiste, um seinen Zug zu sehen."),
        // MOD-OWNED EXTRA STEP (Compat.TutorialGrabStep) — shown right after the portrait-hover
        // strip above, teaching the VR-native ALTERNATIVE to that hover: holding the enemy mini
        // drives the very same preview (WorldUI.FigureIntentPeek highlights the actor's
        // initiative-track avatar, the one path the game itself uses on portrait hover). The
        // strip closes the moment the player actually picks a figure up, so the text is a
        // promise the step keeps. Trigger = the figure grab button (ProximityGrabber's trigger).
        ["tut_vr_grab_intent"] = Pair(
            "Or pick the enemy figure up with your hand (trigger) — while you hold it, its turn is shown too.",
            "Oder nimm die Gegnerfigur in die Hand (Trigger) — solange du sie hältst, siehst du seinen Zug."),
        ["tut_vr_choice_panel"] = Pair(
            "Decide on the floating panel: poke a button, or point the laser and pull the trigger.",
            "Entscheide im schwebenden Fenster: Knopf antippen oder mit dem Laser zielen und den Trigger drücken."),
        ["tut_vr_short_rest"] = Pair(
            "Press the round SHORT REST keycap on the left side of the control board.",
            "Drücke die runde Taste KURZE RAST links am Kontrollbrett."),
        ["tut_vr_default_move"] = Pair(
            "Basic move: poke the small default-move button on a played card in its board slot.",
            "Standard-Bewegung: tippe den kleinen Bewegungs-Knopf auf einer gespielten Karte im Brett-Slot an."),
        ["tut_vr_skip"] = Pair(
            "Press the SKIP keycap on the board to pass this action.",
            "Drücke die ÜBERSPRINGEN-Taste am Brett, um die Aktion auszulassen."),

        // ---- tutorial VR: the BURNT card after the short rest (TutorialHints, TB_20) ---------
        // The flat tutorial explains the burn by COLOUR ("the card is dark red now") — the one
        // tell VR does not have: the mod's cards are physical, and a burnt card leaves the hand
        // for the burnt pile stack docked off the control board's right edge (PileViewer). The
        // replacement therefore states the RULE (burnt = lost for the scenario) and then sends
        // the player to the one place the card can still be looked at.
        // Composed at display time from two parts so each half can stand alone: the rule line
        // (named when the burnt card is identifiable, plain when it is not) plus the pile
        // paragraph, which is appended ONLY while the VR pile stacks actually exist
        // (TutorialHintPatches.PilesAvailable) — never teach an interaction that is not there.
        // NOT a config gate: the [Cards] PileViewer key was removed by user ruling 2026-08-11
        // (the stacks are the only way to see those piles in VR, so they are unconditional).
        // {0} = the burnt card's game-localized title; the pile paragraph's {0} = the pile's
        // OWN caption, read from the game's GUI_CARD_SECTION_BURNT section noun (PileViewer.Caption),
        // so the hint and the label physically written on the stack can never disagree.
        ["tut_vr_burnt_card"] = Pair(
            "\"{0}\" is BURNT — lost for the rest of the scenario.",
            "\"{0}\" ist VERBRANNT — für den Rest des Szenarios verloren."),
        ["tut_vr_burnt_card_plain"] = Pair(
            "The card you just lost is BURNT — gone for the rest of the scenario.",
            "Die eben verlorene Karte ist VERBRANNT — für den Rest des Szenarios verloren."),
        ["tut_vr_burnt_card_pile"] = Pair(
            "In VR a burnt card is not just tinted — it physically lies on the \"{0}\" pile, " +
            "docked to the right of the control board.\n\n" +
            "Open that pile once and look at the card: tap the stack with a fingertip, or point " +
            "the laser at it and pull the trigger. Tapping again closes it.",
            "In VR wird eine verbrannte Karte nicht nur eingefärbt — sie liegt körperlich auf dem " +
            "Stapel \"{0}\" rechts am Kontrollbrett.\n\n" +
            "Öffne den Stapel einmal und sieh dir die Karte an: Stapel mit der Fingerspitze antippen " +
            "oder mit dem Laser anzielen und den Trigger drücken. Erneutes Antippen schließt ihn wieder."),

        // ---- tutorial VR generic controls (TutorialHints tier-3 marker safety net) ------------
        // Shown when an UNPINNED hint (tutorials without a flow dump yet) resolves to text
        // containing a hardware-input token (WASD/Mausrad/right-click/…): a self-contained VR
        // controls summary that is correct no matter which flat control the original taught.
        // Body is FixedLowerRight-box sized (~existing tut_vr_move_body length); the line
        // variant fits the one-line HelpText strip.
        ["tut_vr_controls_title"] = Pair("VR controls", "VR-Steuerung"),
        ["tut_vr_controls_body"] = Pair(
            "This hint describes the desktop controls — in VR:\n\n" +
            "• Hold a thumbstick CLICKED and drag to move the table; hold BOTH stick-clicks " +
            "to rotate and zoom.\n" +
            "• Grab cards from your palm fan with the trigger and place them on the board slots.\n" +
            "• Confirm / undo / skip with the board keycaps.\n" +
            "• Point the laser at anything to inspect or select it.",
            "Dieser Hinweis beschreibt die Desktop-Steuerung — in VR:\n\n" +
            "• Daumenstick GEDRÜCKT halten und ziehen bewegt den Tisch; BEIDE Stick-Klicks: " +
            "drehen und zoomen.\n" +
            "• Karten mit dem Trigger aus dem Handfächer greifen und in die Brett-Slots legen.\n" +
            "• Bestätigen / Zurück / Überspringen über die Brett-Tasten.\n" +
            "• Mit dem Laser zeigen, um etwas anzusehen oder auszuwählen."),
        ["tut_vr_controls_line"] = Pair(
            "Desktop-control hint — in VR: stick-click + drag moves the table; laser + trigger selects; board keycaps confirm.",
            "Desktop-Hinweis — in VR: Stick-Klick + Ziehen bewegt den Tisch; Laser + Trigger wählt; Brett-Tasten bestätigen."),

        // ---- SettingsPanel: sections / labels / buttons ----
        // ["table_scale"] is GONE with its row (user ruling 2026-08: the "Tischgröße" setting was
        // removed, and the frozen [Rig] WorldScale key it wrapped went with it — see the
        // WorldScaleBase note in Rig/ComfortSettings.cs). ["table_height"] is
        // GONE the same way (user ruling 2026-08: [Comfort] TableHeightOffset removed, free
        // locomotion replaced it) — see VROptionsTab.4.Curated.cs for the whole story.
        ["comfort"] = Pair("Comfort", "Komfort"),
        ["turning"] = Pair("Turning", "Drehen"),
        ["free_movement"] = Pair("Free movement", "Freie Bewegung"),
        ["world_grab"] = Pair("World grab", "Welt greifen"),
        // Escape hatch for a control board the player cannot find any more (walked away, pinned
        // and left behind, stranded by a recentre). The per-frame watchdog recovers it on its own,
        // but the user must never be at the mercy of a timer for their primary control surface.
        // No asterisk (2026-08 naming pass, audit 05 §3): it was a footnote marker nothing
        // explained — the restart note lives in the row's hover text, in words.
        ["disable_post"] = Pair("Disable post-processing", "Post-Processing aus"),
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
        // "BILD", NOT "GRAFIK" (2026-08-22 settings audit, question (c): "Überprüfe die Kategorien
        // und ordne sie eventuell neu wenn du denkst das es intuitiver und Userfreundlicher wäre").
        // The tab used to hold the picture AND the world AND the mod's whole sound surface — twenty
        // rows of six families under one heading called "Darstellung". The world and the sound are
        // the "Umgebung & Ton" tab below; what is left really is only the picture, so the label says
        // so. The KEY stays "cat_graphics" — a text-only rename, exactly like cat_debug →
        // "Erweitert" — so nothing that references the tab has to move.
        ["cat_graphics"] = Pair("Picture", "Bild"),
        // THE NEW TAB (same audit, same question). Two lines, broken at the "&" for the same reason
        // cat_boardcards and cat_avatar_mp are: the 210 px sub-tab column fits its captions by
        // shrinking, and a long single line shrinks to the floor. "Umgebung &" / "Ton" reads as one
        // name. One-word tab names stay single-line.
        ["cat_environment"] = Pair("World &\nsound", "Umgebung &\nTon"),
        // "ERWEITERT", NOT "DEBUG" (2026-08 menu overhaul, audit 05 S1, user ruling "Setze
        // erstmal alle Vorschläge zu den Settings deinerseits so um"): the view holds everyday
        // settings' deep twins — resolution, panel switches, the 2D screen — not developer
        // switches, and the code doc had always called it the advanced view; only this label
        // disagreed. The KEY stays "cat_debug" so nothing that references the view has to move.
        ["cat_debug"] = Pair("Advanced", "Erweitert"),
        // The two tabs the overhaul created: the play surface's own tab (audit 05 S6 — card
        // and board rows do not belong under "Tafeln") and the merged social tab (audit 05 S5 —
        // two mini-tabs glued together by a duplicated mask pair).
        //
        // EXPLICIT LINE BREAKS (user report 2026-08: "die Tab Namen sind unter Umständen sehr
        // lang und der Text wird damit sehr klein"): the 210 px sub-tab column fits its captions
        // by shrinking, and these two names shrank to the floor. Broken at the "&", the fitter
        // only has to fit the longest LINE, so the caption keeps a readable size. The break is
        // authored here rather than left to auto-wrap because the wrap point matters: "Avatar &" /
        // "Mehrspieler" reads as one name; "Avatar" / "& Mehrspieler" reads as two. One-word tab
        // names stay single-line. These keys caption ONLY the sub-tab column (VROptionsTab
        // .BuildCategoryButton) — nothing else renders them, so the break leaks nowhere.
        ["cat_boardcards"] = Pair("Board &\ncards", "Brett &\nKarten"),
        ["cat_avatar_mp"] = Pair("Avatar &\nmultiplayer", "Avatar &\nMehrspieler"),
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
        // Option names stay SHORT on purpose (user ruling: a name is never ellipsised with "..." —
        // it is shortened until it fits). All four are well inside the row's caption budget.
        ["vr_o_flight"] = Pair("Stick flight", "Stick-Flug"),
        ["vr_o_flightdir"] = Pair("Flight direction", "Flugrichtung"),
        ["vr_o_flightspeed"] = Pair("Flight speed", "Fluggeschwindigkeit"),
        ["vr_o_flighthand"] = Pair("Flight hand", "Flug-Hand"),
        ["vr_o_vdrag"] = Pair("Drag vertically", "Senkrecht ziehen"),
        ["vr_o_rotate"] = Pair("Rotate the world", "Welt drehen"),
        ["vr_o_scale"] = Pair("Resize the world", "Welt skalieren"),
        // "Haltedauer", not "halten": the old caption read as a switch while the row is a
        // duration (2026-08 naming pass, audit 05 §3).
        ["vr_o_recenterhold"] = Pair("Recenter: hold time (s)", "Zentrieren: Haltedauer (s)"),
        // PARKED with the world tilt (2026-08, VRRigDriver.WorldTilt.cs): the caption and its
        // h_ hint below are kept so reviving the feature is exactly "restore the clamp + the
        // curated row" — an unused Loc key costs nothing and cannot mislabel anything.
        ["vr_o_worldtilt"] = Pair("World tilt", "Weltneigung"),
        ["vr_o_primaryhand"] = Pair("Dominant hand", "Dominante Hand"),
        ["vr_var_copy"] = Pair("Take all settings from {0}", "Alle Einstellungen von {0} übernehmen"),
        ["vr_o_stickscroll"] = Pair("Scroll with the stick only", "Nur mit dem Stick scrollen"),
        ["vr_o_laserorigin"] = Pair("Laser origin", "Laser-Ursprung"),
        ["vr_o_fingertiptouch"] = Pair("Touch hexes with fingertip", "Feld mit Finger antippen"),
        ["vr_o_fog"] = Pair("Volumetric fog off", "Volumennebel aus"),
        ["vr_o_forward"] = Pair("Forward rendering", "Forward-Rendering"),
        ["vr_o_circle"] = Pair("Free seat around the board", "Freier Platz am Brett"),
        ["vr_o_gfxjobs"] = Pair("Threaded submission", "Parallele Bildabgabe"),
        ["vr_o_autorestart"] = Pair("Restart automatically", "Automatisch neu starten"),
        // ONE family, ONE object name (2026-08 naming pass, audit 05 §3) — and since 2026-08-13 one
        // ROW: "vr_o_barsizemin"/"vr_o_barsizemax" ("Lebensbalken: Mindestgröße"/"Maximalgröße")
        // are gone with their dials, because the pair clamped a factor that is 1.0 at the shipped
        // zoom and therefore did nothing a player could see (ActorBars.ZoomFollowMin has the proof).
        ["vr_o_barsize"] = Pair("Health bars: size", "Lebensbalken: Größe"),
        ["vr_o_dialogs"] = Pair("Dialogs in VR", "Dialoge in VR"),
        ["vr_o_decisiondock"] = Pair("Decision dock", "Entscheidungsleiste"),
        // The board family on the "Objekt: Wirkung" colon pattern (2026-08 naming pass, audit
        // 05 §3): "Brettgröße" / "Brett folgt dir" / "Brett-Bewegung" mixed compound, sentence
        // and hyphen forms for one object.
        ["vr_o_trayscale"] = Pair("Board: size", "Brett: Größe"),
        ["vr_o_trayfollow"] = Pair("Board: follows you", "Brett: folgt dir"),
        // Item 12: the control-board movement scheme row + its three dropdown choices. The
        // choice labels are what the preset row shows INSTEAD of the raw enum members
        // (Free/Limited/LimitedPitch stay the config/log identity, exactly like the board
        // materials above).
        ["vr_o_boardmove"] = Pair("Board: movement", "Brett: Bewegung"),
        ["boardmove_free"] = Pair("Free", "Frei"),
        ["boardmove_limited"] = Pair("Limited", "Begrenzt"),
        ["boardmove_pitch"] = Pair("Limited + tilt", "Begrenzt mit Neigung"),
        // The per-board pitch window that "Begrenzt mit Neigung" clamps the grab to — the rows
        // sit directly under the movement-scheme dropdown (user request: they must be findable).
        ["vr_o_pitchmin"] = Pair("Tilt limit down", "Neigungslimit unten"),
        ["vr_o_pitchmax"] = Pair("Tilt limit up", "Neigungslimit oben"),
        // "Nahansicht: Größe", not bare "Nahansicht": the row is a size dial, not a switch
        // (2026-08 naming pass, audit 05 §3).
        ["vr_o_inspectscale"] = Pair("Close-up: size", "Nahansicht: Größe"),
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

        // ---- 2026-08 menu overhaul: captions + hints for the PROMOTED everyday rows ---------
        // (user ruling "Setze erstmal alle Vorschläge zu den Settings deinerseits so um" — the
        // audit's NORMAL findings joined the curated view; each row carries its own short hint,
        // same convention as every curated row above.)
        // Komfort ▸ Welt greifen
        ["vr_o_zoommin"] = Pair("Zoom-out limit", "Zoom-Untergrenze"),
        ["h_vr_o_zoommin"] = Pair("However far you zoom out, the table never gets smaller than this.", "Wie weit du auch herauszoomst — kleiner als das wird der Tisch nie."),
        ["vr_o_zoommax"] = Pair("Zoom-in limit", "Zoom-Obergrenze"),
        ["h_vr_o_zoommax"] = Pair("However far you zoom in, the table never gets larger than this.", "Wie weit du auch hineinzoomst — größer als das wird der Tisch nie."),
        ["vr_o_keepplace"] = Pair("Keep place on re-don", "Platz nach Absetzen behalten"),
        ["h_vr_o_keepplace"] = Pair("Taking the headset off and putting it back on keeps you where you were at the table.", "Headset absetzen und wieder aufsetzen lässt dich am Tisch stehen, wo du warst."),
        // Komfort ▸ Sichtbarkeit
        ["vr_o_stackedfade"] = Pair("Fade fort superstructures", "Festungs-Aufbauten ausblenden"),
        ["h_vr_o_stackedfade"] = Pair("Upper storeys and battlements fade with the walls below them.", "Obergeschosse und Zinnen verschwinden mit den Wänden darunter."),
        ["vr_o_walkin"] = Pair("All walls solid when inside", "Im Spielfeld: alle Wände massiv"),
        ["h_vr_o_walkin"] = Pair("Standing inside the play field holds every wall fully visible; nothing fades while you are in there.", "Wer im Spielfeld steht, sieht jede Wand vollständig — solange du drin bist, faded nichts."),
        // Komfort ▸ Hände & Zielen — accessibility framing per ruling 15: the row is FOR a
        // player with a weak grip, and its everyday name says so.
        ["vr_o_curlassist"] = Pair("Full-grip assist (weak grip)", "Vollgriff-Hilfe (schwacher Griff)"),
        ["h_vr_o_curlassist"] = Pair("Lower it and a partial squeeze of the grip already counts as a full fist — for hands that cannot press all the way.", "Niedriger stellen, und ein halber Druck auf die Grifftaste zählt schon als volle Faust — für Hände, die nicht ganz durchdrücken können."),
        // Grafik ▸ Darstellung
        ["vr_o_eyeres"] = Pair("Resolution per eye", "Auflösung pro Auge"),
        ["h_vr_o_eyeres"] = Pair("The main sharpness-vs-frames dial: above 1 is sharper and dearer, below 1 cheaper and softer.", "Der Haupt-Regler Schärfe gegen Bildrate: über 1 schärfer und teurer, unter 1 günstiger und weicher."),
        ["vr_o_msaa"] = Pair("MSAA level", "MSAA-Stufe"),
        ["h_vr_o_msaa"] = Pair("Smooths jagged edges. Higher looks calmer and costs GPU time.", "Glättet Treppenkanten. Höher wirkt ruhiger und kostet GPU-Zeit."),
        ["vr_o_aniso"] = Pair("Anisotropic filtering", "Anisotrope Filterung"),
        ["h_vr_o_aniso"] = Pair("Sharpens textures seen at an angle — card faces, board art. Nearly free.", "Schärft schräg gesehene Texturen — Kartenbilder, Brett-Kunst. Kostet fast nichts."),
        ["vr_o_pixellights"] = Pair("Pixel lights (max)", "Pixellichter (max)"),
        ["h_vr_o_pixellights"] = Pair("How many lights are rendered in full quality. Fewer = faster, flatter. -1 keeps the game's own setting.", "Wie viele Lichter in voller Qualität gerechnet werden. Weniger = schneller, flacher. -1 lässt die Spieleinstellung."),
        // Grafik ▸ Darstellung: the environment dial (user ruling 2026-08-12 — real 3D
        // environments replaced the panorama skyboxes; MR precedence is the user's own rule, so
        // the hint states it — the German MR sentence is a fixed formulation, keep it verbatim).
        ["vr_o_sky"] = Pair("Environment", "Umgebung"),
        ["h_vr_o_sky"] = Pair("Replaces the sky around the table with a 3D environment — a cellar or a night forest under real stars. 'Off (black)' shows no surroundings at all, just black, without switching mixed reality on. With mixed reality the sky is always off.", "Ersetzt den Himmel um den Tisch durch eine 3D-Umgebung — Keller oder Nachtwald unter echten Sternen. 'Aus (schwarz)' zeigt gar keine Umgebung, nur Schwarz, ohne Mixed Reality einzuschalten. Bei Mixed Reality ist der Himmel immer aus."),
        // The three dropdown choices, shown instead of the raw enum members (Default/Cellar/
        // SwampNight stay the config/log identity, like the board-movement labels above).
        ["sky_default"] = Pair("Default", "Standard"),
        ["sky_cellar"] = Pair("Cellar", "Keller"),
        ["sky_swamp"] = Pair("Night forest", "Nachtwald"),
        ["sky_off"] = Pair("Off (black)", "Aus (schwarz)"),
        // Grafik ▸ Darstellung: the environment's reaction to the element infusions (ELEMENT MOOD,
        // Core/ElementMood.cs). The hint says what the player will SEE, not how it is published.
        ["vr_o_elemmood"] = Pair("Elements affect surroundings", "Elemente wirken auf Umgebung"),
        ["h_vr_o_elemmood"] = Pair("The surroundings answer the elements on the board: a fresh element reads as fully charged, a waning one slowly breathes so you can see it is about to go out. Off with mixed reality, and only inside a scenario.", "Die Umgebung antwortet auf die Elemente auf dem Brett: ein frisches Element wirkt voll aufgeladen, ein schwindendes atmet langsam, sodass du siehst, dass es gleich erlischt. Bei Mixed Reality aus, und nur im Szenario."),
        ["vr_o_elemmood_amt"] = Pair("Element effect strength", "Stärke der Elementwirkung"),
        ["h_vr_o_elemmood_amt"] = Pair("How strongly the surroundings answer. 1 is as designed, lower is subtler, 0 is the same as switching it off.", "Wie stark die Umgebung antwortet. 1 ist wie vorgesehen, kleiner ist dezenter, 0 entspricht dem Ausschalten."),
        // Grafik ▸ Darstellung: the creepy easter eggs (HAUNT, Core/Haunt.cs). The hint names the
        // three things a player will want to know before switching it on — what it is, that it is
        // never in the way, and that everyone sees the same thing — and not one word about how.
        ["vr_o_haunt"] = Pair("Creepy easter eggs", "Grusel-Easter-Eggs"),
        // THE HAUNT DESCRIPTION, rewritten this round. It promised "eine bleiche Fratze hinter einem
        // Baum" (deleted with the hand-built figures in ModBuild 147) and "kein Ton" (a ruling the
        // user took back himself). What it now names are events that exist, and it states the one
        // property the user calls the most important: they are lit by the room and are meant to be
        // hard to make out.
        ["h_vr_o_haunt"] = Pair("Rare, quiet apparitions in the cellar and the night forest: a face looking in at the barred window while the moonlight dims, someone standing in the dark of the stair shaft, eyes that blink once in the undergrowth, a watcher between distant trunks. They are lit by the room and by nothing else, so in an unlit corner you will barely make one out. Never over the board, never two at once — and every player sees the same one in the same place at the same moment.", "Seltene, leise Erscheinungen im Keller und im Nachtwald: ein Gesicht, das durchs vergitterte Fenster hereinsieht, während der Mondschein schwächer wird, jemand, der im Dunkeln des Treppenschachts steht, Augen, die einmal im Unterholz blinzeln, ein Beobachter zwischen fernen Stämmen. Sie werden nur vom Raum beleuchtet und von sonst nichts — in einer unbeleuchteten Ecke erkennst du also kaum etwas. Nie über dem Brett, nie zwei gleichzeitig — und alle Spieler sehen dieselbe an derselben Stelle im selben Moment."),
        ["vr_o_haunt_freq"] = Pair("Easter egg frequency", "Häufigkeit der Easter-Eggs"),
        ["h_vr_o_haunt_freq"] = Pair("How often they happen. 0.5 is about one every three minutes, 1 shows every one there is. Lower only means you see fewer of the same events — never different ones, so it stays in step with the other players.", "Wie oft sie auftreten. 0,5 ist etwa eines alle drei Minuten, 1 zeigt alle. Kleiner heißt nur, dass du weniger derselben Ereignisse siehst — nie andere, es bleibt also mit den Mitspielern im Gleichklang."),
        // Grafik ▸ Darstellung: the environment HEARD (ENV SOUND, Core/EnvSound.cs). The hint says
        // what the player will HEAR and where from, and it names the easter eggs explicitly —
        // because this one toggle also governs their cues and there is no second switch to find.
        ["vr_o_envsound"] = Pair("Environment sounds", "Umgebungsgeräusche"),
        ["h_vr_o_envsound"] = Pair("The surroundings get sound, placed where it comes from: the drip into its puddle, the candles, the draught at the window, the rat as it crosses, the night in the swamp — and a quiet cue for the creepy easter eggs. Deliberately quiet, and it steps aside automatically whenever the game itself makes a sound. Off with mixed reality, and only inside a scenario.", "Die Umgebung bekommt Klang, und zwar genau dort, wo er herkommt: der Tropfen in seine Pfütze, die Kerzen, der Luftzug am Fenster, die Ratte, wenn sie vorbeihuscht, die Nacht im Sumpf — und ein leiser Hinweis auf die Grusel-Easter-Eggs. Bewusst leise, und tritt automatisch zurück, sobald das Spiel selbst einen Ton macht. Bei Mixed Reality aus, und nur im Szenario."),
        ["vr_o_envsound_amt"] = Pair("Environment volume", "Lautstärke der Umgebung"),
        ["h_vr_o_envsound_amt"] = Pair("How loud the surroundings are. 1 is as designed and already quiet, lower is subtler, 0 is the same as switching it off. The maximum stays well below the game's own sounds on purpose. Your master and effects volumes from the game's audio options apply on top.", "Wie laut die Umgebung ist. 1 ist wie vorgesehen und bereits leise, kleiner ist dezenter, 0 entspricht dem Ausschalten. Das Maximum bleibt bewusst deutlich unter den Tönen des Spiels. Deine Gesamt- und Effektlautstärke aus den Audio-Optionen des Spiels wirken zusätzlich."),
        // ---- Erweitert ▸ Test-Auslöser (user request, hardware ModBuild 141: "ich brauche zum
        // Testen im Erweitert Menu die möglichkeit die Elemente und Easter eggs einzeln auf
        // Knopfdruck auslösen zu können"). EVERY line here has to read as a TEST AID rather than as
        // a game action — that is a requirement of the feature, not a tone choice, because the page
        // sits in the same menu as the settings and a player who wandered in must not think they
        // have just changed the game. So: the page name says "Test", the first note says what it
        // does NOT touch, and every button says whether it is currently on.
        //
        // FOLLOW-UP REQUEST (hardware, verbatim): "In der Triggertestview möchte ich wenn ich etwas
        // triggere das es dauerhaft an ist und mit erneutem toggle wieder ausgemacht wird. So kann
        // ich die Mischungen besser testen." The rows became LATCHES, so every caption that stated a
        // duration was rewritten rather than adjusted: the two holds (8 s for an element, 14 s for an
        // apparition) do not exist any more, and a caption that still promised them would have been
        // the most convincing kind of wrong — a number the tester could read off the screen. What
        // replaces them is the on/off state on the row itself (vr_tt_on / vr_tt_off) and one note
        // that states the interaction (vr_tt_latch).
        ["vr_tt_page"] = Pair("Test triggers", "Test-Auslöser"),
        ["h_vr_tt_page"] = Pair("A test aid: switch element responses and apparitions on and off by hand instead of waiting for the game. Nothing here changes the game or your settings.", "Eine Testhilfe: Elementwirkungen und Erscheinungen von Hand an- und ausschalten, statt auf das Spiel zu warten. Nichts hier verändert das Spiel oder deine Einstellungen."),
        // WAS "Local only: nothing is sent…" until 2026-08-15, when the user ruled that a debug
        // press must be visible to everyone. The note now has to carry the two things that ARE
        // still true and are the ones a player would otherwise get wrong: no GAME state changes,
        // and every other player's own settings still decide what they see.
        ["vr_tt_local"] = Pair("Shared with the other players: they see what you switch on here, if they have the same environment and the effect switched on. No game state changes.", "Wird mit den Mitspielern geteilt: sie sehen, was du hier einschaltest — sofern sie dieselbe Umgebung und den Effekt eingeschaltet haben. Am Spielzustand ändert sich nichts."),
        // THE INTERACTION, in one line. It is the first thing a tester needs and the one thing about
        // this page that cannot be guessed from a row that says "Feuer" — and the second sentence
        // answers the user's own reason for asking ("so kann ich die Mischungen besser testen") in
        // the UI rather than only in a code comment.
        ["vr_tt_latch"] = Pair("One press switches on and leaves it on; the same row again switches it off. Several elements can stay on at once, so you can judge how they mix.", "Ein Druck schaltet an und lässt es an; dieselbe Zeile erneut schaltet wieder aus. Mehrere Elemente dürfen gleichzeitig anbleiben, so lassen sich Mischungen beurteilen."),
        ["vr_tt_scope"] = Pair("Only inside a running scenario; apparitions also only in Cellar and Night forest.", "Nur im laufenden Szenario; Erscheinungen zusätzlich nur in Keller und Nachtwald."),
        ["vr_tt_override"] = Pair("Works even when the effect is switched off — the switch is overridden for as long as the trigger is on, and never changed.", "Wirkt auch bei ausgeschaltetem Effekt — der Schalter wird übergangen, solange der Auslöser an ist, und nie verändert."),
        // The row state. The ON marker is short and upper-case, the off marker quiet, so the lit rows
        // are found by shape while scanning eighteen of them. Plain letters only: the menu uses the
        // game's font atlas, and a symbol it does not carry would render as a box on hardware.
        ["vr_tt_on"] = Pair("{0} — ON", "{0} — AN"),
        ["vr_tt_off"] = Pair("{0} — off", "{0} — aus"),
        ["vr_tt_elem_strong"] = Pair("Elements — strong", "Elemente — stark"),
        ["h_vr_tt_elem_strong"] = Pair("Pretends this element is freshly infused: full strength, rock steady. Ramps in over a second and stays on until you press the same row again.", "Tut so, als wäre dieses Element frisch infundiert: volle Stärke, völlig ruhig. Blendet in einer Sekunde ein und bleibt an, bis du dieselbe Zeile erneut drückst."),
        ["vr_tt_elem_waning"] = Pair("Elements — waning, breathing", "Elemente — schwindend, atmend"),
        ["h_vr_tt_elem_waning"] = Pair("The state that breathes: about half strength, slowly swelling and ebbing — the one that shows an element is about to go out. Stays on until you press the same row again.", "Der atmende Zustand: etwa halbe Stärke, langsam an- und abschwellend — der zeigt, dass ein Element gleich erlischt. Bleibt an, bis du dieselbe Zeile erneut drückst."),
        ["vr_tt_fire"] = Pair("Fire", "Feuer"),
        ["vr_tt_ice"] = Pair("Ice", "Eis"),
        ["vr_tt_air"] = Pair("Air", "Luft"),
        ["vr_tt_earth"] = Pair("Earth", "Erde"),
        ["vr_tt_light"] = Pair("Light", "Licht"),
        ["vr_tt_dark"] = Pair("Dark", "Dunkelheit"),
        ["vr_tt_haunts"] = Pair("Apparitions (easter eggs)", "Erscheinungen (Easter-Eggs)"),
        ["h_vr_tt_haunts"] = Pair("Plays one apparition of the environment you are in right now and holds back all the others while it is on.", "Spielt eine Erscheinung der Umgebung ab, in der du gerade bist, und hält alle anderen zurück, solange sie an ist."),
        // WHICH ROOM'S BUTTONS THESE ARE. USER REQUEST, verbatim: "Verändere die möglichen Optionen
        // die man triggern kann in dem Test menu direkt je nachdem welche Umgebung geladen ist statt
        // immer alle Knöpfe darzustellen." The page now draws only the standing room's cards, so the
        // old note ("dieselbe Nummer ist im Keller eine andere Erscheinung als im Wald", vr_tt_haunt_note)
        // described a problem that no longer exists and is deleted with the six two-room captions.
        // What replaces it says which room the rows below belong to — the tester may well be reading
        // this page in the main menu, before that room has been built.
        ["vr_tt_haunt_room_c"] = Pair("These are the CELLAR's apparitions — the environment you have selected. The night forest has different ones.", "Dies sind die Erscheinungen des KELLERS — der Umgebung, die du gewählt hast. Der Nachtwald hat andere."),
        ["vr_tt_haunt_room_f"] = Pair("These are the NIGHT FOREST's apparitions — the environment you have selected. The cellar has different ones.", "Dies sind die Erscheinungen des NACHTWALDS — der Umgebung, die du gewählt hast. Der Keller hat andere."),
        ["vr_tt_haunt_none"] = Pair("The selected environment has no apparitions, so there is nothing to trigger here. Choose Cellar or Night forest and this list fills with that room's own.", "Die gewählte Umgebung hat keine Erscheinungen, hier gibt es also nichts auszulösen. Wähle Keller oder Nachtwald, dann stehen hier die Erscheinungen dieser Umgebung."),
        // THIS HALF OF THE PAGE DOES NOT BEHAVE LIKE THE ELEMENT HALF, and a tester who found that
        // out by pressing would file it as a bug. Both differences are technical facts rather than
        // choices: only one apparition can be on because the shader's force channel carries a single
        // card id, and an apparition lasts a few authored seconds, so "on" can only honestly mean
        // "again and again". Said plainly, without either reason — the tester needs the behaviour.
        ["vr_tt_haunt_one"] = Pair("Only one apparition at a time: switching one on switches the previous one off. The one that is on repeats until you switch it off.", "Immer nur eine Erscheinung gleichzeitig: eine neue schaltet die vorherige aus. Die eingeschaltete wiederholt sich, bis du sie wieder ausschaltest."),
        ["vr_tt_haunt_n"] = Pair("Apparition {0}", "Erscheinung {0}"),
        // ONE CAPTION PER ROOM PER CARD, cellar (vr_tt_hc_*) and wood (vr_tt_hf_*). They used to be
        // six two-room captions ("Fenster / Fratze"), which stopped being expressible the moment the
        // rooms stopped having the same number of cards — the cellar has six and the wood three
        // (Haunt.CardsIn), so ids 3..5 named forest apparitions that do not exist. The authority for
        // both tables is the bundle (BuildEnvironmentRooms.cs, "HAUNT FORCE ID TABLE", one beside
        // each catalogue); if a catalogue is reordered these strings go stale and only the log's id
        // will still be right.
        //
        // ONE CELLAR ROW SAYS "no figure" ON PURPOSE — id 3 shivers the cobwebs. A tester who presses
        // it and sees no creature would otherwise report a broken button; the caption tells them what
        // to look at instead.
        //
        // TWO KEYS ARE MISSING FROM THIS TABLE AND THAT IS DELIBERATE: vr_tt_hc_2 and vr_tt_hf_0.
        // ModBuild 149 deleted both events on the user's own rulings — "Der 'Oben an eine Treppe geht
        // eine Tür auf' Effekt ist kaputt ... Lösch diesen Effekt komplett" and "Entferne den 'Augen'
        // Effekt im Wald komplett inklusive aller sounds und assets" — but their CARD INDICES had to
        // stay (the shader's group partition needs a multiple of three cards per room; see
        // Haunt.IsInert). VROptionsTab.9.TestTriggers skips those two rows, so nothing ever asks for
        // the keys; leaving a caption behind for a button that is not drawn is how a resurrected
        // string ends up describing an event that no longer happens.
        ["vr_tt_hc_0"] = Pair("Window — a face looks in through the bars", "Fenster — ein Gesicht sieht durchs Gitter herein"),
        ["vr_tt_hc_1"] = Pair("Handprints on the wet wall", "Handabdrücke an der nassen Wand"),
        ["vr_tt_hc_3"] = Pair("The cobwebs shiver (no figure)", "Die Spinnweben zittern (keine Gestalt)"),
        ["vr_tt_hc_4"] = Pair("Someone stands in the stair shaft, watching", "Jemand steht im Treppenschacht und beobachtet"),
        ["vr_tt_hc_5"] = Pair("The bookshelf tips over", "Das Bücherregal kippt um"),
        ["vr_tt_hf_1"] = Pair("A watcher at the treeline", "Ein Beobachter am Baumrand"),
        ["vr_tt_hf_2"] = Pair("Something passes between the trunks", "Etwas geht zwischen den Stämmen durch"),
        // THE ALL-OFF ROW. It was "stop now" while both overrides expired by themselves; with latches
        // it is the page's one guarantee that everything can be put back in a single press, so the
        // caption says EVERYTHING rather than naming the two channels — a tester who has latched
        // three elements and an apparition must not have to work out which rows they left lit.
        ["vr_tt_stop"] = Pair("Switch all test triggers off", "Alle Test-Auslöser ausschalten"),
        // Grafik ▸ Monitor (ruling 11 — the label is the user's own wording)
        ["vr_o_mirroreye"] = Pair("Monitor shows left eye", "Monitor zeigt linkes Auge"),
        // ---- Umgebung & Ton ▸ Kampagnenkarte -------------------------------------------------
        // "vr_o_3dmap" IS RETIRED (ModBuild 230) AND THIS IS THE WHOLE STORY, because it was two
        // faults in one key. (1) It captioned [Rig] Experimental3DMap, which no longer exists: the
        // ruling renamed and inverted it to [Rig] Vanilla2DMap, so a key nothing references would
        // have been left behind — the exact dangling string this file's other retirements name.
        // (2) It was never a caption at all. Every other curated row here is a PAIR — a short
        // label plus an "h_"-prefixed hint (vr_o_sky / h_vr_o_sky, vr_o_haunt / h_vr_o_haunt) —
        // and this one had the hint's TEXT under the label's KEY and no h_ entry whatsoever, so
        // CuratedEntry.Caption handed the row builder a five-line paragraph to print as the row's
        // name. Both halves are fixed by the pair below; nothing "moved", the old key is gone.
        //
        // THE LABEL IS WRITTEN FROM THE OFF STATE, which is what a row's label has to be when the
        // row ships off: "Originale 2D-Karte" (user's own framing: "Nenne die neue Einstellung
        // eher so etwas wie 'Vanilla 2D map'"). It says what turning it ON gets you, and the hint
        // says what leaving it OFF gets you — deliberately in that order, because the default is
        // the room and a player reading this row is a player considering leaving it.
        ["vr_o_2dmap"] = Pair("Original 2D map", "Originale 2D-Karte"),
        ["h_vr_o_2dmap"] = Pair(
            "Off — the normal setting — puts you IN the campaign map: it becomes a table-sized "
            + "parchment you walk around and lean over, with pressable locations, and in "
            + "multiplayer you see each other there. Turn this on to get the game's original flat "
            + "2D map back on the screen instead, unchanged in every detail. Your fellow players "
            + "are unaffected — everyone chooses this for themselves.",
            "Aus — die normale Einstellung — stellt dich IN die Kampagnenkarte: Sie wird zu einem "
            + "tischgroßen Pergament, um das du herumgehst und über das du dich beugst, mit "
            + "drückbaren Orten, und im Mehrspieler seht ihr euch dabei. Schalte dies ein, um "
            + "stattdessen die originale flache 2D-Karte auf der Leinwand zurückzubekommen, in "
            + "jedem Detail unverändert. Deine Mitspieler stört das nicht — das entscheidet jeder "
            + "für sich."),
        ["h_vr_o_mirroreye"] = Pair("What the desktop window mirrors while you play — for whoever is watching at the desk.", "Was das Desktop-Fenster beim Spielen zeigt — für alle, die am Monitor zuschauen."),
        // Brett & Karten
        ["vr_o_spawnleft"] = Pair("Board starts on the left", "Brett startet links"),
        ["h_vr_o_spawnleft"] = Pair("A fresh control board appears beside your head on the left instead of in front of you.", "Ein neues Kontrollbrett erscheint links neben deinem Kopf statt vor dir."),
        ["vr_o_cardwidth"] = Pair("Card width (m)", "Kartenbreite (m)"),
        ["h_vr_o_cardwidth"] = Pair("How large every card is — in the fan, in your hand, on the board.", "Wie groß jede Karte ist — im Fächer, in der Hand, auf dem Brett."),
        ["vr_o_cardsounds"] = Pair("Card sounds", "Karten-Geräusche"),
        ["h_vr_o_cardsounds"] = Pair("The mod's own card sounds: fan open and close, grab, place, take back. Off silences them all at once.", "Die Karten-Klänge des Mods: Fächer auf und zu, Greifen, Ablegen, Zurücknehmen. Aus schaltet alle auf einmal stumm."),
        ["vr_o_slothint"] = Pair("Glow on expected slot", "Erwarteter Slot leuchtet"),
        ["h_vr_o_slothint"] = Pair("The slot the game expects your card in glows softly.", "Der Slot, in den das Spiel deine Karte erwartet, leuchtet sanft."),
        ["vr_o_selready"] = Pair("Selection reminder", "Auswahl-Erinnerung"),
        ["h_vr_o_selready"] = Pair("A pulse when the board is waiting on your card pick.", "Ein Pulsieren, wenn das Brett auf deine Kartenwahl wartet."),
        ["vr_o_hoverinfo"] = Pair("Hover info size", "Info-Karten: Größe"),
        ["h_vr_o_hoverinfo"] = Pair("How large the little info panels over the play area are ('2 Gold', 'Closed door').", "Wie groß die kleinen Infotafeln über dem Spielfeld sind ('2 Gold', 'Geschlossene Tür')."),
        // Tafeln ▸ Tafeln & Anzeigen (the panel-visibility family that lived only under Debug)
        ["vr_o_wristhud"] = Pair("Wrist status display", "Handgelenk-Anzeige"),
        ["h_vr_o_wristhud"] = Pair("HP, XP and gold on your forearm.", "LP, EP und Gold auf deinem Unterarm."),
        ["vr_o_loading"] = Pair("Loading indicator", "Ladeanzeige"),
        ["h_vr_o_loading"] = Pair("A spinner in the headset while the game loads.", "Eine Ladeanzeige im Headset, während das Spiel lädt."),
        // Tafeln ▸ Lebensbalken
        ["vr_o_barsoccluded"] = Pair("Health bars behind walls", "Lebensbalken hinter Wänden"),
        ["h_vr_o_barsoccluded"] = Pair("Bars stay visible even when a wall stands between you and the figure.", "Balken bleiben sichtbar, auch wenn eine Wand zwischen dir und der Figur steht."),
        // Tafeln ▸ 2D-Schirm
        ["vr_o_showintro"] = Pair("Show intro in VR", "Intro in VR zeigen"),
        ["h_vr_o_showintro"] = Pair("Play the game's intro on the floating screen instead of skipping it.", "Das Intro des Spiels auf dem schwebenden Schirm zeigen statt es zu überspringen."),
        ["vr_o_screenwidth"] = Pair("2D screen: width (m)", "2D-Schirm: Breite (m)"),
        ["h_vr_o_screenwidth"] = Pair("How wide the floating 2D screen is.", "Wie breit der schwebende 2D-Schirm ist."),
        ["vr_o_screendist"] = Pair("2D screen: distance (m)", "2D-Schirm: Abstand (m)"),
        ["h_vr_o_screendist"] = Pair("How far away the floating 2D screen hangs.", "Wie weit weg der schwebende 2D-Schirm hängt."),
        // Tafeln ▸ Klick & Zeigen
        ["vr_o_pokeclick"] = Pair("Poke to click", "Antippen klickt"),
        ["h_vr_o_pokeclick"] = Pair("Touching the 2D screen with a fingertip clicks it.", "Den 2D-Schirm mit der Fingerspitze berühren klickt."),
        ["vr_o_pokefirm"] = Pair("Decisions: firm press", "Entscheidung: fester Druck"),
        ["h_vr_o_pokefirm"] = Pair("Decision buttons want a deliberate press, so a stray touch cannot answer for you.", "Entscheidungs-Tasten wollen einen bewussten Druck — eine Streifberührung antwortet nicht für dich."),
        ["vr_o_hexhintfollow"] = Pair("Hex hint follows view", "Feld-Hinweis folgt Blick"),
        ["h_vr_o_hexhintfollow"] = Pair("The hex info panel turns to face wherever you look.", "Die Feld-Infotafel dreht sich dorthin, wo du hinschaust."),
        ["vr_o_buttonanim"] = Pair("Key animation", "Tasten-Animation"),
        ["h_vr_o_buttonanim"] = Pair("Board keycaps crumble away and reassemble instead of popping.", "Brett-Tasten zerfallen und setzen sich wieder zusammen, statt zu ploppen."),
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
        ["h_vr_o_flight"] = Pair("Stick forward flies, sideways strafes.", "Stick nach vorne fliegt, seitwaerts fliegt seitwaerts."),
        ["h_vr_o_flightdir"] = Pair("Fly where you look, or where the laser points.", "Fliegt dorthin, wohin du schaust oder wohin der Laser zeigt."),
        ["h_vr_o_flightspeed"] = Pair("Speed with the stick pushed all the way.", "Geschwindigkeit bei voll durchgedruecktem Stick."),
        ["h_vr_o_flighthand"] = Pair("Which controller's stick flies.", "Mit welchem Controller-Stick du fliegst."),
        ["h_world_grab"] = Pair("Grip empty air to pull the whole table towards you.", "Ins Leere greifen, um den Tisch zu dir zu ziehen."),
        ["h_vr_o_vdrag"] = Pair("Let a world grab also move the table up and down.", "Erlaubt, den Tisch beim Greifen auch zu heben und zu senken."),
        ["h_vr_o_rotate"] = Pair("Let a two-handed grab turn the table.", "Erlaubt, den Tisch mit beiden Händen zu drehen."),
        ["h_vr_o_scale"] = Pair("Let a two-handed grab resize the table.", "Erlaubt, den Tisch mit beiden Händen zu skalieren."),
        ["h_vr_o_recenterhold"] = Pair("How long to hold B+Y before the view recentres.", "Wie lange B+Y gehalten wird, bis die Ansicht neu zentriert."),
        // h_vr_o_worldtilt is PARKED with the world tilt row (see vr_o_worldtilt above);
        // h_table_scale is gone with the removed "Tischgröße" row, and h_table_height with the
        // removed "Tischhöhe" row ([Comfort] TableHeightOffset, user ruling 2026-08).
        ["h_vr_o_worldtilt"] = Pair("Tips the table towards you so far edges are easier to see.", "Neigt den Tisch zu dir, damit ferne Ränder besser zu sehen sind."),
        ["h_vr_o_primaryhand"] = Pair("Which hand holds the laser and plays cards.", "Welche Hand den Laser führt und Karten spielt."),
        ["h_vr_o_stickscroll"] = Pair("Stops a trigger press from panning the list under it, so options are easier to hit.", "Verhindert, dass ein Trigger-Druck die Liste darunter verschiebt — Optionen lassen sich leichter treffen."),
        ["h_vr_o_laserorigin"] = Pair("Whether the laser leaves from the fingertip or the controller.", "Ob der Laser an der Fingerspitze oder am Controller ansetzt."),
        ["h_vr_o_fingertiptouch"] = Pair("Hold the grip and touch a hex to select it — like the laser click.", "Griff halten und ein Feld antippen — wie ein Klick mit dem Laser."),
        ["h_disable_post"] = Pair("Turns off the game's screen effects. Sharper, and cheaper.", "Schaltet die Bildschirmeffekte des Spiels ab. Schärfer und günstiger."),
        ["h_vr_o_fog"] = Pair("Removes the haze in rooms. Clearer view, less atmosphere.", "Entfernt den Dunst in Räumen. Klarere Sicht, weniger Stimmung."),
        ["h_wall_see_through"] = Pair("Fades walls that stand between you and the board.", "Blendet Wände aus, die zwischen dir und dem Brett stehen."),
        ["h_vr_o_forward"] = Pair("A cheaper render path. Helps weak hardware, changes lighting slightly.", "Günstigerer Renderpfad. Hilft schwacher Hardware, ändert die Beleuchtung leicht."),
        ["h_vr_o_circle"] = Pair("On joining, seats you at the spot furthest from every player already at the table, facing the board.", "Setzt dich beim Beitreten an die Stelle mit dem größten Abstand zu allen schon anwesenden Spielern, mit Blick zum Brett."),
        ["h_vr_o_gfxjobs"] = Pair("Spreads drawing over several CPU threads. The single biggest performance gain.", "Verteilt das Zeichnen auf mehrere CPU-Threads. Der größte Leistungsgewinn."),
        ["h_vr_o_autorestart"] = Pair("Restarts the game by itself the one time the setting above needs it.", "Startet das Spiel einmal selbst neu, wenn die Einstellung darüber es braucht."),
        ["h_mixed_reality"] = Pair("Clears the sky to one colour so your room can show through it.", "Färbt den Himmel einfarbig, damit dein Zimmer durchscheinen kann."),
        ["h_key_color"] = Pair("The colour your headset replaces with the room. Black keeps it simply dark.", "Die Farbe, die dein Headset durch das Zimmer ersetzt. Schwarz lässt es einfach dunkel."),
        ["h_show_combat_log"] = Pair("A floating panel listing what just happened.", "Eine schwebende Tafel mit dem, was gerade passiert ist."),
        // The hint now carries what the two removed bound-dials used to claim to configure: the
        // bars follow the table zoom only inside a fixed band, so this one number is the whole size.
        ["h_vr_o_barsize"] = Pair("How large the bars are in front of your eyes. 1.0 is the size they have always had, and it holds however far you zoom the table.", "Wie groß die Balken vor deinen Augen sind. 1.0 ist die Größe, die sie immer hatten — und sie bleibt es, wie weit du den Tisch auch zoomst."),
        ["h_vr_o_dialogs"] = Pair("Show the game's dialogs as panels in front of you.", "Zeigt die Dialoge des Spiels als Tafeln vor dir."),
        ["h_vr_o_decisiondock"] = Pair("A bar within reach for choices you have to make.", "Eine Leiste in Reichweite für Entscheidungen."),
        ["h_control_board"] = Pair("Which control board model sits in front of you.", "Welches Kontrollbrett-Modell vor dir steht."),
        ["h_vr_o_trayscale"] = Pair("How large the control board is.", "Wie groß das Kontrollbrett ist."),
        ["h_vr_o_trayfollow"] = Pair("The board stays in front of you instead of fixed in the room.", "Das Brett bleibt vor dir statt fest im Raum."),
        ["h_vr_o_boardmove"] = Pair("What grabbing the handle bar may do: keep the board level, let it tilt within limits, or move it freely in all axes.", "Was der Griff an der Leiste darf: Brett waagerecht halten, begrenzt neigen oder frei in alle Richtungen bewegen."),
        ["h_vr_o_pitchmin"] = Pair("'Limited + tilt' only: how far the grab may tilt this board DOWN, away from you (degrees).", "Nur bei 'Begrenzt mit Neigung': wie weit der Griff dieses Brett nach UNTEN, von dir weg neigen darf (Grad)."),
        ["h_vr_o_pitchmax"] = Pair("'Limited + tilt' only: how far the grab may tilt this board UP, towards you (degrees).", "Nur bei 'Begrenzt mit Neigung': wie weit der Griff dieses Brett nach OBEN, zu dir hin neigen darf (Grad)."),
        ["h_vr_o_inspectscale"] = Pair("How large a card gets when you hold it up to look at it.", "Wie groß eine Karte wird, wenn du sie zum Ansehen hochhältst."),
        ["h_vr_o_revealmode"] = Pair("Whether turning your wrist opens the card fan, or it is always out.", "Ob ein Drehen des Handgelenks den Fächer öffnet oder er immer offen ist."),
        ["h_vr_o_grabbutton"] = Pair("Which button picks a card up.", "Mit welcher Taste du eine Karte aufnimmst."),
        ["h_hands"] = Pair("How your hands look — gloves, bare, or a simple shape.", "Wie deine Hände aussehen — Handschuhe, bloß oder einfache Form."),
        ["h_vr_o_handscale"] = Pair("How large the hand model you are wearing is.", "Wie groß das Handmodell ist, das du trägst."),
        ["h_head_mask"] = Pair("The mask other players see on your face.", "Die Maske, die andere Spieler in deinem Gesicht sehen."),
        ["h_mask_size"] = Pair("How large your mask is.", "Wie groß deine Maske ist."),
        ["h_mirror"] = Pair("A mirror in front of you so you can see your own mask and hands.", "Ein Spiegel vor dir, damit du Maske und Hände selbst siehst."),
        ["h_vr_o_netenabled"] = Pair("Send your head and hand movement to the other players.", "Sendet deine Kopf- und Handbewegung an die anderen Spieler."),
        ["h_vr_o_keyboardcase"] = Pair("Turns MY PARTY into My Party. The game's keyboard only produces capitals.", "Macht aus MEINE GRUPPE Meine Gruppe. Die Spiel-Tastatur liefert nur Großbuchstaben."),
        ["h_remote_boards"] = Pair("Show the other players' control boards as well as your own.", "Zeigt auch die Kontrollbretter der anderen Spieler."),
        ["h_vr_o_nametags"] = Pair("Show each player's name and Steam picture above their mask.", "Zeigt Name und Steam-Bild der Mitspieler über ihrer Maske."),
        ["vr_needs_restart"] = Pair("Takes effect at the next game start.", "Wirkt erst beim nächsten Spielstart."),


        // Section headers inside the mod's tab of the game options window.
        // UNREFERENCED since the 2026-08-22 settings audit emptied the section (its two rows,
        // [Core] EnableGraphicsJobs and AutoRestartForGraphicsJobs, are on Erweitert ▸ System &
        // Start). Kept, not deleted: it is one line, it costs nothing, and the heading comes back
        // the moment a genuine everyday performance row exists — which is more likely here than
        // for any other retired section key in this file.
        ["vr_sec_performance"] = Pair("Performance", "Leistung"),
        ["vr_sec_panels"] = Pair("Panels & readouts", "Tafeln & Anzeigen"),
        // ["vr_sec_cards"] ("Karten & Brett") is GONE (2026-08 overhaul, audit 05 S6): the
        // section grew into the "Brett & Karten" TAB — see cat_boardcards and its sections.
        ["vr_sec_keyboard"] = Pair("Text entry", "Texteingabe"),
        ["vr_o_keyboardcase"] = Pair("Capitalise words", "Wörter großschreiben"),
        // Section headers WITHIN a tab — one navigation level cheaper than another tab (24 px per
        // group instead of a sidebar entry), which is why the restructure uses them for grouping.
        // ["sec_table_world"] ("Tisch & Welt") is GONE with the last of its three rows — see
        // VROptionsTab.4.Curated.cs, where the section used to be declared, for why each went.
        // ["sec_movement"] ("Bewegung & Drehen") is GONE too (2026-08 overhaul, audit 05 S7):
        // fourteen rows from three sense families under one heading — split into the three below.
        ["sec_turning"] = Pair("Turning", "Drehen"),
        ["sec_locomotion"] = Pair("Locomotion", "Fortbewegung"),
        ["sec_worldgrab"] = Pair("Grabbing the world", "Welt greifen"),
        ["sec_visibility"] = Pair("Visibility", "Sichtbarkeit"),
        ["sec_hands_aim"] = Pair("Hands & aiming", "Hände & Zielen"),
        ["sec_presentation"] = Pair("Presentation", "Darstellung"),
        // Bild's second heading (2026-08-22 audit): the two everyday dials that decide how many
        // headset pixels a floated game window gets — the sharp-render switch and the size. Their
        // two calibration dials are on Erweitert ▸ Menüs & Tafeln beside them.
        ["sec_windows_panels"] = Pair("Windows & panels", "Fenster & Tafeln"),
        // Grafik's one-row monitor section (ruling 11: what the desktop mirror shows).
        ["vr_sec_monitor"] = Pair("Desktop monitor", "Monitor"),
        // ---- "Umgebung & Ton", the tab the 2026-08-22 settings audit created --------------------
        // Five headings that all answer "which world am I in", where the old Grafik page answered
        // "how is it drawn". "Ton" is the one that had no home at all before: the mod's five sound
        // settings were split between a Graphics page and a Cards page.
        ["sec_environment"] = Pair("Environment", "Umgebung"),
        ["sec_haunt"] = Pair("Creepy", "Grusel"),
        ["sec_sound"] = Pair("Sound", "Ton"),
        // TEXT-ONLY RENAME AT ModBuild 230 (the LocKey stays "sec_map3d", the same way cat_debug
        // kept its key when it became "Erweitert"): the heading used to name the feature the one
        // row under it switched ON, and that row now names the OPT-OUT. "Karte 3D" over a row
        // called "Originale 2D-Karte" reads as a contradiction; "Kampagnenkarte" names the subject
        // both rows are about — which map you get, and your card hand while you are in it.
        ["sec_map3d"] = Pair("Campaign map", "Kampagnenkarte"),
        // Brett & Karten — the play surface's own tab (2026-08 overhaul, audit 05 S6).
        ["vr_sec_controlboard"] = Pair("Control board", "Kontrollbrett"),
        // One row ([FigureGrab] GrabFigures), and it earns a heading because it is the master of a
        // whole headline feature — picking the miniatures up — rather than another board dial
        // (2026-08-22 settings audit, question (b)).
        ["vr_sec_figures"] = Pair("Figures", "Figuren"),
        ["vr_sec_cardhand"] = Pair("Cards", "Karten"),
        ["vr_sec_piles_hints"] = Pair("Piles & hints", "Stapel & Hinweise"),
        // Tafeln's new sections: the unified bar family, the 2D screen's everyday face, and
        // how the panels are operated.
        ["vr_sec_bars"] = Pair("Health bars", "Lebensbalken"),
        ["vr_sec_screen2d"] = Pair("2D screen", "2D-Schirm"),
        ["vr_sec_interaction"] = Pair("Pointing & clicking", "Klick & Zeigen"),
        // Avatar & Mehrspieler (2026-08 overhaul, audit 05 S5: ONE tab where two mini-tabs
        // stood; the duplicated mask rows died with the seam). ["sec_appearance"],
        // ["cat_multiplayer"], ["vr_sec_mp_avatar"], ["vr_sec_mirror"] and the old tab name
        // ["avatar"] are gone with the merge — a dead key cannot suggest a tab that no
        // longer exists.
        ["vr_sec_your_look"] = Pair("Your appearance", "Dein Auftritt"),
        ["vr_sec_mp_presence"] = Pair("Playing together", "Zusammen spielen"),
        // Rows whose captions used to be German literals in SettingsPanel.3.Content.cs. They move
        // between tabs in this restructure, so they are localized on the way (the whole mod is
        // localized — a moved row must not arrive as a hardcoded string).
        ["wall_see_through"] = Pair("See-through walls", "Wände durchsichtig"),
        // "Wände: mit Mitspielern synchron", not "Wand-Fades der Mitspieler": "Fades" is
        // jargon and the old name did not say what the toggle DOES (2026-08 naming pass).
        ["wallfade_sync"] = Pair("Walls: sync with teammates", "Wände: mit Mitspielern synchron"),
        ["h_wallfade_sync"] = Pair(
            "Walls that fade for a teammate also fade for you — same animation as your own.",
            "Wände, die bei einem Mitspieler ausgeblendet sind, verschwinden auch bei dir — mit derselben Animation wie deine eigenen."),

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
        // Captions of the four mirrored USE-BAR rows drawn on a REMOTE player's control board (wire
        // record 25 — the second drawer, below their decision row). The wire carries the BAR BIT,
        // never a word: the game's use-slot widgets have no label at all, only card art, so the
        // caption is composed HERE in the VIEWER's language and the slots themselves stay anonymous
        // state-painted tiles.
        ["use_bar_bonuses"] = Pair("Active bonuses", "Aktive Boni"),
        ["use_bar_abilities"] = Pair("Abilities", "Fähigkeiten"),
        ["use_bar_augments"] = Pair("Augments", "Verstärkungen"),
        ["use_bar_items"] = Pair("Items", "Gegenstände"),
        // The MP ready keycap's CONFIRMED state. Vanilla's own GUI_READY ("Mach dich bereit!")
        // was showing here, which names the OPPOSITE action — pressing it revokes ready. The
        // cap's other state reads "Auswahl beenden", so this wording keeps the pair one toggle
        // on the same noun and names the effect (user report 2026-08-08).
        ["confirm_unready"] = Pair("Change selection", "Auswahl ändern"),
        // Cap label while a placed item still owes its element choice (the picker is docked in
        // the decision area under the board); pressing USE afterwards confirms it.
        ["item_choose_element"] = Pair("CHOOSE ELEMENT", "ELEMENT WÄHLEN"),
        // FREE CHARACTER FOCUS (user feature 2026-08-08): the player may look at any character
        // in the action phase; a character they do not control is strictly view-only.
        ["VR_FOCUS_VIEWING"] = Pair("Viewing: {0}", "Ansicht: {0}"),
        ["VR_FOCUS_READONLY"] = Pair("(view only)", "(nur Ansicht)"),
        ["VR_FOCUS_AT_TURN"] = Pair("At turn: {0}", "Am Zug: {0}"),
        ["VR_FOCUS_WRONG_CHARACTER"] = Pair("{0} is at turn", "{0} ist am Zug"),
        // Banner hint shown when a flow WANTS an item but the fan is closed. The fan never opens
        // itself any more (user ruling 2026-08-07: "Weder soll es sich automatisch öffnen, noch
        // soll es jemals die Situation geben, dass man es nicht schließen kann"), so the prompt
        // has to say how to open it.
        ["item_fan_open_hint"] = Pair("tap the items pile to open the fan",
                                      "tippe den Gegenstände-Stapel an, um den Fächer zu öffnen"),
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
        // ["avatar"] is GONE with the merged tab (see the vr_sec_your_look block above).
        // "Head mask", not "Head Mask": the one EN caption that carried title case in a table
        // of sentence-case names (2026-08 naming pass, audit 05 §3).
        ["head_mask"] = Pair("Head mask", "Kopfmaske"),
        ["mask"] = Pair("Mask", "Maske"),
        // MASK NAMES, ONE PER SHIPPED ID.
        //
        // The key is "mask_name_" + the [Net] MaskId value, so HeadMaskLibrary.MaskNames can build
        // the whole [Net] MaskId dropdown from MaskCount without a hand-typed list going stale. A
        // mask with NO entry here degrades to its own number — Loc.Mod("mask") + " " + (i+1), i.e.
        // "Maske 1" / "Mask 1" (HeadMaskLibrary.MaskNames) — instead of vanishing from the list or
        // showing the raw key. That fallback is the design, not a hole in it: an id the player can
        // still select beats a tidy list that silently drops a shipped asset.
        //
        // NAME THE SILHOUETTE, NOT THE GLOW. ModBuild 100 (2026-08-09, the build that turned the
        // mask picker from a slider into a dropdown) named the three after the colour glowing
        // through their seams ("Bernstein"/"Violett"/"Türkis"). The user rejected that set,
        // 2026-08-22, verbatim: "Die neuen Kopfmasken-Namen im Optionsmenü sind verwirrend, ändere
        // die wieder." ModBuild 226 removed the entries and let the numbering fallback take over,
        // and on 2026-08-23 he asked for real names again: "Gib den Masken einen coolen, passenden
        // Namen zu der Maske statt nur 'Maske i'." So the fault was never the IDEA of naming them —
        // it was the feature the words named. A seam colour is a handful of glowing pixels at avatar
        // distance, and the 226 note went on to justify itself with a claim nobody had checked
        // against a picture: that all three "read as the same object". THEY DO NOT. Rendered from
        // the shipped Mask_<n>.fbx + Mask_<n>_albedo.png (the preview path in
        // unity/mask-prep/prepare_masks.py), the three are three unmistakable silhouettes:
        //   0 — a riveted steel great helm: closed face, brow-and-nasal cross bar, breath slits,
        //       nothing of the wearer visible but a lit band where the eyes are.
        //   1 — a smooth porcelain visage: calm, half-lidded eyes, a thin filigree circlet, lit
        //       rune lines and hairline cracks running down the cheeks.
        //   2 — a carved wooden war mask: two heavy horns lashed on with cord, warpaint in bone and
        //       rust across the grain.
        // Each name below names THAT shape — helm / veil / horn — so the word and the thing agree
        // across the table, and a player can say the entry out loud to a peer who is looking at it.
        // Register is the game's own compound-noun class naming (Nightshroud, Doomstalker,
        // Sunkeeper), not a colour swatch and not a joke.
        //
        // LENGTH IS FINE. These sit in the curated tab's dropdown (BuildPresetRow, harvested from
        // the game's own dropdown control). The longest here is "Runenschleier" at 13 characters —
        // shorter than "Schwache Hardware" (17), which the SAME row kit already ships in the
        // graphics-preset dropdown, and than "Aus (schwarz)" (13) in the sky dropdown.
        //
        // ADDING A FOURTH MASK NEEDS ONE ENTRY HERE AND NOTHING ELSE — no code, no list, no row.
        // Do add it: the numbering fallback still works, but a list that mixes names and numbers
        // is worse than either, so name all of them or none.
        ["mask_name_0"] = Pair("Ironwatch", "Eisenwacht"),
        ["mask_name_1"] = Pair("Runeveil", "Runenschleier"),
        ["mask_name_2"] = Pair("Grimhorn", "Grimmhorn"),
        ["mask_size"] = Pair("Mask size", "Maskengröße"),
        ["mirror"] = Pair("Mirror", "Spiegel"),
        // "Bretter", not "Boards" (2026-08 naming pass, audit 05 §3): everywhere else in the
        // German menu the object is a Brett; the one Denglisch holdout is gone.
        ["remote_boards"] = Pair("Player boards", "Mitspieler-Bretter"),
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
        // THE TWO TOPICS THE 2026-08-22 SETTINGS AUDIT ADDED, and the heading above is the reason
        // both were needed: [EnvSound] rides rig.cfg, so every volume dial the mod has was filed
        // under a topic that says PICTURE, and [Sky]/[Elements]/[Haunt]/[Rig] Vanilla2DMap
        // each fell under MinClusterSize and were swept into that topic's "Allgemein" collector.
        // Erweitert is the catalog's own index, so a topic each fixes the misfiling mechanically:
        // a setting added to one of those sections tomorrow lands correctly with no edit at all.
        ["cfg_topic_environment"] = Pair("Environment & world", "Umgebung & Welt"),
        ["cfg_topic_sound"] = Pair("Sound", "Ton"),
        ["cfg_topic_movement"] = Pair("Movement & world", "Bewegung & Welt"),
        ["cfg_topic_hands"] = Pair("Hands & figures", "Hände & Figuren"),
        ["cfg_topic_cards"] = Pair("Cards & fan", "Karten & Fächer"),
        ["cfg_topic_panels"] = Pair("Menus & panels", "Menüs & Tafeln"),
        ["cfg_topic_board"] = Pair("Board & targeting", "Brett & Zielen"),
        // "Steuerbrett", not "Brett-Geometrie": the topic holds every dial of the CONTROL BOARD —
        // buttons, piles, readouts — and "geometry" described only a third of that. The user's own
        // name for the page is "das Debug-Menu 'pro Board'" (report 2026-08).
        ["cfg_topic_board_geometry"] = Pair("Control board (per board)", "Steuerbrett (pro Brett)"),

        // ---- Debug ▸ Steuerbrett: the hand-arranged heading tree (user report 2026-08:
        // "Ordne sie so an, dass man schneller findet wonach man sucht … Geb auch den
        // Überschriften Tooltipps"). Labels are the headings; each "h_"-prefixed sibling is that
        // heading's hover text, and the pairing is BY NAME so the two can never drift apart
        // (same convention as CuratedEntry.HintKey). The tree itself is
        // VROptionsTab.6.BoardTopic.cs.
        ["vr_bg_buttons"] = Pair("Buttons", "Tasten"),
        ["h_vr_bg_buttons"] = Pair(
            "The pressable keycaps on the control board: the three stacked keys (Confirm, Undo and "
            + "SKIP), the rest keys and the FOLLOW/PINNED key — position, size, gap, shape and "
            + "press travel.",
            "Die drückbaren Tasten auf dem Steuerbrett: die drei übereinander sitzenden Tasten "
            + "(Bestätigen, Zurück und ÜBERSPRINGEN), die Rast-Tasten und die FOLGEN/FIXIERT-Taste "
            + "— Lage, Größe, Abstand, Form und Hub."),
        ["vr_bg_cu"] = Pair("Board keys", "Brett-Tasten"),
        ["h_vr_bg_cu"] = Pair(
            "The three keycaps stacked in the board's button recesses — Confirm, Undo and SKIP, "
            + "which sits in the third recess since it stopped being a group of its own: where the "
            + "column sits, how far apart the keys are, their shape and their "
            + "width/height/depth/travel (the width doubles as the diameter when the shape is "
            + "round).",
            "Die drei Tasten in den Tastenmulden des Bretts — Bestätigen, Zurück und "
            + "ÜBERSPRINGEN, das seit dem Ende seiner eigenen Gruppe in der dritten Mulde sitzt: "
            + "wo die Spalte sitzt, wie weit die Tasten auseinander stehen, ihre Form und "
            + "Breite/Höhe/Tiefe/Hub (bei runder Form ist die Breite zugleich der Durchmesser)."),
        ["vr_bg_rest"] = Pair("Rest keys", "Rast-Tasten"),
        ["h_vr_bg_rest"] = Pair(
            "The short/long rest keys: seat, size, gap and shape, plus the keycap geometry.",
            "Die Tasten für kurze/lange Rast: Sitz, Größe, Abstand und Form, dazu die "
            + "Kappen-Geometrie."),
        ["vr_bg_cluster"] = Pair("Pin key", "Fixier-Taste"),
        ["h_vr_bg_cluster"] = Pair(
            "The FOLLOW/PINNED key: where it sits on the board. The SKIP key used to share this "
            + "heading because it was a group of its own; it is one of the board keys now.",
            "Die FOLGEN/FIXIERT-Taste: wo sie am Brett sitzt. Die ÜBERSPRINGEN-Taste stand hier, "
            + "solange sie eine eigene Gruppe war — sie ist jetzt eine der Brett-Tasten."),
        ["vr_bg_board"] = Pair("Board & alignment", "Brett & Ausrichtung"),
        ["h_vr_bg_board"] = Pair(
            "The control board itself: how it hangs in front of you, and the decorative board "
            + "mesh's own trim underneath the functional layout.",
            "Das Steuerbrett selbst: wie es vor dir hängt, und die Feinjustage des dekorativen "
            + "Brett-Meshes unter dem funktionalen Layout."),
        ["vr_bg_pose"] = Pair("Pose & size", "Haltung & Größe"),
        ["h_vr_bg_pose"] = Pair(
            "The whole board's pose: base tilt, the allowed tilt window, extra yaw, overall size "
            + "and position offset.",
            "Die Haltung des ganzen Bretts: Grundneigung, erlaubtes Neigungsfenster, "
            + "Zusatzdrehung, Gesamtgröße und Positionsversatz."),
        ["vr_bg_mesh"] = Pair("Board mesh", "Brett-Mesh"),
        ["h_vr_bg_mesh"] = Pair(
            "Only the visible board model: nudge and rotate the mesh without moving any of the "
            + "elements seated on it.",
            "Nur das sichtbare Brett-Modell: verschiebt und dreht das Mesh, ohne die darauf "
            + "sitzenden Elemente zu bewegen."),
        ["vr_bg_cards"] = Pair("Cards & slots", "Karten & Slots"),
        ["h_vr_bg_cards"] = Pair(
            "Everything card-shaped on the board: the active-cards dock, the card slots' glow, "
            + "the item-use slot and the item cards.",
            "Alles Kartenförmige auf dem Brett: das Dock aktiver Karten, das Glühen der "
            + "Kartenslots, der Item-Slot und die Item-Karten."),
        ["vr_bg_active"] = Pair("Active cards", "Aktive Karten"),
        ["h_vr_bg_active"] = Pair(
            "The dock showing your currently active ability cards: position, card size and grid "
            + "step.",
            "Das Dock mit deinen gerade aktiven Fähigkeitskarten: Position, Kartengröße und "
            + "Rasterabstand."),
        ["vr_bg_slots"] = Pair("Slots & items", "Slots & Items"),
        ["h_vr_bg_slots"] = Pair(
            "The two card slots' snap glow, the item-use clip-in slot, and the item-card fan.",
            "Das Einrast-Glühen der beiden Kartenslots, der Item-Einsteck-Slot und der "
            + "Item-Kartenfächer."),
        ["vr_bg_piles"] = Pair("Piles", "Stapel"),
        ["h_vr_bg_piles"] = Pair(
            "The discard and burn piles on the board: where they sit, how large they are, and "
            + "the gap between them.",
            "Ablage- und Verbrannt-Stapel auf dem Brett: wo sie liegen, wie groß sie sind und "
            + "ihr Abstand zueinander."),
        ["vr_bg_readouts"] = Pair("Readouts & text", "Anzeigen & Text"),
        ["h_vr_bg_readouts"] = Pair(
            "Everything the board tells you: initiative track, placards and hints, the round "
            + "readout, objectives, elements and the decision dock.",
            "Alles, was das Brett dir anzeigt: Initiative-Leiste, Tafeln und Hinweise, die "
            + "Runden-Anzeige, Aufgaben, Elemente und das Entscheidungsdock."),
        ["vr_bg_placards"] = Pair("Placards & hints", "Tafeln & Hinweise"),
        ["h_vr_bg_placards"] = Pair(
            "The initiative track, the status placard, the hover-hint panel and the round "
            + "readout: where each one sits on the board.",
            "Initiative-Leiste, Statustafel, Hinweistafel und Runden-Anzeige: wo sie jeweils "
            + "auf dem Brett sitzen."),
        ["vr_bg_docks"] = Pair("Objectives & elements", "Aufgaben & Elemente"),
        ["h_vr_bg_docks"] = Pair(
            "The objectives panel and the element-infusion dock: position and size of both.",
            "Die Aufgaben-Tafel und das Elemente-Dock: Position und Größe von beiden."),
        ["vr_bg_decision"] = Pair("Decision dock", "Entscheidungsdock"),
        ["h_vr_bg_decision"] = Pair(
            "The shared dock where decision prompts land (take damage, dialogs): position, size, "
            + "and the gap between the prompt text and the buttons.",
            "Das gemeinsame Dock für Entscheidungs-Abfragen (Schaden nehmen, Dialoge): Position, "
            + "Größe und der Abstand zwischen Abfragetext und Tasten."),
        ["h_vr_bg_misc"] = Pair(
            "Control-board entries not filed under a heading above — usually settings added "
            + "after this page was arranged. Nothing is ever lost here.",
            "Steuerbrett-Einträge ohne eigene Überschrift oben — meist Einstellungen, die nach "
            + "dieser Aufteilung dazukamen. Hier geht nichts verloren."),

        // ---- Erweitert ▸ Menüs & Tafeln: the hand-arranged heading tree (2026-08 overhaul,
        // audit 05 S3 — same remedy as the Steuerbrett page: "Ordne sie so an, dass man
        // schneller findet wonach man sucht … Geb auch den Überschriften Tooltipps"). Labels
        // "vr_pt_*", hints "h_vr_pt_*"; tree in VROptionsTab.7.TopicTrees.cs. ----------------
        ["vr_pt_switches"] = Pair("Panels & readouts", "Tafeln & Anzeigen"),
        ["h_vr_pt_switches"] = Pair(
            "Every panel the mod draws in the world: which ones exist, plus their shared "
            + "fine-tuning.",
            "Alle Tafeln, die der Mod in die Welt zeichnet: welche es gibt, dazu ihre "
            + "gemeinsame Feinjustage."),
        ["vr_pt_sw_show"] = Pair("Switches", "Schalter"),
        ["h_vr_pt_sw_show"] = Pair(
            "On/off for each panel — initiative track, elements, objectives, stat sheets, "
            + "dialogs, decision surfaces, wrist displays, tooltips.",
            "An/aus je Tafel — Initiative-Leiste, Elemente, Aufgaben, Statustafeln, Dialoge, "
            + "Entscheidungsflächen, Handgelenk-Anzeigen, Tooltips."),
        ["vr_pt_sw_fine"] = Pair("Fine-tuning", "Feinjustage"),
        ["h_vr_pt_sw_fine"] = Pair(
            "Sizes, clearances and texture quality shared by the panels above.",
            "Größen, Abstände und Texturqualität, die sich die Tafeln oben teilen."),
        ["vr_pt_combatlog"] = Pair("Combat log", "Kampflog"),
        ["h_vr_pt_combatlog"] = Pair(
            "The floating combat-log panel: on/off, whether it follows you, and its "
            + "grab-persisted position and size.",
            "Die schwebende Kampflog-Tafel: an/aus, ob sie dir folgt, und ihre beim Greifen "
            + "gemerkte Position und Größe."),
        ["vr_pt_bars"] = Pair("Health bars", "Lebensbalken"),
        ["h_vr_pt_bars"] = Pair(
            "The bars above figures: on/off, size and its zoom clamp, distance behaviour, "
            + "visibility behind walls.",
            "Die Balken über den Figuren: an/aus, Größe samt Zoom-Klammer, "
            + "Abstands-Verhalten, Sichtbarkeit hinter Wänden."),
        ["vr_pt_screen"] = Pair("2D screen & 3D depth", "2D-Schirm & 3D-Tiefe"),
        ["h_vr_pt_screen"] = Pair(
            "The floating 2D screen the menus live on: its basics, the stereo-depth "
            + "compositor, and the campaign-map fix.",
            "Der schwebende 2D-Schirm, auf dem die Menüs leben: Grundlagen, der "
            + "Stereo-Tiefen-Compositor und der Weltkarten-Fix."),
        ["vr_pt_screen_basic"] = Pair("Screen", "Schirm"),
        ["h_vr_pt_screen_basic"] = Pair(
            "Existence, auto-show, intro, width, distance, and what the desktop monitor "
            + "mirrors.",
            "Existenz, Auto-Anzeige, Intro, Breite, Abstand und was der Desktop-Monitor "
            + "spiegelt."),
        ["vr_pt_screen_depth"] = Pair("3D depth", "3D-Tiefe"),
        ["h_vr_pt_screen_depth"] = Pair(
            "The stereo screen: depth strength, parallax, video handling and the two-layer "
            + "compositor internals.",
            "Der Stereo-Schirm: Tiefenstärke, Parallaxe, Video-Behandlung und die Interna "
            + "des zweilagigen Compositors."),
        ["vr_pt_screen_map"] = Pair("World map", "Weltkarte"),
        ["h_vr_pt_screen_map"] = Pair(
            "The campaign map's re-render fix and its cloud layer.",
            "Der Render-Fix der Kampagnenkarte und ihre Wolkenschicht."),
        ["vr_pt_click"] = Pair("Pointing & clicking", "Klick & Zeigen"),
        ["h_vr_pt_click"] = Pair(
            "How a poke or a laser press becomes a click: poke depth, click delivery, drag "
            + "latching, mouse plumbing.",
            "Wie aus Antippen oder Laser-Druck ein Klick wird: Antipp-Tiefe, "
            + "Klick-Übermittlung, Zieh-Verriegelung, Maus-Verkabelung."),
        ["vr_pt_hexhint"] = Pair("Hex hint", "Feld-Hinweis"),
        ["h_vr_pt_hexhint"] = Pair(
            "The info panel over the hovered hex: gaze behaviour and its three offsets.",
            "Die Infotafel über dem angepeilten Feld: Blick-Verhalten und ihre drei "
            + "Versätze."),
        ["vr_pt_windows"] = Pair("Windows & dialogs", "Fenster & Dialoge"),
        ["h_vr_pt_windows"] = Pair(
            "How unknown game windows are caught and shown, and the rescue chord that always "
            + "brings the screen back.",
            "Wie unbekannte Spielfenster gefangen und angezeigt werden, und der Notgriff, "
            + "der den Schirm immer zurückholt."),
        // The 3D map room's panel rows (2026-08-22 settings audit): the card hand, and the two
        // travel-button offsets the user asked to have "im debug menu" and set himself.
        ["vr_pt_maproom"] = Pair("3D map", "Karte 3D"),
        ["h_vr_pt_maproom"] = Pair(
            "The 3D campaign map's own panels: the card hand, and where the travel-confirm "
            + "button sits on the map window.",
            "Die Tafeln der 3D-Kampagnenkarte: die Kartenhand, und wo der Reise-Knopf am "
            + "Kartenfenster sitzt."),
        ["h_vr_sec_keyboard"] = Pair(
            "The on-screen keyboard for text fields, and its capitalisation.",
            "Die Bildschirmtastatur für Textfelder und ihre Großschreibung."),

        // ---- Erweitert ▸ Karten & Fächer: the hand-arranged heading tree ("vr_ct_*"). -------
        ["vr_ct_fanshape"] = Pair("Fan shape", "Fächer-Form"),
        ["h_vr_ct_fanshape"] = Pair(
            "The hand fan's geometry: card size, spread per card, total arc, radius, arch "
            + "and tilt.",
            "Die Geometrie des Handfächers: Kartengröße, Spreizung je Karte, Gesamtbogen, "
            + "Radius, Wölbung und Neigung."),
        ["vr_ct_fanbehavior"] = Pair("Fan behaviour", "Fächer-Verhalten"),
        ["h_vr_ct_fanbehavior"] = Pair(
            "When the fan opens and how it reacts: the wrist gesture's angles, palm "
            + "following, gaze following, the hover gap.",
            "Wann der Fächer öffnet und wie er reagiert: die Winkel der Handgelenks-Geste, "
            + "Handflächen-Folge, Blick-Folge, die Hover-Lücke."),
        ["vr_ct_anim"] = Pair("Animations", "Animationen"),
        ["h_vr_ct_anim"] = Pair(
            "Timing of everything the cards do: opening, closing, flying, the character "
            + "swap, the particle bursts.",
            "Das Timing von allem, was die Karten tun: Öffnen, Schließen, Fliegen, der "
            + "Charakterwechsel, die Partikel."),
        ["vr_ct_anim_open"] = Pair("Open & close", "Öffnen & Schließen"),
        ["h_vr_ct_anim_open"] = Pair(
            "How fast the fan deals out, folds away, and how fast cards fly.",
            "Wie schnell der Fächer austeilt, sich einklappt, und wie schnell Karten "
            + "fliegen."),
        ["vr_ct_anim_swap"] = Pair("Character swap", "Charakterwechsel"),
        ["h_vr_ct_anim_swap"] = Pair(
            "The hand-exchange animation when you switch characters: timing, travel, arc, "
            + "spin, overshoot.",
            "Die Handtausch-Animation beim Charakterwechsel: Timing, Weg, Bogen, Drehung, "
            + "Nachschwingen."),
        ["vr_ct_anim_fx"] = Pair("Particles", "Partikel"),
        ["h_vr_ct_anim_fx"] = Pair(
            "The card dust burst and the game's own card particles — both off by ruling.",
            "Die Karten-Staubwolke und die spieleigenen Karten-Partikel — beide per "
            + "Entscheidung aus."),
        ["vr_ct_items"] = Pair("Items", "Gegenstände"),
        ["h_vr_ct_items"] = Pair(
            "The item pile's fan and its attention dressing: the emerge/collapse animation, "
            + "the rings, embers and the use berth.",
            "Der Fächer des Gegenstände-Stapels und seine Aufmerksamkeits-Signale: die "
            + "Auf-/Zuklapp-Animation, Ringe, Funken und die Ablage."),
        ["vr_ct_items_fan"] = Pair("Item fan", "Gegenstands-Fächer"),
        ["h_vr_ct_items_fan"] = Pair(
            "How the item cards deal out of the pile and fold back in.",
            "Wie die Gegenstandskarten aus dem Stapel austeilen und wieder einklappen."),
        ["vr_ct_items_cue"] = Pair("Cues & use berth", "Hinweise & Ablage"),
        ["h_vr_ct_items_cue"] = Pair(
            "The 'an item could act now' signals: pile rings and embers, and the use "
            + "berth's outline, glow and ping.",
            "Die Signale für 'ein Gegenstand könnte jetzt wirken': Ringe und Funken am "
            + "Stapel, dazu Umriss, Leuchten und Ping der Ablage."),
        ["vr_ct_held"] = Pair("Held card", "Gehaltene Karte"),
        ["h_vr_ct_held"] = Pair(
            "The card in your hand: grab button, close-up size, how it sits in the grip, "
            + "and the slot glow it lands in.",
            "Die Karte in deiner Hand: Greif-Taste, Nahansicht, wie sie im Griff sitzt, und "
            + "das Slot-Glühen, in dem sie landet."),
        ["vr_ct_piles"] = Pair("Piles", "Stapel"),
        ["h_vr_ct_piles"] = Pair(
            "The pile stacks at the board's edge and their poke-open browse fans — spread "
            + "and radius per pile.",
            "Die Stapel am Brettrand und ihre antippbaren Blätter-Fächer — Spreizung und "
            + "Radius je Stapel."),
        ["vr_ct_board"] = Pair("Control board", "Kontrollbrett"),
        ["h_vr_ct_board"] = Pair(
            "The board's choice, everyday pose, its grab-written pose state ('edit only to "
            + "reset') and the spawn recipe.",
            "Die Brett-Wahl, die Alltags-Haltung, sein beim Greifen gemerkter Zustand "
            + "('nur zum Zurücksetzen ändern') und das Start-Rezept."),
        ["vr_ct_sounds"] = Pair("Sounds", "Klänge"),
        ["h_vr_ct_sounds"] = Pair(
            "The everyday on/off switch, then the five audio items behind it — free-text "
            + "game sound names, empty = that one silent.",
            "Der Alltags-Schalter, dahinter die fünf Audio-Items — freie "
            + "Spiel-Sound-Namen, leer = dieser eine stumm."),
        ["h_vr_tree_misc"] = Pair(
            "Entries not filed under a heading above — usually settings added after this "
            + "page was arranged. Nothing is ever lost here.",
            "Einträge ohne eigene Überschrift oben — meist Einstellungen, die nach dieser "
            + "Aufteilung dazukamen. Hier geht nichts verloren."),

        // ---- Localized names for AUTOMATIC key-prefix groups (2026-08 overhaul, audit 05
        // S4/§2.4 — GroupWordLabel in ConfigCatalog): the raw leading key words "Fan", "Item",
        // "Held", "Screen" used to head German pages. Safety net for topics without a
        // hand-built tree and for sections that grow past the split threshold later. ---------
        ["cfg_gw_fan"] = Pair("Fan", "Fächer"),
        ["cfg_gw_card"] = Pair("Cards", "Karten"),
        ["cfg_gw_item"] = Pair("Items", "Gegenstände"),
        ["cfg_gw_held"] = Pair("Held card", "Gehaltene Karte"),
        ["cfg_gw_tray"] = Pair("Board", "Brett"),
        ["cfg_gw_board"] = Pair("Board", "Brett"),
        ["cfg_gw_spawn"] = Pair("Board start", "Brett-Start"),
        ["cfg_gw_reveal"] = Pair("Fan gesture", "Fächer-Geste"),
        ["cfg_gw_pile"] = Pair("Piles", "Stapel"),
        ["cfg_gw_slot"] = Pair("Card slots", "Kartenslots"),
        ["cfg_gw_screen"] = Pair("2D screen", "2D-Schirm"),
        ["cfg_gw_combat"] = Pair("Combat log", "Kampflog"),
        ["cfg_gw_bar"] = Pair("Health bars", "Lebensbalken"),
        ["cfg_gw_hex"] = Pair("Hex hint", "Feld-Hinweis"),
        ["cfg_gw_map"] = Pair("World map", "Weltkarte"),
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
        // [Elements] — the environment's reaction to the element infusions (ELEMENT MOOD). Without
        // this line the generic browser would head the block with the raw English section word in a
        // German menu, the same defect the [Net] line above was added to fix.
        ["cfg_sec_elements"] = Pair("Elements & surroundings", "Elemente & Umgebung"),
        // [Haunt] — the creepy easter eggs (HAUNT, Core/Haunt.cs). Same reason as the line above:
        // a brand-new cfg section needs a heading or the generic browser prints the raw English
        // section word in a German menu.
        ["cfg_sec_haunt"] = Pair("Creepy easter eggs", "Grusel-Easter-Eggs"),
        // [EnvSound] — the environment heard (ENV SOUND, Core/EnvSound.cs). Same reason as the two
        // lines above: a brand-new cfg section needs a heading or the generic browser prints the raw
        // English section word in a German menu.
        ["cfg_sec_envsound"] = Pair("Environment sounds", "Umgebungsgeräusche"),
        // [MapRoom] — the 3D campaign map's own dials (MAP ROOM, WorldUI/MapRoom/*). Same reason as
        // the three lines above, but it became load-bearing in ModBuild 196: the five icon-size dials
        // moved off the curated Grafik page onto their own Erweitert group, so this heading is now the
        // ONLY signpost to them. Deliberately NOT the flat screen's "Weltkarte" key (vr_pt_screen_map)
        // — that one names the map RE-RENDER subtree and the two would rot together.
        ["cfg_sec_maproom"] = Pair("3D campaign map", "3D-Kampagnenkarte"),

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
        // Heads the [WristHud] block on the HANDS page (ConfigCatalog.SectionLabel). Named after
        // the WIDGET, not the body part, because that is what every row under it is called
        // ("Arm-HUD: quer (m)", "Arm-HUD: Neigung (°)") and because the six retired [WorldUI]
        // twins that used to carry the same captions on the panels page are gone — a player who
        // goes looking where those used to be needs the surviving block to announce itself by the
        // name they were reading (user 2026-08-09: "Ich finde die offsets an der Stelle im Debug
        // nicht mehr wo sie vorher waren" — "I can no longer find the offsets in the debug menu
        // where they used to be").
        ["cat_wrist"] = Pair("Arm HUD", "Arm-HUD"),

        // ---- WristHud ----
        ["no_character"] = Pair("no character", "kein Charakter"),
        ["hp"] = Pair("HP", "LP"),
        ["xp"] = Pair("XP", "EP"),
        ["gold"] = Pair("Gold", "Gold"),
        ["no_conditions"] = Pair("no conditions", "keine Zustände"),
        ["selecting_cards"] = Pair("selecting cards", "wählt Karten"),
        ["current_turn"] = Pair("current turn", "am Zug"),
        ["selected"] = Pair("selected", "ausgewählt"),

        // ---- multiplayer mod-version handshake (VersionGuard / VersionDialog) ----
        // {0} = the mismatching peer's username, {1} = their version, {2} = ours (both already
        // formatted "0.1.0 (Build N)" — or the ver_build_unknown text for pre-handshake builds).
        ["ver_mismatch_title"] = Pair("Mod version mismatch", "Mod-Versionskonflikt"),
        ["ver_mismatch_body"] = Pair(
            "{0} is running GloomhavenVR {1} — you are running {2}.\n\n"
            + "VR sync between different mod builds stays off. \"Join as flat player\" keeps you "
            + "in the session and in VR, with the mod's networking disabled for this session "
            + "(the normal multiplayer game is unaffected). \"Cancel\" leaves the session.",
            "{0} spielt GloomhavenVR {1} — du spielst {2}.\n\n"
            + "Der VR-Abgleich zwischen unterschiedlichen Mod-Builds bleibt aus. \"Als "
            + "Flat-Spieler joinen\" lässt dich in der Sitzung und in VR, das Mod-Netzwerk ist "
            + "für diese Sitzung deaktiviert (das normale Mehrspieler-Spiel läuft unverändert). "
            + "\"Abbrechen\" verlässt die Sitzung."),
        ["ver_join_flat"] = Pair("Join as flat player", "Als Flat-Spieler joinen"),
        ["ver_cancel"] = Pair("Cancel", "Abbrechen"),
        ["ver_build_unknown"] = Pair("an older build (before version negotiation)",
                                     "einen älteren Build (vor dem Versionsabgleich)"),

        // ---- FlatScreen desktop splash ----
        ["starting_desktop"] = Pair("starting… (intro plays on the desktop)",
                                    "startet… (Intro läuft auf dem Desktop)"),
    };
}
