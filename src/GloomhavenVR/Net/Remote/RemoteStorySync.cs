using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// STORY / DIALOG WINDOW SYNC — wire record <see cref="NetProtocol.ExtIdStorySync"/> (19).
///
/// <para>USER REQUEST (3-player hardware session 2026-08-15, verbatim): "In unserem Fenster hatte
/// ein Mitspieler vergessen die Geschichte/Dialog am Anfang weiterzuklicken was zu einem lock
/// geführt hat, daher möchte ich hier ein neues Feature: Das Geschichte Fenster und damit der
/// ganze Dialog sollen synchron sein d.h. das Fenster in der Größe und Position soll voll
/// synchronisiert werden genauso wie der Status des Fensters. Wenn durch den Dialog geklickt
/// wurde, wurde folglich für ALLE entsprechend durchgeklickt und es gibt keinen Lock mehr."</para>
///
/// <para>WHAT THE STORY DIALOG ACTUALLY IS. <c>StoryController</c> (a <c>Singleton</c>) owns a
/// <c>UIWindow</c> and a <c>UICharacterStoryBox</c> (decompiled StoryController.cs:62-66). The box
/// holds <c>List&lt;DialogLineDTO&gt; dialogs</c> and <c>int currentDialogIndex</c>
/// (UICharacterStoryBox.cs:61/63); the game's own click handler is
/// <c>skipButton.onClick → Skip() → ShowNextLine() → ShowLine(currentDialogIndex + 1)</c>
/// (UICharacterStoryBox.cs:81/239-248), and <c>ShowLine</c> past the last page calls
/// <c>Hide()</c> (line 174-177). The mod already floats this window in VR — every
/// <c>'GloomhavenVR.Panel_Modal_Story Window'</c> line in the log is it.</para>
///
/// <para>WHY IT LOCKED, AND WHAT REMOVES THE LOCK. The box is PURELY LOCAL — nothing about it is
/// on the game's wire. While it stands, <c>StoryController.TryBlockedUpdate</c>
/// (StoryController.cs:211-219) holds <c>ActionProcessor.LockProcessingAction()</c> and
/// <c>Choreographer.AddUpdateBlocker()</c> on THAT client, so that client processes nothing from
/// the shared action queue; and the client's <c>GameLoadedAndClientReady</c> side action is sent
/// only from <c>ShowNext()</c> (StoryController.cs:130-141), i.e. only after the last page is
/// clicked through. One player who walks away therefore stalls everyone. MEASURED in that
/// session's three logs (`MODAL DIAG '…Panel_Modal_Story Window' (age …)`): the host dismissed its
/// box after 1.4 s, peer 1 held it 1085.5 s and peer 2 1463.9 s.</para>
///
/// <para>THE LOCK IS REMOVED BY <see cref="NetProtocol.StoryFinishedBit"/> AND NOTHING ELSE. When
/// any player clicks through the end, every other client that still holds the same dialog drives
/// its OWN box past its own last page. That runs the game's own
/// <c>ShowLine → Hide → onFinish → StoryController.OnFinishShow → ShowNext</c> chain locally, and
/// THAT chain is what calls <c>TryUnblockedUpdate()</c> and sends that client's own
/// <c>GameLoadedAndClientReady</c>. The idle peer's machine emits its own handshake, through the
/// game's own code. We send no side action of our own, we never touch
/// <c>ScenarioRuleLibrary</c> and we never touch Bolt — the only thing we drive is a UI seam.</para>
///
/// <para>WHY AN ABSOLUTE PAGE AND NOT A CLICK. The record carries "this dialog is at page N", not
/// "somebody clicked". Two players clicking in the same frame both publish N+1, so the pair cannot
/// skip a page; a re-delivered packet is a no-op; a packet that arrives out of order on the
/// unreliable side-channel is ignored rather than applied backwards. The whole decision is
/// <see cref="NetProtocol.ResolveStoryPage"/>, a pure function the wire tests pin directly.</para>
///
/// <para>PRECEDENT FOLLOWED: the DECISION family (records 12/24/29) — a per-player extras record
/// that is FULL STATE on every packet while the thing is on screen and ABSENT otherwise, with no
/// sequence number, applied through a change-gated setter. It was chosen over the board-UI records
/// because, like a decision row, the story box is a modal the OWNER is interacting with and every
/// other client mirrors; and because its "absence = nothing on screen" contract is what keeps
/// every packet of every session without a narrative byte-identical to the previous build's. What
/// is NOT copied from it: the decision records are cosmetic mirrors, this one drives the receiver's
/// own game UI, so every apply is gated on the receiver's OWN dialog identity
/// (<see cref="PresenceState.StoryKey"/>) before anything is driven.</para>
///
/// <para>LOCAL SETTINGS TAKE PRECEDENCE, and here that ruling is what makes the feature safe: a
/// client with no story window, an unconverted one, or one that never floated is still a full
/// participant in the PAGE sync — the advance path never consults the window at all. Only the POSE
/// path does, and a client that cannot place a window simply keeps its own placement. Nothing
/// about the pose can gate, delay or block anybody's advance.</para>
/// </summary>
internal static class RemoteStorySync
{
    /// <summary>How long a peer's story record stays believed after its last packet. Matches the
    /// env-clock staleness window: the extras packet runs at
    /// <see cref="NetProtocol.ExtrasSendRateHz"/> = 5 Hz, so three seconds is fifteen missed
    /// packets — a peer that quiet is gone, not merely lagging.</summary>
    private const float PeerStaleSeconds = 3f;

