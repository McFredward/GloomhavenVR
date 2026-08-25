using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// THE ONE TABLE OF NAMED ANCHOR EMPTIES a bundled control board may carry, and the resolver that
/// turns those names into transforms.
///
/// <para><b>WHY THIS IS ITS OWN FILE.</b> The very same name list had been written out FOUR times —
/// <c>PlayTray.EnsureBuilt</c> (the local board), <c>Net.RemoteTrayVisual.Build</c> (a peer's copy of
/// that board), <c>Board.BoardFrame.AnchorNames</c> (the contour trace's exclusion set) and
/// <c>unity/…/Editor/BuildBoard.cs</c> (the asset assembler, a different assembly and therefore the
/// one copy that HAS to stay a copy). Three of those four are in this solution, and a board that
/// renames an anchor is exactly the change where one of them gets missed: the local board would seat
/// its keycaps on the new recess while a peer's mirror kept drawing them at the Oak fallback mount —
/// a divergence in the picture, which is the one thing the 1:1 rule forbids. One table, three
/// readers.</para>
///
/// <para><b>THE BUTTON SEATS ARE NUMBERED, NOT NAMED AFTER THEIR OCCUPANT.</b> The boards ship three
/// physical button recesses (user, 2026-08: "ist 3 das Maximum an gleichzeitigen Knöpfen. Daher
/// möchte alle 3 Boards so umgebaut haben, dass sie auf der rechten Seite … 3 statt 2 Slots für die
/// buttons haben"), and which control sits in which recess is a runtime question — Confirm and the
/// item "Use" cap already share seat 0 because they are mutually exclusive. So the mesh names a
/// SEAT (<c>ButtonSeat1/2/3</c>) and the mod decides the occupant.</para>
///
/// <para><b>BOTH SPELLINGS RESOLVE, AND THAT IS DELIBERATE, NOT TRANSITIONAL.</b> Every seat carries
/// an ALIAS list, tried in order, and the legacy <c>ConfirmButton</c> / <c>UndoButton</c> names are
/// permanent members of it:
/// <list type="bullet">
///   <item>seat 0 — <c>ButtonSeat1</c>, then <c>ConfirmButton</c></item>
///   <item>seat 1 — <c>ButtonSeat2</c>, then <c>UndoButton</c></item>
///   <item>seat 2 — <c>ButtonSeat3</c>, then <c>SkipButton</c></item>
/// </list>
/// The mod ships one DLL against whatever bundle is installed, and the shipped bundle
/// (72,966,925 bytes, unchanged for twenty builds — installs have been DLL-only) still carries the
/// two-anchor boards. A resolver that only knew the new spelling would put every existing player's
/// keycaps back on the procedural fallback anchors the moment they updated the plugin. The reverse
/// is just as real: the asset lane regenerating the three boards reports what it emits, and this
/// resolver must not care which of the two it turns out to be.</para>
///
/// <para><b>ADDITIVE OR RENAME?</b> Both, and the alias list is what makes that a non-question: for
/// seats 0 and 1 it is a RENAME WITH ALIASES (same recess, new preferred spelling, old spelling kept
/// forever), and seat 2 is genuinely ADDITIVE — no old board has a third recess, so its absence is
/// the normal case a two-anchor board must survive, not an error. Naming seat 2's alias
/// <c>SkipButton</c> costs nothing and covers the one other name a mesh author would plausibly reach
/// for, since Skip is the control the third recess exists for (see the three-cap enumeration in
/// <c>WorldUI/ButtonCluster.cs</c>).</para>
/// </summary>
internal static class BoardAnchors
{
    /// <summary>How many button seats the cluster can address. THREE, and the number is a finding,
    /// not a preference: <c>WorldUI/ButtonCluster.cs</c> enumerated every
    /// <c>readyButton</c>/<c>m_UndoButton</c>/<c>m_SkipButton</c> toggle site in the decompiled
    /// <c>Choreographer</c> and found six states where all three are live at once, and none where a
    /// fourth mod cap could join (<c>m_selectButton</c> is mutually exclusive with the ready button
    /// at every site that raises it, and the mod draws no cap for it).</summary>
    internal const int ButtonSeatCount = 3;

