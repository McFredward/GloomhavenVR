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
/// <item><b>A structural precondition.</b> The lift requires the HOST'S OWN SUBTREE to be MEASURED
/// DRAWING, with <c>ModalFallback.DrawsAnythingLoose</c> — the same instrument on the same transform
/// that <c>EmptyHeldNow</c> uses to decide a hold may lift, not a second copy of it (this project has
/// shipped a weaker copy of a visibility test twice). ModBuild 233 asked the CHILD instead and lost a
/// race by one window; the reasoning is at the call site. In the healthy case the loop cannot even
/// start: a host whose content draws is a host the liveness rule arms by FIRST PAINT and never
/// releases.</item>
/// <item><b>A budget as the net under it, and since ModBuild 234 it is DERIVED rather than chosen.</b>
/// <see cref="MaxRevivalsPerWindow"/> lifts per host name per <see cref="RevivalWindowSeconds"/>,
/// computed as <c>ModalFallback.ChurnMaxFloats - 1</c> over that fuse's own window. The full
/// arithmetic — one churn count per revive/withdraw cycle on the SUB-VIEW's name, plus the one
/// fallback float once the budget is spent, capped at <c>ChurnMaxFloats</c> or the sub-view is
/// session-suppressed by name and shown NOWHERE — is on the constant. It lands on the 2 that already
/// shipped, so nothing moves; what changes is that the number can no longer be falsified by an edit
/// to the fuse it depends on. THE FALLBACK ON EXHAUSTION IS TODAY'S SHIPPED BEHAVIOUR: the catch-all
/// stands down and the children float on their own, ugly but reachable. It is never "show nothing" —
/// that is [[parent-wins-needs-a-real-parent]] and it must not be re-created by the fuse meant to
/// protect against it. The window refills, so a second loadout minutes later is judged afresh.</item>
/// <item><b>And a retraction, because prevention cannot cover every ordering.</b> If a descendant was
/// floated on an EARLIER tick, the <c>oursAlready</c> re-add at the top of <c>TickCatchAll</c>
/// short-circuits its evaluation from then on and no later revival would reconsider it. So the lift
/// also gives back what has already been taken: <c>ModalFallback.RetractSubViewFloats</c>, which
/// reuses the ModBuild 232 <c>Sticky</c> discipline and writes nothing to the game. In a healthy
/// loadout run it finds nothing, and <c>SUB-VIEW FLOAT RETRACTED</c> never prints.</item>
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
    /// Hold lifts allowed per host window name per <see cref="RevivalWindowSeconds"/>.
    ///
    /// <para><b>ModBuild 234 — THIS IS NOW COMPUTED, AND THE ARITHMETIC IS THE ARGUMENT.</b> It used
    /// to be a literal 2 justified by a preference ("a revival is never a player gesture, so a lower
    /// number than the churn fuse's 3 is a real allowance"). That reasoning was not wrong, but it was
    /// not a derivation and a later edit of <c>ChurnMaxFloats</c> would have silently invalidated it.
    /// Adding the withdrawal (<c>ModalFallback.RetractSubViewFloats</c>) turns it into an inequality
    /// that has exactly one answer:</para>
    /// <list type="number">
    /// <item>The catch-all churn fuse counts ONE tick per <c>OpenWindows.Add</c> of a window the mod
    /// is not already floating, per window NAME, and SESSION-SUPPRESSES that name on the count after
    /// <c>ChurnMaxFloats</c> (ModalFallback.10.CatchAll.cs, <c>churn.Count &gt; ChurnMaxFloats</c>).
    /// A suppressed name is shown NOWHERE — that is the ModBuild 231 defect verbatim ("CATCH-ALL
    /// FUSE: window 'UI Loadout Window' re-floated 4x in 60s — suppressed for this session", and the
    /// player had nothing to click).</item>
    /// <item>One revive/withdraw cycle costs the SUB-VIEW exactly one such count: the count it spent
    /// when it floated. The retraction adds none of its own, and from the tick the host is floated
    /// the sub-view is refused by "parent wins" before the counter is reached at all.</item>
    /// <item>So the worst case for one sub-view name inside one window is
    /// <c>MaxRevivalsPerWindow</c> withdrawn floats plus the ONE fallback float it makes once the
    /// budget is spent and the rule stands down to ModBuild 232 behaviour. That total must stay at
    /// or under <c>ChurnMaxFloats</c>, so <c>MaxRevivalsPerWindow ≤ ChurnMaxFloats - 1</c>, and the
    /// budget is worth nothing below its maximum.</item>
    /// </list>
    /// <para>It lands on 2, which is what shipped — so this is not a behaviour change, it is the
    /// number ceasing to be a choice. It is the same inequality <c>StoryComposite.MaxWithdrawCycles</c>
    /// derives from the same fuse ("1 + cycles ≤ 3"), reached from the other side. THE HOST pays the
    /// same worst case on its own name whenever it is ID-less and reaches the float set through the
    /// catch-all (<c>MaxRevivalsPerWindow</c> floats plus one), so one bound covers both.</para>
    ///
    /// <para>THE FALLBACK ON EXHAUSTION IS UNCHANGED AND IS NEVER "SHOW NOTHING": the catch-all
    /// stands down and the children float on their own, ugly but reachable
    /// ([[parent-wins-needs-a-real-parent]]).</para>
    /// </summary>
    internal const int MaxRevivalsPerWindow = ModalFallback.ChurnMaxFloats - 1;

    /// <summary>The budget window, seconds — literally the catch-all churn fuse's own window rather
    /// than a copy of its value, so the two fuses cannot drift apart about what "recently" means for
    /// the same window. The derivation above only holds if they measure the same interval.</summary>
    private const float RevivalWindowSeconds = ModalFallback.ChurnWindowSeconds;

    private static readonly Dictionary<string, (int Count, float WindowStart)> Revivals = new();

    /// <summary>Per-host latch for the budget-spent warning — one line per host type, not per tick.
    /// </summary>
    private static readonly HashSet<string> BudgetSpentWarned = new();

    /// <summary>Per-host latch for the selection-hold line: it prints on the RISING edge only (the
    /// first tick the hold actually prevents something) and re-arms when the selection ends.
    /// </summary>
    private static readonly HashSet<string> SelectionHoldWarned = new();

    /// <summary>
    /// SHOULD THE LIVENESS HOLD ON <paramref name="host"/> BE LIFTED BECAUSE THE HOST IS DRAWING
    /// ONE LEVEL DOWN, asked while <paramref name="child"/> — a sub-view nested inside it — is about
    /// to be floated as a window of its own?
    ///
    /// <para>ModBuild 234: <paramref name="child"/> ADDRESSES the question (it is the window whose
    /// float this decides and the one named in the log line); it is no longer the SUBJECT of the
    /// drawing measurement. See the precondition in the body for the hardware trace that forced the
    /// change and why the two are not interchangeable.</para>
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
        // The child is the window ASKING; a closed one is not asking for anything.
        if (!child.IsOpen)
            return false;
        // THE STRUCTURAL PRECONDITION — AND SINCE ModBuild 234 IT IS ASKED ABOUT THE HOST'S OWN
        // SUBTREE, NOT ABOUT THE ONE CHILD THAT HAPPENED TO BE ITERATED FIRST.
        //
        // 233 judged the CHILD (`DrawsAnythingLoose(child.transform)`) and that lost a race by one
        // window on hardware. Player.log, one pass, in order:
        //     :15867  PARENT WINS STOOD DOWN: 'New Party display' … the float loop is holding it out
        //     :15868  CATCH-ALL: unknown scenario window 'UI Battle Goal Picker Window' … floated
        //     :15870  SUB-VIEW REVIVAL … because its nested sub-view 'Party Display UI ' … is open
        //                 and MEASURED DRAWING right now
        // The catch-all evaluates 'UI Battle Goal Picker Window' first (it is the first of the two
        // in the tracker: its Show transition precedes the party display's at :15840 vs :15846).
        // Everything else in this method passed for it — the host was open (the WINDOW IDENTITY line
        // at :15869 says `open=True`) and the budget was untouched (the line that DID fire one
        // window later reports `BUDGET: 1/2`, so the picker's call never reached the counter). By
        // elimination the term that failed was this one, on the picker's own subtree: a UIWindow's
        // `IsOpen` flips to true at the START of its show transition (m_CurrentVisualState is
        // assigned before StartAlphaTween, decompiled UIWindow.cs:546-549/317) while its CanvasGroup
        // alpha climbs from 0, and this instrument requires an effective alpha of 0.05. A window can
        // therefore be OPEN and not yet DRAWING for several frames, and the catch-all grace is
        // CatchAllGraceTicks = 2 — far shorter than a fade.
        //
        // JUDGING THE HOST FIXES IT AND IS STRICTLY THE SAME QUESTION. The claim being made is "this
        // host is not empty, it is drawing one level down"; that is a statement about the host's
        // subtree, and asking one arbitrary child is a strictly WEAKER test of it. It is also the
        // instrument's natural argument: this exact call, on this exact transform, is what
        // EmptyHeldNow makes to LIFT the hold at its own 6 Hz probe slot (ModalFallback.9.Spawn.cs).
        // So the revival is now precisely a same-tick fast path for a lift the liveness rule would
        // grant within 167 ms anyway — never a lift it would refuse — and it cannot be a second,
        // weaker copy of a visibility test, which this project has shipped twice.
        //
        // IT IS ALSO PROVABLY ENOUGH FOR THE REPORTED TRACE: DrawsAnythingLoose is a subtree walk
        // with a mod-owned-name filter, so a drawing DESCENDANT implies a drawing HOST. The :15870
        // line establishes that 'Party Display UI ' was drawing on that very tick, therefore
        // 'New Party display' was too, therefore this term would have been TRUE during the picker's
        // evaluation one window earlier and the picker would never have been floated at all.
        if (!ModalFallback.DrawsAnythingLoose(host.transform))
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
                          + "LIFTED because its OWN SUBTREE is MEASURED DRAWING right now, asked "
                          + $"while its nested sub-view '{child.name}' (ID {child.ID}) was about to "
                          + "be floated as a window of its own. The host was released for drawing "
                          + "nothing, but a host whose adopted child draws IS drawing — the content "
                          + "is one level down — so the correct answer is to rebuild the host and "
                          + "render the child inside it, NOT to float the child beside it (ModBuild "
                          + "232 did the second and the user photographed the result: "
                          + "character_ui_quest_getrennt.jpg). NOTE WHAT IS MEASURED AND WHAT IS NOT "
                          + "(ModBuild 234): the drawing test is on the HOST, not on the named child, "
                          + "because 233 asked the child and lost the race by one window — the child "
                          + "that asks first may still be fading in (IsOpen flips at the START of the "
                          + "show transition) while a sibling already draws. The child is named here "
                          + "as the ASKING window, not as the evidence. This is the same call "
                          + "EmptyHeldNow makes to lift the hold at its 6 Hz probe, so this is a "
                          + "same-tick fast path for a lift that was coming anyway, never one it "
                          + "would have refused. The convert loop runs two phases "
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
