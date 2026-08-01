using System;
using System.Collections.Generic;
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
/// MATCHING, three tiers (first hit wins; each replacement logged once per key):
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
///    (stick-axis flick), ProximityGrabber ([Cards] GrabButton, trigger default),
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
/// GATING: double-gated to tutorial scenarios (<see cref="TutorialVR.IsTutorialActive"/> —
/// front-end tutorial, guildmaster tutorial, tutorial/intro-flagged map scenarios), so
/// a coincidental match in normal play is impossible. A headless sweep for flat-specific
/// keys in NON-tutorial scripted levels is not possible without the game data; if a
/// hardware log ever shows one, widen the gate deliberately then.
///
/// Story/game-rule hints are untouched: anything matching no tier keeps the game's own
/// translation. Reversible: config off ⇒ postfixes no-op ⇒ vanilla text. "New windows"
/// (task 5) are served by RICHER REPLACEMENT TEXT in the same box — the funnel rewrites
/// text only; injecting extra CLevelMessages into LevelEventsController.m_MessagesToShow
/// would be new machinery on the scripted chain and is deliberately not built.
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
        => TryOverride(key, controllerKey, resolved, BodyOverrides,
            "tut_vr_move_body", "tut_vr_controls_body", "page", out text);

    internal static bool TryOverrideTitle(string? key, string? controllerKey, string? resolved,
        out string text)
        => TryOverride(key, controllerKey, resolved, TitleOverrides,
            "tut_vr_move_title", "tut_vr_controls_line", "title", out text);

    private static bool TryOverride(string? key, string? controllerKey, string? resolved,
        Dictionary<string, string> exact, string patternLocId, string markerLocId, string kind,
        out string text)
    {
        text = string.Empty;

        // Tier 1 — pinned key (primary, then the gamepad-variant key of the same hint).
        if (TryExact(key, exact, kind, out text)
            || TryExact(controllerKey, exact, kind, out text))
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
        out string text)
    {
        text = string.Empty;
        if (string.IsNullOrEmpty(key) || !exact.TryGetValue(key!, out string locId))
            return false;
        text = Loc.Mod(locId);
        LogOnce(key!, kind, "exact", locId);
        return true;
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
            if (TutorialHints.TryOverrideTitle(message.TitleKey, message.TitleKeyController,
                    ui.title.text, out string text))
                ui.title.text = text;
        }
        catch (Exception ex)
        {
            VRLog.Warn("Tutorial", $"title override failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
