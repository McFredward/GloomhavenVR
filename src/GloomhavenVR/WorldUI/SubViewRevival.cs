using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// A HOST THAT IS DARK BUT HAS A LIVE SUB-VIEW IS NOT EMPTY — IT IS DRAWING ONE LEVEL DOWN.
///
/// <para>THE USER REPORT, 2026-08-23, verbatim: <i>"2) Sobald die private Quest für die Charactere
/// ausgewählt wird, bloppt das Character-UI Fenster wieder auf, zuvor hat es sich geschlossen. Ich
/// möchte das a) Das Fenster sich nicht schließt und Character-UI sichtbar bleibt bis die private
/// Quests vollständig ausgewählt wurde und b) war beim Test die eigentliche Auswahl der Quest wieder
/// ein seperates Fenster. Das soll nicht sein! Es soll in dem Character-UI Fenster angezeigt
/// werden."</i> The photograph is <c>.planning/debug/character_ui_quest_getrennt.jpg</c>: the
/// character column ("Die Löscher") stands as one floated window with a close cross, and the
/// battle-goal picker ("Erstschlag" / "Beutegier") stands beside it as a SECOND floated window with
/// a close cross of its own. Both belong inside one character-UI window.</para>
///
/// <para><b>THE THREE-LINE CHAIN IN THE ModBuild 232 LOG, AND WHAT EACH LINE PROVED.</b> Every line
/// is a mod rule doing exactly what it was told, which is why no single rule looks wrong on its own.
/// Player.log line numbers are from <c>.planning/debug/Player.log</c>:</para>
/// <code>
/// :3551  Adopted nested canvas 'UI Battle Goal Picker Window' in
///            'GloomhavenVR.Panel_Modal_New Party display' …
/// :4635  EMPTY WINDOW RELEASED: 'New Party display' (ID PartyPanel) — DARK — not one of 899
///            Graphic(s) under it passes the content fit's own visibility verdict … (dwell 2.0 s)
/// :5454  PARENT WINS STOOD DOWN: 'New Party display' (ID PartyPanel) is open and would have
///            suppressed the windows nested inside it, but the float loop is holding it out …
/// :5455  CATCH-ALL: unknown scenario window 'UI Battle Goal Picker Window' (ID None) floated
/// :5457  CATCH-ALL: unknown scenario window 'Party Display UI ' (ID None) floated
/// </code>
/// <para>:3551 is requirement (b) ALREADY BUILT AND ALREADY WORKING — in the ordinary map-room flow
/// the picker is a nested canvas of the party display and the conversion adopts it, so it renders
/// inside the host where the game lays it out. :4635 is the ModBuild 230 liveness rule releasing the
/// host because the game had genuinely blanked it. :5454 is the ModBuild 232 rule that stops a child
/// deferring to a host that will never float. All three are right. The RESULT is wrong, because 232
/// answered "the child must stay reachable" with "then let the child float alone" — and the two
/// windows in the photograph are precisely that: BOTH of them are catch-all floats of children of a
/// host sitting in the liveness hold. ModBuild 232 turned an invisible picker into a detached one.
/// </para>
///
/// <para><b>WHO DARKENED THE HOST, EXACTLY.</b> Not the loadout, and not the picker. The map travel
/// choreography did, two whole steps earlier, and the log's own <c>[GUI]</c> lines name the
/// requester:</para>
/// <code>
/// :4504  [GUI] Hide party panel animation requested by RootBehaviour (MapChoreographer) (False) (True)
///        … "Started moving to location: Quest_Campaign_027_Scenario0" … the tween is cancelled …
/// :4520  [GUI] Hide party panel animation requested by RootBehaviour (MapChoreographer) (False) (True)
/// :4635  EMPTY WINDOW RELEASED: 'New Party display' …                    ← 2.0 s dwell after the fade
/// :5202  [GUI] Hide party panel already closed, requested by UI Loadout Window (UILoadoutManager)
/// :5414  [GUI] Show party panel requested by UI Loadout Window (UILoadoutManager) …
/// </code>
/// <para><c>MapChoreographer.EnterLoadout</c> (MapChoreographer.cs:2199) and
/// <c>UILoadoutManager.EnterLoadout</c> (UILoadoutManager.cs:274) both hold a hide token;
/// <c>NewPartyDisplayUI.Hide</c> is refcounted by requester object (NewPartyDisplayUI.cs:684-721)
/// and its ANIMATED branch defers the real close to the animator's finished listener
/// (NewPartyDisplayUI.cs:299-305, <c>window.Hide()</c>). The panel is genuinely gone for the whole
/// map flight. It comes back only at <c>UILoadoutManager.EnableLoadoutInteraction</c>
/// (UILoadoutManager.cs:332, <c>PartyDisplay.Show(this)</c>), whose <c>OnShown</c> listener
/// <c>AutoselectCharacter</c> (:364-373) then calls <c>OpenBattleGoalPanel</c> — which is why the
/// picker opens in the same breath as the panel returns, and why the player experiences one event,
/// not two.</para>
///
/// <para><b>SO THE HOST IS NOT REVIVABLE AT :4635 AND MUST NOT BE.</b> Its content really was gone
/// and the ModBuild 230 user ruling ("Es darf niemals leere Fenster geben") is not negotiable. The
/// defect is entirely in what happens at :5414 when the content COMES BACK: the hold is read by
/// containment in the catch-all one phase before <c>EmptyHeldNow</c>'s 6 Hz probe could lift it, the
/// children are floated on their own, and the conversion then REPARENTS them out of the host's
/// subtree — after which the host can never be measured drawing again and the hold latches for the
/// rest of the room. <c>MODAL LIVENESS CENSUS: … 1 window(s) held out of the float set</c> in that
/// same log is the latch, printed once every 20 s for the remainder of the session.</para>
///
/// <para><b>THE RULE. A held-out host with a live sub-view is REVIVED, not bypassed.</b> When the
/// catch-all is about to stand "parent wins" down because the ancestor is in the liveness hold, it
/// first asks this class whether the CHILD it is standing down for is itself drawing right now. If
/// it is, the hold is lifted on the ancestor instead: the convert loop runs two phases later in the
/// same tick (<c>PhaseCatchAll</c> → <c>PhaseRelease</c> → <c>PhaseConvert</c>), the host floats,
/// and the child is adopted into it as a nested canvas — the :3551 path, reached from the state that
/// used to produce :5455.</para>
///
/// <para><b>THE BOUND, IN TWO PARTS, BECAUSE THIS PROJECT HAS BEEN BURNED BY BOTH FAILURE MODES.</b>
/// The loop to fear is: lift → host floats → host is dark → released after the 2 s dwell → child
/// still open → lift → … at roughly 3.5 s a cycle.</para>
/// <list type="number">
/// <item><b>A structural precondition.</b> The lift requires the child to be MEASURED DRAWING, with
/// <c>ModalFallback.DrawsAnythingLoose</c> — the same instrument <c>EmptyHeldNow</c> uses to decide
/// a hold may lift, not a second copy of it (this project has shipped a weaker copy of a visibility
/// test twice). In the healthy case the loop cannot even start: a host whose child draws is a host
/// the liveness rule arms by FIRST PAINT and never releases.</item>
/// <item><b>A budget as the net under it.</b> <see cref="MaxRevivalsPerWindow"/> lifts per host name
/// per <see cref="RevivalWindowSeconds"/>. THE SHAPE IS COPIED FROM THE CATCH-ALL CHURN FUSE
/// (<c>ChurnMaxFloats</c> = 3 in <c>ChurnWindowSeconds</c> = 60 s, ModalFallback.10.CatchAll.cs)
/// DELIBERATELY, and the number is LOWER for a reason that is not caution: that fuse counts floats a
/// PLAYER can cause by opening a window three times, and ModBuild 231 proved what happens when a
/// fuse mistakes a player for a loop. A revival is never a player gesture — it is one automatic
/// consequence of one game transition — so two is a real allowance (one for the loadout's first
/// character, one for a party display that closes and returns between characters) and a third in the
/// same minute is a loop by construction. THE FALLBACK ON EXHAUSTION IS TODAY'S SHIPPED BEHAVIOUR:
/// the catch-all stands down and the children float on their own, ugly but reachable. It is never
/// "show nothing" — that is [[parent-wins-needs-a-real-parent]] and it must not be re-created by the
/// fuse meant to protect against it. The window refills, so a second loadout minutes later is judged
/// afresh.</item>
/// </list>
///
/// <para><b>AND THE SEPARATE HALF: requirement (a).</b> <see cref="SelectionHoldsRelease"/> stops
/// the liveness rule releasing the character UI WHILE a battle-goal selection is live. Note honestly
/// what it does and does not do: at :4635 it would NOT have fired and could not have, because the
/// party display's own <c>UIWindow</c> was closed at that moment (<c>[GUI] Hide party panel already
/// closed</c> at :5202 is the proof) — the release there was correct. It covers the phase the user
/// actually named: from the moment the picker opens until the selection ends, a momentary blank in
/// the party column (a character swap between two goals runs
/// <c>UnselectCurrentCharacter</c>/<c>ui.CloseBattleGoal()</c>, NewPartyDisplayUI.cs:1175-1210) can
/// no longer expire the 2 s dwell and take the window down mid-choice.</para>
/// </summary>
internal static class SubViewRevival
{
    private const string Scope = "WorldUI";

