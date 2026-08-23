using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;   // UIWindow — the game ships it in this namespace

namespace GloomhavenVR.Net;

/// <summary>
/// THE MAP ROOM'S SHARED WINDOWS — record <see cref="NetProtocol.ExtIdSharedWindow"/> (21): which
/// page of the campaign map's story every player in the 3D room has read to, and where the three
/// shared map windows stand.
///
/// <para>USER REQUEST (2026-08-22, verbatim): "d) Wenn eine Quest angeklickt wird das erscheinende
/// Fenster soll voll synchronisiert werden in der man die Quest bestätigen kann, genauso wie die
/// darauffolgendene Story-Fenster. Im Flat-Spiel sieht jeder seine eigne Story und kann in seiner
/// eigenen Geschwindigkeit durchklicken und der Geschichte zuhören - das soll hier nur für die
/// Spieler gelten die die 3D-Worldmap ausgeschaltet haben - aller anderen synchronsieren sich den
/// aktuellen Stand der Story. Klickt einer weiter ist es für alle im 3d-Worldmap-Raum
/// weitergeklickt worden." Plus, from request 3: "Die Position dieser Fenster sollen voll
/// synchronsiert werden, auch wenn es jemand woanders hinverschiebt."</para>
///
/// <para><b>WHY THIS IS NOT RECORD 19.</b> The map's narrative is <c>MapStoryController</c> — a
/// DIFFERENT <c>Singleton</c> from the scenario's <c>StoryController</c>, with its own
/// <c>UICharacterStoryBox</c> and its own <c>UIWindow</c>. <c>RemoteStorySync.Box()</c> resolves
/// <c>Singleton&lt;StoryController&gt;</c> and nothing else, so record 19 is INERT on the map and
/// always has been. The two controllers are also mutually exclusive in practice —
/// <c>StoryController</c> is hard-wired to <c>Choreographer</c>, <c>LevelEventsController</c> and
/// <c>PhaseManager.PhaseType == Scenario</c>, none of which exist on the campaign map — but this
/// class does not rely on that: it resolves the MAP singleton by instance and could not drive the
/// scenario box if both stood.</para>
///
/// <para><b>AND IT IS PARTLY A STALL FIX.</b> <c>MapChoreographer.CheckCampaignIntro</c> sets
/// <c>ActionProcessor.SetState(ActionProcessorStateType.Halted)</c> when online while the
/// Gloomhaven intro story stands, and its finish callback does NOT release it — the release comes
/// later, down the move chain. A player who leaves that box standing therefore stops processing the
/// shared action queue for everybody, which is the same class of stall record 19 exists to remove.
/// The release here is the same mechanism record 19 uses and nothing else: driving this client's
/// OWN box past its OWN last page runs the game's own
/// <c>ShowLine → Hide → onFinish → OnFinishShow → ShowNext</c> chain locally. <b>If the halt is not
/// released by that chain, the remedy is NOT to call <c>ActionProcessor.SetState</c> ourselves</b> —
/// that would be writing game state, which this project does not do.</para>
///
/// <para><b>THE QUEST WINDOW IS POSE ONLY.</b> Its CONTENT is already synced by the game itself:
/// the host's <c>SendGameAction(GameActionType.SelectQuest, ActionPhaseType.MapHQ, …)</c> reaches
/// <c>MapChoreographer.ProxySelectedLocation → UIMapMultiplayerController.ProxyHostSelectedLocation
/// → UIQuestPopupManager.ShowMultiplayerPreview</c>, which opens the client's own copy. A second
/// channel for that is forbidden, and the confirm click stays on the game's host-authoritative path
/// — no mod code may drive it.</para>
///
/// <para><b>THE ENCOUNTER IS KIND 3 AND IT IS POSE ONLY (ModBuild 231).</b> User, verbatim: <i>"Die
/// 'Begegnung' ist ein Storyfenster und soll wie das Storyfenster auch 'blau' sein also voll
/// synchronisiert sein."</i> The paragraph that stood here said the road/city event panel was
/// <b>deliberately not in this record</b> because <i>"its page advance is a real
/// <c>GameActionType.ContinueRoadEvent</c> the game already sends and receives. Syncing it would be
/// the forbidden second source of truth, and it is the single most likely mistake in this
/// area."</i> Every word of that is still true about its CONTENT and none of it was ever about its
/// POSE. Kind 3 carries <see cref="NetProtocol.StoryPageNone"/>, never the finished bit, and never
/// touches a button — a pose block and nothing else, exactly like kind 2, whose content the game
/// also already owns. The forbidden mistake is still forbidden, and it is now written into the send
/// path itself rather than into a paragraph (see the entry-3 block in <see cref="Sample"/>).</para>
///
/// <para>BOTH GATES ARE <c>MapRoomDriver.Active</c>. A client with the 3D map off writes no record
/// and applies none — it keeps the flat game's per-player pacing exactly as the request says, and
/// its packets stay byte-identical to ModBuild 221's. Opting into the room IS the consent for the
/// page to be driven.</para>
/// </summary>
internal static class RemoteMapStory
{
    private const string Scope = "Net";

    /// <summary>How long a peer's record stays believed after its last packet — record 19's number
    /// and record 19's reasoning (fifteen missed packets at 5 Hz).</summary>
    private const float PeerStaleSeconds = 3f;

    /// <summary>How long this client keeps announcing a map story it has FINISHED after its own box
    /// closed. Record 19's number and its reasoning: the client that most needs the statement is
    /// the one whose box opens LATE, and a minute covers the realistic load-skew spread between
    /// three PCs. Bounded rather than permanent so the idle packet goes back to being
    /// byte-identical, and so a re-shown message is never met with a stale "already done".</summary>
    private const float FinishedLingerSeconds = 60f;

    private const float MoveEpsilonMeters = 0.005f;
    private const float MoveEpsilonDegrees = 0.5f;
    private const float MoveEpsilonSize = 0.005f;

    /// <summary>How long the local pose must stand still before the move counts as FINISHED and the
    /// stamp is bumped — record 19's number, for record 19's reason: a drag is continuous, and
    /// bumping per frame would make "last mover" mean "last frame" and let two people fight at
    /// 5 Hz.</summary>
    private const float MoveSettleSeconds = 0.25f;

    private const float NoteThrottleSeconds = 5f;

    // ---- per-peer state ----------------------------------------------------------------------

