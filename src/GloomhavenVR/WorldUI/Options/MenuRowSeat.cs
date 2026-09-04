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
/// <para><b>IT CAME BACK, AND THE ModBuild 409 DIAGNOSIS ABOVE IS AT BEST INCOMPLETE — READ THIS
/// BEFORE RE-DERIVING THE HOVER THEORY.</b> User, 2026-09-04, hardware, screenshot
/// <c>überlagerung.jpg</c>: <i>"Es ist wieder aufgetreten, dass das 'VR Optionen' Menu, über dem
/// 'Optionen' Tab liegt siehe überlagerung.jpg. Das darf nicht sein."</i> The ModBuild 420 log
/// answers it on the instrument's own line, and the answer is not the stale start cache. Three
/// <c>VR MENU ROW</c> lines were written that session and the two healthy ones differ from the
/// broken one in exactly one term:</para>
///
/// <list type="bullet">
/// <item><description>main menu, SEATED: <c>layout VerticalLayoutGroupExtended enabled=True,
/// 10 laid-out row(s) of 11 child(ren)</c> — row at (184,-560), donor at (184,-480), 80 px
/// apart.</description></item>
/// <item><description>pause menu in a SCENARIO, SEATED: <c>layout VerticalLayoutGroupExtended
/// enabled=True, 11 laid-out row(s) of 15 child(ren)</c> — row at (176,-275), donor at (176,-195),
/// 80 px apart.</description></item>
/// <item><description>pause menu on the CAMPAIGN MAP, OVERPRINT: <c>layout NONE, 8 laid-out row(s)
/// of 9 child(ren)</c> — row AND donor both at (0,-195), 0 px apart.</description></item>
/// </list>
///
/// <para>The hover writer was ruled out by the very line that carried the verdict —
/// <c>hover writer: hoverRect is NOT the row rect (hover cannot move the row)</c> — and
/// <c>this show re-based 0 stale start cache(s)</c> says no stale cache was found either.
/// <c>layout NONE</c> explains the 0 px on its own: <b>there is no layout group on that row parent
/// at all</b>, so nothing ever seats a cloned row, and <c>Instantiate</c>'s copy of the donor's
/// <c>anchoredPosition</c> IS the final position. The clone is printed on top of its donor with no
/// writer involved.</para>
///
/// <para><b>WHICH MENU, AND HOW THAT IS KNOWN.</b> The game has two ESC menus,
/// <c>UIMapEscMenu</c> (campaign map) and <c>UIScenarioEscMenu</c> (dungeon), and both are
/// <c>ESCMenu : Singleton&lt;ESCMenu&gt;</c> — one static slot, last <c>Awake</c> wins (the trap
/// documented in <c>OptionsToggle.ResolveMenu</c>, ModBuild 290). The broken line is preceded in
/// the same log by <c>X tap on UIMapEscMenu</c> and by <c>UIWindow SHOWN: 'UI Map Esc Menu'</c>,
/// and the healthy pause line by <c>X tap on UIScenarioEscMenu</c>: the menu with no layout group
/// is the CAMPAIGN MAP's. Their child censuses agree — <c>UIScenarioEscMenu.OnShow</c> switches
/// four rows on and off by save state (<c>quitDungeonButton</c>, <c>skipMissionButton</c>,
/// <c>skipTutorialButton</c>, <c>loadLevelEditorButton</c>), which is why that prefab needs a
/// layout group and has one (11 of 15); <c>UIMapEscMenu</c> toggles only its tutorial row and its
/// divisor and is authored as fixed positions on a grid (8 of 9).</para>
///
/// <para><b>WHO OWNS A ROW'S POSITION, IN EACH CASE. This is the whole contract.</b></para>
/// <list type="bullet">
/// <item><description><b>A layout group exists and is enabled → THE GAME OWNS EVERY ROW.</b>
/// <see cref="Reseat"/> asks it to run again and takes its answer as given. Not one row position is
/// written by this mod on that path, ever — fighting a layout group is a write war and this
/// project does not start one.</description></item>
/// <item><description><b>No layout group → THIS MOD OWNS ITS OWN ROW AND THE ROWS BELOW IT.</b>
/// <see cref="PlaceWithoutLayout"/> measures the column's PITCH from the game's own rows (the
/// distance from the donor to the nearest row below it, both read from their SHIPPED positions),
/// seats the clone one pitch below its donor, and pushes every <c>UIMainMenuOption</c> row that was
/// authored below the donor down by that same pitch — which is what a vertical layout group would
/// have done had the prefab carried one. Nothing else in the parent is touched: a background, a
/// header or a divisor is not a row and is left exactly where the game put it.</description></item>
/// </list>
///
/// <para><b>NOT A PER-FRAME WRITER.</b> Both paths run only from <see cref="Reseat"/> — on the show
/// edge, on a layout-signature change, and when the drift belt finds a row off its seat. The
/// mod-placed path is a FIXED POINT: every target is computed from the row's SHIPPED position plus
/// the measured pitch, never from where the row currently is, so running it again writes the same
/// numbers, and a position is only assigned when it actually differs. The overprint is therefore
/// impossible by construction rather than merely detected: the clone's seat is one pitch below the
/// donor and the pitch has a floor above the OVERPRINT threshold. The verdict stays as the
/// falsifier — and if it ever fires again it is printed at the WARNING tier, so it cannot be lost
/// in a quiet default log the way the ModBuild 331 lines were.</para>
///
/// <para><b>REVERSIBILITY.</b> The game state touched is the two cached numbers on each row
/// (<c>_startAnchoredPosition</c>, <c>hoverDisplacement</c>), both of which the mod's injection
/// had already made wrong, and — on the mod-placed path only — the <c>anchoredPosition</c> of the
/// rows pushed down to open the slot. The shipped value of each is kept the first time it is
/// touched, and <see cref="Release"/> — run before a clone is destroyed — puts them all back and
/// rebuilds the layout without the clone, so the game's rows are left exactly as they shipped even
/// when the menu is closed at teardown.</para>
/// </summary>
internal static class MenuRowSeat
{
    /// <summary>Two rows closer than this fraction of a row height are OVERPRINTED.</summary>
    private const float OverprintFraction = 0.5f;

