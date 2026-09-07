using System;
using FFSNet;
using GloomhavenVR.Core;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.Party;
using MapRuleLibrary.State;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE ASSIGNMENT WINDOW — who loses which item, who gets the gold — AND WHO IS ALLOWED TO TOUCH IT.
///
/// <para><b>USER REQUEST (2026-09-07, verbatim):</b> <i>"Das Zuweisungsfenster (zB wer welchen
/// Gegenstand verliert, Gold erhält etc), soll ein Multiplayerfenster werden und jeder soll es
/// bedienen können (wie bei der Story und der Begegnung auch)."</i></para>
///
/// <para><b>THE MECHANISM, IN ONE SENTENCE.</b> The assignment family gates operability on WHO IS
/// HOST, in a game that already knows WHOSE CHARACTER IT IS — <c>CMapCharacter.IsUnderMyControl</c>
/// (decompiled/MapRuleLibrary/MapRuleLibrary.Party/CMapCharacter.cs:107) is the game's own
/// per-player ownership term, it is what the loadout screen (UILoadoutManager.cs:179), the
/// enhancement window (UINewEnhancementWindow.cs:741/796) and the party display
/// (NewPartyDisplayUI.cs:1187) all consult, and NOT ONE of the four gates below consults it. So the
/// window is raised on every client BY DESIGN and is operable on exactly one of them — and that one
/// is the host, not the player the decision is about.</para>
///
/// <para><b>THE FOUR GATES, each read out of the decompile rather than inferred.</b> All four are
/// <c>FFSNetwork.IsHost</c> and all four are independent, which is why a fix that relaxes one of
/// them is not a fix:
/// <list type="number">
/// <item><b>The +/- buttons are switched off in the view.</b>
/// <c>UIDistributePointsSlot.EnableAddPoints</c> (UIDistributePointsSlot.cs:249) —
/// <c>if (!m_IsRewards || !FFSNetwork.IsOnline || FFSNetwork.IsHost) { addPointButton.interactable
/// = enable; … } else { addPointButton.interactable = false; }</c>, and
/// <c>EnableRemovePoints</c> (:260) identically. <c>m_IsRewards</c> is set only from
/// <c>Init(…, isRewards, …)</c> (:143) and only <c>UIDistributeReward.Distribute</c> passes
/// <c>isRewards: true</c> (UIDistributeReward.cs:61) — so this gate is exactly the map-side reward
/// family and nothing else.</item>
/// <item><b>The CONFIRM button is switched off, and on a gamepad it is ABSENT.</b>
/// <c>UIDistributeReward.SetButtonInteractable</c> (UIDistributeReward.cs:110) —
/// <c>bool flag = interactable &amp;&amp; (!FFSNetwork.IsOnline || FFSNetwork.IsHost); if
/// (InputManager.GamePadInUse) { confirmButton.gameObject.SetActive(flag); … } else {
/// confirmButton.interactable = flag; }</c>.</item>
/// <item><b>Every point send is inside <c>if (FFSNetwork.IsHost)</c>, not inside
/// <c>if (IsOnline)</c></b> — DistributeGoldProcess.cs:100/139, DistributeItemsProcess.cs:72/90/110,
/// DistributeAttackModifierProcess.cs:57/75, DistributeConditionsProcess.cs:57/75,
/// DistributeGoldBagProcess.cs:72/90/105 — and so is the confirm send,
/// <c>UIDistributeReward.OnConfirmClick</c> (:142).</item>
/// <item><b>The gamepad keycaps are offered to the host only.</b>
/// <c>UIDistributePointsPopup.ShowHotkeys</c> (:50-63) — <c>if (FFSNetwork.IsOnline) return
/// FFSNetwork.IsHost;</c>.</item>
/// </list>
/// The sibling item-LOSS picker states the same policy out loud rather than merely enforcing it:
/// <c>ItemRewardLosePicker.CanSelect</c> (:25-35) is <c>FFSNetwork.IsOnline ? FFSNetwork.IsHost :
/// true</c>, it is handed straight to <c>picker.Show(…, CanSelect, …)</c> (:54) where
/// <c>ItemCardPicker</c> writes <c>card.Selectable.interactable = canSelect</c> (:115), and a client
/// is shown <c>Consoles/GUI_WAIT_FOR_HOST_TIP</c> (:63) — the game's own words for this defect.</para>
///
/// <para><b>AND THE GAME NEVERTHELESS MAKES EVERY CLIENT WATCH IT.</b> That is the half that turns
/// this from a design choice into a reportable defect. <c>UIDistributeRewardManager.Process</c>
/// (:34-41) opens an all-player ready-up — <c>Singleton&lt;UIMapMultiplayerController&gt;.Instance
/// .ShowRewardsMultiplayer(…)</c>, which locks every client behind
/// <c>GUI_WAIT_PLAYERS_CONFIRM_TIP</c> (UIMapMultiplayerController.cs:108-132) — and only then runs
/// <c>ProcessSP</c>. Nothing on the raise path is host-gated: <c>UIDistributeReward.Distribute</c>
/// (:58) reaches <c>popup.Show(…, isRewards: true)</c> on every machine, and :71-75 even prepares
/// the client for it (<c>if (FFSNetwork.IsClient) ActionProcessor.SetState(ProcessFreely)</c>). Both
/// hardware logs of ModBuild 474 agree — <c>DISTRIBUTE POPUP FLOATED: 'UI Distribute Items Rewards
/// Popup'</c> appears once on the host AND once on the peer. Every player is made to look at a
/// window none of them but the host can touch.</para>
///
/// <para><b>WHY THIS CLASS SHIPS AN INSTRUMENT AND NOT THE REMEDY.</b> The remedy is known and is
/// written out below, but every part of it is closed to this lane. It needs (a) new Harmony patch
/// classes, and <c>scripts/patch-inventory.sh check</c> fails on an unregistered patch class while
/// <c>docs/PATCH-INVENTORY.md</c> is closed to every lane and <c>generate</c> is forbidden; (b) for
/// the "Multiplayerfenster" half — the corner network badge and the synced pose — a new
/// <see cref="SharedWindowKind"/>, which is a kind byte, a <c>SharedWindowKindMax</c> bump and a
/// <c>SharedWindowMaxEntries</c> bump in <c>Net/NetProtocol.cs</c>, closed to everyone. What is
/// shippable here is the reading that decides the next round, and it is deliberately the reading
/// that can convict the fix as well as the defect.</para>
///
/// <para><b>THE REMEDY, so the next lane does not re-derive it.</b> Copy
/// <c>Net/EncounterChoice.cs</c> — it is the same request, already granted, for the window the user
/// names as his precedent ("Jeder Spieler kann auf eine Option drücken und sie wird dann
/// angenommen"). Its recorded finding transfers verbatim and is the whole reason a naive fix is a
/// campaign-killer: a client may NOT originate the game's own <c>SendGameAction</c> here, because an
/// arriving <c>GameActionEvent</c> is ENQUEUED and the distribute actions carry
/// <c>ActionPhaseType.NONE</c>, so <c>TryProcessNextAction</c>'s
/// <c>action.TargetPhaseID != (int)currentState.PhaseType</c> test ends in the "Desynchronization
/// occurred" dialog. The channel that works is <c>Synchronizer.SendSideAction(…, sendToHostOnly:
/// true)</c>, delivered by <c>ActionProcessor.ProcessSideAction</c> with no queue and no phase test.
/// So: a client press sends ONE side action naming the actor and the process type and advances
/// NOTHING locally; the host validates it and drives its OWN
/// <c>UIDistributePointsSlot</c>/<c>confirmButton</c>; from there the game's unmodified code sends
/// the real <c>DistributeUIAddPoint</c>/<c>DistributeUIConfirm</c> and every machine — the presser
/// included — replays it through <c>UIDistributeRewardManager.ProxyAddPoints</c>/
/// <c>ProxyConfirmClick</c> (:118/:131/:145, wired at GameAction.cs:678-696). The receivers already
/// accept an action without checking who sent it, so nothing on that side changes.
/// <b>THE HAZARD THAT MUST BE STATED WITH IT:</b> <c>service.Apply()</c> runs unconditionally on
/// EVERY client (UIDistributeReward.cs:81) and the model is advanced by replaying UI actions, so a
/// half-done patch that un-greys a client's buttons WITHOUT also routing its press produces a
/// silent, persisted desync instead of a visible failure. Un-greying is the LAST step, never the
/// first.</para>
///
/// <para><b>WHAT IS DELIBERATELY NOT IN THIS FAMILY</b>, because the user's "etc" has an edge and
/// naming it is the point:
/// <list type="bullet">
/// <item><b>The scenario-side redistribute-damage popup</b> (<c>DistributeDamageService</c>). It is
/// ALREADY per-player-owned and not host-owned — <c>caster.IsUnderMyControl</c> at :153/:162 and the
/// send at :183. It is the shape the user is asking for, not an instance of the defect, and adopting
/// it would break a flow that works.</item>
/// <item><b>The scenario-side "who burns a card" select popup</b>
/// (<c>DistributeSelectPlayerActorService</c>, host-gated at :113/:120/:145). It IS the same defect,
/// but it is a scenario decision about preventing damage, not an assignment of loot, and the user's
/// two examples and his "Zuweisungsfenster" both name the reward flows. It is listed here rather
/// than covered so that a later round can adopt it on purpose instead of discovering it.</item>
/// <item><b>The reward SHOWCASE</b> (<c>UICampaignRewardWindow</c> / <c>UIRewardsManager</c>) — it
/// displays what was won and decides nothing, so there is nothing to operate.</item>
/// </list></para>
/// </summary>
internal static class AssignmentWindows
{
    private const string Scope = "WorldUI";