    /// <summary>
    /// Hold lifts allowed per host window name per <see cref="RevivalWindowSeconds"/>. See the
    /// two-part bound on the class: this is the NET, not the mechanism, and the number is lower
    /// than the catch-all churn fuse's 3 because no player gesture produces a revival.
    /// </summary>
    private const int MaxRevivalsPerWindow = 2;

    /// <summary>The budget window, seconds — same 60 s the catch-all churn fuse uses, so the two
    /// fuses cannot disagree about what "recently" means for the same window.</summary>
    private const float RevivalWindowSeconds = 60f;

    private static readonly Dictionary<string, (int Count, float WindowStart)> Revivals = new();

    /// <summary>Per-host latch for the budget-spent warning — one line per host type, not per tick.
    /// </summary>
    private static readonly HashSet<string> BudgetSpentWarned = new();

    /// <summary>Per-host latch for the selection-hold line: it prints on the RISING edge only (the
    /// first tick the hold actually prevents something) and re-arms when the selection ends.
    /// </summary>
    private static readonly HashSet<string> SelectionHoldWarned = new();

    /// <summary>
    /// SHOULD THE LIVENESS HOLD ON <paramref name="host"/> BE LIFTED BECAUSE
    /// <paramref name="child"/> — a sub-view nested inside it that is about to be floated as a
    /// window of its own — is drawing right now?
    ///
    /// <para>Called from <c>ModalFallback.AncestorWillBeFloated</c> at the exact point where the
    /// ModBuild 232 stand-down would otherwise fire. Returning true means the CALLER must remove
    /// <paramref name="host"/> from the liveness hold and let "parent wins" stand: this method does
    /// the budget accounting and all the logging, it does not touch the hold itself (the hold set
    /// belongs to ModalFallback.9.Spawn.cs and there must be exactly one place that mutates it per
    /// reason).</para>
    /// </summary>
    internal static bool ShouldReviveHost(UIWindow host, UIWindow child)
    {
        if (host == null || child == null || ReferenceEquals(host, child))
            return false;
        // The host must still be a window the game considers open — reviving a float for a window
        // the game has closed would rebuild exactly the empty frame ModBuild 230 removed.
        if (!host.IsOpen)
            return false;
        // THE STRUCTURAL PRECONDITION. Judged on the CHILD, with the same instrument EmptyHeldNow
        // uses to lift a hold, because "the host is drawing one level down" is literally the claim
        // being made. A child that is open but blank is not a reason to rebuild the host.
        if (!child.IsOpen || !ModalFallback.DrawsAnythingLoose(child.transform))
            return false;

        string key = host.name;
        float now = Time.unscaledTime;
        if (!Revivals.TryGetValue(key, out (int Count, float WindowStart) budget)
            || now - budget.WindowStart > RevivalWindowSeconds)
        {
            budget = (0, now);
            // A refilled window re-arms the warning: the next exhaustion is a NEW event and must
            // print, or a session-long loop would be represented by one line an hour old.
            BudgetSpentWarned.Remove(key);
        }
        if (budget.Count >= MaxRevivalsPerWindow)
        {
            Revivals[key] = budget;
            if (BudgetSpentWarned.Add(key))
            {
                VRLog.Warn(Scope, $"SUB-VIEW REVIVAL BUDGET SPENT: '{host.name}' (ID {host.ID}) has "
                                  + $"already been revived {budget.Count}× in the last "
                                  + $"{RevivalWindowSeconds:0} s, so the hold on it is NOT lifted "
                                  + $"again for its live sub-view '{child.name}'. THAT IS A LOOP "
                                  + "VERDICT, NOT A PREFERENCE: a revival is never a player gesture, "
                                  + "so a third one inside a minute means the host is going dark "
                                  + "again as soon as it floats. FALLING BACK TO ModBuild 232 "
                                  + "BEHAVIOUR — the children float on their own, which is ugly but "
                                  + "reachable; nothing is hidden. If this line appears in a loadout "
                                  + "log, read the EMPTY WINDOW RELEASED lines for this same window "
                                  + "next: they say what the host was measured as while it stood.");
            }
            return false;
        }

        budget = (budget.Count + 1, budget.WindowStart);
        Revivals[key] = budget;
        VRLog.Warn(Scope, $"SUB-VIEW REVIVAL: the liveness hold on '{host.name}' (ID {host.ID}) is "
                          + $"LIFTED because its nested sub-view '{child.name}' (ID {child.ID}) is "
                          + "open and MEASURED DRAWING right now. The host was released for drawing "
                          + "nothing, but a host whose adopted child draws IS drawing — the content "
                          + "is one level down — so the correct answer is to rebuild the host and "
                          + "render the child inside it, NOT to float the child beside it (ModBuild "
                          + "232 did the second and the user photographed the result: "
                          + "character_ui_quest_getrennt.jpg). The convert loop runs two phases "
                          + "later in THIS tick, so the host floats without the player seeing a "
                          + "frame of either state. BUDGET: "
                          + $"{budget.Count}/{MaxRevivalsPerWindow} lift(s) used in this "
                          + $"{RevivalWindowSeconds:0} s window; on exhaustion the rule stands down "
                          + "to ModBuild 232 behaviour rather than hiding anything. THIS LINE IS THE "
                          + "ANSWER TO 'which rule lifted the hold and why' — if a PARENT WINS STOOD "
                          + "DOWN line for this same window appears after it, the lift did not hold.");
        return true;
    }

