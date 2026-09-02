using FFSNet;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE SECOND CLAIMANT ON THE GAME'S BARE READY TOGGLE: the mod's own PHYSICAL BOARD KEYCAP.
///
/// <para><b>USER REPORT (ModBuild 348 multiplayer hardware test), verbatim, item 1:</b> <i>"'Auswahl
/// beenden' wird aktuell als Fenster gespawnt zusätzlich zum physischen Knopf auf dem Board. Das darf
/// nicht sein. Siehe auswahl-beenden.jpg."</i> The screenshot is a gold-framed bar reading AUSWAHL
/// BEENDEN with a close cross on it, hanging in the cellar beside the floated ESC menu and the
/// multiplayer submenu.</para>
///
/// <para><b>WHAT THAT WINDOW IS, AND IT IS NOT A NEW OBJECT.</b> It is the SAME singleton
/// <c>UIReadyToggle</c> that <see cref="FloatRefusalTable"/> ROW 2 was written for — a 300x60 px
/// <c>UIWindow</c> carrying <c>[RequireComponent(typeof(Toggle), typeof(UIWindow))]</c> whose only
/// content is one button. The game re-labels that one singleton per flow, and during ONLINE card
/// selection it labels it "Auswahl beenden":
/// <c>UIScenarioMultiplayerController.InitializeReadyToggleForCardSelection()</c>
/// (decompiled/GH.Runtime/UIScenarioMultiplayerController.cs:260-297) runs
/// <c>if (FFSNetwork.IsOnline) Singleton&lt;UIReadyToggle&gt;.Instance.Initialize(show: true, …,
/// readyTextLoc: "GUI_END_SELECTION", unreadyTextLoc: "GUI_CANCEL", …)</c>, and
/// <c>CardsHandManager.OnWindowShown()</c> (CardsHandManager.cs:303-305) additionally writes the
/// gamepad label from <c>Consoles/GUI_HOTKEY_END_SELECTION</c>. OFFLINE the same act is
/// <c>Choreographer.readyButton</c>, which is not a <c>UIWindow</c> at all — which is exactly why
/// this only ever showed up in a large MULTIPLAYER test.</para>
///
/// <para><b>WHY IT FLOATED ANYWAY, IN THE LOG'S OWN WORDS.</b> ModBuild 348,
/// .planning/debug/LogOutput.log:23901 —
/// <c>CATCH-ALL: unknown scenario window 'Multiplayer Ready Toggle' (ID None) floated — enroll it
/// explicitly.</c> — and the same log contains ZERO <c>FLOAT REFUSED</c> lines. ROW 2 is CONDITIONAL:
/// it refuses only while <c>MapRoom.ReadyToggleParkClaim</c> says somebody is drawing the button
/// somewhere better, and both parkers of that claim (<c>MapRoom.MapTravelConfirm</c> in the map room,
/// <c>WorldUI.LoadoutConfirmPark</c> on the pre-scenario loadout screen) are stood down inside a
/// scenario. So the row matched, its claim was false, and <c>Refuses</c> returned false.</para>
///
/// <para><b>AND THE ANSWER IS NOT A SECOND ROW.</b> <see cref="FloatRefusalTable.Refuses"/> walks the
/// table and, on the FIRST row whose component matches, <c>return false</c>s when that row's claim is
/// not held — it does not <c>continue</c> to a later row. A second <c>UIReadyToggle</c> row would
/// therefore be unreachable code. The right shape is a second CLAIMANT on the existing row, which is
/// what this file is, and it is the same shape ModBuild 235 already used when the loadout screen
/// became a second parker beside the map room.</para>
///
/// <para><b>THE CLAIM IS "THE ACT IS ON THE BOARD", NOT "THIS WINDOW IS UGLY".</b> The mod presents
/// end-selection during a scenario as the control board's CONFIRM keycap
/// (<c>Cards.PlayTray.BoardButton</c>, built at Cards/Tray/PlayTray.6.Build.cs:426-441). That keycap
/// is driven by the very predicate the game uses to decide whether the toggle may be clicked —
/// <c>CardsGameApi.ReadyToggleAvailable()</c>, read at Cards/Tray/PlayTray.5.Status.cs:236-246 — and
/// it wears the game's own wording: the ModBuild 348 log's <c>BUTTON ALIGN</c> line (:961) reads
/// <c>CONFIRM 'Auswahl beenden' and item-USE 'Benutzen' are written to this one pose</c>. So the
/// physical button the user is pointing at IS this window's content, already in the room, in the
/// place he tuned for it.</para>
///
/// <para><b>THIS IS A PARK'S READER-SIDE FACT AND IT IS DELIBERATELY NOT A PARK.</b> Nothing here
/// re-parents, hides, disables or writes anything at all — not to the toggle, not to its canvas, not
/// to the game. The board keycap is an INDEPENDENT control that fires the game's own
/// <c>UIReadyToggle.ReadyUp</c> path (<c>CardsGameApi.TrayConfirmReady</c>); this file only reports
/// that it is there. The consequence matters: if this claim is wrong, the worst case is a window that
/// is not floated while a working button sits on the board — and the claim is re-evaluated every tick
/// so it cannot outlive its edge.</para>
///
/// <para><b>THE DEADLOCK FLOOR, AND WHY IT IS A COUNT AND NOT A LEVEL.</b> "A window must never be
/// unreachable" outranks "a bare control must not float" — ROW 2's own doc says so. So the claim
/// stands down whenever the game says the act IS possible right now
/// (<c>CardsGameApi.ReadyToggleAvailable()</c>) and the board is offering NO confirm control
/// (<c>PlayTray.ConfirmControlShown</c>), and the toggle floats again within a tick. That test is a
/// COUNT of consecutive ticks rather than a bare level for one reason: the two terms are written by
/// two different Update pumps whose relative order is not locked (scripts/check-frame-order.py locks
/// the order INSIDE ModalFallback.Tick, not between it and the Cards driver), so a single tick in
/// which the game has just armed the toggle and the tray has not yet drawn its keycap is an ordering
/// artefact and not a deadlock. One float per card-selection round would burn the catch-all's own
/// churn fuse (<c>ChurnMaxFloats</c> = 3 per 60 s) and take the safety valve away for the rest of the
/// session, which is the opposite of what the floor is for.</para>
///
/// <para><b>MULTIPLAYER.</b> Nothing here goes on the wire and nothing here is game state. It records
/// which local GameObject a local uGUI subtree is drawn under on THIS client; two players may
/// legitimately disagree about every term in it, and the owner's dial is never ANDed with a viewer's.
/// The claim is only ever true ONLINE, because offline the game never shows this window for this
/// act.</para>
/// </summary>
internal static class BoardConfirmStandIn
{
    private const string Scope = "WorldUI";

