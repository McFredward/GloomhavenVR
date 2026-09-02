using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine.UI;   // UIWindow / UIWindowID — the game ships them in this namespace

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>THE ONE PLACE THAT DECIDES "THIS WINDOW IS A MENU THE PLAYER OPENED".</b> Before ModBuild 341
/// that question was answered in seven different expressions across five files, every one of them
/// keyed on <c>UIWindow.ID</c> — and the mod's own settings window does not have one.
///
/// <para><b>THE DEFECT THIS CLASS EXISTS FOR (user, 2026-09-02, hardware round on ModBuild 340,
/// verbatim):</b> <i>"Wenn man die VR Optionen offen hat kommt die Kartenhand nicht. Bitte
/// vergwissere dich, dass kein Optionsmenu - auch nicht die VR Optionen den Spielfluss stört."</i>
/// The log (<c>.planning/debug/second_logs/LogOutput.log</c>) has the whole chain in five lines:</para>
/// <code>
/// WINDOW IDENTITY 'GloomhavenVR.OptionsTabWindow' (ID None): … nearest ancestor UIWindow &lt;none&gt;.
/// MODAL FALLBACK ASSERTED: windows=2, … blocking=True, … mode=CardSelection → ModalUI.
/// Modal commit-block ENGAGED — … Blocking window(s): 'GloomhavenVR.OptionsTabWindow' (ID None).
/// fan state: mode=CardsSelection, widgets=14, fanBuffer=8, gateEnabled=False, open=False (vrMode=ModalUI).
/// MODAL LIVENESS ARMED: 'GloomhavenVR.OptionsTabWindow' (ID None) after 178 ms …
/// </code>
/// <para><b>AND THE COMMIT-BLOCK IS ONLY HALF OF IT.</b> The obvious half is
/// <c>ModalFallback.IsBlockingWindow</c> → <c>BlockingWindowModalActive</c> → the Cards driver's
/// commit gate. The half that actually produces the reported symptom is one step further on:
/// <c>IsBlockingWindow</c> also feeds <c>wantLock</c> → <c>VRModeStateMachine.SetAuxModal</c> →
/// <c>VRMode.ModalUI</c>, and <c>ModalUI</c>'s row in <c>VRModeStateMachine.InteractorPolicy</c>
/// (VRModeStateMachine.cs:246) is <c>Poke | Ray | Grab</c> — <b>no <c>PalmGate</c></b>. So
/// <c>VRHand.SetInteractorPolicy</c> sets <c>PalmGate.Enabled = false</c>, and
/// <c>CardsDriver.UpdatePalmGate</c>'s <c>revealed</c> term is false for the rest of the session.
/// That is the log's <c>gateEnabled=False, open=False</c> and it is exactly "die Kartenhand kommt
/// nicht": not "the cards refuse a grab", but "the fan never opens at all". Blocking the commits
/// would have been survivable; losing the palm gate is not.</para>
///
/// <para><b>WHY THE MOD'S WINDOW HAS NO ID, AND WHY THAT STAYS.</b> ModBuild 337 detached the VR
/// settings pane from the game's Options window and deliberately left its <c>UIWindowID</c> at
/// <c>None</c>. Handing it a real game id is not a tidy-up, it is a shared key, and this repo has
/// already paid for that once: <c>UIWindow.GetWindow(id)</c>/<c>GetWindowsByID</c> would start
/// returning the mod's window for the game's id, <c>UIWindowManager</c>'s
/// <c>extraSkipHideWindows</c>/<c>extraSkipShowWindows</c> sets are keyed by id, and — decisively —
/// <c>ModalFallback.ResetEscMenuToggleGroup</c> (ModalFallback.7.Close.cs:188) fires for exactly
/// <c>Options</c>/<c>OptionsSubmenu</c>/<c>ViceOptionsSubmenu</c>/<c>CompendiumPanel</c> and runs
/// <c>ESCMenu.toggleGroup.SetAllTogglesOff()</c>, so closing the VR settings window with its own X
/// would take the game's Options window down with it. That is the very coupling ModBuild 336/337
/// removed on the user's ruling <i>"Ich will das es möglich ist das man die Optionen und die VR
/// Optionen öffnet parallel ohne Probleme"</i>. <b>So the classifier learns about the window; the
/// window does not learn the game's identity.</b></para>
///
/// <para><b>TWO QUESTIONS, NOT ONE, AND THEY ARE NAMED FOR THEMSELVES.</b> The old
/// <c>NonBlockingMenus</c> set was asked two different questions and answered both with the same
/// membership, which is how a per-site verdict silently becomes a policy:</para>
/// <list type="number">
/// <item><see cref="IsPlayerMenu"/> — <i>"is this a menu the player opened, which must therefore
/// never disturb play?"</i> This is the play-flow question and the mod's window IS one.</item>
/// <item><see cref="IsGameOwnedMenu"/> — <i>"is this one of the GAME's own menu windows, with the
/// window machinery that implies?"</i> Two rules ride on that machinery and neither is true of a
/// mod-owned window: the single-window <c>ToggleGroup</c> that hides one menu when a sibling opens
/// (so our float must be <c>Sticky</c> to survive it), and <c>MainOptionOptions</c> re-parenting the
/// options window at RUNTIME (so its hierarchy is not a stable placement fact and the "parent wins"
/// rule must not read it). The mod's window is in neither: ModBuild 337 took the cloned row's
/// <c>ExtendedToggle</c> out of that <c>ToggleGroup</c>, and its parent is fixed. Sticky would
/// also COST it — only <c>WindowPanel.UserClosing</c> ever drops a sticky float, so any path that
/// hides the window without <c>CloseFloatedWindow</c> would leave a frame standing with nothing in
/// it, against <i>"Es darf niemals leere Fenster geben"</i>. And the ancestor exemption would be
/// actively WRONG for it in the fire-exit mode, where <c>VROptionsTab</c> could not detach the pane
/// and it really is a sub-view of the game's Options window: exempting it there would float it as a
/// second window on top of the one it is drawn inside.</item>
/// </list>
///
/// <para><b>IDENTITY, NOT NAME-GUESSING.</b> The window is recognised by REGISTRATION —
/// <see cref="RegisterModMenu"/> is called by <c>VROptionsTab.Inject</c> the moment the clone
/// exists, and <c>VROptionsTab.Forget</c> calls <see cref="ForgetModMenu"/> on every teardown path
/// so the entry cannot outlive the object ([[gate-outliving-its-edge]]). The NAME test is kept as a
/// fallback only, because a missed registration must not cost the player his card hand a second
/// time, and because <c>ModalFallback.8.Convert.cs</c> has been matching that same literal since
/// ModBuild 339 for the content-fit exemption — one constant now, in one place, rather than two.</para>
///
/// <para><b>MULTIPLAYER.</b> Nothing here is networked and nothing here writes game state. A
/// <c>UIWindow</c> float exists only on the client that opened it; both peers run the identical
/// classification against their own local windows.</para>
///
/// <para>GREP: <c>MENU FAMILY</c> — one line per mod-owned menu window when it is first classified,
/// naming the verdict and what it turns off.</para>
/// </summary>
internal static class MenuWindowFamily
{
    /// <summary>
    /// The GameObject name <c>VROptionsTab.CloneWindow</c> stamps on the mod's standalone settings
    /// window. The fallback identity — see the class doc's "IDENTITY, NOT NAME-GUESSING".
    /// </summary>
    internal const string VROptionsWindowName = "GloomhavenVR.OptionsTabWindow";

