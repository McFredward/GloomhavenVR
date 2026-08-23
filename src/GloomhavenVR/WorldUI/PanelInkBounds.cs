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
/// single most important line in this file: the mod's own chrome (<c>GloomhavenVR.ModalCloseX</c>,
/// which <c>ModalCloseButton.Build</c> parents to the HOST rect at anchor (1,1), and the supersample
/// quad, parented to the host as well) rides the FRAME's corners on purpose. Unioning the mod's own
/// frame-anchored furniture would re-derive the frame and this class would answer its own question
/// with the number it was written to replace.</para>
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
/// restated here BY VALUE from <c>CanvasConversion.FitMinAlpha</c>, the same borrowing (and the same
/// standing risk of drift) as the plate fractions above, and the graphics it drops are COUNTED into
/// <see cref="Ink.Faint"/> so the falsifier can say whether this term was the whole story.</para>
///
/// <para>Three further exclusions, each of which was a way to measure the frame again:
/// <list type="bullet">
/// <item><b>FULL-FRAME BACKDROP PLATES.</b> A window's own root <c>Image</c> fills its frame; so does
/// the perks view's 1620x1080 <c>Blur</c>. The test is the fit's, taken BY VALUE (this file may not
/// edit <c>CanvasConversion.3.Fit.cs</c>): width &gt;= 0.80 and height &gt;= 0.95 of the host rect —
/// see <c>CanvasConversion.FixedFitPlateWidthFraction</c> / <c>…HeightFraction</c>, the same
/// borrowing <c>EnchantressComposite</c> already does.</item>
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
/// <para><b>THIS CLASS NEVER WRITES GAME STATE.</b> It reads transforms and components and returns a
/// rectangle. No Show/Hide/SetActive/CanvasGroup, no layout rebuild, no allocation per call beyond the
/// two static scratch buffers below.</para>
/// </summary>
internal static class PanelInkBounds
{
    /// <summary>Full-frame plate test, BY VALUE from <c>CanvasConversion.FixedFitPlateWidthFraction</c>
    /// and <c>…FixedFitPlateHeightFraction</c> (0.80 / 0.95). Restated rather than referenced because
    /// those are private to another lane's file; if that pair ever moves, this pair must follow and the
    /// falsifier's "plates excluded" count is what would show the drift.</summary>
    private const float PlateWidthFraction = 0.80f;
    private const float PlateHeightFraction = 0.95f;

    /// <summary>EFFECTIVE-ALPHA FLOOR, BY VALUE from <c>CanvasConversion.FitMinAlpha</c> (0.05) — the
    /// same borrowing, for the same reason, as <see cref="PlateWidthFraction"/>. This is the term whose
    /// absence put the grab bar 390 px under an empty frame; see the class comment for both instruments'
    /// numbers. Graphics it rejects are counted into <see cref="Ink.Faint"/>, never silently dropped.</summary>
    private const float FaintAlphaFloor = 0.05f;

    /// <summary>Node budget for one walk. A converted window is order hundreds of transforms; this is
    /// a runaway guard, not a working limit, and <see cref="Ink.Truncated"/> reports if it ever bites
    /// rather than letting a silently short union move the bar.</summary>
    private const int MaxNodes = 6000;

    /// <summary>The measured ink of one window, in the host RectTransform's own local uGUI pixels —
    /// the SAME space <c>ConvertedPanel.HostRect.rect</c> is expressed in, so the two are directly
    /// comparable and the log can print both.</summary>
    internal struct Ink
    {
        internal bool Valid;
        internal Rect Rect;
        internal int Graphics;
        internal int Plates;
        internal int EmptyText;
        internal int ModChrome;
        /// <summary>Drawn-but-invisible: effective alpha (own colour x inherited CanvasGroup alpha)
        /// below <see cref="FaintAlphaFloor"/>. A non-zero count on a window whose union used to reach
        /// far outside its frame is this term doing the work it was added for.</summary>
        internal int Faint;
        internal bool Truncated;
        /// <summary>The graphic that set the union's BOTTOM edge — the one the bar has to clear, and
        /// the only name worth carrying into the report.</summary>
        internal string BottomName;
    }

    private struct ClipFrame
    {
        internal readonly Transform Transform;
        internal readonly Rect Clip;

