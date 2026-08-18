using System.IO;
using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.WireTests;

/// <summary>
/// The FIXIERT control board under a world zoom — four user reports that must all be true at once,
/// driven at the rig scales his own hardware logs recorded (×19.01 … ×137.05).
///
/// <para><b>WHY THIS IS PINNED HERE.</b> Whether a pinned board "zooms with the world" is observable
/// ONLY by eye, from inside a headset, one pinch at a time — and the number the mod printed about it
/// through four shipped builds AGREED WITH ALL FOUR, because it reported apparent size (world size ÷
/// rig scale) and what the eye judges is angular size (size ÷ distance). So the property was
/// unfalsifiable from the log, unfalsifiable from the compiled form, and the reports kept coming:</para>
/// <list type="number">
/// <item>ModBuild 158 — "Ich hatte in einer Hand das Controllboard und habe dann gezoomed — dann hat
/// das controllboard mitgezoomed... Weiterhin hat sich damit auch das Maximum und Minimum wieder
/// verschoben."</item>
/// <item>ModBuild 159 — "Das Board zoomed immer noch im Fixiert modus mit."</item>
/// <item>ModBuild 160 — "Fixiert funktioniert nun garnicht mehr — egal was man dort einstellt, das
/// board zoomed nun immer mit und geht immer mit."</item>
/// <item>Standing ruling 2026-08-07 — "Fixiert heißt: völlig unabhängig vom Character, bewegt sich in
/// KEINSTER Weise, außer es wird aktiv verschoben oder skaliert."</item>
/// </list>
/// <para>Report 3 is the one that kills the obvious answer: a rig-parented board is zoom-invariant and
/// travels with the player. A world-static board stays put and rides the zoom. NEITHER PURE ANCHOR IS
/// CORRECT, and the rule that satisfies all four is a gate — carry the rig-local pose on frames where
/// the rig SCALE changed, and only on those. The four properties below are exactly the four reports,
/// restated as arithmetic, so the next round is decidable without a headset.</para>
///
/// <para>Plus a SOURCE LINT, because the fifth failure mode is not arithmetic at all: ModBuild 159 ran
/// this same maths one frame early (in the Update phase, before WorldGrab wrote the new rig scale) and
/// produced a ±10 % breathing through every pinch. A right answer computed at the wrong instant is
/// still wrong, and no vector can express an instant — so the phase is pinned by reading the source.</para>
/// </summary>
internal static class BoardZoomCarryVectors
{
    /// <summary>The rig scales the user's own hardware logs swept through, in one session.</summary>
    private const float RigMin = 19.01f;
    private const float RigMid = 68.50f;
    private const float RigMax = 137.05f;

    private static bool Near(float a, float b, float tol = 1e-3f) => Mathf.Abs(a - b) <= tol;

    private static bool Near(Vector3 a, Vector3 b, float tol = 1e-3f) =>
        Near(a.x, b.x, tol) && Near(a.y, b.y, tol) && Near(a.z, b.z, tol);

    /// <summary>
    /// A yaw about world up, built by hand. <c>Quaternion.Euler</c> and <c>Quaternion.AngleAxis</c> are
    /// engine ECalls and throw outside Unity ("ECall methods must be packaged into a system module"),
    /// the same wall MapRoomSeatVectors hit with <c>LookRotation</c>. The quaternion product and the
    /// quaternion-times-vector rotation ARE managed, so everything the carry actually does can be
    /// driven here — only the constructors have to be spelled out.
    /// </summary>
    private static Quaternion Yaw(float degrees)
    {
        float half = degrees * 0.5f * Mathf.Deg2Rad;
        return new Quaternion(0f, Mathf.Sin(half), 0f, Mathf.Cos(half));
    }

    /// <summary>
    /// A miniature of the real thing: the rig (position, rotation, uniform scale), the pin holder
    /// (world origin, identity rotation, uniform scale) and the tray root under it. Every step below
    /// drives the SHIPPED helper, not a copy of it.
    /// </summary>
    private struct Board
    {
        public Vector3 RigPos;
        public Quaternion RigRot;
        public float RigScale;
        public float Holder;      // pin-holder uniform scale
        public float LocalScale;  // the size the player dialled in; NEVER written by the carry
        public Vector3 WorldPos;  // the tray root's world position
        public Quaternion WorldRot;

