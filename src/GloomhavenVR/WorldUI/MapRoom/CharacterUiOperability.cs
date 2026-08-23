using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using FFSNet;
using GloomhavenVR.Core;
using MapRuleLibrary.Party;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE CHARACTER UI STAYS FULLY OPERABLE WHILE A FOREIGN WINDOW IS OPEN.
///
/// <para><b>USER REPORT, 2026-08-23, verbatim:</b> <i>"1&#41; Während das Fenster der Magierin offen
/// ist kann ich die Character-UI nicht mehr bedienen außer charactere umzuswitchen. Ich will, dass es
/// voll bedienbar ist egal welche Fenster offen sind. Der einzige Einfluss der die character-UI auf
/// die anderen Fenster haben sollen ist die auswahl des aktuellen characters."</i> — while the
/// enchantress' window is open the character UI answers nothing except switching characters. It must
/// be fully operable no matter which windows are open, and the ONLY influence the character UI may
/// have on the other windows is which character is currently selected.</para>
///
/// <para>This is a general ruling and not a one-window fix. On the flat screen the guildmaster is a
/// single-mode machine: you are in the shop OR at the character screen, never both, so a mode that
/// greys out the party display's icon row is correct there. In the map room both panels are physical
/// surfaces the player can reach with his hands, standing side by side by explicit ruling
/// (<i>"Anders als in Flat soll es hier möglich sein mehrere Fenster parallel offen zu haben"</i>),
/// so the same mode is a VR defect.</para>
///
/// <para><b>THE MECHANISM, NAMED FROM SOURCE.</b> <c>UINewEnhancementWindow.EnterShop</c> calls
/// <c>NewPartyDisplayUI.PartyDisplay.EnableEnhancementMode(OnSelectedCharacter)</c>
/// (UINewEnhancementWindow.cs:207) BEFORE <c>shopWindow.Show()</c>, and that is
/// <c>EnableSelectionMode(cb, disableButtons: <b>true</b>, disableButtonsIgnoreCards: true)</c>
/// (NewPartyDisplayUI.cs:1389-1392). It is not one term but four, and only the last one is what the
/// report is about:</para>
/// <list type="number">
///   <item><description><c>DisableMapOptions()</c> (:1409 → :1501) — <c>EnableResetLevel(false)</c>,
///   <c>DisableSelection()</c> on AVAILABLE slots, <c>CloseWindows()</c>. Recruiting into an empty
///   slot and the undo-a-level-up window. Left exactly as the game set it, see below.</description></item>
///   <item><description><c>flagsInteraction |= DISABLE_TOGGLE_OFF_CHARACTER |
///   DISABLE_DISPLAY_ITEMS_ON_CHARACTER_SELECT</c>, and with <c>disableButtons</c> also
///   <c>DISABLE_BUTTONS</c> (:1410-1414). The third of those is dead bookkeeping — it is written at
///   :1413, cleared at :1490 and <b>read nowhere in the decompile</b>; the first is what stops a
///   second click deselecting the character, and it is PROTECTIVE here, see below.</description></item>
///   <item><description>per slot, <c>EnableAutoOpenDefaultPanel(false)</c> and
///   <c>EnableSelectCharacter(false)</c> (:1417-1418). The second sets
///   <c>disabledCharacterSelection</c>, and <c>DetermineInteractability</c> then holds
///   <c>classToggle.interactable</c> and <c>assignToggle.interactable</c> at false for as long as it
///   stands (NewPartyCharacterUI.cs:1147-1148). <b>Both were already re-armed</b> by
///   <c>GuildmasterDestinations.ReArmCharacterScreen</c> in ModBuild 195, because the merchant and
///   the temple set exactly these two and nothing else.</description></item>
///   <item><description><b>AND THE ONE NOBODY HAD:</b> <c>it.DisableButtons(ignoreCards)</c> (:1421),
///   which is <c>buttonsCanvasGroup.interactable = false</c> AND
///   <c>buttonsCanvasGroup.blocksRaycasts = false</c> plus a grey-out of the five icon materials
///   (NewPartyCharacterUI.cs:990-999). That is the whole icon row — person, cards, perks, items,
///   battle goal, assign — dead as a control AND transparent to the pointer.</description></item>
/// </list>
///
/// <para><b>WHY THE SYMPTOM READS EXACTLY AS HE DESCRIBED IT.</b> The portrait itself is a different
/// object behind a different gate: <c>button</c> is gated by <c>slotInteraction</c>, which this path
/// never touches for an ASSIGNED slot. So the portrait keeps taking clicks and keeps running
/// <c>InvokeOnCharacterSelected</c> — <i>"außer charactere umzuswitchen"</i> — while every icon in
/// the row above it is behind a CanvasGroup that is neither interactable nor raycastable. One term,
/// two fields, and it accounts for the whole report.</para>
///
/// <para><b>THE OTHER FOUR DESTINATIONS DO NOT SHARE IT — MEASURED, NOT ASSUMED.</b>
/// <c>UIShopItemWindow.EnterShop</c> passes <c>disableButtons: <b>false</b></c>
/// (UIShopItemWindow.cs:79) and <c>UITempleWindow.EnterTemple</c> passes
/// <c>disableButtons: <b>false</b></c> (UITempleWindow.cs:111); with that argument the per-slot body
/// runs <c>DisableButtons</c> only for slots whose <c>State != Assigned</c> (:1419-1422), so the
/// merchant and the temple leave the icon row of every real character alone and only set the two
/// flags of item 3 above. <c>UITrainerWindow</c> and <c>UITownRecordsWindow</c> never mention
/// <c>NewPartyDisplayUI</c> at all. The enchantress is the only caller in the entire decompile that
/// passes <c>disableButtons: true</c>. So the family is: five destinations, three touch the party
/// display, two of those were already fixed, and this file is the third.</para>
///
/// <para><b>THE SEAM: THE GAME'S OWN INVERSE SETTER, LEVEL-TRIGGERED, AND NOTHING ELSE.</b>
/// <see cref="ReArmButtonRow"/> calls <c>NewPartyCharacterUI.EnableButtons(ignoreCardButton: true)</c>
/// — the public method the game itself uses to undo <c>DisableButtons</c>, restricted to the exact
/// argument that makes it the bit-for-bit inverse of the enchantress' call. No field of the mod's own
/// is written into the game, no window is shown or hidden, no Escape is sent, and
/// <c>DisableSelectionMode</c> is NOT called — that would tear down the whole mode and take the
/// selection callback with it, which is the one thing the report asks to keep.</para>
///
/// <para><b>WHAT HAPPENS IF THE GAME RE-ASSERTS IT.</b> Nothing, and this is provable rather than
/// hopeful ([[dont-win-a-write-war]]). <c>buttonsCanvasGroup.interactable</c> has exactly two writers
/// in the whole decompile — <c>DisableButtons</c> (NewPartyCharacterUI.cs:992) and
/// <c>EnableButtons</c> (:1009) — and exactly three callers of the first:
/// <c>EnableSelectionMode</c> (a one-shot mode transition), <c>NewPartyDisplayUI.DisableSlots</c>
/// (:1835, the solo-quest lock) and <c>HideAssignedCharacter</c> (NewPartyCharacterUI.cs:1399). None
/// of them runs per frame, so the re-arm writes once per mode entry and then reads two bools per slot
/// forever. If that ever stops being true the class says so out loud at
/// <see cref="ReArmWarTicks"/> consecutive ticks instead of re-arming harder.</para>
///
/// <para><b>AND THE OTHER TWO CALLERS ARE NOT COLLATERAL.</b> Both of them pair the CanvasGroup write
/// with <c>DisableSelection()</c>, i.e. <c>button.interactable = false</c> — a field the selection
/// mode never touches on an assigned slot — and <c>HideAssignedCharacter</c> also raises
/// <c>IsHidden</c>. The re-arm therefore refuses any slot that is not <c>Assigned</c>, any slot that
/// is hidden, and any slot whose portrait button is disabled. That triple is an EXACT discriminator
/// for "this row was closed by a guildmaster selection mode", not a heuristic: it is the only one of
/// the three call sites that leaves the portrait live.</para>
///
/// <para><b>WHAT IS DELIBERATELY LEFT ALONE, AND WHY EACH ONE IS A KEEP AND NOT AN OVERSIGHT.</b></para>
/// <list type="bullet">
///   <item><description><c>DISABLE_TOGGLE_OFF_CHARACTER</c> stays set. It is what stops a second
///   click on the selected portrait deselecting him, and deselecting him drives
///   <c>onCharacterSelectedCallback?.Invoke(null)</c> (NewPartyDisplayUI.cs:1293) into
///   <c>UINewEnhancementWindow.OnSelectedCharacter</c>, whose first statement is
///   <c>character = characterUI.Service</c> (:220) with no null test. Clearing this flag would hand
///   the player a way to throw inside the game's own callback. It is a guard, not a
///   restriction.</description></item>
///   <item><description>EMPTY and AVAILABLE slots keep their lock — <c>DisableMapOptions</c>'s
///   <c>DisableSelection()</c> and the <c>State != Assigned</c> branch of <c>DisableButtons</c>.
///   Recruiting a NEW mercenary in the middle of a purchase is the one edit the vanilla lock is
///   actually protecting, and the report does not ask for it. Same ruling, same reason, as
///   <c>GuildmasterDestinations.ReArmCharacterScreen</c>'s.</description></item>
///   <item><description><c>DISABLE_RESET_LEVELUP</c> stays set, so the undo-a-level-up window still
///   does not pop while a destination is open. It is set by <c>DisableMapOptions</c>, which the
///   game ALSO runs on every ordinary <c>OnLeaveMap</c> (UIGuildmasterHUD.cs:526) — it is not part
///   of the selection mode and undoing it here would be a change nobody asked
///   for.</description></item>
/// </list>
///
/// <para><b>HOW THE SELECTION SURVIVES — THE ONE INFLUENCE HE WANTS KEPT.</b>
/// <c>onCharacterSelectedCallback</c> is installed by <c>EnableSelectionMode</c> (:1425-1434) and
/// removed only by <c>DisableSelectionMode</c> (:1497). This class never calls either and never
/// touches the field, so the enchantress' <c>OnSelectedCharacter</c> → <c>cardsDisplay.Display(…)</c>
/// (UINewEnhancementWindow.cs:219-228) keeps firing on every portrait click exactly as it does today.
/// The falsifier measures the callback's presence rather than trusting that sentence: see
/// <c>selectionCallback</c> in the verdict line, which is read off the display's own field.</para>
///
/// <para><b>MULTIPLAYER.</b> Nothing here touches the wire and nothing lets one player drive another's
/// UI. The state this restores is the state the party display has whenever no destination window is
/// open, i.e. most of a session, and the per-control online guards are ANDed INSIDE
/// <c>DetermineInteractability</c> — <c>classToggle.interactable = !disabledCharacterSelection &amp;&amp;
/// (!IsOnline || characterData.IsUnderMyControl)</c> (:1148),
/// <c>battleGoalToggle.interactable = !IsOnline || characterData.IsUnderMyControl</c> (:1159) — so a
/// client that re-gains its icon row still cannot open or reassign a character it does not control.
/// <c>buttonsCanvasGroup</c> is upstream of all of them and can only ever un-hide a control the
/// game's own guard has already allowed.</para>
///
/// <para><b>THE FALSIFIER.</b> <see cref="Judge"/> prints one line,
/// <c>CHARACTER UI OPERABLE: CONFIRMED</c> or <c>NOT ACHIEVED</c>, and it is measured off the objects
/// and not off the intent that wrote them ([[measure-the-picture-not-the-state]]): for every control
/// family on every assigned slot it reads the Selectable's own <c>IsInteractable()</c>, walks the
/// CanvasGroup chain for the first group that stops raycasts, and counts the listeners actually bound
/// to the control's UnityEvent through <c>m_Calls.m_RuntimeCalls</c> — a serialized-only
/// <c>GetPersistentEventCount</c> would read zero for controls the game wires in <c>Awake</c>. A
/// NOT ACHIEVED names the failing family and the term that killed it. Change-gated and capped at
/// <see cref="MaxVerdictReports"/> lines per session.</para>
///
/// <para><b>WHERE THIS BELONGS EVENTUALLY.</b> The two selection-flag terms of item 3 live in
/// <c>GuildmasterDestinations.ReArmCharacterScreen</c> and the third one lives here; they are the
/// same family and a later round should merge them behind one reconciler. They are kept apart today
/// because they are re-armed under DIFFERENT discriminators — the flags are safe to re-arm on any
/// assigned slot, the CanvasGroup is not (see the solo-quest paragraph above) — and folding a
/// narrower guard into a wider loop is how a lock gets lost. This class is ticked from
/// <c>EnchantressComposite.Tick</c>, which <c>ModalFallback.TickCatchAll</c> calls above every early
/// return in it, so the re-arm runs on ticks in which no window is tracked and the catch-all is
/// standing down — a mode outlives the window that set it, which is the whole defect
/// ([[gated-remedy-never-ran]]).</para>
/// </summary>
internal static class CharacterUiOperability
{
    private const string Scope = "WorldUI";