    /// <summary>
    /// PLAYER-REACHABLE MENUS, by the game's own window id. Item 3b's original set, moved here
    /// verbatim: these must NOT assert <c>ModalUI</c>, so the user keeps FULL world interaction
    /// (board / cards / fan) while the pause menu — or a submenu opened from it (options /
    /// multiplayer / compendium) — is open. Every other window that reaches the modal path (story,
    /// level messages, dialog-confirms, results, durability) still blocks.
    /// </summary>
    private static readonly HashSet<UIWindowID> PlayerMenuIds = new()
    {
        UIWindowID.ESCMenu,
        UIWindowID.Options,
        UIWindowID.OptionsSubmenu,
        UIWindowID.ViceOptionsSubmenu,
        UIWindowID.CompendiumPanel,
        UIWindowID.MultiplayerFriendList,
        UIWindowID.HelpBox,
    };

    /// <summary>
    /// THE ESC / OPTIONS FAMILY — the narrower set the standing ruling <i>"es MUSS immer möglich
    /// sein das Optionsmenu zu öffnen"</i> protects. Used by the liveness rule's exemption
    /// (<c>ModalFallback.9.Spawn.cs</c>): a window in this family is never judged dark and never
    /// held out of the float set, because the pause menu IS the recovery path and there is no
    /// second one behind it. The menu-spawned CONFIRMATION boxes are deliberately not in it.
    /// </summary>
    private static readonly HashSet<UIWindowID> EscOptionsIds = new()
    {
        UIWindowID.ESCMenu,
        UIWindowID.Options,
        UIWindowID.OptionsSubmenu,
        UIWindowID.ViceOptionsSubmenu,
    };

