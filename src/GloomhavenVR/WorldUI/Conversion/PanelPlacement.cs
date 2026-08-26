using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Shared spawn / re-place clamp for movable world panels (the combat log, the enemy
/// reveal, the MP version dialog). Given the head pose it either produces a fresh spawn pose or
/// HEALS an existing/persisted pose into one GUARANTEED to sit inside the player's
/// forward field of view at a comfortable reading distance:
///
/// - yaw clamped to ±<see cref="MaxYawDeg"/> around world up,
/// - pitch clamped to [<see cref="MinPitchDeg"/>, <see cref="MaxPitchDeg"/>] (biased
///   slightly downward for reading),
/// - distance clamped to [<see cref="MinDistance"/>, <see cref="MaxDistance"/>] real
///   meters × the diorama scale,
/// - upright (zero roll/pitch on the panel itself), facing the head.
///
/// Item 2 (test #24): panels sometimes spawned far outside the view — e.g. the combat
/// log stranded off to the side after a Mixed-Reality toggle re-derived it from a stale
/// persisted offset. Routing EVERY show / first-place / re-derive through this clamp
/// makes that impossible: a candidate already in view passes through unchanged (the
/// clamp is idempotent inside the cone), an out-of-view one is pulled back in front of
/// the head. uGUI fronts render along −forward, so the returned rotation points the
/// panel's +Z AWAY from the head (same convention as the surfaces' own FaceHead).
/// </summary>
internal static class PanelPlacement
{
    /// <summary>Comfortable reading distance for a fresh spawn (real meters × diorama scale).</summary>
    internal const float SpawnDistance = 0.75f;

    /// <summary>A fresh spawn sits this far below eye level (real meters × diorama scale).</summary>
    internal const float SpawnDrop = 0.12f;

    /// <summary>Nearest a panel may sit from the head after a heal (real meters).</summary>
    internal const float MinDistance = 0.45f;

    /// <summary>Farthest a panel may sit from the head after a heal (real meters).</summary>
    internal const float MaxDistance = 1.4f;

    /// <summary>Half-angle of the horizontal keep-in-view cone (degrees).</summary>
    internal const float MaxYawDeg = 35f;

    /// <summary>Bottom of the vertical keep-in-view band (degrees; − = below eye level).</summary>
    internal const float MinPitchDeg = -30f;

    /// <summary>Top of the vertical keep-in-view band (degrees; + = above eye level).</summary>
    internal const float MaxPitchDeg = 20f;

    /// <summary>A heal that moves the panel less than this (real meters) is treated as a no-op.</summary>
    internal const float HealEpsilon = 0.02f;

    /// <summary>
    /// Fresh spawn pose: a comfortable reading distance straight in front of the head,
    /// slightly below eye level, upright and facing the head.
    /// <paramref name="worldScale"/> is the diorama scale (1 outside a scenario).
    /// </summary>
    internal static void Spawn(Camera head, float worldScale,
        out Vector3 position, out Quaternion rotation)
    {
        Transform h = head.transform;
        Vector3 fwd = Flatten(h.forward);
        position = h.position + fwd * (SpawnDistance * worldScale)
                              - Vector3.up * (SpawnDrop * worldScale);
        rotation = Facing(position, h.position);
    }

    /// <summary>
    /// Heal a candidate world pose into the forward FOV. Returns true when the pose was
    /// actually relocated (the candidate was outside the yaw/pitch cone or the distance
    /// band — callers persist the healed pose); false when it was already comfortably in
    /// view and passes through unchanged. <paramref name="rotation"/> always comes back
    /// upright, facing the head.
    /// </summary>
    internal static bool ClampIntoView(Camera head, float worldScale,
        ref Vector3 position, out Quaternion rotation)
    {
        Transform h = head.transform;
        Vector3 headPos = h.position;
        Vector3 headFwd = Flatten(h.forward);

        Vector3 toPanel = position - headPos;
        float dist = toPanel.magnitude;
        Vector3 dir = dist > 1e-4f ? toPanel / dist : headFwd;

        // Decompose into signed yaw (around world up) and pitch relative to the head.
        Vector3 flat = toPanel;
        flat.y = 0f;
        float yaw = flat.sqrMagnitude > 1e-6f
            ? Vector3.SignedAngle(headFwd, flat.normalized, Vector3.up)
            : 0f;
        float pitch = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;

        float clampedYaw = Mathf.Clamp(yaw, -MaxYawDeg, MaxYawDeg);
        float clampedPitch = Mathf.Clamp(pitch, MinPitchDeg, MaxPitchDeg);
        float clampedDist = Mathf.Clamp(dist, MinDistance * worldScale, MaxDistance * worldScale);

        // Rebuild a unit direction from the clamped yaw/pitch, then re-seat at the clamped distance.
        Vector3 horiz = Quaternion.AngleAxis(clampedYaw, Vector3.up) * headFwd;
        float pitchRad = clampedPitch * Mathf.Deg2Rad;
        Vector3 newDir = horiz * Mathf.Cos(pitchRad) + Vector3.up * Mathf.Sin(pitchRad);
        Vector3 newPos = headPos + newDir * clampedDist;

        bool moved = (newPos - position).sqrMagnitude > (HealEpsilon * worldScale) * (HealEpsilon * worldScale);
        position = newPos;
        rotation = Facing(newPos, headPos);
        return moved;
    }

    /// <summary>
    /// Upright orientation facing the head (uGUI front toward the viewer → +Z away).
    ///
    /// <para>THE one "faces the player" formula for mod panels — yaw-only, derived from the
    /// panel-to-head vector (not the raw gaze forward, so an off-centre panel still turns to the
    /// player). Shared with <c>ModalFallback</c>'s spawn facing and with <c>GrabbableModal</c>'s
    /// re-face-on-release, so a released menu ends up at exactly the orientation a freshly
    /// floated one has.</para>
    ///
    /// <para><b>THE THREE CALL SITES, AND WHICH OF THEM IS GATED (2026-08-22, requests 7b and 8).</b>
    /// Exactly one of them is:
    /// <list type="number">
    /// <item><b><c>GrabbableModal.OnGrabFinished</c> — the RELEASE re-face. GATED.</b> By
    /// <see cref="SharedWindows.IsShared"/> unconditionally (a window that belongs to the whole room
    /// must keep the orientation it is given, on every client and on the sender too), and for a
    /// private window by the player's own <c>[WorldUI] WindowFacing</c> dial. See that method.</item>
    /// <item><b><see cref="Spawn"/> — the one-shot facing at OPEN. NOT gated</b>, deliberately. It is
    /// what <c>ModalFallback.8.Convert</c> logs as "one-shot facing applied". A shared window has no
    /// agreed orientation when it opens — record 19/21 carry a pose block only once somebody has
    /// MOVED the window — so there is nothing to preserve and nothing to disagree with: each client
    /// simply places its own copy in its own view, exactly as it always has. Suppressing it would
    /// leave the window at the rotation the host happened to be built with, which is wrong for every
    /// player at once and is not what was asked for ("nach dem Greifen" is the request).</item>
    /// <item><b><see cref="ClampIntoView"/> — the heal. NOT gated</b>, same argument plus one: it
    /// runs only for panels that are not shared windows at all (the combat log, the enemy reveal,
    /// the MP version dialog), and its whole job is to recover a pose that is unusable.</item>
    /// </list>
    /// The one facing site that is NOT in this list is the presence-regain refloat, which reaches
    /// <see cref="Spawn"/> through <c>ModalFallback.ComputeHmdPose</c> and therefore re-faces AND
    /// re-positions a shared window a peer had placed. That one wants the skip the local
    /// <c>UserMoved</c> latch already gets — see <c>GrabbableModal.PeerPlaced</c>, which exists for
    /// it and which this lane could not wire up because the refloat is not its file.</para>
    /// </summary>
    internal static Quaternion Facing(Vector3 panelPos, Vector3 headPos)
    {
        Vector3 away = Flatten(panelPos - headPos);
        return Quaternion.LookRotation(away, Vector3.up);
    }

    /// <summary>Horizontal unit projection (falls back to world forward when degenerate).</summary>
    private static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward;
    }
}

