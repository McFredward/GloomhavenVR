using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE VR ROW'S SEAT IN A MENU — owned every frame the menu is shown, never once.
///
/// <para><b>THE DEFECT (user, 2026-09-03, screenshot <c>options_tab_overlap.jpg</c>):</b> <i>"Manchmal
/// (nicht immer, ich weiß nicht was es auslöst), ist der Tab 'VR Optionen' genau auf dem 'Optionen'
/// Tab drauf statt auf seinem Platz."</i> In the picture the pause menu reads Fortfahren,
/// Mehrspieler, 'VR Optionen' printed EXACTLY over 'Optionen', Spielanleitung, an EMPTY row-sized
/// slot, Hauptmenü, Spiel verlassen.</para>
///
/// <para><b>THE MECHANISM, read off the decompiled game.</b> The rows of the pause menu are laid
/// out by a <c>VerticalLayoutGroupExtended</c> on their parent
/// (<c>HorizontalOrVerticalLayoutGroupExtended.SetChildrenAlongAxisExtended</c>, decompiled
/// <c>UnityEngine.UI/HorizontalOrVerticalLayoutGroupExtended.cs</c>:79-146; its
/// <c>GetChildren()</c> is a STABLE <c>OrderBy(priority)</c>, so a clone with its donor's
/// <c>layoutPriority</c> always sits directly after its donor). The mod's row is
/// <c>Instantiate</c>d from the game's Optionen row (<see cref="VRMenuEntry"/>), and
/// <c>Instantiate</c> runs the clone's <c>Awake</c> BEFORE the layout has ever seen it — while the
/// clone still carries the donor's <c>anchoredPosition</c>. That <c>Awake</c> is
/// <c>UIMenuOptionToggle.Awake</c> (decompiled <c>UIMenuOptionToggle.cs</c>:37-43), and it CACHES
/// the position: <c>_startAnchoredPosition = hoverRect.anchoredPosition; hoverDisplacement +=
/// hoverRect.anchoredPosition;</c>. The layout then seats the clone one slot down and pushes every
/// row after it down by one — and nothing ever re-caches. The next time the laser leaves a row,
/// <c>UIMenuOptionToggle.OnHovered</c> (<c>:100-111</c>) writes
/// <c>hoverRect.anchoredPosition = _startAnchoredPosition</c>: the clone lands on the donor's
/// slot, and every game row the injection pushed down lands ONE SLOT TOO HIGH. A position write
/// never dirties a layout group (only size, anchor and hierarchy changes do), so the layout never
/// runs again and the picture sticks. That is the screenshot to the pixel: Spielanleitung — hovered
/// on the way — one slot up, Hauptmenü and Spiel verlassen — not hovered — still seated, and the
/// empty slot between them where Spielanleitung belongs. "Manchmal" is "after the laser crossed
/// the rows". The game never shows this because without an injected row every cached start
/// position equals the slot the layout gives it.</para>
///
/// <para><b>THE FIX IS TWO THINGS, AND THE SECOND IS THE ONE THAT CANNOT BE DEFEATED.</b> First,
/// the stale caches are RE-BASED to the seat the layout actually gave each row (the game's own
/// writer then writes the right number, so the normal case never drifts at all). Second, every
/// frame the menu is shown, every row's position is compared with its seat; any row off its seat
/// — by that writer, by a select animation's authored <c>OriginalValue</c>
/// (<c>UIMenuOption.CancelSelectedAnimation</c>, <c>UIMenuOption.cs</c>:199-204), by a loop
/// animator's undo value captured in the same too-early <c>Awake</c> (<c>LoopAnimator.cs</c>
/// :116-135), by anything — triggers an IMMEDIATE layout rebuild, which is the game's own
/// authority on where rows go and cannot produce two rows in one slot. The seat cache is only ever
/// used to DETECT drift, never as the value written: the layout is asked again every time, so a
/// legitimate re-layout (a row appearing, a caption changing width) is adopted, not fought.
/// Hovered rows keep their nudge: after a rebuild the game's own re-based
/// <c>hoverDisplacement</c> is put back on the row that is under the laser.</para>
///
/// <para>This runs in <c>LateUpdate</c> (<c>WorldUIModule</c>'s late list), after every Update
/// writer the game has — the hover handlers, LeanTween, the animators — and before uGUI builds the
/// frame's canvases, so the rendered frame is always the seated one. Cost while nothing drifts: one
/// signature hash and one position compare per row per frame, on a menu of eight rows.</para>
///
/// <para><b>REVERSIBILITY.</b> The only game state touched is the two cached numbers on each row
/// (<c>_startAnchoredPosition</c>, <c>hoverDisplacement</c>), both of which the mod's injection
/// had already made wrong. Their ORIGINAL values are kept the first time a row is re-based, and
/// <see cref="Release"/> — run before a clone is destroyed — puts them back and rebuilds the
/// layout without the clone, so the game's rows are left exactly as they shipped even when the
/// menu is closed at teardown.</para>
/// </summary>
internal static class MenuRowSeat
{
    /// <summary>Two rows closer than this fraction of a row height are OVERPRINTED.</summary>
    private const float OverprintFraction = 0.5f;

