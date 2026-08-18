using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// THE CONTROL BOARD'S SIZE, IN ONE FRAME OF REFERENCE.
///
/// <para>Every number that describes "how big the control board is" now passes through here.
/// The class exists because the board's size was being expressed in THREE different frames at
/// once — the tray's own <c>localScale</c>, the board's WORLD width, and its APPARENT width in
/// the player's perceived metres — and the conversions between them were spelled out separately
/// at four call sites, each assuming a cancellation that was true in one anchor mode and false
/// in the other. Three shipped user reports came out of that (2026-08-03 "es spawned VIEL ZU
/// KLEIN", 2026-08-07 "beim Zoomen … wird das fixierte Board kleiner oder größer", 2026-08-15
/// "das Minimum und Maximum des boards ist immer noch abhängig von der Größe meiner Maske" —
/// see <see cref="PlayTray"/>'s watchdog file for the full chain).</para>
///
/// <para>THE THREE FRAMES AND THE TWO FACTORS BETWEEN THEM.
/// <code>
///   worldWidth    = BoardWidthLocal · localScale · parentScale
///   apparentWidth = worldWidth / rigScale          (what the player SEES, perceived metres)
///   sizeUnits     = apparentWidth / BoardWidthLocal  (what TrayScale × BoardScale speak in)
/// </code>
/// <c>parentScale</c> is everything above the tray (the rig in FOLGEN, the pin holder in
/// FIXIERT); <c>rigScale</c> is the player's own scale, which the table zoom moves between
/// 0.1× and 12× of base. The mod's limits — <c>[Cards] BoardMinWidthMeters</c> /
/// <c>BoardMaxWidthMeters</c> — are written in APPARENT metres, because that is the only frame
/// in which "18 to 140 cm" means anything to a person.</para>
///
/// <para>THE INVARIANT THE WHOLE FILE EXISTS TO PROTECT: <b>parentScale ≡ rigScale</b>. Both
/// anchors carry exactly ONE rig-scale factor — FOLGEN because the tray hangs under the rig,
/// FIXIERT because <c>PlayTray.SyncPinHolder</c> re-asserts the holder's scale from the live rig
/// every frame (world-pose-preserving). When it holds, <c>sizeUnits == localScale</c>, the
/// apparent width is <c>BoardWidthLocal × localScale</c> and NOTHING about the board's size
/// depends on the zoom, on the player's height, or on any cosmetic dial. The conversions below
/// still divide it out EXPLICITLY rather than assume it — that is the difference between code
/// that is correct and code that merely happens to be right — and
/// <see cref="AnchorRatio"/> is logged so a future stale holder shows up as a number instead of
/// as a resized board.</para>
///
/// <para>SIZE IS ONLY HALF OF WHAT THE EYE MEASURES, and the missing half cost ModBuild 159.
/// <b>Angular size = size ÷ distance.</b> Everything above freezes the NUMERATOR. The 2026-08-18
/// report ("Das Board zoomed immer noch im Fixiert modus mit") was the denominator: a world-space
/// pin left the board standing at fixed game-world coordinates while the zoom rescaled the PLAYER
/// about a pivot that is not the board, so its distance in player metres changed with every pinch
/// and the board grew and shrank in the eye — while this file's own diagnostic reported "apparent
/// cm" and called it constant, because that is all it measured. A diagnostic that agrees with a
/// broken build is itself a defect, so the distance arithmetic now lives here too
/// (<see cref="TryPlayerDistance"/>, <see cref="AngularWidthDegrees"/>), it is logged beside the
/// size, and the wire vectors drive both terms across a zoom sweep.</para>
///
/// <para>UNITY-FREE ON PURPOSE (beyond <c>Mathf</c> and the plain <c>Vector3</c>/<c>Quaternion</c>
/// value types, which the test assembly gets from the game's own UnityEngine.CoreModule): this
/// file is linked into tests/GloomhavenVR.WireTests and driven value by value. The property under
/// test — "the board does not change size or place when the zoom does" — is observed ONLY from
/// inside a headset, one hardware round at a time, and it has now cost five shipped builds.</para>
/// </summary>
internal static class BoardSizeFrame
{
    /// <summary>The board's own width in tray-local metres — its GEOMETRY, and the only
    /// board-side quantity any size bound is allowed to be a function of. Must equal
    /// <c>PlayTray.BoardW</c> (PlayTray.6.Build.cs); the wire vectors assert the pair.</summary>
    internal const float BoardWidthLocal = 0.64f;

    /// <summary>The shared grab handle's generic factor range, mirrored from
    /// <c>WorldUI.PanelGrabHandle.MinScale</c>/<c>MaxScale</c>. It is mirrored rather than
    /// referenced because this file must link into the test assembly without dragging in the
    /// WorldUI half of the mod; the wire vectors lint the two pairs against each other.
    ///
    /// <para>It matters here because <c>PanelGrabHandle</c> applies it FIRST and the owner's
    /// window SECOND, so an owner window that lies outside this range does not widen the
    /// gesture — it re-writes the already-clamped target and INFLATES the board. That is not a
    /// hypothesis: ModBuild 158's hardware log caught it, <c>BoardScale_Steel</c> ratcheting
    /// 0.54 → 1.00 → 1.13 in one session as the owner's zoom-coupled floor climbed above 2.
    /// The window this class hands out is therefore always INTERSECTED with this pair.</para></summary>
    internal const float GestureFactorMin = 0.15f;