/// <summary>
/// THE REVEALED-WINDOW POSE LOCK, AND THE INSTRUMENT THAT PROVES IT (ModBuild 197).
///
/// <para>THE RULING IT ENFORCES, in the user's own words (ModBuild 193, re-reported after 196):
/// <i>"Die Fenster verändern ständig ihre Position wenn ein neues Fenster gespawned wird oder
/// schließt. Das soll nicht sein — ohne explizite Bewegung vom User, sollen sie ihre Position
/// nicht verändern. Spawne die Fenster so, das alle im Sichtfeld passen aber einmal gespawned sind
/// sie fix."</i> Placement at spawn is free; after the window has been REVEALED its pose belongs to
/// the player and to nobody else.</para>
///
/// <para>WHY A WATCH AND NOT ANOTHER GUARD CLAUSE. The ruling was already implemented twice —
/// <c>ModalFallback.TickPoseRePlaceOne</c> refuses to move a revealed window (ModBuild 149) and the
/// ModBuild 183 per-set relayout was deleted outright (ModBuild 193) — and the report came back
/// anyway. Every guard in this codebase protects ONE call site; none of them can say whether some
/// OTHER call site wrote the pose, and eight rounds of "read the code and reason about it" is
/// exactly the failure mode the project has a standing rule against. This class watches the pose
/// VALUE instead: whatever moved it, announced or not, is measured, attributed and reported in real
/// millimetres, and — unless the writer announced itself as one the ruling allows — put back.</para>
///
/// <para>WHAT IT WATCHES. The mod-owned GRAB FRAME, not the game-owned host: the host is a follower
/// (<c>GrabbableModal.SyncHostToFrame</c> copies the frame's pose onto it every Update AND every
/// LateUpdate), so a direct host write is undone within a frame and is not what the player sees
/// persist. The frame is the authority, so the frame is the subject. Panels with no grab frame at
/// all fall back to the host — and the verdict line says which of the two it measured, because a
/// number whose subject is ambiguous is not a measurement.</para>
///
/// <para>WHAT COUNTS AS THE PLAYER'S OWN MOVE: any frame in which the shared grab handle reports a
/// hand on the window, plus <see cref="GrabSettleFrames"/> frames afterwards so
/// <c>GrabbableModal.OnGrabFinished</c>'s release re-face (a rotation-only write, one frame after
/// the last gripped frame) is attributed to the grab that caused it rather than to a phantom.</para>
///
/// <para>THE THREE VERDICTS ARE DISTINGUISHABLE ON PURPOSE (project rule: an instrument that cannot
/// tell its own failure modes apart agrees with every broken build). At the end of a window's float
/// exactly one line is printed:
/// <list type="number">
/// <item><b>NEVER ARMED</b> — the window never reached the revealed state while it floated, so
/// nothing was watched. This line is explicitly NOT evidence that its pose held still.</item>
/// <item><b>NO WRITES</b> — armed, N frames sampled over M seconds, zero post-reveal writes. This
/// is the only line that is evidence of stillness, and it carries the sample count so a watch that
/// ran for two frames cannot be mistaken for one that ran for two minutes.</item>
/// <item><b>WRITES FOUND</b> — the COUNT, the LARGEST single write in real millimetres, who did it,
/// and the same pair per caller.</item>
/// </list>
/// While a window is standing still the class is SILENT: no heartbeat, no per-frame line.</para>
/// </summary>
internal static class PanelPoseWatch
{
    /// <summary>A frame-pose change below this (real millimetres) is float noise, not a write.
    /// 1 mm is an order of magnitude under anything the eye can judge at reading distance and two
    /// orders under the smallest movement the user has ever reported.</summary>
    internal const float MoveEpsilonMm = 1.0f;