        internal ClipFrame(Transform transform, Rect clip)
        {
            Transform = transform;
            Clip = clip;
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
    internal static bool TryMeasure(ConvertedPanel panel, out Ink ink)
    {
        ink = default;
        ink.BottomName = string.Empty;
        try
        {
            return MeasureCore(panel, ref ink);
        }
        catch (System.Exception)
        {
            Stack.Clear();
            ink.Valid = false;
            return false;
        }
    }

    private static bool MeasureCore(ConvertedPanel panel, ref Ink ink)
    {
        RectTransform? host = panel.HostRect;
        Transform? target = panel.Target;
        if (host == null || target == null || !target.gameObject.activeInHierarchy)
            return false;

        Rect hostRect = host.rect;
        float plateW = hostRect.width * PlateWidthFraction;
        float plateH = hostRect.height * PlateHeightFraction;
        bool plateTestUsable = hostRect.width > 1f && hostRect.height > 1f;

        float minX = 0f, minY = 0f, maxX = 0f, maxY = 0f;
        int nodes = 0;

        Stack.Clear();
        Stack.Add(new ClipFrame(target, Unbounded));
        while (Stack.Count > 0)
        {
            int last = Stack.Count - 1;
            ClipFrame node = Stack[last];
            Stack.RemoveAt(last);
            Transform t = node.Transform;
            if (t == null || !t.gameObject.activeSelf)
                continue;
            if (++nodes > MaxNodes)
            {
                ink.Truncated = true;
                break;
            }

            bool isRoot = ReferenceEquals(t, target);
            // The mod's own furniture, wherever it was parented. Named by the convention every
            // mod-created GameObject in this assembly follows. Counted, not silently dropped.
            if (!isRoot && t.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal))
            {
                ink.ModChrome++;
                continue;
            }
            // Foreign render subtree: not uGUI ink, drawn by another camera at its own world pose.
            if (!isRoot && (t.GetComponent<Renderer>() != null || t.GetComponent<Camera>() != null))
                continue;

            Rect clip = node.Clip;
            var rt = t as RectTransform;
            if (rt != null)
            {
                if (TryHostLocalBounds(host, rt, out Rect bounds))
                {
                    if (ClipsChildren(t) && !Intersect(clip, bounds, out clip))
                        continue; // fully clipped away: neither this nor anything under it draws

                    var graphic = t.GetComponent<Graphic>();
                    if (Draws(graphic) && Intersect(clip, bounds, out Rect visible)
                        && visible.width > 0f && visible.height > 0f)
                    {
                        // FAINT FIRST, deliberately: a graphic at effective alpha 0 draws nothing at
                        // all, so saying "it is a full-frame plate" about it would report the weaker
                        // of two true statements and hide the term the next reader needs.
                        if (IsFaint(graphic))
                        {
                            ink.Faint++;
                        }
                        else if (plateTestUsable && visible.width >= plateW && visible.height >= plateH)
                        {
                            ink.Plates++;
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
                Stack.Add(new ClipFrame(t.GetChild(i), clip));
        }
        Stack.Clear();

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
    /// derives for the capture frame, restated here because that method is private to another lane's
    /// file. Part one is the ACTIVE DIRECT CHILDREN of the conversion target by instance id, which
    /// works on any converted window. Part two is the game's own <c>NewPartyDisplayUI</c> answer — the
    /// <c>ActiveDisplay</c> enum plus which of the six sub-view roots are actually open — and it is
    /// what catches the battle-goal picker, whose root is NOT a direct child of the target and which
    /// part one alone would therefore miss entirely.</para>
    ///
    /// <para>Cost: one <c>childCount</c> loop of order ten plus seven property reads on a singleton.
    /// Never throws; a partial mix is still STABLE (it fails in the same place every frame), so it
    /// stays a usable signature rather than a source of phantom re-captures.</para>
    /// </summary>
    internal static int ActiveSetSignature(ConvertedPanel panel)
    {
        int sig = 17;
        Transform? target = panel.Target;
        if (target != null)
        {
            int n = target.childCount;
            sig = sig * 31 + n;
            for (int i = 0; i < n; i++)
            {
                Transform c = target.GetChild(i);
                if (c != null && c.gameObject.activeSelf)
                    sig = sig * 31 + c.GetInstanceID();
            }
        }

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