    /// <summary>Drift smaller than this (px, squared) is float noise, not a writer.</summary>
    private const float DriftEpsilonSq = 0.25f;

    private const int ShowLogCap = 6;
    private const int DriftLogCap = 6;

    private readonly struct Row
    {
        public readonly RectTransform Rect;
        /// <summary>The row's toggle when its hover writer targets the row's own rect, else null.</summary>
        public readonly UIMenuOptionToggle? Writer;
        public Row(RectTransform rect, UIMenuOptionToggle? writer) { Rect = rect; Writer = writer; }
    }

    private sealed class Seat
    {
        public readonly string Menu;
        public RectTransform? Parent;
        public LayoutGroup? Layout;
        public readonly List<Row> Rows = new(10);
        public readonly Dictionary<RectTransform, Vector2> Seated = new();
        public int Signature;
        public bool Shown;
        public int RebuildsThisShow;
        public int RebasedThisShow;
        public int RebasedEver;
        public int Rebuilds;
        /// <summary>Row-to-donor distance read on the show edge BEFORE any rebuild: what the
        /// player would have seen had nothing acted.</summary>
        public float ArrivalGap;

        public Seat(string menu) { Menu = menu; }

        public void Reset()
        {
            Parent = null;
            Layout = null;
            Rows.Clear();
            Seated.Clear();
            Signature = 0;
            Shown = false;
            RebuildsThisShow = 0;
            RebasedThisShow = 0;
            ArrivalGap = 0f;
        }
    }

    private static readonly Seat PauseSeat = new("pause");
    private static readonly Seat MainSeat = new("main");

    /// <summary>Each re-based row's caches as the game left them, kept for <see cref="Release"/>.</summary>
    private static readonly Dictionary<UIMenuOptionToggle, (Vector2 start, Vector2 displacement)> Originals = new();

    private static int _showLogs;
    private static int _driftLogs;
    private static string? _lastShowSignature;
    private static string? _lastDriftSignature;