    /// <summary>A frame-rotation change below this is noise (matches the reveal gate's own
    /// <c>RevealRotEpsilonDeg</c>, so the two instruments cannot disagree about "still").</summary>
    internal const float TurnEpsilonDeg = 0.25f;

    /// <summary>Frames after the last gripped frame during which a write is still the player's:
    /// the release re-face (<c>GrabbableModal.OnGrabFinished</c>) lands one frame after the grip
    /// ends, and the handle's own lerps decay over a few more.</summary>
    internal const int GrabSettleFrames = 8;

    /// <summary>
    /// How many times the lock will put ONE window back before it concedes. A write war with a
    /// per-frame writer has no winner (ModBuild 179, the overrideSorting concession): if something
    /// re-writes the pose every frame, restoring it every frame makes the value ALTERNATE, which in
    /// MultiPass means the two eye passes can disagree. Past this count the lock stands down for
    /// that window, says so once, and keeps counting — so the log still names the writer.
    /// </summary>
    internal const int MaxCorrections = 8;

    /// <summary>
    /// "This never happened" for the frame stamps below. NOT <c>int.MinValue</c>, and the
    /// difference is load-bearing: every read is of the form <c>Time.frameCount - stamp</c>, and
    /// <c>now - int.MinValue</c> OVERFLOWS to a large NEGATIVE number, which passes every
    /// <c>&lt;= n</c> recency test — so an un-stamped window would report "the player is holding
    /// it" and "a placement announced this" forever, i.e. the instrument would agree with every
    /// broken build. A sentinel a million frames in the past cannot overflow (frame counts are
    /// small positives) and fails every recency test, which is what it is for.
    /// </summary>
    private const int NeverFrame = -1_000_000;

    /// <summary>Who asked to move a revealed window, as the verdict line names them.</summary>
    internal enum Writer
    {
        /// <summary>The player's own grab (or its release re-face). The ONE allowed writer.</summary>
        UserGrab,

        /// <summary>A mod placement that came through <c>ModalFallback.ComputeHmdPose</c> — the
        /// single funnel for the spawn, the presence-regain refloat and the pre-reveal re-place.
        /// Allowed (the doff/don rescue is a deliberate physical act by the player and is the only
        /// thing that can recover a window stranded behind them), but NOTED once per window: on a
        /// revealed window this is the "third MODAL SPAWN CLAMP line" the spawn log's own reading
        /// guide calls the bug signature.</summary>
        Placement,

        /// <summary>A REMOTE player's grab, mirrored onto a shared window's frame by
        /// <c>Net.RemoteStorySync</c> (record 19, the scenario story box) or <c>Net.RemoteMapStory</c>
        /// (record 21, the map story box and the quest popup). It is a human move too, so it is
        /// allowed and noted, never reverted — reverting it would silently break shared-window pose
        /// sync (both paths are stamp-guarded and would not re-send).
        ///
        /// <para>Since ModBuild 226 such a write may be followed by a short GLIDE of the window's
        /// DRAWN pose (GrabbableModal's remote pose easing). The lock does not see it and must not:
        /// its subject is the grab FRAME, which the easing never touches — the frame is set to the
        /// received pose exactly once, in the frame it arrives.</para></summary>
        Peer,

        /// <summary>Nobody announced this write. By construction it is not one the ruling allows,
        /// so it is refused: the locked pose is written back and the caller is named ONCE.</summary>
        Unattributed,
    }

    private sealed class Entry
    {
        internal string Name = "<window>";
        internal bool Seen;

        // ---- arming -------------------------------------------------------------------------
        internal bool Armed;
        internal float ArmedAt;
        internal int ArmedFrame;
        internal float ArmScale = 1f;
        internal int Samples;
        internal bool MeasuredFrame;      // false = the host was the subject (no grab frame)
        internal string NotArmedReason = "the window closed before it was ever revealed";

        // ---- the lock -----------------------------------------------------------------------
        internal Vector3 LockedPos;
        internal Quaternion LockedRot = Quaternion.identity;

        // ---- the announce token (valid for the frame it was written in, and the next) --------
        // Explicitly Unattributed rather than default(Writer): whichever member happens to sit
        // first in the enum must not become the meaning of "nobody said anything".
        internal Writer Announced = Writer.Unattributed;
        internal int AnnouncedFrame = NeverFrame;
        internal string AnnouncedWhy = "";

        // ---- the player ---------------------------------------------------------------------
        internal int LastGrabbedFrame = NeverFrame;

        // ---- what happened ------------------------------------------------------------------
        internal int Writes;
        internal float LargestMm;
        internal float LargestDeg;
        internal string LargestBy = "";
        internal readonly Dictionary<string, CallerTally> ByCaller = new(4);
        internal int Corrections;
        internal bool Conceded;
        internal readonly HashSet<string> RefusalLogged = new();
        internal bool PlacementNoted;
        internal bool PeerNoted;

        // ---- the size-change assertion (requirement 4) ---------------------------------------
        internal string? PendingSizeNote;
        internal int PendingSizeFrame = NeverFrame;
        internal int SizeChanges;
        internal float SizeChangeWorstMm;
        internal bool SizeMoveLogged;

        /// <summary>The window's DRAWN centre (host rect centre in world space) at the previous
        /// sample — the reference the size-change check measures against. See Track.</summary>
        internal Vector3 PrevCentre;

        internal bool HaveCentre;
    }

    private struct CallerTally
    {
        internal int Count;
        internal float LargestMm;
    }

    private static readonly Dictionary<ConvertedPanel, Entry> Entries = new(8);
    private static readonly List<ConvertedPanel> RetireScratch = new(4);

