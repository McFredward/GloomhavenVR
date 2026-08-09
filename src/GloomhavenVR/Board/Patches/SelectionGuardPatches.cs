using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using ScenarioRuleLibrary;
using Script.GUI.SMNavigation;
using Script.GUI.SMNavigation.States.PopupStates;
// The portrait click's audible acknowledgement. Aliased because every branch of the guard below
// reports its outcome to it and the fully qualified name would bury the branch it annotates.
using ClickSound = GloomhavenVR.WorldUI.Surfaces.InitiativePortraitClickSound;

namespace GloomhavenVR.Board.Patches;

// ---------------------------------------------------------------------------
// Action-phase selection guard (USER-BUG deadlock fix).
//
// Translated report: "During the action phase I laser-clicked ANOTHER of my
// characters who is NOT at turn. Nothing changed — but then I could no longer
// select ANY action (deadlock). Clicking an enemy plays an 'invalid' sound; I
// want the SAME when I click a non-current character."
//
// ROOT CAUSE: a human click on a PLAYER's initiative-track avatar routes
//   InitiativeTrackPlayerAvatar.OnClick(actorUI)          (:30)
//     -> base InitiativeTrackActorAvatar.OnClick(actorUI) (:154)
//       -> InitiativeTrack.Select(actorUI)                (:332)  -> sets SELECTED actor
// During the ActionSelection phase this re-points the docked control-board cards
// to the clicked actor, whose halves the game refuses to play while the owner is
// not Choreographer.CurrentActor (FullAbilityCard.cs:635). The action board then
// shows the WRONG actor's cards -> nothing is clickable -> deadlock.
//
// FIX: prefix the HUMAN player-portrait click seam ONLY and, when the click would
// select a NON-CURRENT locally-controlled player during the action phase, play the
// game's own invalid-click SFX and RETURN FALSE (skip the original entirely — no
// Select, no ToggleViewAllCards), exactly like clicking an enemy. The current actor
// stays selected so the action board remains usable.
//
// WHY THIS SEAM IS SAFE: the game's PROGRAMMATIC selects (round-start turn select in
// UpdateInitiativeTrack, character-tab switch via CardsHandTabs, the initiative-
// adjustment select in Choreographer) and the MOD's own take-damage drive
// (CardsGameApi.SelectActor -> track.Select) all call InitiativeTrack.Select DIRECTLY
// and never this avatar OnClick — so none of them are touched, and last round's
// attacked-actor selection keeps working. Enemy avatars run the base OnClick, never
// this override, and the guard ignores non-player actors anyway.
//
// Applied by BoardModule alongside the other board patches; removed collectively via
// Plugin.OnDestroy (Harmony.UnpatchSelf) for the hot-reload contract.
// ---------------------------------------------------------------------------

