using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Core;

/// <summary>
/// SIZE A CONVERTED uGUI LAYOUT ROOT FROM ITS OWN CONTENT, VERTICALLY.
///
/// <para><b>THE DEFECT THIS EXISTS FOR</b> (user report 2026-09-06, item 8, "In dem getesteten
/// Szenario hat sich Text der Punkte im dem Questziel überlagert", screenshot
/// <c>.planning/debug/überschneidender_text.jpg</c>): two scenario objectives drawn on top of each
/// other — the third line of "Ihr verliert, wenn in der materiellen Ebene keine lebendigen Söldner
/// mehr sind." sits UNDER the plate of "Tötet alle Dämonen, um den Riss zu schließen."</para>
///
/// <para><b>THE MECHANISM, READ OFF THE SHIPPED INSTRUMENT AND NOT INFERRED.</b> The
/// <c>OBJECTIVES TREE</c> dump of ModBuild 457 says it in three numbers, identically on BOTH
/// clients (host Player.log:58846, peer Player.log:51639):</para>
/// <code>
///   Mission Objective Container  300x100   ← sizeDeltaX 300 forced by the mod, HEIGHT still 100
///     UI Quest Type              300x22    ← its LayoutElement asks for 36
///     Container                  300x74    ← VLG; its two rows want 58 + 40 = 98
///       UI Mission Objective     289x58    [CSF h=Unconstrained v=PreferredSize]
///       UI Mission Objective     289x40    [CSF h=Unconstrained v=PreferredSize]
/// </code>
/// <para>100 px is not a measurement. It is <c>CanvasConversion</c>'s degenerate-rect placeholder
/// (<c>size = max(size, 100)</c>): the container's authored rect is (0,0) because in the game's 2D
/// HUD its size comes from a parent that no longer exists after re-parenting under the world-space
/// host. <c>ObjectivesSurface.ApplyContentWidth</c> replaces the horizontal half of that
/// placeholder with a real budget every tick; the vertical half was never replaced by anything, so
/// the panel is permanently 100 authored px tall no matter what it contains.</para>
///
/// <para><b>WHY A SHORTFALL BECOMES AN OVERLAP RATHER THAN A CLIP.</b> A
/// <c>VerticalLayoutGroup</c> that is shorter than its children's total preferred size does not
/// overflow — it SHRINKS: <c>LayoutGroup.SetChildrenAlongAxis</c> lerps every child from its
/// preferred size back toward its minimum and advances the cursor by the SHRUNK size. Each
/// objective row then carries <c>ContentSizeFitter(vertical = PreferredSize)</c>, which is an
/// <c>ILayoutSelfController</c> and runs AFTER the group: it puts the row's own height straight
/// back to 58 / 40 px around a cursor position that was computed for the squeezed value. Row two
/// is therefore seated 24 px too high and draws its plate over row one's last line. The rows never
/// disagree about their own size; they disagree about where the next one starts.</para>
///
/// <para><b>SO IT IS NOT A GERMAN BUG, AND IT IS NOT A WIDTH BUG.</b> German is only what pushed
/// this scenario's first objective from two lines to three. The constraint that broke is ROWS: the
/// same panel is correct in the earlier dump (Player.log:5696 — one 22 px row, header at its full
/// 36 px, total 96 &lt; 100) and breaks the moment the content asks for more than the placeholder,
/// in any language. Widening the column reduces the line count and can therefore HIDE it, which is
/// exactly why a width check passes over it.</para>
///
/// <para><b>THE FIX.</b> Give the root the height its own layout asks for. The conversion collapsed
/// its anchors to the host centre on BOTH axes (<c>CanvasConversion.1.Core</c>: <c>anchorMin =
/// anchorMax = (0.5, 0.5)</c>), so its height IS its <c>sizeDelta.y</c> and nothing else drives it —
/// the identical argument that makes the width write stick. With the room it asks for, the group's
/// lerp saturates at the preferred size, the row fitters agree with the cursor that placed them,
/// and a two-line objective pushes the next one DOWN. Nothing below the root is touched: every
/// descendant height stays derived by the game's own layout groups.</para>
///
/// <para><b>WHY NOT A ContentSizeFitter ON THE ROOT.</b> It would do the same job, but it is a
/// COMPONENT added to game-owned UI that has to be removed again on release, and
/// <c>RemoteWidgetMirror</c> has already rejected it for the mirrored copy in writing ("it would
/// put a component on the clone that the OWNER's panel does not have, which is the opposite of a
/// mirror"). A <c>sizeDelta.y</c> write is the same value through the same capture/restore
/// machinery the width already uses, and it is available verbatim on both sides.</para>
///
/// <para><b>MULTIPLAYER.</b> This is a pure function of the content at the forced column, and the
/// mirror forces the BOARD OWNER's column onto its clone, so both sides evaluate the same
/// expression over the same rows and land on the same height. No wire field, no client-local term:
/// the height cannot be a second opinion because it is not an opinion. Both call sites are here so
/// that the owner's panel and the mirrored one cannot drift the day one of them is retuned.</para>
/// </summary>
internal static class LayoutContentHeight
{
    /// <summary>Dead band for the height write, matching the width write's. Below this a re-assert
    /// is two float compares and no layout work at all.</summary>
    public const float DeadBandPx = 0.5f;