    /// <summary>
    /// How long this client keeps announcing a dialog it has FINISHED, after its own box closed.
    ///
    /// <para>THIS NUMBER IS THE FEATURE'S REAL SAFETY MARGIN, so it is generous rather than tight.
    /// The finished statement is the one that unlocks somebody, and the client that most needs it
    /// is the one whose story box opens LATE — a slower machine still loading the scenario while a
    /// faster player has already read the narrative and clicked through. A 5-second window would
    /// have covered packet loss and nothing else; a minute covers the whole realistic load-skew
    /// spread between three PCs. It costs 9 payload bytes at
    /// <see cref="NetProtocol.ExtrasSendRateHz"/> = 5 Hz — ~45 B/s, for one minute, once per
    /// scenario narrative.</para>
    ///
    /// <para>It is BOUNDED rather than permanent for two reasons: the idle packet must go back to
    /// being byte-identical to the previous build's for the rest of the scenario, and a later
    /// re-show of the same message (a scenario restart) must never be met with a stale "already
    /// done" that skips it before anyone reads a word.</para>
    ///
    /// <para>WHAT THIS DOES NOT COVER, stated rather than hidden: a peer whose box opens more than
    /// a minute after ours, or one still on message 1 of a queue while we are on message 2. Both
    /// degrade to exactly today's behaviour — that player clicks their own dialog through — so the
    /// failure direction is the pre-feature one, never a new one.</para>
    /// </summary>
    private const float FinishedLingerSeconds = 60f;

    /// <summary>Movement epsilon for "the local user moved the window", in the frame's own world
    /// units: 5 mm at diorama scale, the same threshold <c>GrabbableModal</c> uses to decide a host
    /// is moving. Below it nothing a hand does is distinguishable from float noise.</summary>
    private const float MoveEpsilonMeters = 0.005f;

    /// <summary>Rotation epsilon for the same test (degrees).</summary>
    private const float MoveEpsilonDegrees = 0.5f;

    /// <summary>Size epsilon for the same test (grab factor). One wire code is 0.01, so half a code
    /// is the smallest change that could ever survive quantisation.</summary>
    private const float MoveEpsilonSize = 0.005f;

    /// <summary>How long the local pose must stand still before the move counts as FINISHED and the
    /// stamp is bumped. A drag is continuous; bumping per frame would make "last mover" mean "last
    /// frame" and let two people fight at 5 Hz. One quarter second is well under the time it takes
    /// to let go and well over a frame.</summary>
    private const float MoveSettleSeconds = 0.25f;

    /// <summary>What a peer last said about its story box. Value type, one per sender, replaced
    /// whole on every packet — the decision family's "full state, newest wins" contract.</summary>
    private readonly struct PeerStory
    {
        public PeerStory(byte flags, byte page, byte pageCount, uint key, byte poseStamp,
                         byte sizeCode, RigPose pose, float at)
        {
            Flags = flags;
            Page = page;
            PageCount = pageCount;
            Key = key;
            PoseStamp = poseStamp;
            SizeCode = sizeCode;
            Pose = pose;
            At = at;
        }

        public readonly byte Flags;
        public readonly byte Page;
        public readonly byte PageCount;
        public readonly uint Key;
        public readonly byte PoseStamp;
        public readonly byte SizeCode;
        public readonly RigPose Pose;

        /// <summary>Local unscaled time this arrived — the staleness clock and, for the pose, the
        /// "last mover" clock. Deliberately a LOCAL time: peers share no clock and the record
        /// carries no timestamp, so ordering across senders is arrival ordering here.</summary>
        public readonly float At;

        public bool Open => (Flags & NetProtocol.StoryOpenBit) != 0;
        public bool Finished => (Flags & NetProtocol.StoryFinishedBit) != 0;
        public bool HasPose => (Flags & NetProtocol.StoryPoseBit) != 0;
        public int PageIndex => Page == NetProtocol.StoryPageNone ? -1 : Page;
    }

    private static readonly Dictionary<int, PeerStory> Peers = new();

    /// <summary>Per-peer bookkeeping for the LAST-MOVER election: the pose stamp we last saw from
    /// that peer, and the local time it last CHANGED. A peer that keeps re-sending an unchanged
    /// stamp is not moving anything and must not keep winning the election.</summary>
    private static readonly Dictionary<int, byte> PeerPoseStamp = new();
    private static readonly Dictionary<int, float> PeerPoseChangedAt = new();

    // ---- local state ------------------------------------------------------------------------

