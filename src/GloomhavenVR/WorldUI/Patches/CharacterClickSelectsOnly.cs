using GloomhavenVR.Core;
using HarmonyLib;

namespace GloomhavenVR.WorldUI.Patches;

/// <summary>
/// A CLICK ON A CHARACTER SELECTS THAT CHARACTER — AND DOES NOTHING ELSE.
///
/// <para>THE USER REPORT, 2026-08-22, verbatim: <i>"Ich möchte das ein Klick auf den Character nur
/// den aktuell ausgewählten Character für die Handkarten ändert nicht direct das
/// Characterinfo-Sub-Menu öffnet, das soll wirklich nur dann passieren, wenn man auf das
/// entsprechende Symbol (das 1. mit der abgebildeten 'Person') in der Leiste klickt."</i>
/// — a click on a character must only change WHICH character is selected (and therefore whose hand
/// cards the room shows); the character-info sub-menu may open only from the first icon in the row,
/// the one that shows a person.</para>
///
/// <para>THE SEAM, read out of the decompile and not inferred.
/// <c>NewPartyCharacterUI.Awake</c> wires the row of icons the report calls "die Leiste"
/// (NewPartyCharacterUI.cs:326-331): <c>classToggle</c> (:125, the PERSON icon) →
/// <c>OnClickAssign</c>, plus <c>cardsToggle</c> (:144), <c>perksToggle</c> (:156),
/// <c>itemsToggle</c> (:171), <c>assignToggle</c> (:100) and <c>battleGoalToggle</c> (:189). The
/// click on the CARD ITSELF is <c>OnClick()</c> (:802-862), and its assigned-character branch is
/// exactly two things in sequence:</para>
/// <list type="number">
/// <item><c>ToggleSelect(flag)</c> + <c>InvokeOnCharacterSelected(isSelected)</c> (:811-812) — THE
/// SELECTION. That is the half the user wants to keep, and it is untouched here.</item>
/// <item><c>if (autoOpenDefaultPanel &amp;&amp; AreButtonsInteractable &amp;&amp;
/// !InputManager.GamePadInUse)</c> (:817) → <c>classToggle.isOn = true</c> (:821), else
/// <c>battleGoalToggle.isOn = true</c> (:825), else <c>classToggle.group.SetAllTogglesOff()</c>
/// (:829) — THE AUTO-OPEN he is complaining about. Setting <c>classToggle.isOn</c> runs the same
/// listener the person icon runs: <c>OnClickAssign</c> (:685-698) →
/// <c>NewPartyDisplayUI.OnCharacterPickerSelected</c> (:1214-1237) → <c>CharacterSelector.Show</c>,
/// i.e. the party-assembly window with the character display in it. THAT is the
/// "Characterinfo-Sub-Menu".</item>
/// </list>
/// <para>With the flag false the game's own <c>else</c> at :834 runs <c>SetAllTogglesOff()</c>: the
/// selection happens, no panel opens. This is not a mod-invented path — it is the branch the game
/// already takes on a gamepad (the <c>!GamePadInUse</c> conjunct at :817) and in every guildmaster
/// selection mode, so it is exercised in shipped play.</para>
///
/// <para>WHY THIS IS A SCOPED SWAP AND NOT A WRITE ON THE FLAG
/// ([[dont-win-a-write-war]] — "concede the flag, own the number").
/// <c>autoOpenDefaultPanel</c> is private, defaults to <b>true</b> (:252) and has a public setter
/// <c>EnableAutoOpenDefaultPanel</c> (:864). It has THREE writers, and two of them would undo a
/// one-shot mod write:</para>
/// <list type="bullet">
/// <item>the field initialiser, <c>true</c>, on every slot;</item>
/// <item><c>NewPartyDisplayUI.EnableSelectionMode</c> (:1417) sets it false and
/// <c>DisableSelectionMode</c> (:1495) sets it back from
/// <c>!flagsInteraction.HasFlag(DISABLE_DISPLAY_ITEMS_ON_CHARACTER_SELECT)</c> — and by then the
/// flag has already been cleared out of <c>flagsInteraction</c> at :1489, so that call is
/// <c>true</c>;</item>
/// <item><b>THE MOD ITSELF.</b> <c>WorldUI.MapRoom.GuildmasterDestinations.ReArmCharacterScreen</c>
/// is LEVEL-TRIGGERED once per tick for as long as the map room stands and puts
/// <c>autoOpenDefaultPanel</c> back to <c>true</c> on every slot that has it off (ModBuild 195 —
/// the merchant leaves the party display in selection mode and the room never leaves the merchant).
/// A permanent <c>false</c> here would be re-raised ~72 times a second by our own code, and that
/// class prints <c>CHARACTER SCREEN RE-ARM STUCK</c> when it notices. That is the write war this
/// project has already paid for once.</item>
/// </list>
/// <para>So the flag is CONCEDED. This patch lowers it only for the duration of one
/// <c>OnClick</c> call and raises it again in the postfix, which means the outcome is true
/// regardless of who wrote the field when: the read at :817 happens inside the same synchronous
/// call, after every possible external write and before any next one. WHEN THE GAME CALLS THE
/// SETTER WITH <c>true</c> — which it does on every <c>DisableSelectionMode</c>, and which our own
/// re-arm does every tick — NOTHING HAPPENS: the field really is true between clicks, every other
/// reader sees the game's own value, and the next click lowers it again for a few microseconds.
/// There is no per-frame contest and no state for a peer, a mode transition or a reload to
/// desynchronise.</para>
///
/// <para>REJECTED ALTERNATIVES.
/// (a) A prefix on <c>EnableAutoOpenDefaultPanel</c> forcing <c>isEnabled = false</c>, plus an
/// <c>Awake</c> postfix to beat the initialiser. It works on the GAME's writers but not on ours:
/// <c>ReArmCharacterScreen</c> reads the field, sees false, calls the setter, reads false again next
/// tick and logs a stuck re-arm forever. It also makes the field permanently lie to anything that
/// asks it, including our own diagnostics.
/// (b) A transpiler on <c>OnClick</c> to delete the auto-open block: brittle against any game patch
/// and unnecessary — the game already has the branch we want.
/// (c) Re-implementing the selection in mod code and skipping <c>OnClick</c> entirely: it would
/// have to reproduce the FTUE guard, the <c>PlayerRegistry.IsSwitchingCharacter</c> guard, the
/// empty-slot assign path and <c>ToggleSelect</c>/<c>UpdateSprite</c> — see
/// [[share-machinery-not-look]].</para>
///
/// <para>THE CARD FAN — THE QUESTION THIS CHANGE TURNS ON, ANSWERED FROM SOURCE. With the flag
/// false the game runs <c>classToggle.group.SetAllTogglesOff()</c>, so a character click closes
/// every open tab including the cards tab. Does the map room's card fan go with it? NO.
/// <c>MapRoomHand.ResolveCharacter</c> (MapRoomHand.1.Core.cs) asks
/// <c>NewPartyDisplayUI.PartyDisplay.SelectedUISlot.Data</c> — and <c>SelectedUISlot</c> is
/// <c>selectedCharacter</c> (NewPartyDisplayUI.cs:226), written by
/// <c>SelectCurrentCharacter</c> (:862) out of <c>OnCharacterSelect</c> (:871-886), which is what
/// <c>InvokeOnCharacterSelected</c> at OnClick:812 invokes — BEFORE the auto-open block and with no
/// reference to any toggle. Neither <c>CardWindowSelected</c> (NewPartyCharacterUI.cs:270) nor
/// <c>cardsToggle</c> appears anywhere in <c>MapRoomHand.*</c>; the fan follows the SELECTED
/// CHARACTER alone and rebuilds off a selection/loadout signature poll. So the vanilla
/// <c>SetAllTogglesOff</c> path is exactly right and the fan keeps doing what the report asks:
/// clicking character B changes the hand cards to B's. NO tab-preserving re-open is shipped, and
/// MapRoomHand is not touched. (The alternative — re-asserting the previously open toggle on the
/// new character — was therefore not needed, and it would have re-opened the info sub-menu on a
/// character click whenever the info tab happened to be the open one, i.e. the very thing the
/// report asks to stop.)</para>
///
/// <para>HOW IT IS SCOPED, term by term, so the multiplayer assign-role flow is provably untouched.
/// The swap happens only when ALL of these hold:</para>
/// <list type="bullet">
/// <item><c>WorldUIConfig.ConversionActive</c> — VR UI presentation is on. With the mod off, or
/// before canvas conversion, the prefix restores nothing and the game is byte-identical.</item>
/// <item><c>__instance.Data != null</c> — an ASSIGNED character. The empty/available slot click is
/// a DIFFERENT branch of <c>OnClick</c> (:837-856, <c>assignToggle.isOn = true</c> → the recruit
/// picker) which does not read <c>autoOpenDefaultPanel</c> at all; gating on it anyway means this
/// class can never be the reason a free slot stops opening its picker.</item>
/// <item><c>!MapFTUEManager.IsPlaying</c> — the scripted first-run tutorial walks the player
/// through <c>SelectFirstSlot</c>/<c>CreateNewCharacter</c>/<c>SelectSecondSlot</c>
/// (EMapFTUEStep.cs) with its own guards inside <c>OnClick</c> (:804) and <c>OnClickAssign</c>
/// (:693). It gets vanilla behaviour; a behaviour preference is not worth a stuck tutorial.</item>
/// <item><c>!InputManager.GamePadInUse</c> — on a gamepad :817 is false by its own conjunct, so the
/// game never auto-opens there and there is nothing to suppress. (In VR the flag is held false by
/// <see cref="InputModeGuard"/> anyway.)</item>
/// <item>the field is currently <c>true</c> — if a guildmaster selection mode already set it false,
/// the game agrees with us and the swap is skipped, so nothing is restored either.</item>
/// </list>
/// <para>The multiplayer ASSIGN-ROLE flow does not go through this method: it is
/// <c>multiplayerAssignRoleButton.onClick → OnClickAssignRole</c> (Awake:345), a separate Button
/// firing <c>OnClickedAssignRole</c> → <c>NewPartyDisplayUI.OnMultiplayerAssignRoleSelected</c>.
/// <c>OnClick</c>'s own online guard <c>FFSNetwork.IsOnline &amp;&amp;
/// PlayerRegistry.IsSwitchingCharacter</c> (:804) returns before anything this patch could affect —
/// the prefix has lowered the flag by then, the original returns immediately, the postfix raises it
/// again, net zero. And for a character NOT under this client's control
/// <c>classToggle.interactable</c> is already false (:1148), so the auto-open could only ever have
/// reached the battle-goal toggle for a teammate; that toggle is still one click away in the same
/// row. EVERY panel the auto-open could have opened remains reachable from its own icon, whose
/// interactable state this patch does not touch.</para>
///
/// <para>MULTIPLAYER. Local presentation only. Nothing here writes rule-library or Bolt state and
/// nothing goes on the wire: the selection itself still runs the game's unmodified
/// <c>InvokeOnCharacterSelected</c> path, so the merchant/temple/enhancement
/// <c>onCharacterSelectedCallback</c> (NewPartyDisplayUI.cs:886) still re-points those windows at
/// the clicked character exactly as before. Only a local UI panel does not open.</para>
///
/// <para>KNOWN LOG CONSEQUENCE, stated here so it is never mistaken for a regression.
/// <c>GuildmasterDestinations.TickSheetOutcome</c> arms on <c>SelectedUISlot</c> changing to a slot
/// with a character and warns <c>CHARACTER SHEET OUTCOME … DID NOT OPEN</c> if the party-assembly
/// window is not open 30 ticks later. Its premise — "a slot click should open the sheet" — is
/// exactly what this change retires, so it will now warn on every character click in the map room.
/// That instrument lives in a file this lane does not own; the suppression line below names the
/// consequence in the log so the two can never be read apart, and the owning lane must re-key that
/// watcher onto the PERSON ICON (<c>classToggle</c>) instead of the slot click.</para>
///
/// <para>DEGRADES SAFELY, AND NEVER SILENTLY. The patch target is bound by <c>nameof</c>, so a
/// missing method is a compile error rather than a dead patch; <see cref="Prepare"/> prints one arm
/// line at registration and every single click prints its verdict, so "the patch never fired" is a
/// decidable question from one hardware log. If <c>OnClick</c> throws while the flag is lowered the
/// postfix does not run and the flag stays false — which is the state this patch wants anyway, and
/// which the map room's own level-triggered re-arm restores within a tick. Registered by
/// <c>WorldUIModule</c>.</para>
/// </summary>
[HarmonyPatch]
internal static class CharacterClickSelectsOnly
{
    private const string Scope = "WorldUI";