    /// <summary>Ticks between two verdicts. The re-arm runs every tick because it is two bool reads
    /// per slot; the verdict walks a CanvasGroup chain per control and is therefore paced. Thirty
    /// ticks is well under a second at rig tick rate, so no state a player can produce with his hands
    /// can slip between two of them.</summary>
    private const int VerdictCadenceTicks = 30;

    /// <summary>Cap on verdict lines per session. <c>EnchantressComposite.MaxOneWindowReports</c>'s
    /// reason with a little more headroom, because this verdict can legitimately change once per
    /// destination window opened: a state that oscillates should say so a handful of times and then
    /// stop rather than fill the log.</summary>
    private const int MaxVerdictReports = 8;

    /// <summary>Consecutive re-arming ticks after which a write war is declared rather than fought.
    /// Three, for <c>GuildmasterDestinations.ReArmWarTicks</c>'s reason: a one-shot mode transition is
    /// a single tick, a per-frame writer is unmistakable by the third.</summary>
    private const int ReArmWarTicks = 3;

    private static int _verdictTicks;
    private static int _reports;
    private static string _lastVerdict = string.Empty;
    private static int _reArmFightTicks;
    private static bool _reArmProbeWarned;
    private static bool _judgeProbeWarned;

    /// <summary>Reflection into <c>UnityEventBase</c>'s runtime call list, resolved once. See the
    /// falsifier paragraph in the class doc for why the public count is not enough.</summary>
    private static bool _eventReflectionResolved;
    private static FieldInfo? _fCalls;
    private static FieldInfo? _fRuntimeCalls;

