using System;
using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary.CustomLevels;

namespace GloomhavenVR.Compat;

/// <summary>
/// VR HINT TEXT — swaps tutorial hints that teach the FLAT controls (mouse / WASD /
/// edge scroll / on-screen HUD buttons) for VR instructions, at display time.
///
/// INTERCEPT POINT (verified in the decompiled sources): every level-message page
/// resolves its text in ONE funnel, <c>LevelMessagePageUI.OnLanguageChanged()</c>
/// (LevelMessagePageUI.cs:41-49 — called from <c>Init</c> and <c>RefreshText</c>, and by
/// the I2 language-change event), reading <c>page.PageTextKey</c> (or
/// <c>PageTextKeyController</c> on gamepad); the box title — which for the
/// <c>HelpText</c> strip IS the whole one-line instruction — resolves in
/// <c>LevelMessageUILayout.Init/OnLanguageChanged</c> from <c>_message.TitleKey</c>.
/// Postfixes there get the last word after every path that (re)writes the text,
/// including the gamepad-variant rewrites.
///
/// MATCHING, four tiers (first hit wins; each replacement logged once per key):
/// 0. BURNT-CARD BOX (<see cref="TryOverrideBurntCard"/>) — the short-rest burn box (TB_20),
///    whose flat explanation is a COLOUR tell ("the card is dark red now"). VR has no such
///    tell: the mod's cards are physical and a burnt card goes to the burnt PILE stack, so
///    the replacement states the rule and sends the player to that pile. See the method.
/// 1. EXACT KEY table (<see cref="BodyOverrides"/>/<see cref="TitleOverrides"/>) —
///    keys pinned from the tutorial-2 hardware flow dump
///    (.planning/debug/LogOutput.log — TutorialFlowPatches prints every scripted
///    message's keys at scenario start). The literal game STRINGS are not readable on
///    this machine (the localization payload — I2 <c>LanguageSourceAsset</c> global
///    sources + the ruleset <c>LinkedCSV</c>s — lives in the game install's asset
///    data, not in the Managed DLLs the repo mirrors), so the classification is
///    STRUCTURAL: every <c>HelpText</c> strip instructs exactly the action its
///    DISMISS trigger waits for (that is what dismisses it), so a VR instruction for
///    that same action is correct BY CONSTRUCTION regardless of the flat wording;
///    TUTORIAL_2_TEXT_003 is the camera box (user-verified on hardware: the
///    "W A S D" instructions). Every referenced VR control was cross-checked against
///    the mod's input code — WorldGrab (stick-click drag/rotate/zoom), SnapTurn
///    (stick-axis flick), ProximityGrabber (cards and figures grab on the TRIGGER; the
///    [Cards] GrabButton dial is retired),
///    BoardClickDriver (laser trigger / fingertip poke, second-click-to-confirm),
///    HalfSelection (docked round cards, top/bottom poke, on-face default-action
///    buttons), PlayTray/ButtonCluster (CONFIRM/UNDO/SKIP keycaps, round rest
///    keycaps left of the board), UseBarsSurface (bars docked below the board).
/// 2. KEY PATTERN — the KEY (not the text) contains "CAMERA"/"MOUSE"/"KEYBOARD"
///    (ordinal, case-insensitive). Loc keys are internal IDs; a controls-hint key
///    carries such a word, story keys do not.
/// 3. RESOLVED-TEXT MARKER scan — safety net for tutorials whose keys have NOT been
///    dumped yet (only tutorial 2 has a hardware flow dump): if the text the game
///    just resolved contains an unambiguous flat-input token ("WASD", "Mausrad",
///    "right-click", "Leertaste", "left stick", …) the hint is a controls hint by
///    definition and is swapped for the generic VR-controls text. This deliberately
///    relaxes the earlier "never match display strings" rule ONLY for hardware-input
///    tokens that cannot occur in story prose (the rule was about prose collisions;
///    TEXT_003 proved key names alone are opaque — it contains no "CAMERA").
///    Tokens cover EN + DE (the mod's replacement languages); every match is logged
///    loudly so the key can be promoted to tier 1 after one hardware run.
///
/// GATING: every postfix checks <see cref="TutorialVR.Enabled"/> (the bridge's error latch —
/// the [Compat] TutorialVRAdapt config gate went with the 2026-08-13 ruling) AND
/// <see cref="TutorialVR.IsTutorialActive"/> — front-end tutorial, guildmaster tutorial,
/// tutorial/intro-flagged map scenarios — so a coincidental match in normal play is impossible. A headless sweep for flat-specific
/// keys in NON-tutorial scripted levels is not possible without the game data; if a
/// hardware log ever shows one, widen the gate deliberately then.
///
/// Story/game-rule hints are untouched: anything matching no tier keeps the game's own
/// translation. Outside a tutorial — or once the error latch trips — the postfixes no-op and the
/// game's own text stands (there is no config switch any more). This funnel rewrites
/// TEXT only — it can never add a step. Injecting extra CLevelMessages into
/// <c>LevelEventsController.m_MessagesToShow</c> stays deliberately unbuilt (that IS new
/// machinery on the scripted chain); where the VR tutorial genuinely needs an ADDITIONAL step,
/// <see cref="TutorialGrabStep"/> shows a mod-owned message through the handler's public
/// <c>ShowHelpText</c> instead, entirely beside the controller's trigger stores. Such messages
/// are matched here by MESSAGE NAME (in <see cref="TryOverrideTitle"/>), not by loc key.
///
/// NAMING THE CARD: a hint that demands ONE SPECIFIC card must NAME it — in VR the player
/// opens their own hand fan, so "play the requested card" means brute-forcing (user ruling
/// 2026-08). <see cref="NamedVariants"/> maps such a step's generic mod text to a "{0}"
/// variant that <see cref="TutorialCardNames"/> fills from the GAME's own data at display
/// time (the step's own trigger carries the card's localization term). A step whose card
/// cannot be resolved keeps the generic wording and logs a Warn naming it — never a guessed
/// or English-only name.
/// </summary>
internal static class TutorialHints
{
    /// <summary>Pinned PAGE (body) keys → mod loc id. TEXT_003 is the tutorial-2 camera
    /// box (user-verified flat "W A S D" content); the other TB pages are story/rules
    /// prose with no required input action (their dismiss is the box's own Continue
    /// button, which exists in VR) and stay vanilla.</summary>
    private static readonly Dictionary<string, string> BodyOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["TUTORIAL_2_TEXT_003"] = "tut_vr_move_body",
        };

    /// <summary>Pinned TITLE keys → mod loc id. For the HelpText strip the title IS the
    /// instruction line; each mapping instructs (in VR terms) exactly the action the
    /// strip's dismiss trigger waits for — see the flow dump in the class header.</summary>
    private static readonly Dictionary<string, string> TitleOverrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["TUTORIAL_2_HELP_004"] = "tut_vr_pick_card",      // dismiss: AbilityCardSelected 'Trample'
            ["TUTORIAL_2_HELP_005"] = "tut_vr_pick_card2",     // dismiss: AbilityCardSelected 'Grab and Go'
            ["TUTORIAL_2_HELP_006"] = "tut_vr_confirm_cards",  // dismiss: ConfirmButtonPressed
            ["TUTORIAL_2_HELP_007"] = "tut_vr_half_bottom",    // dismiss: CardBottomHalfSelected
            ["TUTORIAL_2_HELP_008"] = "tut_vr_hex",            // dismiss: TileSelectedForAbility
            ["TUTORIAL_2_HELP_009"] = "tut_vr_execute",        // dismiss: SEvent Ability step (2nd tap / confirm)
            ["TUTORIAL_2_HELP_010"] = "tut_vr_initiative",     // dismiss: InitiativeAvatarHovered
            ["TUTORIAL_2_HELP_011_1"] = "tut_vr_hex",          // dismiss: TileSelectedForAbility
            ["TUTORIAL_2_HELP_011_2"] = "tut_vr_execute",      // dismiss: SEvent Ability step
            ["TUTORIAL_2_HELP_012"] = "tut_vr_half_top",       // dismiss: CardTopHalfSelected
            ["TUTORIAL_2_HELP_013"] = "tut_vr_hex_target",     // dismiss: TileSelectedForAbility (attack)
            ["TUTORIAL_2_HELP_014"] = "tut_vr_execute",        // dismiss: SEvent Ability step (attack)
            ["TUTORIAL_2_HELP_015"] = "tut_vr_confirm",        // dismiss: ConfirmButtonPressed
            ["TUTORIAL_2_HELP_016"] = "tut_vr_choice_panel",   // dismiss: PlayerChoseDamage (floating dialog)
            ["TUTORIAL_2_HELP_018_1"] = "tut_vr_short_rest",   // dismiss: ShortRestPressed
            ["TUTORIAL_2_HELP_018_2"] = "tut_vr_confirm",      // dismiss: ShortRestConfirmed (popup on keycaps)
            ["TUTORIAL_2_HELP_020"] = "tut_vr_pick_card",      // dismiss: AbilityCardSelected 'Provoking Roar'
            ["TUTORIAL_2_HELP_021_1"] = "tut_vr_pick_card2",   // dismiss: AbilityCardSelected 'Trample'
            ["TUTORIAL_2_HELP_021_2"] = "tut_vr_confirm_cards",// dismiss: ConfirmButtonPressed
            ["TUTORIAL_2_HELP_023_1"] = "tut_vr_half_top",     // dismiss: CardTopHalfSelected
            ["TUTORIAL_2_HELP_023_2"] = "tut_vr_hex_target",   // dismiss: TileSelectedForAbility (attack)
            ["TUTORIAL_2_HELP_023_3"] = "tut_vr_execute",      // dismiss: SEvent Ability step
            ["TUTORIAL_2_HELP_024_1"] = "tut_vr_default_move", // dismiss: DefaultMoveAbilityPressed
            ["TUTORIAL_2_HELP_024_2"] = "tut_vr_hex",          // dismiss: TileSelectedForAbility
            ["TUTORIAL_2_HELP_024_3"] = "tut_vr_execute",      // dismiss: SEvent Ability step
            ["TUTORIAL_2_HELP_024_4"] = "tut_vr_skip",         // dismiss: SkipButtonPressed
            ["TUTORIAL_2_HELP_024_5"] = "tut_vr_confirm",      // dismiss: ConfirmButtonPressed
        };

    /// <summary>
    /// Generic step text → its NAMED variant (a "{0}" format string). Applied whenever the
    /// step's own trigger identifies the card (see <see cref="TutorialCardNames"/>): these
    /// are exactly the steps that demand ONE specific card be played/placed/selected, where
    /// an unnamed instruction forces the player to brute-force the hand fan. Steps that
    /// name no card (confirm, hex pick, execute, rest, skip …) are deliberately absent.
    /// </summary>
    private static readonly Dictionary<string, string> NamedVariants =
        new(StringComparer.Ordinal)
        {
            ["tut_vr_pick_card"] = "tut_vr_pick_card_named",     // AbilityCardSelected(card)
            ["tut_vr_pick_card2"] = "tut_vr_pick_card2_named",   // AbilityCardSelected(card)
            ["tut_vr_half_bottom"] = "tut_vr_half_bottom_named", // CardBottomHalfSelected(card)
            ["tut_vr_half_top"] = "tut_vr_half_top_named",       // CardTopHalfSelected(card)
        };

    /// <summary>
    /// Tier-0 BURNT-CARD box. Page 1 of the short-rest burn box (TB_20, displayed by the
    /// game's own <c>ShortRestChoseToBurn</c> event — flow dump, .planning/debug/LogOutput.log)
    /// is the immediate reaction to the burn, so replacing it is correct BY CONSTRUCTION
    /// whatever the flat wording was (the same structural argument the exact table rests on).
    /// </summary>
    private const string BurntCardPage = "TUTORIAL_2_TEXT_020_1";

    /// <summary>The burn box's remaining pages. They keep the game's own prose UNLESS they
    /// still carry the flat COLOUR tell (<see cref="ColourTellMarkers"/>) — that sentence is
    /// simply false in VR, where a burnt card is identified by WHERE IT LIES, not by a tint.
    /// A hit is logged as a Warn so the pin can be moved to the real page after one run.</summary>
    private static readonly string[] BurntCardFollowPages =
        { "TUTORIAL_2_TEXT_020_2", "TUTORIAL_2_TEXT_020_3" };

    /// <summary>Colour-tell tokens (EN + DE), matched ONLY inside the burn box's own pages —
    /// narrow on purpose: a bare "rot"/"red" would collide with prose ("verrottet", "reddish
    /// glow"), these compounds cannot mean anything but "the card is tinted".</summary>
    private static readonly string[] ColourTellMarkers =
    {
        "dunkelrot", "dunkelrote", "dunkelroten", "dunkelrotem",
        "dark red", "dark-red", "darkred",
        "rot gefärbt", "rot eingefärbt", "rot markiert", "wird rot",
        "tinted red", "turns red", "red tint",
    };

    /// <summary>Tier-2 key substrings — a loc KEY carrying one of these names a
    /// flat-controls hint (internal IDs, language-independent).</summary>
    private static readonly string[] KeyPatterns = { "CAMERA", "MOUSE", "KEYBOARD" };

    /// <summary>
    /// Tier-3 tokens: hardware-input words that cannot occur in story prose. Substring
    /// match (ordinal, case-insensitive) on the text the game just RESOLVED, so the scan
    /// is language-correct for EN/DE and covers gamepad-variant rewrites too. "Maus" is
    /// deliberately only matched in compounds/with an article — plain "maus" would hit
    /// "Mausoleum"-class prose.
    /// </summary>
    private static readonly string[] FlatTextMarkers =
    {
        "wasd", "w a s d", "w, a, s, d",
        "keyboard", "tastatur",
        "mouse", "mausrad", "maustaste", "der maus", "die maus", "mit maus",
        "scroll wheel", "scrollrad",
        "right-click", "right click", "rechtsklick",
        "left-click", "left click", "linksklick",
        "spacebar", "space bar", "leertaste",
        "esc key", "esc-taste", "press esc", "esc drücken",
        "edge of the screen", "bildschirmrand",
        "right stick", "left stick", "rechter stick", "linker stick",
        "d-pad", "steuerkreuz",
    };

    /// <summary>Keys already logged as overridden this session (log once per key, not per repaint).</summary>
    private static readonly HashSet<string> Logged = new(StringComparer.OrdinalIgnoreCase);

    internal static bool TryOverrideBody(string? key, string? controllerKey, string? resolved,
        out string text)
        => TryOverrideBurntCard(key, controllerKey, resolved, out text)
        || TryOverride(key, controllerKey, resolved, BodyOverrides,
            "tut_vr_move_body", "tut_vr_controls_body", "page", null, out text);

    /// <summary>
    /// Title override for a scripted (or mod-owned) message. Takes the whole
    /// <see cref="CLevelMessage"/> because the card a step demands lives in the message's own
    /// DISMISS trigger — the same trigger that will close the strip once that card is played
    /// — so the instruction and the completion condition can never name different cards.
    /// </summary>
    internal static bool TryOverrideTitle(CLevelMessage message, string? resolved, out string text)
    {
        // MOD-OWNED messages (Compat.TutorialGrabStep's extra VR step). These carry a
        // placeholder loc key on purpose (a mod-invented key would make the game's
        // LocalizationManager log "term not found"), so they are identified by MESSAGE NAME —
        // which the mod itself authored and which can never collide with a scripted one.
        if (string.Equals(message.MessageName, TutorialGrabStep.MessageName, StringComparison.Ordinal))
        {
            text = Loc.Mod("tut_vr_grab_intent");
            return true;
        }
        return TryOverride(message.TitleKey, message.TitleKeyController, resolved, TitleOverrides,
            "tut_vr_move_title", "tut_vr_controls_line", "title", message.DismissTrigger, out text);
    }

    /// <summary>
    /// The short-rest BURN box: state the rule (burnt = lost for the scenario) — naming the
    /// card when it can be identified — and then point at the VR burnt PILE, the only place
    /// the card can still be looked at once it has left the hand.
    ///
    /// The pile paragraph is appended only while the pile stacks exist
    /// (<see cref="PilesAvailable"/> — unconditionally true since the 2026-08-11 ruling retired
    /// the [Cards] PileViewer dial; the guard stays so the mod can never teach an interaction
    /// that is not there). When they do exist the interaction is the stack's
    /// real one — fingertip poke or board laser click toggles the browse fan
    /// (<c>PileViewer.PileStack.OnPoke</c>/<c>LaserToggle</c>), and the stack is live because
    /// the burn has just put a card on it (<c>_hasCards</c>; the hardware log shows
    /// discard=0, burnt=1 the instant this box opens).
    /// </summary>
    private static bool TryOverrideBurntCard(string? key, string? controllerKey, string? resolved,
        out string text)
    {
        text = string.Empty;
        bool primary = Matches(BurntCardPage, key, controllerKey);
        bool follow = !primary && MatchesAny(BurntCardFollowPages, key, controllerKey)
                      && HasColourTell(resolved);
        if (!primary && !follow)
            return false;

        string pileParagraph = PilesAvailable()
            ? string.Format(Loc.Mod("tut_vr_burnt_card_pile"), PileViewer.Caption(PileKind.Burnt))
            : string.Empty;

        if (follow)
        {
            // The colour sentence sits on a page we did NOT pin. Replacing it with the pile
            // paragraph keeps the box truthful; without the piles there is nothing to say
            // instead, so the game's own page stays (it is prose, not an instruction).
            if (pileParagraph.Length == 0)
                return false;
            text = pileParagraph;
            if (Logged.Add("burnt-follow:" + key))
                VRLog.Warn("Tutorial", $"burnt-card box: the flat COLOUR tell is on page '{key}', "
                    + $"not on the pinned '{BurntCardPage}' — replaced it with the burnt-pile "
                    + "paragraph. Move the pin to this key after this run.");
            return true;
        }

        string rule;
        if (TutorialCardNames.TryBurntCardName(out string card))
        {
            rule = string.Format(Loc.Mod("tut_vr_burnt_card"), card);
        }
        else
        {
            rule = Loc.Mod("tut_vr_burnt_card_plain");
            if (Logged.Add("unnamed-burnt"))
                VRLog.Warn("Tutorial", $"burnt-card step '{BurntCardPage}': the burnt card could "
                    + "not be identified (no unique lost card and no SetShortRestCard level "
                    + "event) — using the unnamed wording. The player has to open the burnt pile "
                    + "to see which card it was.");
        }

        text = pileParagraph.Length == 0 ? rule : rule + "\n\n" + pileParagraph;
        LogOnce(key ?? controllerKey ?? BurntCardPage, "page", "burnt-card", "tut_vr_burnt_card");
        return true;
    }

    /// <summary>Do the VR pile stacks exist? Always on — user ruling 2026-08-11: essential
    /// (the [Cards] PileViewer dial is gone; the stacks build whenever an active hand exists).</summary>
    private static bool PilesAvailable() => true;

    private static bool Matches(string pinned, string? key, string? controllerKey) =>
        string.Equals(pinned, key, StringComparison.OrdinalIgnoreCase)
        || string.Equals(pinned, controllerKey, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAny(string[] pinned, string? key, string? controllerKey)
    {
        foreach (string p in pinned)
            if (Matches(p, key, controllerKey))
                return true;
        return false;
    }

    private static bool HasColourTell(string? resolved)
    {
        if (string.IsNullOrEmpty(resolved))
            return false;
        foreach (string marker in ColourTellMarkers)
            if (resolved!.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static bool TryOverride(string? key, string? controllerKey, string? resolved,
        Dictionary<string, string> exact, string patternLocId, string markerLocId, string kind,
        CLevelTrigger? nameSource, out string text)
    {
        text = string.Empty;

        // Tier 1 — pinned key (primary, then the gamepad-variant key of the same hint).
        if (TryExact(key, exact, kind, nameSource, out text)
            || TryExact(controllerKey, exact, kind, nameSource, out text))
            return true;

        // Tier 2 — controls-word in the KEY itself.
        if (HasKeyPattern(key) || HasKeyPattern(controllerKey))
        {
            text = Loc.Mod(patternLocId);
            LogOnce(key ?? controllerKey ?? "?", kind, "key-pattern", patternLocId);
            return true;
        }

        // Tier 3 — hardware-input token in the RESOLVED text (unpinned tutorials).
        if (!string.IsNullOrEmpty(resolved))
        {
            foreach (string marker in FlatTextMarkers)
            {
                if (resolved!.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                text = Loc.Mod(markerLocId);
                if (Logged.Add(kind + ":" + (key ?? "?")))
                    VRLog.Info("Tutorial", $"UNPINNED flat hint {kind} key '{key}' matched "
                        + $"marker '{marker}' — replaced with '{markerLocId}'. Promote this "
                        + "key to the exact table (TutorialHints) with a step-specific text.");
                return true;
            }
        }
        return false;
    }

    private static bool TryExact(string? key, Dictionary<string, string> exact, string kind,
        CLevelTrigger? nameSource, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrEmpty(key) || !exact.TryGetValue(key!, out string locId))
            return false;
        text = Resolve(locId, key!, nameSource);
        LogOnce(key!, kind, "exact", locId);
        return true;
    }

    /// <summary>
    /// The step's mod text — in its NAMED form when the step demands a specific card AND that
    /// card resolves from the step's own trigger. The name itself comes from the game's
    /// localization (see <see cref="TutorialCardNames"/>), so it always reads exactly like the
    /// title printed on the card. If it cannot be resolved we keep the generic sentence rather
    /// than print a raw term, and say so once per step in the log — that Warn is the signal
    /// that a player was left to brute-force this step.
    /// </summary>
    private static string Resolve(string locId, string key, CLevelTrigger? nameSource)
    {
        if (!NamedVariants.TryGetValue(locId, out string namedId))
            return Loc.Mod(locId);
        if (TutorialCardNames.TryFromTrigger(nameSource, out string cardName))
            return string.Format(Loc.Mod(namedId), cardName);
        if (Logged.Add("unnamed:" + key))
            VRLog.Warn("Tutorial", $"step '{key}' ({locId}) tells the player to play a SPECIFIC "
                + "card, but its name could not be resolved from the step's own dismiss trigger "
                + $"(ctxId '{nameSource?.EventTriggerContextId}') — falling back to the generic "
                + "wording, so the player has to find the card by trial and error here.");
        return Loc.Mod(locId);
    }

    private static bool HasKeyPattern(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return false;
        foreach (string pattern in KeyPatterns)
            if (key!.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private static void LogOnce(string key, string kind, string tier, string locId)
    {
        if (Logged.Add(kind + ":" + key))
            VRLog.Info("Tutorial", $"flat hint {kind} key '{key}' replaced with VR text "
                + $"'{locId}' ({tier} match, tutorial only).");
    }
}

/// <summary>Body text of every level-message page — the single funnel all repaint paths
/// share (Init, RefreshText, I2 language change, gamepad-variant swap).</summary>
[HarmonyPatch(typeof(LevelMessagePageUI), "OnLanguageChanged")]
internal static class LevelMessagePageUI_OnLanguageChanged_Patch
{
    private static void Postfix(LevelMessagePageUI __instance)
    {
        try
        {
            if (!TutorialVR.Enabled || !TutorialVR.IsTutorialActive)
                return;
            CLevelMessagePage? page = __instance.page; // publicized private
            if (page == null || __instance.information == null)
                return;
            if (TutorialHints.TryOverrideBody(page.PageTextKey, page.PageTextKeyController,
                    __instance.information.text, out string text))
                __instance.information.text = text;
        }
        catch (Exception ex)
        {
            VRLog.Warn("Tutorial", $"page-text override failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

/// <summary>Box/help-text TITLE — set in Init (and re-set on language/controller change);
/// both funnels get the same postfix so the override survives every rewrite. For the
/// HelpText strip the title is the entire one-line instruction.</summary>
[HarmonyPatch(typeof(LevelMessageUILayout))]
internal static class LevelMessageUILayout_Title_Patch
{
    [HarmonyPostfix]
    [HarmonyPatch("Init")]
    private static void InitPostfix(LevelMessageUILayout __instance, CLevelMessage message)
        => Apply(__instance, message);

    [HarmonyPostfix]
    [HarmonyPatch("OnLanguageChanged")]
    private static void LanguagePostfix(LevelMessageUILayout __instance)
        => Apply(__instance, __instance._message); // publicized private

    private static void Apply(LevelMessageUILayout ui, CLevelMessage? message)
    {
        try
        {
            if (!TutorialVR.Enabled || !TutorialVR.IsTutorialActive)
                return;
            if (message == null || ui.title == null || !ui.title.gameObject.activeSelf)
                return;
            if (TutorialHints.TryOverrideTitle(message, ui.title.text, out string text))
                ui.title.text = text;
        }
        catch (Exception ex)
        {
            VRLog.Warn("Tutorial", $"title override failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