    /// <summary>Suppressions so far this session — clicks that selected a character and opened
    /// nothing.</summary>
    private static int _suppressed;

    /// <summary>Clicks that reached <c>OnClick</c> with the flag untouched, for any of the gate
    /// reasons in the class doc.</summary>
    private static int _passed;

    /// <summary>The long "what just changed and what it does to the log" line is printed once.</summary>
    private static bool _explained;

    /// <summary>One arm line per session, from Harmony's own <c>Prepare</c> hook — proof the class
    /// was reached at all. A no-op patch that never says so has cost this project whole rounds.</summary>
    private static bool _armed;

    private static bool Prepare()
    {
        if (!_armed)
        {
            _armed = true;
            VRLog.Info(Scope,
                "CHARACTER CLICK = SELECT ONLY: arming on NewPartyCharacterUI.OnClick (user request "
                + "2026-08-22 — a click on a character must only change the selected character, and "
                + "the character-info sub-menu must open only from the person icon in the row). "
                + "Every click on a party slot from here on prints either a SUPPRESSED or a PASSED "
                + "line, so if neither ever appears the patch is not reaching the click.");
        }
        return true;
    }

    /// <summary>
    /// Lower <c>autoOpenDefaultPanel</c> for the duration of this one call, so the read at
    /// NewPartyCharacterUI.cs:817 takes the game's own <c>SetAllTogglesOff</c> branch. See the class
    /// doc for why this is a scoped swap and not a write on the field.
    /// </summary>
    /// <param name="__state">true ⇒ we lowered the flag and the POSTFIX owes the raise.</param>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(NewPartyCharacterUI), nameof(NewPartyCharacterUI.OnClick))]
    private static void BeforeOnClick(NewPartyCharacterUI __instance, out bool __state)
    {
        __state = false;
        if (__instance == null)
            return;

        string? declined = Declined(__instance);
        if (declined != null)
        {
            _passed++;
            VRLog.Info(Scope,
                $"CHARACTER CLICK PASSED THROUGH ('{__instance.name}', slot {__instance.SlotIndex}): "
                + declined + $" Vanilla OnClick runs unchanged. ({_suppressed} suppressed / "
                + $"{_passed} passed this session.)");
            return;
        }

        __instance.EnableAutoOpenDefaultPanel(isEnabled: false);
        __state = true;
        _suppressed++;

        if (!_explained)
        {
            _explained = true;
            VRLog.Info(Scope,
                $"CHARACTER CLICK = SELECT ONLY, first suppression ('{__instance.name}', slot "
                + $"{__instance.SlotIndex}). autoOpenDefaultPanel is lowered for the duration of this "
                + "one OnClick call and raised again in the postfix, so the game's own else-branch "
                + "(classToggle.group.SetAllTogglesOff, NewPartyCharacterUI.cs:834) runs instead of "
                + "classToggle.isOn = true (:821). The character IS selected — InvokeOnCharacterSelected "
                + "at :812 runs first and is untouched — so the map room's card fan follows to this "
                + "character (MapRoomHand resolves NewPartyDisplayUI.SelectedUISlot.Data, never a "
                + "toggle). The character-info sub-menu now opens ONLY from the person icon. "
                + "EXPECT A KNOWN FALSE ALARM: GuildmasterDestinations' CHARACTER SHEET OUTCOME "
                + "watcher still assumes a slot click should open the sheet and will report DID NOT "
                + "OPEN after every character click. That is this change, by design — it is not a "
                + "broken character screen, and the watcher needs re-keying onto the person icon.");
        }
        else
        {
            VRLog.Info(Scope,
                $"CHARACTER CLICK SUPPRESSED THE AUTO-OPEN ('{__instance.name}', slot "
                + $"{__instance.SlotIndex}): selection only, no panel. ({_suppressed} suppressed / "
                + $"{_passed} passed this session. A CHARACTER SHEET OUTCOME 'DID NOT OPEN' warning "
                + "following this line is expected.)");
        }
    }