    /// <summary>Mark every tracked window unseen; <see cref="EndTick"/> retires whatever the tick's
    /// <see cref="Track"/> calls did not touch (the window stopped floating).</summary>
    internal static void BeginTick()
    {
        // AND SAMPLE THE PLAYER'S EYE HEIGHT WHILE WE ARE HERE. This is the one WorldUI callback
        // that runs every single tick from the very first one, long before any window spawns, which
        // is exactly what <see cref="HeadEyeHeight"/> needs: a value MEASURED off a tracked head at
        // some earlier, calmer moment, ready for the spawn that happens while the head is not
        // tracked. It costs one transform read and one compare. See HeadEyeHeight for the bug.
        HeadEyeHeight.Observe(CanvasConversion.WorldCamera);
        foreach (KeyValuePair<ConvertedPanel, Entry> kv in Entries)
            kv.Value.Seen = false;
    }

    /// <summary>Retire (and report on) every window that stopped floating this tick.</summary>
    internal static void EndTick()
    {
        RetireScratch.Clear();
        foreach (KeyValuePair<ConvertedPanel, Entry> kv in Entries)
        {
            if (!kv.Value.Seen)
                RetireScratch.Add(kv.Key);
        }
        for (int i = 0; i < RetireScratch.Count; i++)
        {
            ConvertedPanel panel = RetireScratch[i];
            if (Entries.TryGetValue(panel, out Entry entry))
                Report(entry, "the window stopped floating");
            Entries.Remove(panel);
        }
        RetireScratch.Clear();
    }

    /// <summary>
    /// A sanctioned writer is about to move <paramref name="panel"/>. The token is honoured for the
    /// frame it was written in and the one after it, because a placement written from Update lands
    /// on the frame transform before this tick's sample but a placement written from a LateUpdate
    /// (or from a MonoBehaviour whose order is undefined against the module) lands after it.
    /// Harmless when the window is not being watched (pre-reveal, or not floated at all).
    /// </summary>
    internal static void Announce(ConvertedPanel? panel, Writer writer, string why)
    {
        if (panel == null || !Entries.TryGetValue(panel, out Entry entry))
            return;
        entry.Announced = writer;
        entry.AnnouncedFrame = Time.frameCount;
        entry.AnnouncedWhy = why;
    }

    /// <summary>
    /// Is a MOD PLACEMENT the writer of this window's pose right now — i.e. did
    /// <c>ModalFallback.ComputeHmdPose</c> announce itself this frame or last?
    ///
    /// <para>THE SAME TOKEN <see cref="Classify"/> READS, ASKED BY A SECOND CONSUMER (ModBuild 226,
    /// user request 7a). <see cref="GrabbableModal"/> has to tell two kinds of external pose write
    /// apart before it decides whether to glide into the new pose or land on it: a mod PLACEMENT
    /// (the spawn, the presence-regain refloat, the one pre-reveal re-place — all three funnelled
    /// through <c>ComputeHmdPose</c>, which announces here) must be instant, while a REMOTE
    /// player's grab arriving on the wire must glide. Both arrive at the same method with the same
    /// arguments, so nothing about the POSE can separate them; who is writing it can, and it is
    /// already recorded.</para>
    ///
    /// <para>Same one-frame recency window as <see cref="Classify"/> — an announce and the write it
    /// covers are at most a frame apart by construction (the announcer returns the pose to a caller
    /// that writes it immediately) — so the two consumers can never disagree about who moved a
    /// window. The sentinel arithmetic is <see cref="NeverFrame"/>'s, for the overflow reason
    /// written there.</para>
    ///
    /// <para>A window with no entry yet (never tracked, i.e. never revealed) answers FALSE, and the
    /// caller must not read that as "a peer wrote this": its own gate requires a revealed window
    /// before it eases anything, so the pre-reveal placements land instantly for that reason
    /// instead.</para>
    /// </summary>
    internal static bool PlacementAnnounced(ConvertedPanel? panel) =>
        panel != null
        && Entries.TryGetValue(panel, out Entry entry)
        && entry.Announced == Writer.Placement
        && Time.frameCount - entry.AnnouncedFrame <= 1;

    /// <summary>
    /// THE EXPLICIT PERMISSION CHECK. Returns false — and logs the refusal ONCE per (window,
    /// caller), naming the caller — when <paramref name="panel"/> has been revealed and therefore
    /// belongs to the player. Callers that would otherwise move a revealed window ask this instead
    /// of carrying their own private guard clause, so every refusal in the mod reads the same and
    /// appears in the same log family.
    /// </summary>
    internal static bool MayMove(ConvertedPanel? panel, string caller)
    {
        if (panel == null || !Entries.TryGetValue(panel, out Entry entry) || !entry.Armed)
            return true; // not revealed yet: the spawn path is free, which is the other half of the ruling
        if (entry.RefusalLogged.Add(caller))
        {
            VRLog.Info("WorldUI", $"POSE LOCK: '{entry.Name}' — REFUSED a pose write from '{caller}'. "
                                  + "The window has been revealed, so by user ruling its pose is the "
                                  + "player's until they grab it themselves ('einmal gespawned sind sie "
                                  + "fix'). Nothing was written and nothing moved. Reported once per "
                                  + "window per caller; the POSE WATCH verdict line at the end of this "
                                  + "window's float carries the totals.");
        }
        return false;
    }

    /// <summary>
    /// The window's RECT or world SCALE is about to change (a content re-fit, the board-relative
    /// scale re-derivation). A size change must grow the window about its OWN CENTRE and leave the
    /// pose alone; this arms the next sample to check exactly that instead of assuming it, and the
    /// verdict line reports how many size changes were checked and the worst pose movement any of
    /// them produced. <paramref name="what"/> is quoted verbatim if one of them ever moves it.
    /// </summary>
    internal static void NoteSizeChange(ConvertedPanel? panel, string what)
    {
        if (panel == null || !Entries.TryGetValue(panel, out Entry entry))
            return;
        entry.PendingSizeNote = what;
        entry.PendingSizeFrame = Time.frameCount;
        entry.SizeChanges++;
    }