    private readonly struct PeerEntry
    {
        public PeerEntry(in SharedWindowEntry e, float at)
        {
            Flags = e.Flags;
            Page = e.Page;
            PageCount = e.PageCount;
            ContentKey = e.ContentKey;
            PoseStamp = e.PoseStamp;
            SizeCode = e.SizeCode;
            Frame = e.Frame;
            Pose = e.Pose;
            At = at;
        }

        public readonly byte Flags;
        public readonly byte Page;
        public readonly byte PageCount;
        public readonly uint ContentKey;
        public readonly byte PoseStamp;
        public readonly byte SizeCode;
        public readonly byte Frame;
        public readonly RigPose Pose;
        public readonly float At;

        public bool Open => (Flags & NetProtocol.SharedOpenBit) != 0;
        public bool Finished => (Flags & NetProtocol.SharedFinishedBit) != 0;
        public bool HasPose => (Flags & NetProtocol.SharedPoseBit) != 0;
        public int PageIndex => Page == NetProtocol.StoryPageNone ? -1 : Page;
    }

    /// <summary>Peer id → that peer's entry for one kind. One dictionary per kind rather than a
    /// dictionary of arrays: two kinds is not enough to be worth indirection, and a missing entry
    /// then means exactly "that peer's window of this kind is down".</summary>
    private static readonly Dictionary<int, PeerEntry> StoryPeers = new();

    /// <inheritdoc cref="StoryPeers"/>
    private static readonly Dictionary<int, PeerEntry> QuestPeers = new();

    /// <inheritdoc cref="StoryPeers"/>
    private static readonly Dictionary<int, PeerEntry> EncounterPeers = new();

    /// <summary>Per-peer, per-kind last-mover clock: the stamp we last saw and when it CHANGED. A
    /// peer re-sending the same stamp five times a second is not moving anything and must not keep
    /// winning the election.</summary>
    private static readonly Dictionary<int, byte> StoryStamp = new();
    private static readonly Dictionary<int, float> StoryStampAt = new();
    private static readonly Dictionary<int, byte> QuestStamp = new();
    private static readonly Dictionary<int, float> QuestStampAt = new();
    private static readonly Dictionary<int, byte> EncounterStamp = new();
    private static readonly Dictionary<int, float> EncounterStampAt = new();

    private static readonly List<int> Scratch = new(4);

    // ---- local state, one block per kind ------------------------------------------------------

    /// <summary>The local half of one shared window: what it is showing, whether a hand here has
    /// moved it, and the wire stamp that says so.</summary>
    private sealed class Local
    {
        internal uint Key;
        internal byte PageCount;
        internal float FinishedUntil;

        internal Vector3 FramePos;
        internal Quaternion FrameRot = Quaternion.identity;
        internal float FrameSize = 1f;
        internal bool HaveBaseline;

        internal bool PoseOwned;
        internal bool Moving;
        internal float MoveSettleAt;
        internal byte PoseStamp;

        internal int FollowingPeer;
        internal byte FollowedStamp;
        internal bool FollowedStampValid;

        internal void ForgetPose()
        {
            HaveBaseline = false;
            PoseOwned = false;
            Moving = false;
            FollowingPeer = 0;
            FollowedStampValid = false;
        }

        internal void Reset()
        {
            Key = 0u;
            PageCount = 0;
            FinishedUntil = 0f;
            FramePos = Vector3.zero;
            FrameRot = Quaternion.identity;
            FrameSize = 1f;
            PoseStamp = 0;
            FollowedStamp = 0;
            MoveSettleAt = 0f;
            ForgetPose();
        }
    }

    private static readonly Local StoryLocal = new();
    private static readonly Local QuestLocal = new();
    private static readonly Local EncounterLocal = new();

    /// <summary>The persistent send buffer. Allocated once so the 5 Hz write path stays free of
    /// garbage, exactly as the board-tuning sampler's buffer is.</summary>
    private static readonly SharedWindowEntry[] SendBuffer =
        new SharedWindowEntry[NetProtocol.SharedWindowMaxEntries];

    /// <summary>What the last packet carried, so <see cref="SendDue"/> can compare live state
    /// against it rather than against a guess.</summary>
    private static bool _sentValid;
    private static int _sentStoryPage = int.MinValue;
    private static bool _sentStoryFinished;
    private static bool _sentQuestOpen;
    private static bool _sentEncounterOpen;

    private static string _lastNote = string.Empty;
    private static float _nextNoteAt;

    /// <summary>Drop everything on session end / shutdown. Nothing here owns a GameObject, so this
    /// IS the teardown.</summary>
    internal static void Reset()
    {
        StoryPeers.Clear();
        QuestPeers.Clear();
        EncounterPeers.Clear();
        StoryStamp.Clear();
        StoryStampAt.Clear();
        QuestStamp.Clear();
        QuestStampAt.Clear();
        EncounterStamp.Clear();
        EncounterStampAt.Clear();
        StoryLocal.Reset();
        QuestLocal.Reset();
        EncounterLocal.Reset();
        _sentValid = false;
        _sentStoryPage = int.MinValue;
        _sentStoryFinished = false;
        _sentQuestOpen = false;
        _sentEncounterOpen = false;
        _lastNote = string.Empty;
        _nextNoteAt = 0f;
    }

    /// <summary>
    /// Whether the extras packet must go out NOW rather than on its own cadence.
    ///
    /// <para>THE PAGE AND THE FINISHED BIT PRE-EMPT, and record 19's do NOT. That is a deliberate
    /// difference, not a silent divergence: for record 19 the statement is a lock release and
    /// 200 ms of cadence latency was acceptable, while here the feature IS the latency — <i>"Klickt
    /// einer weiter ist es für alle im 3d-Worldmap-Raum weitergeklickt worden"</i> is judged by
    /// whether the other player's page turns when yours does. The QUEST window's open/close edge
    /// pre-empts for the same reason. The POSE deliberately does not: a drag produces a new pose
    /// every frame and rides the capped idiom like every other dragged thing.</para>
    ///
    /// <para>PURE: reads live state, compares against what was last SENT, mutates nothing. The rate
    /// gate evaluates this every frame and <see cref="Sample"/> only runs on the frames it lets
    /// through.</para>
    /// </summary>
    internal static bool SendDue
    {
        get
        {
            if (!MapRoomDriver.Active)
                return false;
            UICharacterStoryBox? box = MapBox();
            int page = box != null ? box.currentDialogIndex : int.MinValue;
            bool finished = box == null && StoryLocal.Key != 0u
                            && Time.unscaledTime < StoryLocal.FinishedUntil;
            bool questOpen = QuestPopup() != null;
            // The encounter's OPEN EDGE pre-empts for the same reason the quest window's does: the
            // record is what makes a peer's blue bar and pose apply to it at all, and a window that
            // appears is worth one packet. Its CONTENT never pre-empts anything here because this
            // record never carries it.
            bool encounterOpen = EventPanel() != null;
            if (!_sentValid)
                return page != int.MinValue || finished || questOpen || encounterOpen;
            return page != _sentStoryPage || finished != _sentStoryFinished
                   || questOpen != _sentQuestOpen || encounterOpen != _sentEncounterOpen;
        }
    }