        // Carry state, mirroring PlayTray's fields exactly.
        public Vector3 CachedRigLocalPos;
        public Quaternion CachedRigLocalRot;
        public float CarryRigScale;

        /// <summary>Half the board's width in tray-local units (PlayTray.BoardHalfWidthLocal).</summary>
        public const float HalfWidthLocal = 0.36f;

        /// <summary>What the player SEES: world size ÷ their own scale.</summary>
        public readonly float PerceivedWidth =>
            BoardZoomCarry.PerceivedMeters(HalfWidthLocal * 2f * Holder * LocalScale, RigScale);

        /// <summary>Where the player sees it: the rig-local position's magnitude, in player metres.
        /// (The head sits at the rig origin here; a head offset is a constant that cancels out of
        /// every delta below, so leaving it out makes the property sharper, not weaker.)</summary>
        public readonly float PerceivedDistance =>
            BoardZoomCarry.ToRigLocal(WorldPos, RigPos, RigRot, RigScale).magnitude;

        public readonly float Subtended => BoardZoomCarry.SubtendedDegrees(PerceivedWidth, PerceivedDistance);

        /// <summary>The perceived POSE, not just the distance — the quantity the rule holds constant.</summary>
        public readonly Vector3 Perceived => BoardZoomCarry.ToRigLocal(WorldPos, RigPos, RigRot, RigScale);

        /// <summary>The measure both the release-time clamp and the live two-hand gesture window ride
        /// on (PlayTray.TryGetApparentWidthPerScaleUnit) — the subject of report 1's second sentence.</summary>
        public readonly float PerUnit =>
            BoardZoomCarry.ApparentWidthPerScaleUnit(HalfWidthLocal, Holder, RigScale);

        /// <summary>End-of-frame cache, exactly as TickPinnedZoomCarry does it.</summary>
        public void Cache()
        {
            CachedRigLocalPos = BoardZoomCarry.ToRigLocal(WorldPos, RigPos, RigRot, RigScale);
            CachedRigLocalRot = BoardZoomCarry.Conjugate(RigRot) * WorldRot;
        }

        /// <summary>
        /// One frame of TickPinnedZoomCarry: gate on the rig scale, carry if it moved, re-cache always.
        /// <paramref name="held"/> reproduces the gripped case, where the hand owns the world position
        /// and the carry contributes only the size.
        /// </summary>
        public bool Tick(bool held = false)
        {
            bool carried = false;
            if (BoardZoomCarry.ScaleChanged(CarryRigScale, RigScale, out _))
            {
                Vector3 wantPos = held
                    ? WorldPos
                    : BoardZoomCarry.ToWorld(CachedRigLocalPos, RigPos, RigRot, RigScale);
                Quaternion wantRot = held ? WorldRot : RigRot * CachedRigLocalRot;
                Holder = BoardZoomCarry.CarryHolderScale(Holder, CarryRigScale, RigScale);
                WorldPos = wantPos;
                WorldRot = wantRot;
                CarryRigScale = RigScale;
                carried = true;
            }
            Cache();
            return carried;
        }

        public static Board Fresh(float rigScale)
        {
            var b = new Board
            {
                RigPos = new Vector3(24.50f, 7.83f, 16.39f),  // the seat his 146-line log held
                RigRot = Yaw(37f),
                RigScale = rigScale,
                Holder = rigScale,      // ApplyFollowMode bakes the rig scale at pin time
                LocalScale = 0.62f,
                WorldRot = Yaw(12f),
                CachedRigLocalRot = Quaternion.identity,
                CarryRigScale = rigScale,
            };
            // Seat the board half a player-metre in front of the rig, expressed in world units.
            b.WorldPos = BoardZoomCarry.ToWorld(new Vector3(0f, -0.15f, 0.55f), b.RigPos, b.RigRot, rigScale);
            b.Cache();
            return b;
        }
    }