    /// <summary>
    /// Write <paramref name="root"/>'s height from its own preferred height. Returns true only when
    /// it actually wrote, so the caller owns the rebuild and the change-gated log.
    ///
    /// <para>The preferred height is whatever the LAST layout pass computed — a
    /// <c>VerticalLayoutGroup</c> caches it in <c>CalculateLayoutInputVertical</c>. That is why
    /// callers rebuild the column FIRST and read this SECOND. It cannot feed back on itself: a
    /// child's preferred height is a function of its WIDTH (fixed here) and its text, never of the
    /// root's height, so the write converges in one step and re-asserting is idempotent.</para>
    /// </summary>
    public static bool Apply(RectTransform? root, out float beforePx, out float afterPx)
    {
        beforePx = 0f;
        afterPx = 0f;
        if (root == null)
            return false;

        beforePx = root.sizeDelta.y;
        afterPx = beforePx;

        float want = LayoutUtility.GetPreferredHeight(root);
        // A zero/NaN measure is a layout that has not run yet (or a panel with no rows at all).
        // Writing it would collapse the host rect to nothing, which is a worse picture than the one
        // being fixed — leave the placeholder standing and try again next tick.
        if (float.IsNaN(want) || want < 1f || Mathf.Abs(beforePx - want) <= DeadBandPx)
            return false;

        root.sizeDelta = new Vector2(root.sizeDelta.x, want);
        afterPx = want;
        return true;
    }

    /// <summary>
    /// THE ROW GUARD. The worst vertical shortfall, in authored px, over <paramref name="root"/> and
    /// every <c>VerticalLayoutGroup</c> beneath it: how much more room the children ask for than the
    /// group has to give them. <c>&gt; 0</c> is the squeeze that becomes an overlap.
    ///
    /// <para>Only VERTICAL groups are measured, on purpose. A horizontal group that is too short
    /// clips its content; it cannot re-seat the next sibling on top of the previous one, which is
    /// the defect being watched for. Measuring both would report a number that is true and does not
    /// answer the question.</para>
    ///
    /// <para><b>CALL IT ON THE WRITE PATH ONLY.</b> <c>GetComponentsInChildren</c> allocates, and a
    /// per-tick allocation over a subtree is the exact shape of defect this project has shipped
    /// three times (see <c>ObjectivesSurface.ObjectivesStyleInterval</c>, which exists for the same
    /// reason). The caller's cheap test is <see cref="Apply"/> itself — one pooled
    /// <c>LayoutUtility</c> read and one float compare — and the root's OWN shortfall, the number
    /// that actually names the defect, falls out of that comparison for free. This walk is the
    /// AFTER reading: it answers "is anything still squeezed once the root has the room it asked
    /// for?", which no arithmetic on the root alone can answer.</para>
    /// </summary>
    /// <param name="worst">Name of the group carrying that shortfall, for the log line.</param>
    public static float MeasureRowOverflowPx(RectTransform? root, out string worst)
    {
        worst = string.Empty;
        if (root == null)
            return 0f;

        float shortfall = 0f;
        foreach (VerticalLayoutGroup group in root.GetComponentsInChildren<VerticalLayoutGroup>(includeInactive: true))
        {
            var rect = group.transform as RectTransform;
            if (rect == null)
                continue;
            float want = LayoutUtility.GetPreferredHeight(rect);
            if (float.IsNaN(want))
                continue;
            float miss = want - rect.rect.height;
            if (miss > shortfall)
            {
                shortfall = miss;
                worst = rect.name;
            }
        }
        return shortfall;
    }
}