    /// <summary>Content key of the dialog we last saw open here. Kept after the box closes so the
    /// FINISHED announcement can name it (see <see cref="FinishedLingerSeconds"/>).</summary>
    private static uint _localKey;
    private static byte _localPageCount;

    /// <summary>Unscaled time the finished announcement stops being written. 0 = not announcing.</summary>
    private static float _finishedUntil;

    /// <summary>The pose/size we last read from — or wrote to — the story window's grab frame.
    /// A difference from this is what "the local user moved it" MEANS here: the frame is written by
    /// exactly three things (the grab handle, ModalFallback's own placement, and this class), and
    /// the other two are both recorded into these fields the moment they happen.</summary>
    private static Vector3 _lastFramePos;
    private static Quaternion _lastFrameRot = Quaternion.identity;
    private static float _lastFrameSize = 1f;
    private static bool _haveFrameBaseline;

    /// <summary>True once the local user has moved/resized the story window during THIS open — the
    /// gate on writing a pose block at all. Until a human touches it, every client keeps the
    /// placement its own gaze-spawn chose, which is the only placement that is guaranteed
    /// reachable for that player.</summary>
    private static bool _localPoseOwned;

    /// <summary>A local move is in progress; the stamp is bumped when it settles.</summary>
    private static bool _localMoving;
    private static float _localMoveSettleAt;

    /// <summary>The wire stamp for our own pose — bumped once per completed local move.</summary>
    private static byte _localPoseStamp;

    /// <summary>Which peer's pose we are currently following (0 = ours / none), and the stamp we
    /// applied from them, so the same pose is applied once rather than every frame.</summary>
    private static int _followingPeer;
    private static byte _followedStamp;
    private static bool _followedStampValid;

    // ---- diagnostics ------------------------------------------------------------------------

    private static int _lastLoggedPage = int.MinValue;
    private static uint _lastLoggedKey;
    private static string _lastIgnoreReason = string.Empty;
    private static float _nextIgnoreLogAt;

    /// <summary>Throttle for the "ignored, and why" line: a reason that does not change is logged
    /// at most this often, so a peer stuck on a foreign dialog cannot flood the log.</summary>
    private const float IgnoreLogThrottleSeconds = 5f;

    /// <summary>Drop everything on session end / shutdown, so a new session re-derives every fact
    /// rather than inheriting one. Nothing here owns a GameObject, so this IS the teardown.</summary>
    internal static void Reset()
    {
        Peers.Clear();
        PeerPoseStamp.Clear();
        PeerPoseChangedAt.Clear();
        _localKey = 0;
        _localPageCount = 0;
        _finishedUntil = 0f;
        _haveFrameBaseline = false;
        _localPoseOwned = false;
        _localMoving = false;
        _localMoveSettleAt = 0f;
        _localPoseStamp = 0;
        _followingPeer = 0;
        _followedStamp = 0;
        _followedStampValid = false;
        _lastLoggedPage = int.MinValue;
        _lastLoggedKey = 0;
        _lastIgnoreReason = string.Empty;
        _nextIgnoreLogAt = 0f;
    }

    // ---- identity ---------------------------------------------------------------------------

    /// <summary>
    /// Content hash of a dialog — FNV-1a over the page count and then, per page, the page's
    /// LOCALIZATION KEY and speaking-character guid.
    ///
    /// <para>WHY THOSE FIELDS AND NOT THE RENDERED TEXT. <c>DialogLineDTO.text</c> is the KEY
    /// (<c>CLevelMessagePage.PageTextKey</c> / <c>ScenarioDialogueLine.Text</c>, DialogLineDTO.cs:25
    /// and 45); the box translates it only when it paints (UICharacterStoryBox.cs:165/204). The
    /// <c>title</c> field, by contrast, is already translated at construction (DialogLineDTO.cs:40),
    /// so hashing it would make two players in different languages disagree about which dialog they
    /// are holding — and disagreement here means the sync silently stops working. Keys and guids
    /// come from the same YML on every machine.</para>
    ///
    /// <para>A key is a MATCH GATE, never an instruction: the only thing a receiver does with a
    /// mismatch is nothing. So a hash collision cannot advance a wrong dialog to a wrong page — it
    /// would have to collide across two dialogs open at the same moment in the same scenario, and
    /// the consequence would still be bounded by the receiver's own page count.</para>
    ///
    /// <para>INTERNAL, NOT PRIVATE, SINCE ModBuild 222 — and that is the whole of this file's change
    /// for the map-room work. <see cref="RemoteMapStory"/> hashes the MAP story box, which is a
    /// different controller but the identical <c>UICharacterStoryBox</c> holding the identical
    /// <c>List&lt;DialogLineDTO&gt;</c>, so it needs THIS function and not a copy of it: two
    /// implementations of one content hash is exactly how two records that must agree stop
    /// agreeing. Nothing else here is shared and nothing about record 19's behaviour changed.</para>
    /// </summary>
    internal static uint HashDialog(List<DialogLineDTO>? pages)
    {
        if (pages == null || pages.Count == 0)
            return 0u;
        unchecked
        {
            const uint prime = 16777619u;
            uint h = 2166136261u;
            h = (h ^ (uint)pages.Count) * prime;
            for (int i = 0; i < pages.Count; i++)
            {
                DialogLineDTO line = pages[i];
                h = HashString(h, line?.text);
                h = HashString(h, line?.character);
            }
            // 0 is the reserved "no dialog" value; fold it away rather than lose the gate.
            return h == 0u ? 1u : h;
        }
    }