    /// <summary>
    /// How many consecutive ticks "the act is possible AND the board offers nothing" must hold before
    /// the claim stands down. At the mod's ordinary per-frame tick this is a fifth of a second — long
    /// enough that no pump-ordering artefact can reach it, short enough that a player who is actually
    /// stuck waits less time than it takes him to look for the button.
    /// </summary>
    private const int SilentTicksBeforeStandDown = 18;

    private static int _silentTicks;
    private static bool _stoodDown;
    private static bool _standDownLogged;
    private static string _why = "never evaluated — the mod has not ticked in an online card selection yet";

    /// <summary>The claimant's own words, for the refusal table's <c>FLOAT REFUSED</c> line and for
    /// its lapse warning. Never empty.</summary>
    internal static string Why => _why;

    /// <summary>
    /// ONE TICK. Called from <c>ModalFallback.TickCatchAll</c> ABOVE its early returns, for the same
    /// reason <c>EnchantressComposite.Tick</c> sits there: a stand-down must be able to happen on a
    /// tick in which no unknown window is tracked at all, or the remedy is gated behind the very
    /// condition it exists to end ([[gated-remedy-never-ran]]).
    /// </summary>
    internal static void Tick()
    {
        if (!OnlineCardSelection())
        {
            Release("the phase is not an ONLINE card selection, so the board's CONFIRM keycap is not "
                    + "standing in for anything");
            return;
        }

        PlayTray? tray = PlayTray.Current;
        if (tray == null || !tray.IsVisible)
        {
            Release("the mod's control board is not in the room right now (no tray, or hidden), so "
                    + "there is no physical keycap to stand in for this window");
            return;
        }

        bool offered = tray.ConfirmControlShown;
        bool actPossible = CardsGameApi.ReadyToggleAvailable();

        if (offered || !actPossible)
        {
            _silentTicks = 0;
            if (_stoodDown)
            {
                _stoodDown = false;
                _standDownLogged = false;
            }
            _why = offered
                ? "the mod is drawing this act as the control board's physical CONFIRM keycap, "
                  + $"labelled \"{tray.ConfirmControlLabel ?? "(no label yet)"}\" — the button the user "
                  + "pointed at in auswahl-beenden.jpg, in the place he tuned for it"
                : "the board shows no confirm keycap yet BECAUSE THE ACT IS NOT POSSIBLE YET "
                  + "(CardsGameApi.ReadyToggleAvailable() is false — the game has not made the ready "
                  + "toggle clickable), and a window carrying a button the game refuses to accept is "
                  + "not a way out of anything. The keycap appears on the same predicate the moment "
                  + "the act becomes possible";
            return;
        }

        // The act IS possible and the board is silent. Count, do not conclude — see the class doc.
        if (_silentTicks < int.MaxValue)
            _silentTicks++;
        _why = "the game says this toggle can take a click RIGHT NOW and the control board is "
               + $"offering no confirm control ({_silentTicks} consecutive tick(s) of "
               + $"{SilentTicksBeforeStandDown} before the claim stands down)";
        if (_silentTicks < SilentTicksBeforeStandDown || _stoodDown)
            return;

        _stoodDown = true;
        _why = "STOOD DOWN — the game says this toggle can take a click and the control board has "
               + $"offered no confirm control for {SilentTicksBeforeStandDown} consecutive tick(s). A "
               + "confirm the player cannot reach is a deadlock and that rule outranks the one about "
               + "bare controls, so this window is handed back to the float path";
        if (_standDownLogged)
            return;
        _standDownLogged = true;
        // HW-VERIFY
        VRLog.Note(Scope, "BOARD CONFIRM STAND-IN STOOD DOWN: the game reports the multiplayer ready "
                          + "toggle ('Auswahl beenden') CLICKABLE right now, but the control board has "
                          + $"shown no CONFIRM keycap for {SilentTicksBeforeStandDown} consecutive "
                          + "tick(s). The refusal that keeps that window out of the room is therefore "
                          + "OFF and it floats on its own again from this tick — THAT IS THE SAFETY "
                          + "VALVE AND NOT A REGRESSION: a bare control in a frame of its own is ugly, "
                          + "a confirm the player cannot reach is a deadlock, and the second rule wins. "
                          + "IF YOU ARE READING THIS IN A HARDWARE LOG, THE BOARD KEYCAP IS THE BUG — "
                          + "PlayTray.ConfirmControlShown was false while CardsGameApi"
                          + ".ReadyToggleAvailable() was true. Nothing was written to the game here.");
    }

