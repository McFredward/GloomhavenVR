using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// THE FIXIERT BOARD'S ANCHOR: a shadow of the player's own rig frame, re-copied every frame in
/// the LAST phase before rendering. The pinned control board hangs under this transform, so its
/// position, its orientation AND its size are stored RIG-LOCALLY and re-derived by the transform
/// hierarchy — never by arithmetic this class or <see cref="PlayTray"/> performs per frame.
///
/// <para>WHY THE FRAME CHANGED (user, 2026-08-18, hardware round on ModBuild 159): "Das Board
/// zoomed immer noch im Fixiert modus mit — das soll nicht sein. Fixiert heißt in jeglicher
/// hinsicht fixiert und fix, EGAL wie man zoomed oder sich bewegt auch wenn man es in einer Hand
/// festhält." That sentence and his standing 2026-08-07 ruling ("Fixiert heißt: völlig unabhängig
/// vom Character, bewegt sich in KEINSTER Weise, außer es wird aktiv verschoben oder skaliert")
/// are both true in exactly ONE frame of reference — HIS. A board nailed to game-world coordinates
/// satisfies neither, because a zoom, a snap turn, a world grab, a teleport or a recentre moves
/// and rescales THE PLAYER; anything the player is not attached to therefore sweeps across their
/// view without a single line of code touching it. A board nailed to the RIG satisfies both: no
/// rig-driven change can move it or resize it in the player's eye, and physically WALKING still
/// changes what you see, because you are moving inside the space the board is nailed to.</para>
///
/// <para>THE TWO MEASURED DEFECTS THIS REPLACES, both from <c>.planning/debug/LogOutput.log</c> of
/// that round:
/// <list type="number">
/// <item><b>A ONE-FRAME LAG, proven by identity.</b> The old <c>SyncPinHolder</c> re-asserted the
///   holder's scale from the live rig inside <c>CardsDriver.Update</c> — a phase that runs BEFORE
///   <c>WorldGrab.Update</c> writes the new rig scale. The log prints the pair: L3467 reads rig
///   ×64.79, and L3474 reads "parent chain ×64.79 ÷ rig ×68.50" — the holder at frame N is EXACTLY
///   the rig scale of frame N−1. Across 223 <c>BOARD SIZE</c> lines the anchor ratio wandered
///   0.89…1.11 while the zoom swept 1.66×–11.98×, settling back to exactly 1.00 (79 lines) the
///   instant the pinch stopped: a ±10 % breathing of the board through every zoom gesture. Copying
///   the rig frame in LateUpdate, after every Update-phase rig writer and after the rig's own
///   world-tilt heal, removes the lag; the ratio is then 1.000 because both sides of it are the
///   same float read in the same frame.</item>
/// <item><b>THE DISTANCE TERM WAS NEVER FROZEN.</b> Angular size = size ÷ distance. ModBuild 159
///   froze the numerator (world size ÷ rig scale) and left the board standing at fixed WORLD
///   coordinates while the zoom rescaled the player about a pivot that is not the board — so the
///   distance in player metres changed with every zoom and the board grew and shrank in the eye
///   exactly as reported. Under this frame the board's rig-local position is constant, so the
///   distance is constant too, and the two terms can no longer disagree.</item>
/// </list></para>
///
/// <para>WHY THIS IS A SHADOW AND NOT A REAL CHILD OF THE RIG. Parenting the holder under
/// <c>VRRigDriver.RigRoot</c> would give the same invariant with no code at all — and would
/// DESTROY THE BOARD every time the rig is torn down, because <c>TearDownRig</c> destroys the rig
/// root and Unity takes its whole subtree with it (that is why <c>HandsDriver</c> rebuilds its
/// hands root from scratch after every rig rebuild). A pinned board must survive a rig rebuild:
/// carrying it across one is the behaviour the old <c>SyncPinHolder</c> case (2) existed for. So
/// the holder stays a <c>DontDestroyOnLoad</c> scene root and COPIES the rig's world pose and
/// world scale instead. The board's pose under it is untouched by that copy, which is what makes
/// the rig-local storage real: nothing here ever computes where the board should be.</para>
///
/// <para>THE RIG-REBUILD CARRY IS NOW FREE. A rebuild or a recentre hands us a different rig
/// transform; we copy THAT one and the board keeps its rig-local pose, i.e. the same place
/// relative to the player — which is what "my board stayed where I put it" means to a person
/// wearing a headset. The old explicit carry (cache the rig-relative pose every frame, re-apply it
/// when <c>RigPoseVersion</c> bumps) is gone with the arithmetic that needed it.</para>
///
/// <para>EXECUTION ORDER IS THE MECHANISM, so it is pinned. <see cref="DefaultExecutionOrder"/>
/// 20000 puts this <c>LateUpdate</c> after every default-order one — including
/// <c>VRRigDriver.LateUpdate</c>, which re-asserts the world tilt on the rig root — and before
/// <c>PerfFrameSplit</c>'s 30000 end-of-logic marker. This is one of the rare cases the codebase's
/// standing objection to the attribute does not apply (see <c>RigModule</c>): the order is not an
/// accident this would freeze, it is the property being asserted — "the anchor is the rig frame
/// that this frame RENDERS with".</para>
/// </summary>
[DefaultExecutionOrder(20000)]
internal sealed class TrayPinFrame : MonoBehaviour
{
    /// <summary>The transform whose WORLD pose is preserved across the very first rig seat — the
    /// pinned tray. Set by <see cref="PlayTray.ApplyFollowMode"/> right after the component is
    /// added. Null is legal (the frame then just tracks the rig).</summary>
    internal Transform? Payload;