    /// <summary>The last answer <see cref="Raised"/> gave, so the census fires on the EDGE and not
    /// per frame. −1 = never asked, which is why it is an int and not a bool.</summary>
    private static int _lastRaised = -1;

    /// <summary>Unconditional liveness counter: how many assignment-window raises this process has
    /// seen. A report whose census line never appears at all is a different defect from one where
    /// it appears and says the client may not operate the window, and this number separates
    /// them.</summary>
    private static int _raisesSeen;

    /// <summary>
    /// IS AN ASSIGNMENT WINDOW UP RIGHT NOW?
    ///
    /// <para>One public bool on one singleton. <c>UIDistributeRewardManager.IsDistributing</c> is
    /// the game's own "an assignment is being made" flag — set true at the top of <c>Process</c>
    /// and false only when the whole promise chain resolves (UIDistributeRewardManager.cs:32/:62)
    /// — and it is already what two other places in this mod read for the same question
    /// (<c>WorldUI/FlatScreen/FlatScreen.4.Lifecycle.cs:104</c> and the ModBuild 373 note on
    /// <c>MandatoryDecision</c>:220). Reading it here rather than resolving the five private
    /// <c>DistributeRewardProcess</c> entries keeps this a single source of truth and costs one
    /// property read per frame.</para>
    ///
    /// <para>Deliberately NOT <c>Singleton&lt;UIDistributePointsPopup&gt;.Instance.IsShown</c>. Each
    /// of the five <c>DistributeRewardProcess</c> entries holds its OWN
    /// <c>[SerializeField] UIDistributeReward processUI</c>, and each of those holds its own
    /// <c>popup</c> serialized field, so the singleton is at best one of several — and it is
    /// whichever copy <c>Awake</c>d last, since <c>Singleton&lt;T&gt;</c> simply overwrites its
    /// static field (Singleton.cs:11-14). <b>HOW MANY DISTINCT POPUP OBJECTS THERE ACTUALLY ARE IS
    /// NOT MEASURED HERE AND WAS NEVER MEASURED ANYWHERE</b> — an earlier revision of this comment
    /// asserted "FIVE" as a fact, which it is not; whether the five serialized fields point at five
    /// objects or at one shared object is a scene-authoring question the decompiled source cannot
    /// answer. <c>Net/AssignmentChoice.NotePopupIdentity</c> settles it on hardware with the
    /// <c>ASSIGNMENT POPUP IDENTITY</c> line, which prints the <c>GetInstanceID()</c> of the reward
    /// UI, of its popup and of the singleton at every raise. Reading the manager's own
    /// <c>IsDistributing</c> is correct either way, which is why this property does not wait for
    /// that answer.</para>
    /// </summary>
    internal static bool Raised =>
        Singleton<UIDistributeRewardManager>.IsInitialized
        && Singleton<UIDistributeRewardManager>.Instance != null
        && Singleton<UIDistributeRewardManager>.Instance.IsDistributing;