    /// <summary>
    /// IS A BATTLE-GOAL ("private quest") SELECTION LIVE INSIDE <paramref name="window"/>, so that
    /// the liveness rule must not release it however dark it momentarily measures? Requirement (a).
    ///
    /// <para>Four terms, and every one of them is the GAME's own state rather than a timer:</para>
    /// <list type="number">
    /// <item><c>NewPartyDisplayUI.PartyDisplay</c> exists (NewPartyDisplayUI.cs:246 —
    /// <c>Singleton&lt;APartyDisplayUI&gt;.Instance as NewPartyDisplayUI</c>).</item>
    /// <item>Its own <c>UIWindow</c> is open (<c>IsOpen</c>, NewPartyDisplayUI.cs:248). <b>THIS IS
    /// THE BOUND AND IT IS LOAD-BEARING.</b> Nothing in <c>NewPartyDisplayUI.Hide</c> calls
    /// <c>battleGoalSelector.Hide()</c> — only <c>HideCurrentDisplay</c> does
    /// (NewPartyDisplayUI.cs:1009-1011) — so after a hide that did not route through it, BOTH
    /// <c>ActiveDisplay</c> and the picker's <c>IsOpen</c> can stay latched at their selection
    /// values indefinitely. Without this term the hold would outlive the selection and keep an empty
    /// character UI standing forever, which is the ModBuild 230 defect wearing a different hat.
    /// Note what it also means for the ModBuild 232 trace: at Player.log:4635 this term was FALSE
    /// (the map flight's animated hide had closed the panel), so this rule would not have fired
    /// there and the release at :4635 stays correct.</item>
    /// <item>The party display itself says the battle-goal tab is the active one
    /// (<c>ActiveDisplay == DisplayType.BATTLE_GOALS</c>, NewPartyDisplayUI.cs:202/1182) — the same
    /// predicate the game polls in <c>UILoadoutManager.CanShowConfirmationButton</c>
    /// (UILoadoutManager.cs:111-119).</item>
    /// <item>The picker window is open (<c>BattleGoalWindow.IsOpen</c>, NewPartyDisplayUI.cs:238 →
    /// UIBattleGoalPickerWindow.cs:75 → <c>UIWindow.IsOpen</c>). <c>UIBattleGoalPickerWindow</c> is
    /// a plain MonoBehaviour holding a <c>UIWindow</c>, not a <c>UIWindow</c> subclass, and its
    /// serialized id is the enum default — there is no <c>BattleGoal*</c> member in
    /// <c>UIWindowID</c> at all, which is why the log calls it <c>ID None</c> and why it must be
    /// reached through the party display rather than <c>UIWindow.GetWindow</c>.</item>
    /// </list>
    ///
    /// <para>And one containment term, which is a question about WHICH window this is and not an
    /// identity claim: the picker must be a descendant of <paramref name="window"/>. That keeps the
    /// hold on the one float that actually renders the selection instead of on every window standing
    /// while a selection happens somewhere else.</para>
    /// </summary>
    internal static bool SelectionHoldsRelease(UIWindow? window, out string reason)
    {
        reason = string.Empty;
        if (window == null)
            return false;
        try
        {
            NewPartyDisplayUI? display = NewPartyDisplayUI.PartyDisplay;
            if (display == null || !display.IsOpen)
            {
                SelectionHoldWarned.Remove(window.name);
                return false;
            }
            if (display.ActiveDisplay != NewPartyDisplayUI.DisplayType.BATTLE_GOALS)
            {
                SelectionHoldWarned.Remove(window.name);
                return false;
            }
            UIBattleGoalPickerWindow? picker = display.BattleGoalWindow;
            if (picker == null || !picker.IsOpen)
            {
                SelectionHoldWarned.Remove(window.name);
                return false;
            }
            Transform pickerT = picker.transform;
            if (!pickerT.IsChildOf(window.transform))
            {
                SelectionHoldWarned.Remove(window.name);
                return false;
            }
            reason = "a battle-goal ('private quest') selection is LIVE inside it — "
                     + "NewPartyDisplayUI.PartyDisplay is open, its ActiveDisplay is BATTLE_GOALS "
                     + $"and its BattleGoalWindow ('{picker.name}') reports open";
            if (SelectionHoldWarned.Add(window.name))
            {
                VRLog.Warn(Scope, $"BATTLE GOAL SELECTION HOLD: '{window.name}' (ID {window.ID}) "
                                  + "measured DARK, but the liveness rule is NOT releasing it: "
                                  + $"{reason}. USER RULING (ModBuild 233): \"Das Fenster [soll] "
                                  + "sich nicht schließen und Character-UI sichtbar bleiben bis die "
                                  + "private Quests vollständig ausgewählt wurde.\" THE HOLD IS "
                                  + "BOUNDED BY THE GAME, NOT BY A TIMER: all three terms are read "
                                  + "live every check and the party display's own UIWindow.IsOpen is "
                                  + "one of them, so a genuinely closed panel is released normally. "
                                  + "This line prints on the RISING EDGE only and re-arms when the "
                                  + "selection ends — a second one for the same window means a "
                                  + "second selection, not a stuck rule.");
            }
            return true;
        }
        catch (System.Exception ex)
        {
            // A game-side accessor threw. Do NOT hold: an instrument that cannot answer must not
            // silently veto a rule that is backed by a user ruling of its own.
            VRLog.Warn(Scope, $"BATTLE GOAL SELECTION HOLD probe threw for '{window.name}' "
                              + $"({ex.GetType().Name}: {ex.Message}) — the liveness rule proceeds "
                              + "as if no selection were live, which is the ModBuild 230 default.");
            return false;
        }
    }

    /// <summary>Module teardown — mirrors <c>ModalFallback.CatchAllReset</c>, which calls this.
    /// The budget is per room standing: a new room is a new session of the rule.</summary>
    internal static void Reset()
    {
        Revivals.Clear();
        BudgetSpentWarned.Clear();
        SelectionHoldWarned.Clear();
    }
}