    /// <summary>
    /// The four anchors the board's ORIENTATION FRAME is derived from — <c>Slot1→Slot2</c> is the
    /// long axis, <c>ShortRestToken→LongRestToken</c> the short one, their cross product the
    /// decorated-face normal. Load-bearing in three places (<c>PlayTray.EnsureBuilt</c>,
    /// <c>RemoteTrayVisual.Build</c> and the editor assembler) and deliberately NOT extended by the
    /// button seats: the frame must not change meaning when a board gains or loses a recess.
    /// </summary>
    internal static readonly string[] FrameAnchorNames =
        { "Slot1", "Slot2", "ShortRestToken", "LongRestToken" };

    /// <summary>Accepted names per button seat, MOST PREFERRED FIRST. See the class note.</summary>
    private static readonly string[][] SeatAliases =
    {
        new[] { "ButtonSeat1", "ConfirmButton" },
        new[] { "ButtonSeat2", "UndoButton" },
        new[] { "ButtonSeat3", "SkipButton" },
    };

    /// <summary>
    /// EVERY name a bundled board may legitimately carry — the four frame anchors plus every accepted
    /// spelling of every seat. This is the set <c>BoardFrame</c> excludes from the contour trace
    /// (nothing parked on an anchor is part of the board's silhouette), so it has to list the
    /// ALIASES too: a board authored with the new spelling whose caps were traced as board geometry
    /// would push the stroke out past the real rim.
    /// </summary>
    internal static readonly string[] AllAnchorNames = BuildAllNames();

    private static string[] BuildAllNames()
    {
        int n = FrameAnchorNames.Length;
        foreach (string[] seat in SeatAliases)
            n += seat.Length;
        var all = new string[n];
        int w = 0;
        foreach (string s in FrameAnchorNames)
            all[w++] = s;
        foreach (string[] seat in SeatAliases)
            foreach (string s in seat)
                all[w++] = s;
        return all;
    }

    /// <summary>The preferred (authoring) name of button seat <paramref name="seat"/> — what a
    /// freshly generated board is expected to carry, and what the log names when it is missing.</summary>
    internal static string SeatName(int seat) =>
        seat >= 0 && seat < SeatAliases.Length ? SeatAliases[seat][0] : $"ButtonSeat{seat + 1}";

    /// <summary>Every accepted spelling of one seat, for a log line.</summary>
    internal static string SeatNamesJoined(int seat) =>
        seat >= 0 && seat < SeatAliases.Length ? string.Join("/", SeatAliases[seat]) : SeatName(seat);

    /// <summary>True when <paramref name="name"/> is one of the anchor names in
    /// <see cref="AllAnchorNames"/> (any seat, any spelling).</summary>
    internal static bool IsAnchorName(string name)
    {
        foreach (string s in AllAnchorNames)
            if (s == name)
                return true;
        return false;
    }

    /// <summary>
    /// Resolve button seat <paramref name="seat"/> under <paramref name="visualRoot"/>, trying every
    /// accepted spelling in order. Null when the board has no such recess — which is the ORDINARY
    /// answer for seat 2 on every board shipped so far, and the caller's cue to fall back
    /// (<c>PlayTray.BuildButtons</c> synthesises a procedural anchor; the peer mirror keeps its
    /// authored mount).
    /// </summary>
    internal static Transform? FindSeat(Transform visualRoot, int seat)
    {
        if (visualRoot == null || seat < 0 || seat >= SeatAliases.Length)
            return null;
        foreach (string name in SeatAliases[seat])
        {
            Transform? t = FindDeep(visualRoot, name);
            if (t != null)
                return t;
        }
        return null;
    }

    /// <summary>
    /// Resolve all <see cref="ButtonSeatCount"/> seats into <paramref name="into"/> (length
    /// <see cref="ButtonSeatCount"/>) and return how many the board actually supplies. A board that
    /// supplies seats 0 and 1 but not 2 returns 2 — the two-anchor fallback, not a failure.
    /// </summary>
    internal static int ResolveSeats(Transform visualRoot, Transform?[] into)
    {
        int found = 0;
        for (int i = 0; i < ButtonSeatCount && i < into.Length; i++)
        {
            into[i] = FindSeat(visualRoot, i);
            if (into[i] != null)
                found++;
        }
        return found;
    }