/// <summary>
/// Human player-portrait click gate. Verified against the REAL GH.Runtime.dll
/// (v1.1.8307.0), ilspycmd 8.2:
/// <c>public override void OnClick(InitiativeTrackActorBehaviour actorUI)</c>
/// (InitiativeTrackPlayerAvatar.cs:30) — the player override of the avatar click,
/// invoked by the avatar button's serialized UnityEvent (virtual dispatch guarantees
/// a click on a player avatar runs this override). Enemy avatars use the un-overridden
/// base at :154, so this patch never sees an enemy click.
/// </summary>
[HarmonyPatch(typeof(InitiativeTrackPlayerAvatar), nameof(InitiativeTrackPlayerAvatar.OnClick))]
internal static class InitiativeTrackPlayerAvatar_OnClick_Guard
{
    /// <param name="__state">true ⇒ the original ran, so the POSTFIX owes the outcome report;
    /// false ⇒ a branch here already reported it (see <see cref="Report"/>).</param>
    private static bool Prefix(InitiativeTrackActorBehaviour actorUI, out bool __state)
    {
        __state = false;
        CActor? clicked = actorUI != null ? actorUI.Actor : null;
        if (clicked == null)
        {
            __state = true;
            return true; // vanilla
        }

        // FREE CHARACTER FOCUS (feature): outside the secret card-selection window a portrait
        // click is a VIEW request — "show me that character's hand, piles and played cards".
        // CharacterFocus records it and the card pipeline presents that character READ-ONLY.
        //
        // THE FOCUS BRANCH RETURNS FALSE, ALWAYS (user ruling 2026-08-08, "das Wechseln darf nie
        // blockiert sein" + "der Wechsel darf nie eine offene Entscheidung stören"). Once the
        // focus is taken, vanilla's ENTIRE OnClick is suppressed — both halves of it
        // (InitiativeTrackPlayerAvatar.cs:30-44):
        //   * the SELECT (base.OnClick → InitiativeTrack.Select → avatar.Select →
        //     CardsHandManager.SwitchHand) — a real game-state write, and the one that re-points
        //     the docked control-board cards at a non-acting actor whose halves the game then
        //     silently refuses (FullAbilityCard.cs:635), i.e. the original action deadlock;
        //   * the unconditional CardsHandManager.ToggleViewAllCards, which latches
        //     IsFullCardPreviewShowing and deadlocks every later card action (see
        //     Board/Patches/AllCardsViewerBlock.cs, which blocks that latch independently).
        // That is the STRUCTURAL separation the ruling asks for: a VR portrait click cannot
        // answer, cancel or advance anything, because the only code that runs for it is
        // CharacterFocus.TryFocus, and TryFocus has no rules call in it. A pending decision
        // therefore survives a focus switch untouched — the prompt's own widgets live on a
        // different surface entirely (WorldUI.Surfaces.DecisionDockSurface on
        // PlayTray.DecisionMount) and are never re-parented, re-built or re-pointed by a focus.
        //
        // It is also why NO gate below can block the switch any more: the switch has already
        // happened by the time we get there.
        if (CharacterFocus.TryFocus(clicked))
        {
            Report(clicked, ClickSound.Outcome.Succeeded,
                "CharacterFocus.TryFocus took the focus — this client now PRESENTS that character " +
                "(read-only when it is not one of ours). A view change is a successful click");
            return false;
        }

        // ---- focus did NOT take: card-selection phase, or not a focus target at all ----------
        // In the card-selection phase vanilla's own switch IS the legitimate mechanism (a player
        // may leaf through their OWN characters while choosing cards), so the pre-focus rules
        // below decide, exactly as they did before the feature existed.

        // MP test item #8a: a portrait of a character ASSIGNED TO ANOTHER PLAYER —
        // refuse the whole click (select AND ToggleViewAllCards). Only active online with
        // >1 participant (OwnershipGuardActive).
        if (CardsGameApi.IsForeignControlledSelect(clicked))
        {
            CardsGameApi.RejectForeignSelect(clicked);
            Report(clicked, ClickSound.Outcome.AlreadyAnswered,
                "MP ownership guard — RejectForeignSelect already played the game's invalid-click " +
                "SFX itself, so the click has been answered once");
            return false;
        }

        // Unreachable while the focus gate is open (the focus branch above already returned), so
        // in practice this only ever sees the card-selection phase — where the predicate is false
        // by construction (it tests for ActionSelection/Action). Kept as the standing answer for
        // the one case that can still reach it: an EXHAUSTED hero's stale portrait during the
        // action phase, whose select would re-point the docked cards at a dead actor.
        if (!CardsGameApi.IsActionPhaseNonCurrentPlayerSelect(clicked))
        {
            __state = true;
            return true; // vanilla — run the original select; the postfix reports what it did
        }

        CardsGameApi.RejectActionPhaseSelect(clicked);
        Report(clicked, ClickSound.Outcome.AlreadyAnswered,
            "action-phase select guard — RejectActionPhaseSelect already played the game's " +
            "invalid-click SFX itself, so the click has been answered once");
        return false; // reject like an enemy click — keep the current actor selected
    }