    /// <summary>The pinch, as WorldGrab actually solves it: the GLUED HAND MIDPOINT stays fixed in the
    /// world, so changing the scale MOVES THE RIG. `rig.position = _midAnchorWorld - rot * (mid * s)`
    /// (Rig/WorldGrab.cs). Getting this right is what makes the test meaningful — a zoom that left the
    /// rig where it was would make a world-static board look correct.</summary>
    private static void Pinch(ref Board b, Vector3 midAnchorWorld, Vector3 midLocal, float newScale)
    {
        b.RigScale = newScale;
        b.RigPos = midAnchorWorld - b.RigRot * (midLocal * newScale);
    }

    internal static void Run(Harness t, string repoRoot)
    {
        PureZoomLeavesTheEyeUnchanged(t);
        PureTranslationLeavesTheWorldPoseUnchanged(t);
        HeldBoardKeepsItsPerceivedSize(t);
        GestureLimitsStopMovingWithTheZoom(t);
        TheGateItself(t);
        PhaseLint(t, repoRoot);
    }

    // ---- (a) a pure zoom leaves perceived size AND perceived distance unchanged ------------------
    //
    // Report 1's first sentence, report 2 in full, and the "zoomed mit" half of report 3. Swept across
    // the user's whole recorded range in both directions, one frame at a time, because the shipped
    // defect was per-frame and a single big step would hide a per-frame lag.
    private static void PureZoomLeavesTheEyeUnchanged(Harness t)
    {
        t.Case("boardzoom/pure-zoom-invisible");
        var b = Board.Fresh(RigMin);
        float width0 = b.PerceivedWidth, dist0 = b.PerceivedDistance, ang0 = b.Subtended;
        Vector3 perceived0 = b.Perceived;

        // The hands are glued to a world point ~1 player-metre ahead; that point does not move.
        Vector3 midLocal = new(0f, 0f, 1.0f);
        Vector3 midAnchor = BoardZoomCarry.ToWorld(midLocal, b.RigPos, b.RigRot, b.RigScale);

        const int Steps = 60;
        for (int i = 1; i <= Steps; i++)
        {
            float s = Mathf.Lerp(RigMin, RigMax, i / (float)Steps);
            Pinch(ref b, midAnchor, midLocal, s);
            t.True(b.Tick(), $"a scale step of a real pinch always fires the gate (step {i})");
        }
        t.True(Near(b.RigScale, RigMax), "the sweep reached the top of his recorded range");
        t.True(Near(b.PerceivedWidth, width0, 1e-4f), "perceived WIDTH is unchanged by a ×7.2 zoom");
        t.True(Near(b.PerceivedDistance, dist0, 1e-4f), "perceived DISTANCE is unchanged by the same zoom");
        t.True(Near(b.Subtended, ang0, 1e-3f), "the board therefore subtends the SAME ANGLE — the measure the eye judges");
        t.True(Near(b.Perceived, perceived0, 1e-4f), "and the whole perceived POSE is unchanged, not just its magnitude");

        // The player's dialled size is never touched by the carry — the holder absorbs the zoom.
        t.True(Near(b.LocalScale, 0.62f), "the two-hand/BoardScale size the player set is never written by the carry");
        t.True(Near(b.Holder / b.RigScale, 1f, 1e-4f), "the holder tracked the rig exactly: holder/rig is the invariant");

        // ...and back down again. The carry must be reversible, or a zoom out would leave a residue.
        for (int i = 1; i <= Steps; i++)
        {
            float s = Mathf.Lerp(RigMax, RigMin, i / (float)Steps);
            Pinch(ref b, midAnchor, midLocal, s);
            b.Tick();
        }
        t.True(Near(b.PerceivedWidth, width0, 1e-4f), "zooming back out leaves no residue in the perceived width");
        t.True(Near(b.PerceivedDistance, dist0, 1e-4f), "nor in the perceived distance");

        // THE CONTRAST THAT CONVICTS THE OLD BUILDS. Same sweep, no carry at all (ModBuild 158's
        // world-static pin): the board's world pose is frozen and the eye sees it swell and approach.
        var frozen = Board.Fresh(RigMin);
        float fw0 = frozen.PerceivedWidth, fd0 = frozen.PerceivedDistance;
        Pinch(ref frozen, midAnchor, midLocal, RigMax);   // no Tick() — nothing carries it
        t.True(!Near(frozen.PerceivedWidth, fw0, 0.05f),
               "a world-static board's perceived width DOES change under the same zoom — this is report 2");
        t.True(!Near(frozen.PerceivedDistance, fd0, 0.05f),
               "and so does its perceived distance — which is why the old apparent-size line agreed with it");
    }