    /// <inheritdoc cref="GestureFactorMin"/>
    internal const float GestureFactorMax = 2f;

    /// <summary>Smallest sane value for any measured scale factor — below this a transform is
    /// degenerate and the caller must fall back rather than divide.</summary>
    internal const float MinFactor = 1e-5f;

    /// <summary>
    /// The board's apparent width per UNIT of its own <c>localScale</c>, in perceived metres —
    /// the one measure the gesture window and the safety clamp share. Returns false when a
    /// transform is degenerate.
    /// </summary>
    internal static bool TryWidthPerScaleUnit(float parentScale, float rigScale, out float perUnit)
    {
        perUnit = 0f;
        if (!(parentScale > MinFactor) || !(rigScale > MinFactor)
            || float.IsInfinity(parentScale) || float.IsInfinity(rigScale))
            return false;
        perUnit = BoardWidthLocal * parentScale / rigScale;
        return perUnit > MinFactor && !float.IsInfinity(perUnit);
    }

    /// <summary>How far the tray's parent chain has drifted from the player's own scale. 1 when
    /// the invariant holds (see the class doc); anything else means the FIXIERT holder is stale
    /// and the board is being drawn at the wrong size for the current zoom.</summary>
    internal static float AnchorRatio(float parentScale, float rigScale) =>
        !(parentScale > MinFactor) || !(rigScale > MinFactor) ? 1f : parentScale / rigScale;

    /// <summary>Apparent width (perceived metres) of a tray at <paramref name="localScale"/>.</summary>
    internal static float ApparentWidth(float localScale, float parentScale, float rigScale) =>
        TryWidthPerScaleUnit(parentScale, rigScale, out float perUnit) ? perUnit * localScale : 0f;

    /// <summary>Size units (the frame <c>TrayScale × BoardScale</c> speak in) of a live tray —
    /// its apparent width divided by the board's own width. Equal to <paramref name="localScale"/>
    /// exactly when the anchor invariant holds.</summary>
    internal static float SizeUnits(float localScale, float parentScale, float rigScale) =>
        localScale / Mathf.Max(AnchorRatio(parentScale, rigScale), MinFactor);

    /// <summary>The inverse of <see cref="SizeUnits"/>: the <c>localScale</c> that renders
    /// <paramref name="units"/> at the current anchor.</summary>
    internal static float LocalScaleFor(float units, float parentScale, float rigScale) =>
        units * Mathf.Max(AnchorRatio(parentScale, rigScale), MinFactor);

    /// <summary>Apparent width, in perceived metres, of a board of <paramref name="units"/>.</summary>
    internal static float WidthOf(float units) => units * BoardWidthLocal;

    // ---------------------------------------------------------------- THE DENOMINATOR --
    //
    // The three conversions above answer "how big is it?". The two below answer "how big does it
    // LOOK?", which is the question the player is actually asking and the one ModBuild 159 got
    // wrong: it held the size and let the distance move. Both are pure functions of numbers the
    // caller already has, and both are driven by the wire vectors across the rig scales the
    // 2026-08-18 log recorded.

    /// <summary>
    /// How far the board is from the head IN PLAYER METRES — the world distance divided by the
    /// player's own scale, for exactly the reason the apparent width is: at rig scale 21 the player
    /// IS twenty-one times larger, so a world metre is 1/21 of a metre to them. This is the
    /// quantity a FIXIERT board must hold across any zoom, snap turn, teleport or world grab, and
    /// the ONE the size diagnostic used to be blind to. False when the rig scale is degenerate.
    /// </summary>
    internal static bool TryPlayerDistance(Vector3 headWorld, Vector3 boardWorld, float rigScale,
                                           out float metres)
    {
        metres = 0f;
        if (!(rigScale > MinFactor) || float.IsInfinity(rigScale))
            return false;
        float world = (boardWorld - headWorld).magnitude;
        if (float.IsNaN(world) || float.IsInfinity(world))
            return false;
        metres = world / rigScale;
        return !float.IsInfinity(metres);
    }

    /// <summary>
    /// The angle the board's width subtends at the eye, in degrees — <b>the number the user's
    /// report is about</b>. Size and distance are each only half of it, and holding one while the
    /// other moves is indistinguishable, from inside the headset, from holding neither. Scale-free
    /// by construction: both arguments are in player metres, so their ratio is invariant under any
    /// uniform rescale of the player, which is what makes it the right thing to assert. 0 for a
    /// degenerate distance (a board at the eye has no meaningful angular width).
    /// </summary>
    internal static float AngularWidthDegrees(float apparentWidth, float playerDistance) =>
        !(playerDistance > MinFactor) || !(apparentWidth > 0f)
        || float.IsInfinity(playerDistance) || float.IsInfinity(apparentWidth)
            ? 0f
            : 2f * Mathf.Atan2(apparentWidth * 0.5f, playerDistance) * Mathf.Rad2Deg;