    /// <summary>
    /// What VANILLA did with the click, for the clicks this guard let through — the other half of
    /// the outcome report (see <see cref="Report"/>).
    ///
    /// <para>The original is NOT unconditionally a success: <c>InitiativeTrackPlayerAvatar.OnClick</c>
    /// only forwards to <c>base.OnClick</c> when its private <c>isSelectableByClick</c> is set
    /// (InitiativeTrackPlayerAvatar.cs:30-44), and <c>InitiativeTrack.Select</c> then returns
    /// immediately — logging "Skip select … in phase …" — unless <c>IsSelectable</c> holds for the
    /// current phase (InitiativeTrack.cs:114/334). So the honest test is the RESULT: is the track's
    /// selected actor now the clicked character? That is the game's own definition of the character
    /// having changed (it is what drives <c>avatar.Select</c> → <c>CardsHandManager.SwitchHand</c>),
    /// it is read through the same accessor the mod's own drive uses
    /// (<c>CardsGameApi.SelectActor</c>, InitiativeTrack.cs:527), and it costs one call.</para>
    ///
    /// <para>Never throws: a torn-down track must leave the click alone, not take the pointer with
    /// it. An unreported outcome degrades to the sound's own pre-focus fallback.</para>
    /// </summary>
    private static void Postfix(InitiativeTrackActorBehaviour actorUI, bool __state)
    {
        if (!__state)
            return; // a branch in the prefix already reported this click
        try
        {
            CActor? clicked = actorUI != null ? actorUI.Actor : null;
            if (clicked == null)
                return;
            InitiativeTrack track = InitiativeTrack.Instance;
            InitiativeTrackActorBehaviour? selected = track != null ? track.SelectedActor() : null;
            bool landed = selected != null && ReferenceEquals(selected.Actor, clicked);
            Report(clicked,
                landed ? ClickSound.Outcome.Succeeded : ClickSound.Outcome.Refused,
                landed
                    ? "vanilla's own select LANDED — InitiativeTrack.SelectedActor is now this " +
                      "character (the card-selection phase's legitimate character switch)"
                    : "vanilla ran and its select did NOT land — InitiativeTrack.IsSelectable is " +
                      "false in this phase (or the avatar's isSelectableByClick is clear), so " +
                      "nothing changed");
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Board", $"Portrait click outcome report threw — the click is unaffected: {e.Message}");
        }
    }

    /// <summary>
    /// Tell the click's audible acknowledgement WHAT THIS CLICK ACTUALLY DID.
    ///
    /// <para>USER REPORT 2026-08-09: "Das 'Abgelehnt-Geräusch' kommt wenn ich auf ein Character den
    /// ich selber nicht besitze — obwohl es ja gar nicht (mehr) abgelehnt wird, dort sollte dieses
    /// Geräusch nicht kommen sondern das wenn man ganz normal einen character ändert." The sound
    /// used to be keyed on OWNERSHIP, which stopped predicting refusal the moment free character
    /// focus made a foreign portrait click succeed. It is keyed on the outcome now, and THIS is the
    /// only place that knows it — so every branch above reports, and the report is a pure
    /// bookkeeping write that cannot throw and cannot change what the click did
    /// (<c>WorldUI.Surfaces.InitiativePortraitClickSound.NoticeOutcome</c>).</para>
    /// </summary>
    private static void Report(CActor clicked, ClickSound.Outcome outcome, string branch)
        => ClickSound.NoticeOutcome(clicked, outcome, branch);
}

// ---------------------------------------------------------------------------
// FOCUS CLICK GATE #2 (user ruling 2026-08-08) — the game's own click INTERCEPTOR.
//
// The prefix above can only run if the portrait's click actually reaches
// InitiativeTrackPlayerAvatar.OnClick. Two game-side gates sit in front of it, and
// both were verified against the REAL GH.Runtime.dll (v1.1.8307.0, ilspycmd 8.2):
//
//  1. Selectable.interactable on the avatar's Button — NOT a problem, and worth
//     recording so nobody "fixes" it later: InitiativeTrackPlayerAvatar overrides
//     SetAttributesDirect and passes activeButton: TRUE to the base unconditionally
//     (InitiativeTrackPlayerAvatar.cs:120-125), keeping only a private
//     `isSelectableByClick` for vanilla's own select. So a PLAYER portrait's button is
//     interactable in every phase, including the whole action phase where the game
//     calls UpdateInitiativeTrack(..., playersSelectable: false, ...)
//     (Choreographer.cs:12548). Our seam therefore never depended on interactability
//     and still does not.
//
//  2. InteractabilityManager.ShouldAllowClickForExtendedButton — a REAL blocker.
//     ExtendedButton.OnPointerClick (ExtendedButton.cs:168-173) returns WITHOUT calling
//     base.OnPointerClick when the manager says no, so the button's onClick — and with
//     it our prefix — never runs at all. The manager says no whenever a level-interaction
//     PROFILE is loaded (InteractabilityManager.cs:96-115: any button not explicitly
//     listed in the profile's isolated controls is intercepted), and a profile is loaded
//     for every dismissable level message (LevelEventsController.cs:954/958) and stays
//     loaded for a WHOLE level whose ShouldPreventUnspecifiedInteraction is set
//     (LevelEventsController.cs:986). s_EventsControllerActive is true for the entire
//     scenario (LevelEventsController.cs:66/138/898), so this is not a menu-only path:
//     it is exactly "the game owes the player a decision, so nothing else may be
//     clicked" — the class of block the ruling outlaws for the FOCUS switch.
//
// FIX, deliberately narrow: allow the click ONLY for an initiative-track actor avatar
// button, and ONLY while CharacterFocus.Open. Both halves matter.
//   * "actor avatar button" — every OTHER isolated control keeps the game's isolation
//     exactly as authored, so a tutorial that says "click THIS card" still means it.
//   * "while the focus gate is open" — precisely when the click is guaranteed to be a
//     pure VIEW change: the prefix above returns false on a successful focus, so no
//     select, no SwitchHand, no ToggleViewAllCards, no rules call. During card selection
//     the gate is shut, this bypass is off, and vanilla's isolation (which then really
//     does guard a game action — the hand switch) is byte-identical.
// With VR conversion off (flat play) the bypass is off too.
// ---------------------------------------------------------------------------