    /// <summary>
    /// PURE READ — is the mod's physical board keycap standing in for THIS toggle instance this
    /// instant? Called from <see cref="FloatRefusalTable"/> ROW 2's <c>heldBy</c>, which the
    /// eligibility walk re-enters several times per tick, so it holds no state and logs nothing.
    ///
    /// <para>Every level term is re-read here rather than cached from <see cref="Tick"/>: a tick that
    /// stops running (module detach, a guarded throw, a scene load) must not be able to hold a
    /// refusal open. The only thing carried over from the tick is the stand-down, and its stale value
    /// fails SAFE — a stale <c>true</c> floats the window.</para>
    ///
    /// <para>THE OBJECT IDENTITY IS CHECKED AND NOT JUST THE STATE, for ROW 2's own reason:
    /// <c>UIReadyToggle</c> is a Singleton reused for quests, city events, town records, retirement
    /// and the lobby's slot assignment, and a claim about one use of it must never refuse another.
    /// </para>
    /// </summary>
    internal static bool StandsInFor(GameObject? toggleGo)
    {
        if (_stoodDown || toggleGo == null)
            return false;
        if (!OnlineCardSelection())
            return false;
        PlayTray? tray = PlayTray.Current;
        if (tray == null || !tray.IsVisible)
            return false;
        if (!Singleton<UIReadyToggle>.IsInitialized)
            return false;
        UIReadyToggle toggle = Singleton<UIReadyToggle>.Instance;
        return toggle != null && ReferenceEquals(toggle.gameObject, toggleGo);
    }

    /// <summary>Teardown (module detach) — mirrors <c>FloatRefusalTable.Reset</c>'s call site, so a
    /// stand-down can never survive the module that measured it.</summary>
    internal static void Reset()
    {
        _silentTicks = 0;
        _stoodDown = false;
        _standDownLogged = false;
        _why = "never evaluated — the mod has not ticked in an online card selection yet";
    }

    /// <summary>
    /// ONLINE card selection, which is the only state in which the game shows this window for this
    /// act — <c>InitializeReadyToggleForCardSelection</c>'s whole body is inside
    /// <c>if (FFSNetwork.IsOnline)</c>. The phase is the game's own
    /// <c>SelectAbilityCardsOrLongRest</c>, the same term <c>CardsGameApi.ReadyToggleAvailable()</c>
    /// gates on, so the two can never disagree about which phase this is.
    /// </summary>
    private static bool OnlineCardSelection() =>
        FFSNetwork.IsOnline
        && PhaseManager.PhaseType == CPhase.PhaseType.SelectAbilityCardsOrLongRest;

    private static void Release(string why)
    {
        _silentTicks = 0;
        _stoodDown = false;
        _standDownLogged = false;
        _why = why;
    }
}