    /// <summary>
    /// THE SETTINGS SURFACES — the windows whose widgets must never have a click swallowed
    /// (user ruling 2026-08-02: <i>"das Optionsmenue soll NIEMALS blockiert sein"</i>). The game's
    /// Options window and its tab sub-windows; the ESC menu is added by the one call site that
    /// wants it, because it is the OPENER rather than a settings surface.
    /// </summary>
    private static readonly HashSet<UIWindowID> SettingsIds = new()
    {
        UIWindowID.Options,
        UIWindowID.OptionsSubmenu,
    };

    /// <summary>
    /// The mod's own menu windows, held by REFERENCE. Never more than one entry today; a list
    /// rather than a field so a second mod-owned menu costs one <c>RegisterModMenu</c> call and no
    /// edit here. Compacted on every write so a destroyed window cannot linger.
    /// </summary>
    private static readonly List<UIWindow> ModOwned = new();

    /// <summary>Per-window latch for the MENU FAMILY verdict line — one line per window, not per
    /// frame (the classifier is asked for every floated window every tick).</summary>
    private static readonly HashSet<string> Announced = new();

    /// <summary>
    /// A mod-owned MENU window now exists — from here on every family test in
    /// <c>ModalFallback</c> answers for it exactly as it does for the game's own Options window.
    /// Idempotent; a null or already-registered window is ignored.
    /// </summary>
    internal static void RegisterModMenu(UIWindow? window)
    {
        if (window == null)
            return;
        Compact();
        for (int i = 0; i < ModOwned.Count; i++)
        {
            if (ReferenceEquals(ModOwned[i], window))
                return;
        }
        ModOwned.Add(window);
        // HW-VERIFY: this is the ModBuild 341 fix's own falsifier. If the next hardware log opens
        // the VR options in a card-selection phase and this line is ABSENT, the registration did
        // not run and the window is being classified by NAME alone; if it is absent AND a
        // "Modal commit-block ENGAGED … 'GloomhavenVR.OptionsTabWindow'" line follows, the name
        // fallback failed too and the classification is still wrong.
        VRLog.Note("WorldUI", $"MENU FAMILY: registered the mod-owned menu window '{window.name}' "
                              + $"(ID {window.ID}) — it is now treated exactly like the game's own "
                              + "Optionen/ESC menu by every ModalFallback family test: NON-blocking "
                              + "(no ModalUI lock, so the hand's PalmGate stays enabled and the card "
                              + "fan still opens), no card/tray commit gate, exempt from the "
                              + "liveness rule, and its widgets keep the settings click exemption. "
                              + "Its UIWindowID stays None on purpose (ModBuild 337) — a shared game "
                              + "id would re-couple it to the Optionen window's close arbitration.");
    }

    /// <summary>
    /// The mod-owned menu window is gone (teardown, scene change, degraded rebuild). Called from
    /// <c>VROptionsTab.Forget</c>, which every path — including <c>Shutdown</c>'s <c>finally</c> —
    /// runs through, so the registration can never outlive the object it names.
    /// </summary>
    internal static void ForgetModMenu(UIWindow? window)
    {
        for (int i = ModOwned.Count - 1; i >= 0; i--)
        {
            if (ModOwned[i] == null || (window != null && ReferenceEquals(ModOwned[i], window)))
                ModOwned.RemoveAt(i);
        }
        Announced.Clear();
    }

    /// <summary>Drop entries whose window Unity has destroyed under us.</summary>
    private static void Compact()
    {
        for (int i = ModOwned.Count - 1; i >= 0; i--)
        {
            if (ModOwned[i] == null)
                ModOwned.RemoveAt(i);
        }
    }