    /// <summary>
    /// ONE floated window, ONE tick. Arms the lock the first time the window is revealed, samples
    /// its pose, attributes and reports any movement, and puts an unsanctioned move back.
    ///
    /// <para>MUST be called AFTER the grab follow has run this tick (ModalFallback.Tick step 5), so
    /// the sample sees the pose the player will actually be shown this frame.</para>
    ///
    /// <paramref name="grab"/> supplies both the subject (its grab frame) and the restore path — a
    /// direct host write would be snapped straight back by the next follow tick, which is the
    /// RefloatOpenWindows lesson. <paramref name="poseOwnedExternally"/> excludes the map room's
    /// hover cards, whose pose is legitimately rewritten every frame by
    /// <c>ModalFallback.TickHoverCards</c>. <paramref name="peerOwned"/> marks the story window
    /// while multiplayer is mirroring a remote player's grab onto it.
    /// </summary>
    internal static void Track(ConvertedPanel? panel, GrabbableModal? grab,
        bool revealed, bool poseOwnedExternally, bool peerOwned, float worldScale)
    {
        if (panel == null || !panel.IsAlive || panel.HostGo == null)
            return;
        if (!Entries.TryGetValue(panel, out Entry entry))
        {
            // Resolved ONCE, at creation, and never again: reading GameObject.name marshals a fresh
            // string out of native Unity on every call, and this runs for every floated window every
            // frame. The host object is created by the conversion and never renamed.
            entry = new Entry { Name = panel.HostGo.name };
            Entries[panel] = entry;
        }
        entry.Seen = true;

        if (poseOwnedExternally)
        {
            entry.NotArmedReason = "its pose is owned by another mod system every frame "
                                   + "(the map room's hover card follows the pointer) — the lock "
                                   + "deliberately does not apply to it";
            return;
        }
        if (!revealed)
        {
            entry.NotArmedReason = "the window closed while still behind the reveal gate";
            return;
        }

        Transform? subject = SubjectOf(panel, grab, out bool measuredFrame);
        if (subject == null)
            return;

        int now = Time.frameCount;
        bool grabbed = grab != null && grab.IsGrabbed;
        if (grabbed)
            entry.LastGrabbedFrame = now;

        // THE SECOND SUBJECT, and it is not redundant with the first. The pose lock watches the
        // grab FRAME, which is what a placement writes; but a SIZE change never touches the frame
        // at all — it writes the host's localScale and its rect — so a frame-only instrument is
        // blind to precisely the failure requirement 4 is about (a scale applied about a corner or
        // about the panel root, which the player sees as the window jumping). The drawn centre is
        // the host rect's own centre carried into world space, so it folds in the pivot, the rect
        // size, the local scale AND the pose. With today's centred pivot the two subjects coincide
        // exactly — that coincidence IS the guarantee, and this is what would notice it breaking.
        bool haveCentre = TryDrawnCentre(panel, out Vector3 centre);

        if (!entry.Armed)
        {
            entry.Armed = true;
            entry.ArmedAt = Time.unscaledTime;
            entry.ArmedFrame = now;
            entry.ArmScale = Mathf.Max(worldScale, 1e-4f);
            entry.MeasuredFrame = measuredFrame;
            entry.LockedPos = subject.position;
            entry.LockedRot = subject.rotation;
            entry.PrevCentre = centre;
            entry.HaveCentre = haveCentre;
            entry.Samples = 1;
            return;
        }
        entry.Samples++;

        float movedMm = (subject.position - entry.LockedPos).magnitude / entry.ArmScale * 1000f;
        float turnedDeg = Quaternion.Angle(subject.rotation, entry.LockedRot);
        bool moved = movedMm > MoveEpsilonMm || turnedDeg > TurnEpsilonDeg;

        // ---- requirement 4: a size change must not move the window --------------------------
        // Checked against the PREVIOUS frame's drawn centre (not against the lock): the question is
        // whether THIS resize displaced the window, not how far the window is from where it was
        // revealed. Suppressed while the player is holding it, where the centre moves for the one
        // legitimate reason there is.
        bool afterSizeChange = now - entry.PendingSizeFrame <= 1;
        if (afterSizeChange && haveCentre && entry.HaveCentre && !grabbed)
        {
            float centreMm = (centre - entry.PrevCentre).magnitude / entry.ArmScale * 1000f;
            entry.SizeChangeWorstMm = Mathf.Max(entry.SizeChangeWorstMm, centreMm);
            if (centreMm > MoveEpsilonMm && !entry.SizeMoveLogged)
            {
                entry.SizeMoveLogged = true;
                VRLog.Warn("WorldUI", $"POSE LOCK: '{entry.Name}' — a SIZE change MOVED the window's "
                                      + $"drawn centre by {centreMm:F1} mm ({entry.PendingSizeNote}). "
                                      + "A rect or world-scale change must grow a window about its OWN "
                                      + "CENTRE and leave its pose untouched; this one did not, and to "
                                      + "the player that reads exactly like the window jumping when a "
                                      + "sub-view opens. The grab frame itself moved "
                                      + $"{movedMm:F1} mm, so the displacement is "
                                      + (movedMm > MoveEpsilonMm
                                          ? "a genuine POSE write and the lock will deal with it below."
                                          : "NOT a pose write — the scale is being applied about "
                                            + "something other than the window's centre (a corner, or "
                                            + "the panel root). Reported once per window."));
            }
            entry.PendingSizeNote = null;
        }
        else if (!afterSizeChange)
        {
            entry.PendingSizeNote = null;
        }

        entry.PrevCentre = centre;
        entry.HaveCentre = haveCentre;

        if (!moved)
            return;

        Writer writer = Classify(entry, now, grabbed, peerOwned);
        string caller = CallerName(entry, writer);
        RecordWrite(entry, caller, movedMm, turnedDeg);

        switch (writer)
        {
            case Writer.UserGrab:
                // The one allowed writer: re-baseline in silence. This is the pose the player chose.
                entry.LockedPos = subject.position;
                entry.LockedRot = subject.rotation;
                return;

            case Writer.Placement:
            case Writer.Peer:
                if (writer == Writer.Placement ? !entry.PlacementNoted : !entry.PeerNoted)
                {
                    if (writer == Writer.Placement)
                        entry.PlacementNoted = true;
                    else
                        entry.PeerNoted = true;
                    VRLog.Info("WorldUI", $"POSE LOCK: '{entry.Name}' was moved {movedMm:F1} mm / "
                                          + $"{turnedDeg:F1}° AFTER it was revealed by '{caller}' "
                                          + $"({entry.AnnouncedWhy}). This writer is sanctioned, so the "
                                          + "move stands and the lock re-baselines on it — but it is a "
                                          + "post-reveal move and the standing ruling only tolerates it "
                                          + "because a human act (this player's headset coming back on, "
                                          + "or a remote player's own grab) caused it. Reported once per "
                                          + "window per writer.");
                }
                entry.LockedPos = subject.position;
                entry.LockedRot = subject.rotation;
                return;

            default:
                if (!Refuse(entry, grab, movedMm, turnedDeg))
                {
                    // The lock could NOT put it back (no grab frame, or it has conceded). Re-baseline
                    // anyway, or every following frame would re-measure the same unfixed offset and
                    // the WRITES count would become a frame count instead of a write count — the
                    // number the verdict line exists to report would then mean nothing.
                    entry.LockedPos = subject.position;
                    entry.LockedRot = subject.rotation;
                }
                return;
        }
    }

