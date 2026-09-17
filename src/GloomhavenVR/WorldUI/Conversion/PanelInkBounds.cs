using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>WHAT A CONVERTED WINDOW ACTUALLY DRAWS, as a rectangle in the host's own uGUI pixels.</b>
///
/// <para><b>WHY THIS EXISTS.</b> ModBuild 234 wrote down the rule that a reservation must cost what a
/// window DRAWS, not what it FRAMES: <c>New Party display</c> booked 88° of arc to draw 14°, because
/// its 1988x1080 host rect is mostly empty transparent frame with the ink pinned to the left. The
/// grab bar was the same defect in a different place, and the user photographed it
/// (<c>.planning/debug/quest_überlap.jpg</c>): during the battle-goal phase that window's lowest
/// drawn graphic is literally called <c>Rewards</c> and sits at host-local y=-913, i.e. 373 px BELOW
/// the host rect's own bottom edge at y=-540 — while <see cref="GrabbableModal"/> placed the brass
/// bar one gap below y=-540. The bar therefore landed ON the reward row of the last battle goal, and
/// the user could not read it. Its width and centre came from the frame too, so its left end started
/// near the frame's midpoint (inside the picker) and its right end ran roughly 640 px past the
/// rightmost thing the window draws, over nothing at all.</para>
///
/// <para><b>WHAT IT MEASURES, AND WHAT IT DELIBERATELY DOES NOT.</b> The walk starts at
/// <see cref="ConvertedPanel.Target"/> — the GAME's window root — and NOT at the host. That is the
/// single most important line in this file: the mod's own frame-anchored chrome rides the FRAME's
/// corners on purpose, and unioning it would re-derive the frame, so this class would answer its own
/// question with the number it was written to replace. Since the walk starts at the target, that
/// exclusion is achieved BY THE STARTING POINT and not by any test — see
/// <see cref="ChromeNames"/> for the proof, window by window, and for what the name test that used
/// to stand here actually excluded.</para>
///
/// <para><b>THE ONE THAT COST ModBuild 242 — THE MOD'S OWN OPTIONS PANE WAS NOT COUNTED AS INK.</b>
/// User report, verbatim (2026-08-24): <i>"Im Optionsmenu-Fenster ist die ganze Zeit ein langer
/// Greifbalken BIS ich in die VR Optionen gehe. Dort ist das nur noch ein kleiner Balken unter dem
/// linken Teil des Menüs, siehe Optionsbalken.jpg. Hier sollte die Länge genauso sein wie wenn ich
/// andere native Optionen im Optionsfenster öffne, ich verstehe nicht warum unser mod-eigenes Menü da
/// nicht berücksichtigt wird."</i></para>
///
/// <para>THE ROOT CAUSE WAS A NAME TEST STANDING IN FOR AN ANCESTRY RULE. Through ModBuild 241 this
/// walk skipped — SUBTREE AND ALL — every node whose name began with <c>GloomhavenVR.</c>, and
/// <c>VROptionsTab.1.Inject.cs</c> names its injected objects <c>GloomhavenVR.OptionsTab</c> (:279),
/// <c>GloomhavenVR.OptionsTabWindow</c> (:332), <c>GloomhavenVR.Content</c> (:373) and
/// <c>GloomhavenVR.SubTabs</c> (:432). The pane is a CHILD of the game's own options window, so the
/// walk reached it, matched the prefix and dropped the entire VR settings UI. The ModBuild 241
/// hardware log measures both halves of the defect on the same window within 40 lines of each other:
/// <list type="bullet">
/// <item>A NATIVE tab open (line 3715): <c>the ink union spans x -796..712 (width 1508 px, centre
/// -42)</c>, <c>56 graphic(s) unioned … 1 mod chrome object(s) excluded</c>, bar half-width 415 px.</item>
/// <item>THE VR TAB open (line 3755): <c>the ink union spans x -783..-384 (width 399 px, centre
/// -583)</c>, <c>42 graphic(s) unioned … 2 mod chrome object(s) excluded</c>, bar half-width 110 px —
/// the stub in his photograph. The host rect is 1552x1080 px in both.</item>
/// </list>
/// The second mod-chrome object is the pane; the first is the <c>VR Optionen</c> row in the game's
/// own category column, which the prefix test had been eating (with its label) on EVERY sample since
/// ModBuild 236. The content fit next door disagreed the whole time and nobody laid the two lines
/// side by side: <c>HIT RECT</c> for the same window in the same state reads <c>DRAWN CONTENT
/// 1301x1827 px at (-132,374) from 154 visible graphic(s)</c> — because that walk applies its
/// mod-name test PER GRAPHIC and therefore still measured the pane's game-named children.</para>
///
/// <para><b>THE ONE THAT COST ModBuild 239 (<c>.planning/debug/grosser_abstand.jpg</c>).</b> Until
/// that round <see cref="Draws"/> asked only for the graphic's OWN <c>color.a</c>, and that is the
/// project's <c>[[inherited-alpha-is-not-the-group]]</c> trap written out one more time: a graphic
/// under a <c>CanvasGroup</c> at alpha 0 has <c>enabled</c> true, <c>activeInHierarchy</c> true,
/// <c>color.a</c> 1 and <c>canvasRenderer.cull</c> FALSE, and it puts not one pixel on the screen.
/// <c>New Party display</c> carries such a subtree permanently — a closed rewards popup whose
/// <c>Background Image</c> measures <c>(-270,-913)-(270,-789)</c>, i.e. 373 px BELOW the host rect's
/// own bottom edge at y=-540 and centred on the FRAME's x rather than the column's. The ModBuild 238
/// log has both instruments in the same session disagreeing about the same window by a factor of
/// four and nobody read them side by side:
/// <list type="bullet">
/// <item><c>GRAB BAR CLEARS THE INK</c> (this class): <c>792 graphic(s) unioned</c>, union
/// <c>x -984..992 … y -913..540</c> — and that on <c>sample 1 of generation 1, held 0 frame(s)</c>,
/// so the monotone envelope had contributed exactly nothing to it.</item>
/// <item><c>HIT RECT</c> (<c>CanvasConversion.TryGetVisibleHostRect</c>, same tick, same window):
/// <c>DRAWN CONTENT 880x1080 px at (-542,0) from 182 visible graphic(s)</c> — bottom y=-540,
/// <c>content fits inside the frame</c>.</item>
/// </list>
/// The bar was therefore placed at y=-930, 390 px under a window that ends at y=-540, and centred at
/// x=+4 instead of the column's x=-542. The whole difference is the ONE term the fit's verdict has
/// and this class did not: <c>color.a x canvasRenderer.GetInheritedAlpha() &gt;= 0.05</c>. It is
/// <c>CanvasConversion.FitMinAlpha</c> ITSELF — see <see cref="FaintAlphaFloor"/> below, which
/// references it rather than restating it, so the two cannot drift; the plate fractions beside it
/// borrow the same way since the 2026-09 refactor. The graphics it drops are COUNTED into
/// <see cref="Ink.Faint"/> so the falsifier can say whether this term was the whole story.</para>
///
/// <para>Three further exclusions, each of which was a way to measure the frame again:
/// <list type="bullet">
/// <item><b>FULL-FRAME BACKDROP PLATES.</b> A window's own root <c>Image</c> fills its frame; so does
/// the perks view's 1620x1080 <c>Blur</c>. The test is the fit's, taken BY VALUE (this file may not
/// edit <c>CanvasConversion.3.Fit.cs</c>): width &gt;= 0.80 and height &gt;= 0.95 of the host rect —
/// see <c>CanvasConversion.FixedFitPlateWidthFraction</c> / <c>…HeightFraction</c>, the same
/// borrowing <c>EnchantressComposite</c> already does.
/// <para><b>ModBuild 447 — THE COUNT IS LOAD-BEARING NOW, NOT A DIAGNOSTIC.</b> This exclusion is
/// right for the question this class answers ("what is painted INSIDE the frame") and it was wrong
/// for the question its one consumer was really asking ("how big is this window"), because on
/// <c>UI Shop Item Window</c> the excluded plates ARE the picture: the union spans <c>x 461..977</c>,
/// the item list alone, inside a 1920 px window whose other two thirds is the shopkeeper artwork.
/// The user photographed the consequence (<c>händlerbalken.jpg</c>) and the fix is in the consumer,
/// not here — <see cref="Ink.Plates"/> is read by <c>GrabbableModal</c> and decides, through
/// <c>GrabBarLayout.SolveSpan</c>, whether the grab bar's width and centre come from the frame or
/// from this union. NOTHING ABOUT THIS WALK CHANGED, and that is deliberate: the union is still the
/// bar's VERTICAL answer and still the "does this window draw anything at all" verdict, both of
/// which need the plate gone. But a future round that widens, narrows or deletes the plate test is
/// now moving handles as well as counters, and must read <c>SolveSpan</c> before it does.</para>
/// <para><b>ModBuild 449 — AND THE PLATE'S BOTTOM EDGE IS MEASURED, because 447's sentence above
/// ("the union is still the bar's VERTICAL answer") was true of the host and false of the peer.</b>
/// The plate test is a GREATER-OR-EQUAL on both axes, so a plate may be LARGER than the frame; a 16:9
/// artwork stretched to the width of a 2580x1080 canvas is 1451 px tall and hangs 371 px below a
/// 1080 px frame. The union then answers "the lowest thing drawn is at the frame's own bottom" and
/// the rod is seated one gap under that — in the middle of the picture. <see cref="Ink.PlateBottom"/>
/// is that edge, kept OUT of <see cref="Ink.Rect"/> (the X, the badge and the re-face pivot all ride
/// that rectangle and none of them wanted a backdrop in it) and read by the rod's vertical seat
/// alone.</para></item>
/// <item><b>EMPTY TEXT.</b> <c>PanelSupersample.Draws</c> is permissive by design — enabled, active,
/// alpha above zero, not culled — and that is right for a CAPTURE FRAME, which must never crop. It is
/// wrong here: a <c>TMP_Text</c> with an empty string passes every one of those tests and contributes
/// its whole (often frame-wide) RectTransform to the union while putting no pixel on the screen. The
/// ModBuild 235 log shows exactly such a graphic, a 599x74 <c>Title</c> reaching the host rect's right
/// edge at x=994 in a phase whose visible content stops at x=-93.</item>
/// <item><b>FOREIGN RENDER SUBTREES.</b> A real <see cref="Renderer"/> or <see cref="Camera"/> under
/// the window belongs to somebody else (the live 3D character rig and its preview camera) and is not
/// uGUI ink at all — the same rule, for the same reason, as the capture frame's walk.</item>
/// </list></para>
///
/// <para><b>THE ONE THAT COST ModBuild 241 — MOUSEOVERS.</b> User report, verbatim (2026-08-24):
/// <i>"Mouseovers sollen den greifbar nicht vergrößeren, sonst kommt es ständig dazu, dass der Balken
/// sich hektisch verändert wenn man mit dem Laser durch Elemente mit mouseovers zB der Kartenliste
/// geht. Mouseovers haben die Erlaubnis aus der Größe des Fensters herauszuragen ohne die eigentliche
/// Größe zu verändern (Ausnahmeregelung für Mouseovers). WICHTIG: Das soll nicht für andere Elemente
/// gelten wie zB die Auswahl der persönlichen Quest wo das resizing das von dir eingebaut wurde das
/// Problem der Verdeckung behoben hat."</i></para>
///
/// <para>THE ROOT CAUSE IS THAT THIS WALK NEVER ASKED. <c>CanvasConversion.3.Fit.cs</c> has refused to
/// MEASURE a hover widget since ModBuild 201 — <see cref="TransientFamilies"/> is that rule's table —
/// and this class, written 35 builds later for the same window, simply did not consult it. The
/// ModBuild 239 hardware log has both instruments in the same session disagreeing about the same
/// window, exactly as they did over the inherited-alpha term one round earlier:
/// <list type="bullet">
/// <item>The fit's own <c>MOUSEOVER LEDGER</c> on <c>New Party display</c>:
/// <c>0 transient graphic(s) refused this pass, 320 over this window's life, from
/// UIPartyItemInventoryTooltip (the item-card hint), UILocalTooltip and its subclasses</c>.</item>
/// <item><c>GRAB BAR CLEARS THE INK</c> for the SAME window, over 24 measured samples: the union's
/// bottom edge took 2 distinct values, but its CENTRE took THIRTEEN — -818, -817, -560, -547, -544,
/// -535, -530, -409, -389, -248, -234, 0, +6 px — and its width thirteen more, 328 up to 1976 px.
/// The equipment view's own right edge is x=-111 px (the fit line names its rect as -654..-111), and
/// three of those unions end at -104, -88 and -78 instead: 7, 23 and 33 px of item hint. The bar is
/// centred on the union and sized from it, so each of those is a visible twitch of the handle.</item>
/// </list>
/// <b>AND THE SECOND HALF, WHICH IS THE LOUDER ONE.</b> <c>TooltipOnWindow.RaiseToWindowTop</c> ends
/// with <c>rect.SetParent(owner.Target, worldPositionStays: false)</c> — the widget becomes a DIRECT
/// CHILD of the conversion target for the length of the hover, and is put back after it.
/// <see cref="ActiveSetSignature"/> hashes exactly the active direct children of that target, so every
/// hover and every un-hover was a GENERATION EVENT: the monotone envelope was thrown away and
/// re-seeded from the next sample, which is why that window reached generation 51 in one session and
/// why the bar could jump on a mouse-out as well as a mouse-in. Both halves are fixed here, from the
/// one table.</para>
///
/// <para><b>WHY THE EXEMPTION CANNOT REACH THE PERSONAL-QUEST ROWS</b>, which is the constraint he
/// marked WICHTIG. The test is an IDENTITY — six of the game's own component types on the node or an
/// ancestor of it, and nothing else. It does not test size, position, lifetime, transparency, novelty,
/// or "does it stick out of the frame", and every one of those would ALSO describe the quest picker's
/// reward rows: <c>Rewards</c> at host-local y=-628 px in this very log, 88 px below a frame that ends
/// at -540, is the content ModBuild 236 moved the bar for and the fix he says solved his occlusion
/// problem. Those rows live under the picker's own sub-view root and carry none of the six types, so
/// they are measured exactly as they were before this round — the two NOT ACHIEVED lines they produce
/// (the handle correctly hanging 105 px below the frame) are unchanged by construction. A future round
/// that wants to widen this must add a NAMED family to <see cref="TransientFamilies"/>, never a
/// property test.</para>
///
/// <para><b>THIS CLASS NEVER WRITES GAME STATE.</b> It reads transforms and components and returns a
/// rectangle. No Show/Hide/SetActive/CanvasGroup, no layout rebuild, no allocation per call beyond the
/// two static scratch buffers below.</para>
/// </summary>
internal static class PanelInkBounds
{
    /// <summary>Full-frame plate test — <c>CanvasConversion.FixedFitPlateWidthFraction</c> and
    /// <c>FixedFitPlateHeightFraction</c> THEMSELVES (0.80 / 0.95), not copies of their values.
    ///
    /// <para>They were copies until the 2026-09 refactor (F-52), justified by "those are private to
    /// another lane's file" — a boundary that does not exist: <c>CanvasConversion.3.Fit.cs</c> and
    /// this file are the same folder, the same namespace, the same assembly and the same lane, and
    /// <see cref="FaintAlphaFloor"/> three declarations down had already proved the mechanism works.
    /// The drift was load-bearing rather than cosmetic: since ModBuild 447 <c>Ink.Plates</c> is a
    /// HANDLE — read by <c>GrabbableModal</c> and deciding, through <c>GrabBarLayout.SolveSpan</c>,
    /// whether the grab bar's width and centre come from the frame or from this union — so a tune of
    /// the fit's pair would silently stop agreeing with the bar's width source, with nothing but a
    /// count in a log line to notice.</para></summary>
    private const float PlateWidthFraction = CanvasConversion.FixedFitPlateWidthFraction;
    private const float PlateHeightFraction = CanvasConversion.FixedFitPlateHeightFraction;