    // ---- the two windows ---------------------------------------------------------------------

    /// <summary>The live MAP story box, or null when the campaign map has no narrative on screen.
    /// Never throws and never creates anything. <c>MapStoryController.dialogBox</c> and
    /// <c>.window</c> are private <c>[SerializeField]</c>s reached as ordinary members because the
    /// build PUBLICIZES the game assemblies — the same route <c>RemoteStorySync</c> already takes to
    /// <c>UICharacterStoryBox.ShowLine</c>.</summary>
    private static UICharacterStoryBox? MapBox()
    {
        if (!Singleton<MapStoryController>.IsInitialized)
            return null;
        MapStoryController mc = Singleton<MapStoryController>.Instance;
        if (mc == null || mc.window == null)
            return null;
        if (!mc.window.IsOpen && !mc.window.IsVisible)
            return null;
        return mc.dialogBox;
    }

    /// <summary>The FLOATED quest-confirm popup, or null. Asked of the float set rather than of the
    /// game, because a window can be open and not floated and this record only ever describes a
    /// window that exists in world space.</summary>
    private static UIQuestPopup? QuestPopup()
    {
        UIWindow? w = ModalFallback.FloatedWindowWithId(UIWindowID.QuestPopup);
        return w != null ? w.GetComponent<UIQuestPopup>() : null;
    }

    /// <summary>
    /// The quest window's content key: <c>FNV-1a</c> of the quest's own localization key.
    ///
    /// <para>A LOCALIZATION KEY, never a translated title — two players in different languages must
    /// compute the same value. It is read off the popup's own <c>quest</c> field, so the host's copy
    /// (<c>selectedQuestPopup</c>) and the client's copy (<c>multiplayerQuestPopup</c>) key
    /// identically: both wrap the same <c>CQuestState</c>.</para>
    /// </summary>
    private static uint QuestKey(UIQuestPopup? popup)
    {
        if (popup == null)
            return 0u;
        try
        {
            Assets.Script.GUI.Quest.IQuest? q = popup.quest;
            return q != null ? NetProtocol.HashMapKey(q.LocalisedNameKey) : 0u;
        }
        catch (System.Exception)
        {
            return 0u;
        }
    }

    /// <summary>The FLOATED encounter panel ("Begegnung"), or null. Resolved through
    /// <see cref="SharedWindows.WindowOf"/> — i.e. through <c>Singleton&lt;UIEventPanel&gt;</c> and
    /// its <c>[RequireComponent(typeof(UIWindow))]</c> pairing, never by name or id — and then asked
    /// of the FLOAT SET for the same reason <see cref="QuestPopup"/> is: this record only ever
    /// describes a window that exists in world space, and a window that is open but not converted
    /// has no pose to publish and nowhere to apply one.</summary>
    private static UIEventPanel? EventPanel()
    {
        UIWindow? w = SharedWindows.WindowOf(SharedWindowKind.Encounter);
        if (w == null || !w.IsOpen)
            return null;
        if (!ModalFallback.TryGetGrabFor(w, out GrabbableModal? grab) || grab == null)
            return null;
        return w.GetComponent<UIEventPanel>();
    }

    /// <summary>
    /// The encounter's content key: <c>FNV-1a</c> of the road event's own <c>ID</c>.
    ///
    /// <para>THE ID AND NEVER A TRANSLATED STRING — two players in different languages must compute
    /// the same value, and <c>CRoadEvent.ID</c> is the yml key the event is loaded under
    /// (decompiled/MapRuleLibrary/MapRuleLibrary.YML.Events/CRoadEvent.cs:9), identical on every
    /// client that loaded the same content. A receiver holding a DIFFERENT event ignores the entry,
    /// which is record 19's discipline verbatim: better to leave a window where it is than to move
    /// it to where a different window stands on somebody else's table.</para>
    /// </summary>
    private static uint EncounterKey(UIEventPanel? panel)
    {
        if (panel == null)
            return 0u;
        try
        {
            MapRuleLibrary.YML.Events.CRoadEvent? ev = panel.eventData;
            string? id = ev != null ? ev.ID : null;
            return string.IsNullOrEmpty(id) ? 0u : NetProtocol.HashMapKey(id!);
        }
        catch (System.Exception)
        {
            return 0u;
        }
    }

    // ---- send side ---------------------------------------------------------------------------