    /// <summary>Raise the flag again. Unconditional on <paramref name="__state"/>: we only ever
    /// lower a flag we observed HIGH at entry, and the decompile has no writer of it reachable from
    /// inside <c>OnClick</c> (<c>EnableSelectionMode</c>/<c>DisableSelectionMode</c> are mode
    /// transitions driven from the guildmaster HUD). Even if a write were lost, the map room's own
    /// level-triggered re-arm converges the field to true within one tick.</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(NewPartyCharacterUI), nameof(NewPartyCharacterUI.OnClick))]
    private static void AfterOnClick(NewPartyCharacterUI __instance, bool __state)
    {
        if (!__state || __instance == null)
            return;
        __instance.EnableAutoOpenDefaultPanel(isEnabled: true);
    }

    /// <summary>
    /// The gate, as one sentence naming the term that declined — or null to suppress. Written as a
    /// message rather than a bool so a hardware log never has to guess which conjunct was false.
    /// </summary>
    private static string? Declined(NewPartyCharacterUI slot)
    {
        if (!WorldUIConfig.ConversionActive)
            return "VR canvas conversion is not active (mod off, or before conversion), so this "
                   + "class is inert by construction.";

        if (slot.Data == null)
            return "the slot holds no character, so this is the empty-slot branch of OnClick "
                   + "(:837-856, assignToggle -> the recruit picker) which never reads "
                   + "autoOpenDefaultPanel.";

        if (MapFTUEManager.IsPlaying)
            return "the scripted map tutorial (MapFTUEManager) is playing and gets vanilla "
                   + "behaviour — its own steps drive this screen.";

        if (InputManager.GamePadInUse)
            return "a gamepad is in use, and OnClick:817 already requires !GamePadInUse, so the "
                   + "auto-open cannot fire anyway.";

        if (!slot.autoOpenDefaultPanel)
            return "autoOpenDefaultPanel is ALREADY false — a guildmaster selection mode "
                   + "(merchant/temple/trainer/enchantress) owns the party display and the game "
                   + "itself is suppressing the auto-open.";

        return null;
    }
}