    private static uint HashString(uint h, string? s)
    {
        unchecked
        {
            const uint prime = 16777619u;
            h = (h ^ 0xFFu) * prime; // field separator: "ab"+"c" must not hash like "a"+"bc"
            if (s == null)
                return h;
            for (int i = 0; i < s.Length; i++)
                h = (h ^ s[i]) * prime;
            return h;
        }
    }

    /// <summary>The live story box, or null when there is no scenario narrative on screen. Never
    /// throws and never creates anything.</summary>
    private static UICharacterStoryBox? Box()
    {
        if (!Singleton<StoryController>.IsInitialized)
            return null;
        StoryController sc = Singleton<StoryController>.Instance;
        if (sc == null || sc.window == null || !sc.IsVisible)
            return null;
        return sc.dialogBox;
    }

    // ---- send side --------------------------------------------------------------------------

    /// <summary>
    /// Fill the story fields of the outgoing extras packet. Called once per extras send from
    /// <c>NetAvatarDriver</c>; leaves <see cref="PresenceState.HasStorySync"/> false — and
    /// therefore the whole record absent, and the packet byte-identical to the previous build's —
    /// whenever no story box stands here and none has just finished.
    /// </summary>
    internal static void Sample(ref PresenceState extras)
    {
        float now = Time.unscaledTime;
        UICharacterStoryBox? box = Box();

        if (box == null)
        {
            // The box is down. Keep announcing the dialog we just finished for a bounded while, so
            // the statement that unlocks a peer survives packet loss; then fall completely silent.
            TrackFrame(reset: true);
            if (_localKey == 0u || now >= _finishedUntil)
            {
                _finishedUntil = 0f;
                _localKey = 0u;
                return;
            }
            extras.HasStorySync = true;
            extras.StoryFlags = NetProtocol.StoryFinishedBit;
            extras.StoryPage = NetProtocol.EncodeStoryPage(_localPageCount);
            extras.StoryPageCount = _localPageCount;
            extras.StoryKey = _localKey;
            return;
        }

        List<DialogLineDTO>? pages = box.dialogs;
        uint key = HashDialog(pages);
        if (key == 0u)
            return; // a box with no pages is a box mid-teardown; there is nothing to name.

        if (key != _localKey)
        {
            // A NEW dialog (the queue moved on, or the scenario just started): the pose ownership
            // is per-open, so a placement somebody chose for the last message does not follow the
            // next one around.
            _localPoseOwned = false;
            _localMoving = false;
            _followingPeer = 0;
            _followedStampValid = false;
            _haveFrameBaseline = false;
        }
        _localKey = key;
        _localPageCount = (byte)Mathf.Clamp(pages!.Count, 0, NetProtocol.StoryPageMax);
        _finishedUntil = now + FinishedLingerSeconds;

        extras.HasStorySync = true;
        extras.StoryFlags = NetProtocol.StoryOpenBit;
        extras.StoryPage = NetProtocol.EncodeStoryPage(box.currentDialogIndex);
        extras.StoryPageCount = _localPageCount;
        extras.StoryKey = key;

        TrackFrame(reset: false);
        // THE DRAG ITSELF TRAVELS, NOT ONLY ITS RESULT (ModBuild 226) — record 21's sibling change,
        // for the same user report ("sollen auch die BEWEGUNG und die Position voll übertragen
        // (flüssig, wie bei der Position des Boards auch)"). `_localPoseOwned` is set by TrackFrame
        // only after MoveSettleSeconds of STILLNESS, so a window being dragged for the first time
        // published nothing at all while it moved and its whole journey arrived as one jump; no
        // receiver-side easing can invent frames it never received. `_localMoving` is therefore a
        // second reason to publish — but deliberately NOT a reason to bump `_localPoseStamp`, which
        // elects the room's last mover and would let two draggers trade the window at the send rate.
        // A mid-drag record carries a FRESH POSE under an UNCHANGED STAMP, which is why ResolvePose
        // now elects on the stamp and decides whether to apply on the pose VALUE.
        if ((_localPoseOwned || _localMoving)
            && ModalFallback.TryGetStoryGrab(out GrabbableModal? grab) && grab != null
            && TryReadFrame(grab, out Vector3 pos, out Quaternion rot, out float size)
            && TryToAnchor(pos, rot, out Vector3 localPos, out Quaternion localRot))
        {
            extras.StoryFlags |= NetProtocol.StoryPoseBit;
            extras.StoryPoseStamp = _localPoseStamp;
            extras.StorySizeCode = NetProtocol.EncodeStorySize(size);
            extras.StoryPose = new RigPose { Position = localPos, Rotation = localRot };
        }
    }