    /// <summary>
    /// One step for one menu. <paramref name="shown"/> is the menu's own open state; while it is
    /// false nothing is read and the show edge is re-armed.
    /// </summary>
    internal static void Tick(bool pause, UIMainMenuOption? row, UIMainMenuOption? donor, bool shown)
    {
        Seat seat = pause ? PauseSeat : MainSeat;
        if (!shown || row == null || donor == null)
        {
            seat.Reset();
            return;
        }

        var rowRect = row.transform as RectTransform;
        var donorRect = donor.transform as RectTransform;
        var parent = rowRect != null ? rowRect.parent as RectTransform : null;
        if (rowRect == null || donorRect == null || parent == null
            || !parent.gameObject.activeInHierarchy)
        {
            seat.Reset();
            return;
        }

        bool showEdge = !seat.Shown || !ReferenceEquals(seat.Parent, parent);
        if (showEdge)
        {
            seat.Reset();
            seat.Shown = true;
            seat.Parent = parent;
            seat.Layout = parent.GetComponent<LayoutGroup>();
            seat.ArrivalGap = Mathf.Abs(rowRect.anchoredPosition.y - donorRect.anchoredPosition.y);
        }

        int signature = LayoutSignature(parent, seat.Layout);
        bool relaid = false;
        if (showEdge || signature != seat.Signature || seat.Seated.Count == 0)
        {
            seat.Signature = signature;
            Reseat(seat, parent);
            relaid = true;
        }

        // THE BELT: any row off its seat means a writer ran after the layout. Ask the layout
        // again — never write the cached number — then restore the hover nudge the layout does
        // not know about.
        RectTransform? drifted = null;
        UIMenuOptionToggle? driftedWriter = null;
        Vector2 driftedFrom = default;
        Vector2 driftedSeat = default;
        for (int i = 0; i < seat.Rows.Count; i++)
        {
            Row r = seat.Rows[i];
            if (r.Rect == null || !seat.Seated.TryGetValue(r.Rect, out Vector2 expected))
                continue;
            if (r.Writer != null && r.Writer.isHovered && r.Writer._useStartAnchoredPosition)
                expected = r.Writer.hoverDisplacement;
            if ((r.Rect.anchoredPosition - expected).sqrMagnitude <= DriftEpsilonSq)
                continue;
            drifted = r.Rect;
            driftedWriter = r.Writer;
            driftedFrom = r.Rect.anchoredPosition;
            driftedSeat = expected;
            break;
        }

        if (drifted != null)
        {
            Reseat(seat, parent);
            relaid = true;
            LogDrift(seat, drifted, driftedWriter, driftedFrom, driftedSeat);
        }

        if (relaid)
            RestoreHoverNudge(seat);

        if (showEdge)
            LogShow(seat, rowRect, donorRect);
    }

    /// <summary>
    /// Before a clone is destroyed: give every re-based row its shipped caches back, take the
    /// clone out of the layout, and rebuild so the game's rows return to their own slots.
    /// </summary>
    internal static void Release(UIMainMenuOption? row)
    {
        foreach (KeyValuePair<UIMenuOptionToggle, (Vector2 start, Vector2 displacement)> kv in Originals)
        {
            UIMenuOptionToggle toggle = kv.Key;
            if (toggle == null)
                continue;
            toggle._startAnchoredPosition = kv.Value.start;
            toggle.hoverDisplacement = kv.Value.displacement;
        }
        Originals.Clear();
        PauseSeat.Reset();
        MainSeat.Reset();

        if (row == null)
            return;
        var rowRect = row.transform as RectTransform;
        var parent = rowRect != null ? rowRect.parent as RectTransform : null;
        row.gameObject.SetActive(false);
        if (parent == null || !parent.gameObject.activeInHierarchy)
            return;
        var layout = parent.GetComponent<LayoutGroup>();
        if (layout != null && layout.enabled)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
    }

    internal static void ResetLogLatches()
    {
        _showLogs = 0;
        _driftLogs = 0;
        _lastShowSignature = null;
        _lastDriftSignature = null;
        PauseSeat.RebasedEver = 0;
        MainSeat.RebasedEver = 0;
        PauseSeat.Rebuilds = 0;
        MainSeat.Rebuilds = 0;
    }

    // ==========================================================================================
    //  Mechanism
    // ==========================================================================================

    /// <summary>
    /// Rebuild the layout NOW, record every laid-out row's seat, and re-base the hover caches of
    /// every row whose cache no longer names its seat.
    /// </summary>
    private static void Reseat(Seat seat, RectTransform parent)
    {
        if (seat.Layout != null && seat.Layout.enabled)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
        seat.Rebuilds++;
        seat.RebuildsThisShow++;

        seat.Rows.Clear();
        seat.Seated.Clear();
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null || !child.gameObject.activeSelf || IgnoresLayout(child))
                continue;
            UIMenuOptionToggle? writer = HoverWriter(child);
            seat.Rows.Add(new Row(child, writer));
            seat.Seated[child] = child.anchoredPosition;