/// <summary>
/// Lets an initiative-track PORTRAIT click through the game's interaction-isolation
/// interceptor while free character focus is open, so no level message / interaction
/// profile can ever refuse a focus switch. Verified against the REAL GH.Runtime.dll
/// (v1.1.8307.0), ilspycmd 8.2:
/// <c>public static bool ShouldAllowClickForExtendedButton(ExtendedButton buttonToCheck)</c>
/// (InteractabilityManager.cs:96) — the exact method <c>ExtendedButton.OnPointerClick</c>
/// consults before dispatching (ExtendedButton.cs:168-173). The portrait's button is the
/// public <c>ExtendedButton avatarButton</c> field of
/// <c>InitiativeTrackActorBehaviour</c> (InitiativeTrackActorBehaviour.cs:14).
/// <para>Read-only and side-effect free: it answers a question, it does not dismiss the
/// message, load a profile, or touch <c>InteractabilityHighlightCanvas</c>. The prompt the
/// profile belongs to is not disturbed in any way — it stays open and stays answerable.</para>
/// </summary>
[HarmonyPatch(typeof(InteractabilityManager),
    nameof(InteractabilityManager.ShouldAllowClickForExtendedButton))]
internal static class InteractabilityManager_PortraitFocusBypass
{
    /// <summary>One line per burst — a held laser re-asks this every frame.</summary>
    private static float _lastLogTime = float.NegativeInfinity;

    private const float LogIntervalSeconds = 5f;

    private static bool Prefix(ExtendedButton buttonToCheck, ref bool __result)
    {
        try
        {
            if (buttonToCheck == null || !WorldUIConfig.ConversionActive)
                return true; // flat play / nothing to identify — vanilla verdict
            if (!CharacterFocus.Open)
                return true; // card selection: vanilla isolation is the correct answer
            var row = buttonToCheck.GetComponentInParent<InitiativeTrackActorBehaviour>();
            if (row == null)
                return true; // not a portrait — every other control keeps its isolation
            if (row.avatarButton != null && !ReferenceEquals(row.avatarButton, buttonToCheck))
                return true; // some other button inside the row (none authored today)

            __result = true; // allow — the click is a pure view change, see the header
            Log(row);
            return false;
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Board", $"[Focus] interactability bypass threw — vanilla verdict stands: {e.Message}");
            return true;
        }
    }

    private static void Log(InitiativeTrackActorBehaviour row)
    {
        float now = UnityEngine.Time.unscaledTime;
        if (now - _lastLogTime < LogIntervalSeconds)
            return;
        _lastLogTime = now;
        string who = row.Actor != null ? CharacterFocus.Describe(row.Actor as CPlayerActor) : "?";
        VRLog.Info("Board", $"[Focus] portrait click on '{who}' ALLOWED past the game's " +
                            "interaction-isolation profile (InteractabilityManager) — a focus " +
                            "switch is a view change and may never be blocked; every other " +
                            "isolated control keeps the game's verdict.");
    }
}

// ---------------------------------------------------------------------------
// MP ownership select guard (MP test item #8a) — board-miniature seam.
//
// In VR a laser/poke click on a character's MINIATURE (or its hex) is dispatched
// through the game's own click path (Controller.LateUpdate → TileBehaviour.s_Callback
// → Choreographer.TileHandler, see BoardClickDriver), and during the card-selection
// wait state TileHandler's select branch has NO ownership check: it selects ANY
// player found on the clicked tile and switches the hand to it. Online, that lets
// a VR player select a character assigned to another player. The initiative-track
// PORTRAIT seam is guarded above; this guard closes the miniature seam.
//
// WHY NOT InitiativeTrack.Select(CPlayerActor): that overload is also the game's
// PROGRAMMATIC select for remote actors (CheckForInitiativeAdjustments,
// Choreographer.cs:11681, selects the initiative-adjusting actor on every client)
// — a wholesale ownership prefix there would break remote-turn display. TileHandler
// is exclusively the tile CLICK dispatch, so the guard sits on the human seam only.
// ---------------------------------------------------------------------------