    /// <summary>EFFECTIVE-ALPHA FLOOR — <c>CanvasConversion.FitMinAlpha</c> itself (0.05), NOT a copy
    /// of its value. This is the term whose absence put the grab bar 390 px under an empty frame; see
    /// the class comment for both instruments' numbers. Graphics it rejects are counted into
    /// <see cref="Ink.Faint"/>, never silently dropped.
    ///
    /// <para>ModBuild 439 (survey row R27): the house "is this graphic painting?" floor existed as
    /// FIVE 0.05 literals — this one, <c>EnemyRevealSurface.DrawAlphaFloor</c>,
    /// <c>RemoteWidgetMirror.FitMinAlpha</c>, a bare inline one in <c>ModalFallback</c>, and the
    /// original — of which <c>check-mirrors.sh</c> could see two. It is one constant now, for the
    /// reason <c>FitMinAlpha</c>'s own doc gives about a different pair of readers: the rule that
    /// hides a window and the rule that brings it back must not be able to disagree about the same
    /// graphic. Unlike <see cref="PlateWidthFraction"/> this one needed no borrowing at all — the
    /// source has been <c>internal</c> since ModBuild 291 — which the plate pair above now is too
    /// (2026-09 refactor, F-52), so this paragraph's "unlike" no longer distinguishes them.</para></summary>
    private const float FaintAlphaFloor = CanvasConversion.FitMinAlpha;