    /// <summary>
    /// MAY THIS CLIENT OPERATE THE ASSIGNMENT WINDOW?
    ///
    /// <para><b>This is a MIRROR of a named game expression, never a policy of the mod's own.</b>
    /// The term is <c>!FFSNetwork.IsOnline || FFSNetwork.IsHost</c>, which is literally the
    /// right-hand side of <c>UIDistributeReward.SetButtonInteractable</c> (:110) and the same test
    /// <c>UIDistributePointsSlot.EnableAddPoints</c> (:249) applies to the +/- buttons. If the game
    /// ever changes it, this reads WRONG and the census says so out loud — which is the correct
    /// failure mode for a mirror. It must never become the thing that decides; the moment the
    /// remedy lands, the deciding term stays inside the game's own widgets and this stays a
    /// reading.</para>
    /// </summary>
    internal static bool ThisClientMayOperate => !FFSNetwork.IsOnline || FFSNetwork.IsHost;

    /// <summary>
    /// HOW MANY PLAYERS MAY OPERATE IT, as this client understands it — and the point of the number
    /// is that it is 1 in every session with more than one player in it.
    ///
    /// <para>Offline the answer is 1 because there is one player. Online it is 1 whatever the party
    /// size, because the gate is <c>FFSNetwork.IsHost</c> and there is exactly one host. It is
    /// returned as a count rather than a bool so the census line can print it beside the registry
    /// size and the sentence "1 of 4 players may operate this window" needs no interpretation.</para>
    /// </summary>
    internal static int OperatorCount => 1;