    // ------------------------------------------------------------ seat recess extents --

    /// <summary>
    /// Name of the empty that carries button seat <paramref name="seat"/>'s MEASURED RECESS SIZE.
    /// Written by the editor assembler (<c>unity/…/Editor/BuildBoard.cs</c>), read here.
    /// </summary>
    internal static string SeatExtentName(int seat) => $"SeatExtent{seat + 1}";

    /// <summary>
    /// The measured HALF-EXTENTS of button seat <paramref name="seat"/>'s recess FLOOR, in board
    /// metres — x along the board's long axis, y along its short axis. Null when the board carries
    /// no measurement (every bundle built before this, and the procedural fallback board).
    ///
    /// <para><b>WHY THE PREFAB CARRIES A MEASUREMENT AT ALL.</b> The keycaps are sized from
    /// <c>[BoardButtons] Width/Height</c>, one global pair the user dialled in — shipped
    /// 0.063 × 0.065 m (<c>Defaults.BoardButtons_Width/Height</c>, which is what his cfg holds;
    /// <c>ButtonTuning.DefaultBoardWidth</c>'s 0.073 is only the PRE-BIND fallback and is never the
    /// live cap). The three re-authored boards cut their button recesses at three different sizes,
    /// and that 65 mm height fits none of them — so a cap that fits the tuning overhangs its own
    /// seat. The size therefore has to be fitted PER BOARD, and the only honest
    /// source for "how big is this recess" is the board itself.</para>
    ///
    /// <para><b>WHY MEASURED AT IMPORT RATHER THAN BAKED AS CONSTANTS.</b> The alternative was three
    /// pairs of authored numbers in <c>Defaults</c>. It was rejected on evidence: the numbers this
    /// round was handed for the three recesses (79.0 × 68.7 / 83.2 × 72.3 / 71.6 × 62.3 mm) turned
    /// out to describe the recess at its top RIM, while a keycap sits on its FLOOR — which a
    /// ray-cast sweep of the three committed FBXes measures at 74.6 × 64.3 / 81.0 × 70.1 /
    /// 61.2 × 51.9 mm. Bronze is 10.4 mm narrower than the number that would have been baked. A
    /// second-hand figure about geometry went stale before it was even written down; the assembler
    /// reads the geometry it is already ray-casting for the anchor projection, so it cannot.</para>
    ///
    /// <para>The same empties bound the OFFSET, not only the size — see
    /// <see cref="ClampSeatPose"/>. One measurement, two jobs: how big a cap may be, and how far it
    /// may be nudged before it leaves the well.</para>
    ///
    /// <para><b>IT COSTS NO WIRE FIELD.</b> A peer clones the SAME prefab out of the SAME bundle
    /// (<c>Net.RemoteTrayVisual</c>), so the peer measures the identical extents and
    /// <see cref="FitCapSize"/> gives the identical answer. The fitted size is derived on every
    /// client from data every client already has, exactly like the seat POSES are.</para>
    /// </summary>
    internal static Vector2? SeatExtent(Transform visualRoot, int seat) =>
        MeasuredExtent(visualRoot, SeatExtentName(seat));

    /// <summary>Names of the empties carrying the two REST PAD measurements — same mechanism, same
    /// assembler, same clamp; <c>RestControls</c>'s discs sit in an authored pad exactly the way the
    /// keycaps sit in an authored recess.</summary>
    internal static string RestExtentName(bool shortRest) => shortRest ? "RestExtentShort" : "RestExtentLong";

    /// <summary>The measured half-extents of a rest pad's floor, or null when unmeasured.</summary>
    internal static Vector2? RestExtent(Transform visualRoot, bool shortRest) =>
        MeasuredExtent(visualRoot, RestExtentName(shortRest));