    /// <summary>Node budget for one walk. A converted window is order hundreds of transforms; this is
    /// a runaway guard, not a working limit, and <see cref="Ink.Truncated"/> reports if it ever bites
    /// rather than letting a silently short union move the bar.</summary>
    private const int MaxNodes = 6000;

    /// <summary>ModBuild 243 — how many levels below <see cref="ConvertedPanel.Target"/>
    /// <see cref="ActiveSetSignature"/>'s part one hashes. TWO, because the game's options window
    /// keeps its eleven tab windows at <c>Target/Tabs/&lt;tab&gt;</c> and depth one therefore saw
    /// four tab presses as no event at all; see that method's ModBuild 243 block for the log lines.
    /// Every level costs a per-frame walk on every floated window, so this is raised only against a
    /// measured hierarchy, never on principle.</summary>
    private const int SignatureDepth = 2;

    /// <summary>Node budget for ONE signature walk — a runaway guard for a window with a very wide
    /// second level, not a working limit (the options window hashes of order sixty). Reaching it is
    /// REPORTED, never silent: a signature that has quietly stopped covering part of the tree looks
    /// exactly like a window that never changes ([[sentinel-overflow-and-silent-scans]]).</summary>
    private const int SignatureMaxNodes = 256;

    /// <summary>
    /// <b>MOD-OWNED OBJECTS THAT ARE NOT WINDOW CONTENT — BY NAME, ONE AT A TIME.</b> Index 0 is
    /// "not chrome" and is never printed; every other entry is matched with <c>StartsWith</c> so the
    /// instanced forms (<c>GloomhavenVR.PanelSS_UI Options Window_unified</c>,
    /// <c>GloomhavenVR.AvatarTurnRing[Bruno:3]</c>) are one entry each.
    ///
    /// <para><b>WHY A LIST AND NOT THE PREFIX IT REPLACES.</b> The prefix answered "was this object
    /// made by the mod", and that is not the question. The question is "is this object part of the
    /// picture the window paints", and a mod-authored object can be either — the VR options pane is
    /// content, a breathing focus ring is not. The prefix could not tell them apart, so it deleted the
    /// pane; see the class comment for the two log lines that measure it.</para>
    ///
    /// <para><b>THE FIRST TWO ENTRIES ARE A BELT, AND THE PROOF IS WORTH WRITING DOWN.</b> They are
    /// the two objects the old test named as its reason, and NEITHER IS REACHABLE from this walk:
    /// <list type="bullet">
    /// <item><c>GloomhavenVR.ModalCloseX</c> is parented to the HOST rect
    /// (<c>ModalCloseButton.Build</c>), and <c>CanvasConversion.1.Core.cs:241</c> parents the
    /// conversion TARGET to that same host rect — so the X is a SIBLING of this walk's root, never a
    /// descendant of it.</item>
    /// <item>The supersample display quad is a SCENE ROOT, not a child of the host at all
    /// (<c>PanelSupersample.2.Capture.cs</c> <c>BuildDisplay</c>, and <c>SyncGeometry</c> says so in
    /// as many words: "The display quad is a scene root, so its full pose is copied"). The ModBuild
    /// 241 class comment's claim that it is "parented to the host as well" was simply wrong.</item>
    /// </list>
    /// The whole ModBuild 241 hardware session confirms it: <c>mod chrome object(s) excluded</c> reads
    /// 0 on every floated window in the log — <c>New Party display</c> (13 lines), <c>Quest Log
    /// Manager</c>, <c>UI Quest Popup</c>, <c>UI Loadout Window</c>, <c>UI Event Window</c>, <c>Map
    /// Story Window</c>, <c>UI Map Esc Menu</c> — and 1 or 2 on exactly one, the options window, where
    /// both were the VR options tab. The exclusion never once did its stated job. The two entries stay
    /// anyway because they cost one <c>StartsWith</c> against a name that is not in this tree, and
    /// because a future round that re-parents either of them under the target would otherwise
    /// re-create ModBuild 234's "88° of arc to draw 14°" silently.</para>
    ///
    /// <para><b>THE REST IS THE RULE THE PREFIX WAS ACCIDENTALLY ALSO CARRYING</b>, and it is a real
    /// one: mod-drawn PRESENTATION OVERLAY on the game's content. <c>CanvasConversion.3.Fit.cs:700-709</c>
    /// records what it costs to measure it — <c>GloomhavenVR.FocusRing</c> / <c>GloomhavenVR.SelectionRing</c>
    /// BREATHE (<c>Board.FocusCue</c> pulses their scale), and the 2026-08-08 log has 26 applied
    /// re-fits of <c>Panel_InitiativeTrack</c> caught at different points of their swell, host height
    /// oscillating 182/186/188/190 px, which the user felt as the portraits stepping up and down. The
    /// grab bar is centred on and sized from this union, so measuring a breathing ring here would be
    /// that defect with a handle attached. Named by TYPE of furniture, never by "the mod made it".</para>
    ///
    /// <para>A future round that finds a mod object wrongly counted as ink adds its NAME here, and
    /// the log line names what it removed (<see cref="DescribeChrome"/>) so "the exclusion did
    /// nothing" and "the exclusion ate the window" can never look alike again.</para>
    /// </summary>
    private static readonly string[] ChromeNames =
    {
        string.Empty,
        "GloomhavenVR.ModalCloseX (the mod's close button, parented to the HOST — unreachable from here)",
        "GloomhavenVR.PanelSS_ (the supersample display quad, a scene root — unreachable from here)",
        "GloomhavenVR.FocusRing (breathing focus cue drawn over a portrait)",
        "GloomhavenVR.SelectionRing (breathing selection cue drawn over a portrait)",
        "GloomhavenVR.FocusTurnRing (breathing turn cue drawn over a portrait)",
        "GloomhavenVR.RemoteFocusRing (a peer's focus cue drawn over a portrait)",
        "GloomhavenVR.RemoteSelectionGlow (a peer's selection cue drawn over a portrait)",
        "GloomhavenVR.AvatarTurnRing (a peer's turn cue drawn over a portrait)",
        "GloomhavenVR.FocusBoardFrame (the focus frame drawn around a converted panel)",
        "GloomhavenVR.RemoteFocusFrame (a peer's focus frame drawn around a converted panel)",
        "GloomhavenVR.MrBacking (the mixed-reality opacity plate behind a panel)",
    };

    /// <summary>The <see cref="ChromeNames"/> prefixes, without the parenthesised explanation that
    /// only the log wants. Built once; the entries are compile-time constants in practice.</summary>
    private static readonly string[] ChromePrefixes = BuildChromePrefixes();