    /// <summary>The transform whose pose IS the window's pose — see the class doc for why the grab
    /// frame outranks the host.</summary>
    private static Transform? SubjectOf(ConvertedPanel panel, GrabbableModal? grab,
        out bool measuredFrame)
    {
        Transform? frame = grab != null ? ((IPanelGrabOwner)grab).GrabRoot : null;
        if (frame != null)
        {
            measuredFrame = true;
            return frame;
        }
        measuredFrame = false;
        return panel.HostGo != null ? panel.HostGo.transform : null;
    }

    /// <summary>
    /// Where the window is DRAWN, as one world point: the host <see cref="RectTransform"/>'s own
    /// rect centre carried through its full local-to-world matrix. Unlike the transform position it
    /// changes when the pivot, the rect size or the local scale changes, which is exactly what the
    /// size-change assertion has to be able to see.
    /// </summary>
    private static bool TryDrawnCentre(ConvertedPanel panel, out Vector3 centre)
    {
        RectTransform rect = panel.HostRect;
        if (rect == null)
        {
            centre = default;
            return false;
        }
        centre = rect.TransformPoint(rect.rect.center);
        return true;
    }

    private static Writer Classify(Entry entry, int now, bool grabbed, bool peerOwned)
    {
        if (grabbed || now - entry.LastGrabbedFrame <= GrabSettleFrames)
            return Writer.UserGrab;
        if (now - entry.AnnouncedFrame <= 1)
            return entry.Announced;
        return peerOwned ? Writer.Peer : Writer.Unattributed;
    }

    private static string CallerName(Entry entry, Writer writer) => writer switch
    {
        Writer.UserGrab => "the player's own grab",
        Writer.Placement => "ModalFallback.ComputeHmdPose (spawn / presence-regain refloat / re-place)",
        Writer.Peer => "Net.RemoteStorySync / Net.RemoteMapStory (a remote player's grab)",
        _ => "unattributed — no mod placement path announced this write",
    };

    private static void RecordWrite(Entry entry, string caller, float movedMm, float turnedDeg)
    {
        entry.Writes++;
        if (movedMm > entry.LargestMm)
        {
            entry.LargestMm = movedMm;
            entry.LargestDeg = turnedDeg;
            entry.LargestBy = caller;
        }
        entry.ByCaller.TryGetValue(caller, out CallerTally tally);
        tally.Count++;
        if (movedMm > tally.LargestMm)
            tally.LargestMm = movedMm;
        entry.ByCaller[caller] = tally;
    }

    /// <summary>Put the window back where the player last left it, and name whoever moved it. The
    /// correction is bounded: past <see cref="MaxCorrections"/> the lock concedes rather than
    /// alternate the value every frame against a per-frame writer. Returns whether it actually
    /// restored the pose (false = the caller must re-baseline instead).</summary>
    private static bool Refuse(Entry entry, GrabbableModal? grab, float movedMm, float turnedDeg)
    {
        string caller = CallerName(entry, Writer.Unattributed);
        bool restorable = grab != null && !entry.Conceded && entry.Corrections < MaxCorrections;

        if (restorable)
        {
            entry.Corrections++;
            // SnapFrameTo, not PlaceFrameAt: a restore is a CORRECTION and has to be instant.
            // PlaceFrameAt may glide a SHARED window into its new pose (the remote-pose easing,
            // ModBuild 226), and a glide is the one thing a lock must never produce — the window
            // would be visibly somewhere the ruling has already refused, and the very next frame's
            // sample would read the in-flight glide as a further unattributed write and burn another
            // correction against MaxCorrections.
            grab!.SnapFrameTo(entry.LockedPos, entry.LockedRot);
        }
        else if (grab != null && !entry.Conceded)
        {
            entry.Conceded = true;
            VRLog.Warn("WorldUI", $"POSE LOCK: '{entry.Name}' — CONCEDED after {MaxCorrections} "
                                  + "corrections. Something is writing this window's pose repeatedly "
                                  + "and putting it back every time would make the value alternate "
                                  + "every frame, which in MultiPass lets the two eyes disagree. The "
                                  + "lock stands down for this window; it keeps COUNTING, so the POSE "
                                  + "WATCH verdict line still names the writer and its magnitude.");
        }

        if (entry.RefusalLogged.Add(caller))
        {
            VRLog.Warn("WorldUI", $"POSE LOCK: '{entry.Name}' was moved {movedMm:F1} mm / "
                                  + $"{turnedDeg:F1}° after it was revealed, by {caller}. The user "
                                  + "ruling is that a spawned window never moves again without the "
                                  + "player's own grab, so this write is REFUSED"
                                  + (restorable
                                      ? " and the window has been put back at the pose it was revealed "
                                        + "at (via its grab frame — a direct host write would be undone "
                                        + "by the next follow tick anyway)."
                                      : grab == null
                                          ? ", but this window has no grab frame to restore through, so "
                                            + "the move STANDS and this line is a report, not a fix."
                                          : ", and the lock has already conceded on this window.")
                                  + " NOTHING in the mod announced this write: the writers that do are "
                                  + "the player's grab, ModalFallback.ComputeHmdPose and the two "
                                  + "shared-window pose appliers (Net.RemoteStorySync record 19, "
                                  + "Net.RemoteMapStory record 21). Reported once per window per caller.");
        }
        return restorable;
    }