    /// <summary>
    /// Read one measurement empty. Its <c>localPosition</c> x/y are HALF-extents in board metres, not
    /// a position — see <see cref="SeatExtent"/> for why the prefab carries these at all.
    /// </summary>
    private static Vector2? MeasuredExtent(Transform visualRoot, string name)
    {
        Transform? t = visualRoot != null ? FindDeep(visualRoot, name) : null;
        if (t == null)
            return null;
        Vector3 p = t.localPosition;
        float hx = Mathf.Abs(p.x);
        float hy = Mathf.Abs(p.y);
        // A measurement is only usable if it is SANE. Rejecting an absurd one and falling back to
        // the tuned size is the difference between an instrument and an instrument that lies: the
        // assembler runs in an editor nobody watches, and a mis-measured seat would silently resize
        // every keycap on the board. Band: 10 mm (smaller than any pressable recess) to 200 mm (the
        // upper clamp ButtonTuning already puts on the cap itself).
        if (hx < 0.005f || hy < 0.005f || hx > 0.100f || hy > 0.100f)
        {
            VRLog.Warn("Cards", $"Board: '{name}' carries an out-of-band recess half-extent " +
                                $"({hx:F4}, {hy:F4}) m — ignored; the caps that sit there keep their " +
                                "tuned size and their tuned offset, unclamped.");
            return null;
        }
        return new Vector2(hx, hy);
    }

    /// <summary>
    /// THE FIT. The cap size actually built, given the user's tuned <paramref name="tuned"/> W×H and
    /// the tightest seat recess on this board (<paramref name="minHalf"/>, null = no measurement).
    ///
    /// <para><b>IT ONLY EVER SHRINKS.</b> <c>[BoardButtons] Width/Height</c> stays the ceiling — the
    /// value the fit is measured AGAINST, never a value the fit rewrites. A board whose recesses are
    /// roomier than the tuning gets exactly the tuned cap, which is why raising the global still
    /// does what he expects up to the point where the seat runs out. Lowering the GLOBAL to make
    /// Bronze fit would have made Oak and Steel wear Bronze's cap and would have re-seated a number
    /// he dialled in himself as a side effect of an asset change — the same class of defect as the
    /// centred cluster stack that had to be made top-anchored last round.</para>
    ///
    /// <para><b>ONE SIZE FOR THE WHOLE CLUSTER.</b> The caller passes the SMALLEST half-extent over
    /// the board's seats, so Confirm, Undo and the item "Use" cap stay identical to each other —
    /// the standing ruling ("der Use-Button soll genauso groß sein und sich nach den Werten richten,
    /// die die generischen Buttons vorgegeben haben"). It is a no-op on the three shipped boards,
    /// whose three seats are cut to the same size, and it is the right rule the day one is not.</para>
    ///
    /// <para><b>THE MARGIN IS THE CAP'S OWN TRAVEL</b> (<paramref name="margin"/> —
    /// <c>[BoardButtons] Travel</c>, shipped 4 mm). It is not invented: it is the only LENGTH in the
    /// cap's own tuning family that describes CLEARANCE rather than SIZE, and it is already the
    /// distance the cap moves inside this well every time it is pressed. Reusing it laterally makes
    /// the clearance isotropic — the same gap all round the cap that the cap travels through — so
    /// the well reads as a well instead of a slot the cap fills edge to edge, and a user who dials a
    /// deeper press gets a deeper-looking seat to match. The caller clamps it into a sane band so a
    /// zero travel cannot produce an edge-to-edge cap.</para>
    /// </summary>
    internal static Vector2 FitCapSize(Vector2 tuned, Vector2? minHalf, float margin)
    {
        if (minHalf == null)
            return tuned;
        float w = Mathf.Min(tuned.x, 2f * (minHalf.Value.x - margin));
        float h = Mathf.Min(tuned.y, 2f * (minHalf.Value.y - margin));
        // Floor at ButtonTuning's own lower clamps (0.020 / 0.015 m): a cap smaller than that is
        // not pressable in VR, and if a recess is genuinely that tight the honest failure is a cap
        // that overhangs slightly, not one nobody can hit.
        return new Vector2(Mathf.Max(0.020f, w), Mathf.Max(0.015f, h));
    }

    // ------------------------------------------------------ keeping a cap in its own seat --

    /// <summary>
    /// THE SLACK a cap has inside its seat: how far its centre may move from the seat anchor before
    /// the cap's edge reaches the recess wall, per axis, in board metres. Zero when the cap exactly
    /// fills the recess.
    /// </summary>
    internal static Vector2 SeatSlack(Vector2 minHalf, Vector2 capSize) =>
        new(Mathf.Max(0f, minHalf.x - capSize.x * 0.5f),
            Mathf.Max(0f, minHalf.y - capSize.y * 0.5f));