    // ---- (b) a pure translation leaves the WORLD pose unchanged ----------------------------------
    //
    // Ruling 4 and the "geht immer mit" half of report 3. Walking, stick locomotion, teleport, flight
    // and snap turn all move the rig WITHOUT changing its scale, so the gate must stay shut and the
    // board must stay in the room.
    private static void PureTranslationLeavesTheWorldPoseUnchanged(Harness t)
    {
        t.Case("boardzoom/pure-translation-inert");
        var b = Board.Fresh(RigMid);
        Vector3 world0 = b.WorldPos;
        Quaternion rot0 = b.WorldRot;
        float holder0 = b.Holder;
        float dist0 = b.PerceivedDistance;

        // Walk 40 world units, turn 180° in snap steps, and fly up — every one of them at a constant
        // rig scale.
        for (int i = 0; i < 40; i++)
        {
            b.RigPos += new Vector3(1f, 0f, 0.5f);
            t.True(!b.Tick(), $"walking does not fire the zoom gate (step {i})");
        }
        for (int i = 0; i < 8; i++)
        {
            b.RigRot *= Yaw(22.5f);
            t.True(!b.Tick(), $"a snap turn does not fire the zoom gate (step {i})");
        }
        b.RigPos += new Vector3(0f, 12f, 0f);
        t.True(!b.Tick(), "stick flight does not fire the zoom gate");

        t.True(Near(b.WorldPos, world0, 1e-5f), "the board's WORLD position is bit-for-bit where the player left it");
        t.True(Quaternion.Angle(b.WorldRot, rot0) < 1e-4f, "and so is its world rotation");
        t.True(Near(b.Holder, holder0, 1e-5f), "the pin holder was never written");
        t.True(!Near(b.PerceivedDistance, dist0, 0.01f),
               "the player HAS moved relative to it — that is the point of pinning, and it is ruling 4");

        // A world-grab DRAG (translate + rotate the rig, scale untouched) is locomotion too.
        t.Case("boardzoom/world-grab-drag-inert");
        var d = Board.Fresh(RigMax);
        Vector3 dw0 = d.WorldPos;
        for (int i = 0; i < 30; i++)
        {
            d.RigPos += new Vector3(0.7f, -0.2f, 0.3f);
            d.RigRot *= Yaw(3f);
            t.True(!d.Tick(), $"a world-grab drag at constant scale writes nothing (step {i})");
        }
        t.True(Near(d.WorldPos, dw0, 1e-5f), "dragging the world past a pinned board leaves the board in the room");
    }

    // ---- (c) a held board keeps its perceived size across a zoom ---------------------------------
    //
    // Report 1 exactly: "Ich hatte in einer Hand das Controllboard und habe dann gezoomed — dann hat
    // das controllboard mitgezoomed." PanelGrabHandle writes root.position in WORLD space and never
    // reparents, so while held the HAND owns the position and the carry contributes only the size.
    private static void HeldBoardKeepsItsPerceivedSize(Harness t)
    {
        t.Case("boardzoom/held-keeps-perceived-size");
        var b = Board.Fresh(RigMin);
        float width0 = b.PerceivedWidth;
        Vector3 world0 = b.WorldPos;

        Vector3 midLocal = new(0f, 0f, 1.0f);
        Vector3 midAnchor = BoardZoomCarry.ToWorld(midLocal, b.RigPos, b.RigRot, b.RigScale);
        for (int i = 1; i <= 40; i++)
        {
            float s = Mathf.Lerp(RigMin, RigMid, i / 40f);
            Pinch(ref b, midAnchor, midLocal, s);
            b.Tick(held: true);
        }
        t.True(Near(b.PerceivedWidth, width0, 1e-4f),
               "a HELD board's perceived size is constant through the zoom — report 1's first sentence");
        t.True(Near(b.WorldPos, world0, 1e-5f),
               "and its world position was left to the hand, which is ruling 4's 'außer es wird aktiv verschoben'");
        t.True(Near(b.Holder / b.RigScale, 1f, 1e-4f), "the holder tracked the rig while held, same as when free");

        // Without the carry the same held board grows in the eye by the full zoom factor — the defect.
        var frozen = Board.Fresh(RigMin);
        float f0 = frozen.PerceivedWidth;
        Pinch(ref frozen, midAnchor, midLocal, RigMid);
        t.True(Near(frozen.PerceivedWidth, f0 * RigMin / RigMid, 1e-4f),
               "un-carried, the held board's perceived size scales by exactly 1/s — that IS 'mitgezoomed'");
    }