    /// <summary>The party display's mode word, resolved once. <c>flagsInteraction</c> is a private
    /// field of a private nested enum type, so it is read reflectively and printed as a string —
    /// it is quoted in the verdict because "which mode is the display in" is half the question the
    /// falsifier is asked.</summary>
    private static bool _flagsReflectionResolved;
    private static FieldInfo? _fFlags;
    private static FieldInfo? _fSelectionCallback;

    /// <summary>One tick. Called from <c>EnchantressComposite.Tick</c>, above its own preconditions,
    /// so the re-arm does not depend on the enchantress window still being open.</summary>
    internal static void Tick()
    {
        if (!MapRoomDriver.Active)
        {
            _reArmFightTicks = 0;
            _verdictTicks = 0;
            _lastVerdict = string.Empty;
            return;
        }

        ReArmButtonRow();

        if (++_verdictTicks < VerdictCadenceTicks)
            return;
        _verdictTicks = 0;
        Judge();
    }

    /// <summary>Session reset — called from <c>EnchantressComposite.Reset</c>. Clears latches only:
    /// there is nothing parked and nothing borrowed, so there is nothing to hand back.</summary>
    internal static void Reset()
    {
        _verdictTicks = 0;
        _reports = 0;
        _lastVerdict = string.Empty;
        _reArmFightTicks = 0;
        _reArmProbeWarned = false;
        _judgeProbeWarned = false;
    }

