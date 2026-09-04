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
/// <item><description><b>No layout group → THIS MOD OWNS ITS OWN ROW AND EVERY COLUMN MEMBER
/// BELOW IT.</b> <see cref="PlaceWithoutLayout"/> measures the column's PITCH from the game's own
/// rows (the SMALLEST gap between any two of them, read from their SHIPPED positions), seats the
/// clone one pitch below its donor, and shifts the WHOLE BLOCK of members authored below that slot
/// down by the same pitch — which is what a vertical layout group would have done had the prefab
/// carried one. Nothing above the donor is touched, and nothing that is not row-shaped is touched:
/// a full-panel background or frame is not a member and is left exactly where the game put
/// it.</description></item>
/// </list>
///
/// <para><b>IT HALF-WORKED, AND ModBuild 423 IS THE BUILD THAT SAYS WHY — READ THIS BEFORE
/// TOUCHING THE PLACEMENT.</b> ModBuild 422 shipped the paragraph above with a CASCADE instead of a
/// block shift: each row went to <c>min(shipped, lastY - pitch)</c>, "only as far as it has to go",
/// so an authored gap further down was absorbed rather than dragged along. The top of the map menu
/// came out right and the bottom did not. User, 2026-09-04, <c>menu_kaputt.jpg</c>: <i>"Das Menu
/// ist nun immer dauerhaft mit einer anderen Überlagerung drin 'Hauptmenu' und 'Schließen'
/// überlagert sich jetzt, siehe menu_kaputt.jpg, fix das."</i></para>
///
/// <para><b>THE ARITHMETIC, against the campaign map's authored column</b> (measured off the two
/// screenshots by fitting the panel's projection to the four evenly-spaced top rows, sub-pixel
/// residuals, row height 72.5): Fortfahren −35, Mehrspieler −115, <b>Optionen −195</b> (the donor,
/// confirmed by the log), Spielanleitung −275, then a deliberate 125 px space to Hauptmenü −400,
/// then Spiel verlassen −480. The pitch is 80 and the mod's slot is −275. The cascade should then
/// have produced −355 / −435 / −515, moving all three. <b>The ModBuild 422 log instead says
/// <c>pushing 2 of 3 game row(s) below it down</c></b>, and the photograph shows Spielanleitung
/// alone at −355 with Hauptmenü and Spiel verlassen roughly 13 px apart near −470. Two rows below
/// the slot did not end up where the cascade puts them, so the three rows the mod counted are NOT
/// the three rows the eye sees: at least one visible row was outside the mod's set, and the mod
/// pushed another row down onto it. The 422 membership test is the only thing that can exclude a
/// visible row — <c>GetComponent&lt;UIMainMenuOption&gt;() != null</c> plus a skip on
/// <c>LayoutElement.ignoreLayout</c> — and it is now gone (<see cref="IsColumnMember"/>).</para>
///
/// <para><b>THE LESSON, WHICH IS THE GENERAL ONE.</b> A cascade's clearance guarantee is only over
/// the rows in its own list; a row missing from the list is invisible to it and gets landed on. A
/// RIGID BLOCK SHIFT has no list to fall out of — every member moves by the same number, so every
/// gap between them is the gap the game authored — and it needed no cleverness to begin with. The
/// proof is written out in <see cref="PlaceWithoutLayout"/>.</para>
///
/// <para><b>AND WHERE THE GAME ITSELF AUTHORED AN OVERLAP, LEGIBILITY WINS.</b> A rigid shift keeps
/// the author's spacing exactly, which is right until the author's spacing is itself unreadable. So
/// the plan is MEASURED BEFORE IT IS WRITTEN — over the donor, the clone and everything below them,
/// the part the mod is answerable for — and if any pair of rects would still overlap, the column
/// below the donor is re-flowed onto one even grid whose step clears every pair on it. The menu
/// never holds an intermediate arrangement, because nothing is written until the plan is chosen.
/// Two plans, one of them provably clear, and the trade between them is the user's own: <i>"Das
/// darf nicht sein."</i></para>
///
/// <para><b>NOT A PER-FRAME WRITER.</b> Both paths run only from <see cref="Reseat"/> — on the show
/// edge, on a layout-signature change, and when the drift belt finds a row off its seat. The
/// mod-placed path is a FIXED POINT: every target, and the choice between the two plans, is a
/// function of the rows' SHIPPED positions and the measured pitch, never of where a row currently
/// is, so running it again writes the same numbers, and a position is only assigned when it
/// actually differs. The overprint is therefore impossible by construction rather than merely
/// detected: the clone's seat is one pitch below the donor, the pitch has a floor of a WHOLE row
/// height (a clone is a full-height row and needs a full row height of clearance), and every other
/// gap in the finished column is one the game authored — or, if that was not readable, one even
/// step that clears every pair.</para>
///
/// <para><b>AND THE FALSIFIER NOW LOOKS AT THE WHOLE MENU.</b> Until ModBuild 423 the OVERPRINT
/// verdict compared the clone with its donor and nothing else, which is why 422 printed
/// <c>VERDICT SEATED</c> over a menu whose last two rows were on top of each other — the instrument
/// answered a question adjacent to the one asked. <see cref="TightestGap"/> now measures every
/// consecutive pair of column members in the FINISHED menu and the verdict fires on the closest
/// one, wherever it is, against the clearance those two rects actually need (<c>(h1 + h2) / 2</c>)
/// rather than against a fraction of a nominal row height — the shipped defect left two rows 45 px
/// apart with 72 px rects, an unmistakable overlap that "half a row height" would still have called
/// clear;
/// <see cref="RowCensus"/> prints every child of the row parent with its shipped and final position
/// and whether it counts as a member, so the next round is decided by a grep rather than by
/// measuring a photograph. If either verdict fires it prints at the WARNING tier, so it cannot be
/// lost in a quiet default log the way the ModBuild 331 lines were.</para>
///
/// <para><b>REVERSIBILITY.</b> The game state touched is the two cached numbers on each row
/// (<c>_startAnchoredPosition</c>, <c>hoverDisplacement</c>), both of which the mod's injection
/// had already made wrong, and — on the mod-placed path only — the <c>anchoredPosition</c> of the
/// column members shifted down to open the slot. The shipped value of each is kept the first time it is
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
    /// A measured pitch is raised to this many row heights before it is used. ONE whole row height,
    /// not a fraction of one: the clone is a full-height row and needs a full row height below its
    /// donor or the two rects overlap. A measured pitch under that can only come from a pair the
    /// GAME authored tight, and taking that as the block's step would hand the mod's own row the
    /// same overlap. Comfortably above <see cref="OverprintFraction"/>, so the mod-placed seat can
    /// never be an overprint BY CONSTRUCTION.
    /// </summary>
    private const float MinPitchFraction = 1f;

    /// <summary>Dead keys are pruned from <see cref="Displaced"/> once it holds more entries than
    /// any real menu has rows — a scene change destroys a menu's rows without telling us.</summary>
    private const int DisplacedPruneAt = 32;

    /// <summary>A child taller than this many donor heights is a background or a frame, not a row
    /// (<see cref="IsColumnMember"/>). One and a half leaves room for a two-line caption.</summary>
    private const float MemberMaxHeightFactor = 1.5f;

    /// <summary>...and one thinner than this is a hairline or a zero-height anchor object. A
    /// separator bar clears it, and travels with the block as it should.</summary>
    private const float MemberMinHeightFactor = 0.08f;

    /// <summary>
    /// A member must be at least this tall to have a vote on the PITCH. A separator has to travel
    /// with the block, but it must not SET the block's step: a 40 px divider authored inside a
    /// deliberate gap would otherwise make the pitch 40 and seat the mod's row half-way inside its
    /// own donor. Measured on the rows, applied to everything.
    /// </summary>
    private const float PitchRowMinHeightFactor = 0.5f;

    /// <summary>How many children the row census prints before it says "+N more".</summary>
    private const int CensusCap = 14;

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
    /// THE MENU WITH NO LAYOUT GROUP: seat the mod's row one pitch below its donor and move the
    /// WHOLE BLOCK of column members below it down by that same pitch. Rigid — every gap the game
    /// authored inside that block survives untouched, and the only new adjacency is the donor's.
    ///
    /// <para><b>WHY RIGID, AND WHY THE ModBuild 422 CASCADE WAS WRONG.</b> 422 moved each row
    /// "only as far as it has to go" (<c>min(shipped, lastY - pitch)</c>), which let a row keep its
    /// shipped position while the row above it was pushed down. That guarantee only ever covered
    /// rows in the mod's OWN list: a sibling the membership test excluded is not in the
    /// <c>lastY</c> chain at all, so the mod pushed a member down onto it. The 422 log said so in
    /// its own words — <c>pushing 2 of 3 game row(s) below it down</c> — while the eye counts three
    /// rows below the slot and only one of them (Spielanleitung) landed where the mod intended. A
    /// rigid shift has no chain to fall out of: every member moves by the same number, so every gap
    /// between members is the gap the game authored.</para>
    ///
    /// <para><b>THE PROOF.</b> Let <c>p</c> be the pitch (<see cref="MeasurePitch"/>) and <c>D</c>
    /// the donor's position. The members split AT THE DONOR — not at the clone's slot, which is
    /// what makes the proof unconditional: everything at or above <c>D</c> is untouched, everything
    /// strictly below <c>D</c> moves to <c>shipped - p</c>, and the clone takes <c>D - p</c>. In
    /// the finished column, reading downwards:</para>
    /// <list type="number">
    /// <item><description>Everything above the donor is exactly where the game put
    /// it.</description></item>
    /// <item><description>donor to clone is exactly <c>p</c>, which is floored at
    /// <see cref="MinPitchFraction"/> of a row height — above the
    /// <see cref="OverprintFraction"/> the OVERPRINT verdict fires at.</description></item>
    /// <item><description>clone to the topmost shifted member <c>b</c> is
    /// <c>(D - p) - (b - p) = D - b</c> — the gap the game authored between the donor and that
    /// member, neither widened nor narrowed.</description></item>
    /// <item><description>member to member inside the block is <c>(b1 - p) - (b2 - p)</c> — again
    /// the authored gap, unchanged.</description></item>
    /// </list>
    /// <para>Every gap in the result is therefore either <c>p</c> or a gap the game itself
    /// authored: <b>the mod cannot put two rows closer together than the game already had
    /// them</b>, whatever the pitch came out as and whatever order the children are in. That is the
    /// property the user is owed after two overprints in two builds, and it is a property of the
    /// rule rather than of a measurement taken afterwards. (Splitting at the SLOT instead would
    /// need the extra premise that no member lies between <c>D</c> and <c>D - p</c>, which the
    /// pitch floor can break; splitting at the donor needs no premise at all.)</para>
    ///
    /// <para>Every target is computed from a row's SHIPPED position (<see cref="Shipped"/>) plus
    /// the pitch, never from where the row currently stands, so the pass is a fixed point: run it
    /// a hundred times and it writes the same numbers once. A row is assigned only when its current
    /// position actually differs, so this is not a per-frame writer even on the frames the drift
    /// belt calls it.</para>
    ///
    /// <para><b>MEMBERSHIP IS BY SHAPE, NOT BY COMPONENT</b> — see <see cref="IsColumnMember"/> for
    /// why the 422 <c>GetComponent&lt;UIMainMenuOption&gt;</c> test is the suspect for the row that
    /// escaped, and why nothing row-shaped can escape the new one.</para>
    /// </summary>
    private static void PlaceWithoutLayout(Seat seat, RectTransform parent, RectTransform row,
                                           RectTransform donor)
    {
        PruneDisplaced();

        float donorHeight = DonorHeight(donor, row);
        float pitch = MeasurePitch(parent, row, donor, donorHeight);
        Vector2 donorSeat = Shipped(donor);

        // THE SPLIT IS AT THE DONOR, not at the clone's slot — see the proof above. Everything the
        // game authored at or above the donor keeps the game's own placement and is collected only
        // so the plan can be judged against it.
        int above = 0;
        var below = new List<Plan>(8);
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null || ReferenceEquals(child, row))
                continue;
            if (!IsColumnMember(child, donorHeight))
                continue;
            Vector2 shipped = Shipped(child);
            if (shipped.y >= donorSeat.y - 0.5f)
                above++;
            else
                Insert(below, new Plan(child, shipped, child.rect.height, shipped.y));
        }

        // PLAN A, THE RIGID BLOCK SHIFT: one number for all of them, so no gap between them can
        // change. Nothing is written yet — the plan is judged first.
        for (int i = 0; i < below.Count; i++)
            below[i] = below[i].At(below[i].Shipped.y - pitch);
        var clone = new Plan(row, row.anchoredPosition, row.rect.height, donorSeat.y - pitch);
        var donorPlan = new Plan(donor, donorSeat, donorHeight, donorSeat.y);
        string how = $"the MOD placed the row (no layout group on '{parent.name}'), one pitch of "
            + $"{pitch:F0} px below the donor, and shifted the whole block of {below.Count} of "
            + $"{above + below.Count} column member(s) below it down by that same pitch "
            + "(RIGID: every gap the game authored inside the block is unchanged)";

        // THE REMEDY, and the reason the user's word is the last one. A rigid shift cannot CREATE
        // an overlap, but it faithfully preserves one the GAME authored — and a menu the player
        // cannot read is a menu the player cannot read, whoever authored it. So the plan is
        // measured before it is written, and if any pair still overlaps the authored spacing is
        // given up and the column below the donor goes onto one even grid. Legibility wins over
        // the author's spacing; that is the user's own priority, not a taste of mine.
        float slack = WorstSlack(donorPlan, clone, below, out string worst, out float had, out float need);
        if (slack < 0f)
        {
            float step = Mathf.Max(pitch, (donorHeight + clone.Height) * 0.5f);
            float previous = clone.Height;
            for (int i = 0; i < below.Count; i++)
            {
                step = Mathf.Max(step, (previous + below[i].Height) * 0.5f);
                previous = below[i].Height;
            }
            clone = clone.At(donorSeat.y - step);
            for (int i = 0; i < below.Count; i++)
                below[i] = below[i].At(donorSeat.y - step * (i + 2));
            how += $"; then RE-FLOWED that column onto an even grid of {step:F0} px, because the "
                + $"rigid plan left {worst} only {had:F0} px apart where their rects need "
                + $"{need:F0} — a spacing the GAME authored, kept by a rigid shift and given up "
                + "here because legibility wins over it";
        }

        // ...and only now is anything written.
        for (int i = 0; i < below.Count; i++)
        {
            Plan p = below[i];
            var want = new Vector2(p.Shipped.x, p.Y);
            if (!Displaced.ContainsKey(p.Rect))
                Displaced[p.Rect] = p.Shipped;
            if ((p.Rect.anchoredPosition - want).sqrMagnitude > DriftEpsilonSq)
                p.Rect.anchoredPosition = want;
        }
        var seated = new Vector2(donorSeat.x, clone.Y);
        if ((row.anchoredPosition - seated).sqrMagnitude > DriftEpsilonSq)
            row.anchoredPosition = seated;

        seat.Placement = how;
    }

    /// <summary>One column member and where this pass intends to put it. Positions are decided
    /// entirely in these before a single <c>anchoredPosition</c> is written, so the menu never
    /// holds an intermediate arrangement and the rule that ran is decided once.</summary>
    private readonly struct Plan
    {
        public readonly RectTransform Rect;
        public readonly Vector2 Shipped;
        public readonly float Height;
        public readonly float Y;

        public Plan(RectTransform rect, Vector2 shipped, float height, float y)
        {
            Rect = rect; Shipped = shipped; Height = height; Y = y;
        }

        public Plan At(float y) => new Plan(Rect, Shipped, Height, y);
    }

    /// <summary>Insertion sort by SHIPPED position, top of the column first. A menu has under
    /// twenty rows and this runs on a show edge, not on a frame.</summary>
    private static void Insert(List<Plan> sorted, Plan entry)
    {
        int at = sorted.Count;
        while (at > 0 && sorted[at - 1].Shipped.y < entry.Shipped.y)
            at--;
        sorted.Insert(at, entry);
    }

    /// <summary>
    /// How much room the tightest pair in a PLANNED column has to spare. Negative means those two
    /// rects overlap — which is the only question worth asking, and a physical one: a pair whose
    /// heights are <c>h1</c> and <c>h2</c> needs <c>(h1 + h2) / 2</c> between their positions.
    ///
    /// <para>The part of the column judged here is exactly the part the mod is ANSWERABLE for —
    /// the donor, the clone and everything below them. Rows above the donor are never moved by
    /// either plan, so letting one of them decide between the plans would trade away the author's
    /// spacing lower down to fix something no plan can reach. The instrument's own
    /// <see cref="TightestGap"/> is deliberately wider than this: it measures the finished menu end
    /// to end, so a pair the game authored on top of itself is still reported.</para>
    /// </summary>
    private static float WorstSlack(Plan donor, Plan clone, List<Plan> below,
                                    out string pair, out float had, out float need)
    {
        var all = new List<Plan>(below.Count + 2);
        all.Add(donor);
        all.Add(clone);
        all.AddRange(below);
        all.Sort((a, b) => b.Y.CompareTo(a.Y));

        float worst = float.MaxValue;
        pair = "fewer than two column members — nothing to compare";
        had = 0f;
        need = 0f;
        for (int i = 1; i < all.Count; i++)
        {
            float gap = all[i - 1].Y - all[i].Y;
            float wants = (all[i - 1].Height + all[i].Height) * 0.5f;
            if (gap - wants >= worst)
                continue;
            worst = gap - wants;
            had = gap;
            need = wants;
            pair = $"'{Short(all[i - 1].Rect.name)}' at y={all[i - 1].Y:F0} and "
                 + $"'{Short(all[i].Rect.name)}' at y={all[i].Y:F0}";
        }
        return worst == float.MaxValue ? 0f : worst;
    }

    /// <summary>
    /// THE COLUMN'S ROW PITCH, read off the game's own ROWS: the SMALLEST vertical gap between any
    /// two FULL-HEIGHT members, which for a column of equal rows is the gap between neighbours and
    /// is not inflated by a deliberate space further down. The mod's own row is excluded — it is
    /// the thing being placed — and every position is the SHIPPED one, so a column this pass has
    /// already opened still reports the authored pitch rather than the pitch plus itself.
    ///
    /// <para>Thin members are deliberately not consulted (<see cref="PitchRowMinHeightFactor"/>):
    /// a divider authored inside a gap must travel with the block but must not set its step.</para>
    ///
    /// <para>Floored at <see cref="MinPitchFraction"/> of a row height. That floor is what makes
    /// the seat unable to be judged an OVERPRINT by construction: the verdict fires below
    /// <see cref="OverprintFraction"/> of a row height and the seat is always at least a tenth of
    /// a row height further away than that.</para>
    /// </summary>
    private static float MeasurePitch(RectTransform parent, RectTransform row, RectTransform donor,
                                      float donorHeight)
    {
        var ys = new List<float>(8);
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null || ReferenceEquals(child, row))
                continue;
            if (!IsColumnMember(child, donorHeight)
                || child.rect.height < donorHeight * PitchRowMinHeightFactor)
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

        if (pitch < donorHeight * MinPitchFraction)
            pitch = donorHeight * MinPitchFraction;
        return pitch;
    }

    /// <summary>Where the game shipped this row — its recorded position once the mod has moved
    /// it, its current one until then.</summary>
    private static Vector2 Shipped(RectTransform rect)
        => Displaced.TryGetValue(rect, out Vector2 shipped) ? shipped : rect.anchoredPosition;

    /// <summary>The row height every measurement here is scaled against, with a floor so a rect
    /// that has not been laid out yet cannot make the pitch collapse.</summary>
    private static float DonorHeight(RectTransform donor, RectTransform row)
    {
        float h = Mathf.Max(donor.rect.height, row.rect.height);
        return h > 1f ? h : FallbackPitch;
    }

    /// <summary>
    /// A MEMBER OF THE COLUMN — something row-shaped the mod must keep clear of, judged BY SHAPE
    /// rather than by component type.
    ///
    /// <para><b>This is where ModBuild 422 went wrong and it is worth the paragraph.</b> 422 asked
    /// <c>GetComponent&lt;UIMainMenuOption&gt;() != null</c> and additionally skipped anything
    /// carrying a <c>LayoutElement</c> with <c>ignoreLayout</c>. Both are ways for a VISIBLE row to
    /// be INVISIBLE to the placement — and a row the mod cannot see is a row it will happily shove
    /// another row on top of. That is exactly what the user photographed in
    /// <c>menu_kaputt.jpg</c>.</para>
    ///
    /// <para>So the test is now geometric and total: any active child whose height lies between
    /// <see cref="MemberMinHeightFactor"/> and <see cref="MemberMaxHeightFactor"/> of the donor's
    /// is a member. That takes in every row whatever component it carries, and a separator or
    /// divider too — which SHOULD travel with the block rather than be left behind by it. It leaves
    /// out only what cannot be a row: a full-panel background or frame, which is far taller, and a
    /// zero-height anchor object. An <c>ignoreLayout</c> flag is not consulted, because on a parent
    /// with no layout group there is no layout to ignore.</para>
    /// </summary>
    private static bool IsColumnMember(RectTransform child, float donorHeight)
    {
        if (!child.gameObject.activeSelf)
            return false;
        float h = child.rect.height;
        return h >= donorHeight * MemberMinHeightFactor && h <= donorHeight * MemberMaxHeightFactor;
    }

    /// <summary>
    /// THE FALSIFIER'S EYES: the most OVERLAPPING pair of column members anywhere in the finished
    /// menu, not just the mod's row against its donor. ModBuild 422 shipped an overprint that its
    /// own verdict called SEATED, because the verdict only ever looked at those two rows; widening
    /// it is the more important half of this fix.
    ///
    /// <para>The test is physical rather than a fraction of a nominal row height: two rows collide
    /// when their rects overlap, which for a pair whose heights are <c>h1</c> and <c>h2</c> happens
    /// below a gap of <c>(h1 + h2) / 2</c>. That matters — the shipped defect left two rows 45 px
    /// apart with 72 px rects, an unmistakable overlap that "half a row height" (36 px) would still
    /// have called clear. The clearance a pair NEEDS is returned alongside the gap it HAS, so the
    /// log states both and the reader never has to trust a bare threshold.</para>
    /// </summary>
    private static float TightestGap(RectTransform parent, RectTransform row, float donorHeight,
                                     out float needed, out string pair)
    {
        var found = new List<(float Y, float H, string Name)>(12);
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null || !IsColumnMember(child, donorHeight))
                continue;
            found.Add((child.anchoredPosition.y, child.rect.height, child.name));
        }
        if (!ReferenceEquals(row.parent, parent))
            found.Add((row.anchoredPosition.y, row.rect.height, row.name));

        found.Sort((a, b) => b.Y.CompareTo(a.Y));
        float gapOfWorst = float.MaxValue;
        float worstSlack = float.MaxValue;
        needed = 0f;
        pair = "fewer than two column members — nothing to compare";
        for (int i = 1; i < found.Count; i++)
        {
            float gap = found[i - 1].Y - found[i].Y;
            float need = (found[i - 1].H + found[i].H) * 0.5f;
            float slack = gap - need;
            if (slack >= worstSlack)
                continue;
            worstSlack = slack;
            gapOfWorst = gap;
            needed = need;
            pair = $"'{Short(found[i - 1].Name)}' at y={found[i - 1].Y:F0} and "
                 + $"'{Short(found[i].Name)}' at y={found[i].Y:F0}";
        }
        return gapOfWorst;
    }

    /// <summary>Every child of the row parent with its shipped and its final position — the clause
    /// that decides the next hardware round without anyone having to measure a photograph.</summary>
    private static string RowCensus(RectTransform parent, float donorHeight)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i) as RectTransform;
            if (child == null)
                continue;
            if (i >= CensusCap)
            {
                sb.Append(", +").Append(parent.childCount - i).Append(" more");
                break;
            }
            if (i > 0)
                sb.Append(", ");
            Vector2 now = child.anchoredPosition;
            Vector2 was = Shipped(child);
            string moved = Mathf.Abs(now.y - was.y) > 0.5f
                ? $"y={was.y:F0}->{now.y:F0}" : $"y={now.y:F0}";
            string kind = !child.gameObject.activeSelf ? "off"
                : IsColumnMember(child, donorHeight) ? "member"
                : $"NOT A MEMBER (h={child.rect.height:F0})";
            sb.Append($"'{Short(child.name)}' {moved} {kind}");
        }
        return sb.ToString();
    }

    private static string Short(string name)
        => name.StartsWith("GloomhavenVR.", StringComparison.Ordinal) ? name.Substring(13) : name;

    /// <summary>A gap of <see cref="float.MaxValue"/> means there was no pair to measure; printing
    /// it as a number would read as a real measurement of an absurd column.</summary>
    private static string TightestText(float gap)
        => gap >= float.MaxValue ? "no measurable gap" : $"{gap:F0} px";

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
        // THE WIDENED FALSIFIER. ModBuild 422's verdict compared the clone with its donor and
        // nothing else, so it printed SEATED over a menu whose last two rows were on top of each
        // other. This looks at every consecutive pair of column members in the finished menu.
        float donorHeight = DonorHeight(donor, row);
        string tightestPair = "the row parent is gone";
        float tightest = float.MaxValue;
        float tightestNeeds = 0f;
        if (seat.Parent != null)
            tightest = TightestGap(seat.Parent, row, donorHeight, out tightestNeeds, out tightestPair);
        bool tightOverprint = tightest < tightestNeeds;

        string signature = $"{seat.Menu}|{overprint}|{arrivedOverprinted}|{row.GetSiblingIndex()}|{donor.GetSiblingIndex()}"
            + $"|{Mathf.RoundToInt(row.anchoredPosition.y)}|{Mathf.RoundToInt(donor.anchoredPosition.y)}"
            // Clamped: with fewer than two members the gap is float.MaxValue, and rounding that to
            // an int is undefined — a signature term must never be a number nobody can read.
            + $"|{seat.Rows.Count}|{layout}|{seat.Placement}|{tightOverprint}"
            + $"|{Mathf.RoundToInt(Mathf.Min(tightest, 99999f))}";
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
            + "owns that row's position and the positions of the rows below it, and nothing else's. "
            + $"TIGHTEST PAIR anywhere in this menu: {tightestPair} = {TightestText(tightest)} apart "
            + $"where their two rects need {tightestNeeds:F0} px not to overlap — VERDICT "
            + $"{(tightOverprint ? "OVERPRINT" : "CLEAR")}. "
            + $"ROWS: {(seat.Parent != null ? RowCensus(seat.Parent, donorHeight) : "the row parent is gone")}. "
            + "CORRECTION (ModBuild 423) to the PLACEMENT rule of 422, which half-worked and shipped a "
            + "SECOND overprint further down the same menu (user, 2026-09-04, menu_kaputt.jpg: \"Das "
            + "Menu ist nun immer dauerhaft mit einer anderen Überlagerung drin 'Hauptmenu' und "
            + "'Schließen' überlagert sich jetzt, siehe menu_kaputt.jpg, fix das.\"). 422 cascaded each "
            + "row down 'only as far as it has to go', which guarantees a pitch only against rows in "
            + "the mod's OWN list — its own line said 'pushing 2 of 3', so a row the eye can see was "
            + "not one the mod could. 423 shifts the whole block below the slot by one pitch instead, "
            + "so every gap between rows is the gap the game authored, and judges column membership "
            + "by row SHAPE rather than by a UIMainMenuOption component. The ROWS census above names "
            + "every child and says which ones are members: a row printed as NOT A MEMBER beside a "
            + "row-sized height is the next bug, and needs no photograph to find.";

        if (overprint || tightOverprint)
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