    /// <summary>
    /// A world point expressed in the PLAYER'S OWN FRAME (the rig frame): metres the player would
    /// measure, along the player's own axes. This is the frame the FIXIERT pin is stored in since
    /// 2026-08-18 — <c>PlayTray</c> gets it for free from the transform hierarchy (the pin holder
    /// IS this frame), and the vectors get it from here so the invariant can be driven without a
    /// scene: a constant player-frame pose against a sweeping rig is what "fixiert" means.
    /// </summary>
    internal static Vector3 ToPlayerFrame(Vector3 rigPos, Quaternion rigRot, float rigScale,
                                          Vector3 worldPoint) =>
        // Conjugate, not Quaternion.Inverse: a rig rotation is a unit quaternion, for which the two
        // are the same value — and Inverse is an engine ECall, which would make this file
        // un-runnable in the wire tests (they link it and run OUTSIDE Unity, so every native entry
        // point throws SecurityException). The whole reason the arithmetic lives here is that it
        // can be driven without a headset; a native call would quietly take that away.
        new Quaternion(-rigRot.x, -rigRot.y, -rigRot.z, rigRot.w) * (worldPoint - rigPos)
        / Mathf.Max(rigScale, MinFactor);

    /// <summary>The inverse of <see cref="ToPlayerFrame"/>: where a player-frame point currently
    /// IS in the world. A rig-local pin moves through the world on every zoom, turn and step —
    /// that motion is not a defect, it is what holds the board still in the player's eye.</summary>
    internal static Vector3 ToWorld(Vector3 rigPos, Quaternion rigRot, float rigScale,
                                    Vector3 playerPoint) =>
        rigPos + rigRot * (playerPoint * rigScale);

    /// <summary>
    /// THE BOARD'S SIZE BOUNDS, in size units — a function of the board's own geometry
    /// (<see cref="BoardWidthLocal"/>) and of the two limits the player writes in perceived
    /// metres, AND OF NOTHING ELSE. No rig scale, no table zoom, no player height, no avatar or
    /// mask dial enters here; that is the whole point, and it is what the 2026-08-15 report asked
    /// for. Order-safe against a crossed or absurd config pair.
    /// </summary>
    internal static void Bounds(float minWidthMeters, float maxWidthMeters, out float lo, out float hi)
    {
        float min = Mathf.Max(minWidthMeters, 0.01f);
        float max = Mathf.Max(maxWidthMeters, min + 0.02f);
        lo = min / BoardWidthLocal;
        hi = max / BoardWidthLocal;
    }

    /// <summary>Hold a size (in units) inside <see cref="Bounds"/>.</summary>
    internal static float ClampUnits(float units, float minWidthMeters, float maxWidthMeters)
    {
        Bounds(minWidthMeters, maxWidthMeters, out float lo, out float hi);
        return Mathf.Clamp(units, lo, hi);
    }

    /// <summary>
    /// The window the two-hand resize may write into the tray's <c>localScale</c>: the bounds
    /// above, carried through the live anchor ratio, then INTERSECTED with the shared handle's
    /// generic factor range (see <see cref="GestureFactorMin"/> for why the intersection is not
    /// optional). Never inverted — a config or an anchor that would collapse the window yields a
    /// single reachable value rather than a floor above its own ceiling.
    /// </summary>
    internal static void GestureWindow(float minWidthMeters, float maxWidthMeters,
                                       float parentScale, float rigScale,
                                       out float lo, out float hi)
    {
        Bounds(minWidthMeters, maxWidthMeters, out float unitsLo, out float unitsHi);
        float ratio = Mathf.Max(AnchorRatio(parentScale, rigScale), MinFactor);
        lo = Mathf.Clamp(unitsLo * ratio, GestureFactorMin, GestureFactorMax);
        hi = Mathf.Clamp(unitsHi * ratio, GestureFactorMin, GestureFactorMax);
        if (hi < lo)
            hi = lo;
    }

    /// <summary>The apparent width, in perceived metres, that the two-hand gesture can actually
    /// reach at the current anchor — <see cref="GestureWindow"/> read back as a size. Reported in
    /// the board's diagnostic line because it is NOT always the configured pair: the handle's
    /// factor ceiling of 2 caps the shipped 140 cm maximum at 128 cm, and a player who cannot
    /// make the board as wide as the setting promises deserves to see why in the log.</summary>
    internal static void ReachableWidths(float minWidthMeters, float maxWidthMeters,
                                         float parentScale, float rigScale,
                                         out float loMeters, out float hiMeters)
    {
        GestureWindow(minWidthMeters, maxWidthMeters, parentScale, rigScale, out float lo, out float hi);
        loMeters = ApparentWidth(lo, parentScale, rigScale);
        hiMeters = ApparentWidth(hi, parentScale, rigScale);
    }
}