            if (writer == null || !writer._useStartAnchoredPosition)
                continue;
            Vector2 delta = child.anchoredPosition - writer._startAnchoredPosition;
            if (delta.sqrMagnitude <= DriftEpsilonSq)
                continue;
            if (!Originals.ContainsKey(writer))
                Originals[writer] = (writer._startAnchoredPosition, writer.hoverDisplacement);
            writer.hoverDisplacement += delta;
            writer._startAnchoredPosition = child.anchoredPosition;
            seat.RebasedThisShow++;
            seat.RebasedEver++;
        }
    }

    /// <summary>A rebuild puts a hovered row on its seat; the game's nudge goes back on it.</summary>
    private static void RestoreHoverNudge(Seat seat)
    {
        for (int i = 0; i < seat.Rows.Count; i++)
        {
            Row r = seat.Rows[i];
            if (r.Rect == null || r.Writer == null)
                continue;
            if (r.Writer.isHovered && r.Writer._useStartAnchoredPosition)
                r.Rect.anchoredPosition = r.Writer.hoverDisplacement;
        }
    }

    /// <summary>
    /// The row's toggle, but ONLY when its hover writer targets the row's own rect. When
    /// <c>hoverRect</c> is a child, its anchoredPosition is local to the row and the layout never
    /// touches it — there is nothing to re-base and nothing to guard.
    /// </summary>
    private static UIMenuOptionToggle? HoverWriter(RectTransform row)
    {
        var toggle = row.GetComponent<UIMenuOptionToggle>();
        if (toggle == null || toggle.hoverRect == null)
            return null;
        return ReferenceEquals(toggle.hoverRect, row) ? toggle : null;
    }

    private static bool IgnoresLayout(RectTransform child)
    {
        var element = child.GetComponent<LayoutElement>();
        return element != null && element.ignoreLayout;
    }

    /// <summary>
    /// What the layout's result depends on, folded: child count, active children, the parent's
    /// size and whether the group is enabled. A change means the render-time rebuild will move
    /// rows, so the seats are re-read from a rebuild of our own rather than compared stale.
    /// </summary>
    private static int LayoutSignature(RectTransform parent, LayoutGroup? layout)
    {
        int active = 0;
        for (int i = 0; i < parent.childCount; i++)
            if (parent.GetChild(i).gameObject.activeSelf)
                active++;
        Vector2 size = parent.rect.size;
        unchecked
        {
            int h = parent.childCount;
            h = h * 31 + active;
            h = h * 31 + Mathf.RoundToInt(size.x);
            h = h * 31 + Mathf.RoundToInt(size.y);
            h = h * 31 + (layout != null && layout.enabled ? 1 : 0);
            return h;
        }
    }

    // ==========================================================================================
    //  Instruments
    // ==========================================================================================

    private static void LogShow(Seat seat, RectTransform row, RectTransform donor)
    {
        if (_showLogs >= ShowLogCap)
            return;
        float rowHeight = row.rect.height;
        float gap = Mathf.Abs(row.anchoredPosition.y - donor.anchoredPosition.y);
        bool overprint = gap <= rowHeight * OverprintFraction;
        bool arrivedOverprinted = seat.ArrivalGap <= rowHeight * OverprintFraction;
        string layout = seat.Layout != null
            ? $"{seat.Layout.GetType().Name} enabled={seat.Layout.enabled}"
            : "NONE";
        UIMenuOptionToggle? writer = HoverWriter(row);
        string hover = writer != null
            ? $"hoverRect IS the row rect, useStart={writer._useStartAnchoredPosition}, start cache=({writer._startAnchoredPosition.x:F0},{writer._startAnchoredPosition.y:F0})"
            : "hoverRect is NOT the row rect (hover cannot move the row)";
        string signature = $"{seat.Menu}|{overprint}|{arrivedOverprinted}|{row.GetSiblingIndex()}|{donor.GetSiblingIndex()}"
            + $"|{Mathf.RoundToInt(row.anchoredPosition.y)}|{Mathf.RoundToInt(donor.anchoredPosition.y)}"
            + $"|{seat.Rows.Count}|{layout}";
        if (signature == _lastShowSignature)
            return;
        _lastShowSignature = signature;
        _showLogs++;

        string verdict = overprint
            ? $"OVERPRINT (the two rows are {gap:F0} px apart, within half a row height of {rowHeight:F0} px)"
            : $"SEATED (the two rows are {gap:F0} px apart, more than half a row height of {rowHeight:F0} px)";
        string arrival = arrivedOverprinted
            ? $"ON ARRIVAL the rows were {seat.ArrivalGap:F0} px apart — OVERPRINTED before this pass rebuilt the layout"
            : $"on arrival the rows were already {seat.ArrivalGap:F0} px apart";
        // HW-VERIFY
        VRLog.Note("WorldUI",
            $"VR MENU ROW ({seat.Menu}): '{row.name}' is sibling {row.GetSiblingIndex()} at anchored "
            + $"({row.anchoredPosition.x:F0},{row.anchoredPosition.y:F0}); its donor '{donor.name}' is "
            + $"sibling {donor.GetSiblingIndex()} at ({donor.anchoredPosition.x:F0},{donor.anchoredPosition.y:F0}); "
            + $"layout {layout}, {seat.Rows.Count} laid-out row(s) of {(seat.Parent != null ? seat.Parent.childCount : 0)} "
            + $"child(ren); hover writer: {hover}; this show re-based {seat.RebasedThisShow} stale start "
            + $"cache(s) ({seat.RebasedEver} this session) in {seat.RebuildsThisShow} rebuild(s); {arrival}. "
            + $"VERDICT {verdict}. A stale start cache is the writer behind the overprint: "
            + "UIMenuOptionToggle.Awake caches the row's position before the layout has seated a cloned "
            + "row (UIMenuOptionToggle.cs:37-43) and OnHovered writes it back on every hover exit "
            + "(:100-111). The seat is now owned every frame the menu is shown; grep 'VR MENU ROW DRIFT' "
            + "for any writer that still moves a row.");
    }

    private static void LogDrift(Seat seat, RectTransform row, UIMenuOptionToggle? writer,
                                 Vector2 from, Vector2 expected)
    {
        if (_driftLogs >= DriftLogCap)
            return;
        bool hovered = writer != null && writer.isHovered;
        string signature = $"{seat.Menu}|{row.name}|{hovered}";
        if (signature == _lastDriftSignature)
            return;
        _lastDriftSignature = signature;
        _driftLogs++;

        string cache = writer != null
            ? $"its start cache reads ({writer._startAnchoredPosition.x:F0},{writer._startAnchoredPosition.y:F0})"
            : "its hover writer does not target the row rect";
        // HW-VERIFY
        VRLog.Note("WorldUI",
            $"VR MENU ROW DRIFT ({seat.Menu}): '{row.name}' sat at ({from.x:F0},{from.y:F0}) instead of its "
            + $"seat ({expected.x:F0},{expected.y:F0}) (hovered={hovered}; {cache}) and was re-seated by a "
            + $"layout rebuild (rebuild #{seat.Rebuilds} this session). If the start cache already equals "
            + "the seat, the writer is NOT the hover handler: look at the row's select animation "
            + "(UIMenuOption.CancelSelectedAnimation -> GUIAnimator.GoInitState writes an authored "
            + "OriginalValue, UIMenuOption.cs:199-204) or its loop animator (LoopAnimator.cs:78-91). "
            + "Either way the layout re-seated it before this frame rendered.");
    }
}