    /// <summary>
    /// Fill the shared-window fields of the outgoing extras packet. Leaves
    /// <see cref="PresenceState.HasSharedWindow"/> false — and therefore the whole record absent —
    /// whenever this client's own 3D map room is not standing or neither window is up.
    /// </summary>
    internal static void Sample(ref PresenceState extras)
    {
        if (!MapRoomDriver.Active)
        {
            // The room is down. Forget the local halves so re-entering does not publish a pose
            // stamp nobody made, and fall silent — the whole record absent, the packet unchanged.
            StoryLocal.Reset();
            QuestLocal.Reset();
            _sentValid = false;
            return;
        }

        float now = Time.unscaledTime;
        int n = 0;

        // ---- entry 1: the map story box ------------------------------------------------------
        UICharacterStoryBox? box = MapBox();
        if (box == null)
        {
            TrackFrame(SharedWindowKind.MapStory, StoryLocal, reset: true);
            if (StoryLocal.Key != 0u && now < StoryLocal.FinishedUntil)
            {
                // The box is down here and we clicked it through: keep saying so for a bounded
                // while, so the statement that unlocks a peer survives packet loss.
                SendBuffer[n] = default;
                SendBuffer[n].Kind = NetProtocol.SharedWindowKindMapStory;
                SendBuffer[n].Flags = NetProtocol.SharedFinishedBit;
                SendBuffer[n].Page = NetProtocol.EncodeStoryPage(StoryLocal.PageCount);
                SendBuffer[n].PageCount = StoryLocal.PageCount;
                SendBuffer[n].ContentKey = StoryLocal.Key;
                n++;
            }
            else
            {
                StoryLocal.FinishedUntil = 0f;
                StoryLocal.Key = 0u;
            }
        }
        else
        {
            List<DialogLineDTO>? pages = box.dialogs;
            uint key = RemoteStorySync.HashDialog(pages);
            if (key != 0u)
            {
                if (key != StoryLocal.Key)
                    StoryLocal.ForgetPose(); // pose ownership is PER OPEN
                StoryLocal.Key = key;
                StoryLocal.PageCount = (byte)Mathf.Clamp(pages!.Count, 0, NetProtocol.StoryPageMax);
                StoryLocal.FinishedUntil = now + FinishedLingerSeconds;

                SendBuffer[n] = default;
                SendBuffer[n].Kind = NetProtocol.SharedWindowKindMapStory;
                SendBuffer[n].Flags = NetProtocol.SharedOpenBit;
                SendBuffer[n].Page = NetProtocol.EncodeStoryPage(box.currentDialogIndex);
                SendBuffer[n].PageCount = StoryLocal.PageCount;
                SendBuffer[n].ContentKey = key;
                TrackFrame(SharedWindowKind.MapStory, StoryLocal, reset: false);
                WritePose(SharedWindowKind.MapStory, StoryLocal, ref SendBuffer[n]);
                n++;
            }
        }

        // ---- entry 2: the quest-confirm window (POSE ONLY) -----------------------------------
        UIQuestPopup? popup = QuestPopup();
        if (popup == null)
        {
            TrackFrame(SharedWindowKind.QuestConfirm, QuestLocal, reset: true);
            QuestLocal.Key = 0u;
        }
        else if (n < NetProtocol.SharedWindowMaxEntries)
        {
            uint key = QuestKey(popup);
            if (key != QuestLocal.Key)
                QuestLocal.ForgetPose();
            QuestLocal.Key = key;

            SendBuffer[n] = default;
            SendBuffer[n].Kind = NetProtocol.SharedWindowKindQuestConfirm;
            SendBuffer[n].Flags = NetProtocol.SharedOpenBit;
            // The quest window has no pages, and saying so explicitly is what stops a receiver
            // from ever running the page arbitration on it.
            SendBuffer[n].Page = NetProtocol.StoryPageNone;
            SendBuffer[n].PageCount = 0;
            SendBuffer[n].ContentKey = key;
            TrackFrame(SharedWindowKind.QuestConfirm, QuestLocal, reset: false);
            WritePose(SharedWindowKind.QuestConfirm, QuestLocal, ref SendBuffer[n]);
            n++;
        }

        // ---- entry 3: the ENCOUNTER, "Begegnung" (POSE ONLY) ---------------------------------
        //
        // WHAT THIS BLOCK MAY NOT DO, written here rather than in a doc paragraph because a
        // paragraph is what failed last time: it may not carry a PAGE, it may not carry the
        // FINISHED bit, and no code anywhere may click this window's buttons. The encounter's own
        // advance is a GAME ACTION the game already sends and receives
        // (Synchronizer.SendGameAction(GameActionType.ContinueRoadEvent, ActionPhaseType.MapEvent,
        // …) at decompiled/GH.Runtime/UIEventPanel.cs:606/610/724 → ClientContinueRoadEvent :869).
        // A second channel for it is forbidden. What travels here is the POSE — the half the mod
        // owns and the half the game has no opinion about.
        UIEventPanel? evPanel = EventPanel();
        if (evPanel == null)
        {
            TrackFrame(SharedWindowKind.Encounter, EncounterLocal, reset: true);
            EncounterLocal.Key = 0u;
        }
        else if (n < NetProtocol.SharedWindowMaxEntries)
        {
            uint key = EncounterKey(evPanel);
            if (key != EncounterLocal.Key)
                EncounterLocal.ForgetPose();
            EncounterLocal.Key = key;

            SendBuffer[n] = default;
            SendBuffer[n].Kind = NetProtocol.SharedWindowKindEncounter;
            SendBuffer[n].Flags = NetProtocol.SharedOpenBit;
            SendBuffer[n].Page = NetProtocol.StoryPageNone;
            SendBuffer[n].PageCount = 0;
            SendBuffer[n].ContentKey = key;
            TrackFrame(SharedWindowKind.Encounter, EncounterLocal, reset: false);
            WritePose(SharedWindowKind.Encounter, EncounterLocal, ref SendBuffer[n]);
            n++;
        }

        _sentValid = true;
        _sentStoryPage = box != null ? box.currentDialogIndex : int.MinValue;
        _sentStoryFinished = box == null && StoryLocal.Key != 0u && now < StoryLocal.FinishedUntil;
        _sentQuestOpen = popup != null;
        _sentEncounterOpen = evPanel != null;

        if (n == 0)
            return;   // nothing to say: the record is absent and the packet is unchanged
        extras.HasSharedWindow = true;
        extras.SharedWindowCount = n;
        extras.SharedWindowEntries = SendBuffer;
    }

    /// <summary>
    /// Attach this client's pose for one shared window to its record-21 entry, when there is one
    /// worth attaching.
    ///
    /// <para><b>THE DRAG ITSELF TRAVELS, NOT ONLY ITS RESULT (ModBuild 226).</b> The user, verbatim:
    /// "Die Bewegungen der 'blauen' MP-Fenster ... sollen auch die BEWEGUNG und die Position voll
    /// übertragen (flüssig, wie bei der Position des Boards auch)". Until this build the gate here
    /// was <c>PoseOwned</c> alone, and <see cref="TrackFrame"/> only sets that after
    /// <see cref="MoveSettleSeconds"/> of STILLNESS — so a window being dragged for the first time
    /// published NOTHING at all while it moved, and its whole journey arrived as one jump at the
    /// end. No amount of receiver-side easing can invent the frames in between; a receiver that
    /// eases toward a single endpoint is smooth and still wrong, because it never saw the path.</para>
    ///
    /// <para><c>Moving</c> is therefore a second reason to publish. It is deliberately NOT a reason
    /// to bump <see cref="Local.PoseStamp"/>: the stamp elects the room's LAST MOVER, and bumping it
    /// per packet would make "last mover" mean "last packet" and let two draggers trade the window
    /// at the send rate — the very thing <see cref="MoveSettleSeconds"/>' own comment forbids. So a
    /// mid-drag entry carries a FRESH POSE under an UNCHANGED STAMP, and
    /// <see cref="ResolvePose"/> is what had to learn the difference: it elects on the stamp and
    /// decides whether to apply on the pose VALUE.</para>
    /// </summary>
    private static void WritePose(SharedWindowKind kind, Local local, ref SharedWindowEntry entry)
    {
        if (!local.PoseOwned && !local.Moving)
            return;
        if (!SharedWindows.TryGetGrab(kind, out GrabbableModal? grab) || grab == null)
            return;
        if (!TryReadFrame(grab, out Vector3 pos, out Quaternion rot, out float size))
            return;
        if (!ToShared(pos, rot, out Vector3 local3, out Quaternion localRot, out byte frame))
            return;
        entry.Flags |= NetProtocol.SharedPoseBit;
        entry.PoseStamp = local.PoseStamp;
        entry.SizeCode = NetProtocol.EncodeStorySize(size);
        entry.Frame = frame;
        entry.Pose = new RigPose { Position = local3, Rotation = localRot };
    }