    /// <summary>
    /// Watch the story window's grab frame for a move the LOCAL user made, and bump
    /// <see cref="_localPoseStamp"/> once when that move settles.
    ///
    /// <para>There is no "is grabbed" flag to read on <c>GrabbableModal</c>, and this needs none:
    /// the frame is written by exactly three things — the grab handle, ModalFallback's own spawn /
    /// re-place, and this class's own apply — and the latter two both write their result into the
    /// baseline as they happen. Anything left over is a hand. The baseline is dropped whenever the
    /// window is not <c>GrabVisible</c> (still behind the reveal gate, render-hidden, or gone), so
    /// the pre-reveal placement pass can never be mistaken for a user's choice.</para>
    /// </summary>
    private static void TrackFrame(bool reset)
    {
        if (reset || !ModalFallback.TryGetStoryGrab(out GrabbableModal? grab) || grab == null
            || !TryReadFrame(grab, out Vector3 pos, out Quaternion rot, out float size))
        {
            _haveFrameBaseline = false;
            _localMoving = false;
            return;
        }

        if (!_haveFrameBaseline)
        {
            _lastFramePos = pos;
            _lastFrameRot = rot;
            _lastFrameSize = size;
            _haveFrameBaseline = true;
            return;
        }

        float eps = MoveEpsilonMeters * Mathf.Max(PanelLayout.WorldScale, 0.01f);
        bool moved = (pos - _lastFramePos).sqrMagnitude > eps * eps
                     || Quaternion.Angle(rot, _lastFrameRot) > MoveEpsilonDegrees
                     || Mathf.Abs(size - _lastFrameSize) > MoveEpsilonSize;

        float now = Time.unscaledTime;
        if (moved)
        {
            _lastFramePos = pos;
            _lastFrameRot = rot;
            _lastFrameSize = size;
            _localMoving = true;
            _localMoveSettleAt = now + MoveSettleSeconds;
            // AND THE SPAWN ANCHOR IS SPENT FROM THE FIRST MILLIMETRE (ModBuild 243), not from the
            // settle — the anchor must be over the instant a hand takes the window, never one
            // MoveSettleSeconds later. The mirror of the same line in RemoteMapStory for the map
            // room's kinds; see the SHARED WINDOW ANCHOR block in ArcSeats.cs.
            WorldUI.ModalFallback.NoteSharedAnchorSpent(
                WorldUI.SharedWindowKind.ScenarioStory,
                "this client moved its own scenario story window by hand");
            // The local user is moving it: stop following anybody. Never yank a panel out of a
            // hand, and never fight a hand at 5 Hz.
            _followingPeer = 0;
            _followedStampValid = false;
            return;
        }
        if (_localMoving && now >= _localMoveSettleAt)
        {
            _localMoving = false;
            _localPoseOwned = true;
            unchecked { _localPoseStamp++; }
            VRLog.Info("Net", $"Story window MOVED locally: '{ModalFallback.StoryWindowLogName}' " +
                              $"now at anchor-local {DescribeAnchorLocal(_lastFramePos, _lastFrameRot)} " +
                              $"size {_lastFrameSize:0.00}x — pose stamp {_localPoseStamp}, record 19 " +
                              "starts carrying a pose block, and this client becomes the LAST MOVER " +
                              "every peer follows. The pose travels in seat-anchor-local REAL metres " +
                              "(divided by this client's diorama WorldScale), never as a world point: " +
                              "peers sit elsewhere and zoom differently, so a world point would put " +
                              "the window somewhere none of them chose.");
        }
    }

    /// <summary>Read the grab frame's world pose and the user's size factor, or false when the
    /// window is not currently a grabbable, revealed float.</summary>
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

    // ---- the shared frame -------------------------------------------------------------------