    /// <summary>Drift smaller than this (px, squared) is float noise, not a writer.</summary>
    private const float DriftEpsilonSq = 0.25f;

    /// <summary>
    /// The pitch of last resort for the mod-placed path, used only when the parent holds no other
    /// row to measure against. 80 px is what BOTH layout-driven menus produced in the ModBuild 420
    /// log (main menu -480 → -560, scenario pause menu -195 → -275), so it is the game's own row
    /// pitch rather than a number chosen here.
    /// </summary>
    private const float FallbackPitch = 80f;

    /// <summary>
    /// A measured pitch is raised to this fraction of a row height before it is used. The OVERPRINT
    /// verdict fires at <see cref="OverprintFraction"/>, so a floor above it makes the mod-placed
    /// seat unable to be an overprint BY CONSTRUCTION — the verdict can then only ever accuse a
    /// path that is not this one.
    /// </summary>
    private const float MinPitchFraction = OverprintFraction + 0.1f;

    /// <summary>Dead keys are pruned from <see cref="Displaced"/> once it holds more entries than
    /// any real menu has rows — a scene change destroys a menu's rows without telling us.</summary>
    private const int DisplacedPruneAt = 32;

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

        /// <summary>Which placement rule seated the rows on the last <see cref="Reseat"/> — the
        /// game's layout group, or the mod. Named on the instrument line.</summary>
        public string Placement = "not yet placed";

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
            Placement = "not yet placed";
        }
    }

    private static readonly Seat PauseSeat = new("pause");
    private static readonly Seat MainSeat = new("main");

    /// <summary>Each re-based row's caches as the game left them, kept for <see cref="Release"/>.</summary>
    private static readonly Dictionary<UIMenuOptionToggle, (Vector2 start, Vector2 displacement)> Originals = new();

    /// <summary>
    /// THE SHIPPED POSITION of every row the mod-placed path has pushed down to open a slot —
    /// recorded the first time that row is moved and never re-read from the row afterwards. It is
    /// what makes the placement a fixed point (every target is <c>shipped - pitch</c>, so running
    /// the pass again writes the same number) and what <see cref="Release"/> puts back. Survives a
    /// hide, because after one show the rows no longer stand where the game left them and a
    /// re-read would take the mod's own answer for the shipped one.
    /// </summary>
    private static readonly Dictionary<RectTransform, Vector2> Displaced = new();

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
            Reseat(seat, parent, rowRect, donorRect);
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
            Reseat(seat, parent, rowRect, donorRect);
            relaid = true;
            LogDrift(seat, drifted, driftedWriter, driftedFrom, driftedSeat);
        }

        if (relaid)
            RestoreHoverNudge(seat);

        if (showEdge)
            LogShow(seat, rowRect, donorRect);
    }

    /// <summary>
    /// Before a clone is destroyed: give every re-based row its shipped caches back, put every row
    /// the mod pushed down back where the game authored it, take the clone out of the layout, and
    /// rebuild so the game's rows return to their own slots.
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

        // The mod-placed path is the only writer of these, and this is its undo: the menu with no
        // layout group has nobody else to put its rows back.
        foreach (KeyValuePair<RectTransform, Vector2> kv in Displaced)
        {
            RectTransform rect = kv.Key;
            if (rect == null)
                continue;
            rect.anchoredPosition = kv.Value;
        }
        Displaced.Clear();

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
    /// Seat the rows NOW — by the game's layout group where there is one, by
    /// <see cref="PlaceWithoutLayout"/> where there is not — then record every laid-out row's seat
    /// and re-base the hover caches of every row whose cache no longer names its seat.
    /// </summary>
    private static void Reseat(Seat seat, RectTransform parent, RectTransform row, RectTransform donor)
    {
        if (seat.Layout != null && seat.Layout.enabled)
        {
            // OWNER: THE GAME. Its layout group is asked to run again and its answer is taken as
            // given; no row position is written from this branch. That is deliberate — writing a
            // position a layout group owns is a write war, and the seat cache below is only ever
            // used to DETECT drift, never as the value written.
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
            seat.Placement = $"the game's {seat.Layout.GetType().Name} laid the rows out";
        }
        else
        {
            // OWNER: THIS MOD. Nothing lays this parent out, so the clone would keep the donor's
            // anchored position forever — the ModBuild 420 'layout NONE' overprint on the campaign
            // map's UIMapEscMenu. The mod does the layout group's job for its own row and for the
            // rows it has to push down to make room, and for nothing else.
            PlaceWithoutLayout(seat, parent, row, donor);
        }
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

    /// <summary>
    /// THE MENU WITH NO LAYOUT GROUP: place the mod's row from the donor's rect and the pitch the
    /// column's own rows already carry, and push the rows below the donor down by that same pitch.
    ///
    /// <para>Every target is computed from a row's SHIPPED position (<see cref="Shipped"/>) plus
    /// the pitch, never from where the row currently stands, so the pass is a fixed point: run it
    /// a hundred times and it writes the same two numbers once. A row is assigned only when its
    /// current position actually differs, so this is not a per-frame writer even on the frames the
    /// drift belt calls it.</para>
    ///
    /// <para>ONLY <c>UIMainMenuOption</c> ROWS ARE MOVED. A parent with no layout group holds
    /// whatever the prefab author put there — <c>UIMapEscMenu</c> alone carries a <c>_divisor</c>
    /// object beside its rows — and shoving a background or a header down by a row pitch because
    /// it happens to sit low in the panel would be a far worse defect than the one being fixed.
    /// The count of rows actually held is on the instrument line, so a hardware round can see
    /// whether something was left behind.</para>
    /// </summary>
    private static void PlaceWithoutLayout(Seat seat, RectTransform parent, RectTransform row,
                                           RectTransform donor)
    {
        PruneDisplaced();

        float pitch = MeasurePitch(parent, row, donor);
        Vector2 donorSeat = Shipped(donor);
        float slotY = donorSeat.y - pitch;

        // The rows authored below the new slot, top-to-bottom by SHIPPED position. Insertion sort:
        // a menu has under twenty rows and this runs on a show edge, not a frame.
        var below = new List<(RectTransform Rect, Vector2 Shipped)>(8);
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null || ReferenceEquals(child, row) || ReferenceEquals(child, donor))
                continue;
            if (!child.gameObject.activeSelf || IgnoresLayout(child) || !IsRow(child))
                continue;
            Vector2 shipped = Shipped(child);
            if (shipped.y > slotY + 0.5f)
                continue;   // authored above the new slot: the game's own placement stands.
            int at = below.Count;
            while (at > 0 && below[at - 1].Shipped.y < shipped.y)
                at--;
            below.Insert(at, (child, shipped));
        }

        // ONLY AS FAR AS IT HAS TO GO. Each row keeps its shipped position unless that would put
        // it within a pitch of the row above it, so an authored gap further down the column is
        // absorbed instead of being dragged along. Every target is a function of shipped positions
        // and the pitch alone, which is what makes running this again write the same numbers.
        int pushed = 0;
        float lastY = slotY;
        for (int i = 0; i < below.Count; i++)
        {
            (RectTransform rect, Vector2 shipped) = below[i];
            float wantY = Mathf.Min(shipped.y, lastY - pitch);
            lastY = wantY;
            var want = new Vector2(shipped.x, wantY);
            if (Mathf.Abs(wantY - shipped.y) > 0.5f)
            {
                if (!Displaced.ContainsKey(rect))
                    Displaced[rect] = shipped;
                pushed++;
            }
            if ((rect.anchoredPosition - want).sqrMagnitude > DriftEpsilonSq)
                rect.anchoredPosition = want;
        }

        var seated = new Vector2(donorSeat.x, slotY);
        if ((row.anchoredPosition - seated).sqrMagnitude > DriftEpsilonSq)
            row.anchoredPosition = seated;

        seat.Placement = $"the MOD placed the row (no layout group on '{parent.name}'), one pitch "
            + $"of {pitch:F0} px below the donor, pushing {pushed} of {below.Count} game row(s) "
            + "below it down";
    }

    /// <summary>
    /// THE COLUMN'S ROW PITCH, read off the game's own rows: the SMALLEST vertical gap between any
    /// two of them, which for a column of equal rows is the gap between neighbours and is not
    /// fooled by a deliberate space further down. The mod's own row is excluded — it is the thing
    /// being placed — and every position is the SHIPPED one, so a column this pass has already
    /// opened still reports the authored pitch rather than the pitch plus itself.
    ///
    /// <para>Floored at <see cref="MinPitchFraction"/> of a row height. That floor is what makes
    /// the seat unable to be judged an OVERPRINT by construction: the verdict fires below
    /// <see cref="OverprintFraction"/> of a row height and the seat is always at least a tenth of
    /// a row height further away than that.</para>
    /// </summary>
    private static float MeasurePitch(RectTransform parent, RectTransform row, RectTransform donor)
    {
        var ys = new List<float>(8);
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null || ReferenceEquals(child, row))
                continue;
            if (!child.gameObject.activeSelf || IgnoresLayout(child) || !IsRow(child))
                continue;
            ys.Add(Shipped(child).y);
        }

        float pitch = float.PositiveInfinity;
        for (int a = 0; a < ys.Count; a++)
            for (int b = a + 1; b < ys.Count; b++)
            {
                float gap = Mathf.Abs(ys[a] - ys[b]);
                if (gap > 0.5f && gap < pitch)
                    pitch = gap;
            }
        if (float.IsPositiveInfinity(pitch))
            pitch = 0f;   // the donor is the only row in the column; the floor below decides.

        float height = Mathf.Max(donor.rect.height, row.rect.height);
        if (height <= 1f)
            height = FallbackPitch;
        if (pitch < height * MinPitchFraction)
            pitch = height;
        return pitch;
    }

    /// <summary>Where the game shipped this row — its recorded position once the mod has moved
    /// it, its current one until then.</summary>
    private static Vector2 Shipped(RectTransform rect)
        => Displaced.TryGetValue(rect, out Vector2 shipped) ? shipped : rect.anchoredPosition;

    /// <summary>A menu ROW, as opposed to whatever else the prefab put under the same parent.</summary>
    private static bool IsRow(RectTransform child) => child.GetComponent<UIMainMenuOption>() != null;

    /// <summary>A scene change destroys a menu's rows without telling us; drop the dead keys once
    /// the table is bigger than any real menu.</summary>
    private static void PruneDisplaced()
    {
        if (Displaced.Count <= DisplacedPruneAt)
            return;
        var live = new List<KeyValuePair<RectTransform, Vector2>>(Displaced.Count);
        foreach (KeyValuePair<RectTransform, Vector2> kv in Displaced)
            if (kv.Key != null)
                live.Add(kv);
        if (live.Count == Displaced.Count)
            return;
        Displaced.Clear();
        for (int i = 0; i < live.Count; i++)
            Displaced[live[i].Key] = live[i].Value;
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
            + $"|{seat.Rows.Count}|{layout}|{seat.Placement}";
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
        // The wording up to and including the ModBuild 409 sentence is APPENDED TO, never reworded:
        // a surface checker counts the tokens on this line and a past round's grep must keep
        // matching. Everything new goes after it — including the correction to it.
        string line =
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
            + "for any writer that still moves a row. "
            + $"PLACEMENT: {seat.Placement}; the resulting gap is {gap:F0} px. "
            + "CORRECTION (ModBuild 422) to the stale-cache sentence above, which is ModBuild 409's "
            + "theory and is at best incomplete — do not re-derive it. The user reported the overprint "
            + "again on 2026-09-04 (\"Es ist wieder aufgetreten, dass das 'VR Optionen' Menu, über dem "
            + "'Optionen' Tab liegt siehe überlagerung.jpg. Das darf nicht sein.\"), and on the ModBuild "
            + "420 line that produced that screenshot the hover writer was ruled out by this very line "
            + "('hoverRect is NOT the row rect') and 0 stale caches were found. The cause was 'layout "
            + "NONE': the campaign map's UIMapEscMenu has NO layout group on the row parent, so nothing "
            + "seated the clone and it kept its donor's anchored position exactly, 0 px away. Read the "
            + "PLACEMENT clause to see which rule ran; where it says the MOD placed the row, this mod "
            + "owns that row's position and the positions of the rows below it, and nothing else's.";

        if (overprint)
        {
            // HW-VERIFY
            VRLog.Alert("WorldUI", line);
        }
        else
        {
            // HW-VERIFY
            VRLog.Note("WorldUI", line);
        }
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