    private static string[] BuildChromePrefixes()
    {
        var prefixes = new string[ChromeNames.Length];
        prefixes[0] = string.Empty;
        for (int i = 1; i < ChromeNames.Length; i++)
        {
            string full = ChromeNames[i];
            int space = full.IndexOf(' ');
            prefixes[i] = space > 0 ? full.Substring(0, space) : full;
        }
        return prefixes;
    }

    /// <summary>Which <see cref="ChromeNames"/> entry <paramref name="name"/> is, or 0 for window
    /// content. One <c>StartsWith</c> per entry, and only for names that begin with the mod's own
    /// prefix — so a game object (every node in the common case) costs exactly one comparison.</summary>
    private static int ChromeIndexOf(string name)
    {
        if (!name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal))
            return 0;
        for (int i = 1; i < ChromePrefixes.Length; i++)
        {
            if (name.StartsWith(ChromePrefixes[i], System.StringComparison.Ordinal))
                return i;
        }
        return 0;
    }

    /// <summary>The chrome entries in <paramref name="mask"/>, spelled out for the log — the same
    /// contract, for the same reason, as <c>TransientFamilies.Describe</c>.</summary>
    internal static string DescribeChrome(int mask)
    {
        var sb = new System.Text.StringBuilder(96);
        for (int i = 1; i < ChromeNames.Length; i++)
        {
            if ((mask & (1 << i)) == 0)
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(ChromeNames[i]);
        }
        return sb.Length > 0
            ? sb.ToString()
            : "nothing (no mod-owned overlay is inside this window — mod-AUTHORED CONTENT such as the "
              + "VR options pane is measured as ink, which is the ModBuild 242 fix)";
    }

    /// <summary>The measured ink of one window, in the host RectTransform's own local uGUI pixels —
    /// the SAME space <c>ConvertedPanel.HostRect.rect</c> is expressed in, so the two are directly
    /// comparable and the log can print both.</summary>
    internal struct Ink
    {
        internal bool Valid;
        internal Rect Rect;
        internal int Graphics;
        /// <summary>Graphics refused as FULL-FRAME BACKDROP PLATES — a surface covering at least
        /// 0.80 of the host rect's width and 0.95 of its height, at an effective alpha the fit calls
        /// visible. ModBuild 447: this is no longer only a census number. A non-zero count is the
        /// statement "this window paints its whole frame", and <c>GrabbableModal</c> hands it to
        /// <c>GrabBarLayout.SolveSpan</c>, which then takes the grab bar's width and centre from the
        /// FRAME instead of from <see cref="Rect"/>. See the class comment's ModBuild 447
        /// block.</summary>
        internal int Plates;

        /// <summary>
        /// ModBuild 449 — <b>THE LOWEST EDGE ANY FULL-FRAME PLATE REACHES</b>, in the same
        /// host-local px as <see cref="Rect"/>, or <c>float.PositiveInfinity</c> when this walk saw
        /// no plate at all. It is deliberately NOT folded into <see cref="Rect"/>: the union's
        /// contract is "a tight box around what the window draws OTHER than its backdrop", and the
        /// close X, the badge and the re-face pivot are all seated off that box. This is one extra
        /// number for the one consumer that must not ignore a plate — the grab bar's VERTICAL seat.
        ///
        /// <para><b>WHY IT HAD TO EXIST</b> (2026-09-05, item 15, the co-player's merchant). The
        /// plate exclusion was written against the belief, stated in as many words on
        /// <c>GrabbableModal.SyncBar</c>, that "a full-frame plate cannot change this number anyway —
        /// its bottom edge IS hostRect.yMin". That is true only while the plate's aspect matches the
        /// frame's. On the co-player's 2580x1080 canvas the merchant's 16:9 shopkeeper artwork is
        /// stretched to the frame's WIDTH and so stands 1451 px tall against a 1080 px frame, hanging
        /// <c>371 px BELOW it</c> (the fit's own reading for the same window and tick:
        /// <c>DRAWN CONTENT 2597x1451 px at (8,-186) [y -911..540]</c>, against the host's
        /// <c>1937x1080 px at (8,0) [y -540..540]</c>). The union reported its bottom at the frame's
        /// -540, the rod was seated one gap under that at -574, and 337 px of merchant were still
        /// drawn below it — the bar in the middle of the picture, on the peer only. The plate test
        /// is a GREATER-OR-EQUAL test on both axes, so a plate is allowed to be BIGGER than the frame
        /// and this field is the only thing that notices.</para>
        ///
        /// <para>A plate whose bottom is at or above <c>hostRect.yMin</c> — every plate on a canvas
        /// whose aspect matches the art, which is every plate the host client ever measured — leaves
        /// every consumer of this field bit-identical, because they all take a <c>Min</c> against
        /// <c>hostRect.yMin</c> anyway.</para>
        /// </summary>
        internal float PlateBottom;

        /// <summary>The plate that set <see cref="PlateBottom"/>. Empty when there was none.</summary>
        internal string PlateBottomName;

        internal int EmptyText;
        internal int ModChrome;
        /// <summary>Bit per index of <see cref="ChromeNames"/> refused on this walk. The count alone
        /// cannot tell "the exclusion did nothing" from "the exclusion ate the window" — which is
        /// exactly how a <c>1 mod chrome object(s) excluded</c> on the options window read as
        /// harmless for six builds while it was deleting the VR settings pane.</summary>
        internal int ModChromeMask;
        /// <summary>Drawn-but-invisible: effective alpha (own colour x inherited CanvasGroup alpha)
        /// below <see cref="FaintAlphaFloor"/>. A non-zero count on a window whose union used to reach
        /// far outside its frame is this term doing the work it was added for.</summary>
        internal int Faint;
        /// <summary>MOUSEOVER GRAPHICS REFUSED — drawn, visible, and belonging to one of
        /// <see cref="TransientFamilies"/>. Directly comparable with the number the fit's
        /// <c>MOUSEOVER LEDGER</c> prints for the same window, which is the point of counting it:
        /// two instruments that disagree about how many hover graphics a window has are two
        /// instruments one of which is wrong, and that is how ModBuild 239's inherited-alpha defect
        /// was finally read.</summary>
        internal int Transient;
        /// <summary>Bit per family index of <see cref="TransientFamilies.Names"/> refused on this
        /// walk. Spelled out with <see cref="TransientFamilies.Describe"/> for the log, so "the
        /// exclusion did nothing" and "the exclusion never had anything to do" cannot look alike.</summary>
        internal int TransientMask;
        internal bool Truncated;
        /// <summary>The graphic that set the union's BOTTOM edge — the one the bar has to clear, and
        /// the only name worth carrying into the report.</summary>
        internal string BottomName;
    }

    private struct ClipFrame
    {
        internal readonly Transform Transform;
        internal readonly Rect Clip;

        /// <summary>The transient family this node INHERITS from its ancestors, carried down the
        /// stack exactly like <see cref="Clip"/>. Doing it this way rather than calling
        /// <see cref="TransientFamilies.Of"/> per graphic is what keeps this walk memo-free: the walk
        /// is already top-down, so every node's ancestor chain has been visited before it and one
        /// <see cref="TransientFamilies.Self"/> probe per node answers the whole question. See
        /// <see cref="MeasureCore"/>'s comment for why NOT sharing the fit's memo is deliberate.</summary>
        internal readonly int Family;

        internal ClipFrame(Transform transform, Rect clip, int family)
        {
            Transform = transform;
            Clip = clip;
            Family = family;
        }
    }

    private static readonly Rect Unbounded = Rect.MinMaxRect(-1e6f, -1e6f, 1e6f, 1e6f);
    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly List<ClipFrame> Stack = new(128);

    /// <summary>
    /// Measure what <paramref name="panel"/>'s GAME content draws, in host-local uGUI px.
    /// Returns false — and leaves <paramref name="ink"/> at <c>Valid = false</c> — when there is
    /// nothing measurable, which the caller must treat as "keep the frame-based placement".
    /// Never throws: a throw here would stand down a window's whole follow tick.
    /// </summary>
    // contentRoot is an optional MR-only content boundary. Null preserves all existing window,
    // grab and capture queries. A declared board row retains ancestor masks but never measures
    // parent/sibling artwork; visible images inside it are ink even if they fill the row's frame.
    internal static bool TryMeasure(ConvertedPanel panel, out Ink ink, bool includeParkedHint = true,
                                    Rect? frameOverride = null, ISet<Transform>? excludedRoots = null,
                                    Transform? contentRoot = null)
    {
        ink = default;
        ink.BottomName = string.Empty;
        // NOT ZERO. `default` leaves this at 0, which is a host-local y INSIDE every centred frame
        // and would read as "a plate ends at the window's middle" on every window that has no plate
        // at all. The identity for a Min is the infinity.
        ink.PlateBottom = float.PositiveInfinity;
        ink.PlateBottomName = string.Empty;
        try
        {
            return MeasureCore(panel, ref ink, includeParkedHint, frameOverride, excludedRoots, contentRoot);
        }
        catch (System.Exception)
        {
            Stack.Clear();
            ink.Valid = false;
            return false;
        }
    }

    private static bool MeasureCore(ConvertedPanel panel, ref Ink ink, bool includeParkedHint, Rect? frameOverride,
                                    ISet<Transform>? excludedRoots, Transform? contentRoot)
    {
        RectTransform? host = panel.HostRect;
        Transform? target = panel.Target;
        if (host == null || target == null || !target.gameObject.activeInHierarchy
            || !MrBackingScope.Valid(target, contentRoot))
            return false;

        // Native mirror pivots carry the original parent layout, while the owner's fitted frame
        // is separate sampled presentation data. Only backdrop classification reads this override;
        // all actual geometry is still transformed through the original host/pivot.
        Rect hostRect = frameOverride ?? host.rect;
        float plateW = hostRect.width * PlateWidthFraction;
        float plateH = hostRect.height * PlateHeightFraction;
        bool plateTestUsable = hostRect.width > 1f && hostRect.height > 1f;

        float minX = 0f, minY = 0f, maxX = 0f, maxY = 0f;
        int nodes = 0;

        // NO MEMO, AND NOT THE FIT'S. CanvasConversion.3.Fit.cs memoises its bottom-up family walk in
        // a static dictionary that it clears at the top of every split measure; this walk runs on a
        // completely different cadence (an event-driven capture, see GrabbableModal's ink block), so
        // sharing that dictionary would mean each instrument silently invalidating the other's cache,
        // and — worse — a dictionary keyed on Transforms that grows without bound on any window the
        // fit never touches, holding Unity fake-null keys for destroyed objects. This walk is TOP-DOWN
        // and single-pass, so it needs no cache at all: the family is inherited down the stack below,
        // one Self() probe per node, and the interval over which an ancestor chain must stay still is
        // exactly one walk. (The only behavioural difference is that inheritance answers with the
        // OUTERMOST marker where the fit answers with the innermost; they are non-zero for the same
        // set of nodes, which is all an exclusion reads.)
        bool excludeHint = !includeParkedHint && !HintOnOwnerComposite.IsParkedContent(target);
        Stack.Clear();
        Stack.Add(new ClipFrame(target, Unbounded, 0));
        while (Stack.Count > 0)
        {
            int last = Stack.Count - 1;
            ClipFrame node = Stack[last];
            Stack.RemoveAt(last);
            Transform t = node.Transform;
            if (t == null || !t.gameObject.activeSelf || (excludedRoots != null && excludedRoots.Contains(t))
                || !MrBackingScope.Visit(t, contentRoot))
                continue;
            if (++nodes > MaxNodes)
            {
                ink.Truncated = true;
                break;
            }

            bool isRoot = ReferenceEquals(t, target);
            // Placement must measure the owner independently of the transient annotation.
            // The ordinary ink query retains it for chrome/input; measuring the hint itself
            // also remains valid, since it is then the target rather than an owner guest.
            if (excludeHint && HintOnOwnerComposite.IsParkedContent(t))
                continue;
            // MOD-OWNED FURNITURE, BY NAME, ONE NAME AT A TIME (ModBuild 242). The subtree skip is
            // kept — every entry in the table is a self-contained overlay or a frame-anchored plate,
            // and skipping a breathing ring's children is the point — but the SET is now explicit,
            // so a mod object that genuinely draws inside the window (the VR options pane) is ink.
            // Counted AND NAMED, never silently dropped: see ChromeNames.
            int chrome = isRoot ? 0 : ChromeIndexOf(t.name);
            if (chrome != 0)
            {
                ink.ModChrome++;
                ink.ModChromeMask |= 1 << chrome;
                continue;
            }
            // Foreign render subtree: not uGUI ink, drawn by another camera at its own world pose.
            if (!isRoot && (t.GetComponent<Renderer>() != null || t.GetComponent<Camera>() != null))
                continue;

            Rect clip = node.Clip;
            var rt = t as RectTransform;
            // THE MOUSEOVER EXEMPTION (ModBuild 241) — inherited first, probed only if it has to be.
            // Three things bound the cost of the six-way probe, which is the one new per-node expense
            // this round adds to a walk of up to MaxNodes transforms:
            //   * an INHERITED family short-circuits it, so a hover subtree is probed at its root and
            //     nowhere below;
            //   * the ROOT is never probed — it is the window, and a window that answered "I am a
            //     tooltip" would exclude itself entirely;
            //   * a non-RectTransform is never probed. Every one of the six is a uGUI widget;
            //     TooltipOnWindow.Settle itself refuses a widget whose transform is not a
            //     RectTransform, so a plain Transform cannot be one of them.
            int family = node.Family;
            if (family == 0 && !isRoot && rt != null)
                family = TransientFamilies.Self(t);
            if (rt != null)
            {
                if (TryHostLocalBounds(host, rt, out Rect bounds))
                {
                    if (ClipsChildren(t) && !Intersect(clip, bounds, out clip))
                        continue; // fully clipped away: neither this nor anything under it draws

                    var graphic = t.GetComponent<Graphic>();
                    // Expand drawn glyphs only; clip geometry and the authored scale census
                    // must retain the original RectTransform bounds.
                    Rect drawBounds = RewardHeadingBounds.Expand(host, graphic, bounds);
                    if (MrBackingScope.Paint(t, contentRoot)
                        && Draws(graphic) && Intersect(clip, drawBounds, out Rect visible)
                        && visible.width > 0f && visible.height > 0f)
                    {
                        // ---- ModBuild 449 - THE HANDLE FOLLOWED AN ANIMATION, NOT THE CONTENT. ---
                        //
                        // 448's family 7 is a SUBTREE test guarded so it can never delete a widget's
                        // own label, and on the map room's quest card that guard refuses: every one
                        // of this line's own MOUSEOVER LEDGER readings on the 448 pair says `0
                        // transient graphic(s) refused ... from no hover/tooltip family`, while the
                        // bar's top edge wandered across y=-743..-801 px on the host and
                        // y=-468..-794 px on the co-player, each line naming 'UIFX_Wave (1)' as the
                        // graphic holding it down. Two clients sampling one animation at their own
                        // phase is a 1:1 breach that no settle gate can close, and it is the user's
                        // "der handle ist an einer anderen Hoehe als bei mir".
                        //
                        // Asked HERE, inside the draws test, and not beside the Self() probe above:
                        // the walk visits hundreds of nodes and only a few dozen of them paint, so
                        // the ancestor walk is paid once per DRAWN graphic that 448 already cleared.
                        if (family == 0 && TransientFamilies.IsDeclaredEffectQuad(t, target))
                            family = TransientFamilies.EffectQuadFamily;
                        // FAINT FIRST, deliberately: a graphic at effective alpha 0 draws nothing at
                        // all, so saying "it is a full-frame plate" about it would report the weaker
                        // of two true statements and hide the term the next reader needs.
                        if (IsFaint(graphic))
                        {
                            ink.Faint++;
                        }
                        // MOUSEOVER SECOND, for the same reason FAINT is first: a hover widget that
                        // is still fading in at effective alpha 0 contributes nothing to the union
                        // whatever we call it, and counting it here would inflate the "the exemption
                        // did work" number with graphics the exemption did not have to touch. What
                        // this count means is therefore exactly: graphics that WOULD have moved the
                        // bar and no longer do.
                        else if (family != 0)
                        {
                            ink.Transient++;
                            ink.TransientMask |= 1 << family;
                        }
                        // An explicitly scoped board row measures its actual painted images,
                        // including portrait backgrounds. Calling these a full-frame backdrop
                        // would substitute the obsolete screen-sized parent for a small row.
                        // A movie's sole RawImage IS its content, despite filling the frame. In
                        // build 505 the generic backdrop exclusion left no ink and hid its grab
                        // bar after two empty samples. Exempt only the declared content identity:
                        // all visibility/alpha/clip checks above and other backdrop rules remain.
                        else if (contentRoot == null && !ReferenceEquals(graphic, panel.ContentGraphic)
                                 && plateTestUsable && visible.width >= plateW && visible.height >= plateH)
                        {
                            ink.Plates++;
                            // ModBuild 449 — THE PLATE STILL CONTRIBUTES NOTHING TO THE UNION, and
                            // one number beside it. See Ink.PlateBottom for the co-player's merchant
                            // that made a plate's bottom edge worth measuring; the HORIZONTAL
                            // exclusion, which is what the union exists for, is untouched.
                            if (visible.yMin < ink.PlateBottom)
                            {
                                ink.PlateBottom = visible.yMin;
                                ink.PlateBottomName = t.name;
                            }
                        }
                        else if (IsEmptyText(graphic))
                        {
                            ink.EmptyText++;
                        }
                        else
                        {
                            if (ink.Graphics == 0)
                            {
                                minX = visible.xMin; maxX = visible.xMax;
                                minY = visible.yMin; maxY = visible.yMax;
                                ink.BottomName = t.name;
                            }
                            else
                            {
                                if (visible.xMin < minX) minX = visible.xMin;
                                if (visible.xMax > maxX) maxX = visible.xMax;
                                if (visible.yMax > maxY) maxY = visible.yMax;
                                if (visible.yMin < minY)
                                {
                                    minY = visible.yMin;
                                    ink.BottomName = t.name;
                                }
                            }
                            ink.Graphics++;
                        }
                    }
                }
            }

            for (int i = t.childCount - 1; i >= 0; i--)
                Stack.Add(new ClipFrame(t.GetChild(i), clip, family));
        }
        Stack.Clear();

        // THE SAFETY NET IS THE FALLBACK ITSELF, and it is a different (stronger) one than the fit's.
        // CanvasConversion.3.Fit.cs builds both a clean and a raw union and falls back to the raw one
        // when the exclusion empties a bucket, because it MUST write a size for the window either way.
        // Nothing here must write anything: an unmeasurable ink means the bar keeps the placement it
        // already had, which is the one outcome that cannot be wrong on the user's screen. So a window
        // whose every drawn graphic turned out to be a mouseover simply reports NOT ACHIEVED with the
        // refused count on the line (ink.Transient survives this return), and the next reader can see
        // in one line whether this round emptied a bucket. It should be unreachable: the six families
        // are hover widgets the game instantiates on top of a window, never the window.
        if (ink.Graphics == 0 || maxX - minX <= 0f || maxY - minY <= 0f)
            return false;

        ink.Rect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        ink.Valid = true;
        return true;
    }

    /// <summary>Is this graphic switched on at all? The permissive part is
    /// <c>PanelSupersample.Draws</c>'s, verbatim in effect. It is NOT the question "does it put pixels
    /// on the screen" and ModBuild 238 shipped believing it was — <see cref="IsFaint"/> and
    /// <see cref="IsEmptyText"/> carry the two extra rules this class needs and the capture frame must
    /// not have (a capture frame must never crop; a bar placement must never chase a ghost).</summary>
    private static bool Draws(Graphic? g)
    {
        if (g == null || !g.enabled || !g.gameObject.activeInHierarchy)
            return false;
        if (g.color.a <= 0.004f)
            return false;
        CanvasRenderer cr = g.canvasRenderer;
        return cr != null && !cr.cull;
    }

    /// <summary>
    /// DRAWN BUT INVISIBLE — effective alpha below <see cref="FaintAlphaFloor"/>, where effective
    /// alpha is the graphic's own colour alpha TIMES the alpha its CanvasRenderer inherited from every
    /// <c>CanvasGroup</c> above it. <see cref="Draws"/> cannot answer this: a closed popup held at
    /// <c>CanvasGroup.alpha = 0</c> passes enabled, active, <c>color.a</c> and <c>cull</c> on every
    /// one of its graphics. Unity 2021.3.5f1 has no setter for the inherited value
    /// (<c>[[inherited-alpha-is-not-the-group]]</c>) but the getter is exactly what the content fit
    /// already reads, so this is the fit's verdict and not a second opinion.
    /// </summary>
    private static bool IsFaint(Graphic? g)
    {
        if (g == null)
            return true;
        CanvasRenderer cr = g.canvasRenderer;
        if (cr == null)
            return true;
        return g.color.a * cr.GetInheritedAlpha() < FaintAlphaFloor;
    }

    /// <summary>A text component with nothing to typeset. Its RectTransform is frequently the width of
    /// the whole window (a header slot, a right-aligned label's box), so counting it as ink is exactly
    /// the "the tight box is not the rect" error in reverse — here the RECT is the lie.</summary>
    private static bool IsEmptyText(Graphic? g)
    {
        if (g is TMP_Text tmp)
            return string.IsNullOrWhiteSpace(tmp.text);
        if (g is Text legacy)
            return string.IsNullOrWhiteSpace(legacy.text);
        return false;
    }

    /// <summary>Both uGUI clipping mechanisms, enabled ones only — same rule as the capture walk.</summary>
    private static bool ClipsChildren(Transform t)
    {
        var rect2d = t.GetComponent<RectMask2D>();
        if (rect2d != null && rect2d.enabled && rect2d.gameObject.activeInHierarchy)
            return true;
        var mask = t.GetComponent<Mask>();
        if (mask != null && mask.enabled && mask.gameObject.activeInHierarchy)
        {
            var graphic = t.GetComponent<Graphic>();
            if (graphic != null && graphic.enabled)
                return true;
        }
        return false;
    }

    /// <summary>Axis-aligned bounds of <paramref name="rt"/> in <paramref name="host"/>'s local space,
    /// in uGUI px. World corners rather than the raw rect, so a child under any chain of scales or
    /// rotations is measured where it actually lands.</summary>
    private static bool TryHostLocalBounds(RectTransform host, RectTransform rt, out Rect bounds)
    {
        bounds = default;
        Rect local = rt.rect;
        if (local.width <= 0f && local.height <= 0f)
            return false;
        rt.GetWorldCorners(Corners);
        Vector3 first = host.InverseTransformPoint(Corners[0]);
        float minX = first.x, maxX = first.x, minY = first.y, maxY = first.y;
        for (int i = 1; i < 4; i++)
        {
            Vector3 p = host.InverseTransformPoint(Corners[i]);
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }
        bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    private static bool Intersect(Rect a, Rect b, out Rect result)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin);
        float xMax = Mathf.Min(a.xMax, b.xMax);
        float yMin = Mathf.Max(a.yMin, b.yMin);
        float yMax = Mathf.Min(a.yMax, b.yMax);
        result = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return xMax > xMin && yMax > yMin;
    }

    /// <summary>
    /// <b>A CHEAP, STABLE SIGNATURE OVER WHAT IS OPEN — the seam that says "re-capture".</b>
    ///
    /// <para>A LOCAL DERIVATION of the same two-part answer <c>PanelSupersample.ActiveSetSignature</c>
    /// derives for the capture frame. PART TWO OF THE TWO IS A LIVE CLONE and is recorded as such
    /// (2026-09 refactor, F-54): the fourteen statements from the <c>NewPartyDisplayUI.PartyDisplay</c>
    /// fetch to the sixth <c>MixSubView</c> call, and <c>MixSubView</c> itself, are byte-identical in
    /// the two files. The reason given here was that the other method "is private to another lane's
    /// file"; <c>WorldUI/Sharpness/</c> and <c>WorldUI/Conversion/</c> are the same lane. PART ONE is
    /// genuinely different and must stay per-caller — see the table in the review — and the ORDER of
    /// the six calls is load-bearing, because the mix is a rolling <c>sig * 31</c> hash: change it and
    /// every window in both subsystems sees one phantom generation event. Part one is the ACTIVE SET
    /// beneath the conversion target by instance id, to
    /// <see cref="SignatureDepth"/> levels (see the ModBuild 243 block below for why it is two and not
    /// one), which works on any converted window. Part two is the game's own <c>NewPartyDisplayUI</c>
    /// answer — the <c>ActiveDisplay</c> enum plus which of the six sub-view roots are actually open —
    /// and it is what catches the battle-goal picker, whose root is NOT a direct child of the target
    /// and which part one alone would therefore miss entirely.</para>
    ///
    /// <para><b>ModBuild 243 — PART ONE WAS EXACTLY ONE LEVEL TOO SHALLOW, AND THE OPTIONS WINDOW
    /// PROVED IT.</b> User report, verbatim (2026-08-24): <i>"Die Reaktionszeit wenn man ein Sub-Menü
    /// z.B. im Optionsmenü öffnet von dem 'X' Button und dem Greifbalken ist zu gering. Man sieht immer
    /// wie es erst nach einer kurzen Zeit seinen Zustand verändert."</i></para>
    ///
    /// <para>Through ModBuild 242 this method hashed the active DIRECT CHILDREN of the target and
    /// nothing below them. The game's options window puts its eleven tab windows one level further
    /// down; the ModBuild 242 log prints the whole path on its own click trace (line 1638):
    /// <c>GloomhavenVR.Panel_Modal_UI Options Window_unified / UI Options Window_unified / Tabs /
    /// GloomhavenVR.OptionsTabWindow / Main Area / GloomhavenVR.SubTabs / Cat.2 / Background</c>. The
    /// conversion target is <c>UI Options Window_unified</c>; <c>Tabs</c> is the direct child and it
    /// NEVER toggles; the thing that toggles is <c>Tabs/&lt;tab&gt;</c>, at depth two —
    /// <c>UISubmenuGOWindow.Show()</c> is <c>gameObject.SetActive(true)</c> and
    /// <c>OnCompleteHidden</c> is <c>SetActive(false)</c>. So the one event the signature exists to
    /// catch was invisible to it, and the measured consequence is in the same log:</para>
    /// <list type="bullet">
    /// <item>FOUR options-tab presses (lines 1581, 2675, 2797 <c>'GloomhavenVR.OptionsTab'</c>, and
    /// 2288 <c>'Audio'</c> — a NATIVE tab, so this is not a mod-tab quirk) and the grab bar's
    /// generation stayed at <b>1</b> through every one of them, up to <c>sample 198 of that
    /// generation</c> at line 2295.</item>
    /// <item>The ONE generation event the options window ever saw (line 2961, generation 1 → 2) was
    /// fired by a <c>'Class Toggle'</c> click in a DIFFERENT window (line 2951): part two hashes the
    /// <c>NewPartyDisplayUI</c> singleton, which is global, so every floated window's generation
    /// moved on the same tick — <c>New Party display</c> to 3, <c>UI Map Esc Menu</c> to 3, the
    /// options window to 2 — and the options window's own reading on that line is
    /// <c>NOW OPEN: none, ActiveDisplay=CHARACTER_SELECTOR, 0 root(s)</c>. The one event it saw was
    /// not its own, and it changed nothing (the union stayed 399 px wide).</item>
    /// </list>
    /// <para>THE FIX IS DEPTH, NOT A NEW SIGNAL. <see cref="SignatureDepth"/> is 2 because 2 is what
    /// the measured hierarchy needs and every level costs a per-frame walk on every floated window.
    /// It is bounded twice — <see cref="SignatureMaxNodes"/> and the transient skip below — and the
    /// budget being reached is REPORTED (<c>truncated</c>) rather than silently changing
    /// the answer, because a signature that quietly stops covering a subtree looks exactly like a
    /// window that never changes ([[sentinel-overflow-and-silent-scans]]).</para>
    ///
    /// <para>WHY EXTRA GENERATION EVENTS ARE NOW CHEAP, which is the objection this change has to
    /// answer. Before ModBuild 242 a generation event was violent: it threw the monotone envelope
    /// away and re-seeded it from the next sample, and since hover widgets were IN the union, the
    /// re-seed could take a tooltip's extent as the whole window. Both halves of that are gone — the
    /// hover subtrees are out of the union AND out of this signature — so a spurious event now costs
    /// one re-measurement that produces the same rectangle, i.e. <c>moved == false</c> and nothing on
    /// the screen. The generation counter on the <c>GRAB BAR CLEARS THE INK</c> line is the falsifier:
    /// a window that climbs generations while its union never changes is this depth being too greedy,
    /// and the fix would be to name the churning child, never to go back to depth one.</para>
    ///
    /// <para>WHAT DEPTH TWO STILL DOES NOT CATCH, so that the next round does not assume it does: the
    /// VR options pane's own sub-category column (<c>GloomhavenVR.SubTabs/Cat.N</c>) rebuilds the rows
    /// under <c>GloomhavenVR.Content</c>, which sits at depth FIVE. Those presses produce no signature
    /// edge and are served by the confirm path in <c>GrabbableModal</c> instead (a change is noticed at
    /// the verify poll and confirmed one confirm-stride later), which is slower but bounded. Making
    /// them instant needs a notification from the code that rebuilds them, not more depth here.</para>
    ///
    /// <para><b>A RAISED MOUSEOVER IS NOT A NEW GENERATION (ModBuild 241).</b> This was the second
    /// half of "der Balken verändert sich hektisch", and it is the more violent half.
    /// <c>TooltipOnWindow.RaiseToWindowTop</c> finishes with
    /// <c>rect.SetParent(owner.Target, worldPositionStays: false)</c> — for the length of a hover the
    /// widget IS a direct child of the very transform this method hashes, and it goes away again on
    /// mouse-out. Every hover therefore fired a generation event, which throws the monotone envelope
    /// away, re-seeds it from the next sample and cancels any release run in progress; the ModBuild
    /// 239 log shows <c>New Party display</c> at <b>generation 51</b> in a single session against 320
    /// transient sightings. Skipping children that carry a family — the same
    /// <see cref="TransientFamilies"/> identity the ink walk uses, so the two can never disagree —
    /// makes a hover invisible to the signature, and the count is returned so the falsifier can say
    /// how many it skipped rather than leaving "no hovers happened" and "hovers were hidden"
    /// looking alike. The hashed CHILD COUNT had to move after the loop and count only what was
    /// hashed: leaving it at <c>target.childCount</c> would have let the hover back into the
    /// signature through the back door and undone the exemption while looking like it worked.</para>
    ///
    /// <para>Cost: a <c>childCount</c> loop of order ten at depth one and one of order ten under each
    /// of those, ONE six-way component probe per active DIRECT child only (a raised mouseover is
    /// always a direct child of the target — see <see cref="HashActiveSet"/> — so the second level
    /// needs no probe, which is what keeps a per-frame walk at three property reads per node), plus
    /// seven property reads on a singleton. Never throws; a
    /// partial mix is still STABLE (it fails in the same place every frame), so it stays a usable
    /// signature rather than a source of phantom re-captures.</para>
    /// </summary>
    /// <param name="transientChildren">How many active nodes were skipped because they are a raised
    /// mouseover. Reported, never acted on.</param>
    internal static int ActiveSetSignature(ConvertedPanel panel, out int transientChildren) =>
        ActiveSetSignature(panel, out transientChildren, out _, out _);

    /// <summary>
    /// <see cref="ActiveSetSignature(ConvertedPanel, out int)"/> with the two falsifier terms the
    /// grab bar's own log line carries. The two-argument form above stays because
    /// <c>LoadoutConfirmPark</c> calls it and that file belongs to another lane; both forms are the
    /// SAME walk, so the two call sites can never see different signatures.
    /// </summary>
    /// <param name="nodesHashed">Active nodes that actually went into the hash. Zero on a window
    /// whose target is gone; a value that never changes while the user is pressing tabs is this
    /// walk being too shallow again.</param>
    /// <param name="truncated">The <see cref="SignatureMaxNodes"/> budget stopped the walk, so the
    /// answer covers only part of the tree. Reported so a blind signature cannot look like a still
    /// window.</param>
    internal static int ActiveSetSignature(ConvertedPanel panel, out int transientChildren,
        out int nodesHashed, out bool truncated)
    {
        Transform? target = panel.Target;
        // ---- THE ONE-FRAME MEMO (ModBuild 243) ---------------------------------------------------
        // TWO callers now ask the same question about the same window in the same frame:
        // GrabbableModal.ServiceInkCapture from the follow tick, and CanvasConversion's TickHitRect,
        // which uses the edge to move the laser rect on the same frame as the brass bar. Without
        // this the walk would run twice per window per frame — a small number that this project has
        // twice discovered is not small ([[one-line-owned-the-frame]]). Keyed by the TARGET's
        // instance id (a panel's identity for this purpose) and by Time.frameCount, so a stale entry
        // can never be served and the two callers can never see different answers.
        int key = target != null ? target.GetInstanceID() : 0;
        int frame = Time.frameCount;
        if (target != null)
        {
            for (int i = 0; i < SigMemoSlots; i++)
            {
                if (SigMemoKey[i] != key || SigMemoFrame[i] != frame)
                    continue;
                transientChildren = SigMemoTransient[i];
                nodesHashed = SigMemoNodes[i];
                truncated = SigMemoTruncated[i];
                return SigMemoValue[i];
            }
        }

        int value = ComputeActiveSetSignature(target, out transientChildren, out nodesHashed,
            out truncated);
        if (target != null)
        {
            int slot = s_sigMemoNext;
            s_sigMemoNext = (slot + 1) % SigMemoSlots;
            SigMemoKey[slot] = key;
            SigMemoFrame[slot] = frame;
            SigMemoValue[slot] = value;
            SigMemoTransient[slot] = transientChildren;
            SigMemoNodes[slot] = nodesHashed;
            SigMemoTruncated[slot] = truncated;
        }
        return value;
    }

    /// <summary>
    /// Slots in the one-frame memo. EIGHT because that is more floated windows than the map room's
    /// arc allocator will seat, so every open window keeps its entry for the whole frame — a single
    /// slot would have been useless here, since the two callers run in DIFFERENT PHASES (the follow
    /// tick in Update, the hit rect in LateUpdate) and walk the whole panel list in between.
    ///
    /// <para>FIXED INT ARRAYS AND NOT A DICTIONARY, deliberately: nothing is allocated after class
    /// load, nothing keyed on a Unity object can go fake-null and leak, and a full ring simply evicts
    /// its oldest entry into a re-walk. The memo can never change an answer — the worst a miss costs
    /// is the walk that would have happened anyway.</para>
    /// </summary>
    private const int SigMemoSlots = 8;
    private static readonly int[] SigMemoKey = new int[SigMemoSlots];
    private static readonly int[] SigMemoFrame = CreateFrameSlots();
    private static readonly int[] SigMemoValue = new int[SigMemoSlots];
    private static readonly int[] SigMemoTransient = new int[SigMemoSlots];
    private static readonly int[] SigMemoNodes = new int[SigMemoSlots];
    private static readonly bool[] SigMemoTruncated = new bool[SigMemoSlots];
    private static int s_sigMemoNext;

    /// <summary>Frame slots start at -1 so slot 0 cannot serve a hit on frame 0 for instance id 0.</summary>
    private static int[] CreateFrameSlots()
    {
        var frames = new int[SigMemoSlots];
        for (int i = 0; i < frames.Length; i++)
            frames[i] = -1;
        return frames;
    }

    private static int ComputeActiveSetSignature(Transform? target, out int transientChildren,
        out int nodesHashed, out bool truncated)
    {
        transientChildren = 0;
        nodesHashed = 0;
        truncated = false;
        int sig = 17;
        if (target != null)
            sig = HashActiveSet(target, sig, SignatureDepth, ref transientChildren, ref nodesHashed,
                ref truncated);

        NewPartyDisplayUI? display;
        try
        {
            display = NewPartyDisplayUI.PartyDisplay;
        }
        catch (System.Exception)
        {
            return sig;
        }
        if (display == null || target == null)
            return sig;
        try
        {
            sig = sig * 31 + (int)display.ActiveDisplay;
            sig = MixSubView(sig, display.CharacterSelector, target);
            sig = MixSubView(sig, display.PerkManager, target);
            sig = MixSubView(sig, display.AbilityCardsDisplay, target);
            sig = MixSubView(sig, display.EnhancementCardsDisplay, target);
            sig = MixSubView(sig, display.ItemInventoryDisplay, target);
            sig = MixSubView(sig, display.BattleGoalWindow, target);
        }
        catch (System.Exception)
        {
        }
        return sig;
    }

    /// <summary>
    /// One level of <see cref="ActiveSetSignature"/>'s part one, recursing while
    /// <paramref name="depth"/> is left. The order of the hash is the sibling order, which Unity
    /// keeps stable, so the same tree hashes the same every frame.
    ///
    /// <para>THE HASHED COUNT IS PER LEVEL AND IT COUNTS ONLY WHAT WAS HASHED — the ModBuild 241
    /// rule, one level deeper. Mixing <c>t.childCount</c> instead would put a raised mouseover back
    /// into the signature through the back door and undo the exemption while looking like it worked.
    /// The count is mixed AFTER the children so that "two children swapped for one" cannot collide
    /// with "one child" at the same instance ids.</para>
    /// </summary>
    private static int HashActiveSet(Transform t, int sig, int depth, ref int transientChildren,
        ref int nodesHashed, ref bool truncated)
    {
        bool topLevel = depth == SignatureDepth;
        int n = t.childCount;
        int counted = 0;
        for (int i = 0; i < n; i++)
        {
            Transform c = t.GetChild(i);
            if (c == null || !c.gameObject.activeSelf)
                continue;
            // THE TRANSIENT PROBE IS DEPTH-ONE ONLY, and that is a fact about the game rather than a
            // saving. `TooltipOnWindow.RaiseToWindowTop` ends with
            // `rect.SetParent(owner.Target, worldPositionStays: false)` — a raised mouseover is
            // ALWAYS a direct child of the conversion target, never deeper — so probing the second
            // level would find nothing and cost six TryGetComponent calls per node per frame on
            // every floated window. A hover subtree is still skipped WHOLE (the `continue` below
            // never descends into it), which is what makes the exemption reach the widget's children
            // too.
            if (topLevel && TransientFamilies.Self(c) != 0)
            {
                transientChildren++;
                continue;
            }
            if (nodesHashed >= SignatureMaxNodes)
            {
                truncated = true;
                break;
            }
            counted++;
            nodesHashed++;
            sig = sig * 31 + c.GetInstanceID();
            if (depth > 1)
                sig = HashActiveSet(c, sig, depth - 1, ref transientChildren, ref nodesHashed,
                    ref truncated);
        }
        return sig * 31 + counted;
    }

    private static int MixSubView(int sig, Component? view, Transform target)
    {
        if (view == null)
            return sig * 31;
        Transform t = view.transform;
        bool open = t.gameObject.activeInHierarchy && IsUnder(t, target);
        return sig * 31 + (open ? t.GetInstanceID() : 0);
    }

    private static bool IsUnder(Transform t, Transform root)
    {
        Transform? p = t;
        while (p != null)
        {
            if (ReferenceEquals(p, root))
                return true;
            p = p.parent;
        }
        return false;
    }
}