    /// <summary>The ONE verdict line for a window's whole float — see the class doc for why the
    /// three forms must be distinguishable.</summary>
    private static void Report(Entry entry, string why)
    {
        if (!entry.Armed)
        {
            VRLog.Info("WorldUI", $"POSE WATCH '{entry.Name}': NEVER ARMED — {entry.NotArmedReason} "
                                  + $"({why}). Nothing was watched, so this line is NOT evidence that "
                                  + "the window's pose held still; it says only that the lock had no "
                                  + "revealed window to hold.");
            return;
        }

        float seconds = Time.unscaledTime - entry.ArmedAt;
        string subject = entry.MeasuredFrame
            ? "its mod-owned grab frame (the authority the host follows)"
            : "its host transform (this window has no grab frame)";
        string sizeNote = entry.SizeChanges == 0
            ? " No rect/world-scale change was reported while it was revealed."
            : $" SIZE CHANGES: {entry.SizeChanges} checked while it was revealed; the worst DRAWN-CENTRE "
              + $"displacement any of them produced was {entry.SizeChangeWorstMm:F1} mm"
              + (entry.SizeChangeWorstMm <= MoveEpsilonMm
                  ? $" — i.e. none above the {MoveEpsilonMm:F1} mm noise floor: the window grew about "
                    + "its own centre and stayed where it was, which is the rule."
                  : " — ABOVE the noise floor, i.e. a size change DID displace this window; see the "
                    + "POSE LOCK warning for which one and by how much.");

        if (entry.Writes == 0)
        {
            VRLog.Info("WorldUI", $"POSE WATCH '{entry.Name}': NO WRITES in {entry.Samples} sampled "
                                  + $"frame(s) over {seconds:F1} s revealed — the pose it was revealed "
                                  + $"at is the pose it {why} with. Measured on {subject}; anything "
                                  + $"above {MoveEpsilonMm:F1} mm or {TurnEpsilonDeg:F2}° would have "
                                  + "counted." + sizeNote);
            return;
        }

        var sb = new System.Text.StringBuilder(256);
        foreach (KeyValuePair<string, CallerTally> kv in entry.ByCaller)
        {
            if (sb.Length > 0)
                sb.Append("; ");
            sb.Append('\'').Append(kv.Key).Append("' ×").Append(kv.Value.Count)
              .Append(" (largest ").Append(kv.Value.LargestMm.ToString("F1")).Append(" mm)");
        }
        VRLog.Info("WorldUI", $"POSE WATCH '{entry.Name}': WRITES FOUND — COUNT {entry.Writes} in "
                              + $"{entry.Samples} sampled frame(s) over {seconds:F1} s revealed; "
                              + $"LARGEST {entry.LargestMm:F1} mm / {entry.LargestDeg:F1}° by "
                              + $"'{entry.LargestBy}'. Per caller: {sb}. "
                              + $"{entry.Corrections} of them were put back by the lock"
                              + (entry.Conceded ? " before it conceded" : string.Empty)
                              + $". Measured on {subject}." + sizeNote);
    }
}

/// <summary>
/// THE PLAYER'S EYE HEIGHT ABOVE THEIR OWN TRACKING FLOOR — REMEMBERED, BECAUSE THE HEAD CAMERA
/// DOES NOT ALWAYS HAVE ONE.
///
/// <para>THE BUG THIS EXISTS FOR (ModBuild 198 hardware log, the user's photograph
/// <c>Tischbeine.jpg</c>: "Alle Fenster spawnen jetzt UNTER dem Tisch"). The map room's permanent
/// windows — character screen, ESC menu, quest log, options — are converted during the map scene's
/// LOAD, in the gap between <c>BuildMapRig</c> creating the head camera and the first tracked XR
/// pose reaching it. <c>CreateHeadCamera</c> parents the camera to the rig root at
/// <c>localPosition = Vector3.zero</c>, and the map rig's root IS the tracking floor, so for those
/// ~35 frames the head camera reports the player's eyes ON THE FLOOR. The log proves it three ways
/// in the same session: the rig line says "tracking floor at (-184.45, -154.55, 0.18)", the
/// spawn-clamp lines say the head was at exactly y = -154.55 with a perfectly horizontal forward
/// (which no real HMD ever has), and the peer-head diagnostic printed "local head: (-184.45,
/// -154.55, 0.18)". 0.7 s later "Map rig recentered" reported the same head at y = 216.87, i.e.
/// 1.87 m above that floor — the windows had been placed 1.77 m too low and, by the ModBuild 193
/// ruling that a spawned window never moves again, they stayed there.</para>
///
/// <para>WHY A REMEMBERED VALUE AND NOT A DEFERRED SPAWN. Deferring is the obvious fix and it is
/// not available from here: <c>ModalFallback</c>'s convert loop puts a window that fails to place
/// into its <c>Failed</c> set, which "retries only after a close/re-open" — so refusing to place
/// would strand the map room's permanent windows for the whole visit. Re-placing later is not
/// available either: the pre-reveal re-place is the only sanctioned second write and in this very
/// log it arrived AFTER the reveal deadline and was correctly refused by
/// <see cref="PanelPoseWatch"/>. The window therefore gets exactly ONE placement and it has to be
/// right the first time — which means the vertical reference must be right the first time.</para>
///
/// <para>WHY <c>localPosition.y</c> IS THE RIGHT QUANTITY IN EVERY RIG. The head camera is always
/// parented to the rig root and its local pose is written by the TrackedPoseDriver, i.e. it is the
/// raw XR pose. That makes <c>localPosition.y</c> the player's real eye height above their real
/// tracking floor whatever the rig root is doing — scaled diorama, map seat or 1:1 menu — which is
/// the same number <c>VRRigDriver.RecenterMap</c> prints as "N m real above the tracking floor".
/// Nothing here is scaled: metres in, metres out, and the caller applies the rig scale.</para>
/// </summary>
internal static class HeadEyeHeight
{
    /// <summary>
    /// Below this height above the tracking floor (real metres) a "head" pose is not a head. A
    /// seated adult's eyes are ~1.10–1.30 m up and even sitting on the floor puts them near 0.80 m,
    /// so 0.40 m is far under anything a player can produce — while both readings the ModBuild 198
    /// log actually contains (0.00 m for the map-load spawns, −0.245 m for the quest-preview popup)
    /// are unambiguously under it. It is a "this is not a measurement" test, not a comfort dial:
    /// widening it towards real eye heights would start correcting poses that are simply low.
    /// </summary>
    internal const float MinPlausibleMeters = 0.40f;