    /// <summary>
    /// Watch one shared window's grab frame for a move the LOCAL user made, and bump its stamp once
    /// when that move settles.
    ///
    /// <para>Record 19's mechanism verbatim, and it needs no "is grabbed" flag for the same reason:
    /// the frame is written by exactly three things — the grab handle, ModalFallback's own
    /// placement, and this class's own apply — and the latter two both record their result into the
    /// baseline as they happen. Anything left over is a hand.</para>
    /// </summary>
    private static void TrackFrame(SharedWindowKind kind, Local local, bool reset)
    {
        if (reset || !SharedWindows.TryGetGrab(kind, out GrabbableModal? grab) || grab == null
            || !TryReadFrame(grab, out Vector3 pos, out Quaternion rot, out float size))
        {
            local.HaveBaseline = false;
            local.Moving = false;
            return;
        }

        if (!local.HaveBaseline)
        {
            local.FramePos = pos;
            local.FrameRot = rot;
            local.FrameSize = size;
            local.HaveBaseline = true;
            return;
        }

        float eps = MoveEpsilonMeters * Mathf.Max(PanelLayout.WorldScale, 0.01f);
        bool moved = (pos - local.FramePos).sqrMagnitude > eps * eps
                     || Quaternion.Angle(rot, local.FrameRot) > MoveEpsilonDegrees
                     || Mathf.Abs(size - local.FrameSize) > MoveEpsilonSize;

        float now = Time.unscaledTime;
        if (moved)
        {
            local.FramePos = pos;
            local.FrameRot = rot;
            local.FrameSize = size;
            local.Moving = true;
            local.MoveSettleAt = now + MoveSettleSeconds;
            // A hand owns it right now: stop following anybody. Never yank a panel out of a hand,
            // and never fight a hand at 5 Hz.
            local.FollowingPeer = 0;
            local.FollowedStampValid = false;
            return;
        }
        if (!local.Moving || now < local.MoveSettleAt)
            return;
        local.Moving = false;
        local.PoseOwned = true;
        unchecked { local.PoseStamp++; }
        VRLog.Info(Scope, $"MAP ROOM shared window MOVED locally: {kind} — pose stamp "
                          + $"{local.PoseStamp}, record 21 starts carrying a pose block for it and "
                          + "this client becomes the LAST MOVER everyone in the room follows. The "
                          + "pose travels PARCHMENT-LOCAL (frame 1): origin the parchment's own "
                          + "world bounds centre, unit the room's derived seat scale — both pure "
                          + "functions of the SAME shared map, so 'where you put it' means the same "
                          + "physical place on every table. Record 19's seat-anchor frame is NOT "
                          + "used here: its origin is the orbit camera's focal point, which every "
                          + "player pans for themselves.");
    }