/// <summary>
/// Verified against the REAL GH.Runtime.dll (v1.1.8307.0), ilspycmd 8.2:
/// <c>public void TileHandler(CClientTile clientTile, List&lt;CTile&gt; optionalTileList = null,
/// bool networkActionIfOnline = false, bool isUserClick = false,
/// bool actingPlayerHasSecondClickConfirmationEnabled = false)</c> (Choreographer.cs:1811).
/// Its select branch (Choreographer.cs:1826-1840): wait state <c>WaitingForCardSelection</c>
/// &amp;&amp; <c>CardsHandManager.IsActive()</c> &amp;&amp; <c>FindPlayerAt(tile)</c> != null
/// &amp;&amp; not in <c>ConfirmationBoxState</c> → <c>InitiativeTrack.Select(player)</c> +
/// <c>SwitchHand</c> + tile-click SFX, then RETURN — unconditionally, so when this prefix
/// replicates exactly those entry conditions and the found player is foreign-controlled,
/// skipping the whole original (return false) removes ONLY the select; every other
/// TileHandler branch is unreachable in that condition set anyway.
/// </summary>
[HarmonyPatch(typeof(Choreographer), nameof(Choreographer.TileHandler))]
internal static class Choreographer_TileHandler_OwnershipGuard
{
    private static bool Prefix(Choreographer __instance, CClientTile clientTile)
    {
        try
        {
            if (__instance == null || clientTile == null || clientTile.m_Tile == null)
                return true;
            if (__instance.m_WaitState == null
                || __instance.m_WaitState.m_State != Choreographer.ChoreographerStateType.WaitingForCardSelection)
                return true;
            CardsHandManager hands = CardsHandManager.Instance;
            if (hands == null || !hands.IsActive())
                return true;
            if (ScenarioManager.Scenario == null)
                return true;
            CPlayerActor? clicked = ScenarioManager.Scenario.FindPlayerAt(clientTile.m_Tile.m_ArrayIndex);
            if (clicked == null)
                return true;
            UINavigation nav = Singleton<UINavigation>.Instance;
            if (nav != null && nav.StateMachine.IsCurrentState<ConfirmationBoxState>())
                return true; // vanilla would not select either — keep byte-identical fallthrough
            if (!CardsGameApi.IsForeignControlledSelect(clicked))
                return true; // own character / offline / solo session — vanilla

            CardsGameApi.RejectForeignSelect(clicked);
            return false; // skip the select entirely — local selection untouched
        }
        catch (System.Exception e)
        {
            // A throwing guard must never eat the game's click dispatch.
            VRLog.Warn("Board", $"TileHandler ownership guard threw — passing click through: {e.Message}");
            return true;
        }
    }
}

// ---------------------------------------------------------------------------
// MP reassignment fallback trigger (MP test item #8b) — see
// Board/SelectionOwnershipFallback.cs for the full design. This patch only ARMS
// the fallback; the reaction runs from BoardDriver.Update on a later frame.
// ---------------------------------------------------------------------------

/// <summary>
/// Verified against the REAL GH.Runtime.dll (v1.1.8307.0), ilspycmd 8.2:
/// <c>public void OnControlReleased()</c> (CharacterManager.cs:488) — the in-scenario
/// seam FFSNet invokes when this client loses control of a character (host reassign /
/// drop); its body flips <c>CharacterActor.IsUnderMyControl</c> to false only when it
/// was true. Parameterless, so no Bolt type is referenced (the paired
/// <c>OnControlAssigned(NetworkPlayer)</c> is deliberately NOT patched — its parameter
/// is a Bolt-derived type and requirement #8b only needs the release edge).
/// Prefix records whether the character was locally controlled; postfix arms the
/// fallback only for a real mine→foreign transition.
/// </summary>
[HarmonyPatch(typeof(CharacterManager), nameof(CharacterManager.OnControlReleased))]
internal static class CharacterManager_OnControlReleased_Fallback
{
    private static void Prefix(CharacterManager __instance, out bool __state)
    {
        __state = __instance != null
                  && __instance.CharacterActor is CPlayerActor player
                  && player.IsUnderMyControl;
    }

    private static void Postfix(CharacterManager __instance, bool __state)
    {
        if (!__state || __instance == null)
            return;
        if (__instance.CharacterActor is CPlayerActor player && !player.IsUnderMyControl)
            SelectionOwnershipFallback.Arm(player);
    }
}