    /// <summary>
    /// THE SEAT POSE: where a cap actually sits, anchor-local. Takes the tuned per-board
    /// <paramref name="offset"/> and the stack term <paramref name="seatY"/>, and CLAMPS the in-plane
    /// part so the cap cannot leave the recess it is sitting in. Z passes through untouched — that is
    /// the proud depth toward the player and has nothing to do with the recess walls.
    ///
    /// <para><b>THE PROBLEM THIS SOLVES.</b> <c>ConfirmUndoOffset_Steel.x</c> is +0.462 and
    /// <c>_Bronze.x</c> is +0.447 — 46 cm on a 64 cm board. Those are not nudges: the SHIPPED Steel
    /// and Bronze boards had their zones mirrored against Oak (buttons on -x, rest on +x), and the
    /// user dialled the whole cluster across the board to put it back on the right-hand side. The
    /// re-authored boards are canonical, so the same dial now pushes the cluster ~35 cm clear OFF the
    /// board. <c>RestButtonOffset_Steel.x</c> = -0.44 and <c>_Bronze.x</c> = -0.445 do the same to the
    /// rest discs in the other direction. And <c>GenericButtonSpacing_Bronze</c> = 0.06 was tuned as
    /// an inter-cap gap against anchors 110 mm apart, so on a 70 mm recess pitch it lands the three
    /// caps 41 / 19 / 79 mm off their recess centres.</para>
    ///
    /// <para><b>WHY A CLAMP AND NOT A CANONICAL/MIRRORED DETECTOR.</b> The obvious alternative is to
    /// read the layout off the anchor frame (seats on +x, rest pads on -x = canonical) and apply only
    /// the offset's Z there. It is wrong, and its own acceptance test is what kills it: <b>Oak has
    /// always been canonical.</b> <c>ConfirmUndoOffset_Oak</c> = (-0.008, 0, +0.009) and
    /// <c>RestButtonOffset_Oak</c> = (+0.008, 0, -0.007) are genuine 8 mm nudges tuned ON a canonical
    /// board — so "canonical then Z only" moves Oak's caps 8 mm on the bundle he is running RIGHT
    /// NOW. A canonical/mirrored test has no memory of WHEN a value was tuned; it cannot tell
    /// "canonical and always was" from "canonical now, mirrored when tuned", and it discards a real
    /// nudge in order to undo a relocation.</para>
    ///
    /// <para><b>WHAT THIS KEYS ON INSTEAD</b> is the board's GENERATION, observed through the one
    /// fact the cap fit already reads: does this board carry a MEASURED recess. An old-bundle board
    /// carries none, gets no bound, and is laid out bit-identically to today — Oak included, which is
    /// the acceptance condition. A re-authored board carries one, and its caps cannot leave their
    /// wells. It is not a threshold in disguise: nothing is compared against a constant, the bound IS
    /// the geometry of the seat the cap sits in, measured off the same mesh that decides the cap's
    /// size.</para>
    ///
    /// <para><b>THE SPACING FALLS OUT OF THE SAME RULE.</b> <paramref name="seatY"/> is folded in
    /// BEFORE the clamp, so the stack term is bounded by the same wall. That is deliberate rather
    /// than a second decision about <c>GenericButtonSpacing</c> / <c>RestButtonSpacing</c>: on a board
    /// with authored seats the anchor pitch already IS the spacing, so any spacing on top of it is
    /// double-counting — and the honest remedy for a double count is not to let it leave the well.
    /// Ignoring those dials outright was the alternative; it needs a second condition (a measured
    /// board can still supply only two seats), it throws away the DIRECTION of a tuning that this
    /// preserves as far as the geometry allows, and it lands within the same few millimetres. The
    /// residual is at most the slack, which is the margin <see cref="FitCapSize"/> already reserved.</para>
    ///
    /// <para><b>IT IS DERIVED, SO IT COSTS NO WIRE FIELD.</b> Every term — the tuned offset and
    /// spacing (already synced through record 28), the measured recess, the fitted cap — is available
    /// identically on every client, and this is the single implementation all three callers use
    /// (<c>PlayTray.SetConfirmUndoOffset</c>, <c>RestControls.SetOffset</c> and
    /// <c>Net.RemoteBoardFurniture</c>), so a peer's board cannot lay out differently from the
    /// owner's.</para>
    /// </summary>
    internal static Vector3 ClampSeatPose(Vector3 offset, float seatY, Vector2? minHalf, Vector2 capSize)
    {
        Vector3 p = offset + new Vector3(0f, seatY, 0f);
        if (minHalf == null)
            return p;   // unmeasured board: no bound is known, so nothing is bounded (today's layout)
        Vector2 slack = SeatSlack(minHalf.Value, capSize);
        return new Vector3(Mathf.Clamp(p.x, -slack.x, slack.x),
                           Mathf.Clamp(p.y, -slack.y, slack.y),
                           p.z);
    }