    // ---- (d) the gesture limits stop moving with the zoom ----------------------------------------
    //
    // Report 1's second sentence: "Weiterhin hat sich damit auch das Maximum und Minimum wieder
    // verschoben." Both the release-time clamp and the live two-hand window are bounded by
    // perUnit = BoardHalfWidthLocal*2 * parent / rigScale, with `parent` the pin holder. While the
    // holder was frozen and the rig swept, that ratio swept too, and his BoardScale_Steel ratcheted
    // 0.54 -> 1.00 -> 1.13 in a single session.
    private static void GestureLimitsStopMovingWithTheZoom(Harness t)
    {
        t.Case("boardzoom/limits-are-zoom-invariant");
        const float MinW = 0.18f, MaxW = 1.40f;   // [Cards] BoardMinWidthMeters / BoardMaxWidthMeters

        var b = Board.Fresh(RigMin);
        float perUnit0 = b.PerUnit;
        Vector2 limits0 = new(MinW / perUnit0, MaxW / perUnit0);

        Vector3 midLocal = new(0f, 0f, 1.0f);
        Vector3 midAnchor = BoardZoomCarry.ToWorld(midLocal, b.RigPos, b.RigRot, b.RigScale);
        for (int i = 1; i <= 50; i++)
        {
            float s = Mathf.Lerp(RigMin, RigMax, i / 50f);
            Pinch(ref b, midAnchor, midLocal, s);
            b.Tick();
            Vector2 limits = new(MinW / b.PerUnit, MaxW / b.PerUnit);
            t.True(Near(limits.x, limits0.x, 1e-4f) && Near(limits.y, limits0.y, 1e-4f),
                   $"the gesture window is unmoved at rig ×{s:F2} (step {i})");
        }
        t.True(Near(b.PerUnit, perUnit0, 1e-5f),
               "perUnit — the one expression both enforcement points share — is a constant through a ×7.2 zoom");

        // THE OLD BEHAVIOUR, for contrast: a frozen holder makes the same window sweep by the zoom
        // factor, which is what pushed the overflow into BoardScale_<board> and moved the player's
        // own min/max every time the gesture fired.
        var frozen = Board.Fresh(RigMin);
        float fPerUnit0 = frozen.PerUnit;
        Pinch(ref frozen, midAnchor, midLocal, RigMax);   // holder stays baked at RigMin
        t.True(Near(frozen.PerUnit, fPerUnit0 * RigMin / RigMax, 1e-5f),
               "with a frozen holder the window scales by 1/s — this is the drift he reported twice");
    }