    private static bool TryReadFrame(GrabbableModal grab, out Vector3 pos, out Quaternion rot,
                                     out float size)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;
        size = 1f;
        var owner = (IPanelGrabOwner)grab;
        if (!owner.GrabVisible)
            return false;
        Transform? frame = owner.GrabRoot;
        if (frame == null)
            return false;
        pos = frame.position;
        rot = frame.rotation;
        size = Mathf.Clamp(frame.localScale.x, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);
        return true;
    }

    // ---- the shared frame ---------------------------------------------------------------------

    /// <summary>
    /// World pose → the frame the record travels in, and WHICH frame that was.
    ///
    /// <para>Frame 1 (PARCHMENT-LOCAL) whenever the map room can measure its parchment, which is
    /// the whole life of the room; frame 0 (record 19's seat anchor) only as the fallback for the
    /// frames in which it cannot. Writing the frame down as a BYTE is what makes that fallback
    /// safe — a receiver decodes with the frame the sender used, not with the one it assumes.</para>
    /// </summary>
    private static bool ToShared(Vector3 worldPos, Quaternion worldRot,
                                 out Vector3 localPos, out Quaternion localRot, out byte frame)
    {
        localPos = Vector3.zero;
        localRot = Quaternion.identity;
        frame = NetProtocol.SharedFrameParchment;
        if (MapRoomDriver.TryGetParchmentFrame(out Vector3 center, out float scale))
        {
            localPos = (worldPos - center) / Mathf.Max(scale, 0.0001f);
            // Rotation travels as an ABSOLUTE world rotation: world axes are already shared (the
            // mod's avatar poses have always travelled that way), so there is nothing to rotate
            // out of and nothing per-client to rotate back in.
            localRot = worldRot;
            return true;
        }
        frame = NetProtocol.SharedFrameSeatAnchor;
        if (!PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion yaw))
            return false;
        float s = Mathf.Max(PanelLayout.WorldScale, 0.01f);
        localPos = Quaternion.Inverse(yaw) * (worldPos - anchor) / s;
        localRot = Quaternion.Inverse(yaw) * worldRot;
        return true;
    }

    /// <summary>The inverse of <see cref="ToShared"/>, decoded in the frame the SENDER named. An
    /// unknown frame never reaches here — <c>PresenceSerializer.TryRead</c> drops the pose block and
    /// keeps the page, which is the fail-closed direction.</summary>
    private static bool ToWorld(byte frame, Vector3 localPos, Quaternion localRot,
                                out Vector3 worldPos, out Quaternion worldRot)
    {
        worldPos = Vector3.zero;
        worldRot = Quaternion.identity;
        if (frame == NetProtocol.SharedFrameParchment)
        {
            if (!MapRoomDriver.TryGetParchmentFrame(out Vector3 center, out float scale))
                return false;
            worldPos = center + localPos * scale;
            worldRot = localRot;
            return true;
        }
        if (frame != NetProtocol.SharedFrameSeatAnchor)
            return false;
        if (!PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion yaw))
            return false;
        float s = Mathf.Max(PanelLayout.WorldScale, 0.01f);
        worldPos = anchor + yaw * (localPos * s);
        worldRot = yaw * localRot;
        return true;
    }

    // ---- receive side ---------------------------------------------------------------------

    /// <summary>Take one peer's freshly parsed extras packet. Pure bookkeeping — nothing is driven
    /// here, so a burst of packets in one frame costs one apply, not one per packet.</summary>
    internal static void Observe(int senderId, in PresenceState p)
    {
        if (senderId <= 0)
            return;
        bool sawStory = false;
        bool sawQuest = false;
        bool sawEncounter = false;
        if (p.HasSharedWindow && p.SharedWindowEntries != null)
        {
            float now = Time.unscaledTime;
            int n = p.SharedWindowCount;
            if (n > p.SharedWindowEntries.Length)
                n = p.SharedWindowEntries.Length;
            for (int i = 0; i < n; i++)
            {
                SharedWindowEntry e = p.SharedWindowEntries[i];
                switch (e.Kind)
                {
                    case NetProtocol.SharedWindowKindMapStory:
                        sawStory = true;
                        StoryPeers[senderId] = new PeerEntry(in e, now);
                        NoteStamp(senderId, in e, StoryStamp, StoryStampAt, now);
                        break;
                    case NetProtocol.SharedWindowKindQuestConfirm:
                        sawQuest = true;
                        QuestPeers[senderId] = new PeerEntry(in e, now);
                        NoteStamp(senderId, in e, QuestStamp, QuestStampAt, now);
                        break;
                    case NetProtocol.SharedWindowKindEncounter:
                        sawEncounter = true;
                        EncounterPeers[senderId] = new PeerEntry(in e, now);
                        NoteStamp(senderId, in e, EncounterStamp, EncounterStampAt, now);
                        break;
                    default:
                        // A kind this build does not know. The parser has already stepped over its
                        // bytes; there is nothing to do here but leave it alone.
                        break;
                }
            }
        }
        // Absence of a KIND is a defined state — that peer's window of that kind is down — so it
        // must forget the entry rather than keep the last one.
        if (!sawStory)
            Forget(senderId, StoryPeers, StoryStamp, StoryStampAt);
        if (!sawQuest)
            Forget(senderId, QuestPeers, QuestStamp, QuestStampAt);
        if (!sawEncounter)
            Forget(senderId, EncounterPeers, EncounterStamp, EncounterStampAt);
    }

    private static void NoteStamp(int senderId, in SharedWindowEntry e,
                                  Dictionary<int, byte> stamps, Dictionary<int, float> at, float now)
    {
        if ((e.Flags & NetProtocol.SharedPoseBit) == 0)
        {
            stamps.Remove(senderId);
            at.Remove(senderId);
            return;
        }
        // The LAST-MOVER clock ticks on a CHANGED stamp only.
        if (!stamps.TryGetValue(senderId, out byte had) || had != e.PoseStamp)
        {
            stamps[senderId] = e.PoseStamp;
            at[senderId] = now;
        }
    }

    private static void Forget(int senderId, Dictionary<int, PeerEntry> peers,
                               Dictionary<int, byte> stamps, Dictionary<int, float> at)
    {
        peers.Remove(senderId);
        stamps.Remove(senderId);
        at.Remove(senderId);
    }

    /// <summary>
    /// Apply whatever the room has agreed on to this client's own map windows. Called once per
    /// frame, not once per packet: two peers publishing page 3 in the same frame must drive the
    /// local box once.
    ///
    /// <para>THE PAGE HALF NEVER CONSULTS A WINDOW AND THE POSE HALF NEVER GATES THE PAGE. That
    /// separation is record 19's, and it is what guarantees an unplaceable window can never hold
    /// anybody's story up.</para>
    /// </summary>
    internal static void Resolve()
    {
        PruneStale();
        if (!MapRoomDriver.Active)
        {
            // The 3D map is off here: this player keeps the flat game's own per-player pacing,
            // exactly as the request scopes it. Nothing is driven, nothing is placed.
            StoryLocal.FollowingPeer = 0;
            StoryLocal.FollowedStampValid = false;
            QuestLocal.FollowingPeer = 0;
            QuestLocal.FollowedStampValid = false;
            EncounterLocal.FollowingPeer = 0;
            EncounterLocal.FollowedStampValid = false;
            return;
        }

        UICharacterStoryBox? box = MapBox();
        if (box == null)
        {
            StoryLocal.FollowingPeer = 0;
            StoryLocal.FollowedStampValid = false;
        }
        else
        {
            uint key = RemoteStorySync.HashDialog(box.dialogs);
            if (key != 0u)
            {
                ResolveStoryPage(box, key);
                ResolvePose(SharedWindowKind.MapStory, StoryLocal, key, StoryPeers, StoryStampAt);
            }
        }

        UIQuestPopup? popup = QuestPopup();
        if (popup == null)
        {
            QuestLocal.FollowingPeer = 0;
            QuestLocal.FollowedStampValid = false;
        }
        else
        {
            ResolvePose(SharedWindowKind.QuestConfirm, QuestLocal, QuestKey(popup), QuestPeers,
                        QuestStampAt);
        }

        // The encounter: POSE ONLY. There is deliberately no page arm here at all — not a disabled
        // one, not a guarded one. Nothing in this class may advance a road event.
        UIEventPanel? evPanel = EventPanel();
        if (evPanel == null)
        {
            EncounterLocal.FollowingPeer = 0;
            EncounterLocal.FollowedStampValid = false;
            return;
        }
        ResolvePose(SharedWindowKind.Encounter, EncounterLocal, EncounterKey(evPanel),
                    EncounterPeers, EncounterStampAt);
    }

    private static void PruneStale()
    {
        PruneStale(StoryPeers, StoryStamp, StoryStampAt);
        PruneStale(QuestPeers, QuestStamp, QuestStampAt);
        PruneStale(EncounterPeers, EncounterStamp, EncounterStampAt);
    }

    private static void PruneStale(Dictionary<int, PeerEntry> peers, Dictionary<int, byte> stamps,
                                   Dictionary<int, float> at)
    {
        if (peers.Count == 0)
            return;
        float now = Time.unscaledTime;
        Scratch.Clear();
        foreach (KeyValuePair<int, PeerEntry> kv in peers)
        {
            if (now - kv.Value.At > PeerStaleSeconds)
                Scratch.Add(kv.Key);
        }
        for (int i = 0; i < Scratch.Count; i++)
            Forget(Scratch[i], peers, stamps, at);
        Scratch.Clear();
    }

    // ---- 2d: the page ------------------------------------------------------------------------

    private static void ResolveStoryPage(UICharacterStoryBox box, uint key)
    {
        int localPage = box.currentDialogIndex;
        int localCount = box.dialogs?.Count ?? 0;

        int remotePage = -1;
        bool remoteFinished = false;
        int furthestPeer = 0;
        int finishedPeer = 0;
        int matching = 0;
        foreach (KeyValuePair<int, PeerEntry> kv in StoryPeers)
        {
            PeerEntry s = kv.Value;
            if (s.ContentKey != key)
                continue;
            matching++;
            if (s.PageIndex > remotePage)
            {
                remotePage = s.PageIndex;
                furthestPeer = kv.Key;
            }
            if (s.Finished && finishedPeer == 0)
            {
                remoteFinished = true;
                finishedPeer = kv.Key;
            }
        }

        // REUSED VERBATIM, never re-derived: this pure function's three properties — idempotence,
        // ordering and no-skipping — are exactly what a shared page needs, and they are already
        // pinned by the wire tests. A second copy of them is how the two would drift apart.
        int target = NetProtocol.ResolveStoryPage(localPage, localCount, remotePage, remoteFinished);
        if (target < 0)
        {
            if (matching == 0 && StoryPeers.Count > 0)
                Note($"{StoryPeers.Count} peer(s) published a MAP story record but none for THIS "
                     + $"message (local key 0x{key:X8}) — they are holding a different one, so "
                     + "nothing here may be advanced");
            return;
        }
        if (localPage < 0)
        {
            // The first line has not been painted yet: the box is still running its open animation
            // and the animator's own OnAnimationFinished will call ShowLine(0) afterwards. Driving
            // a page now would be undone — and undone BACKWARDS — a fraction of a second later.
            Note($"page {target} is wanted but this map box has not shown its first line yet "
                 + "(currentDialogIndex −1, the open animation is still running) — the animator's "
                 + "own ShowLine(0) must land first or it would drag the message backwards");
            return;
        }
        // THERE IS NO MAP EQUIVALENT OF RECORD 19'S DISPLAY-DELAY GUARD, and its absence is
        // MEASURED rather than assumed: MapStoryController declares its own static
        // DisplayDelayInEffect and, by a tree-wide grep of the decompiled sources, NOTHING reads or
        // writes it — the only live members of that name belong to StoryController and
        // LevelMessagesUIHandler. The guard that matters is the one above (currentDialogIndex < 0
        // means the box has not painted its first line), and that one is honoured.

        string who = remoteFinished
            ? $"player {finishedPeer} clicked THROUGH the end"
            : $"player {furthestPeer} advanced to page {remotePage}";

        // THE SEAM. ShowLine takes an ABSOLUTE index and is what the game's own click path calls
        // (Skip → ShowNextLine → ShowLine(currentDialogIndex + 1)). Driving it directly rather than
        // replaying clicks is what makes this idempotent and late-joiner safe, and it sidesteps
        // skipButton.interactable being false mid-animation — a synthetic click would simply be
        // dropped there. At or past the page count it calls Hide(), which runs the game's own close
        // chain: Hide → onFinish → MapStoryController.OnFinishShow → ShowNext, and THAT chain is
        // what performs the OnMapMessageShown state write every client already performs for itself
        // when it clicks its own copy through, and what lets the map flow that halted the
        // ActionProcessor proceed. Nothing of ours is sent and nothing of the game is written.
        box.ShowLine(target);

        bool terminal = target >= localCount;
        VRLog.Info(Scope, $"MAP ROOM story APPLIED (record 21, kind MapStory): message key "
                          + $"0x{key:X8}, {localCount} page(s) here, moved from page {localPage} to "
                          + $"{target}{(terminal ? " (past the last page → the box closes)" : "")} — "
                          + $"{who}. The record carries the ABSOLUTE page, so two players clicking "
                          + "at once both publish the same number and no page is skipped; "
                          + "re-applying it is a no-op. THIS IS A DIFFERENT CONTROLLER FROM RECORD "
                          + "19's: MapStoryController, which record 19 has never been able to see."
                          + (terminal
                              ? " This client now runs the game's OWN ShowLine → Hide → OnFinishShow "
                                + "→ ShowNext chain. For the Gloomhaven intro that also matters "
                                + "beyond comfort: MapChoreographer.CheckCampaignIntro HALTS this "
                                + "client's ActionProcessor while that box stands, so a player who "
                                + "walked away was stalling the party."
                              : " MEASURED here: the local page index before and after. ASSUMED: "
                                + "that the peer is reading the same text — the message key is what "
                                + "tests that, and a mismatch ignores the record instead of guessing."));
    }

    // ---- §6: the pose ------------------------------------------------------------------------

    private static void ResolvePose(SharedWindowKind kind, Local local, uint key,
                                    Dictionary<int, PeerEntry> peers, Dictionary<int, float> at)
    {
        if (local.Moving)
            return; // a hand owns it right now; never fight a hand.

        // LAST MOVER WINS. Among peers holding the same content and publishing a pose, follow the
        // one whose stamp changed most recently.
        int bestPeer = 0;
        float bestAt = float.NegativeInfinity;
        foreach (KeyValuePair<int, PeerEntry> kv in peers)
        {
            PeerEntry s = kv.Value;
            if (s.ContentKey != key || !s.HasPose)
                continue;
            if (!at.TryGetValue(kv.Key, out float when))
                continue;
            if (when > bestAt)
            {
                bestAt = when;
                bestPeer = kv.Key;
            }
        }
        if (bestPeer == 0)
            return;
        if (local.PoseOwned && bestAt < local.MoveSettleAt)
            return; // our own move is the newer one

        PeerEntry owner = peers[bestPeer];

        // ELECT ON THE STAMP, DECIDE ON THE POSE (ModBuild 226). Until this build the early-out here
        // was `FollowedStamp == owner.PoseStamp`, which was exactly right while a pose block only
        // ever existed for a FINISHED move: same stamp meant same pose meant nothing to do. Now that
        // WritePose also publishes DURING a drag, the stamp deliberately does not change while the
        // window is moving — so that test would have dropped every mid-drag pose and left the
        // receiver with the same single end-of-drag jump it had before, merely eased. The stamp is
        // still what elects the last mover above (bumping it per packet would let two draggers trade
        // the window at the send rate); what is compared HERE is the pose itself.
        //
        // The comparison is against FramePos/FrameRot/FrameSize, which is where this method records
        // every pose it applies, within the SAME epsilons TrackFrame uses to decide that a hand
        // moved something. That is not a coincidence and it is what keeps this from writing at the
        // packet rate when nothing has changed: a difference too small for TrackFrame to call a move
        // is too small to be worth applying, and applying it anyway would hand TrackFrame's baseline
        // a write it might read back as a local hand.
        if (!SharedWindows.TryGetGrab(kind, out GrabbableModal? grab) || grab == null)
        {
            Note($"player {bestPeer} published a pose for the {kind} window but this client has no "
                 + "floated one to place (not converted, or still behind the reveal gate) — the "
                 + "PAGE sync is untouched by this, which is why an unplaceable window can never "
                 + "hold anybody up");
            return;
        }
        if (!ToWorld(owner.Frame, owner.Pose.Position, owner.Pose.Rotation,
                     out Vector3 worldPos, out Quaternion worldRot))
        {
            Note($"player {bestPeer} published a {kind} pose in frame {owner.Frame} but this client "
                 + "cannot resolve that frame right now (no measurable parchment, or no seat "
                 + "anchor) — the local placement stands, which is the fail-closed direction");
            return;
        }

        float size = NetProtocol.DecodeStorySize(owner.SizeCode);

        float applyEps = MoveEpsilonMeters * Mathf.Max(PanelLayout.WorldScale, 0.01f);
        if (local.HaveBaseline && local.FollowingPeer == bestPeer
            && (worldPos - local.FramePos).sqrMagnitude <= applyEps * applyEps
            && Quaternion.Angle(worldRot, local.FrameRot) <= MoveEpsilonDegrees
            && Mathf.Abs(size - local.FrameSize) <= MoveEpsilonSize)
            return; // already standing exactly there — applying it again would be a no-op write

        // Whether this is the START of following someone new decides whether the full line below is
        // written. A drag now arrives as a STREAM of poses under one stamp, and the paragraph-long
        // apply line was written for one arrival per move; at the drag rate it would bury the log it
        // was meant to explain. So: the whole story once per followed peer / per settled move, and
        // nothing at all for the frames in between. The pose is still applied either way — this
        // governs only what is said about it.
        bool newFollow = local.FollowingPeer != bestPeer || !local.FollowedStampValid
                         || local.FollowedStamp != owner.PoseStamp;

        var frameOwner = (IPanelGrabOwner)grab;
        Transform? frameRoot = frameOwner.GrabRoot;
        if (frameRoot != null)
            frameRoot.localScale = Vector3.one
                                   * Mathf.Clamp(size, PanelGrabHandle.MinScale,
                                                 PanelGrabHandle.MaxScale);
        // PlaceFrameAt and never the HOST: GrabbableModal copies the frame onto the host every
        // frame in both Update and LateUpdate, so a write to the host is a write that is about to
        // be overwritten.
        grab.PlaceFrameAt(worldPos, worldRot);

        // Record what WE just wrote as the movement baseline, or the very next tick reads our own
        // write back as a local user move and starts a stamp war.
        local.FramePos = worldPos;
        local.FrameRot = worldRot;
        local.FrameSize = size;
        local.HaveBaseline = true;
        local.PoseOwned = false;
        local.FollowingPeer = bestPeer;
        local.FollowedStamp = owner.PoseStamp;
        local.FollowedStampValid = true;

        if (!newFollow)
            return;

        WarnIfOutOfCone(kind, bestPeer, worldPos);

        VRLog.Info(Scope, $"MAP ROOM shared window POSE APPLIED: {kind} (content key 0x{key:X8}) "
                          + $"follows player {bestPeer} (pose stamp {owner.PoseStamp}, the most "
                          + $"recently CHANGED stamp in the room), frame {owner.Frame} "
                          + $"({(owner.Frame == NetProtocol.SharedFrameParchment ? "parchment-local" : "seat-anchor")}) "
                          + $"({owner.Pose.Position.x:0.00},{owner.Pose.Position.y:0.00},"
                          + $"{owner.Pose.Position.z:0.00}) m, size {size:0.00}x → world "
                          + $"({worldPos.x:0.00},{worldPos.y:0.00},{worldPos.z:0.00}). "
                          + "A SHARED WINDOW'S POSE CHANGES FOR EXACTLY TWO REASONS: a hand on this "
                          + "client, or a hand on a peer's. Neither is the mod re-arranging "
                          + "anything, which is what the ModBuild 193 ruling ('ohne explizite "
                          + "Bewegung vom User sollen sie ihre Position nicht verändern') forbade — "
                          + "a remote player deliberately dragging this window IS an explicit user "
                          + "movement, simply a different user's.");
    }

    /// <summary>
    /// Say ONCE when an applied pose lands outside the local usable cone.
    ///
    /// <para>A remote pose is deliberately NOT clamped into it: clamping would silently break
    /// <i>"voll synchronsiert … auch wenn es jemand woanders hinverschiebt"</i> — the whole point is
    /// that the window is where the other player put it. So it is applied verbatim and the log
    /// carries its own evidence, which means an "I can't see the window" report arrives already
    /// diagnosed. The recovery is the one the player already has: grab it, which moves it for
    /// everyone.</para>
    /// </summary>
    private static void WarnIfOutOfCone(SharedWindowKind kind, int peer, Vector3 worldPos)
    {
        Camera? head = Rig.VRRigDriver.HeadCamera != null ? Rig.VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return;
        Vector3 to = worldPos - head.transform.position;
        to.y = 0f;
        Vector3 fwd = head.transform.forward;
        fwd.y = 0f;
        if (to.sqrMagnitude < 1e-6f || fwd.sqrMagnitude < 1e-6f)
            return;
        float deg = Vector3.Angle(fwd.normalized, to.normalized);
        if (deg <= UsableHalfConeDeg)
            return;
        Note($"player {peer} put the {kind} window {deg:F0}° off this client's own "
             + $"forward (outside the {UsableHalfConeDeg:F0}° usable half-cone), and it was applied "
             + "THERE ANYWAY rather than clamped into view. That is deliberate: clamping would mean "
             + "the two clients genuinely see the window in different places, which contradicts the "
             + "request. If a player reports 'I cannot see the window', this line is the evidence — "
             + "and the recovery is to grab it, which moves it for everyone");
    }

    /// <summary>
    /// Half-angle beyond which an applied remote pose is reported as "out of view", degrees.
    ///
    /// <para>A VALUE COPY of <c>ModalFallback.FallbackUsableHalfConeDeg</c> — the margined
    /// half-field a Quest-class headset produces — rather than of the LIVE
    /// <c>UsableHalfConeDeg</c>, which is derived per frame from the head camera's projection
    /// matrix inside a file this lane does not own. It is safe to copy precisely because it decides
    /// only whether to LOG and never whether to move anything: the pose is applied verbatim either
    /// way, so a future divergence costs a slightly wrong log threshold and nothing else.</para>
    /// </summary>
    private const float UsableHalfConeDeg = 35f;

    private static void Note(string reason)
    {
        float now = Time.unscaledTime;
        if (reason == _lastNote && now < _nextNoteAt)
            return;
        _lastNote = reason;
        _nextNoteAt = now + NoteThrottleSeconds;
        VRLog.Info(Scope, $"MAP ROOM record 21 — {reason}. Ignoring is always the safe direction "
                          + "here: a page not applied is re-offered by the next packet 200 ms later, "
                          + "while a page applied to the wrong message would skip somebody's text.");
    }
}