    /// <summary>True when the clamp actually bit — the tuned offset would have put this cap outside
    /// its own seat. Callers LOG it; nothing about the layout depends on it.</summary>
    internal static bool SeatPoseWasClamped(Vector3 requested, Vector3 clamped) =>
        Mathf.Abs(requested.x - clamped.x) > 1e-6f || Mathf.Abs(requested.y - clamped.y) > 1e-6f;

    // ------------------------------------- keeping the MESH under the controls that sit on it --

    /// <summary>
    /// How far any pinned anchor may end up standing off the mesh surface behind it, in board
    /// metres, once the asset pose has moved the mesh. 5 mm — the same order as the seat slack a
    /// keycap already has, and a tenth of the thinnest board's 34 mm thickness, so a cap displaced
    /// by the whole budget is still visibly seated in its well.
    /// </summary>
    internal const float MaxAnchorLift = 0.005f;

    /// <summary>
    /// THE ASSET POSE, BOUNDED: the per-board mesh offset and euler that
    /// <c>PlayTray.SetAssetPose</c> applies, clamped so the board mesh cannot walk out from under
    /// the control set that stays pinned to it.
    ///
    /// <para><b>THE PROBLEM THIS SOLVES, observed and not inferred.</b> The asset pose moves the
    /// BOARD MESH while every anchor is written back to where it was, so cards, keycaps and rest
    /// discs stay put and the art slides beneath them. It exists to correct a mesh whose decorated
    /// face is not coplanar with its own anchor plane, and the SHIPPED Bronze board was exactly
    /// that — a raked lectern 301 mm deep. <c>AssetOffset_Bronze</c> = (0, -0.11, +0.08) and
    /// <c>AssetPitchDegrees_Bronze</c> = 57 are in the user's live cfg, where they beat any default
    /// this repo can change, and they were correct for that mesh.</para>
    ///
    /// <para>The re-authored Bronze is a flat 0.640 x 0.320 x 0.0354 m plate whose face IS the
    /// anchor plane. Replaying those two dials against it in the Unity assembler
    /// (<c>Editor/PreviewBoard.ApplyAssetPose</c>, 2026-08-25) left THREE of the five seat and rest
    /// anchors with no mesh behind them at all, and the other two standing 151.5 mm and 167.1 mm
    /// off the surface: the board tips up like a wall and the whole control set hangs in the air in
    /// front of it. Oak and Steel, whose dials are identity, measured 0.5 mm — which is exactly the
    /// <c>proud</c> offset the assembler seats every anchor with, so that pair is the control.</para>
    ///
    /// <para><b>WHY A BOUND AND NOT "ZERO IT ON A NEW BOARD".</b> The dial is a control the user
    /// asked for, and hard-zeroing it on the re-authored boards would delete a feature to fix a
    /// stale value. Matching the tuned value against the historical constant and dropping it was
    /// the other candidate: it silently ignores 57 deg if he ever dials it again on purpose, which
    /// is worse than the defect. A bound keeps every small correction he can express and refuses
    /// only the ones that separate the mesh from the controls — which on a flat board is the whole
    /// point, and the arithmetic below says so rather than a constant saying so.</para>
    ///
    /// <para><b>THE GATE IS THE ONE <see cref="ClampSeatPose"/> ALREADY USES</b> — does this board
    /// carry a measured recess. <paramref name="anchorRadius"/> null means an old-bundle board: no
    /// measurement, no bound, bit-identical to today, so the raked lectern still gets laid flat on
    /// the bundle currently installed on his machine. A re-authored board supplies the radius and
    /// is bounded.</para>
    ///
    /// <para><b>THE TILT BOUND IS DERIVED, NOT PICKED.</b> Rotating the mesh by theta about the
    /// board root lifts an anchor at radius r by at most r*sin(theta), so the angle that spends the
    /// whole <see cref="MaxAnchorLift"/> budget is asin(lift/r) — and r is MEASURED as the furthest
    /// pinned anchor from the root, not assumed. On these boards that is about 0.247 m, so the
    /// bound lands near 1.2 deg: on a flat plate any real tilt separates the mesh from the pinned
    /// set, and that is a fact about the geometry rather than a policy. The offset is bounded by
    /// the same budget on all three axes — along the normal it lifts every anchor equally, in plane
    /// it slides the art under a cap that has only its seat slack to give.</para>
    ///
    /// <para><b>DERIVED, SO IT COSTS NO WIRE FIELD.</b> The tuned pose already travels on extension
    /// record 28 and the radius is read off the same prefab on every client, so the owner and every
    /// peer clamp to the same numbers. <c>Net.RemoteTrayVisual</c> calls this exact method.</para>
    /// </summary>
    /// <param name="anchorRadius">Largest pinned-anchor distance from the board root, in board
    /// metres, or null on a board that carries no measured recess (old bundle → unbounded).</param>
    internal static void ClampAssetPose(ref Vector3 offset, ref Vector3 euler, float? anchorRadius)
    {
        if (anchorRadius == null || anchorRadius.Value <= 1e-4f)
            return;   // unmeasured board: no bound is known, so nothing is bounded (today's layout)

        // THE BUDGET IS SPLIT BECAUSE THE TWO TERMS ADD. An offset along the board normal lifts
        // every anchor by its own magnitude and a tilt lifts the furthest one by r*sin(theta);
        // giving each the whole budget would let a pose that spends both stand an anchor 10 mm
        // proud, which is twenty times the 0.5 mm the assembler seats one at and would read as a
        // keycap hovering over its well. Half each, so the guarantee in the summary is the one the
        // arithmetic actually makes.
        const float half = MaxAnchorLift * 0.5f;
        offset = new Vector3(Mathf.Clamp(offset.x, -half, half),
                             Mathf.Clamp(offset.y, -half, half),
                             Mathf.Clamp(offset.z, -half, half));

        float maxDeg = Mathf.Asin(Mathf.Clamp01(half / anchorRadius.Value)) * Mathf.Rad2Deg;
        euler = new Vector3(Mathf.Clamp(euler.x, -maxDeg, maxDeg),
                            Mathf.Clamp(euler.y, -maxDeg, maxDeg),
                            Mathf.Clamp(euler.z, -maxDeg, maxDeg));
    }

