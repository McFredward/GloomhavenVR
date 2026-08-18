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
/// <para>UNITY-FREE ON PURPOSE (beyond <c>Mathf</c>): this file is linked into
/// tests/GloomhavenVR.WireTests and driven value by value. The property under test — "the size
/// the gesture can reach does not move when the zoom does" — is observed ONLY from inside a
/// headset, one hardware round at a time, and it has now cost four shipped builds.</para>
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