    /// <summary>
    /// World pose → the frame the record travels in: the offset from THIS client's seat anchor
    /// (<c>PanelLayout.TryGetAnchor</c> — the orbit focus plus the cached seat yaw, NOT the live
    /// head, so nothing here re-orients with head movement), expressed in REAL metres by dividing
    /// out the local diorama <c>WorldScale</c>, and a rotation relative to that same yaw.
    /// </summary>
    private static bool TryToAnchor(Vector3 worldPos, Quaternion worldRot,
                                    out Vector3 localPos, out Quaternion localRot)
    {
        localPos = Vector3.zero;
        localRot = Quaternion.identity;
        if (!PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion yaw))
            return false;
        float scale = Mathf.Max(PanelLayout.WorldScale, 0.01f);
        localPos = Quaternion.Inverse(yaw) * (worldPos - anchor) / scale;
        localRot = Quaternion.Inverse(yaw) * worldRot;
        return true;
    }

    /// <summary>The inverse of <see cref="TryToAnchor"/>, against the RECEIVER's own seat and own
    /// zoom — which is the whole point: the same record puts the window at the same apparent place
    /// and the same apparent size in every headset.</summary>
    private static bool TryToWorld(Vector3 localPos, Quaternion localRot,
                                   out Vector3 worldPos, out Quaternion worldRot)
    {
        worldPos = Vector3.zero;
        worldRot = Quaternion.identity;
        if (!PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion yaw))
            return false;
        float scale = Mathf.Max(PanelLayout.WorldScale, 0.01f);
        worldPos = anchor + yaw * (localPos * scale);
        worldRot = yaw * localRot;
        return true;
    }

    private static string DescribeAnchorLocal(Vector3 worldPos, Quaternion worldRot) =>
        TryToAnchor(worldPos, worldRot, out Vector3 p, out Quaternion r)
            ? $"({p.x:0.00},{p.y:0.00},{p.z:0.00}) m / {r.eulerAngles.y:0}° yaw"
            : "(no seat anchor yet)";

    // ---- receive side -----------------------------------------------------------------------

    /// <summary>
    /// Take one peer's freshly parsed extras packet. Pure bookkeeping — nothing is driven here, so
    /// a burst of packets in one frame costs one apply, not one per packet.
    /// </summary>
    internal static void Observe(int senderId, in PresenceState p)
    {
        if (senderId <= 0)
            return;
        float now = Time.unscaledTime;
        if (!p.HasStorySync)
        {
            // Absence is a defined state, not "keep the last one": that peer has no story box.
            Peers.Remove(senderId);
            PeerPoseStamp.Remove(senderId);
            PeerPoseChangedAt.Remove(senderId);
            return;
        }

        Peers[senderId] = new PeerStory(p.StoryFlags, p.StoryPage, p.StoryPageCount, p.StoryKey,
                                        p.StoryPoseStamp, p.StorySizeCode, p.StoryPose, now);

        if ((p.StoryFlags & NetProtocol.StoryPoseBit) == 0)
        {
            PeerPoseStamp.Remove(senderId);
            PeerPoseChangedAt.Remove(senderId);
            return;
        }
        // The LAST-MOVER clock ticks on a CHANGED stamp only. A peer re-sending the same pose five
        // times a second is not moving anything and must not keep winning the election.
        if (!PeerPoseStamp.TryGetValue(senderId, out byte had) || had != p.StoryPoseStamp)
        {
            PeerPoseStamp[senderId] = p.StoryPoseStamp;
            PeerPoseChangedAt[senderId] = now;
        }
    }

    /// <summary>
    /// Apply whatever the room has agreed on to THIS client's story box and story window. Called
    /// once per frame from <c>NetAvatarDriver.ApplyPending</c>.
    ///
    /// <para>The two halves are deliberately independent: the PAGE half never consults the window,
    /// and the POSE half never gates the page. That separation is what guarantees the pose feature
    /// can never re-introduce the lock it was shipped alongside.</para>
    /// </summary>
    internal static void Resolve()
    {
        PruneStale();
        UICharacterStoryBox? box = Box();
        if (box == null)
        {
            _followingPeer = 0;
            _followedStampValid = false;
            return;
        }

        uint key = HashDialog(box.dialogs);
        if (key == 0u)
            return;

        ResolvePage(box, key);
        ResolvePose(key);
    }

    private static void PruneStale()
    {
        if (Peers.Count == 0)
            return;
        float now = Time.unscaledTime;
        List<int>? drop = null;
        foreach (KeyValuePair<int, PeerStory> kv in Peers)
        {
            if (now - kv.Value.At <= PeerStaleSeconds)
                continue;
            (drop ??= new List<int>(2)).Add(kv.Key);
        }
        if (drop == null)
            return;
        for (int i = 0; i < drop.Count; i++)
        {
            Peers.Remove(drop[i]);
            PeerPoseStamp.Remove(drop[i]);
            PeerPoseChangedAt.Remove(drop[i]);
        }
    }

    private static void ResolvePage(UICharacterStoryBox box, uint key)
    {
        int localPage = box.currentDialogIndex;
        int localCount = box.dialogs?.Count ?? 0;

        int remotePage = -1;
        bool remoteFinished = false;
        int furthestPeer = 0;
        int finishedPeer = 0;
        int matching = 0;
        foreach (KeyValuePair<int, PeerStory> kv in Peers)
        {
            PeerStory s = kv.Value;
            if (s.Key != key)
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

        int target = NetProtocol.ResolveStoryPage(localPage, localCount, remotePage, remoteFinished);
        if (target < 0)
        {
            if (matching == 0 && Peers.Count > 0)
                NoteIgnored($"{Peers.Count} peer(s) published a story record but none for THIS " +
                            $"dialog (local key 0x{key:X8}) — they are holding a different message, " +
                            "so nothing here may be advanced");
            return;
        }

        // The first line has not been painted yet: the box is still running its open animation, and
        // the animator's own OnAnimationFinished will call ShowLine(0) afterwards
        // (UICharacterStoryBox.cs:83-86). Driving a page now would be undone — and undone
        // BACKWARDS — a fraction of a second later. Wait; the next packet is at most 200 ms away.
        if (localPage < 0)
        {
            NoteIgnored($"page {target} is wanted but this box has not shown its first line yet " +
                        "(currentDialogIndex −1, the open animation is still running) — the " +
                        "animator's own ShowLine(0) must land first or it would drag the dialog " +
                        "backwards");
            return;
        }
        if (StoryController.DisplayDelayInEffect)
        {
            NoteIgnored($"page {target} is wanted but StoryController.DisplayDelayInEffect is set " +
                        "(a scripted message is inside its display delay) — the box on screen is " +
                        "not yet the one the wire is talking about");
            return;
        }

        string who = remoteFinished
            ? $"player {finishedPeer} clicked THROUGH the end"
            : $"player {furthestPeer} advanced to page {remotePage}";

        // THE SEAM. ShowLine takes an ABSOLUTE index and is what the game's own click path calls
        // (Skip → ShowNextLine → ShowLine(currentDialogIndex + 1), UICharacterStoryBox.cs:239-248).
        // Driving it directly rather than replaying clicks is what makes this idempotent and
        // late-joiner safe, and it sidesteps skipButton.interactable being false mid-animation
        // (UICharacterStoryBox.cs:112-116) — a synthetic click would simply be dropped there.
        // At or past the page count it calls Hide() (line 174-177), which runs the game's whole
        // close chain: Hide → onFinish → StoryController.OnFinishShow → ShowNext, and THAT is
        // where TryUnblockedUpdate() and this client's own GameLoadedAndClientReady live
        // (StoryController.cs:122-141). Nothing of ours is sent; the idle client unlocks itself.
        box.ShowLine(target);

        bool terminal = target >= localCount;
        VRLog.Info("Net", $"Story window APPLIED: '{ModalFallback.StoryWindowLogName}' " +
                          $"(dialog key 0x{key:X8}, {localCount} page(s) here) moved from page " +
                          $"{localPage} to {target}{(terminal ? " (past the last page → the box closes)" : "")} " +
                          $"— {who}. Record 19 carries the ABSOLUTE page, so two players clicking at " +
                          "once both publish the same number and no page is skipped; re-applying it " +
                          $"is a no-op (ResolveStoryPage returns −1 for anything at or below {target}). " +
                          (terminal
                              ? "This client now runs the game's OWN ShowLine → Hide → OnFinishShow → " +
                                "ShowNext chain, which is what releases ActionProcessor.LockProcessingAction " +
                                "and sends this client's own GameLoadedAndClientReady — the lock is gone " +
                                "because THIS machine finished its dialog, not because anything was sent."
                              : "MEASURED here: the local page index before and after. ASSUMED: that the " +
                                "peer is reading the same text — the dialog key is what tests that, and a " +
                                "mismatch ignores the record instead of guessing."));
        _lastLoggedPage = target;
        _lastLoggedKey = key;
    }

    private static void ResolvePose(uint key)
    {
        if (_localMoving)
            return; // a hand owns it right now; never fight a hand.

        // LAST MOVER WINS. Among peers holding the same dialog and publishing a pose, follow the
        // one whose stamp changed most recently.
        int bestPeer = 0;
        float bestAt = float.NegativeInfinity;
        foreach (KeyValuePair<int, PeerStory> kv in Peers)
        {
            PeerStory s = kv.Value;
            if (s.Key != key || !s.HasPose)
                continue;
            if (!PeerPoseChangedAt.TryGetValue(kv.Key, out float at))
                continue;
            if (at > bestAt)
            {
                bestAt = at;
                bestPeer = kv.Key;
            }
        }
        if (bestPeer == 0)
            return;

        // Our own move outranks a peer move that is older than it. Ownership is per-open, so
        // "older" is measured on the same local clock the peer's arrival is measured on.
        if (_localPoseOwned && bestAt < _localMoveSettleAt)
            return;

        PeerStory owner = Peers[bestPeer];

        // ELECT ON THE STAMP, DECIDE ON THE POSE (ModBuild 226). The old early-out here was
        // `_followedStamp == owner.PoseStamp`, which was exactly right while a pose block only ever
        // existed for a FINISHED move: same stamp meant same pose meant nothing to do. Now that the
        // sender also publishes DURING a drag under a deliberately unchanged stamp, that test would
        // drop every mid-drag pose and leave the receiver with the same end-of-drag jump it had
        // before. The stamp still elects the last mover above; the pose VALUE decides here, against
        // the SAME epsilons TrackFrame uses to call something a move — a difference too small for it
        // to be a move is too small to be worth writing, and writing it anyway would hand
        // TrackFrame's baseline something it might read back as a local hand.
        if (!ModalFallback.TryGetStoryGrab(out GrabbableModal? grab) || grab == null)
        {
            NoteIgnored($"player {bestPeer} published a window pose but this client has no floated " +
                        "story window to place (not converted, or still behind the reveal gate) — " +
                        "the PAGE sync is untouched by this, which is why an unplaceable window can " +
                        "never hold anybody up");
            return;
        }
        if (!TryToWorld(owner.Pose.Position, owner.Pose.Rotation,
                        out Vector3 worldPos, out Quaternion worldRot))
        {
            NoteIgnored($"player {bestPeer} published a window pose but this client has no seat " +
                        "anchor yet (PanelLayout.TryGetAnchor false — no rig/camera controller) — " +
                        "the local placement stands");
            return;
        }

        float size = NetProtocol.DecodeStorySize(owner.SizeCode);

        float applyEps = MoveEpsilonMeters * Mathf.Max(PanelLayout.WorldScale, 0.01f);
        if (_haveFrameBaseline && _followingPeer == bestPeer
            && (worldPos - _lastFramePos).sqrMagnitude <= applyEps * applyEps
            && Quaternion.Angle(worldRot, _lastFrameRot) <= MoveEpsilonDegrees
            && Mathf.Abs(size - _lastFrameSize) <= MoveEpsilonSize)
            return; // already standing exactly there — applying it again would be a no-op write.

        // A drag now arrives as a STREAM of poses under one stamp, and the paragraph below was
        // written for one arrival per move. Say the whole thing once per followed peer / per settled
        // move and nothing for the frames in between; the pose is applied either way.
        bool newFollow = _followingPeer != bestPeer || !_followedStampValid
                         || _followedStamp != owner.PoseStamp;

        var frameOwner = (IPanelGrabOwner)grab;
        Transform? frame = frameOwner.GrabRoot;
        if (frame != null)
            frame.localScale = Vector3.one
                               * Mathf.Clamp(size, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);
        grab.PlaceFrameAt(worldPos, worldRot);

        // AND THE SPAWN ANCHOR IS SPENT (ModBuild 243). This is the hole the shared-anchor block in
        // ArcSeats.cs names and leaves open because it belongs to this file: record 19 carries kind
        // 1, and without this line a peer's pose applied in the few hundred milliseconds between the
        // scenario story window's spawn and its reveal could be overwritten by the pre-reveal
        // re-place putting the anchor back. The window has a REAL pose now — somebody moved it — so
        // the anchor is over for its life, exactly as the user's narrowing requires ("nur die
        // initiale Spawnposition"). Cheap to call every arrival: the latch adds once.
        WorldUI.ModalFallback.NoteSharedAnchorSpent(
            WorldUI.SharedWindowKind.ScenarioStory,
            $"a peer's pose was applied (player {bestPeer}, record 19)");

        // Record what WE just wrote as the movement baseline, or the very next tick would read our
        // own write back as a local user move and start a stamp war.
        _lastFramePos = worldPos;
        _lastFrameRot = worldRot;
        _lastFrameSize = size;
        _haveFrameBaseline = true;
        _localPoseOwned = false;
        _followingPeer = bestPeer;
        _followedStamp = owner.PoseStamp;
        _followedStampValid = true;

        if (!newFollow)
            return;

        VRLog.Info("Net", $"Story window POSE APPLIED: '{ModalFallback.StoryWindowLogName}' " +
                          $"(dialog key 0x{key:X8}) follows player {bestPeer} (pose stamp " +
                          $"{owner.PoseStamp}, the most recently CHANGED stamp in the room) — " +
                          $"anchor-local ({owner.Pose.Position.x:0.00},{owner.Pose.Position.y:0.00}," +
                          $"{owner.Pose.Position.z:0.00}) m, size {size:0.00}x → world " +
                          $"({worldPos.x:0.00},{worldPos.y:0.00},{worldPos.z:0.00}) at this client's " +
                          $"own seat and its own WorldScale {PanelLayout.WorldScale:0.000}. OWNERSHIP " +
                          "IS LAST-MOVER, not host: the pose only ever travels because a human " +
                          "deliberately dragged the window, and whoever drags it last owns it. " +
                          "MEASURED: the wire pose and the world pose written. ASSUMED: that this " +
                          "client's seat anchor is where the player is actually sitting — if the next " +
                          "log shows this world position far from the player's own panels, the anchor " +
                          "is stale, not the record.");
    }

    /// <summary>Log an "ignored, and why" line — once per changed reason, then at most every
    /// <see cref="IgnoreLogThrottleSeconds"/> while the same reason persists.</summary>
    private static void NoteIgnored(string reason)
    {
        float now = Time.unscaledTime;
        if (reason == _lastIgnoreReason && now < _nextIgnoreLogAt)
            return;
        _lastIgnoreReason = reason;
        _nextIgnoreLogAt = now + IgnoreLogThrottleSeconds;
        VRLog.Info("Net", $"Story window IGNORED: '{ModalFallback.StoryWindowLogName}' — {reason}. " +
                          $"(last applied page {(_lastLoggedPage == int.MinValue ? "none" : _lastLoggedPage.ToString())}" +
                          $" of dialog 0x{_lastLoggedKey:X8}.) Ignoring is always the safe direction " +
                          "here: a page not applied is re-offered by the next packet 200 ms later, " +
                          "while a page applied to the wrong dialog would skip somebody's text.");
    }
}