    // ------------------------------------------------------------------------------ the re-arm --

    /// <summary>
    /// PUT THE ICON ROW BACK ON THE ASSIGNED SLOTS. Level-triggered and idempotent: in the steady
    /// state this reads two bools per slot and writes nothing. See the class doc for the exact
    /// discriminator and for what happens if the game re-asserts.
    /// </summary>
    private static void ReArmButtonRow()
    {
        try
        {
            NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
            List<NewPartyCharacterUI>? slots = display != null ? display.CharacterSlots : null;
            if (slots == null || slots.Count == 0)
            {
                _reArmFightTicks = 0;
                return;
            }

            int rearmed = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                NewPartyCharacterUI? slot = slots[i];
                if (slot == null || slot.State != PartySlotState.Assigned || slot.IsHidden)
                    continue;

                CanvasGroup? row = slot.buttonsCanvasGroup;
                if (row == null || (row.interactable && row.blocksRaycasts))
                    continue;

                // THE DISCRIMINATOR. DisableSlots and HideAssignedCharacter both pair the CanvasGroup
                // write with DisableSelection, i.e. button.interactable = false. EnableSelectionMode
                // is the only one of the three that leaves the portrait live, and it is the only one
                // this class is allowed to undo.
                ExtendedButton? portrait = slot.button;
                if (portrait == null || !portrait.interactable)
                    continue;

                slot.EnableButtons(ignoreCardButton: true);
                rearmed++;
            }

            if (rearmed == 0)
            {
                _reArmFightTicks = 0;
                return;
            }

            if (_reArmFightTicks == 0)
            {
                VRLog.Info(Scope, $"CHARACTER UI BUTTON ROW RE-ARMED on {rearmed} assigned slot's icon row: a "
                                  + "guildmaster destination had called EnableSelectionMode with "
                                  + "disableButtons=true, which is buttonsCanvasGroup.interactable=false AND "
                                  + "blocksRaycasts=false on every slot — the whole icon row dead and "
                                  + "transparent to the pointer while the portrait underneath kept taking "
                                  + "clicks. The enchantress is the only caller in the decompile that passes "
                                  + "that argument; the merchant and the temple pass false. Undone with the "
                                  + "game's own EnableButtons, with the selection mode and its character "
                                  + "callback left standing. READ IT AS: this line at most once per "
                                  + "enchantress window opened. If it repeats, see the STUCK line.");
            }

            if (++_reArmFightTicks == ReArmWarTicks)
            {
                VRLog.Warn(Scope, $"CHARACTER UI BUTTON ROW RE-ARM STUCK: the icon row has been re-armed "
                                  + $"{_reArmFightTicks} ticks running. buttonsCanvasGroup has exactly two "
                                  + "writers in the decompile and all three of their callers are one-shot "
                                  + "transitions, so this can only mean a mode is being re-entered every "
                                  + "frame. That is a write war and neither side wins it: expect the icon row "
                                  + "to flicker rather than settle. The remedy is to find and stop that "
                                  + "per-frame entry, NOT to re-arm harder.");
            }
        }
        catch (System.Exception ex)
        {
            _reArmFightTicks = 0;
            if (!_reArmProbeWarned)
            {
                _reArmProbeWarned = true;
                VRLog.Warn(Scope, $"CHARACTER UI BUTTON ROW RE-ARM threw once and is now silent for this "
                                  + $"session — {ex.GetType().Name}: {ex.Message}. If the icon row on the "
                                  + "character panel is grey and dead while the enchantress window is open, "
                                  + "this is the reason the mod did not undo it.");
            }
        }
    }

    // ----------------------------------------------------------------------------- the verdict --

    /// <summary>
    /// THE FALSIFIER. One line, measured off the objects. Runs only while a guildmaster destination
    /// window is actually open — with no foreign window there is no claim to falsify, and the latch
    /// is cleared so the next window re-states the verdict.
    /// </summary>
    private static void Judge()
    {
        try
        {
            string foreignName = string.Empty;
            EGuildmasterMode foreignMode = EGuildmasterMode.None;
            FindOpenDestination(ref foreignMode, ref foreignName);
            if (foreignMode == EGuildmasterMode.None)
            {
                _lastVerdict = string.Empty;
                return;
            }

            NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
            List<NewPartyCharacterUI>? slots = display != null ? display.CharacterSlots : null;
            if (display == null || slots == null || slots.Count == 0)
                return;

            ResolveDisplayReflection();
            string mode = ReadFlags(display);
            string callback = ReadSelectionCallback(display);

            List<string> failures = new();
            System.Text.StringBuilder census = new();
            int judged = 0;
            int censusedOnly = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                NewPartyCharacterUI? slot = slots[i];
                if (slot == null || slot.State != PartySlotState.Assigned || slot.IsHidden)
                    continue;

                // WHOSE SLOT IS THIS. Online, the game gates classToggle, assignToggle and
                // battleGoalToggle on IsUnderMyControl / AssignedSlots inside DetermineInteractability
                // itself — a slot another player is driving is SUPPOSED to be dead here, and counting
                // it would make the verdict permanently red in multiplayer for a rule this mod never
                // touched. Such a slot is censused and excluded, and the line says how many.
                CMapCharacter? data = slot.Data;
                bool mine = !FFSNetwork.IsOnline || (data != null && data.IsUnderMyControl);
                if (mine)
                    judged++;
                else
                    censusedOnly++;

                if (census.Length > 0)
                    census.Append(" ");
                census.Append(SlotLabel(slot)).Append(mine ? ": " : " NOT MINE, censused only: ");
                census.Append(DescribeControl("portrait", slot.button, slot, mine, failures)).Append(", ");
                census.Append(DescribeControl("person", slot.classToggle, slot, mine, failures)).Append(", ");
                census.Append(DescribeControl("cards", slot.cardsToggle, slot, mine, failures)).Append(", ");
                census.Append(DescribeControl("perks", slot.perksToggle, slot, mine, failures)).Append(", ");
                census.Append(DescribeControl("items", slot.itemsToggle, slot, mine, failures)).Append(", ");
                census.Append(DescribeControl("goal", slot.battleGoalToggle, slot, mine, failures)).Append(", ");
                // THE TWO SLOT-ASSIGNMENT CONTROLS ARE NEVER COUNTED. Both ride
                // NewPartyDisplayUI.EnableAssignPlayer, which is a REQUEST-COUNTED lock keyed by the
                // requesting component and taken identically by the merchant, the temple AND the
                // enchantress. It is not part of the term this class undoes, it is host-only role
                // assignment rather than a character-UI control, and writing into a refcount another
                // window is holding is how this project has broken things before. Censused so a
                // hardware log can still say what they were doing.
                census.Append(DescribeControl("assign", slot.assignToggle, slot, false, failures)).Append(", ");
                census.Append(DescribeControl("mpAssign", slot.multiplayerAssignRoleButton, slot, false, failures)).Append(".");
            }

            if (judged == 0)
            {
                _lastVerdict = string.Empty;
                return;
            }

            string headline = failures.Count == 0
                ? "CHARACTER UI OPERABLE: CONFIRMED"
                : "CHARACTER UI OPERABLE: NOT ACHIEVED";
            string blame = failures.Count == 0
                ? "every control family on every assigned slot is interactable, raycastable and bound"
                : "DEAD: " + string.Join("; ", failures.ToArray());

            string verdict = $"{headline} — foreign window '{foreignName}' is open in mode {foreignMode}; "
                             + $"the party display's mode reads {mode}; selection callback {callback}; "
                             + $"{judged} assigned slot's controls judged and {censusedOnly} censused "
                             + $"only. {blame}. CENSUS {census}"
                             + " READING KEY: interactable is the Selectable's own IsInteractable, which "
                             + "already folds in every CanvasGroup above it; raycast is raycastTarget ANDed "
                             + "with the first CanvasGroup in the chain that stops raycasts; handlers is "
                             + "persistent PLUS runtime listeners read off m_Calls.m_RuntimeCalls, so a "
                             + "control the game wires in Awake reads non-zero. A family whose object is "
                             + "inactive is reported absent and excluded from the verdict — the "
                             + "multiplayer assign button is inactive in single player by design. The "
                             + "two slot-assignment families and any slot another player controls are "
                             + "censused but never counted, because the game gates those itself and "
                             + "this mod does not touch that gate.";

            // CHANGE-GATED ON THE VERDICT, NOT ON THE CENSUS. The census carries listener counts and
            // toggle states that move whenever the player opens a panel, so keying the latch on the
            // whole line would burn the session's budget on lines that say the same thing. The key is
            // the headline plus the failure list — the two halves that decide the report.
            string key = headline + "|" + foreignMode + "|" + blame;
            if (key == _lastVerdict || _reports >= MaxVerdictReports)
                return;
            _lastVerdict = key;
            _reports++;
            if (failures.Count == 0)
                VRLog.Info(Scope, verdict);
            else
                VRLog.Warn(Scope, verdict);
        }
        catch (System.Exception ex)
        {
            if (!_judgeProbeWarned)
            {
                _judgeProbeWarned = true;
                VRLog.Warn(Scope, $"CHARACTER UI OPERABLE probe threw once and is now silent for this session "
                                  + $"— {ex.GetType().Name}: {ex.Message}. The re-arm above it is unaffected; "
                                  + "what is lost is the measurement, so treat the feature as UNJUDGED rather "
                                  + "than working.");
            }
        }
    }

    /// <summary>The first guildmaster destination window that is open, off the HUD's own serialized
    /// references through <c>GuildmasterDestinations.ModeWindow</c> — never by name and never by a
    /// scene sweep. <c>MercenaryLog</c> is skipped because it resolves to the same window as
    /// <c>TownRecords</c>.</summary>
    private static void FindOpenDestination(ref EGuildmasterMode mode, ref string name)
    {
        EGuildmasterMode[] candidates =
        {
            EGuildmasterMode.Enchantress,
            EGuildmasterMode.Merchant,
            EGuildmasterMode.Temple,
            EGuildmasterMode.Trainer,
            EGuildmasterMode.TownRecords,
        };
        for (int i = 0; i < candidates.Length; i++)
        {
            UIWindow? window = GuildmasterDestinations.ModeWindow(candidates[i]);
            if (window == null || !window.IsOpen)
                continue;
            mode = candidates[i];
            name = window.name;
            return;
        }
    }

    private static string SlotLabel(NewPartyCharacterUI slot)
    {
        CMapCharacter? data = slot.Data;
        string who = data != null ? data.CharacterID : "<no character>";
        return $"slot {slot.SlotIndex} '{who}'";
    }

    /// <summary>One control family, measured. Appends to <paramref name="failures"/> when the family
    /// is present and dead, naming the term that killed it.</summary>
    private static string DescribeControl(string family, Selectable? control, NewPartyCharacterUI slot,
                                          bool counts, List<string> failures)
    {
        if (control == null)
            return family + "<missing>";
        if (!control.gameObject.activeInHierarchy)
            return family + "<absent>";

        bool canPress;
        string press = InteractableTerm(control, out canPress);
        bool canHit;
        string hit = RaycastTerm(control, out canHit);
        int handlers = HandlerCount(control);

        if (!counts)
        {
            // censused, not judged — see the two call-site notes in Judge
        }
        else if (!canPress)
            failures.Add($"{SlotLabel(slot)} {family} not interactable — {press}");
        else if (!canHit)
            failures.Add($"{SlotLabel(slot)} {family} takes no raycast — {hit}");
        else if (handlers == 0)
            failures.Add($"{SlotLabel(slot)} {family} has no click handler bound");

        return $"{family}<interactable={press}, raycast={hit}, handlers={HandlerText(handlers)}>";
    }

    private static string HandlerText(int handlers) =>
        handlers < 0 ? "unreadable" : handlers.ToString();

    /// <summary>Is the control interactable, and if not, WHICH term says so — its own flag or the
    /// first CanvasGroup above it that refuses. <c>Selectable.IsInteractable</c> already folds the
    /// group chain in, so the walk exists only to name the term.</summary>
    private static string InteractableTerm(Selectable control, out bool ok)
    {
        ok = control.IsInteractable();
        if (ok)
            return "yes";
        if (!control.interactable)
            return "its own interactable flag is false";
        Transform? t = control.transform;
        while (t != null)
        {
            CanvasGroup group = t.GetComponent<CanvasGroup>();
            if (group != null)
            {
                if (!group.interactable)
                    return $"CanvasGroup '{group.name}' has interactable=false";
                if (group.ignoreParentGroups)
                    break;
            }
            t = t.parent;
        }
        return "no, and no CanvasGroup in the chain owns up to it";
    }

    /// <summary>Does a pointer ray actually reach the control — its graphic's <c>raycastTarget</c>
    /// ANDed with every CanvasGroup above it, stopping at the first <c>ignoreParentGroups</c>. This is
    /// the clause that would catch a term this class has NOT diagnosed, e.g. a
    /// <c>ControllerInputArea</c> dropping <c>blocksRaycasts</c> on an unfocused area
    /// (ControllerInputArea.cs:276-279).</summary>
    private static string RaycastTerm(Selectable control, out bool ok)
    {
        ok = false;
        Graphic? graphic = control.targetGraphic;
        if (graphic == null)
            return "no target graphic to hit";
        if (!graphic.raycastTarget)
            return $"'{graphic.name}' has raycastTarget=false";
        Transform? t = graphic.transform;
        while (t != null)
        {
            CanvasGroup group = t.GetComponent<CanvasGroup>();
            if (group != null)
            {
                if (!group.blocksRaycasts)
                    return $"CanvasGroup '{group.name}' has blocksRaycasts=false";
                if (group.ignoreParentGroups)
                    break;
            }
            t = t.parent;
        }
        ok = true;
        return "live";
    }

    /// <summary>Listeners actually bound to the control's click event — persistent plus runtime.
    /// Negative means the reflection did not resolve, which is stated rather than counted as
    /// zero.</summary>
    private static int HandlerCount(Selectable control)
    {
        UnityEventBase? ev = control switch
        {
            Toggle toggle => toggle.onValueChanged,
            Button button => button.onClick,
            _ => null,
        };
        if (ev == null)
            return -1;

        int persistent = ev.GetPersistentEventCount();
        int runtime = RuntimeCallCount(ev);
        return runtime < 0 ? persistent : persistent + runtime;
    }

    private static int RuntimeCallCount(UnityEventBase ev)
    {
        if (!_eventReflectionResolved)
        {
            _eventReflectionResolved = true;
            _fCalls = typeof(UnityEventBase).GetField(
                "m_Calls", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            System.Type? callList = _fCalls != null ? _fCalls.FieldType : null;
            _fRuntimeCalls = callList != null
                ? callList.GetField("m_RuntimeCalls",
                                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                : null;
        }
        if (_fCalls == null || _fRuntimeCalls == null)
            return -1;
        object? calls = _fCalls.GetValue(ev);
        if (calls == null)
            return -1;
        return _fRuntimeCalls.GetValue(calls) is IList list ? list.Count : -1;
    }

    private static void ResolveDisplayReflection()
    {
        if (_flagsReflectionResolved)
            return;
        _flagsReflectionResolved = true;
        _fFlags = typeof(NewPartyDisplayUI).GetField(
            "flagsInteraction", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        _fSelectionCallback = typeof(NewPartyDisplayUI).GetField(
            "onCharacterSelectedCallback",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    }

    /// <summary>The party display's mode word. The field is a private nested flags enum, so it is
    /// read reflectively and printed by name.</summary>
    private static string ReadFlags(NewPartyDisplayUI display)
    {
        if (_fFlags == null)
            return "unreadable";
        object? value = _fFlags.GetValue(display);
        return value != null ? value.ToString() : "null";
    }

    /// <summary>THE ONE INFLUENCE THE REPORT ASKS TO KEEP, measured rather than asserted: is the
    /// selection mode's own character callback still installed on the display. If this ever reads
    /// 'not installed' while a destination window is open, picking a character has stopped steering
    /// that window and the re-arm has overreached.</summary>
    private static string ReadSelectionCallback(NewPartyDisplayUI display)
    {
        if (_fSelectionCallback == null)
            return "unreadable";
        object? value = _fSelectionCallback.GetValue(display);
        if (value is not System.Delegate del)
            return "NOT installed — a character pick no longer steers the foreign window";
        return $"installed with {del.GetInvocationList().Length} target's worth of listeners";
    }
}