    // ---- the gate itself -------------------------------------------------------------------------
    private static void TheGateItself(Harness t)
    {
        t.Case("boardzoom/gate");
        t.True(!BoardZoomCarry.ScaleChanged(RigMid, RigMid, out _), "an unchanged scale is not a zoom");
        t.True(!BoardZoomCarry.ScaleChanged(RigMax, RigMax * (1f + 0.5e-4f), out _),
               "a change below the relative epsilon is not a zoom");
        t.True(BoardZoomCarry.ScaleChanged(RigMax, RigMax * (1f + 2e-4f), out _),
               "a change above it is");

        // RELATIVE, not absolute — the whole reason the epsilon is a ratio. The same 0.5 % step must
        // read the same at both ends of his ×19 … ×137 range; an absolute epsilon cannot do that.
        t.True(BoardZoomCarry.ScaleChanged(RigMin, RigMin * 1.005f, out float dLo), "0.5 % fires at ×19.01");
        t.True(BoardZoomCarry.ScaleChanged(RigMax, RigMax * 1.005f, out float dHi), "0.5 % fires at ×137.05");
        t.True(Near(dLo, dHi, 1e-5f), "and reports the SAME relative delta at both ends");

        // Degenerate operands are not zooms; the caller re-baselines instead of carrying by garbage.
        t.True(!BoardZoomCarry.ScaleChanged(0f, RigMid, out _), "a zero previous scale is not a zoom");
        t.True(!BoardZoomCarry.ScaleChanged(RigMid, float.NaN, out _), "a NaN scale is not a zoom");
        t.True(Near(BoardZoomCarry.CarryHolderScale(40.10f, 0f, RigMid), 40.10f),
               "and CarryHolderScale refuses to divide by a degenerate previous scale");

        // The accumulator property: a creep of less than one epsilon per frame must still be caught,
        // because the gate compares against the last CARRIED scale and not the last frame's.
        t.Case("boardzoom/sub-epsilon-creep-accumulates");
        var b = Board.Fresh(RigMid);
        float carried = b.CarryRigScale;
        bool everFired = false;
        for (int i = 0; i < 200; i++)
        {
            b.RigScale *= 1f + 0.5e-4f;   // half an epsilon per frame — invisible frame to frame
            if (b.Tick())
                everFired = true;
        }
        t.True(everFired, "a sub-epsilon creep is caught once it accumulates — it cannot slip the gate forever");
        t.True(b.RigScale > carried * 1.009f, "and the creep really was a whole percent of zoom");
    }