    /// <summary>The largest distance from <paramref name="boardRoot"/> to any of the supplied
    /// pinned anchors, in board-root-local metres — the lever arm the tilt bound is derived from.
    /// Null when nothing usable was supplied, which is the same "no bound is known" state an
    /// unmeasured board is in.</summary>
    internal static float? AnchorRadius(Transform boardRoot, IReadOnlyList<Transform?> anchors)
    {
        if (boardRoot == null)
            return null;
        float r = 0f;
        int n = 0;
        for (int i = 0; i < anchors.Count; i++)
        {
            if (anchors[i] == null)
                continue;
            r = Mathf.Max(r, boardRoot.InverseTransformPoint(anchors[i]!.position).magnitude);
            n++;
        }
        return n == 0 ? null : r;
    }

    /// <summary>True when <see cref="ClampAssetPose"/> actually bit. Callers LOG it; nothing about
    /// the layout depends on it.</summary>
    internal static bool AssetPoseWasClamped(Vector3 reqOffset, Vector3 reqEuler,
                                             Vector3 offset, Vector3 euler) =>
        (reqOffset - offset).sqrMagnitude > 1e-12f || (reqEuler - euler).sqrMagnitude > 1e-8f;

    /// <summary>Depth-first name lookup — the same one every board reader already used privately.</summary>
    internal static Transform? FindDeep(Transform root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform? found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }
}
