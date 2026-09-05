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
///
/// <para><b>ModBuild 237 — THE WINDOW KIND 2 DESCRIBES CAN CHANGE WHILE THE RECORD IS LIVE, AND THE
/// WIRE DID NOT MOVE AN INCH FOR IT.</b> USER REQUEST (2026-08-23, verbatim): <i>"Ich möchte aber
/// das die gesamte Story, das Fenster und damit auch der Status des Fensters vollständig
/// synchronisiert wird."</i> <c>StoryComposite</c> tells the quest intro inside the LOADOUT screen —
/// the story window's own content is parked into it — so for the length of that intro the window the
/// player reads the story in is not <c>MapStoryController.window</c>.
/// <c>WorldUI/SharedWindowIdentity</c> therefore points <c>SharedWindowKind.MapStory</c> at the
/// composed host while the composite stands, and this class follows it without knowing anything about
/// composites: it asks <see cref="SharedWindows.TryGetGrab"/> for the kind, exactly as it always
/// did.</para>
///
/// <para><b>WHAT HAD TO CHANGE HERE IS ONE THING, AND IT IS AN IDENTITY.</b> The move baseline used
/// to be kept PER KIND, so the instant the kind resolved to a different transform the handover read
/// as a DRAG — see <see cref="Local.Grab"/> and <see cref="SyncIdentity"/> for the whole argument,
/// including why dropping the baseline alone is not enough. THE RECORD ITSELF IS UNTOUCHED: no new
/// field, no new kind, no new flag, and not one byte of the loadout screen's own content — the page,
/// the page count and the dialog hash are still read off <c>MapStoryController.dialogBox</c> and
/// nothing is ever read off the host. What travels is WHERE the window stands, which is the half the
/// game has no opinion about.</para>
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

    /// <summary>
    /// How many consecutive FRAMES the GAME must have said this client's map story window is CLOSED
    /// before the FINISHED bit goes on the wire.
    ///
    /// <para>The bound exists because <c>UIWindow.IsOpen</c> has been observed to drop for a SINGLE
    /// TICK and come back - <c>WorldUI.StoryComposite.StoryWindow</c> records the ModBuild 232 log
    /// where it did exactly that while the mod's float stood throughout. A one-tick dropout must not
    /// publish "everybody skip to the end".</para>
    ///
    /// <para><b>FRAMES AND NOT SECONDS, and the difference is load-bearing.</b> The cost of this
    /// bound is not paid by the flap it catches, it is paid by a message CHAIN:
    /// <c>MapStoryController.OnFinishShow -> ShowNext</c> can open the next message shortly after
    /// closing this one, and while the window is open again this client publishes the NEW key - so
    /// any FINISHED for the old one that has not left yet never leaves at all, and a peer still
    /// holding the old message is stranded exactly as before. Two frames (~22 ms at 90 Hz) kills
    /// the one-tick dropout and leaves the send edge - which pre-empts the 5 Hz gate - essentially
    /// the whole inter-message gap to travel in. A quarter-second bound would have spent most of
    /// that gap on the guard. THE RESIDUAL IS STATED AND NOT HIDDEN: a chain that re-opens within
    /// ~2 frames plus one send opportunity would still strand a peer, and the MAP STORY CLOSE line
    /// is what would show it (a close line on one client with no APPLIED line on the other).</para>
    /// </summary>
    private const int StoryClosedSettleFrames = 2;

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

        /// <summary>
        /// THE GRAB FRAME THE BASELINE ABOVE WAS TAKEN FROM. The ModBuild 237 fix, and the reason the
        /// shared identity is allowed to move at all.
        ///
        /// <para>Up to ModBuild 236 the baseline was kept PER KIND. <see cref="TrackFrame"/> caches
        /// the pose of whatever grab frame <see cref="SharedWindows.TryGetGrab"/> hands it and calls
        /// ANY change a move — so the instant a kind started resolving to a different window, the
        /// swap itself read as a drag: <see cref="Moving"/> was set, a pose was published and, on
        /// settle, this client could be elected the room's LAST MOVER, pushing the composed host's
        /// pose onto every peer's story box. Holding the frame beside the numbers taken from it turns
        /// that into an identity test that cannot be got wrong: a different frame is a different
        /// subject, and a different subject means there is no baseline, not a move.</para>
        ///
        /// <para>IT IS THE GRAB FRAME AND NOT THE WINDOW, deliberately, because it also catches the
        /// case the identity swap does not: the SAME window re-floated after a withdrawal gets a
        /// fresh <c>GrabbableModal</c> at a freshly placed pose, and that was a phantom drag by the
        /// same arithmetic.</para>
        /// </summary>
        internal GrabbableModal? Grab;

        /// <summary>The window this kind resolved to when the last identity line was printed — the
        /// subject of the swap log, kept separately from <see cref="Grab"/> so an ordinary
        /// close/re-float is silent and a genuine window-to-window handover is not.</summary>
        internal UIWindow? LastWindow;

        /// <summary><c>Time.frameCount</c> of the last identity change. Nothing is published or
        /// applied for this kind while it is fresh; see <see cref="IdentitySettleFrames"/>.</summary>
        internal int SwapFrame = int.MinValue;

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
            Grab = null;
            LastWindow = null;
            SwapFrame = int.MinValue;
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

    /// <summary>THE GAME'S OWN VERDICT ON THE MAP STORY WINDOW, LAST TICK - <c>UIWindow.IsOpen</c>
    /// and nothing else, so the falling edge read off it is the game running <c>Hide()</c> and never
    /// this mod re-showing something of its own.</summary>
    private static bool _storyGameOpenLast;

    /// <summary>The frame the GAME's <c>IsOpen</c> last went false for the map story window, or -1
    /// while it is open. The settle clock for <see cref="StoryClosedSettleFrames"/>; the -1 is
    /// tested BEFORE the subtraction, because a sentinel inside an arithmetic comparison is how
    /// [[sentinel-overflow-and-silent-scans]] happened.</summary>
    private static int _storyGameClosedFrame = -1;

    /// <summary>Unscaled time this client's map story box was last closed BY THE GAME, or -1 when no
    /// map story has closed this session. The ORIGIN the <c>ENCOUNTER ARRIVAL</c> line measures
    /// from - see <see cref="NoteEncounterArrival"/> for why an origin and not a timestamp.</summary>
    private static float _storyClosedAt = -1f;

    /// <summary>Who closed it: this player's own click, or a peer's FINISHED record driven through
    /// <see cref="ResolveStoryPage"/>. Prose, printed verbatim.</summary>
    private static string _storyClosedWhy = "no map story has closed on this client";

    /// <summary>The message key that close belonged to - printed beside the encounter's own key so
    /// a reader can confirm both clients measured their arrival from the SAME origin before
    /// subtracting one number from the other.</summary>
    private static uint _storyClosedKey;

    /// <summary>Unscaled time <see cref="ResolveStoryPage"/> last drove the TERMINAL page here, and
    /// the peer whose record asked for it - the attribution for <see cref="_storyClosedWhy"/>. A
    /// TIME and not a latch, so it cannot outlive its own subject.</summary>
    private static float _storyTerminalDriveAt = -1f;
    private static int _storyTerminalDrivePeer;

    /// <summary>The encounter identity the <c>ENCOUNTER ARRIVAL</c> line has already been printed
    /// for, so one encounter produces one line per client and never one per packet.</summary>
    private static uint _encounterNotedKey;

    private static string _lastNote = string.Empty;
    private static float _nextNoteAt;

    // ---- the terminal-page latch ---------------------------------------------------------------

    /// <summary>
    /// THE CONTENT KEY WHOSE LAST PAGE HAS ALREADY BEEN DRIVEN THROUGH HERE. Until ModBuild 445 the
    /// comment beside <c>box.ShowLine(target)</c> claimed that "re-applying it is a no-op". It is
    /// not, and the host paid for that sentence with an "Ein Fehler ist aufgetreten" box.
    ///
    /// <para>WHY A TERMINAL PAGE IS NOT IDEMPOTENT, read out of the game's own source rather than
    /// inferred: <c>UICharacterStoryBox.ShowLine</c> (UICharacterStoryBox.cs:172-192) returns through
    /// <c>Hide()</c> when <c>dialogIndex &gt;= dialogs.Count</c> and NEVER writes
    /// <c>currentDialogIndex</c> on that path. So next frame the local page reads the same value, the
    /// peer entry still carries <c>Finished</c> for the whole of <see cref="PeerStaleSeconds"/>, the
    /// pure resolver returns the same terminal target, and we call <c>Hide()</c> again. <c>Hide()</c>
    /// (:250-265) invokes <c>onFinish</c> and does not clear it, so every repeat runs the game's
    /// entire close chain a second, third and seventeenth time:
    /// <c>MapStoryController.OnFinishShow</c> (MapStoryController.cs:174-183) → <c>ShowNext</c>
    /// (:87-103) → <c>m_AllMessagesShownDelegate</c> →
    /// <c>MapChoreographer.FinishedShowingIntroTravelMessages</c> (MapChoreographer.cs:1846-1863) →
    /// <c>CompleteMoveCallback</c> (:2159-2194). That callback dereferences
    /// <c>MovingToLocation</c> as its second statement and NULLS it at :2178, so every invocation
    /// after the first throws a <c>NullReferenceException</c> that the game catches into
    /// <c>ERROR_MAP_CHOREOGRAPHER_00010</c> — the error box the HOST saw the moment the co-player
    /// clicked the story forward, while the co-player saw nothing at all, because a sender never
    /// applies its own record.</para>
    ///
    /// <para>THE LATCH IS KEYED ON THE MESSAGE, NEVER ON A TIMER, so it cannot expire into the same
    /// defect. It is released when a different content key arrives, and when the box reports
    /// <c>currentDialogIndex &lt; 0</c> — which is exactly what <c>UICharacterStoryBox.Show</c>
    /// writes (:117-124) as it re-opens the box, so even the SAME text shown a second time is driven
    /// again.</para>
    /// </summary>
    private static uint _storyConsumedKey;

    /// <inheritdoc cref="_storyConsumedKey"/>
    private static bool _storyConsumed;

    /// <summary>Whether the refusal has already been reported for <see cref="_storyConsumedKey"/>.
    /// The refusal is re-decided every frame; the line that reports it may be printed once, or it
    /// re-creates the flood it exists to prove is gone.</summary>
    private static bool _storyConsumedLogged;

    // ---- what the standing falsifier reports, all of it recorded where it happens ---------------

    private static int _storyPublishedFrame = int.MinValue;
    private static byte _storyPublishedStamp;
    private static bool _storyPublishedMoving;
    private static int _storyAppliedFrame = int.MinValue;
    private static int _storyAppliedPeer;
    private static byte _storyAppliedStamp;

    private static string _sharedVerdictKey = string.Empty;
    private static float _sharedNextHeartbeatAt;
    private static int _sharedHeartbeats;

    /// <summary>How often the standing falsifier repeats itself when nothing about the verdict has
    /// changed, and how many of those repeats a session may carry. A verdict CHANGE is always
    /// reported and is not capped — the cap is on the heartbeat, so a long quiet session cannot bury
    /// the log and a late regression can still never be silent.</summary>
    private const float SharedHeartbeatSeconds = 30f;

    /// <inheritdoc cref="SharedHeartbeatSeconds"/>
    private const int MaxSharedHeartbeats = 20;

    /// <summary>How recent a publish/apply has to be to count as "this tick" in the falsifier. The
    /// send path runs on the extras cadence (5 Hz idle, 15 Hz while a shared bar is held) and this
    /// line runs every frame, so a strict same-frame test would report NO on four frames out of five
    /// while the record was in fact carrying a pose block continuously.</summary>
    private const int SharedRecentFrames = 20;

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
        _storyConsumedKey = 0u;
        _storyConsumed = false;
        _storyConsumedLogged = false;
        _storyPublishedFrame = int.MinValue;
        _storyPublishedStamp = 0;
        _storyPublishedMoving = false;
        _storyAppliedFrame = int.MinValue;
        _storyAppliedPeer = 0;
        _storyAppliedStamp = 0;
        _sharedVerdictKey = string.Empty;
        _sharedNextHeartbeatAt = 0f;
        _sharedHeartbeats = 0;
        _storyGameOpenLast = false;
        _storyGameClosedFrame = -1;
        _storyClosedAt = -1f;
        _storyClosedWhy = "no map story has closed on this client";
        _storyClosedKey = 0u;
        _storyTerminalDriveAt = -1f;
        _storyTerminalDrivePeer = 0;
        _encounterNotedKey = 0u;
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
            // ModBuild 449 - THE SAME TWO-BRANCH TEST Sample() uses, and it has to be the same or
            // the edge would not pre-empt: the box being GONE is one way this client has clicked
            // its story through, and the GAME saying the window is closed while the mod's sticky
            // re-show keeps drawing it is the other - and it is the one that actually happens.
            bool finished = (box == null && StoryLocal.Key != 0u
                             && Time.unscaledTime < StoryLocal.FinishedUntil)
                            || StoryClickedThroughHere(box);
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

    /// <summary>
    /// Whether the map story window is still LOGICALLY open, as opposed to still drawn.
    ///
    /// <para>Deliberately not the same test as <see cref="MapBox"/>'s. That one accepts
    /// <c>IsOpen || IsVisible</c> so a pose keeps being published and applied through the close
    /// fade. This one is <c>IsOpen</c> alone: <c>UIWindow.IsOpen</c> is
    /// <c>m_CurrentVisualState == Shown</c> (UIWindow.cs:317) and <c>Hide()</c> writes that state
    /// synchronously, before the alpha tween begins (:524-548), so it is the only field that says
    /// "the game has already run this box's close chain" on the very frame it happened. Advancing a
    /// page into a window in that state runs the chain again.</para>
    /// </summary>
    private static bool MapStoryWindowOpen()
    {
        if (!Singleton<MapStoryController>.IsInitialized)
            return false;
        MapStoryController mc = Singleton<MapStoryController>.Instance;
        return mc != null && mc.window != null && mc.window.IsOpen;
    }

    /// <summary>
    /// IS THE LOCAL BOX SITTING ON ITS LAST PAGE? The entitlement term for the FINISHED bit.
    ///
    /// <para>"I clicked THROUGH the end" is a claim about a page, and the only client entitled to
    /// make it is one whose box was on the final page when the game closed it.
    /// <c>UICharacterStoryBox.ShowLine</c> past the last page runs <c>Hide()</c> WITHOUT writing
    /// <c>currentDialogIndex</c> (:172-177), so after a genuine click-through the index is still
    /// <c>Count - 1</c> and this is true. A window that goes closed in the MIDDLE of a message is
    /// something else entirely, and publishing FINISHED for it would drag every peer past text
    /// nobody has read.</para></summary>
    private static bool OnLastPage(UICharacterStoryBox? box)
    {
        if (box == null)
            return false;
        List<DialogLineDTO>? pages = box.dialogs;
        int count = pages != null ? pages.Count : 0;
        return count > 0 && box.currentDialogIndex >= count - 1;
    }

    /// <summary>
    /// <b>THIS CLIENT HAS CLICKED ITS MAP STORY THROUGH, EVEN THOUGH THE MOD IS STILL DRAWING IT.
    /// </b> The term that used to be missing, and the whole of the ModBuild 449 defect.
    ///
    /// <para><b>THE BUG, MEASURED.</b> In the 2026-09-05 two-player session the host clicked the
    /// campaign-map story box through and his party set off; the co-player's copy had been page-
    /// synced to the last page (<c>MAP ROOM story APPLIED ... moved from page 1 to 2</c>, peer frame
    /// 36887) and then STOPPED - he sat on that page for 672 frames (~7.4 s) until he clicked it
    /// away himself at frame 37559. Everything downstream is chained to that click: the map travel
    /// leg, and then the road event. So the 'Begegnung!' window arrived ~7.5 s late on his machine,
    /// and the mod's own travel drive is exonerated to the millisecond (both clients: DURATION asked
    /// 3.400 s, measured 3.405 / 3.406 s over 306 frames on each).</para>
    ///
    /// <para><b>WHY THE TERMINAL CLOSE NEVER TRAVELLED.</b> <see cref="Sample"/> published the
    /// FINISHED bit only while <see cref="MapBox"/> returned null, and <see cref="MapBox"/> is
    /// <c>IsOpen || IsVisible</c>. <c>UIWindow.IsVisible</c> is literally
    /// <c>m_CanvasGroup != null &amp;&amp; m_CanvasGroup.alpha &gt; 0</c> (UIWindow.cs:305-315) - the
    /// exact field <c>ModalFallback.ReassertStickyVisible</c> pins to 1 on every STICKY float whose
    /// game window is hidden. The mod floats the map story window and keeps that float standing for
    /// the whole encounter (neither log has <c>MODAL WINDOW: 'Map Story Window' released</c> until
    /// long after the road event closed), so <see cref="MapBox"/> could never return null, the
    /// FINISHED branch could never run, and the record that unlocks every other player's box was
    /// never sent. [[a-claim-must-not-measure-itself]] - a statement about the player's click was
    /// resting on a flag this mod writes. <c>WorldUI.StoryComposite.StoryWindow</c> carries the
    /// identical finding for the identical window and fixed it there; this file was never fixed.
    /// </para>
    ///
    /// <para><b>SO THE TEST IS THE GAME'S OWN STATE MACHINE.</b> <c>UIWindow.IsOpen</c> is
    /// <c>m_CurrentVisualState == Shown</c> (:317), written by <c>Hide()</c> synchronously and
    /// never by this mod - <see cref="MapStoryWindowOpen"/> already asks exactly that on the RECEIVE
    /// side, and the send side now asks the same question. Two guards bound it: the page
    /// entitlement (<see cref="OnLastPage"/>) and <see cref="StoryClosedSettleFrames"/> against the
    /// one-tick <c>IsOpen</c> dropout <c>StoryComposite</c> recorded.</para>
    ///
    /// <para>PURE: reads live game state and this file's own clocks, and writes nothing - it is
    /// asked from <see cref="SendDue"/>, which is documented as mutating nothing.</para>
    /// </summary>
    private static bool StoryClickedThroughHere(UICharacterStoryBox? box) =>
        box != null
        && StoryLocal.Key != 0u
        && !MapStoryWindowOpen()
        && OnLastPage(box)
        && _storyGameClosedFrame >= 0
        && Time.frameCount - _storyGameClosedFrame >= StoryClosedSettleFrames;

    /// <summary>
    /// Track the GAME's close edge for the map story window, once per frame, and print the one line
    /// that says whether the record that unlocks every other player's box is going out.
    ///
    /// <para>Called from <see cref="Resolve"/> (per frame) rather than from <see cref="Sample"/>
    /// (5 Hz) so the settle clock below is sampled at frame rate, and BEFORE the map-room gate so a
    /// room standing down cannot leave a stale edge behind.</para>
    /// </summary>
    private static void TickStoryCloseEdge()
    {
        float now = Time.unscaledTime;
        bool gameOpen = MapStoryWindowOpen();
        if (gameOpen)
        {
            _storyGameClosedFrame = -1;
        }
        else if (_storyGameOpenLast)
        {
            _storyGameClosedFrame = Time.frameCount;
            _storyClosedAt = now;
            _storyClosedKey = StoryLocal.Key;
            bool driven = _storyTerminalDriveAt >= 0f && now - _storyTerminalDriveAt <= 1f;
            _storyClosedWhy = driven
                ? $"DRIVEN HERE by player {_storyTerminalDrivePeer}'s FINISHED record"
                : "CLICKED THROUGH by the player at this machine";
            UICharacterStoryBox? box = MapBox();
            bool onLast = OnLastPage(box);
            // HW-VERIFY: the falling edge of the GAME's own IsOpen on the map story window - one
            // line per message close, per client, never per frame. It is the falsifier for the
            // ModBuild 449 fix: this line saying "the FINISHED record WILL go out" on the client
            // that clicked, and a `MAP ROOM story APPLIED ... (past the last page -> the box
            // closes)` on every other client within ~200 ms, is the fix working. This line saying
            // WITHHELD, or this line present on one client with no APPLIED line anywhere, is the
            // fix inert - and the term printed here says which. Its ABSENCE all session means no
            // map story box was ever closed in the 3D room; `grep -c 'Show event screen'` and the
            // game's own story lines separate that from a dead instrument.
            VRLog.Note(Scope, "MAP STORY CLOSE: the GAME closed this client's map story window "
                            + $"(UIWindow.IsOpen went false) and it was {_storyClosedWhy}. "
                            + $"LOCAL PAGE {(box != null ? box.currentDialogIndex : -1)} of "
                            + $"{StoryLocal.PageCount} page(s), message key 0x{StoryLocal.Key:X8}. "
                            + "THE MOD IS STILL DRAWING IT: "
                            + $"{(box != null ? "YES" : "no")} - MapBox() is IsOpen||IsVisible and "
                            + "ModalFallback.ReassertStickyVisible pins IsVisible's CanvasGroup "
                            + "alpha to 1 on a sticky float, which is exactly why this record may "
                            + "NOT be decided on it. THE FINISHED RECORD "
                            + (StoryLocal.Key != 0u && onLast && MapRoomDriver.Active
                                ? $"WILL GO OUT on the next extras packet ({StoryClosedSettleFrames} "
                                  + "frame(s) of settle against a one-tick IsOpen dropout, then the "
                                  + "send edge pre-empts the 5 Hz gate), and every peer holding this "
                                  + "message key drives its own box through the end - grep MAP ROOM "
                                  + "story APPLIED on theirs"
                                : "IS WITHHELD, by term: "
                                  + (!MapRoomDriver.Active
                                      ? "this client's 3D map room is not standing, so record 21 is "
                                        + "not sampled at all here and this player keeps the flat "
                                        + "game's own per-player pacing, exactly as the request "
                                        + "scopes it"
                                      : StoryLocal.Key == 0u
                                          ? "no message key was ever sampled here, so there is "
                                            + "nothing to name on the wire"
                                          : "this box was NOT on its last page, so nobody here "
                                            + "clicked THROUGH the end and saying so would drag "
                                            + "every peer past text they have not read"))
                            + ". Nothing local is written by any of this: the peer that receives it "
                            + "runs the game's own ShowLine -> Hide chain, which is the same chain "
                            + "its own click runs.");
        }
        _storyGameOpenLast = gameOpen;
    }

    /// <summary>
    /// <b>THE ENCOUNTER WINDOW ARRIVED HERE - the one line that measures item (3) of the 2026-09-05
    /// report</b> (<i>"Das Begegnungsfenster erscheint nicht zeitgleich bei allen Spielern ... Es
    /// soll zeitgleich erscheinen bei allen."</i>).
    ///
    /// <para><b>AN ORIGIN, NOT A TIMESTAMP, AND THAT IS THE WHOLE POINT.</b> The two clients' logs
    /// share NO clock - no wall time, no session time, and their frame counters start at different
    /// moments - so "the arrival timestamp" cannot be compared across two files at all. Working out
    /// this round's delay took reconstructing one from the game's GameAction ids, sparse frame
    /// stamps and a count of presence packets. So this line reports the ELAPSED TIME FROM AN EVENT
    /// THE FIX MAKES SIMULTANEOUS: the map story box closing. Subtract the two numbers and that IS
    /// the residual arrival delay, with no shared clock needed.</para>
    ///
    /// <para>Once per encounter identity per client. The identity is the same
    /// <see cref="EncounterKey"/> record 21 already carries - FNV-1a of <c>CRoadEvent.ID</c>, a yml
    /// key, so it is byte-identical on both machines regardless of UI language and is the join key
    /// between the two logs.</para>
    /// </summary>
    private static void NoteEncounterArrival(uint key)
    {
        float now = Time.unscaledTime;
        string since = _storyClosedAt >= 0f
            ? $"{now - _storyClosedAt:F2} s after this client's map story box (message key "
              + $"0x{_storyClosedKey:X8}) was closed, and that close was {_storyClosedWhy}"
            : "with NO map story box having closed on this client this session, so there is no "
              + "shared origin to measure from and the delay for this encounter cannot be read off "
              + "these two logs";
        // HW-VERIFY: THE ARRIVAL LINE. One per encounter, per client. Grep both logs for
        // `] [Net] ENCOUNTER ARRIVAL:` and match on the event key; the DIFFERENCE of the two
        // "s after ... closed" numbers is the delay the user reported, and it needs no clock the
        // logs do not have. If the token is absent from BOTH logs, no encounter happened - the
        // game's own unconditional `Encountered Road Event:` line separates that from a dead
        // instrument, and a non-zero count of THAT with zero of THIS means this line is the defect.
        VRLog.Note(Scope, $"ENCOUNTER ARRIVAL: the 'Begegnung' window (event key 0x{key:X8}) is "
                        + $"floated and shared on this client at frame {Time.frameCount}, {since}. "
                        + "WHAT DECIDES THE MOMENT: the window is opened by THIS client's own map "
                        + "flow (UIEventPanel.ShowEvent at the end of MapTimedMovementFlow leg 1), "
                        + "never by a packet - so the only fact that can make it simultaneous is "
                        + "the story close above being simultaneous, which is what record 21's "
                        + "FINISHED bit is for (grep MAP STORY CLOSE). The mod's own party-travel "
                        + "drive is NOT a term here: it is constant-duration by construction and "
                        + "the 2026-09-05 logs measured 3.405 s and 3.406 s for it over 306 frames "
                        + "on both clients. COMPARE THIS NUMBER WITH THE PEER'S: equal to within a "
                        + "wire tick is the fix; a difference of seconds is the residual and the "
                        + "story-close line on each side names which client was late. CHECK THE TWO "
                        + "MESSAGE KEYS MATCH before subtracting: two clients that closed DIFFERENT "
                        + "map messages have no shared origin and their two numbers are not "
                        + "comparable.");
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
        // ModBuild 449 - THE CLICK-THROUGH IS THE GAME'S VERDICT, NOT THE MOD'S FLOAT LIFETIME.
        // Read StoryClickedThroughHere for the whole account: the FINISHED branch below used to be
        // reachable only once MapBox() went null, and MapBox() is IsOpen||IsVisible while
        // ModalFallback.ReassertStickyVisible pins IsVisible's alpha to 1 for as long as the mod
        // floats the window - which is the whole encounter. So the record that closes every other
        // player's story box was never sent, and each of them sat on the last page until they
        // clicked it away themselves. Everything the road event chains behind that click moved with
        // it, the 'Begegnung' window included.
        bool clickedThrough = StoryClickedThroughHere(box);
        if (box == null || clickedThrough)
        {
            TrackFrame(SharedWindowKind.MapStory, StoryLocal, reset: true);
            // THE LINGER IS THE BOUND FOR BOTH BRANCHES, unchanged: FinishedUntil was last written
            // by the OPEN branch below, so a click-through publishes inside the same 60 s window a
            // box that vanished does, and the idle packet still goes back to being byte-identical.
            bool lingering = StoryLocal.Key != 0u && now < StoryLocal.FinishedUntil;
            if (lingering)
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
            else if (!clickedThrough)
            {
                StoryLocal.FinishedUntil = 0f;
                StoryLocal.Key = 0u;
            }
            // clickedThrough with the linger spent: fall silent, and KEEP the key. Clearing it here
            // would make StoryClickedThroughHere false on the next tick, drop this client back into
            // the OPEN branch, and republish a box the game closed a minute ago as "open on page N"
            // - a stale statement, resurrected by the mod's own float, at 5 Hz.
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
            // The window's ARRIVAL edge, keyed on the encounter's own identity so a re-sample never
            // repeats it and a second encounter always gets its own line.
            if (key != 0u && key != _encounterNotedKey)
            {
                _encounterNotedKey = key;
                NoteEncounterArrival(key);
            }
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
        if (kind == SharedWindowKind.MapStory)
        {
            _storyPublishedFrame = Time.frameCount;
            _storyPublishedStamp = local.PoseStamp;
            _storyPublishedMoving = local.Moving;
        }
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
            // The subject is gone, so the baseline's owner is too. LastWindow is deliberately NOT
            // cleared: the next window to carry this kind must still be reported as a handover even
            // when the two never overlapped, which is exactly what happens at the composite's rising
            // edge (the story window leaves the float set on the tick the host takes over).
            local.Grab = null;
            return;
        }

        // THE IDENTITY TEST COMES BEFORE THE MOVE TEST, and it is the whole of the ModBuild 237 fix.
        if (SyncIdentity(kind, local, grab, pos, rot, size))
            return;   // nothing is published on the tick the subject changed

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
            // AND THE SPAWN ANCHOR IS SPENT FROM THE FIRST MILLIMETRE (ModBuild 243), not from the
            // settle: the anchor must be over the instant a hand takes the window, never one
            // MoveSettleSeconds later. Cheap to call repeatedly — the latch adds once and the log
            // line prints once. The window itself is already safe (a grabbed window is never
            // re-placed), so this covers the interval between the release and the settle.
            WorldUI.ModalFallback.NoteSharedAnchorSpent(
                kind, $"this client moved its own {kind} window by hand");
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

    /// <summary>How many frames after an identity change this kind stays silent in BOTH directions —
    /// nothing published, nothing applied. One would do; two costs 400 ms of nothing at the 5 Hz idle
    /// cadence in the worst case and removes the whole class of one-frame ordering question between
    /// <c>NetAvatarDriver</c>'s update and <c>ModalFallback</c>'s.</summary>
    private const int IdentitySettleFrames = 2;

    /// <summary>
    /// HAS THE WINDOW BEHIND THIS KIND CHANGED SINCE THE BASELINE WAS TAKEN? If so, drop everything
    /// that described the old one, take a fresh baseline from the new one, and say so.
    ///
    /// <para><b>WHY THIS EXISTS.</b> ModBuild 237 lets <c>SharedWindowKind.MapStory</c> follow the
    /// quest intro's composed host (see <c>WorldUI/SharedWindowIdentity</c>), so the transform this
    /// record describes can change while the record is live. <see cref="TrackFrame"/> calls any change
    /// of that transform a MOVE, which is right for a hand and catastrophic for a handover: it would
    /// set <see cref="Local.Moving"/>, publish a pose, and on settle bump the stamp and elect this
    /// client the room's LAST MOVER — pushing the loadout screen's pose onto every peer's story box.
    /// The ModBuild 236 note that predicted this proposed dropping <c>HaveBaseline</c> and
    /// <c>Moving</c>; that is NOT sufficient and the missing half is <see cref="Local.PoseOwned"/>.
    /// A client that had already moved the OLD window keeps <c>PoseOwned</c> true across the swap,
    /// and <see cref="WritePose"/> gates on <c>PoseOwned || Moving</c> — so it would keep publishing,
    /// now describing the NEW window, under the UNCHANGED stamp that already elected it. Peers do not
    /// re-elect on an unchanged stamp; they simply keep following, and the pose they follow is
    /// suddenly a different window's. So the whole pose block is forgotten
    /// (<see cref="Local.ForgetPose"/>), which is the same thing this record already does when the
    /// message content changes — pose ownership is per subject, and a new window is a new
    /// subject.</para>
    ///
    /// <para><b>IT IS SAFE IN BOTH DIRECTIONS BECAUSE IT IS NOT DIRECTIONAL.</b> The test is "is this
    /// the frame my numbers came from", asked of a reference. Rising edge, falling edge, a re-float of
    /// the same window, a window that vanishes and a different one that appears in the same tick — all
    /// of them are the same answer, and the state it produces is the state a freshly opened window
    /// starts in.</para>
    ///
    /// <para>Returns true when the identity changed, which is the caller's cue to publish nothing on
    /// this tick.</para>
    /// </summary>
    private static bool SyncIdentity(SharedWindowKind kind, Local local, GrabbableModal grab,
                                     Vector3 pos, Quaternion rot, float size)
    {
        if (ReferenceEquals(local.Grab, grab))
            return false;

        bool hadBaseline = local.HaveBaseline;
        bool wasMoving = local.Moving;
        bool wasOwner = local.PoseOwned;
        Vector3 hadPos = local.FramePos;
        UIWindow? from = local.LastWindow;
        UIWindow? to = SharedWindows.WindowOf(kind);

        local.ForgetPose();          // baseline, Moving, PoseOwned, and whoever we were following
        local.Grab = grab;
        local.FramePos = pos;
        local.FrameRot = rot;
        local.FrameSize = size;
        local.HaveBaseline = true;
        local.SwapFrame = Time.frameCount;

        // A HANDOVER IS ONLY REPORTED WHEN THE WINDOW CHANGED. A window that merely re-floated gets a
        // fresh GrabbableModal and lands here too — that is the point, its fresh pose must not read as
        // a drag either — but it is not an identity swap and saying so would bury the edges that are.
        bool handover = to != null && from != null && !ReferenceEquals(from, to);
        if (to != null)
            local.LastWindow = to;
        if (!handover)
            return true;

        VRLog.Info(Scope, $"STORY WINDOW SHARED IDENTITY SWAPPED — move-tracker half: "
                          + $"SharedWindowKind.{kind} now resolves to '{to!.name}' and no longer to "
                          + $"'{from!.name}'. BASELINE DROPPED: "
                          + (hadBaseline
                              ? $"pos ({hadPos.x:0.00},{hadPos.y:0.00},{hadPos.z:0.00}), moving={wasMoving}, "
                                + $"this client was the last mover={wasOwner} at stamp {local.PoseStamp}"
                              : "there was none")
                          + ". FRESH BASELINE TAKEN from the new grab frame: "
                          + $"({pos.x:0.00},{pos.y:0.00},{pos.z:0.00}), size {size:0.00}x. POSE "
                          + "PUBLISHED THIS TICK = NO, and that is structural rather than a promise: "
                          + "WritePose publishes only while PoseOwned or Moving, and the line above "
                          + "cleared both — so the swap cannot be mistaken for a drag, cannot bump the "
                          + "stamp and cannot elect this client the room's last mover. POSE APPLIED "
                          + $"THIS TICK = NO: nothing is applied for {IdentitySettleFrames} frame(s) "
                          + "either, so PanelPoseWatch cannot meet a peer write on a window the kind "
                          + "has just left. WHETHER A HAND WAS ON THE BAR is the window half's to "
                          + "answer — grep the same string for it; the swap is refused outright while "
                          + "one is. A move-tracker half with no window half beside it means a window "
                          + "was re-floated rather than the identity moving, which is the other thing "
                          + "this method exists to make harmless.");
        return true;
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
        // Before the map-room gate on purpose: the edge below is the GAME's own close of the story
        // window, and a room standing down must not leave a stale one latched for the next one.
        TickStoryCloseEdge();
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

        // ModBuild 237's standing falsifier. Placed AFTER the map-story arm so it reports the state
        // this tick actually produced, and it is a pure read — it resolves windows and compares
        // numbers, and writes nothing but its own rate-limit fields.
        ReportSharedStory(box);

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

    // ---- ModBuild 237: the standing falsifier --------------------------------------------------

    /// <summary>
    /// THE ONE LINE A TESTER CAN GREP THAT IS TRUE ONLY IF THE USER'S REQUEST IS SATISFIED.
    ///
    /// <para><b>USER REQUEST (2026-08-23, verbatim):</b> <i>"Ich möchte aber das die gesamte Story,
    /// das Fenster und damit auch der Status des Fensters vollständig synchronisiert wird."</i> The
    /// PAGE half has shipped since ModBuild 222 and has its own line
    /// (<c>MAP ROOM story APPLIED</c>). This one is about the WINDOW half, which went inert in
    /// ModBuild 236 without a single line saying so — the kind resolved to a window that was no longer
    /// floated, so <see cref="SharedWindows.TryGetGrab"/> simply found nothing and every code path
    /// returned quietly. That silence is the defect this line exists to make impossible.</para>
    ///
    /// <para><b>EVERY CLAUSE IS MEASURED THIS TICK AND NONE OF THEM IS A MECHANISM.</b> Which window
    /// answers the kind, whether the mod has a live floated panel and grab frame for it, whether the
    /// shared predicate says this client takes part, whether a pose block went out and whether one
    /// came in with whose stamp, and which grab frame the move tracker's baseline is standing on.
    /// [[an-instrument-can-assert-a-cause]] — the one thing it deliberately does NOT claim is what
    /// the window actually SHOWS: the corner network badge is switched by
    /// <c>GrabbableModal.SyncSharedState</c> from this same predicate one tick later and reported by
    /// its own line, <c>SHARED WINDOW BADGE</c>, so the two together are the proof and neither
    /// pretends to be it alone.</para>
    ///
    /// <para><b>ModBuild 238 — THERE ARE THREE VERDICTS NOW AND THE THIRD IS WHY.</b> Through
    /// ModBuild 237 this line had two, and the negative one fired as a WARNING 24 times in one
    /// hardware session while the player was not reading a story at all: every one of those lines
    /// says "no composite is standing, so MapStoryController.window carries its own kind", which is
    /// the resting state of the campaign map and not a fault. A line whose own subject — "the window
    /// the player is reading the story in" — does not exist cannot be judging anything, and this
    /// project has already had to retract wrong instrument text twice. So the resting state says so,
    /// at Info, in its own words; CONFIRMED and NOT ACHIEVED are reserved for the ticks on which
    /// there IS a story to share.</para>
    ///
    /// <para>GREP: <c>STORY WINDOW SHARED: CONFIRMED</c> — the request is met.
    /// <c>STORY WINDOW SHARED: NOT ACHIEVED</c> — it is not, with the failing clause named.
    /// <c>STORY WINDOW SHARED: RESTING</c> — there is no story on screen here, so neither of the
    /// other two is being claimed. It must NEVER be a Warning.</para>
    /// </summary>
    private static void ReportSharedStory(UICharacterStoryBox? box)
    {
        UIWindow? window = SharedWindows.WindowOf(SharedWindowKind.MapStory);
        UIWindow? host = WorldUI.SharedWindowIdentity.MapStoryHost;
        if (window == null && box == null)
        {
            // No map story anywhere on this client. Re-arm so the next one gets a fresh verdict line
            // rather than being suppressed by the last quest's.
            _sharedVerdictKey = string.Empty;
            _sharedHeartbeats = 0;
            return;
        }

        bool haveGrab = SharedWindows.TryGetGrab(SharedWindowKind.MapStory,
                                                 out GrabbableModal? grab) && grab != null;
        ConvertedPanel? panel = ModalFallback.PanelFor(window);
        bool floated = panel != null && panel.IsAlive && !panel.RevealPending && !panel.RenderHidden;
        bool blue = SharedWindows.IsShared(window);
        bool grabbed = haveGrab && grab!.IsGrabbed;

        int now = Time.frameCount;
        bool publishing = _storyPublishedFrame != int.MinValue
                          && now - _storyPublishedFrame <= SharedRecentFrames;
        bool applying = _storyAppliedFrame != int.MinValue
                        && now - _storyAppliedFrame <= SharedRecentFrames;

        // =========================================================================================
        // ModBuild 238 — THE SUBJECT OF THE SENTENCE HAS TO EXIST BEFORE THE SENTENCE CAN BE FALSE.
        // =========================================================================================
        //
        // This line's own subject is "the window the player is reading the story in". In the ModBuild
        // 237 hardware log it fired as a WARNING 24 times — :1962, :2931, :3018, :3107, :3133, :3250,
        // :3338, :3703, :3810, :4200, :4838, :5635, :5800, :5911, :6024, :6129, :6237 and on — and
        // every one of them says "no composite is standing, so MapStoryController.window carries its
        // own kind", i.e. THE PLAYER IS NOT READING ANY STORY AT ALL. The early re-arm above only
        // catches the case where there is neither a window nor a box, and on the campaign map there
        // is ALWAYS a window: MapStoryController is a Singleton whose serialized `window` field
        // exists for the whole life of the scene whether it is open or not. So the guard never fired
        // and the instrument judged a claim with no subject, at Warning level, for the rest of the
        // session.
        //
        // THIS PROJECT HAS HAD TO RETRACT WRONG INSTRUMENT TEXT TWICE ([[verify-the-instrument-first]],
        // [[an-instrument-can-assert-a-cause]]) AND A THIRD MUST NOT STAND. The fix is not to silence
        // the line — an absence is exactly what nobody can diagnose — but to say plainly which of the
        // three states it is in. RESTING is not a verdict about sharing; it is the statement that
        // there is nothing to share yet, and it goes out at Info.
        bool storyOnScreen = box != null || floated;
        bool ok = storyOnScreen && window != null && haveGrab && floated && blue;
        string verdict = !storyOnScreen ? "RESTING" : ok ? "CONFIRMED" : "NOT ACHIEVED";
        string key = verdict + "|" + (window != null ? window.name : "<none>") + "|" + haveGrab
                     + floated + blue + publishing + applying;

        float nowSec = Time.unscaledTime;
        bool changed = key != _sharedVerdictKey;
        if (!changed)
        {
            if (nowSec < _sharedNextHeartbeatAt || _sharedHeartbeats >= MaxSharedHeartbeats)
                return;
            _sharedHeartbeats++;
        }
        _sharedVerdictKey = key;
        _sharedNextHeartbeatAt = nowSec + SharedHeartbeatSeconds;

        string measured =
            $"SharedWindowKind.MapStory resolves to '{(window != null ? window.name : "<no window>")}' "
            + $"this tick, and the reason is: {WorldUI.SharedWindowIdentity.MapStoryWhy} "
            + $"(identity generation {WorldUI.SharedWindowIdentity.MapStoryGeneration}, composed host "
            + $"{(host != null ? "'" + host.name + "'" : "none — the story box carries its own kind")}). "
            + $"LIVE FLOATED PANEL: {floated}, mod-owned grab frame resolved: {haveGrab}, a hand on its "
            + $"bar right now: {grabbed}. SHARED PREDICATE (what paints the bar): {blue} — "
            + $"{(blue ? "SHARED BLUE" : "private brass")}; the paint itself is one tick behind this "
            + "and is reported by its own line, SHARED WINDOW BAR. PUBLISHING A POSE BLOCK: "
            + (publishing
                ? $"YES, stamp {_storyPublishedStamp}, mid-drag={_storyPublishedMoving}, last written "
                  + $"{now - _storyPublishedFrame} frame(s) ago"
                : "no — nobody here has moved this window since it opened, which is the ordinary "
                  + "resting state and NOT a fault")
            + ". APPLYING ONE: "
            + (applying
                ? $"YES, following player {_storyAppliedPeer} at pose stamp {_storyAppliedStamp}, last "
                  + $"applied {now - _storyAppliedFrame} frame(s) ago"
                : "no — no peer in this room is publishing a pose for this window, which again is the "
                  + "resting state: record 21 carries a pose block only once somebody has MOVED the "
                  + "window")
            + ". THE MOVE TRACKER'S BASELINE stands on "
            + (StoryLocal.Grab != null
                ? $"the grab frame of '{(StoryLocal.LastWindow != null ? StoryLocal.LastWindow.name : "?")}'"
                  + $", HaveBaseline={StoryLocal.HaveBaseline}, moving={StoryLocal.Moving}, this client "
                  + $"is the last mover={StoryLocal.PoseOwned}"
                : "nothing at all — there is no grab frame to measure, so no local move can be "
                  + "detected and none is published")
            + $"; the story box itself is {(box != null ? "on screen" : "not on screen")} and its PAGE "
            + "sync is independent of every clause above";

        if (!storyOnScreen)
        {
            VRLog.Info(Scope, "STORY WINDOW SHARED: RESTING — there is no story on screen on this "
                              + "client, so there is nothing for the window half to be about. THIS IS "
                              + "THE ORDINARY STATE OF THE CAMPAIGN MAP AND IT IS NOT A FAULT: "
                              + "MapStoryController is a Singleton whose serialized window exists for "
                              + "the whole life of the scene whether it is open or not, so a window "
                              + "resolving for the kind means only that the object is there. The "
                              + "clauses below are printed for continuity, not as a verdict — a "
                              + "verdict needs a subject, and the subject of this line is \"the window "
                              + "the player is reading the story in\". MEASURED THIS TICK: " + measured
                              + ". WHAT TO EXPECT NEXT: the moment a map story box opens or the mod "
                              + "floats the story window, this line becomes CONFIRMED or NOT ACHIEVED "
                              + "and those are the two that mean something.");
            return;
        }
        if (ok)
        {
            VRLog.Info(Scope, "STORY WINDOW SHARED: CONFIRMED — the window the story is being told in "
                              + "is the one that carries the shared kind, so its bar is blue, its pose "
                              + "travels on record 21 in both directions and it does not re-face when "
                              + "released. MEASURED THIS TICK: " + measured + ". WHAT THIS DOES NOT "
                              + "CLAIM: that a pose is in flight. Publishing and applying both read no "
                              + "until somebody drags the window, and that is correct — a freshly "
                              + "opened shared window has no agreed pose and each client places its own "
                              + "copy in its own view. Only a NO on the first three clauses is a "
                              + "defect.");
            return;
        }
        VRLog.Warn(Scope, "STORY WINDOW SHARED: NOT ACHIEVED — the window the player is reading the "
                          + "story in is not the one carrying the shared kind, so its pose is neither "
                          + "published nor applied and its bar is brass. MEASURED THIS TICK: " + measured
                          + ". READ IT LIKE THIS: no window at all means MapStoryController is down and "
                          + "there is nothing to share. A window named but grab frame resolved=False "
                          + "means it is not converted or is still behind the reveal gate — that client "
                          + "keeps its own placement and remains a full participant in the PAGE sync, "
                          + "which is why an unplaceable window can never hold anybody's story up. "
                          + "SHARED PREDICATE=False with a window and a grab means ParticipatesHere "
                          + "said no, i.e. this player has the 3D world map switched off and is keeping "
                          + "the flat game's own per-player pacing by the user's own scoping. And a "
                          + "composed host of none while the quest intro is on screen is the ModBuild "
                          + "236 defect returning — grep STORY WINDOW SHARED IDENTITY DEFERRED and "
                          + "STORY COMPOSITE CLAIM for why the identity did not move.");
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

        // THE TERMINAL LATCH, RELEASED BEFORE IT IS READ. A different message, or the same message
        // re-opened — which the game marks by resetting currentDialogIndex to −1 in
        // UICharacterStoryBox.Show (:117-124) — is a new subject, and a latch that outlived its
        // subject would silently switch this whole feature off for the rest of the session.
        if (_storyConsumed && (_storyConsumedKey != key || localPage < 0))
        {
            _storyConsumed = false;
            _storyConsumedLogged = false;
        }
        if (_storyConsumed)
        {
            if (!_storyConsumedLogged)
            {
                _storyConsumedLogged = true;
                // HW-VERIFY: the once-per-message proof that the terminal page is applied ONCE. It
                // prints on the frame after the last page was driven through and then never again
                // for that message, so a session with N closed shared map messages carries at most
                // N of these lines. Seventeen of anything here would mean the latch is not holding.
                VRLog.Note(Scope, "MAP ROOM record 21 — the last page of message key "
                                  + $"0x{key:X8} has already been driven through on this client, so "
                                  + "the peer's lingering FINISHED record is being refused rather "
                                  + "than re-applied. It has to be refused: ShowLine past the last "
                                  + "page runs Hide() WITHOUT writing currentDialogIndex "
                                  + "(UICharacterStoryBox.cs:172-177), so the same target is "
                                  + "recomputed every frame, and each repeat re-runs the game's "
                                  + "close chain down to MapChoreographer.CompleteMoveCallback, "
                                  + "which dereferences a MovingToLocation it nulled itself the "
                                  + "first time — that NullReferenceException is the "
                                  + "'Ein Fehler ist aufgetreten' box the host saw. The latch is "
                                  + "released by a different content key or by the box re-opening, "
                                  + "never by a timer.");
            }
            return;
        }

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

        bool terminal = target >= localCount;

        // THE BELT, and it covers a case the latch above cannot: the player AT THIS MACHINE clicked
        // his own copy through. Then the first terminal ShowLine we would issue is already the
        // SECOND close of that box, and it throws exactly as a repeat of our own would — the latch
        // never armed because we never drove anything. A window the game has already told to close
        // is not a window whose page may be advanced.
        //
        // UIWindow.IsOpen is `m_CurrentVisualState == Shown` (UIWindow.cs:317) and Hide() assigns
        // that state SYNCHRONOUSLY, before the alpha tween starts (:524-548) — so it is false from
        // the instant the close chain runs, while MapBox() deliberately keeps returning the box for
        // the length of the fade (IsOpen || IsVisible). This is the one place that difference is the
        // whole answer, and it is why the test is IsOpen alone and never IsVisible.
        if (terminal && !MapStoryWindowOpen())
        {
            Note($"the last page of message key 0x{key:X8} is wanted, but this client's map story "
                 + "window is already closing (UIWindow.IsOpen false, the fade still running) — the "
                 + "box was clicked through here, so driving it again would run the game's close "
                 + "chain a second time and throw inside CompleteMoveCallback");
            _storyConsumedKey = key;
            _storyConsumed = true;
            _storyConsumedLogged = true;
            return;
        }

        if (terminal)
        {
            // The attribution for the MAP STORY CLOSE line: this close is a PEER's statement being
            // honoured here, not a hand at this machine. A time and not a latch, so it expires.
            _storyTerminalDriveAt = Time.unscaledTime;
            _storyTerminalDrivePeer = remoteFinished ? finishedPeer : furthestPeer;
            // ARMED BEFORE THE CALL, NOT AFTER. ShowLine runs the game's whole close chain
            // synchronously inside itself, and anything in that chain that reached back into this
            // resolver would find the latch already down.
            _storyConsumedKey = key;
            _storyConsumed = true;
            _storyConsumedLogged = false;
        }

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

        VRLog.Info(Scope, $"MAP ROOM story APPLIED (record 21, kind MapStory): message key "
                          + $"0x{key:X8}, {localCount} page(s) here, moved from page {localPage} to "
                          + $"{target}{(terminal ? " (past the last page → the box closes)" : "")} — "
                          + $"{who}. The record carries the ABSOLUTE page, so two players clicking "
                          + "at once both publish the same number and no page is skipped. A "
                          + "MID-MESSAGE page IS idempotent — ResolveStoryPage returns −1 for "
                          + "anything at or below the local page, because ShowLine wrote "
                          + "currentDialogIndex. A TERMINAL page is NOT, and never was: that path "
                          + "Hide()s without writing the index, so it is the latch above and not "
                          + "arithmetic that makes it happen once. THIS IS A DIFFERENT CONTROLLER "
                          + "FROM RECORD 19's: MapStoryController, which record 19 has never been "
                          + "able to see."
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
        // THE IDENTITY IS CHECKED ON THE RECEIVE PATH TOO, and not only in TrackFrame, because
        // Sample runs on the extras cadence (5 Hz when idle) while this runs EVERY FRAME. Between a
        // swap and the next Sample the receive half would otherwise be measuring a peer's pose against
        // the OLD window's baseline and writing it to the NEW window. The cost is one scan of the
        // converted list per kind per frame — ModalFallback.TryGetGrabFor is a ReferenceEquals loop —
        // and it collapses to a single ReferenceEquals here as soon as the subject stops changing.
        if (SharedWindows.TryGetGrab(kind, out GrabbableModal? subject) && subject != null
            && !ReferenceEquals(local.Grab, subject)
            && TryReadFrame(subject, out Vector3 sPos, out Quaternion sRot, out float sSize))
            SyncIdentity(kind, local, subject, sPos, sRot, sSize);

        // NOTHING IS APPLIED ON THE TICK THE SUBJECT CHANGED — the mirror of "nothing is published".
        // PanelPoseWatch reads SharedWindows.IsShared once per tick as its peerOwned flag, and a pose
        // write it cannot attribute is REFUSED and snapped back; a write landing on a window the kind
        // is leaving is exactly that. THE SENTINEL IS TESTED EXPLICITLY rather than by arithmetic:
        // `Time.frameCount - int.MinValue` overflows to a negative number that would pass a `<=` test
        // FOREVER, which is [[sentinel-overflow-and-silent-scans]] verbatim.
        if (local.SwapFrame != int.MinValue
            && Time.frameCount - local.SwapFrame <= IdentitySettleFrames)
            return;

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
        // TELL THE IDENTITY LATCH, so it does not move the kind off this window in the same frame the
        // write landed. PanelPoseWatch classifies a pose write it cannot attribute as Unattributed and
        // SNAPS IT BACK, and its peerOwned flag is SharedWindows.IsShared read once per tick — so an
        // identity that left here now would have this very write undone in front of the player. The
        // watch re-baselines on every sanctioned write, so a couple of clear frames is all it needs.
        // This is Net → WorldUI, the direction the module boundary already runs in.
        WorldUI.SharedWindowIdentity.NotePoseApplied(kind);
        // AND SPEND THE SPAWN ANCHOR (ModBuild 243). A real pose for this kind now exists, so the
        // table/board anchor is over: the user's narrowing is "ich meine nur die initiale
        // Spawnposition - es soll weiterhin von jedem verschiebbar sein". Without this the one
        // pre-reveal re-place could put a window a peer had already dragged back on its home, which
        // is a drag being undone by placement code — the one thing that must never happen here.
        WorldUI.ModalFallback.NoteSharedAnchorSpent(
            kind, $"a peer's pose was applied to this client's {kind} window");

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
        if (kind == SharedWindowKind.MapStory)
        {
            _storyAppliedFrame = Time.frameCount;
            _storyAppliedPeer = bestPeer;
            _storyAppliedStamp = owner.PoseStamp;
        }

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