    // ---- SOURCE LINT: the phase ------------------------------------------------------------------
    //
    // Arithmetic cannot express an instant, and the instant is what ModBuild 159 got wrong: the same
    // maths in CardsDriver.Update ran BEFORE WorldGrab.Update wrote the new rig scale, so the board was
    // carried against the PREVIOUS frame's scale — "parent chain ×64.79 ÷ rig ×68.50" in 82 of 158
    // moving-zoom samples, a ±10 % breathing through every pinch. The read must happen in LateUpdate
    // (after every Update-phase rig writer) at an execution order above VRRigDriver.LateUpdate's tilt
    // heal (default 0) and below PerfFrameSplit's 30000. This lint is the only thing in the repository
    // that can notice if that is ever moved back.
    private static void PhaseLint(Harness t, string repoRoot)
    {
        t.Case("boardzoom/phase-lint");
        const string Driver = "src/GloomhavenVR/Cards/BoardZoomCarryDriver.cs";
        const string Watchdog = "src/GloomhavenVR/Cards/PlayTray.2.Watchdog.cs";

        string driverPath = Path.Combine(repoRoot, Driver.Replace('/', Path.DirectorySeparatorChar));
        t.True(File.Exists(driverPath), Driver + " exists");
        if (!File.Exists(driverPath))
            return;
        string src = File.ReadAllText(driverPath);

        t.True(src.Contains("[DefaultExecutionOrder(20000)]"),
               "the carry driver pins its execution order to 20000 — above VRRigDriver.LateUpdate (0), below PerfFrameSplit (30000)");
        t.True(src.Contains("private void LateUpdate()"),
               "and it runs in LateUpdate, so every Update-phase rig writer has already committed");
        t.True(!src.Contains("private void Update()"),
               "it must NOT have an Update — that phase is where ModBuild 159's one-frame lag came from");
        t.True(src.Contains("TickPinnedZoomCarry"), "and it drives the carry");

        // The carry itself must not be reachable from the Update phase. SyncPinHolder (which DOES run
        // in Update) is allowed to carry the board across a tracking-origin change — that is a
        // different signal and it is version-gated — but it must never write the holder scale.
        string watchdogPath = Path.Combine(repoRoot, Watchdog.Replace('/', Path.DirectorySeparatorChar));
        t.True(File.Exists(watchdogPath), Watchdog + " exists");
        if (!File.Exists(watchdogPath))
            return;
        string wd = File.ReadAllText(watchdogPath);

        int sync = wd.IndexOf("private void SyncPinHolder()", System.StringComparison.Ordinal);
        int carry = wd.IndexOf("private bool CarryPinnedBoardAcrossZoom", System.StringComparison.Ordinal);
        t.True(sync >= 0 && carry >= 0, "both the Update-phase pin housekeeping and the LateUpdate carry are present");
        if (sync < 0 || carry < 0)
            return;

        // Everything between SyncPinHolder and the carry is the Update-phase region; no holder scale
        // write may appear in it. (_pinRoot.localScale is written by exactly two places: ApplyFollowMode
        // at pin time, and CarryPinnedBoardAcrossZoom.)
        int syncEnd = wd.IndexOf("private Vector3 _rigLocalPinPos;", System.StringComparison.Ordinal);
        t.True(syncEnd > sync, "the pin housekeeping's end marker is where it is expected");
        if (syncEnd > sync)
        {
            string updatePhase = wd.Substring(sync, syncEnd - sync);
            t.True(!updatePhase.Contains("_pinRoot.localScale ="),
                   "the Update-phase pin housekeeping never writes the holder scale — that was ModBuild 159's defect");
        }

        // And the gate must be explicit at the call site: an unconditional carry is ModBuild 160.
        t.True(wd.Contains("BoardZoomCarry.ScaleChanged("),
               "the carry is gated on the rig scale having changed — the whole difference from ModBuild 160");

        // THE BASELINE MUST BE ADVANCED BY A SUCCESSFUL CARRY, and this is here because it was
        // MISSING when this file was first written and the vectors above did not notice: they drive
        // BoardZoomCarry's arithmetic and reproduce the sequencing themselves, so a sequencing bug in
        // PlayTray is invisible to them by construction. Without the advance the gate compares every
        // later frame against the scale at the last re-baseline — so it stays open for the rest of the
        // session, and CarryHolderScale re-applies the whole accumulated ratio every frame, running
        // the holder to infinity in about a second. Exactly two assignments to _zoomCarryRigScale are
        // legal: the re-baseline, and the one guarded by a carry that returned true.
        int assignments = 0;
        for (int i = wd.IndexOf("_zoomCarryRigScale = rigScale;", System.StringComparison.Ordinal);
             i >= 0;
             i = wd.IndexOf("_zoomCarryRigScale = rigScale;", i + 1, System.StringComparison.Ordinal))
            assignments++;
        t.Equal(2, assignments, "the carry baseline is advanced in exactly two places: re-baseline, and a successful carry");
        t.True(wd.Contains("&& CarryPinnedBoardAcrossZoom("),
               "and the second one is gated on the carry having actually written — a declined carry must retry, not be swallowed");
        t.True(wd.Contains("private bool CarryPinnedBoardAcrossZoom("),
               "which requires the carry to REPORT whether it wrote");

        // The carry must write the HOLDER, not the tray: writing the tray's world pose moves its
        // parent-local pose, and CardsDriver's issue-C watch would then log at frame rate for the
        // whole gesture (TickBoardPoseWatch compares localPosition/localRotation/localScale).
        int carryBody = wd.IndexOf("private bool CarryPinnedBoardAcrossZoom(", System.StringComparison.Ordinal);
        int carryEnd = carryBody < 0
            ? -1
            : wd.IndexOf("PINNED FREEZE SENTINEL", carryBody, System.StringComparison.Ordinal);
        if (carryBody > 0 && carryEnd > carryBody)
        {
            string body = wd.Substring(carryBody, carryEnd - carryBody);
            t.True(body.Contains("_pinRoot.SetPositionAndRotation("),
                   "the carry re-poses the HOLDER");
            t.True(!body.Contains("_root.SetPositionAndRotation("),
                   "and never the tray itself — the tray's parent-local transform must come out untouched");
        }
        else
        {
            t.True(false, "the carry body is where the lint expects it");
        }
    }
}