    /// <summary>
    /// Is this one of the mod's OWN menu windows? Registration first (an object identity),
    /// the stamped name second (the fallback that cannot go stale). Allocation-free.
    /// </summary>
    internal static bool IsModOwned(UIWindow? window)
    {
        if (window == null)
            return false;
        for (int i = 0; i < ModOwned.Count; i++)
        {
            if (ReferenceEquals(ModOwned[i], window))
                return true;
        }
        return string.Equals(window.name, VROptionsWindowName, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>THE PLAY-FLOW QUESTION: is this a menu the player opened, which must therefore never
    /// disturb play?</b> True for the game's pause/ESC menu, its Options window and submenus, the
    /// compendium, the friend list, the help box — AND for the mod's own VR settings window, which
    /// is the same thing wearing a <c>UIWindowID</c> of <c>None</c>.
    ///
    /// <para>Every gate that suppresses something because "a modal is open" reads THIS, so the
    /// three menus the user named — the game's Optionen, the pause/ESC menu, and the VR options —
    /// cannot drift apart again.</para>
    /// </summary>
    internal static bool IsPlayerMenu(UIWindow? window)
        => window != null && (PlayerMenuIds.Contains(window.ID) || IsModOwned(window));

    /// <summary>
    /// <b>THE GAME-BOOKKEEPING QUESTION: is this one of the GAME's own menu windows?</b> Two rules
    /// key on the machinery that comes with being one, and both are about the game rather than
    /// about play:
    /// <list type="bullet">
    /// <item>THE SINGLE-WINDOW <c>ToggleGroup</c> — opening one menu hides its sibling, so our float
    /// must be <c>Sticky</c> to survive it (the ModBuild 180 parallel-windows ruling).</item>
    /// <item>THE UNSTABLE PARENT — <c>MainOptionOptions</c> re-parents the options window at RUNTIME
    /// (MainOptionOptions.cs:20-25), so its hierarchy is not a placement fact and the "parent wins"
    /// rule (<c>CatchAllEligible</c> / <c>RendersInsideFloatedAncestor</c>) is exempted from reading
    /// it.</item>
    /// </list>
    ///
    /// <para><b>DELIBERATELY GAME-IDs ONLY, and this is a per-site verdict rather than an
    /// oversight.</b> ModBuild 337 took the mod's cloned menu row OUT of that <c>ToggleGroup</c>
    /// precisely so the two windows stop closing each other, so stickiness has nothing to defend it
    /// against — while it would COST: only <c>WindowPanel.UserClosing</c> ever drops a sticky float,
    /// so a path that hides the window without <c>ModalFallback.CloseFloatedWindow</c> (a teardown,
    /// a scene change) would leave the frame standing empty. <i>"Es darf niemals leere Fenster
    /// geben."</i> And the ancestor exemption would be actively WRONG for it: in
    /// <c>VROptionsTab</c>'s FIRE-EXIT mode the pane could not be detached and really is a sub-view
    /// of the game's Options window, so exempting it would float it as a second window standing on
    /// top of the one it is drawn inside — the "zwei Fenster statt einem" defect. In the normal
    /// standalone mode it has no ancestor <c>UIWindow</c> at all (hardware log: "nearest ancestor
    /// UIWindow &lt;none&gt;"), so the unexempted walk finds nothing and it floats on its own
    /// anyway. The rule reads the hierarchy and the hierarchy is right in both modes.</para>
    /// </summary>
    internal static bool IsGameOwnedMenu(UIWindow? window)
        => window != null && PlayerMenuIds.Contains(window.ID);

    /// <summary>
    /// The ESC/Options family the ruling <i>"es MUSS immer möglich sein das Optionsmenu zu
    /// öffnen"</i> protects from the empty-window liveness rule — the game's four ids AND the mod's
    /// own settings window, which the ModBuild 340 log shows being armed by that rule
    /// (<c>MODAL LIVENESS ARMED: 'GloomhavenVR.OptionsTabWindow'</c>).
    /// </summary>
    internal static bool IsEscOptionsFamily(UIWindow? window)
        => window != null && (EscOptionsIds.Contains(window.ID) || IsModOwned(window));

    /// <summary>
    /// A SETTINGS SURFACE — the game's Options window / its tab sub-windows, or the mod's own
    /// settings window. Consumed by the laser fall-through (<c>ModalFallback.IsSettingsSurface</c>)
    /// and by the click exemption that flips <c>InteractabilityManager</c>'s veto
    /// (<c>ModalFallback.IsUnderFloatedPauseOrSettingsWindow</c> →
    /// <c>Patches.SettingsClickExemption</c>). Without the mod's window in here, every row in the
    /// VR settings panel silently eats its click while a scripted tutorial holds an interaction
    /// profile — the 2026-08-02 defect, one window over.
    /// </summary>
    internal static bool IsSettingsWindow(UIWindow? window)
        => window != null && (SettingsIds.Contains(window.ID) || IsModOwned(window));

    /// <summary>
    /// One line per mod-owned menu window the first time <c>ModalFallback</c> actually floats it,
    /// so a hardware log proves the classification RAN rather than merely that the code exists.
    /// Latched by window name; the latch is cleared with the registration.
    /// </summary>
    internal static void AnnounceFloat(UIWindow? window)
    {
        if (window == null || !IsModOwned(window) || !Announced.Add(window.name))
            return;
        // HW-VERIFY: the outcome line for the ModBuild 341 fix. If this appears and a
        // "Modal commit-block ENGAGED" naming the same window appears anyway, the classifier ran
        // and some OTHER predicate is still keying on the id — start from the enumeration in
        // .planning/MENU-NEVER-DISTURBS-PLAY.md.
        VRLog.Note("WorldUI", $"MENU FAMILY: '{window.name}' (ID {window.ID}) floated and is "
                              + "classified as a PLAYER MENU, not a blocking game modal — no "
                              + "ModalUI lock, no card/tray commit gate, no liveness judgement. The "
                              + "card fan's PalmGate therefore stays enabled while this window is "
                              + "open, which is the whole of the 2026-09-02 report.");
    }
}