    /// <summary>How many players the game's registry holds, or −1 when it cannot be read. Never on
    /// the hot path — the census asks it on the raise EDGE only.</summary>
    private static int PlayerCount()
    {
        try
        {
            return PlayerRegistry.AllPlayers != null ? PlayerRegistry.AllPlayers.Count : -1;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    /// <summary>
    /// THE FALSIFIER: of the characters this assignment ranges over, how many are under THIS
    /// client's control, and how many are there in total.
    ///
    /// <para><c>AdventureState.MapState.MapParty.SelectedCharacters</c> is the population every one
    /// of the five processes builds its actor list from — <c>DistributeGoldProcess.Process</c>:180/187
    /// filters exactly that list, and the others do the same — so this is the decision's own subject
    /// set and not a second guess at it. <c>CMapCharacter.IsUnderMyControl</c> (:107) is the game's
    /// per-player ownership term.</para>
    ///
    /// <para>Guarded because <c>AdventureState.MapState</c> is null outside a loaded campaign and
    /// the party can be mid-rebuild; a census that throws is worse than one that says it could not
    /// read. Both counts come back −1 in that case.</para>
    /// </summary>
    private static void Subjects(out int mine, out int total)
    {
        mine = -1;
        total = -1;
        try
        {
            CMapState? state = AdventureState.MapState;
            CMapParty? party = state?.MapParty;
            if (party?.SelectedCharacters == null)
                return;
            mine = 0;
            total = 0;
            foreach (CMapCharacter c in party.SelectedCharacters)
            {
                if (c == null)
                    continue;
                total++;
                if (c.IsUnderMyControl)
                    mine++;
            }
        }
        catch (Exception)
        {
            mine = -1;
            total = -1;
        }
    }

    /// <summary>
    /// Per-frame edge detector. Two property reads in the steady state (the singleton test and the
    /// bool), and it allocates nothing until the flag actually rises.
    ///
    /// <para>Called from the top of <c>ModalFallback.Tick</c>, BEFORE that method's FRAME-ORDER
    /// marker and outside every one of its measured phases, because this belongs to none of them:
    /// the window it watches has no <c>UIWindow</c> and so never enters the modal pipeline at all.
    /// It is placed there rather than in <c>WorldUIModule</c>'s step table only because that table
    /// is not this lane's file; the correct long-term home is a step of its own.</para>
    /// </summary>
    internal static void Tick()
    {
        bool raised = Raised;
        int now = raised ? 1 : 0;
        if (now == _lastRaised)
            return;                     // the common case: one int compare, no allocation, no log

        _lastRaised = now;
        if (!raised)
            return;                     // the fall is not an answer-bearing event; the rise is
        _raisesSeen++;
        Announce();
    }

    /// <summary>Emits the one census line. Pure with respect to everything except the log — it
    /// reads game state and writes nothing, so no mechanism can come to depend on it having
    /// run.</summary>
    private static void Announce()
    {
        bool online = FFSNetwork.IsOnline;
        bool mayOperate = ThisClientMayOperate;
        int players = PlayerCount();
        Subjects(out int mine, out int total);
        string subjects = total < 0
            ? "UNREADABLE (no MapState / no party — the census could not reach "
              + "AdventureState.MapState.MapParty.SelectedCharacters)"
            : $"{mine} of {total}";

        // HW-VERIFY
        VRLog.Note(Scope,
            "ASSIGNMENT WINDOW RAISED — the map-side loot/gold assignment "
            + "(UIDistributeRewardManager.IsDistributing went true; the window the player sees is a "
            + "UIDistributeReward -> UIDistributePointsPopup, floated by DistributeRewardSurface). "
            + $"Raise #{_raisesSeen} on this client. "
            + "ADOPTED INTO THE SHARED WINDOW FAMILY: NO — and that is a property of the build, not "
            + "of this moment. SharedWindows.KindOf is typed on UIWindow and this popup carries "
            + "none (its Show/Hide are a bare window.SetActive on a plain GameObject, "
            + "UIDistributePointsPopup.cs:124/141), there is no record-21 kind for it, and its "
            + "handle is a SurfaceGrabBar rather than the GrabbableModal the shared machinery "
            + "reads. So it wears no corner network badge and its pose is published to nobody, "
            + "even though its CONTENT is already shared by the game's own "
            + "DistributeUIAddPoint/RemovePoint/Confirm records. "
            + $"MAY BE OPERATED BY {OperatorCount} of "
            + $"{(players < 0 ? "an unreadable number of" : players.ToString())} players in the "
            + $"registry (session is {(online ? "ONLINE" : "OFFLINE")}). THIS CLIENT MAY OPERATE "
            + $"IT: {(mayOperate ? "YES" : "NO")} — the deciding term is the game's own "
            + "(!FFSNetwork.IsOnline || FFSNetwork.IsHost), mirrored from "
            + "UIDistributeReward.SetButtonInteractable:110 and UIDistributePointsSlot."
            + "EnableAddPoints:249; the mod does not decide this and must not. "
            + $"FALSIFIER — IS THIS CLIENT THE ONE THE DECISION IS ABOUT: it controls {subjects} of "
            + "the characters this assignment ranges over "
            + "(CMapCharacter.IsUnderMyControl over AdventureState.MapState.MapParty."
            + "SelectedCharacters, the same population the five DistributeRewardProcess entries "
            + "build their actors from). READ THE TWO TOGETHER: a line that says MAY OPERATE IT: NO "
            + "beside a non-zero subject count is the defect this item exists to remove — the "
            + "player whose character is losing the item cannot touch the window that decides it. "
            + "A line that says MAY OPERATE IT: NO beside 0 of 0 is a spectator and is not a fault. "
            + "One line per raise; the fall is not logged.");
    }
}