    /// <summary>
    /// Stand-in used ONLY when the headset has not reported a single usable pose yet in this
    /// session, i.e. when there is nothing to remember. It is an assumption and it is labelled as
    /// one on every log line that uses it. In practice it is close to unreachable: the player passes
    /// through the menu rig — where <see cref="Observe"/> runs every tick with tracking live — for
    /// seconds before any room can be entered, so the remembered measurement is normally already in
    /// hand by the time the first window spawns.
    /// </summary>
    internal const float AssumedMeters = 1.60f;

    private static float _lastMeasured = float.NaN;
    private static int _comparisons;
    private static int _tracked;
    private static int _untracked;

    /// <summary>How many head-height tests have been made this session (the instrument's own
    /// sample count, printed beside every value it produces).</summary>
    internal static int Comparisons => _comparisons;

    /// <summary>Of <see cref="Comparisons"/>, how many found a usable tracked eye height.</summary>
    internal static int TrackedCount => _tracked;

    /// <summary>Of <see cref="Comparisons"/>, how many found none.</summary>
    internal static int UntrackedCount => _untracked;

    /// <summary>True once a real tracked eye height has been measured at least once.</summary>
    internal static bool HaveMeasured => !float.IsNaN(_lastMeasured);

    /// <summary>
    /// One head-height test. Returns true when <paramref name="head"/> carries a usable tracked
    /// pose, in which case <paramref name="eyeMeters"/> is the live height and it is remembered.
    /// A camera with no rig parent (the <c>Camera.main</c> fallback outside the mod's own rig) has
    /// no tracking floor to measure against, so it is reported as tracked and left entirely alone —
    /// this class corrects the mod's rig, never somebody else's camera.
    /// </summary>
    internal static bool Observe(Camera? head, out float eyeMeters)
    {
        eyeMeters = float.NaN;
        Transform? t = head != null ? head!.transform : null;
        Transform? rig = t != null ? t.parent : null;
        if (t == null || rig == null)
            return true;

        _comparisons++;
        float live = t.localPosition.y;
        if (live < MinPlausibleMeters)
        {
            _untracked++;
            return false;
        }
        _tracked++;
        _lastMeasured = live;
        eyeMeters = live;
        return true;
    }

    /// <summary>Per-tick sampler (see <see cref="PanelPoseWatch.BeginTick"/>).</summary>
    internal static void Observe(Camera? head) => Observe(head, out _);

    /// <summary>The eye height to place from, real metres above the tracking floor, plus where it
    /// came from (log material — the caller must print both).</summary>
    internal static float Resolve(out string source)
    {
        if (HaveMeasured)
        {
            source = $"the last tracked head pose this session ({_lastMeasured:F2} m)";
            return _lastMeasured;
        }
        source = $"NO tracked head pose has been seen yet this session — assumed {AssumedMeters:F2} m";
        return AssumedMeters;
    }

    /// <summary>
    /// Correct <paramref name="headPos"/>'s HEIGHT when the camera it came from has no tracked
    /// pose. Only the vertical term is touched: the horizontal position and the forward are the
    /// camera's own and stay the camera's own, so a spawn placed through this still faces and
    /// follows whatever direction the rig is aimed in. Returns true when a substitution was made,
    /// with <paramref name="note"/> carrying the full falsifiable statement for the log.
    /// </summary>
    internal static bool CorrectVerticalReference(Camera? head, ref Vector3 headPos, out string note)
    {
        note = string.Empty;
        if (Observe(head, out _))
            return false;

        Transform t = head!.transform;
        Transform rig = t.parent!;
        float live = t.localPosition.y;
        float eyeMeters = Resolve(out string source);

        Vector3 local = t.localPosition;
        local.y = eyeMeters;
        Vector3 corrected = rig.TransformPoint(local);
        note = $"HEAD HEIGHT SUBSTITUTED — the head camera reported its eyes {live:F2} m above the "
               + $"tracking floor, below the {MinPlausibleMeters:F2} m any real eye can reach, so it "
               + "carries no tracked XR pose yet (the map rig parents the camera to the rig root at "
               + "localPosition zero, and that root IS the tracking floor). Placing from "
               + $"{eyeMeters:F2} m instead — {source}; world y {headPos.y:F2} → {corrected.y:F2}. "
               + $"{_comparisons} head-height comparison(s) this session: {_tracked} tracked, "
               + $"{_untracked} not";
        headPos = corrected;
        return true;
    }

    /// <summary>The player's tracking floor in world units — the rig root, which every rig flavour
    /// places ON that floor. Used to express placement heights in real metres as well as world
    /// units, so a clamp line can be checked against the room instead of only against itself.</summary>
    internal static bool TryTrackingFloorY(out float y)
    {
        Transform? rigRoot = Rig.VRRigDriver.RigRoot;
        if (rigRoot == null)
        {
            y = 0f;
            return false;
        }
        y = rigRoot.position.y;
        return true;
    }
}