    /// <summary>The rig transform the last seat copied, so a REBUILT rig is recognised as a
    /// different instance rather than by a version number.</summary>
    private Transform? _rig;

    /// <summary>True once a rig frame has been copied at least once. The FIRST seat preserves the
    /// payload's world pose (the pin was authored in world space, by a grab or by
    /// <c>PlaceAtHead</c>); every seat after it preserves the payload's RIG-LOCAL pose, which is
    /// the carry.</summary>
    private bool _seated;

    /// <summary>Number of times a DIFFERENT rig transform was adopted (rig rebuild / recentre that
    /// replaced the root). Reported in the board's diagnostic line as the count of carries that
    /// cost no arithmetic.</summary>
    internal int RigChanges { get; private set; }

    /// <summary>Whether the last seat found a live rig. False means the frame is holding its last
    /// world pose — between scenarios, or in the flat dev proxy where no rig exists at all.</summary>
    internal bool HasRig { get; private set; }

    /// <summary>The rig scale the last seat copied — the number the board's anchor ratio is a
    /// division by, surfaced so the diagnostic can state WHICH sample it is claiming 1.000
    /// against.</summary>
    internal float SeatedScale { get; private set; } = 1f;

    private float _nextRigChangeLog;

    /// <summary>
    /// Copy the rig's world pose and world scale onto this holder. Idempotent: calling it twice in
    /// a frame writes the same values, and the payload's pose UNDER the holder is never touched, so
    /// the board does not move in the player's frame no matter how often this runs. Called from
    /// <see cref="LateUpdate"/> (the authoritative one, after every rig writer) and once more from
    /// <c>PlayTray.SyncPinHolder</c> in the Update phase, so the size diagnostic and the size clamp
    /// that run there read an anchor from THIS frame rather than the previous one.
    /// </summary>
    internal void Seat()
    {
        Transform? rig = VRRigDriver.RigRoot;
        if (rig == null)
        {
            // No rig (between scenarios, menu boot, flat dev proxy): hold the last world pose. The
            // board stays exactly where it is until a rig exists again, and the seat that adopts
            // that rig carries it by its rig-local pose — so a rig rebuild is invisible even when
            // there were rigless frames in between.
            HasRig = false;
            return;
        }
        float scale = rig.lossyScale.x;
        if (!(scale > 1e-5f) || float.IsInfinity(scale) || float.IsNaN(scale))
            return; // a degenerate rig is not a frame; hold the last good one
        Vector3 pos = rig.position;
        if (float.IsNaN(pos.x) || float.IsInfinity(pos.x)
            || float.IsNaN(pos.y) || float.IsInfinity(pos.y)
            || float.IsNaN(pos.z) || float.IsInfinity(pos.z))
            return;

        HasRig = true;
        bool firstSeat = !_seated;
        bool rigChanged = !ReferenceEquals(rig, _rig);
        Transform self = transform;

        // FIRST SEAT ONLY: keep the board where it is in the WORLD. Everything that authors a pin
        // — a hand carrying the board, PlaceAtHead, RestorePose — writes a WORLD pose, and this is
        // the instant that world pose becomes a rig-local one. Every LATER seat deliberately does
        // NOT preserve it: preserving the world pose on a rig rebuild is precisely how a pinned
        // board used to be stranded at the old seat after a recentre.
        Vector3 keepPos = default;
        Quaternion keepRot = Quaternion.identity;
        bool keep = firstSeat && Payload != null;
        if (keep)
        {
            keepPos = Payload!.position;
            keepRot = Payload.rotation;
        }

        self.SetPositionAndRotation(pos, rig.rotation);
        self.localScale = Vector3.one * scale;
        SeatedScale = scale;

        if (keep)
            Payload!.SetPositionAndRotation(keepPos, keepRot);

        _rig = rig;
        _seated = true;
        if (rigChanged && !firstSeat)
        {
            RigChanges++;
            float now = Time.unscaledTime;
            if (now >= _nextRigChangeLog)
            {
                _nextRigChangeLog = now + 1f;
                VRLog.Info("Cards", $"Control board (FIXIERT) carried through a rig rebuild/recentre " +
                                    $"(carry #{RigChanges}): the board kept its place relative to the " +
                                    $"player and its anchor now reads the new rig's scale ×{scale:F2}. " +
                                    "Nothing was recomputed — the pin is stored rig-locally, so the " +
                                    "carry is what NOT acting looks like.");
            }
        }
    }

    /// <summary>The authoritative seat: LateUpdate runs after every Update-phase rig writer
    /// (WorldGrab's zoom/yaw solve, SnapTurn, Flight, Recenter) and, at execution order 20000,
    /// after <c>VRRigDriver.LateUpdate</c>'s world-tilt heal — so the frame copied here is the one
    /// the renderer is about to draw with. That is the whole cure for defect (A).</summary>
    private void LateUpdate() => TickGuard.Run("Cards.TrayPinFrame", _seat ??= Seat, "Cards");

    /// <summary>Cached tick delegate ([Optimize] CacheDelegates — a method group in a per-frame
    /// call allocates a fresh Action every frame).</summary>
    private System.Action? _seat;
}
